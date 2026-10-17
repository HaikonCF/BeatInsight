using System.Text.Json.Nodes;
using BeatInsight.Integration.CommunityOverlays.Pipe;

namespace BeatInsight.CommunityOverlays.Poc.Adapter.Live;

/// <summary>
/// Politique de publication des touches vers <c>/websocket/v2/precise</c> (V3.14-COM-KEY-04).
/// Pure et déterministe (horloge injectée) ; thread-safe par verrou.
///
/// Contraintes de KeyOverlay (cyperdark 1.3.3, index.js:392-436, non modifié) :
/// tout changement de <c>count</c> appelle <c>registerKeypress()</c> (calcul BPM), et une baisse de
/// <c>beatmap.time.live</c> sur le canal v2 purge son cache de compteurs. Donc :
/// <list type="bullet">
/// <item>une remise à zéro n'est jamais envoyée comme telle : après une nouvelle partie (ou un
/// raccordement en cours de partie), une touche n'est réintroduite qu'au moment où son compteur
/// augmente réellement (incrément observé), de sorte que le <c>registerKeypress</c> provoqué coïncide
/// avec un vrai appui ; jamais sur un simple rattrapage de valeur (cause d'un pic BPM observé en
/// validation réelle, COM-KEY-04) ;</item>
/// <item>cette réintroduction attend que la purge v2 ait été postée (baisse de temps observée),
/// au plus <see cref="PurgeWaitTimeout"/>, pour qu'une purge tardive ne rejoue pas l'appui ;</item>
/// <item>perte de données, message périmé ou déconnexion : les touches affichées sont relâchées
/// (isPressed=false) avec leur dernier compteur réel inchangé, donc sans événement.</item>
/// <item>V3.14-COM-KEY-07 : quand une nouvelle partie commence alors que la page affiche déjà des
/// compteurs, le canal demande un rechargement de la page de l'overlay (<see cref="ConsumePageReset"/>).
/// Le cache interne de KeyOverlay et son DOM repartent alors de zéro (touches masquées, aucun compteur
/// résiduel), sans qu'aucun count ne soit envoyé : aucun registerKeypress artificiel. La réintroduction
/// au premier incrément réel s'applique ensuite comme à la première partie.</item>
/// </list>
/// </summary>
public sealed class PreciseKeyChannel
{
    public static readonly TimeSpan PurgeWaitTimeout = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan PurgeLookBack = TimeSpan.FromSeconds(1);
    public const double TimeDecreaseToleranceMs = 50;

    private static readonly string[] Names = ["k1", "k2", "m1", "m2"];

    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _clock;
    private readonly bool[] _included = new bool[4];
    private readonly int[] _lastCounts = new int[4];
    private readonly bool[] _lastPressed = new bool[4];
    private readonly int[] _seen = [-1, -1, -1, -1];
    private string? _currentPlay;
    private string? _currentProducer;
    private readonly bool _resetPageOnNewPlay;
    private bool _pageResetPending;
    private bool _anyKeyShownEver;
    private bool _purgeRequired;
    private DateTimeOffset _playDetectedAt;
    private DateTimeOffset? _lastV2DecreaseAt;
    private double? _lastV2TimeMs;
    private bool _released = true;
    private string? _lastPayload;

