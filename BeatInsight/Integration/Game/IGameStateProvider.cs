namespace BeatInsight.Integration.Game;

public interface IGameStateProvider
{
    string Name { get; }

    GameStateSnapshot Current { get; }

    GameStateSnapshot Refresh();
}
