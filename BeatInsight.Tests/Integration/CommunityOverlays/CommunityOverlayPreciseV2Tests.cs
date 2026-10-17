using System.Diagnostics;
using System.IO;
using BeatInsight.Integration.CommunityOverlays;
using BeatInsight.Integration.CommunityOverlays.Pipe;
using BeatInsight.Integration.Game;
using BeatInsight.Integration.KeyOverlay;
using BeatInsight.Integration.KeyOverlay.Lazer;
using Xunit;

namespace BeatInsight.Tests.Integration.CommunityOverlays;

/// <summary>V3.14-COM-LAZER-KEY-03 : contrat precise v2, publication Stable/Lazer, arbitrage des sources, lecteur Lazer.</summary>
public class CommunityOverlayPreciseV2Tests
{
    private const string Stable = "100:2026-10-16T05:00:00.0000000Z";
    private const string Lazer = "200@2026-10-16T05:10:00.0000000Z";
    private DateTimeOffset now = new(2026, 10, 16, 6, 0, 0, TimeSpan.Zero);
    private readonly List<PreciseV2Envelope> published = [];
    private GameStateSnapshot game = Game("osu!stable", 100, Stable);

    private static GameStateSnapshot Game(string client, int pid, string identity) =>
        new("Provider", client, pid, identity, @"C:\Songs\a\a.osu", "1", "2", DateTimeOffset.UtcNow, GameStateAvailability.Available, null);

    private CommunityOverlayPreciseV2Publisher Publisher() => new(bytes =>
    {
        var e = CommunityOverlayPreciseV2PipeProtocol.TryDeserialize(bytes, out string? rejection);
        Assert.Null(rejection);
        published.Add(e!);
    }, () => game, () => now, "session");

    private static KeyOverlaySample StableSample(string play, int k1, int m1 = 0, bool valid = true) => valid
        ? new(KeyOverlayReadStatus.Valid, KeyOverlayReadFailureReason.None, Stable, "a.osu", play, 1000,
            new KeyOverlayKeys(new(k1, true), new(2, false), new(m1, false), new(4, false)), Stopwatch.GetTimestamp(), 17, 0)
        : new(KeyOverlayReadStatus.Unknown, KeyOverlayReadFailureReason.SourceUnavailable, Stable, null, null, null, null, Stopwatch.GetTimestamp(), 0, 0);

    private LazerKeyWireSample LazerSample(long seq, int left, int right, string validity = "Valid", string process = Lazer,
        bool leftActive = false, bool? m1 = false) =>
        validity == "Valid"
            ? new(LazerKeyWire.Version, "runner", seq, now, "Valid", "None", process, process + "#1", "FirstPlay", 0,
                left, leftActive, right, false, true, false, false, m1, false, 0.1, 140)
            : new(LazerKeyWire.Version, "runner", seq, now, validity, "NotInGameplay", process, null, "None", null,
                null, null, null, null, null, null, null, null, null, 0.1, 50);

    // ------------------------------------------------------------------ contrat

    private static PreciseV2Envelope LazerEnvelope(PreciseV2Keys keys) => new(
        CommunityOverlayPreciseV2PipeProtocol.Name, 2, "s", 1, DateTimeOffset.UtcNow, false, PreciseSource.Lazer,
        PreciseValidity.Valid, "ok", Lazer, null, Lazer + "#1", PreciseTransition.FirstPlay, null, DateTimeOffset.UtcNow, keys);

    private static PreciseV2Keys LazerKeys(int? m1Count = null) => new(
        new(PreciseAvailability.Native, PreciseSemantics.LeftAction, 5, true),
        new(PreciseAvailability.Native, PreciseSemantics.RightAction, 3, false),
        new(PreciseAvailability.Unavailable, PreciseSemantics.None, m1Count, null),
        new(PreciseAvailability.Unavailable, PreciseSemantics.None, null, false));

    [Fact]
    public void Contract_never_accepts_an_unavailable_counter_with_a_value()
    {
        Assert.Null(CommunityOverlayPreciseV2PipeProtocol.Validate(LazerEnvelope(LazerKeys())));
        Assert.Equal("compteur indisponible avec valeur", CommunityOverlayPreciseV2PipeProtocol.Validate(LazerEnvelope(LazerKeys(m1Count: 0))));
    }

