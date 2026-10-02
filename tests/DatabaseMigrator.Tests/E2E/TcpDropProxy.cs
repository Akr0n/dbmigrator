using System.Net;
using System.Net.Sockets;

namespace DatabaseMigrator.Tests.E2E;

/// <summary>
/// A TCP relay in front of a database server that cuts a connection the way a firewall or a network fault does: once the server
/// has sent <c>dropAfterServerBytes</c> bytes on one connection, both sides are closed with a reset ("an existing connection was
/// forcibly closed by the remote host"). It does so for the first <c>maxDrops</c> such connections and relays the others untouched,
/// or, with <c>refuseAfterDrop</c>, resets every connection opened after the first cut, as a network that is still down would.
/// The traffic is passed on byte for byte, so encryption between the client and the server works through it.
/// </summary>
internal sealed class TcpDropProxy : IDisposable
{
    private readonly TcpListener _listener;
    private readonly string _upstreamHost;
    private readonly int _upstreamPort;
    private readonly long _dropAfterServerBytes;
    private readonly int _maxDrops;
    private readonly bool _refuseAfterDrop;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<TcpClient> _live = []; // client sides of the relays at work: the network going down takes them all
    private int _drops;

    public TcpDropProxy(string upstreamHost, int upstreamPort, long dropAfterServerBytes, int maxDrops = 1, bool refuseAfterDrop = false)
    {
        _upstreamHost = upstreamHost;
        _upstreamPort = upstreamPort;
        _dropAfterServerBytes = dropAfterServerBytes;
        _maxDrops = maxDrops;
        _refuseAfterDrop = refuseAfterDrop;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(AcceptLoopAsync);
    }

    /// <summary>The port to connect to instead of the server's.</summary>
    public int Port { get; }

    /// <summary>How many connections were cut.</summary>
    public int Drops => Volatile.Read(ref _drops);

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch
            {
                return; // stopped
            }

            _ = Task.Run(() => RelayAsync(client));
        }
    }

    private async Task RelayAsync(TcpClient client)
    {
        using var clientSide = client;
        using var serverSide = new TcpClient();
        try
        {
            if (_refuseAfterDrop && Drops > 0)
            {
                Reset(client);
                return;
            }

            await serverSide.ConnectAsync(_upstreamHost, _upstreamPort, _stop.Token);
            lock (_live)
                _live.Add(client);
            var toServer = PumpAsync(client, serverSide, countsTowardsDrop: false);
            var toClient = PumpAsync(serverSide, client, countsTowardsDrop: true);
            await Task.WhenAny(toServer, toClient);
        }
        catch
        {
            // A relay that ends in an error is just a connection that ended.
        }
        finally
        {
            lock (_live)
                _live.Remove(client);
            serverSide.Close();
            client.Close();
        }
    }

    private async Task PumpAsync(TcpClient from, TcpClient to, bool countsTowardsDrop)
    {
        var buffer = new byte[8192];
        long relayed = 0;
        var source = from.GetStream();
        var destination = to.GetStream();
        while (true)
        {
            int read = await source.ReadAsync(buffer, _stop.Token);
            if (read == 0)
                return;

            if (countsTowardsDrop && relayed >= _dropAfterServerBytes && TryClaimDrop())
            {
                if (_refuseAfterDrop)
                    ResetAllLive(); // a connection the pool kept idle would otherwise carry on as if the network were up
                Reset(from);
                Reset(to);
                return;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), _stop.Token);
            relayed += read;
        }
    }

    private bool TryClaimDrop()
    {
        while (true)
        {
            int current = Volatile.Read(ref _drops);
            if (current >= _maxDrops)
                return false;
            if (Interlocked.CompareExchange(ref _drops, current + 1, current) == current)
                return true;
        }
    }

    private void ResetAllLive()
    {
        TcpClient[] live;
        lock (_live)
            live = _live.ToArray();
        foreach (var client in live)
            Reset(client);
    }

    /// <summary>Closes a socket abortively: the peer sees a reset, not an orderly end of the stream.</summary>
    private static void Reset(TcpClient client)
    {
        try
        {
            client.Client.LingerState = new LingerOption(true, 0);
            client.Close();
        }
        catch
        {
            // Already closed.
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _stop.Dispose();
    }
}
