namespace BeatInsight.CommunityOverlays.Poc.Adapter.Live;

// Entrées de l'adaptateur live (V3.14-COM-09). Ce sont des copies champ à champ des
// snapshots BeatInsight (Integration/Game, Models/AnalysisCache), sans référence au
// projet principal : le POC reste compilable seul, quel que soit l'état du worktree
// principal, et le build de BeatInsight n'est jamais déclenché par le POC.
// Le raccordement réel (étape B) se résume à recopier ces champs depuis
// OverlayEngine.CurrentSnapshot et GameStateService.

/// <summary>Copie de <c>GameRoute</c>.</summary>
public enum SourceRoute
{
    Unknown,
    SongSelect,
    Gameplay,
    Results,
}

/// <summary>Copie de <c>GameStateAvailability</c>.</summary>
public enum SourceAvailability
{
    Available,
    NoProcess,
    NoBeatmap,
    Ambiguous,
    InvalidData,
    ProviderUnavailable,
    Error,
}

/// <summary>
/// Raisons connues pour lesquelles BeatInsight ne rafraîchit plus ses snapshots
/// (la boucle 500 ms est sautée ou l'overlay engine ne publie plus).
/// </summary>
public enum SourceSuspension
{
    None,

    /// <summary><c>OverlayEngine.IsOverlayEditMode</c> : <c>CurrentSnapshot</c> n'est plus mis à jour.</summary>
    OverlayLayoutEdit,

    /// <summary>Mode Fast Labeling : <c>UpdateMap</c> sort immédiatement.</summary>
    FastLabeling,

    /// <summary>Scan de bibliothèque ou backfill ML : les ticks sont sautés.</summary>
    BackgroundLibraryWork,
}

/// <summary>Copie de <c>GameStateSnapshot</c> (champs utiles à l'adaptateur).</summary>
public sealed record SourceGameState(
    string ProviderName,
    string? ClientName,
    string? ProcessIdentity,
    string? CurrentBeatmapPath,
    string? CurrentBeatmapId,
    string? CurrentBeatmapSetId,
    DateTimeOffset LastUpdateUtc,
    SourceAvailability Availability,
    string? SongsDirectory = null,
    string? CurrentBackgroundPath = null);

/// <summary>Copie de <c>GameRouteSnapshot</c>.</summary>
public sealed record SourceGameRoute(SourceRoute Route, DateTimeOffset ObservedAtUtc, string? ProcessIdentity);

/// <summary>
/// Copie de <c>BeatmapMetadataSnapshot</c>, avec le chemin de la beatmap analysée
/// (<c>OverlaySnapshot.Game.CurrentBeatmapPath</c> au moment de la publication de l'analyse).
/// </summary>
public sealed record SourceBeatmapMetadata(
    string BeatmapPath,
    int? BeatmapId,
    int? BeatmapSetId,
    string Artist,
    string Title,
    string Version,
    string Creator,
    long LengthTicks,
    int Bpm,
    int MaxCombo,
    double ApproachRate,
    double OverallDifficulty,
    double CircleSize,
    double HpDrain,
    int CircleCount,
    int SliderCount,
    int SpinnerCount,
    double OsuStarRating,
    double? PrimaryBpm = null,
    double? MinBpm = null,
    double? MaxBpm = null,
    int? OsuMaxCombo = null)
{
    public int TotalObjects => CircleCount + SliderCount + SpinnerCount;
}

/// <summary>Copie de <c>LiveGameplaySnapshot</c> (sans l'analyse, déjà fournie par les métadonnées).</summary>
public sealed record SourceLiveGameplay(
    bool IsGameplay,
    TimeSpan CurrentTime,
    TimeSpan BeatmapDuration,
    string? BeatmapId,
    string? BeatmapSetId,
    string ProcessIdentity,
    DateTimeOffset ObservedAtUtc);

/// <summary>Copie de <c>LiveGameplayStatsSnapshot</c> (précision 0–1, mods en acronymes).</summary>
public sealed record SourceLiveStats(
    string ProcessIdentity,
    string? BeatmapId,
    string? BeatmapSetId,
    TimeSpan CurrentTime,
    int CurrentCombo,
    int MaxComboReached,
    int Count300,
    int Count100,
    int Count50,
    int CountMiss,
    double Accuracy,
    IReadOnlyList<string> Mods,
    DateTimeOffset ObservedAtUtc,
    int? Score = null)
{
    public int JudgedObjects => Count300 + Count100 + Count50 + CountMiss;
}

/// <summary>Copie de <c>LivePerformanceSnapshot</c>. L'adaptateur ne recalcule jamais de PP.</summary>
public sealed record SourcePerformance(
    double CurrentPp,
    double FcPp,
    double Accuracy95FcPp,
    double SsPp,
    int PassedObjects,
    DateTimeOffset ObservedAtUtc);

/// <summary>
/// Identité du seul fichier .osu courant (V3.14-COM-10), lue en lecture seule par BeatInsight :
/// MD5 réel et champs d'en-tête. La durée audio vient de <see cref="SourceAudioFile"/> (COM-11).
/// </summary>
public sealed record SourceOsuFile(
    string BeatmapPath,
    string Md5,
    string? AudioFilename,
    string? TitleUnicode,
    string? ArtistUnicode);

/// <summary>
/// Durée réelle du fichier audio de la beatmap courante (V3.14-COM-11), lue par BeatInsight dans
/// le fichier <c>AudioFilename</c>. <see cref="DurationMs"/> est nulle tant que la durée n'est pas
/// connue avec certitude (lecture en cours, fichier absent, illisible ou format non pris en charge).
/// </summary>
public sealed record SourceAudioFile(
    string BeatmapPath,
    string AudioFilename,
    double? DurationMs,
    string Status);

/// <summary>
/// Tout ce que l'adaptateur reçoit pour un tick. Chaque élément peut manquer :
/// l'absence n'est jamais compensée par une autre source (ni mock, ni valeur précédente
/// hors règles explicites).
/// </summary>
public sealed record BeatInsightLiveInput(
    SourceGameState? Game,
    SourceGameRoute? Route,
    SourceBeatmapMetadata? Metadata = null,
    SourceLiveGameplay? LiveGameplay = null,
    SourceLiveStats? Stats = null,
    SourcePerformance? Performance = null,
    SourceSuspension Suspension = SourceSuspension.None,
    SourceOsuFile? OsuFile = null,
    SourceAudioFile? AudioFile = null);
