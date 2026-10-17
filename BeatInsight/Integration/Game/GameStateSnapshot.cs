using System.Collections.Immutable;

namespace BeatInsight.Integration.Game;

public enum GameStateAvailability
{
    Available,
    NoProcess,
    NoBeatmap,
    Ambiguous,
    InvalidData,
    ProviderUnavailable,
    Error
}

public sealed record ProviderAttempt(
    string ProviderName,
    GameStateAvailability Availability,
    string? DiagnosticReason,
    string? ClientName = null,
    int? ProcessId = null,
    string? ProcessIdentity = null);

public sealed record GameStateSnapshot
{
    public GameStateSnapshot(
        string providerName,
        string? clientName,
        int? processId,
        string? processIdentity,
        string? currentBeatmapPath,
        string? currentBeatmapId,
        string? currentBeatmapSetId,
        DateTimeOffset lastUpdateUtc,
        GameStateAvailability availability,
        string? diagnosticReason)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new ArgumentException("A provider name is required.", nameof(providerName));
        }

        ProviderName = providerName;
        ClientName = clientName;
        ProcessId = processId;
        ProcessIdentity = processIdentity;
        CurrentBeatmapPath = currentBeatmapPath;
        CurrentBeatmapId = currentBeatmapId;
        CurrentBeatmapSetId = currentBeatmapSetId;
        LastUpdateUtc = lastUpdateUtc;
        Availability = availability;
        DiagnosticReason = diagnosticReason;
    }

    public bool IsAvailable => Availability == GameStateAvailability.Available;

    public string ProviderName { get; }

    public string? ClientName { get; }

    public int? ProcessId { get; }

    public string? ProcessIdentity { get; }

    public string? CurrentBeatmapPath { get; }

    public string? CurrentBeatmapId { get; }

    public string? CurrentBeatmapSetId { get; }

    public DateTimeOffset LastUpdateUtc { get; }

    public GameStateAvailability Availability { get; }

    public string? DiagnosticReason { get; }

    /// <summary>
    /// Root Songs directory reported or derived by the selected provider.
    /// MainWindow uses it only as the existing library-folder fallback.
    /// </summary>
    public string? SongsDirectory { get; init; }

    /// <summary>
    /// Fully resolved existing background image for the selected beatmap.
    /// Keeping this provider-neutral path prevents provider-specific details
    /// from leaking into MainWindow.
    /// </summary>
    public string? CurrentBackgroundPath { get; init; }

    public ImmutableArray<ProviderAttempt> ProviderAttempts { get; init; } = [];
}
