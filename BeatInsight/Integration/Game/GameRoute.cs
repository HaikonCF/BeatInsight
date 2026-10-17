namespace BeatInsight.Integration.Game;

public enum GameRoute
{
    Unknown,
    SongSelect,
    Gameplay,
    Results
}

public sealed record GameRouteSnapshot(
    GameRoute Route,
    DateTimeOffset ObservedAtUtc,
    string? ProcessIdentity)
{
    internal static GameRouteSnapshot Unknown(
        DateTimeOffset observedAtUtc,
        string? processIdentity = null) =>
        new(GameRoute.Unknown, observedAtUtc, processIdentity);
}

public interface IGameRouteProvider
{
    GameRouteSnapshot ReadGameRoute();
}
