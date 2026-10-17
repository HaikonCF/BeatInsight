using System.Text.Json.Nodes;
using BeatInsight.CommunityOverlays.Poc.Adapter;
using BeatInsight.CommunityOverlays.Poc.Adapter.Live;
using BeatInsight.CommunityOverlays.Poc.Bridge;
using BeatInsight.Integration.CommunityOverlays.Pipe;
using Xunit;

namespace BeatInsight.CommunityOverlays.Poc.Tests;

/// <summary>V3.14-COM-LAZER-KEY-03 : precise v2 côté compagnon (client, mapping vers le canal KeyOverlay, sources).</summary>
public class PreciseV2Tests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 16, 6, 0, 0, TimeSpan.Zero);

    private static PreciseV2Envelope Lazer(long seq, string play, int left, int right, bool leftActive = false,
        PreciseValidity validity = PreciseValidity.Valid, string session = "s1", string process = "200@a") =>
        new(CommunityOverlayPreciseV2PipeProtocol.Name, 2, session, seq, T0, false, PreciseSource.Lazer, validity,
            validity == PreciseValidity.Valid ? "ok" : "NotGameplay/NotInGameplay", process, "map",
            validity == PreciseValidity.Valid ? play : null,
            validity == PreciseValidity.Valid ? PreciseTransition.FirstPlay : PreciseTransition.None, null, T0,
            validity == PreciseValidity.Valid
                ? new PreciseV2Keys(
                    new(PreciseAvailability.Native, PreciseSemantics.LeftAction, left, leftActive),
                    new(PreciseAvailability.Native, PreciseSemantics.RightAction, right, false),
                    new(PreciseAvailability.Unavailable, PreciseSemantics.None, null, true),
                    new(PreciseAvailability.Unavailable, PreciseSemantics.None, null, false))
                : null);

    private static PreciseV2Envelope Stable(long seq, string play, int k1, int m1, string session = "s1") =>
        new(CommunityOverlayPreciseV2PipeProtocol.Name, 2, session, seq, T0, false, PreciseSource.Stable, PreciseValidity.Valid,
            "None", "100:a", "map", play, PreciseTransition.FirstPlay, 1000, T0,
            new PreciseV2Keys(
                new(PreciseAvailability.Native, PreciseSemantics.Key, k1, false),
                new(PreciseAvailability.Native, PreciseSemantics.Key, 0, false),
                new(PreciseAvailability.Native, PreciseSemantics.Key, m1, false),
                new(PreciseAvailability.Native, PreciseSemantics.Key, 0, false)));

    private static JsonObject Keys(string? payload) => (JsonObject)JsonNode.Parse(payload!)!["keys"]!;

    [Fact]
    public void Lazer_action_counters_drive_k1_k2_and_unavailable_keys_never_appear()
    {
        var channel = new PreciseKeyChannel(() => T0.AddSeconds(5));
        channel.OnEnvelope(PreciseV2Mapping.ToChannel(Lazer(1, "p", 0, 0)));
        string? payload = channel.OnEnvelope(PreciseV2Mapping.ToChannel(Lazer(2, "p", 1, 0, leftActive: true)));
        JsonObject keys = Keys(payload);
        Assert.Equal(1, keys["k1"]!["count"]!.GetValue<int>());
        Assert.True(keys["k1"]!["isPressed"]!.GetValue<bool>());
        Assert.False(keys.ContainsKey("m1")); // indisponible : jamais affiché (aucun compteur artificiel, aucun BPM)
        Assert.False(keys.ContainsKey("m2"));

        // Même avec l'état physique du bouton à vrai et des centaines de messages, M1 n'apparaît jamais.
        for (int i = 3; i < 200; i++)
            payload = channel.OnEnvelope(PreciseV2Mapping.ToChannel(Lazer(i, "p", 1 + i, i / 2))) ?? payload;
        Assert.False(Keys(payload).ContainsKey("m1"));
        Assert.Equal(0, channel.EmittedCounts[2]);
    }

    [Fact]
    public void Unavailable_maps_to_a_constant_internal_value_never_to_a_native_reading()
    {
        PrecisePipeEnvelope mapped = PreciseV2Mapping.ToChannel(Lazer(1, "p", 5, 6));
        Assert.Equal(0, mapped.Keys!.M1.Count);
        Assert.False(mapped.Keys.M1.IsPressed);
        Assert.Equal(5, mapped.Keys.K1.Count);
        Assert.Equal(6, mapped.Keys.K2.Count);
    }

    [Fact]
    public void Switching_source_or_channel_is_always_a_new_play()
    {
        PrecisePipeEnvelope lazer = PreciseV2Mapping.ToChannel(Lazer(1, "same", 3, 3));
        PrecisePipeEnvelope stable = PreciseV2Mapping.ToChannel(Stable(2, "same", 3, 0));
        Assert.NotEqual(lazer.PlayIdentity, stable.PlayIdentity);
        Assert.NotEqual(lazer.ProcessIdentity, stable.ProcessIdentity);
        Assert.StartsWith("v2:", lazer.ProducerSession);

        var channel = new PreciseKeyChannel(() => T0.AddSeconds(5), resetPageOnNewPlay: true);
        channel.OnEnvelope(lazer);
        channel.OnEnvelope(PreciseV2Mapping.ToChannel(Lazer(2, "same", 4, 3)));
        channel.OnEnvelope(stable);
        Assert.Equal(2, channel.NewPlays);
        Assert.True(channel.ConsumePageReset()); // compteurs Lazer affichés : page vierge avant Stable
    }

    [Fact]
    public void Stable_through_v2_keeps_four_native_counters()
    {
        var channel = new PreciseKeyChannel(() => T0.AddSeconds(5));
        channel.OnEnvelope(PreciseV2Mapping.ToChannel(Stable(1, "p", 0, 0)));
        JsonObject keys = Keys(channel.OnEnvelope(PreciseV2Mapping.ToChannel(Stable(2, "p", 1, 1))));
        Assert.Equal(1, keys["k1"]!["count"]!.GetValue<int>());
        Assert.Equal(1, keys["m1"]!["count"]!.GetValue<int>());
    }

    [Fact]
    public void Unknown_v2_releases_without_changing_counters()
    {
        var channel = new PreciseKeyChannel(() => T0.AddSeconds(5));
        channel.OnEnvelope(PreciseV2Mapping.ToChannel(Lazer(1, "p", 0, 0)));
        channel.OnEnvelope(PreciseV2Mapping.ToChannel(Lazer(2, "p", 2, 0, leftActive: true)));
        JsonObject released = Keys(channel.OnEnvelope(PreciseV2Mapping.ToChannel(Lazer(3, "p", 0, 0, validity: PreciseValidity.Unknown))));
        Assert.Equal(2, released["k1"]!["count"]!.GetValue<int>());
        Assert.False(released["k1"]!["isPressed"]!.GetValue<bool>());
    }

    [Fact]
    public void Valid_lazer_precise_proves_gameplay_but_unknown_or_stable_does_not()
    {
        Assert.True(PreciseV2Mapping.ProvesLazerGameplay(Lazer(1, "p", 3, 4)));
        Assert.False(PreciseV2Mapping.ProvesLazerGameplay(Lazer(2, "p", 0, 0, validity: PreciseValidity.Unknown)));
        Assert.False(PreciseV2Mapping.ProvesLazerGameplay(Stable(3, "p", 3, 4)));
    }

    [Fact]
    public void Proven_lazer_gameplay_reconciles_only_v2_state_for_keyoverlay_gate()
    {
        CommunityOverlaySnapshot unknown = MockCommunityOverlayDataSource.BuildAt(20_000) with
        {
            Route = CommunityOverlayRoute.Unknown,
        };

        JsonObject withoutProof = TosuPayloadBuilder.BuildV2(unknown);
        JsonObject withProof = TosuPayloadBuilder.BuildV2(unknown, CommunityOverlayRoute.Gameplay);

        Assert.Equal(10, withoutProof["state"]!["number"]!.GetValue<int>());
        Assert.Equal(2, withProof["state"]!["number"]!.GetValue<int>());
        Assert.Equal(10, TosuPayloadBuilder.BuildV1(unknown)["menu"]!["state"]!.GetValue<int>());
        Assert.Equal(withoutProof["play"]!.ToJsonString(), withProof["play"]!.ToJsonString());
        Assert.Equal(withoutProof["beatmap"]!.ToJsonString(), withProof["beatmap"]!.ToJsonString());

        // La preuve precise peut survivre brièvement à la fin d'une partie : elle ne doit
        // jamais masquer une route générale Results ou SongSelect déjà connue.
        Assert.Equal(7, TosuPayloadBuilder.BuildV2(
            unknown with { Route = CommunityOverlayRoute.Results },
            CommunityOverlayRoute.Gameplay)["state"]!["number"]!.GetValue<int>());
        Assert.Equal(5, TosuPayloadBuilder.BuildV2(
            unknown with { Route = CommunityOverlayRoute.SongSelect },
            CommunityOverlayRoute.Gameplay)["state"]!["number"]!.GetValue<int>());
    }

    [Fact]
    public void V2_client_accepts_in_order_and_rejects_replays_stale_and_other_versions()
    {
        using var client = new PreciseV2PipeClient("unused-v2-test");
        byte[] Frame(PreciseV2Envelope e) => CommunityOverlayPreciseV2PipeProtocol.Serialize(e);
        Assert.True(client.Accept(Frame(Lazer(1, "p", 0, 0)), T0.AddMilliseconds(10), out _, out _));
        Assert.False(client.Accept(Frame(Lazer(1, "p", 0, 0)), T0.AddMilliseconds(10), out _, out _));
        Assert.False(client.Accept(Frame(Lazer(2, "p", 0, 0)), T0.AddSeconds(3), out _, out _));
        byte[] v1 = CommunityOverlayPrecisePipeProtocol.Serialize(new PrecisePipeEnvelope(
            CommunityOverlayPrecisePipeProtocol.Name, 1, "s1", 9, T0, false, PreciseValidity.Unknown, "x", null, null, null,
            PreciseTransition.None, null, T0, null));
        Assert.False(client.Accept(v1, T0, out _, out bool mismatch));
        Assert.True(mismatch);
    }

    [Fact]
    public void Precise_v2_is_opt_in()
    {
        Assert.False(PocOptions.Parse(["--precise-pipe"]).PreciseV2);
        Assert.True(PocOptions.Parse(["--precise-pipe", "--precise-v2"]).PreciseV2);
    }

    [Fact]
    public void V2_priority_requires_a_connected_recent_message()
    {
        Assert.True(PreciseV2PriorityPolicy.IsV2Active(true, 999, 1000));
    }

    [Fact]
    public void V2_priority_allows_v1_when_v2_is_disconnected_or_has_no_message()
    {
        Assert.False(PreciseV2PriorityPolicy.IsV2Active(false, 0, 1000));
        Assert.False(PreciseV2PriorityPolicy.IsV2Active(true, null, 1000));
    }

    [Fact]
    public void V2_priority_expires_at_the_existing_one_second_boundary()
    {
        Assert.False(PreciseV2PriorityPolicy.IsV2Active(true, 1000, 1000));
    }

    [Fact]
    public void V2_priority_returns_when_message_age_becomes_fresh_again()
    {
        Assert.False(PreciseV2PriorityPolicy.IsV2Active(true, 1000.001, 1000));
        Assert.True(PreciseV2PriorityPolicy.IsV2Active(true, 1, 1000));
    }
}
