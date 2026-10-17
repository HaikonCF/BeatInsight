using System.Windows.Threading;
using IOPath = System.IO.Path;

namespace BeatInsight.CommunityOverlays.Poc.Adapter.Live;

/// <summary>
/// Source de test (V3.14-COM-09, option <c>--source synthetic-live</c>) : rejoue un script
/// d'entrées BeatInsight synthétiques à travers <see cref="LiveCommunityOverlayAdapter"/>,
/// pour vérifier le rendu des overlays avec un payload live (champs inconnus neutres).
/// Les entrées imitent la cadence réelle de BeatInsight (une lecture toutes les 500 ms),
/// la publication reste à 100 ms. Aucun accès au jeu ; jamais utilisée par défaut.
/// Cycle de 40 s : SongSelect (0–3 s) → Gameplay (3–35 s, découverte mémoire non prête
/// pendant 1 s, pause de 15 à 16 s) → Results (35–40 s, partie complète).
/// </summary>
public sealed class SyntheticLiveCommunityOverlayDataSource : ICommunityOverlayDataSource
{
    private const double SongSelectMs = 3_000;
    private const double GameplayMs = 32_000;
    private const double CycleMs = 40_000;
    private const double SourcePeriodMs = 500;
    private const double FirstObjectMs = 1_200;
    private const double LastObjectMs = 30_000;
    private const int TotalObjects = 300;
    private const string Process = "synthetic-osu:1:0";

    private readonly PublishTicker _ticker;
    private readonly string _songs;
    private LiveAdapterState _state = LiveAdapterState.Initial;
    private DateTimeOffset _startedAtUtc;

    /// <param name="songsRoot">Racine Songs synthétique (ex. <c>TestAssets/Songs</c> pour les fonds).</param>
    public SyntheticLiveCommunityOverlayDataSource(string? songsRoot, PublishTimerMode timerMode = PublishTimerMode.Compensated)
    {
        _songs = songsRoot is not null && IOPath.IsPathFullyQualified(songsRoot) ? songsRoot : @"C:\SyntheticOsu\Songs";
        _ticker = new PublishTicker(timerMode, TimeSpan.FromMilliseconds(100), DispatcherPriority.Normal, Publish);
        _startedAtUtc = DateTimeOffset.UtcNow;
        Current = Adapt(_startedAtUtc);
    }

    public PublishTicker Ticker => _ticker;

    public CommunityOverlaySnapshot Current { get; private set; }

    public IReadOnlyList<LiveAdapterEvent> LastEvents { get; private set; } = [];

    public event EventHandler<CommunityOverlaySnapshot>? SnapshotChanged;

    public void Start()
    {
        _startedAtUtc = DateTimeOffset.UtcNow;
        _state = LiveAdapterState.Initial;
        Publish();
        _ticker.Start();
    }

    public void Dispose() => _ticker.Dispose();

    private void Publish()
    {
        Current = Adapt(DateTimeOffset.UtcNow);
        SnapshotChanged?.Invoke(this, Current);
    }

    private CommunityOverlaySnapshot Adapt(DateTimeOffset now)
    {
        double elapsed = Math.Max(0, (now - _startedAtUtc).TotalMilliseconds);
        // Dernière lecture BeatInsight : multiple de 500 ms.
        double sampleMs = Math.Floor(elapsed / SourcePeriodMs) * SourcePeriodMs;
        var input = Script(_startedAtUtc, sampleMs, _songs);
        var result = LiveCommunityOverlayAdapter.Adapt(input, _state, now);
        _state = result.State;
        LastEvents = result.Diagnostics.Events;
        return result.Snapshot;
    }

    /// <summary>Entrées BeatInsight à l'instant <paramref name="sampleMs"/> du script.</summary>
    public static BeatInsightLiveInput Script(DateTimeOffset start, double sampleMs, string songs)
    {
        var at = start.AddMilliseconds(sampleMs);
        double cycleMs = sampleMs % CycleMs;
        string path = IOPath.Combine(songs, "900001 BeatInsight - Community Overlay POC",
            "BeatInsight - Community Overlay POC (Bia-chan) [Mock Insane].osu");
        var game = new SourceGameState(
            "SyntheticStableProvider", "osu! stable", Process, path, "1", "1", at, SourceAvailability.Available,
            songs, IOPath.Combine(IOPath.GetDirectoryName(path)!, "bg.png"));
        var metadata = new SourceBeatmapMetadata(
            path, 1, 1, "BeatInsight", "Community Overlay POC", "Mock Insane", "Bia-chan",
            TimeSpan.FromMilliseconds(LastObjectMs - FirstObjectMs).Ticks, 270, 420,
            10, 9, 4, 6, 220, 78, 2, 5.87, 270, 180, 270, 420);

        if (cycleMs < SongSelectMs)
            return new BeatInsightLiveInput(game, new SourceGameRoute(SourceRoute.SongSelect, at, Process), metadata);
        if (cycleMs >= SongSelectMs + GameplayMs)
            return new BeatInsightLiveInput(game, new SourceGameRoute(SourceRoute.Results, at, Process), metadata);

        double playMs = cycleMs - SongSelectMs;
        // Pause de 15 à 16 s de partie : temps et compteurs figés.
        double timeMs = playMs < 15_000 ? playMs : playMs < 16_000 ? 15_000 : playMs - 1_000;
        var route = new SourceGameRoute(SourceRoute.Gameplay, at, Process);
        var live = new SourceLiveGameplay(true, TimeSpan.FromMilliseconds(timeMs), TimeSpan.FromMilliseconds(LastObjectMs - FirstObjectMs),
            "1", "1", Process, at);
        if (playMs < 1_000)
            return new BeatInsightLiveInput(game, route, metadata, live); // découverte mémoire non prête

        int judged = timeMs < FirstObjectMs ? 0
            : (int)Math.Round(Math.Clamp((timeMs - FirstObjectMs) / (LastObjectMs - FirstObjectMs), 0, 1) * TotalObjects);
        int miss = judged > 137 ? 1 : 0;
        int c100 = judged / 23;
        int c300 = judged - miss - c100;
        int combo = miss == 1 ? judged - 138 : judged;
        double accuracy = judged == 0 ? 0 : (300.0 * c300 + 100.0 * c100) / (300.0 * judged);
        var stats = new SourceLiveStats(Process, "1", "1", TimeSpan.FromMilliseconds(timeMs), combo, Math.Max(combo, Math.Min(judged, 137)),
            c300, c100, 0, miss, accuracy, ["HD"], at, judged * 300);
        var performance = judged == 0 ? null
            : new SourcePerformance(Math.Round(4.1 * judged * accuracy, 2), 412 * accuracy, 380, 412, judged, at);
        return new BeatInsightLiveInput(game, route, metadata, live, stats, performance);
    }
}
