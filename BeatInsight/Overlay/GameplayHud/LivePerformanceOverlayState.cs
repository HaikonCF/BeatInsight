using System.Globalization;

namespace BeatInsight.Overlay.GameplayHud;

public sealed record LivePerformanceOverlayState(
    bool HasContent,
    string Current,
    string Fc,
    string Accuracy95Fc,
    string Ss)
{
    public static LivePerformanceOverlayState Empty { get; } = new(false, "—", "—", "—", "—");

    public static LivePerformanceOverlayState Project(OverlaySnapshot? snapshot)
    {
        if (snapshot is not { Performance: { } performance } ||
            snapshot.GameMode is not (OverlayGameState.Gameplay or OverlayGameState.Results))
            return Empty;

        return new(
            true,
            Format(performance.CurrentPp),
            Format(performance.FcPp),
            Format(performance.Accuracy95FcPp),
            Format(performance.SsPp));
    }

    private static string Format(double value) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Round(value):F0}pp");
}

public sealed class LivePerformanceOverlayViewModel : System.ComponentModel.INotifyPropertyChanged
{
    private string current = "—";
    private string fc = "—";
    private string accuracy95Fc = "—";
    private string ss = "—";

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public string Current { get => current; private set => Set(ref current, value); }
    public string Fc { get => fc; private set => Set(ref fc, value); }
    public string Accuracy95Fc { get => accuracy95Fc; private set => Set(ref accuracy95Fc, value); }
    public string Ss { get => ss; private set => Set(ref ss, value); }

    public void Apply(LivePerformanceOverlayState state)
    {
        Current = state.Current;
        Fc = state.Fc;
        Accuracy95Fc = state.Accuracy95Fc;
        Ss = state.Ss;
    }

    private void Set(ref string field, string value,
        [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new(name));
    }
}
