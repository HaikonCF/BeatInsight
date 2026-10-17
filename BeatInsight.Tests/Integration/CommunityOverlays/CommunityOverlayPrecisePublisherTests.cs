using System.Diagnostics;
using BeatInsight.Integration.CommunityOverlays;
using BeatInsight.Integration.CommunityOverlays.Pipe;
using BeatInsight.Integration.KeyOverlay;
using Xunit;

namespace BeatInsight.Tests.Integration.CommunityOverlays;

/// <summary>V3.14-COM-KEY-04 : publication du canal precise (stratégies, battement, transitions, opt-in).</summary>
public class CommunityOverlayPrecisePublisherTests
{
    private const string Process1 = "100:2026-10-14T19:00:00.0000000Z";
    private const string Process2 = "200:2026-10-14T19:30:00.0000000Z";

    private long now = 1_000_000;
    private readonly List<PrecisePipeEnvelope> published = [];

    private CommunityOverlayPrecisePublisher Publisher(PrecisePublishMode mode = PrecisePublishMode.OnChange) =>
        new(bytes =>
            {
                var e = CommunityOverlayPrecisePipeProtocol.TryDeserialize(bytes, out string? rejection);
                Assert.Null(rejection);
                published.Add(e!);
            },
            mode,
            () => new DateTimeOffset(2026, 10, 14, 20, 0, 0, TimeSpan.Zero),
            () => now,
            "session");

    private KeyOverlaySample Valid(string play, int k1, bool p1 = false, string process = Process1, string? beatmap = "a.osu") =>
        new(KeyOverlayReadStatus.Valid, KeyOverlayReadFailureReason.None, process, beatmap, play, 1000,
            new KeyOverlayKeys(new(k1, p1), new(0, false), new(0, false), new(0, false)), now, 17, 0x1234);

    private KeyOverlaySample Unknown(KeyOverlayReadFailureReason reason, string process = Process1) =>
        new(KeyOverlayReadStatus.Unknown, reason, process, null, null, null, null, now, 0, 0);

    private void Advance(double ms) => now += (long)(ms * Stopwatch.Frequency / 1000);

    [Fact]
    public void On_change_strategy_publishes_changes_and_heartbeats_only()
    {
        var publisher = Publisher();
        for (int i = 0; i < 100; i++) // 1 s à 100 Hz, état constant sauf un appui
        {
            publisher.OnSample(Valid("p1", i < 50 ? 3 : 4, p1: i is >= 50 and < 60));
            Advance(10);
        }

        // 1 initial + appui + relâchement + battements (250 ms) ≈ 6, jamais 100.
        Assert.InRange(publisher.MessagesPublished, 5, 8);
        Assert.Equal(100, publisher.Samples);
        Assert.Contains(published, e => e.Keys!.K1.Count == 4 && e.Keys.K1.IsPressed && !e.Heartbeat);
        Assert.Contains(published, e => e.Heartbeat);
        Assert.True(published.Select(e => e.Sequence).SequenceEqual(Enumerable.Range(1, published.Count).Select(i => (long)i)));
    }

    [Fact]
    public void Continuous_strategy_publishes_every_sample()
    {
        var publisher = Publisher(PrecisePublishMode.Continuous);
        for (int i = 0; i < 100; i++)
        {
            publisher.OnSample(Valid("p1", 3));
            Advance(10);
        }
        Assert.Equal(100, publisher.MessagesPublished);
    }

    [Fact]
    public void Unknown_samples_never_carry_keys()
    {
        var publisher = Publisher();
        publisher.OnSample(Valid("p1", 5));
        publisher.OnSample(Unknown(KeyOverlayReadFailureReason.StructureChangedDuringRead));
        PrecisePipeEnvelope last = published[^1];
        Assert.Equal(PreciseValidity.Unknown, last.Validity);
        Assert.Null(last.Keys);
        Assert.Equal("StructureChangedDuringRead", last.Reason);
        Assert.Equal(PreciseTransition.None, last.Transition);
    }

    [Fact]
    public void Transitions_are_qualified_from_identities_never_from_addresses()
    {
        var publisher = Publisher();
        publisher.OnSample(Valid("p1", 1));
        Assert.Equal(PreciseTransition.FirstPlay, published[^1].Transition);

        publisher.OnSample(Valid("p2", 0)); // recul du temps, même écran
        Assert.Equal(PreciseTransition.Retry, published[^1].Transition);

        publisher.OnSample(Unknown(KeyOverlayReadFailureReason.RouteNotGameplay));
        publisher.OnSample(Valid("p3", 0)); // retour en jeu, même map
        Assert.Equal(PreciseTransition.NewGameplay, published[^1].Transition);

        publisher.OnSample(Unknown(KeyOverlayReadFailureReason.RouteNotGameplay));
        publisher.OnSample(Valid("p4", 0, beatmap: "b.osu"));
        Assert.Equal(PreciseTransition.BeatmapChanged, published[^1].Transition);

        publisher.OnSample(Valid("p5", 0, process: Process2, beatmap: "b.osu"));
        Assert.Equal(PreciseTransition.ProcessChanged, published[^1].Transition);

        // La transition est répétée sur tous les messages de la partie (pipe : le plus récent gagne).
        publisher.OnSample(Valid("p5", 1, process: Process2, beatmap: "b.osu"));
        Assert.Equal(PreciseTransition.ProcessChanged, published[^1].Transition);
    }

    [Fact]
    public void Late_beatmap_identity_upgrades_new_gameplay_to_beatmap_changed()
    {
        var publisher = Publisher();
        publisher.OnSample(Valid("p1", 1, beatmap: "a.osu"));
        publisher.OnSample(Unknown(KeyOverlayReadFailureReason.RouteNotGameplay));
        publisher.OnSample(Valid("p2", 0, beatmap: "a.osu")); // GameStateService pas encore à jour
        Assert.Equal(PreciseTransition.NewGameplay, published[^1].Transition);
        publisher.OnSample(Valid("p2", 0, beatmap: "b.osu"));
        Assert.Equal(PreciseTransition.BeatmapChanged, published[^1].Transition);
        Assert.Equal("p2", published[^1].PlayIdentity);
    }

    [Fact]
    public void Precise_channel_is_disabled_by_default()
    {
        Assert.False(MainWindow.IsCommunityOverlaysPreciseEnabled(["BeatInsight.exe"], null));
        Assert.False(MainWindow.IsCommunityOverlaysPreciseEnabled(["BeatInsight.exe"], "0"));
        Assert.True(MainWindow.IsCommunityOverlaysPreciseEnabled(["BeatInsight.exe", "--community-overlays-precise"], null));
        Assert.True(MainWindow.IsCommunityOverlaysPreciseEnabled(["BeatInsight.exe"], "1"));
        Assert.Equal(PrecisePublishMode.OnChange, MainWindow.PrecisePublishModeFrom(null));
        Assert.Equal(PrecisePublishMode.Continuous, MainWindow.PrecisePublishModeFrom("continuous"));
    }

    [Fact]
    public void Precise_pipe_is_distinct_from_gameplay_pipes()
    {
        Assert.NotEqual(CommunityOverlayPipeProtocol.PipeName, CommunityOverlayPrecisePipeProtocol.PipeName);
        Assert.NotEqual(CommunityOverlayPipeProtocol.LegacyPipeNameV1, CommunityOverlayPrecisePipeProtocol.PipeName);
        Assert.Equal("BeatInsight.CommunityOverlays.precise.v1", CommunityOverlayPrecisePipeProtocol.PipeName);
    }
}
