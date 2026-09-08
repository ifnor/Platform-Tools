using PlatformTools.App.Services;

namespace PlatformTools.Tests;

public sealed class WindowLayoutPolicyTests
{
    [Theory]
    [InlineData(1366, 728, 1)]
    [InlineData(1920, 1040, 1.5)]
    [InlineData(1920, 1040, 2)]
    [InlineData(3840, 2080, 2)]
    [InlineData(1024, 728, 1.25)]
    [InlineData(800, 560, 1.5)]
    public void InitialWindowAndDecorationsFitWorkingArea(double width, double height, double dpi)
    {
        var layout = WindowLayoutPolicy.ForScreen(width, height, dpi);
        Assert.True((layout.Width + 32) * dpi <= width);
        Assert.True((layout.Height + 64) * dpi <= height);
        Assert.True(layout.MinWidth <= layout.Width);
        Assert.True(layout.MinHeight <= layout.Height);
    }

    [Fact]
    public void EqualLogicalScreensHaveEqualLayouts()
    {
        Assert.Equal(WindowLayoutPolicy.ForScreen(1920, 1040, 1), WindowLayoutPolicy.ForScreen(3840, 2080, 2));
    }

    [Fact]
    public void LargeScreenDoesNotCreateAnOversizedWindow()
    {
        var layout = WindowLayoutPolicy.ForScreen(3840, 2080, 1);
        Assert.Equal(1040, layout.Width);
        Assert.Equal(680, layout.Height);
    }

    [Fact]
    public void ContentShrinksWithWindowButHasAReadabilityLimit()
    {
        Assert.Equal(1, WindowLayoutPolicy.ContentScale(1600, 1000));
        Assert.InRange(WindowLayoutPolicy.ContentScale(1040, 680), 0.85, 0.90);
        Assert.Equal(0.7, WindowLayoutPolicy.ContentScale(600, 400));
    }
}
