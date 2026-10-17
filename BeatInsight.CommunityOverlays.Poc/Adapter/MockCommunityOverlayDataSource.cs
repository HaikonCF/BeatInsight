using System.Windows.Threading;

namespace BeatInsight.CommunityOverlays.Poc.Adapter;

/// <summary>
/// Source mock déterministe : boucle SongSelect (3 s) → Gameplay (32 s) →
/// Results (5 s). Aucun accès au jeu, aux providers live ni au réseau.
/// Le contenu dépend uniquement du temps écoulé depuis <see cref="Start"/>.
/// Les cycles pairs utilisent la beatmap A, les impairs la beatmap B (même
/// timing) : un changement de beatmap a lieu à chaque retour en SongSelect.
/// Les noms de fichiers correspondent à <c>TestAssets/Songs</c>.
/// </summary>
public sealed class MockCommunityOverlayDataSource : ICommunityOverlayDataSource
{
    private const double SongSelectMs = 3_000;
    private const double GameplayMs = 32_000;
    private const double ResultsMs = 5_000;
    private const double CycleMs = SongSelectMs + GameplayMs + ResultsMs;

    private const int TotalObjects = 300;
    private const int MissIndex = 137;
    private const int SliderBreakIndex = 210;

    private static readonly IReadOnlyList<string> MockMods = ["HD", "DT"];

    private const double FirstObjectMs = 1_200;
    private const double LastObjectMs = 30_000;
    private const double AudioLengthMs = 31_500;

    private static readonly CommunityOverlayBeatmap MockBeatmap = CommunityOverlayBeatmap.Mock(
        beatmapId: 1,
        beatmapSetId: 1,
        checksum: "00000000000000000000000000000000",
        artist: "BeatInsight",
        artistUnicode: "BeatInsight",
        title: "Community Overlay POC",
        titleUnicode: "Community Overlay POC",
        mapper: "Bia-chan",
        version: "Mock Insane",
        stars: 5.87,
        ar: 10.0,
        cs: 4.0,
        od: 9.0,
        hp: 6.0,
        bpmCommon: 270,
        bpmMin: 180,
        bpmMax: 270,
        maxCombo: 420,
        circles: 220,
        sliders: 78,
        spinners: 2,
        firstObjectMs: FirstObjectMs,
        lastObjectMs: LastObjectMs,
        audioLengthMs: AudioLengthMs) with
    {
        Files = new CommunityOverlayBeatmapFiles(
            Folder: "900001 BeatInsight - Community Overlay POC",
            OsuFile: "BeatInsight - Community Overlay POC (Bia-chan) [Mock Insane].osu",
            Background: "bg.png",
            Audio: "audio.wav"),
    };

    private static readonly CommunityOverlayBeatmap SecondMockBeatmap = MockBeatmap with
    {
        BeatmapId = MockField.Of(2),
        BeatmapSetId = MockField.Of(2),
        Checksum = MockField.Of("11111111111111111111111111111111"),
        Title = MockField.Of("Second Mock Map"),
        TitleUnicode = MockField.Of("Second Mock Map"),
        Version = MockField.Of("Mock Hard"),
        Stars = MockField.Of(4.12),
        Files = new CommunityOverlayBeatmapFiles(
            Folder: "900002 BeatInsight - Second Mock Map",
            OsuFile: "BeatInsight - Second Mock Map (Bia-chan) [Mock Hard].osu",
            Background: "bg2.png",
            Audio: "audio.wav"),
    };

    private readonly PublishTicker _ticker;
    private DateTime _startedAtUtc;

