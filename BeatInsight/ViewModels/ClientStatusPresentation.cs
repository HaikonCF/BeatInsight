using BeatInsight.Integration.Game;

namespace BeatInsight.ViewModels;

public enum ClientStatusKind
{
    Disconnected,
    Detecting,
    Connected
}

public sealed record ClientStatusPresentation(
    string Text,
    string CompactText,
    ClientStatusKind Kind,
    string EmptyStateTitle,
    string EmptyStateDescription)
{
    private const string NoClientText = "No osu! client detected";
    private const string NoBeatmapText = "No beatmap selected";

    public static ClientStatusPresentation From(GameStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        ClientCandidate? candidate = FindCandidate(snapshot);
        if (candidate is null)
        {
            return new(
                NoClientText,
                "● No client",
                ClientStatusKind.Disconnected,
                NoClientText,
                "Start a supported osu! client to begin.");
        }

        string displayName = ResolveDisplayName(
            candidate.ProviderName,
            candidate.ClientName);
        if (candidate.IsDetecting)
        {
            string text = $"Detecting {displayName}…";
            return new(
                text,
                $"● {displayName} · detecting",
                ClientStatusKind.Detecting,
                text,
                "BeatInsight is connecting to the detected client.");
        }

        return new(
            $"Connected to {displayName}",
            $"● {displayName}",
            ClientStatusKind.Connected,
            NoBeatmapText,
            "Select or play a beatmap to open its Overview.");
    }

    private static ClientCandidate? FindCandidate(GameStateSnapshot snapshot)
    {
        if (snapshot.Availability == GameStateAvailability.Available)
        {
            return new(
                snapshot.ProviderName,
                snapshot.ClientName,
                IsDetecting(snapshot.Availability, snapshot.DiagnosticReason),
                ClientPresent: true);
        }

        if (!snapshot.ProviderAttempts.IsDefaultOrEmpty)
        {
            foreach (ProviderAttempt attempt in snapshot.ProviderAttempts)
            {
                ClientCandidate candidate = new(
                    attempt.ProviderName,
                    attempt.ClientName,
                    IsDetecting(attempt.Availability, attempt.DiagnosticReason),
                    IsClientPresent(
                        attempt.Availability,
                        attempt.ProcessId,
                        attempt.ProcessIdentity));
                if (candidate.IsDetecting || candidate.ClientPresent)
                {
                    return candidate;
                }
            }

            return null;
        }

        bool detecting = IsDetecting(
            snapshot.Availability,
            snapshot.DiagnosticReason);
        bool clientPresent = IsClientPresent(
            snapshot.Availability,
            snapshot.ProcessId,
            snapshot.ProcessIdentity);
        return detecting || clientPresent
            ? new(
                snapshot.ProviderName,
                snapshot.ClientName,
                detecting,
                clientPresent)
            : null;
    }

    private static bool IsClientPresent(
        GameStateAvailability availability,
        int? processId,
        string? processIdentity) =>
        availability is GameStateAvailability.Available
            or GameStateAvailability.NoBeatmap
            or GameStateAvailability.Ambiguous
            or GameStateAvailability.InvalidData
        || processId.HasValue
        || !string.IsNullOrWhiteSpace(processIdentity);

    private static bool IsDetecting(
        GameStateAvailability availability,
        string? diagnosticReason) =>
        availability == GameStateAvailability.ProviderUnavailable
        && diagnosticReason?.Contains(
            "discovery in progress",
            StringComparison.OrdinalIgnoreCase) == true;

    private static string ResolveDisplayName(
        string providerName,
        string? clientName)
    {
        if (!string.IsNullOrWhiteSpace(clientName))
        {
            return clientName.Trim();
        }

        return providerName switch
        {
            NativeStableGameProvider.ProviderName => "osu!stable",
            LazerGameProvider.ProviderName => "osu!lazer",
            _ when !string.IsNullOrWhiteSpace(providerName) => providerName.Trim(),
            _ => "osu! client"
        };
    }

    private sealed record ClientCandidate(
        string ProviderName,
        string? ClientName,
        bool IsDetecting,
        bool ClientPresent);
}
