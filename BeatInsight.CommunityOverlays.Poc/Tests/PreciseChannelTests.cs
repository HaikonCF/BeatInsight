using System.IO.Pipes;
using System.Text.Json.Nodes;
using BeatInsight.CommunityOverlays.Poc.Adapter.Live;
using BeatInsight.Integration.CommunityOverlays.Pipe;
using Xunit;

namespace BeatInsight.CommunityOverlays.Poc.Tests;

/// <summary>V3.14-COM-KEY-04 : contrat precise, client, et politique KeyOverlay (resets sans faux appui).</summary>
public class PreciseChannelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 14, 20, 0, 0, TimeSpan.Zero);

    private static PrecisePipeEnvelope Env(long seq, string play, int k1, bool p1 = false, int k2 = 0, bool p2 = false,
        int m1 = 0, int m2 = 0, PreciseValidity validity = PreciseValidity.Valid, PreciseTransition transition = PreciseTransition.Retry,
        DateTimeOffset? sent = null, string session = "s1") =>
        new(CommunityOverlayPrecisePipeProtocol.Name, CommunityOverlayPrecisePipeProtocol.Version, session, seq,
            sent ?? T0, false, validity, validity == PreciseValidity.Valid ? "None" : "RouteNotGameplay",
            "1:2026", "map", validity == PreciseValidity.Valid ? play : null, validity == PreciseValidity.Valid ? transition : PreciseTransition.None,
            1000, sent ?? T0,
            validity == PreciseValidity.Valid
                ? new PreciseKeys(new(k1, p1), new(k2, p2), new(m1, false), new(m2, false))
                : null);

    private static JsonObject Keys(string? payload) => (JsonObject)JsonNode.Parse(payload!)!["keys"]!;

    // ---------------------------------------------------------------- contrat

    [Fact]
    public void Envelope_round_trips_and_validates()
    {
        byte[] bytes = CommunityOverlayPrecisePipeProtocol.Serialize(Env(1, "p1", 3, true));
        Assert.True(bytes.Length < 600, $"{bytes.Length} octets");
        var back = CommunityOverlayPrecisePipeProtocol.TryDeserialize(bytes, out string? rejection);
        Assert.Null(rejection);
        Assert.Equal(3, back!.Keys!.K1.Count);
        Assert.True(back.Keys.K1.IsPressed);
    }

    [Fact]
    public void Envelope_validation_rejects_inconsistent_messages()
    {
        Assert.NotNull(CommunityOverlayPrecisePipeProtocol.Validate(Env(1, "p", 1) with { Version = 2 }));
        Assert.NotNull(CommunityOverlayPrecisePipeProtocol.Validate(Env(1, "p", 1) with { Protocol = "x" }));
        Assert.NotNull(CommunityOverlayPrecisePipeProtocol.Validate(Env(0, "p", 1)));
        Assert.NotNull(CommunityOverlayPrecisePipeProtocol.Validate(Env(1, "p", -1)));
        Assert.NotNull(CommunityOverlayPrecisePipeProtocol.Validate(Env(1, "p", 1_000_001)));
        Assert.NotNull(CommunityOverlayPrecisePipeProtocol.Validate(Env(1, "p", 1) with { Keys = null }));
        Assert.NotNull(CommunityOverlayPrecisePipeProtocol.Validate(
            Env(1, "p", 1, validity: PreciseValidity.Unknown) with { Keys = new(new(1, false), new(0, false), new(0, false), new(0, false)) }));
        Assert.NotNull(CommunityOverlayPrecisePipeProtocol.Validate(Env(1, "p", 1) with { PlayIdentity = new string('x', 300) }));
        Assert.Null(CommunityOverlayPrecisePipeProtocol.Validate(Env(1, "p", 1, validity: PreciseValidity.Unknown)));
    }

    [Fact]
    public void Oversized_or_malformed_frames_are_rejected()
    {
        Assert.Null(CommunityOverlayPrecisePipeProtocol.TryDeserialize(new byte[5000], out string? r1));
        Assert.Equal("taille", r1);
        Assert.Null(CommunityOverlayPrecisePipeProtocol.TryDeserialize("{nope"u8, out string? r2));
        Assert.Equal("json", r2);
    }

    // ---------------------------------------------------------------- client

    [Fact]
    public void Client_rejects_out_of_order_stale_and_foreign_producer_messages()
    {
        var client = new PrecisePipeClient("unused");
        byte[] F(PrecisePipeEnvelope e) => CommunityOverlayPrecisePipeProtocol.Serialize(e);

        Assert.True(client.Accept(F(Env(5, "p", 1)), T0, out _, out _));
        Assert.False(client.Accept(F(Env(5, "p", 1)), T0, out _, out _));      // séquence répétée
        Assert.False(client.Accept(F(Env(4, "p", 1)), T0, out _, out _));      // séquence en recul
        Assert.False(client.Accept(F(Env(6, "p", 1, session: "s2")), T0, out _, out _)); // autre producteur
        Assert.False(client.Accept(F(Env(7, "p", 1, sent: T0)), T0.AddSeconds(2), out _, out _)); // périmé
        Assert.True(client.Accept(F(Env(8, "p", 2)), T0.AddMilliseconds(5), out _, out _));
        Assert.False(client.Accept(F(Env(9, "p", 1) with { Version = 9 }), T0, out _, out bool mismatch));
        Assert.True(mismatch);
        Assert.Equal(3, client.OutOfOrder);
        Assert.Equal(1, client.Stale);
    }

    [Fact]
    public async Task Client_receives_over_a_real_pipe_reconnects_and_reports_loss()
    {
        string name = "BeatInsight.Test.precise." + Guid.NewGuid().ToString("N");
        var received = new List<long>();
        int lost = 0;
        using var client = new PrecisePipeClient(name);
        client.EnvelopeReceived += (e, _) => { lock (received) received.Add(e.Sequence); };
        client.ConnectionLost += () => Interlocked.Increment(ref lost);
        client.Start();

        for (int round = 0; round < 2; round++)
        {
            var server = new CommunityOverlayPipeServer(name);
            Assert.True(server.Start());
            await WaitUntil(() => server.ConnectedClients == 1);
            for (int i = 1; i <= 3; i++)
            {
                server.Publish(CommunityOverlayPrecisePipeProtocol.Serialize(
                    Env(i, "p", i, sent: DateTimeOffset.UtcNow, session: $"run{round}")));
                await WaitUntil(() => { lock (received) return received.Count >= round * 3 + i; });
            }
            server.Dispose(); // BeatInsight fermé
            await WaitUntil(() => Volatile.Read(ref lost) >= round + 1);
        }

        lock (received) Assert.Equal(new long[] { 1, 2, 3, 1, 2, 3 }, received); // séquences reprises à la reconnexion
        Assert.Equal(2, client.Connections);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            await Task.Delay(10);
        }
    }

    // ---------------------------------------------------------------- politique KeyOverlay

    [Fact]
    public void First_play_publishes_keys_once_counted_and_never_a_zero_reset()
    {
        DateTimeOffset now = T0;
        var channel = new PreciseKeyChannel(() => now);

        // Aucune touche comptée : rien n'est envoyé pour les touches (pas de 0 qui déclencherait registerKeypress).
        Assert.Empty(Keys(channel.OnEnvelope(Env(1, "p1", 0, p1: true))));
        JsonObject k = Keys(channel.OnEnvelope(Env(2, "p1", 1, p1: true)));
        Assert.Equal(1, (int)k["k1"]!["count"]!);
        Assert.True((bool)k["k1"]!["isPressed"]!);
        Assert.False(k.ContainsKey("k2"));
        // Relâchement : même compteur, isPressed false.
        k = Keys(channel.OnEnvelope(Env(3, "p1", 1, p1: false)));
        Assert.False((bool)k["k1"]!["isPressed"]!);
        // Battement de cœur identique : aucun post.
        Assert.Null(channel.OnEnvelope(Env(4, "p1", 1, p1: false)));
    }

    [Fact]
    public void Retry_withholds_keys_until_v2_purge_is_posted_and_never_sends_the_reset()
    {
        DateTimeOffset now = T0;
        var channel = new PreciseKeyChannel(() => now);
        channel.OnV2Posted(30_000);
        channel.OnEnvelope(Env(0, "p1", 39, k2: 34) with { Sequence = 1 });
        channel.OnEnvelope(Env(1, "p1", 40, k2: 35) with { Sequence = 2 }); // touches affichées
        long withheldBefore = channel.WithheldKeyUpdates;

        // Retry : nouvelle partie, compteurs à 0 → aucun envoi de touche.
        now = now.AddMilliseconds(50);
        Assert.Empty(Keys(channel.OnEnvelope(Env(2, "p2", 0, k2: 0))));
        // Premier appui compté, mais la purge v2 n'a pas encore été postée : retenu.
        now = now.AddMilliseconds(100);
        string? withheld = channel.OnEnvelope(Env(3, "p2", 1));
        Assert.True(withheld is null || !Keys(withheld).ContainsKey("k1"));
        Assert.True(channel.WithheldKeyUpdates > withheldBefore);
        // La page reçoit la baisse de temps (purge du cache KeyOverlay)…
        channel.OnV2Posted(-1500);
        // …puis la touche est réintroduite par un vrai appui.
        now = now.AddMilliseconds(10);
        // Valeur inchangée (simple rattrapage) : toujours rien, pour ne pas créer d'appui hors frappe.
        string? same = channel.OnEnvelope(Env(4, "p2", 1));
        Assert.True(same is null || !Keys(same).ContainsKey("k1"));
        // …puis la touche est réintroduite au vrai appui suivant.
        JsonObject k = Keys(channel.OnEnvelope(Env(5, "p2", 2)));
        Assert.Equal(2, (int)k["k1"]!["count"]!);
        Assert.False(k.ContainsKey("k2"));
        Assert.Equal(2, channel.NewPlays);
    }

    [Fact]
    public void Purge_wait_is_bounded()
    {
        DateTimeOffset now = T0;
        var channel = new PreciseKeyChannel(() => now);
        channel.OnEnvelope(Env(1, "p1", 9));
        channel.OnEnvelope(Env(1, "p1", 10) with { Sequence = 2 }); // touche affichée
        now = now.AddMilliseconds(10);
        channel.OnEnvelope(Env(2, "p2", 1)); // aucune purge v2 (canal gameplay absent)
        now = now + PreciseKeyChannel.PurgeWaitTimeout;
        JsonObject k = Keys(channel.OnEnvelope(Env(3, "p2", 2)));
        Assert.Equal(2, (int)k["k1"]!["count"]!);
        Assert.Equal(1, channel.PurgeTimeouts);
    }

    [Fact]
    public void Purge_posted_shortly_before_detection_is_accepted()
    {
        DateTimeOffset now = T0;
        var channel = new PreciseKeyChannel(() => now);
        channel.OnV2Posted(60_000);
        channel.OnEnvelope(Env(1, "p1", 10));
        now = now.AddMilliseconds(300);
        channel.OnV2Posted(0); // la purge v2 arrive avant la détection precise
        now = now.AddMilliseconds(200);
        channel.OnEnvelope(Env(2, "p2", 0));
        Assert.Equal(1, (int)Keys(channel.OnEnvelope(Env(3, "p2", 1)))["k1"]!["count"]!);
    }

    [Fact]
    public void Data_loss_and_disconnect_release_keys_without_changing_counts()
    {
        DateTimeOffset now = T0;
        var channel = new PreciseKeyChannel(() => now);
        channel.OnEnvelope(Env(1, "p1", 6, k2: 2));
        channel.OnEnvelope(Env(2, "p1", 7, p1: true, k2: 3, p2: true));

        JsonObject released = Keys(channel.OnEnvelope(Env(3, "p1", 0, validity: PreciseValidity.Unknown)));
        Assert.Equal(7, (int)released["k1"]!["count"]!);
        Assert.False((bool)released["k1"]!["isPressed"]!);
        Assert.Equal(3, (int)released["k2"]!["count"]!);
        Assert.False((bool)released["k2"]!["isPressed"]!);
        Assert.Null(channel.OnSourceLost()); // déjà relâché : rien de plus

        // Retour de la même partie : reprise sans reset.
        JsonObject back = Keys(channel.OnEnvelope(Env(4, "p1", 8, p1: true, k2: 3)));
        Assert.Equal(8, (int)back["k1"]!["count"]!);
        Assert.Equal(1, channel.NewPlays);

        Assert.NotNull(channel.OnSourceLost());
        Assert.Equal(new[] { 8, 3, 0, 0 }, channel.EmittedCounts);
    }

    [Fact]
    public void Mouse_buttons_map_to_m1_and_m2()
    {
        var channel = new PreciseKeyChannel(() => T0);
        channel.OnEnvelope(Env(1, "p1", 0));
        JsonObject k = Keys(channel.OnEnvelope(Env(2, "p1", 0, m1: 1, m2: 1)));
        Assert.Equal(1, (int)k["m1"]!["count"]!);
        Assert.Equal(1, (int)k["m2"]!["count"]!);
    }

    [Fact]
    public void Late_attach_or_withheld_catch_up_never_creates_an_off_beat_event()
    {
        // Validation réelle COM-KEY-04 : source attachée en pleine partie (K2 déjà à 13). Envoyer 13
        // puis 14 juste après faisait calculer à KeyOverlay un BPM absurde (556). Désormais K2
        // n'apparaît qu'au vrai appui suivant.
        DateTimeOffset now = T0;
        var channel = new PreciseKeyChannel(() => now);
        channel.OnEnvelope(Env(1, "p0", 40, k2: 30)); // partie précédente affichée
        now = now.AddMilliseconds(10);
        string? attach = channel.OnEnvelope(Env(2, "p1", 31, k2: 13));
        Assert.True(attach is null || Keys(attach).Count == 0);
        now = now + PreciseKeyChannel.PurgeWaitTimeout; // aucune purge v2 (redémarrage d'osu!)
        string? caughtUp = channel.OnEnvelope(Env(3, "p1", 31, k2: 13)); // battement, pas d'appui
        Assert.True(caughtUp is null || Keys(caughtUp).Count == 0);
        JsonObject k = Keys(channel.OnEnvelope(Env(4, "p1", 31, k2: 14))); // vrai appui K2
        Assert.Equal(14, (int)k["k2"]!["count"]!);
        Assert.False(k.ContainsKey("k1"));
    }

    // ---------------------------------------------------------------- COM-KEY-07 : remise à zéro par rechargement

    [Fact]
    public void Retry_after_shown_keys_requests_one_page_reset_and_sends_no_count()
    {
        DateTimeOffset now = T0;
        var channel = new PreciseKeyChannel(() => now, resetPageOnNewPlay: true);
        channel.OnEnvelope(Env(1, "p1", 99));
        channel.OnEnvelope(Env(2, "p1", 100, k2: 1, m1: 1, m2: 1)); // compteurs élevés affichés
        Assert.False(channel.ConsumePageReset());

        string? atReset = channel.OnEnvelope(Env(3, "p2", 0)); // retry : compteurs natifs à 0
        Assert.True(channel.ConsumePageReset());
        Assert.False(channel.ConsumePageReset()); // une seule fois
        Assert.NotNull(atReset);
        Assert.Empty(Keys(atReset)); // page vierge : aucun count envoyé, donc aucun registerKeypress
        Assert.Equal(1, channel.PageResets);
    }

    [Fact]
    public void After_page_reset_first_real_press_is_shown_without_waiting_for_v2()
    {
        DateTimeOffset now = T0;
        var channel = new PreciseKeyChannel(() => now, resetPageOnNewPlay: true);
        channel.OnEnvelope(Env(1, "p1", 9));
        channel.OnEnvelope(Env(2, "p1", 10, k2: 4));
        channel.OnEnvelope(Env(3, "p2", 0));
        Assert.True(channel.ConsumePageReset());

        // Aucune baisse de temps v2 postée : inutile, le cache de la page est neuf.
        JsonObject k = Keys(channel.OnEnvelope(Env(4, "p2", 1)));
        Assert.Equal(1, (int)k["k1"]!["count"]!);
        Assert.False(k.ContainsKey("k2")); // touche non réutilisée : ni ancien compteur, ni zéro
        Assert.Equal(0, channel.PurgeTimeouts);
    }

    [Fact]
    public void Rapid_presses_during_the_transition_are_never_lost()
    {
        DateTimeOffset now = T0;
        var channel = new PreciseKeyChannel(() => now, resetPageOnNewPlay: true);
        channel.OnEnvelope(Env(1, "p1", 1));
        channel.OnEnvelope(Env(2, "p1", 2));
        channel.OnEnvelope(Env(3, "p2", 0));
        Assert.True(channel.ConsumePageReset());
        channel.OnEnvelope(Env(4, "p2", 1));
        channel.OnEnvelope(Env(5, "p2", 2));
        JsonObject k = Keys(channel.OnEnvelope(Env(6, "p2", 3, p1: true)));
        Assert.Equal(3, (int)k["k1"]!["count"]!); // compteur absolu : aucun appui perdu
    }

    [Fact]
    public void Restarted_producer_is_a_new_play_even_with_a_colliding_play_identity()
    {
        var channel = new PreciseKeyChannel(() => T0, resetPageOnNewPlay: true);
        channel.OnEnvelope(Env(1, "pid#2", 4));
        channel.OnEnvelope(Env(2, "pid#2", 5));
        channel.OnEnvelope(Env(1, "pid#2", 0, session: "s2")); // BeatInsight redémarré, même numéro de partie
        Assert.True(channel.ConsumePageReset());
    }

    [Fact]
    public void No_page_reset_when_nothing_was_shown()
    {
        var channel = new PreciseKeyChannel(() => T0, resetPageOnNewPlay: true);
        channel.OnEnvelope(Env(1, "p1", 0));
        channel.OnEnvelope(Env(2, "p2", 0));
        Assert.False(channel.ConsumePageReset());
    }

    [Fact]
    public void Reset_disabled_keeps_the_com_key_04_behaviour()
    {
        var channel = new PreciseKeyChannel(() => T0);
        channel.OnEnvelope(Env(1, "p1", 9));
        channel.OnEnvelope(Env(2, "p1", 10));
        channel.OnEnvelope(Env(3, "p2", 0));
        Assert.False(channel.ConsumePageReset());
        Assert.Equal(0, channel.PageResets);
    }
}
