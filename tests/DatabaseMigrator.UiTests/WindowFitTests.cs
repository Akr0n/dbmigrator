using Avalonia;
using DatabaseMigrator.Views;

namespace DatabaseMigrator.UiTests;

/// <summary>
/// The main window asks for 1400x900 device-independent units and was centred as it was: on a 1920x1080 screen at 125% (work area
/// 1920x1020 pixels, as on the machine where it was reported) that is taller than the screen, so the title bar, with the close,
/// maximise and minimise buttons, ended up above the top edge and the bottom under the taskbar.
/// </summary>
public class WindowFitTests
{
    private static readonly Size Requested = new(1400, 900);
    private static readonly Size Chrome = new(14.4, 37.6); // 18 x 47 pixels at 125%, measured on that window

    private static void AssertFrameInside(PixelRect area, double scaling, Size chrome, (Size Client, PixelPoint Position) fit)
    {
        int width = (int)Math.Ceiling((fit.Client.Width + chrome.Width) * scaling);
        int height = (int)Math.Ceiling((fit.Client.Height + chrome.Height) * scaling);
        Assert.True(fit.Position.X >= area.X, $"left edge {fit.Position.X} is left of the work area ({area.X})");
        Assert.True(fit.Position.Y >= area.Y, $"title bar at {fit.Position.Y} is above the work area ({area.Y})");
        Assert.True(fit.Position.X + width <= area.Right, $"right edge {fit.Position.X + width} is past the work area ({area.Right})");
        Assert.True(fit.Position.Y + height <= area.Bottom, $"bottom edge {fit.Position.Y + height} is past the work area ({area.Bottom})");
    }

    [Fact]
    public void TheReportedScreen_ShrinksTheHeightSoTheTitleBarIsReachable()
    {
        var area = new PixelRect(0, 0, 1920, 1020);

        var fit = WindowFit.Compute(area, 1.25, Requested, Chrome);

        Assert.NotNull(fit);
        AssertFrameInside(area, 1.25, Chrome, fit.Value);
        Assert.Equal(1400, fit.Value.Client.Width); // the width fitted already, only the height gives way
        Assert.Equal(778.4, fit.Value.Client.Height, 6);
        // What Win32 measured on that window once fitted: 76,0 -> 1844,1020 (1768x1020), centred left and right, flush top and bottom.
        Assert.Equal(new PixelPoint(76, 0), fit.Value.Position);
    }

    [Fact]
    public void WhenOnlyTheWidthDoesNotFit_OnlyTheWidthShrinks_AndTheWindowIsCentredVertically()
    {
        var area = new PixelRect(0, 0, 1280, 984); // a 1280x1024 screen at 100% with a taskbar: 1280 wide is less than 1400 units

        var fit = WindowFit.Compute(area, 1.0, Requested, Chrome);

        Assert.NotNull(fit);
        Assert.Equal(1265.6, fit.Value.Client.Width, 6);
        Assert.Equal(900, fit.Value.Client.Height);
        Assert.Equal(new PixelPoint(0, 23), fit.Value.Position); // (984 - 938) / 2
    }

    [Fact]
    public void AWindowThatFits_IsLeftWhereCenterScreenPutIt()
    {
        Assert.Null(WindowFit.Compute(new PixelRect(0, 0, 1920, 1040), 1.0, Requested, Chrome));
    }

    [Fact]
    public void WhenBothSidesAreTooBig_BothShrink()
    {
        var area = new PixelRect(0, 0, 1920, 1020); // 1280 x 680 units at 150%

        var fit = WindowFit.Compute(area, 1.5, Requested, Chrome);

        Assert.NotNull(fit);
        AssertFrameInside(area, 1.5, Chrome, fit.Value);
        Assert.True(fit.Value.Client.Width < 1400);
        Assert.True(fit.Value.Client.Height < 900);
    }

    [Fact]
    public void OnASecondMonitor_ThePositionStaysInThatMonitorsWorkArea()
    {
        var area = new PixelRect(1920, 0, 1920, 1020); // to the right of the first screen

        var fit = WindowFit.Compute(area, 1.25, Requested, Chrome);

        Assert.NotNull(fit);
        AssertFrameInside(area, 1.25, Chrome, fit.Value);
        Assert.Equal(new PixelPoint(1920 + 76, 0), fit.Value.Position); // the same place on that screen as on the first
    }

    [Fact]
    public void AMonitorAboveTheFirst_KeepsItsNegativeOrigin()
    {
        var area = new PixelRect(0, -1080, 1920, 1020);

        var fit = WindowFit.Compute(area, 1.25, Requested, Chrome);

        Assert.NotNull(fit);
        AssertFrameInside(area, 1.25, Chrome, fit.Value);
    }

    [Theory]
    [InlineData(1366, 728, 1.0)]  // a small laptop
    [InlineData(1920, 1020, 1.75)]
    [InlineData(2560, 1380, 1.5)]
    [InlineData(3840, 2060, 2.0)]
    [InlineData(1280, 680, 1.25)]
    public void WhateverTheScreen_TheWholeFrameEndsUpInsideTheWorkArea(int width, int height, double scaling)
    {
        var area = new PixelRect(0, 0, width, height);

        var fit = WindowFit.Compute(area, scaling, Requested, Chrome);

        if (fit is not null)
            AssertFrameInside(area, scaling, Chrome, fit.Value);
        else // left alone only if it fits as it is
            Assert.True((1400 + Chrome.Width) * scaling <= width && (900 + Chrome.Height) * scaling <= height);
    }

    [Fact]
    public void ATinyWorkArea_NeverGivesANegativeSize_AndKeepsTheTitleBarOnScreen()
    {
        var area = new PixelRect(100, 50, 40, 30); // it cannot fit at all: the title bar must still not end up above or left of it

        var fit = WindowFit.Compute(area, 2.0, Requested, Chrome);

        Assert.NotNull(fit);
        Assert.True(fit.Value.Client.Width > 0 && fit.Value.Client.Height > 0);
        Assert.Equal(area.Position, fit.Value.Position);
    }

    [Fact]
    public void AWorkAreaNarrowerThanTheBorders_NeverGivesANegativeWidth()
    {
        var fit = WindowFit.Compute(new PixelRect(0, 0, 20, 2000), 2.0, Requested, Chrome); // 20 px wide, 10 units: less than the 14.4 of borders

        Assert.NotNull(fit);
        Assert.True(fit.Value.Client.Width > 0);
    }
}
