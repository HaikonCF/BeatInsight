namespace BeatInsight.Models.Ml;

/// <summary>
/// Instantané de progression d'un backfill ML. Les compteurs décrivent
/// uniquement le dataset ML ; ils ne reflètent jamais le cache runtime.
/// </summary>
internal sealed class MlDatasetBuildProgress
{
    internal required int TotalFiles { get; init; }
    internal required int ProcessedFiles { get; init; }
    internal required int CapturedFiles { get; init; }
    internal required int DatasetUpToDateFiles { get; init; }
    internal required int UnsupportedFiles { get; init; }
    internal required int FailedFiles { get; init; }
    internal string? CurrentFile { get; init; }
    internal required double Percent { get; init; }
}

/// <summary>
/// Résultat final d'un backfill ML. En cas d'annulation, les samples déjà
/// capturés restent persistés ; aucun rollback n'est tenté.
/// </summary>
internal sealed class MlDatasetBuildResult
{
    internal required int TotalFiles { get; init; }
    internal required int ProcessedFiles { get; init; }
    internal required int CapturedFiles { get; init; }
    internal required int DatasetUpToDateFiles { get; init; }
    internal required int UnsupportedFiles { get; init; }
    internal required int FailedFiles { get; init; }
    internal required bool WasCancelled { get; init; }
    internal required TimeSpan Elapsed { get; init; }
}
