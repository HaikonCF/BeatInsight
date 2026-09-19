using BeatInsight.ViewModels;
using System.Windows;
using System.Windows.Media.Imaging;

namespace BeatInsight.Tests.ViewModels;

public sealed class BiaAssistantViewModelTests
{
    [Fact]
    public void StateMapping_UsesExistingAssetsAndAnalyzingFallsBackToIdle()
    {
        Assert.Equal(
            "Assets/BiaChan/BiaChanIdle.png",
            BiaVisualAssetMap.GetAssetPath(BiaVisualState.Idle));
        Assert.Equal(
            BiaVisualAssetMap.IdleAsset,
            BiaVisualAssetMap.GetAssetPath(BiaVisualState.Analyzing));
        Assert.Equal(
            "Assets/BiaChan/BiaChanError.png",
            BiaVisualAssetMap.GetAssetPath(BiaVisualState.Error));
        Assert.Equal(
            "Assets/BiaChan/BiaChanGoodrun.png",
            BiaVisualAssetMap.GetAssetPath(BiaVisualState.GoodRun));
        Assert.Equal(
            "Assets/BiaChan/BiaChanFailed.png",
            BiaVisualAssetMap.GetAssetPath(BiaVisualState.Failed));
        Assert.Equal(
            "Assets/BiaChan/BiaChanFC.png",
            BiaVisualAssetMap.GetAssetPath(BiaVisualState.FullCombo));
    }

    [Fact]
    public void UnknownState_FallsBackToIdleWithoutThrowing()
    {
        Assert.Equal(
            BiaVisualAssetMap.IdleAsset,
            BiaVisualAssetMap.GetAssetPath((BiaVisualState)999));
    }

    [Theory]
    [InlineData(BiaVisualState.Idle)]
    [InlineData(BiaVisualState.Error)]
    [InlineData(BiaVisualState.Analyzing)]
    [InlineData((BiaVisualState)999)]
    public void ImageSource_ResolvesEmbeddedWpfResource(BiaVisualState state)
    {
        string asset = BiaVisualAssetMap.GetAssetPath(state);
        Uri uri = BiaVisualAssetMap.GetPackUri(asset);

        Assert.True(uri.IsAbsoluteUri);
        using var resource = Application.GetResourceStream(uri)?.Stream;
        Assert.NotNull(resource);
        var image = Assert.IsType<BitmapImage>(BiaVisualAssetMap.GetImage(state));
        Assert.Equal(uri, image.UriSource);
    }

    [Fact]
    public void MissingAsset_FallsBackWithoutThrowing()
    {
        var image = Assert.IsType<BitmapImage>(
            BiaVisualAssetMap.GetImageFromAssetPath("Assets/BiaChan/Missing.png"));
        Assert.Equal(BiaVisualAssetMap.GetPackUri(BiaVisualAssetMap.IdleAsset), image.UriSource);
    }

    [Fact]
    public void OverviewTransitions_IdleAnalyzingErrorAndBackToIdle()
    {
        var overview = new OverviewViewModel();

        Assert.Equal(BiaVisualState.Idle, overview.Assistant.State);
        Assert.IsType<BitmapImage>(overview.Assistant.ImageSource);

        overview.SetLoading("Analyzing...");
        Assert.Equal(BiaVisualState.Analyzing, overview.Assistant.State);
        Assert.Equal(
            BiaVisualAssetMap.GetPackUri(BiaVisualAssetMap.IdleAsset),
            Assert.IsType<BitmapImage>(overview.Assistant.ImageSource).UriSource);

        overview.SetError("Unable to load map.");
        Assert.Equal(BiaVisualState.Error, overview.Assistant.State);
        Assert.IsType<BitmapImage>(overview.Assistant.ImageSource);

        overview.Clear();
        Assert.Equal(BiaVisualState.Idle, overview.Assistant.State);
    }
}
