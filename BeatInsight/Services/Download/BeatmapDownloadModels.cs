namespace BeatInsight.Services.Download;

/// <summary>
/// Résultat typé exposé à l'appelant d'un téléchargement de beatmapset.
/// Remplace le couple bool/string : chaque échec a une cause explicite,
/// jamais un message générique à parser.
/// </summary>
internal enum BeatmapDownloadOutcome
{
    /// <summary>Un fichier .osz valide est disponible localement.</summary>
    Success,

    /// <summary>
    /// La page/le point de téléchargement a été ouvert dans le navigateur
    /// par défaut de l'utilisateur : aucun octet n'a transité par
    /// BeatInsight. C'est le mode de repli lorsqu'aucun téléchargement
    /// programmatique sûr n'est disponible avec l'authentification
    /// actuelle (voir <c>BrowserOpenBeatmapDownloadProvider</c>).
    /// </summary>
    BrowserFallbackOpened,

    RateLimited,
    AuthenticationRequired,
    ProviderUnavailable,
    InvalidDownloadedFile,
    Cancelled,
    Failed,
}

/// <summary>
/// Résultat final d'un <see cref="BeatmapDownloadService"/>. Ne contient
/// jamais de jeton, cookie ou URL authentifiée : seulement des faits sans
/// secret (identifiants, octets, durée, raison typée).
/// </summary>
internal sealed record BeatmapDownloadResult(
    BeatmapDownloadOutcome Outcome,
    string? LocalOszFilePath = null,
    long BytesDownloaded = 0,
    TimeSpan Elapsed = default,
    string? FailureReason = null)
{
    internal static BeatmapDownloadResult Success(
        string localOszFilePath,
        long bytesDownloaded,
        TimeSpan elapsed) => new(
            BeatmapDownloadOutcome.Success,
            localOszFilePath,
            bytesDownloaded,
            elapsed);

    internal static BeatmapDownloadResult BrowserFallback(
        TimeSpan elapsed) => new(
            BeatmapDownloadOutcome.BrowserFallbackOpened,
            Elapsed: elapsed);

    internal static BeatmapDownloadResult Failure(
        BeatmapDownloadOutcome outcome,
        TimeSpan elapsed,
        string? failureReason = null) => new(
            outcome,
            Elapsed: elapsed,
            FailureReason: failureReason);
}

/// <summary>
/// Sortie brute d'un <see cref="IBeatmapDownloadProvider"/>, avant la
/// gestion du fichier temporaire/validation par
/// <see cref="BeatmapDownloadService"/>.
/// </summary>
internal enum BeatmapDownloadProviderOutcome
{
    /// <summary>Des octets ont été écrits dans le flux de destination.</summary>
    BytesWritten,

    /// <summary>
    /// Le provider n'écrit jamais d'octets : il a délégué l'action au
    /// navigateur de l'utilisateur.
    /// </summary>
    BrowserFallbackOpened,

    RateLimited,
    AuthenticationRequired,
    ProviderUnavailable,
    Cancelled,
    Failed,
}

internal sealed record BeatmapDownloadProviderResult(
    BeatmapDownloadProviderOutcome Outcome,
    long BytesWritten = 0,
    string? FailureReason = null)
{
    internal static BeatmapDownloadProviderResult BytesWrittenResult(
        long bytesWritten) => new(
            BeatmapDownloadProviderOutcome.BytesWritten,
            bytesWritten);

    internal static readonly BeatmapDownloadProviderResult BrowserFallback =
        new(BeatmapDownloadProviderOutcome.BrowserFallbackOpened);

    internal static BeatmapDownloadProviderResult Failure(
        BeatmapDownloadProviderOutcome outcome,
        string? failureReason = null) => new(outcome, FailureReason: failureReason);
}
