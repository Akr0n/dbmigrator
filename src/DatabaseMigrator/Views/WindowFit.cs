using System;
using Avalonia;

namespace DatabaseMigrator.Views;

/// <summary>Where a window of a given size goes so that all of it, title bar included, is inside a screen's work area.</summary>
public static class WindowFit
{
    /// <summary>The size to give the client area and the position of the whole frame, or null if the window already fits.</summary>
    /// <param name="workArea">The screen's work area (without the taskbar), in physical pixels.</param>
    /// <param name="scaling">The screen's display scaling (1.25 = 125%).</param>
    /// <param name="requestedClient">The client size the window asks for, in device-independent units.</param>
    /// <param name="chrome">Title bar and borders: frame size minus client size, in device-independent units.</param>
    public static (Size Client, PixelPoint Position)? Compute(PixelRect workArea, double scaling, Size requestedClient, Size chrome)
    {
        // The units are not pixels: 1400x900 units are 1750x1125 pixels at 125%, taller than a 1080-pixel screen once the taskbar
        // is out of it. Window.WindowStartupLocation=CenterScreen then centres a frame that is too tall and the title bar ends
        // up above the top edge.
        double availableWidth = Math.Max(1, workArea.Width / scaling - chrome.Width);
        double availableHeight = Math.Max(1, workArea.Height / scaling - chrome.Height);
        if (requestedClient.Width <= availableWidth && requestedClient.Height <= availableHeight)
            return null;

        var client = new Size(Math.Min(requestedClient.Width, availableWidth), Math.Min(requestedClient.Height, availableHeight));
        int frameWidth = (int)Math.Ceiling((client.Width + chrome.Width) * scaling);
        int frameHeight = (int)Math.Ceiling((client.Height + chrome.Height) * scaling);
        return (client, new PixelPoint(
            workArea.X + Math.Max(0, (workArea.Width - frameWidth) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - frameHeight) / 2)));
    }
}
