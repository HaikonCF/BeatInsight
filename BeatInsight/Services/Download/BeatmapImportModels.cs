namespace BeatInsight.Services.Download;

internal enum BeatmapImportOutcome
{
    /// <summary>Le fichier a été remis au shell ; l'import osu! n'est pas confirmé.</summary>
    LaunchedWaitingForConfirmation,

    /// <summary>Le BeatmapId ciblé est apparu dans l'index local.</summary>
    Confirmed,

    ImportLaunchFailed,

    /// <summary>Le délai borné s'est écoulé sans confirmation.</summary>
    ImportNotConfirmed,

    Cancelled,
}

internal sealed record BeatmapImportResult(
    BeatmapImportOutcome Outcome,
    string? FailureReason = null);
