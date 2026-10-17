using System.Collections.Immutable;
using BeatInsight.Integration.Game;
using BeatInsight.Overlay;
using BeatInsight.Overlay.GameplayHud;

namespace BeatInsight.Tests.Overlay;

public sealed class LiveGameplayStatsOverlayTests
{
    [Fact]
    public void GameplayStatsProjectToVisibleFormattedBlock()
    {
        GameplayHudOverlayState state = GameplayHudSnapshotProjector.Project(
            Snapshot(OverlayGameState.Gameplay),
            new(false, false, false, false, true));

        Assert.True(state.IsLiveStatsVisible);
        Assert.Equal("2", state.LiveStats.Miss);
        Assert.Equal("4", state.LiveStats.Count100);
        Assert.Equal("428x", state.LiveStats.Combo);
        Assert.Equal("97.42%", state.LiveStats.Accuracy);
        Assert.Equal("HDDT", state.LiveStats.Mods);
    }

    [Fact]
    public void NonGameplayOrDisabledOptionCollapsesStats()
    {
        Assert.False(GameplayHudSnapshotProjector.Project(
            Snapshot(OverlayGameState.Results),
            new(false, false, false, false, true)).IsLiveStatsVisible);
        Assert.False(GameplayHudSnapshotProjector.Project(
            Snapshot(OverlayGameState.Gameplay),
            new(false, false, false, false, false)).IsLiveStatsVisible);
    }

    [Fact]
    public void UpdatedSnapshotReplacesAllDisplayedValues()
    {
        OverlaySnapshot first = Snapshot(OverlayGameState.Gameplay);
        OverlaySnapshot retry = first with
        {
            LiveStats = first.LiveStats! with
            {
                CurrentCombo = 1, MaxComboReached = 1,
                Count300 = 1, Count100 = 0, Count50 = 0, CountMiss = 0,
                Accuracy = 1
            }
        };

        LiveGameplayStatsOverlayState state = LiveGameplayStatsOverlayState.Project(retry);

        Assert.Equal("1x", state.Combo);
        Assert.Equal("0", state.Miss);
        Assert.Equal("100.00%", state.Accuracy);
    }

    private static OverlaySnapshot Snapshot(OverlayGameState route)
    {
        var game = new GameStateSnapshot(
            "test", "osu!stable", 7, "7:generation", @"C:\maps\42.osu",
            "42", "24", DateTimeOffset.UtcNow, GameStateAvailability.Available, null);
        var stats = new LiveGameplayStatsSnapshot(
            "7:generation", "42", "24", TimeSpan.FromSeconds(30),
            428, 500, 900, 4, 1, 2, .9742,
            ImmutableArray.Create(new LiveGameplayMod("HD"), new LiveGameplayMod("DT")),
            DateTimeOffset.UtcNow);
        return new(game, route, LiveStats: stats);
    }
}