    /// <param name="priority">
    /// Priorité du timer de publication. <see cref="DispatcherPriority.Background"/> (défaut WPF,
    /// comportement COM-01 à COM-05) passe après le rendu et glisse vers 7–8 Hz quand un overlay
    /// anime ; <see cref="DispatcherPriority.Normal"/> est la correction O2 (V3.14-COM-06).
    /// </param>
    /// <param name="timerMode">
    /// <see cref="PublishTimerMode.Compensated"/> (défaut, V3.14-COM-07) : échéances absolues,
    /// moyenne proche de 100 ms malgré le pas de 15,6 ms du minuteur Windows.
    /// </param>
    public MockCommunityOverlayDataSource(
        TimeSpan? interval = null,
        DispatcherPriority priority = DispatcherPriority.Normal,
        PublishTimerMode timerMode = PublishTimerMode.Compensated)
    {
        _ticker = new PublishTicker(timerMode, interval ?? TimeSpan.FromMilliseconds(100), priority, Publish);
        Current = BuildAt(0);
    }

    public PublishTicker Ticker => _ticker;

    public CommunityOverlaySnapshot Current { get; private set; }

    public event EventHandler<CommunityOverlaySnapshot>? SnapshotChanged;

    public void Start()
    {
        _startedAtUtc = DateTime.UtcNow;
        Publish();
        _ticker.Start();
    }

    public void Dispose() => _ticker.Dispose();

    private void Publish()
    {
        double elapsed = (DateTime.UtcNow - _startedAtUtc).TotalMilliseconds;
        Current = BuildAt(elapsed % CycleMs, (int)(elapsed / CycleMs));
        SnapshotChanged?.Invoke(this, Current);
    }

    /// <summary>Construit l'instantané à une position donnée du cycle (ms) ; <paramref name="cycle"/> choisit la beatmap.</summary>
    public static CommunityOverlaySnapshot BuildAt(double cycleMs, int cycle = 0)
    {
        CommunityOverlaySnapshot snapshot;
        if (cycleMs < SongSelectMs)
            snapshot = BuildSongSelect();
        else if (cycleMs < SongSelectMs + GameplayMs)
            snapshot = BuildGameplay(cycleMs - SongSelectMs);
        else
            snapshot = BuildGameplay(AudioLengthMs) with { Route = CommunityOverlayRoute.Results };

        return cycle % 2 == 0 ? snapshot : snapshot with { Beatmap = SecondMockBeatmap };
    }

    private static CommunityOverlaySnapshot BuildSongSelect() => Snapshot(
        CommunityOverlayRoute.SongSelect,
        timeMs: 0,
        score: 0,
        combo: 0,
        maxCombo: 0,
        accuracy: 100,
        grade: "SS",
        hits: CommunityOverlayHits.Mock(0, 0, 0, 0, 0, 0, 0),
        pp: BuildPp(0, 1.0, 1.0, 0));

    /// <summary>
    /// Snapshot mock : toutes les valeurs sont marquées <see cref="FieldProvenance.Mock"/>.
    /// Les valeurs émises sont celles du contrat v1 (joueur, HP 100, note max = note courante…).
    /// </summary>
    private static CommunityOverlaySnapshot Snapshot(
        CommunityOverlayRoute route, double timeMs, long score, int combo, int maxCombo,
        double accuracy, string grade, CommunityOverlayHits hits, CommunityOverlayPerformance pp) => new(
        Source: CommunityOverlaySourceKind.Mock,
        Route: route,
        SnapshotAtUtc: DateTimeOffset.UnixEpoch,
        Identity: new CommunityOverlayIdentity("mock", "mock", route == CommunityOverlayRoute.SongSelect ? null : 1),
        Client: MockField.Of("stable"),
        PlayerName: MockField.Of("MockPlayer"),
        Beatmap: MockBeatmap,
        CurrentTimeMs: MockField.Of(timeMs),
        Score: MockField.Of(score),
        Combo: MockField.Of(combo),
        MaxCombo: MockField.Of(maxCombo),
        Accuracy: MockField.Of(accuracy),
        Grade: MockField.Of(grade),
        GradeMaxThisPlay: MockField.Of(grade),
        Hits: hits,
        Mods: MockField.Of(MockMods),
        HealthBar: MockField.Of(100.0),
        Pp: pp);

