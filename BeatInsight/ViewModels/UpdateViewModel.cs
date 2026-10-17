using BeatInsight.Services.Settings;
using BeatInsight.Services.Updates;

namespace BeatInsight.ViewModels;

public sealed class UpdateViewModel : ViewModelBase
{
    private bool checkAutomatically = UpdateSettings.Default.CheckAutomatically;
    private bool applyingSettings;
    private UpdateState state = UpdateState.Idle;
    private string currentVersion = BeatInsight.Diagnostics.ReleaseInfo.Version;
    private string statusText = "Ready";
    private string? targetVersion;
    private int progress;

    internal event Action<UpdateSettings>? SettingsChanged;

    public bool CheckAutomatically
    {
        get => checkAutomatically;
        set
        {
            if (!SetProperty(ref checkAutomatically, value) || applyingSettings)
                return;

            SettingsChanged?.Invoke(new UpdateSettings(
                UpdateSettings.CurrentVersion,
                value));
        }
    }

    public string CurrentVersion
    {
        get => currentVersion;
        private set => SetProperty(ref currentVersion, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public string? TargetVersion
    {
        get => targetVersion;
        private set => SetProperty(ref targetVersion, value);
    }

    public int Progress
    {
        get => progress;
        private set => SetProperty(ref progress, value);
    }

    public bool CanCheck => state is not UpdateState.Checking
        and not UpdateState.Downloading
        and not UpdateState.Applying;

    public bool CanDownload => state == UpdateState.Available;

    public bool CanRestart => state == UpdateState.ReadyToRestart;

    public bool IsDownloading => state == UpdateState.Downloading;

    public bool ShowDownload => CanDownload;

    public bool ShowRestart => CanRestart;

    public bool IsAvailable => state is UpdateState.Available
        or UpdateState.Downloading
        or UpdateState.ReadyToRestart;

    internal void ApplySettings(UpdateSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        applyingSettings = true;
        try
        {
            CheckAutomatically = settings.CheckAutomatically;
        }
        finally
        {
            applyingSettings = false;
        }
    }

    internal void ApplyStatus(UpdateStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        state = status.State;
        CurrentVersion = status.CurrentVersion;
        TargetVersion = status.TargetVersion;
        Progress = status.Progress;
        StatusText = status.Message ?? "Ready";
        OnPropertyChanged(nameof(CanCheck));
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(CanRestart));
        OnPropertyChanged(nameof(IsDownloading));
        OnPropertyChanged(nameof(ShowDownload));
        OnPropertyChanged(nameof(ShowRestart));
        OnPropertyChanged(nameof(IsAvailable));
    }
}