    [Fact]
    public void Contract_enforces_the_semantics_of_each_source()
    {
        PreciseV2Keys keys = LazerKeys();
        Assert.NotNull(CommunityOverlayPreciseV2PipeProtocol.Validate(LazerEnvelope(keys with { K1 = keys.K1 with { Semantics = PreciseSemantics.Key } })));
        Assert.NotNull(CommunityOverlayPreciseV2PipeProtocol.Validate(LazerEnvelope(keys with { M1 = new(PreciseAvailability.Native, PreciseSemantics.Key, 0, false) })));
        Assert.NotNull(CommunityOverlayPreciseV2PipeProtocol.Validate(LazerEnvelope(keys) with { Source = PreciseSource.Stable }));
        Assert.NotNull(CommunityOverlayPreciseV2PipeProtocol.Validate(LazerEnvelope(keys) with { Validity = PreciseValidity.Unknown }));
        Assert.NotNull(CommunityOverlayPreciseV2PipeProtocol.Validate(LazerEnvelope(keys) with { Version = 1 }));
        Assert.NotNull(CommunityOverlayPreciseV2PipeProtocol.Validate(LazerEnvelope(keys with { K1 = keys.K1 with { Count = null } })));

        string json = System.Text.Encoding.UTF8.GetString(CommunityOverlayPreciseV2PipeProtocol.Serialize(LazerEnvelope(keys)));
        Assert.Null(CommunityOverlayPreciseV2PipeProtocol.TryDeserialize(System.Text.Encoding.UTF8.GetBytes(json.Insert(1, "\"x\":1,")), out _));
        Assert.Contains("\"count\":null", json);
    }

    [Fact]
    public void V2_pipe_is_distinct_and_v1_is_unchanged()
    {
        Assert.Equal("BeatInsight.CommunityOverlays.precise.v2", CommunityOverlayPreciseV2PipeProtocol.PipeName);
        Assert.Equal("BeatInsight.CommunityOverlays.precise.v1", CommunityOverlayPrecisePipeProtocol.PipeName);
        Assert.Equal(1, CommunityOverlayPrecisePipeProtocol.Version);
        Assert.Equal(2, CommunityOverlayPreciseV2PipeProtocol.Version);
    }

    // ------------------------------------------------------------------ publication

    [Fact]
    public void Stable_is_published_as_four_native_key_counters_with_the_v1_identity_and_transition()
    {
        var v1 = new List<PrecisePipeEnvelope>();
        var reference = new CommunityOverlayPrecisePublisher(b => v1.Add(CommunityOverlayPrecisePipeProtocol.TryDeserialize(b, out _)!), clock: () => now);
        using var publisher = Publisher();
        KeyOverlaySample sample = StableSample("p1", 7, m1: 3);
        reference.OnSample(sample);
        publisher.OnStableSample(sample);

        PreciseV2Envelope e = published[^1];
        Assert.Equal(PreciseSource.Stable, e.Source);
        Assert.Equal(PreciseValidity.Valid, e.Validity);
        Assert.All(new[] { e.Keys!.K1, e.Keys.K2, e.Keys.M1, e.Keys.M2 }, k =>
        {
            Assert.Equal(PreciseAvailability.Native, k.Availability);
            Assert.Equal(PreciseSemantics.Key, k.Semantics);
        });
        Assert.Equal((7, 2, 3, 4), (e.Keys.K1.Count, e.Keys.K2.Count, e.Keys.M1.Count, e.Keys.M2.Count));
        Assert.Equal(v1[^1].PlayIdentity, e.PlayIdentity);
        Assert.Equal(v1[^1].Transition, e.Transition);
        Assert.Equal(Stable, e.ProcessIdentity);
    }

    [Fact]
    public void Lazer_is_published_as_two_native_action_counters_and_two_unavailable()
    {
        game = Game("osu!lazer", 200, "200:2026-10-16T05:10:00.0000000Z");
        using var publisher = Publisher();
        publisher.OnLazerSample(LazerSample(1, 12, 9, leftActive: true, m1: true), now);

        PreciseV2Envelope e = published[^1];
        Assert.Equal(PreciseSource.Lazer, e.Source);
        Assert.Equal(PreciseValidity.Valid, e.Validity);
        Assert.Equal((PreciseAvailability.Native, PreciseSemantics.LeftAction, (int?)12, (bool?)true), (e.Keys!.K1.Availability, e.Keys.K1.Semantics, e.Keys.K1.Count, e.Keys.K1.IsPressed));
        Assert.Equal((PreciseAvailability.Native, PreciseSemantics.RightAction, (int?)9), (e.Keys.K2.Availability, e.Keys.K2.Semantics, e.Keys.K2.Count));
        Assert.Equal(PreciseAvailability.Unavailable, e.Keys.M1.Availability);
        Assert.Null(e.Keys.M1.Count);
        Assert.Null(e.Keys.M2.Count);
        Assert.True(e.Keys.M1.IsPressed); // état physique du bouton, sans compteur
        Assert.Equal(@"C:\Songs\a\a.osu", e.BeatmapIdentity);
    }

    [Fact]
    public void Both_games_in_gameplay_is_ambiguous_and_publishes_no_keys()
    {
        using var publisher = Publisher();
        publisher.OnLazerSample(LazerSample(1, 1, 1), now);
        publisher.OnStableSample(StableSample("p1", 5));
        PreciseV2Envelope e = published[^1];
        Assert.Equal(PreciseValidity.Unknown, e.Validity);
        Assert.Equal("SourcesAmbiguous", e.Reason);
        Assert.Null(e.Keys);
    }

