using System.Globalization;

namespace BeatInsight.Overlay.GameplayHud;

public sealed record LiveGameplayStatsOverlayState(
    bool HasContent,
    string Miss,
    string Count100,
    string Count50,
    string Combo,
    string Accuracy,
    string Mods)
{
    public static LiveGameplayStatsOverlayState Empty { get; } = new(
        false, "—", "—", "—", "—", "—", "—");

    public static LiveGameplayStatsOverlayState Project(OverlaySnapshot? snapshot)
    {
        if (snapshot is not { GameMode: OverlayGameState.Gameplay, LiveStats: { } stats })
            return Empty;

        string mods = stats.Mods.IsDefaultOrEmpty
            ? "NM"
            : string.Join("", stats.Mods.Select(item => item.Acronym));
        return new(
            true,
            stats.CountMiss.ToString(CultureInfo.InvariantCulture),
            stats.Count100.ToString(CultureInfo.InvariantCulture),
            stats.Count50.ToString(CultureInfo.InvariantCulture),
            string.Create(CultureInfo.InvariantCulture, $"{stats.CurrentCombo}x"),
            string.Create(CultureInfo.InvariantCulture, $"{stats.Accuracy * 100:F2}%"),
            mods);
    }
}

public sealed class LiveGameplayStatsOverlayViewModel : System.ComponentModel.INotifyPropertyChanged
{
    private string miss = "—";
    private string count100 = "—";
    private string count50 = "—";
    private string combo = "—";
    private string accuracy = "—";
    private string mods = "—";

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public string Miss { get => miss; private set => Set(ref miss, value); }
    public string Count100 { get => count100; private set => Set(ref count100, value); }
    public string Count50 { get => count50; private set => Set(ref count50, value); }
    public string Combo { get => combo; private set => Set(ref combo, value); }
    public string Accuracy { get => accuracy; private set => Set(ref accuracy, value); }
    public string Mods { get => mods; private set => Set(ref mods, value); }

    public void Apply(LiveGameplayStatsOverlayState state)
    {
        Miss = state.Miss; Count100 = state.Count100; Count50 = state.Count50;
        Combo = state.Combo; Accuracy = state.Accuracy; Mods = state.Mods;
    }

    private void Set(ref string field, string value,
        [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new(name));
    }
}
