using System.Reflection;

namespace DatabaseMigrator;

/// <summary>The version of this build, as the release workflow stamps it (-p:Version=X.Y.Z; the SDK appends +sha).</summary>
public static class AppVersion
{
    /// <summary>"1.0.77+5bb2f8d": the informational version with the commit cut to seven characters.</summary>
    public static string Format(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
            return "unknown";
        int plus = informationalVersion.IndexOf('+');
        if (plus < 0)
            return informationalVersion;
        string commit = informationalVersion[(plus + 1)..];
        return commit.Length > 7 ? $"{informationalVersion[..plus]}+{commit[..7]}" : informationalVersion;
    }

    public static string Current { get; } =
        Format(typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
}