    [Fact]
    public void Lazer_playing_while_beatinsight_follows_stable_is_unknown()
    {
        using var publisher = Publisher();
        publisher.OnStableSample(StableSample("p1", 5, valid: false));
        publisher.OnLazerSample(LazerSample(1, 1, 1), now);
        Assert.Equal("LazerPlayingWhileStableSelected", published[^1].Reason);
        Assert.Null(published[^1].Keys);
    }

    [Fact]
    public void Silent_or_foreign_lazer_source_is_unknown()
    {
        game = Game("osu!lazer", 200, "200:x");
        using var publisher = Publisher();
        publisher.OnLazerSample(LazerSample(1, 1, 1), now);
        Assert.Equal(PreciseValidity.Valid, published[^1].Validity);
        now = now.AddSeconds(1.5);
        publisher.Evaluate();
        Assert.Equal("LazerSourceSilent", published[^1].Reason);

        publisher.OnLazerSample(LazerSample(2, 1, 1, process: "999@2026-10-16T05:10:00.0000000Z"), now);
        Assert.Equal("LazerProcessMismatch", published[^1].Reason);
        Assert.Null(published[^1].Keys);
    }

    [Fact]
    public void No_game_is_unknown_and_heartbeats_keep_strict_sequences()
    {
        game = new GameStateSnapshot("Provider", null, null, null, null, null, null, DateTimeOffset.UtcNow, GameStateAvailability.NoProcess, "none");
        using var publisher = Publisher();
        publisher.Evaluate();
        publisher.Evaluate(); // rien de neuf, battement pas encore dû
        now = now.AddMilliseconds(300);
        publisher.Evaluate();
        Assert.Equal("NoGame", published[0].Reason);
        Assert.Equal(2, published.Count);
        Assert.True(published[1].Heartbeat);
        Assert.Equal(new long[] { 1, 2 }, published.Select(e => e.Sequence));
    }

    // ------------------------------------------------------------------ lecteur Lazer (processus)

    [Fact]
    public void Wire_lines_are_validated_strictly()
    {
        LazerKeyWireSample ok = LazerSample(1, 3, 4);
        Assert.Null(LazerKeyWire.Validate(ok));
        Assert.NotNull(LazerKeyWire.Validate(ok with { LeftCount = -1 }));
        Assert.NotNull(LazerKeyWire.Validate(ok with { Version = 2 }));
        Assert.NotNull(LazerKeyWire.Validate(ok with { Validity = "Weird" }));
        Assert.NotNull(LazerKeyWire.Validate(LazerSample(1, 0, 0, validity: "NotGameplay") with { LeftCount = 0 }));
        Assert.Null(LazerKeyWire.TryParse("{not json", out _));
        Assert.Null(LazerKeyWire.TryParse(LazerKeyWire.Serialize(ok).Insert(1, "\"x\":1,"), out _));
        Assert.NotNull(LazerKeyWire.TryParse(LazerKeyWire.Serialize(ok), out _));
    }

    [Fact]
    public void Runner_lines_must_progress_within_a_session()
    {
        var source = new LazerKeyRunnerSource("unused.exe", clock: () => now);
        Assert.True(source.Accept(LazerKeyWire.Serialize(LazerSample(1, 1, 1))));
        Assert.False(source.Accept(LazerKeyWire.Serialize(LazerSample(1, 2, 1))));   // rejoué
        Assert.True(source.Accept(LazerKeyWire.Serialize(LazerSample(2, 2, 1))));
        Assert.True(source.Accept(LazerKeyWire.Serialize(LazerSample(1, 0, 0) with { Session = "restarted" }))); // lecteur relancé
        Assert.False(source.Accept("garbage"));
        Assert.Equal(1, source.OutOfOrder);
        Assert.Equal(1, source.Rejected);
    }

    [Fact]
    public void Runner_path_is_only_used_when_it_exists()
    {
        Assert.Null(LazerKeyRunnerSource.ResolveRunnerPath(null, Path.GetTempPath()));
        string exe = Path.Combine(Path.GetTempPath(), $"runner-{Guid.NewGuid():N}.exe");
        File.WriteAllText(exe, "");
        try { Assert.Equal(exe, LazerKeyRunnerSource.ResolveRunnerPath(exe, "unused")); }
        finally { File.Delete(exe); }
    }

    [Fact]
    public void V2_is_opt_in()
    {
        Assert.False(MainWindow.IsCommunityOverlaysPreciseV2Enabled([], null));
        Assert.True(MainWindow.IsCommunityOverlaysPreciseV2Enabled(["--community-overlays-precise-v2"], null));
        Assert.True(MainWindow.IsCommunityOverlaysPreciseV2Enabled([], "1"));
    }
}
