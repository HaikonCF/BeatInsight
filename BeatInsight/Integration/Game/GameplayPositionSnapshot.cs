namespace BeatInsight.Integration.Game;

/// <summary>
/// Provider-neutral, analysis-independent gameplay position.
/// A null CurrentTime means that no validated active gameplay clock is available.
/// </summary>
public sealed record GameplayPositionSnapshot(
    TimeSpan? CurrentTime,
    DateTimeOffset ObservedAtUtc,
    string? ProcessIdentity)
{
    public bool IsGameplay => CurrentTime.HasValue;

    internal static GameplayPositionSnapshot Unavailable(
        DateTimeOffset observedAtUtc,
        string? processIdentity = null) =>
        new(null, observedAtUtc, processIdentity);
}

public interface IGameplayPositionProvider
{
    GameplayPositionSnapshot ReadGameplayPosition();
}
