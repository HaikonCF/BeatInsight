using BeatInsight.Services.Settings;

namespace BeatInsight.ViewModels;

public sealed class WindowLifecycleSettingsViewModel : ViewModelBase
{
    private bool closeToTray = WindowLifecycleSettings.Default.CloseToTray;
    private bool startMinimized = WindowLifecycleSettings.Default.StartMinimized;
    private bool startWithWindows;
    private bool applying;

    public event Action<WindowLifecycleSettings>? SettingsChanged;
    public event Action<bool>? StartWithWindowsChangeRequested;

    public bool CloseToTray
    {
        get => closeToTray;
        set
        {
            if (!SetProperty(ref closeToTray, value) || applying)
                return;

            SettingsChanged?.Invoke(Current);
        }
    }

    public bool StartMinimized
    {
        get => startMinimized;
        set
        {
            if (!SetProperty(ref startMinimized, value) || applying)
                return;

            SettingsChanged?.Invoke(Current);
        }
    }

    public bool StartWithWindows
    {
        get => startWithWindows;
        set
        {
            if (!SetProperty(ref startWithWindows, value) || applying)
                return;

            StartWithWindowsChangeRequested?.Invoke(value);
        }
    }

    public WindowLifecycleSettings Current => new(
        WindowLifecycleSettings.CurrentVersion,
        closeToTray,
        startMinimized);

    public void Apply(WindowLifecycleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        applying = true;
        try
        {
            CloseToTray = settings.CloseToTray;
            StartMinimized = settings.StartMinimized;
        }
        finally
        {
            applying = false;
        }
    }

    public void ApplyStartupRegistration(bool enabled)
    {
        applying = true;
        try
        {
            StartWithWindows = enabled;
        }
        finally
        {
            applying = false;
        }
    }
}