    private static CommunityOverlaySnapshot BuildGameplay(double timeMs)
    {
        double progress = Math.Clamp(
            (timeMs - FirstObjectMs) / (LastObjectMs - FirstObjectMs), 0, 1);
        int judged = timeMs < FirstObjectMs ? 0 : (int)Math.Round(progress * TotalObjects);

        int great = 0, ok = 0, meh = 0, miss = 0, sliderBreaks = 0;
        int combo = 0, maxCombo = 0;
        long score = 0;

        for (int i = 0; i < judged; i++)
        {
            int value;
            if (i == MissIndex) { miss++; value = 0; }
            else if (i > 0 && i % 97 == 0) { meh++; value = 50; }
            else if (i > 0 && i % 23 == 0) { ok++; value = 100; }
            else { great++; value = 300; }

            if (value == 0 || i == SliderBreakIndex)
            {
                if (i == SliderBreakIndex) sliderBreaks++;
                combo = 0;
            }
            else
            {
                combo++;
            }

            maxCombo = Math.Max(maxCombo, combo);
            score += value * (1 + combo / 25);
        }

        double accuracy = judged == 0
            ? 100
            : 100.0 * (300 * great + 100 * ok + 50 * meh) / (300.0 * judged);

        return Snapshot(
            CommunityOverlayRoute.Gameplay,
            timeMs,
            score,
            combo,
            maxCombo,
            Math.Round(accuracy, 2),
            GradeFor(accuracy, miss, judged),
            CommunityOverlayHits.Mock(great, ok, meh, miss, geki: 0, katu: 0, sliderBreaks),
            BuildPp(progress, accuracy / 100, judged == 0 ? 1 : (double)maxCombo / judged, miss)) with
        {
            Keys = MockField.Of(BuildKeys(timeMs, judged)),
        };
    }

    /// <summary>
    /// Touches simulées (mock uniquement) : un appui alterné k1/k2 par objet jugé,
    /// maintenu 60 ms. m1/m2 restent inutilisées.
    /// </summary>
    private static CommunityOverlayKeys BuildKeys(double timeMs, int judged)
    {
        if (judged == 0)
            return CommunityOverlayKeys.Idle;

        double lastObjectTime = FirstObjectMs + (judged - 1) * (LastObjectMs - FirstObjectMs) / TotalObjects;
        bool pressed = timeMs - lastObjectTime < 60;
        bool lastIsK1 = (judged - 1) % 2 == 0;

        return new CommunityOverlayKeys(
            new CommunityOverlayKey(pressed && lastIsK1, (judged + 1) / 2),
            new CommunityOverlayKey(pressed && !lastIsK1, judged / 2),
            CommunityOverlayKey.Idle,
            CommunityOverlayKey.Idle);
    }

    private static CommunityOverlayPerformance BuildPp(double progress, double accuracy, double comboRatio, int misses)
    {
        const double ssPp = 412.0;
        double ifFc = ssPp * Math.Pow(accuracy, 8);
        double current = ifFc * progress * Math.Pow(comboRatio, 0.8) * Math.Pow(0.97, misses);

        var byAccuracy = new Dictionary<int, FieldValue<double>>();
        foreach (int acc in new[] { 95, 96, 97, 98, 99, 100 })
            byAccuracy[acc] = MockField.Of(Math.Round(ssPp * Math.Pow(acc / 100.0, 8), 2));

        return new CommunityOverlayPerformance(
            MockField.Of(Math.Round(current, 2)), MockField.Of(Math.Round(ifFc, 2)), MockField.Of(Math.Round(current, 2)), byAccuracy);
    }

    private static string GradeFor(double accuracy, int misses, int judged)
    {
        if (judged == 0) return "SS";
        if (accuracy >= 100) return "SS";
        if (accuracy > 93 && misses == 0) return "S";
        if (accuracy > 90) return "A";
        if (accuracy > 80) return "B";
        if (accuracy > 70) return "C";
        return "D";
    }
}