    /// <param name="resetPageOnNewPlay">
    /// COM-KEY-07 : vrai si l'hôte sait recharger la page (connecteur WebView2) ; faux pour l'ancien
    /// comportement COM-KEY-04 (compteur résiduel affiché jusqu'au prochain appui de la touche).
    /// </param>
    public PreciseKeyChannel(Func<DateTimeOffset>? clock = null, bool resetPageOnNewPlay = false)
    {
        _resetPageOnNewPlay = resetPageOnNewPlay;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    // Diagnostic.
    public long ValidEnvelopes { get; private set; }
    public long UnknownEnvelopes { get; private set; }
    public long NewPlays { get; private set; }
    public long Releases { get; private set; }
    public long PayloadsEmitted { get; private set; }
    public long WithheldKeyUpdates { get; private set; }
    public long PurgeTimeouts { get; private set; }
    public long PageResets { get; private set; }

    /// <summary>Vrai une seule fois après qu'une nouvelle partie a exigé le rechargement de la page.</summary>
    public bool ConsumePageReset()
    {
        lock (_gate)
        {
            bool pending = _pageResetPending;
            _pageResetPending = false;
            return pending;
        }
    }
    public PreciseTransition LastTransition { get; private set; }
    public int[] EmittedCounts { get { lock (_gate) return (int[])_lastCounts.Clone(); } }

    /// <summary>Message precise reçu. Renvoie le payload à poster, ou null s'il n'y a rien à envoyer.</summary>
    public string? OnEnvelope(PrecisePipeEnvelope envelope)
    {
        lock (_gate)
        {
            DateTimeOffset now = _clock();
            if (envelope.Validity != PreciseValidity.Valid || envelope.Keys is null)
            {
                UnknownEnvelopes++;
                return ReleaseLocked();
            }
            ValidEnvelopes++;

            // Nouvelle partie : autre PlayIdentity, ou autre producteur (BeatInsight redémarré : les numéros
            // de partie repartent de 1, une identité identique ne prouve pas que ce soit la même partie).
            bool newProducer = _currentProducer is not null &&
                !string.Equals(envelope.ProducerSession, _currentProducer, StringComparison.Ordinal);
            _currentProducer = envelope.ProducerSession;
            if (newProducer || !string.Equals(envelope.PlayIdentity, _currentPlay, StringComparison.Ordinal))
            {
                NewPlays++;
                LastTransition = envelope.Transition;
                _currentPlay = envelope.PlayIdentity;
                _playDetectedAt = now;
                // Si l'overlay a déjà affiché des compteurs, il faut que sa purge (v2) précède toute réintroduction.
                _purgeRequired = _anyKeyShownEver;
                if (_resetPageOnNewPlay && _anyKeyShownEver)
                {
                    // Page rechargée : cache et DOM de KeyOverlay vierges, aucune purge v2 à attendre.
                    _pageResetPending = true;
                    PageResets++;
                    _anyKeyShownEver = false;
                    _purgeRequired = false;
                    _lastPayload = null;
                }
                Array.Clear(_included);
                Array.Fill(_seen, -1);
            }

            PreciseKey[] keys = [envelope.Keys.K1, envelope.Keys.K2, envelope.Keys.M1, envelope.Keys.M2];
            bool purgeReady = !_purgeRequired || PurgeObservedLocked(now);
            var json = new JsonObject();
            for (int i = 0; i < 4; i++)
            {
                if (!_included[i])
                {
                    bool incremented = _seen[i] >= 0 && keys[i].Count > _seen[i];
                    _seen[i] = keys[i].Count;
                    // Ni zéro, ni rattrapage : seulement sur un incrément réel observé dans cette partie.
                    if (!incremented)
                    {
                        if (keys[i].Count > 0) WithheldKeyUpdates++;
                        continue;
                    }
                    if (!purgeReady)
                    {
                        WithheldKeyUpdates++;
                        continue;
                    }
                    _included[i] = true;
                    _anyKeyShownEver = true;
                }
                _lastCounts[i] = keys[i].Count;
                _lastPressed[i] = keys[i].IsPressed;
                json[Names[i]] = new JsonObject { ["isPressed"] = keys[i].IsPressed, ["count"] = keys[i].Count };
            }
            if (purgeReady) _purgeRequired = false;
            _released = false;
            return EmitLocked(json);
        }
    }

    /// <summary>Connexion perdue, ou source muette au-delà du délai : relâchement sans changement de compteur.</summary>
    public string? OnSourceLost()
    {
        lock (_gate) return ReleaseLocked();
    }

    /// <summary>À appeler quand un payload v2 est effectivement posté à la page (temps <c>beatmap.time.live</c>).</summary>
    public void OnV2Posted(double? timeLiveMs)
    {
        lock (_gate)
        {
            if (timeLiveMs is double t && _lastV2TimeMs is double last && t + TimeDecreaseToleranceMs < last)
                _lastV2DecreaseAt = _clock();
            if (timeLiveMs is not null) _lastV2TimeMs = timeLiveMs;
        }
    }

    private bool PurgeObservedLocked(DateTimeOffset now)
    {
        if (_lastV2DecreaseAt is DateTimeOffset at && at >= _playDetectedAt - PurgeLookBack)
            return true;
        if (now - _playDetectedAt >= PurgeWaitTimeout)
        {
            PurgeTimeouts++;
            return true;
        }
        return false;
    }

    private string? ReleaseLocked()
    {
        if (_released) return null;
        _released = true;
        Releases++;
        var json = new JsonObject();
        for (int i = 0; i < 4; i++)
        {
            if (!_included[i]) continue;
            _lastPressed[i] = false;
            json[Names[i]] = new JsonObject { ["isPressed"] = false, ["count"] = _lastCounts[i] };
        }
        return EmitLocked(json);
    }

    private string? EmitLocked(JsonObject keys)
    {
        // Même forme que tosu (buildResultV2Precise) ; seules les touches réintroduites sont présentes.
        var payload = new JsonObject { ["keys"] = keys, ["hitErrors"] = new JsonArray(), ["tourney"] = new JsonArray() };
        string text = payload.ToJsonString();
        if (string.Equals(text, _lastPayload, StringComparison.Ordinal)) return null;
        _lastPayload = text;
        PayloadsEmitted++;
        return text;
    }
}
