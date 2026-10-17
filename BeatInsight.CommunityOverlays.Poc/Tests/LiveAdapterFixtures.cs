using BeatInsight.CommunityOverlays.Poc.Adapter;
using BeatInsight.CommunityOverlays.Poc.Adapter.Live;

namespace BeatInsight.CommunityOverlays.Poc.Tests;

/// <summary>
/// Snapshots BeatInsight synthétiques et réalistes (V3.14-COM-09) : formes et valeurs
/// identiques à celles produites par GameStateService / OverlaySnapshot, sans osu!.
/// Beatmap de 10 objets (5 cercles, 3 sliders, 2 spinners).
/// </summary>
internal static class LiveAdapterFixtures
{
    public static readonly DateTimeOffset T0 = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    public const string Process = "osu!.exe:4242:638640000000000000";
    public const string OtherProcess = "osu!.exe:5151:638640000990000000";
    public const string Songs = @"C:\Games\osu!\Songs";
    public const string MapA = @"C:\Games\osu!\Songs\100 Artist - Song\Artist - Song (Mapper) [Hard].osu";
    public const string MapB = @"C:\Games\osu!\Songs\200 Other - Tune\Other - Tune (Mapper) [Insane].osu";
    public const int TotalObjects = 10;

    public static SourceGameState Game(
        DateTimeOffset at, string process = Process, string path = MapA, string id = "1001", string set = "100",
        string provider = "NativeStableGameProvider", string client = "osu! stable",
        SourceAvailability availability = SourceAvailability.Available) => new(
        ProviderName: provider,
        ClientName: client,
        ProcessIdentity: availability == SourceAvailability.Available ? process : null,
        CurrentBeatmapPath: path,
        CurrentBeatmapId: id,
        CurrentBeatmapSetId: set,
        LastUpdateUtc: at,
        Availability: availability,
        SongsDirectory: Songs,
        CurrentBackgroundPath: System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "bg.jpg"));

    public static SourceGameRoute Route(SourceRoute route, DateTimeOffset at, string process = Process) => new(route, at, process);

    public static SourceBeatmapMetadata Metadata(string path = MapA, int id = 1001) => new(
        BeatmapPath: path,
        BeatmapId: id,
        BeatmapSetId: 100,
        Artist: "Artist",
        Title: "Song",
        Version: "Hard",
        Creator: "Mapper",
        LengthTicks: TimeSpan.FromSeconds(30).Ticks,
        Bpm: 180,
        MaxCombo: 14,
        ApproachRate: 9,
        OverallDifficulty: 8,
        CircleSize: 4,
        HpDrain: 5,
        CircleCount: 5,
        SliderCount: 3,
        SpinnerCount: 2,
        OsuStarRating: 5.25,
        PrimaryBpm: 180,
        MinBpm: 160,
        MaxBpm: 200,
        OsuMaxCombo: 14);

    public static SourceLiveStats Stats(
        DateTimeOffset at, double timeMs, int c300, int c100 = 0, int c50 = 0, int miss = 0,
        int combo = -1, int maxCombo = -1, string process = Process, string? id = "1001", int? score = null,
        params string[] mods)
    {
        int judged = c300 + c100 + c50 + miss;
        double accuracy = judged == 0 ? 0 : (300.0 * c300 + 100.0 * c100 + 50.0 * c50) / (300.0 * judged);
        int c = combo >= 0 ? combo : judged - miss;
        return new SourceLiveStats(
            process, id, "100", TimeSpan.FromMilliseconds(timeMs), c, maxCombo >= 0 ? maxCombo : c,
            c300, c100, c50, miss, accuracy, mods, at, score ?? judged * 300);
    }

    public static SourcePerformance Performance(DateTimeOffset at, int passed, double current, double fc = 250, double ss = 300) =>
        new(current, fc, Accuracy95FcPp: 220, SsPp: ss, passed, at);

    public static SourceLiveGameplay Live(DateTimeOffset at, double timeMs, string process = Process, string? id = "1001") =>
        new(true, TimeSpan.FromMilliseconds(timeMs), TimeSpan.FromSeconds(30), id, "100", process, at);

    /// <summary>Tick BeatInsight complet en gameplay stable (stats + PP cohérents).</summary>
    public static BeatInsightLiveInput GameplayTick(
        DateTimeOffset at, double timeMs, int c300, int c100 = 0, int c50 = 0, int miss = 0, double? pp = null,
        string process = Process, params string[] mods)
    {
        var stats = Stats(at, timeMs, c300, c100, c50, miss, process: process, mods: mods);
        return new BeatInsightLiveInput(
            Game(at, process),
            Route(SourceRoute.Gameplay, at, process),
            Metadata(),
            Live(at, timeMs, process),
            stats,
            stats.JudgedObjects == 0 ? null : Performance(at, stats.JudgedObjects, pp ?? 10.0 * stats.JudgedObjects));
    }

    public static BeatInsightLiveInput ScreenTick(DateTimeOffset at, SourceRoute route, string path = MapA, string id = "1001") =>
        new(Game(at, path: path, id: id), Route(route, at), Metadata(path, int.Parse(id)));

    /// <summary>Rejoue une séquence d'entrées, chacune à son propre instant.</summary>
    public sealed class Sequence
    {
        public LiveAdapterState State { get; private set; } = LiveAdapterState.Initial;

        public LiveAdapterResult Last { get; private set; } = null!;

        public LiveAdapterResult Step(BeatInsightLiveInput input, DateTimeOffset now)
        {
            Last = LiveCommunityOverlayAdapter.Adapt(input, State, now);
            State = Last.State;
            return Last;
        }
    }

    public static FieldState F(this LiveAdapterResult result, string field) => result.Diagnostics.Fields[field];

    public static bool Has(this LiveAdapterResult result, LiveAdapterEvent e) => result.Diagnostics.Events.Contains(e);

    public static IEnumerable<FieldProvenance> Provenances(CommunityOverlaySnapshot s)
    {
        yield return s.Client.Provenance;
        yield return s.PlayerName.Provenance;
        yield return s.CurrentTimeMs.Provenance;
        yield return s.Score.Provenance;
        yield return s.Combo.Provenance;
        yield return s.MaxCombo.Provenance;
        yield return s.Accuracy.Provenance;
        yield return s.Grade.Provenance;
        yield return s.GradeMaxThisPlay.Provenance;
        yield return s.Mods.Provenance;
        yield return s.HealthBar.Provenance;
        yield return s.Keys.Provenance;
        foreach (var h in new[] { s.Hits.Great, s.Hits.Ok, s.Hits.Meh, s.Hits.Miss, s.Hits.Geki, s.Hits.Katu, s.Hits.SliderBreaks })
            yield return h.Provenance;
        yield return s.Pp.Current.Provenance;
        yield return s.Pp.IfFullCombo.Provenance;
        yield return s.Pp.MaxAchieved.Provenance;
        foreach (var v in s.Pp.ByAccuracy.Values) yield return v.Provenance;
        if (s.Beatmap is { } b)
        {
            yield return b.BeatmapId.Provenance;
            yield return b.Title.Provenance;
            yield return b.Stars.Provenance;
            yield return b.Ar.Converted.Provenance;
            yield return b.BpmCommon.Provenance;
            yield return b.BpmRealtime.Provenance;
            yield return b.AudioLengthMs.Provenance;
        }
    }
}
