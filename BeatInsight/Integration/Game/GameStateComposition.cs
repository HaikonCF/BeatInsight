namespace BeatInsight.Integration.Game;

public static class GameStateComposition
{
    public static GameStateService CreateDefault() =>
        new(CreateDefaultProviders());

    internal static IReadOnlyList<IGameStateProvider> CreateDefaultProviders() =>
        [new NativeStableGameProvider(), new LazerGameProvider()];

    internal static GameStateService Create(IGameStateProvider nativeStable) =>
        new([nativeStable]);
}
