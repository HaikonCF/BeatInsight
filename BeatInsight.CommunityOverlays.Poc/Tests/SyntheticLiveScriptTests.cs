using BeatInsight.CommunityOverlays.Poc.Adapter;
using BeatInsight.CommunityOverlays.Poc.Adapter.Live;
using BeatInsight.CommunityOverlays.Poc.Bridge;
using Xunit;

namespace BeatInsight.CommunityOverlays.Poc.Tests;

/// <summary>V3.14-COM-09 : le script synthétique rejoué à 10 Hz sur des lectures à 2 Hz, comme en réel.</summary>
public class SyntheticLiveScriptTests
{
    [Fact]
    public void Two_cycles_produce_two_plays_complete_results_and_no_spurious_retry()
    {
        var start = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
        var state = LiveAdapterState.Initial;
        var events = new List<LiveAdapterEvent>();
        var routes = new HashSet<CommunityOverlayRoute>();

        for (double ms = 0; ms < 80_000; ms += 100)
        {
            double sample = Math.Floor(ms / 500) * 500;
            var input = SyntheticLiveCommunityOverlayDataSource.Script(start, sample, @"C:\SyntheticOsu\Songs");
            var r = LiveCommunityOverlayAdapter.Adapt(input, state, start.AddMilliseconds(ms));
            state = r.State;
            events.AddRange(r.Diagnostics.Events);
            routes.Add(r.Snapshot.Route);

            Assert.Equal(CommunityOverlaySourceKind.Live, r.Snapshot.Source);
            Assert.Equal(0, TosuPayloadBuilder.BuildV2(r.Snapshot)["play"]!["healthBar"]!["normal"]!.GetValue<double>());
            if (r.Snapshot.Route == CommunityOverlayRoute.Results)
                Assert.True(r.Diagnostics.Fields["hits.300"] == FieldState.LastKnown, $"{ms}: {string.Join(",", r.Diagnostics.Events)} judged={state.Play?.LastJudged} all={string.Join(",", events)}");
        }

        Assert.Equal(2, events.Count(e => e == LiveAdapterEvent.NewPlay));
        Assert.Equal(2, events.Count(e => e == LiveAdapterEvent.ResultsLatched));
        Assert.DoesNotContain(LiveAdapterEvent.Retry, events);
        Assert.DoesNotContain(LiveAdapterEvent.ResultsIncomplete, events);
        Assert.Contains(LiveAdapterEvent.StatsUnavailable, events); // découverte mémoire non prête au début
        Assert.Equal(
            new HashSet<CommunityOverlayRoute> { CommunityOverlayRoute.SongSelect, CommunityOverlayRoute.Gameplay, CommunityOverlayRoute.Results },
            routes);
        Assert.Equal(2, state.LastPlayId);
    }
}
