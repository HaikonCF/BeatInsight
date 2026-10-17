using BeatInsight.Overlay;

namespace BeatInsight.Tests.Overlay;

public sealed class OverlayDisplayCompatibilityTests
{
    private static readonly OverlayWindowRectangle Monitor =
        new(0, 0, 1920, 1080);

    [Fact]
    public void ClientAbsentIsUnsupportedAndHidden()
    {
        OverlayDisplayCompatibility result =
            OverlayDisplayModeClassifier.Classify(false, null);

        Assert.Equal(OverlayDisplayMode.ClientAbsent, result.DisplayMode);
        Assert.False(result.OverlaySupported);
        Assert.False(result.OverlayVisible);
    }

    [Fact]
    public void LiveValidatedWindowedIsSupportedAndVisible()
    {
        OverlayDisplayCompatibility result = Classify(
            style: 0x16CB0000,
            extendedStyle: 0x00050100,
            new(508, 206, 1314, 835));

        Assert.Equal(OverlayDisplayMode.Windowed, result.DisplayMode);
        Assert.True(result.OverlaySupported);
        Assert.True(result.OverlayVisible);
    }

    [Fact]
    public void LiveValidatedBorderlessWithOnePixelOverscanIsSupportedAndVisible()
    {
        OverlayDisplayCompatibility result = Classify(
            style: 0x16010000,
            extendedStyle: 0x00050000,
            new(0, 0, 1920, 1081));

        Assert.Equal(OverlayDisplayMode.Borderless, result.DisplayMode);
        Assert.True(result.OverlaySupported);
        Assert.True(result.OverlayVisible);
    }

    [Fact]
    public void LiveValidatedFullscreenExclusiveIsUnsupportedAndExplainsBorderlessRequirement()
    {
        OverlayDisplayCompatibility result = Classify(
            style: 0x16010000,
            extendedStyle: 0x00050008,
            Monitor);

        Assert.Equal(OverlayDisplayMode.FullscreenExclusive, result.DisplayMode);
        Assert.False(result.OverlaySupported);
        Assert.False(result.OverlayVisible);
        Assert.Contains("Borderless Fullscreen", result.StatusText);
    }

    [Fact]
    public void MinimizedExclusiveRetainsUnsupportedMode()
    {
        OverlayDisplayCompatibility result =
            OverlayDisplayModeClassifier.Classify(
                true,
                new OverlayWindowSnapshot(
                    Style: 0xB6020000,
                    ExtendedStyle: 0,
                    IsMinimized: true,
                    new(-32000, -32000, -31840, -31972),
                    Monitor));

        Assert.Equal(OverlayDisplayMode.FullscreenExclusive, result.DisplayMode);
        Assert.False(result.OverlayVisible);
    }

    [Fact]
    public void MissingWindowFailsClosedInsteadOfClaimingVisibility()
    {
        OverlayDisplayCompatibility result =
            OverlayDisplayModeClassifier.Classify(true, null);

        Assert.Equal(OverlayDisplayMode.Unknown, result.DisplayMode);
        Assert.False(result.OverlaySupported);
        Assert.False(result.OverlayVisible);
    }

    private static OverlayDisplayCompatibility Classify(
        long style,
        long extendedStyle,
        OverlayWindowRectangle bounds) =>
        OverlayDisplayModeClassifier.Classify(
            true,
            new OverlayWindowSnapshot(
                style,
                extendedStyle,
                IsMinimized: false,
                bounds,
                Monitor));
}
