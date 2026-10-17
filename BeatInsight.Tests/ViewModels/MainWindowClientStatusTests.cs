using System.Collections.Immutable;
using BeatInsight.Integration.Game;
using BeatInsight.ViewModels;
using BeatInsight.Overlay;

namespace BeatInsight.Tests.ViewModels;

public sealed class MainWindowClientStatusTests
{
    [Fact]
    public void FullscreenExclusiveAddsExplicitOverlayRequirementWithoutChangingClientState()
    {
        var viewModel = new MainWindowViewModel();
        viewModel.ApplyGameState(Snapshot(
            LazerGameProvider.ProviderName,
            "osu!lazer",
            GameStateAvailability.Available,
            processId: 7));

        viewModel.ApplyOverlayDisplayCompatibility(
            OverlayDisplayCompatibility.FullscreenExclusive);

        Assert.Equal("Connected to osu!lazer", viewModel.ClientStatusText);
        Assert.Equal(OverlayDisplayMode.FullscreenExclusive, viewModel.OverlayDisplayMode);
        Assert.False(viewModel.OverlaySupported);
        Assert.True(viewModel.HasOverlayStatus);
        Assert.Contains("Borderless Fullscreen", viewModel.GameStateStatusText);
    }

    [Fact]
    public void NoProcess_ShowsNoClient()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.ApplyGameState(ServiceSnapshot(
            Attempt(NativeStableGameProvider.ProviderName, GameStateAvailability.NoProcess),
            Attempt(LazerGameProvider.ProviderName, GameStateAvailability.NoProcess)));

        Assert.Equal("No osu! client detected", viewModel.ClientStatusText);
        Assert.Equal("No osu! client detected", viewModel.EmptyStateTitle);
        Assert.Equal(ClientStatusKind.Disconnected, viewModel.ClientStatusKind);
    }

    [Fact]
    public void LazerDiscovery_ShowsDetectingInsteadOfNoBeatmap()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.ApplyGameState(ServiceSnapshot(
            Attempt(NativeStableGameProvider.ProviderName, GameStateAvailability.NoProcess),
            Attempt(
                LazerGameProvider.ProviderName,
                GameStateAvailability.ProviderUnavailable,
                "NativeLazer discovery in progress.",
                "osu!lazer",
                42)));

        Assert.Equal("Detecting osu!lazer…", viewModel.ClientStatusText);
        Assert.Equal("Detecting osu!lazer…", viewModel.EmptyStateTitle);
        Assert.Equal(ClientStatusKind.Detecting, viewModel.ClientStatusKind);
    }

    [Theory]
    [InlineData("NativeLazer", "osu!lazer")]
    [InlineData("NativeStable", "osu!stable")]
    public void AvailableProvider_ShowsConnected(
        string providerName,
        string clientName)
    {
        var viewModel = new MainWindowViewModel();

        viewModel.ApplyGameState(Snapshot(
            providerName,
            clientName,
            GameStateAvailability.Available,
            processId: 7));

        Assert.Equal($"Connected to {clientName}", viewModel.ClientStatusText);
        Assert.Equal($"● {clientName}", viewModel.ClientStatusCompactText);
        Assert.Equal(ClientStatusKind.Connected, viewModel.ClientStatusKind);
    }

    [Fact]
    public void ClientWithoutBeatmap_RemainsConnected()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.ApplyGameState(ServiceSnapshot(Attempt(
            NativeStableGameProvider.ProviderName,
            GameStateAvailability.NoBeatmap,
            clientName: "osu!stable",
            processId: 11)));

        Assert.Equal("Connected to osu!stable", viewModel.ClientStatusText);
        Assert.Equal("No beatmap selected", viewModel.EmptyStateTitle);
    }

    [Fact]
    public void NoProcess_ClearsPreviousConnectedStatus()
    {
        var viewModel = new MainWindowViewModel();
        viewModel.ApplyGameState(Snapshot(
            NativeStableGameProvider.ProviderName,
            "osu!stable",
            GameStateAvailability.Available,
            processId: 7));

        viewModel.ApplyGameState(ServiceSnapshot(
            Attempt(NativeStableGameProvider.ProviderName, GameStateAvailability.NoProcess),
            Attempt(LazerGameProvider.ProviderName, GameStateAvailability.NoProcess)));

        Assert.Equal("No osu! client detected", viewModel.ClientStatusText);
        Assert.Equal(ClientStatusKind.Disconnected, viewModel.ClientStatusKind);
    }

    [Fact]
    public void TransitionsStableToLazerAndBack()
    {
        var viewModel = new MainWindowViewModel();
        GameStateSnapshot stable = Snapshot(
            NativeStableGameProvider.ProviderName,
            "osu!stable",
            GameStateAvailability.Available,
            processId: 7);
        GameStateSnapshot lazer = Snapshot(
            LazerGameProvider.ProviderName,
            "osu!lazer",
            GameStateAvailability.Available,
            processId: 8);

        viewModel.ApplyGameState(stable);
        Assert.Equal("Connected to osu!stable", viewModel.ClientStatusText);
        viewModel.ApplyGameState(lazer);
        Assert.Equal("Connected to osu!lazer", viewModel.ClientStatusText);
        viewModel.ApplyGameState(stable);
        Assert.Equal("Connected to osu!stable", viewModel.ClientStatusText);
    }

    [Fact]
    public void FutureProvider_UsesPublishedClientName()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.ApplyGameState(Snapshot(
            "FutureProvider",
            "osu!future",
            GameStateAvailability.Available,
            processId: 99));

        Assert.Equal("Connected to osu!future", viewModel.ClientStatusText);
        Assert.Equal("● osu!future", viewModel.ClientStatusCompactText);
    }

    [Fact]
    public void FutureProviderWithoutClientName_UsesProviderNameFallback()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.ApplyGameState(Snapshot(
            "FutureProvider",
            null,
            GameStateAvailability.Available,
            processId: 100));

        Assert.Equal("Connected to FutureProvider", viewModel.ClientStatusText);
        Assert.Equal("● FutureProvider", viewModel.ClientStatusCompactText);
    }

    private static ProviderAttempt Attempt(
        string providerName,
        GameStateAvailability availability,
        string? reason = null,
        string? clientName = null,
        int? processId = null) => new(
            providerName,
            availability,
            reason,
            clientName,
            processId,
            processId.HasValue ? $"{processId}:identity" : null);

    private static GameStateSnapshot ServiceSnapshot(
        params ProviderAttempt[] attempts) =>
        Snapshot(
            nameof(GameStateService),
            null,
            GameStateAvailability.ProviderUnavailable) with
        {
            ProviderAttempts = attempts.ToImmutableArray()
        };

    private static GameStateSnapshot Snapshot(
        string providerName,
        string? clientName,
        GameStateAvailability availability,
        int? processId = null) => new(
            providerName,
            clientName,
            processId,
            processId.HasValue ? $"{processId}:identity" : null,
            availability == GameStateAvailability.Available
                ? @"C:\maps\current.osu"
                : null,
            availability == GameStateAvailability.Available ? "42" : null,
            null,
            DateTimeOffset.UtcNow,
            availability,
            null);
}
