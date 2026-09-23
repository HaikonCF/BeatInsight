using BeatInsight.Diagnostics;
using System.Windows;

namespace BeatInsight;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DebugLogger.Initialize();
        GlobalExceptionHandler.Register(this);
        DebugLogger.Info($"{ReleaseInfo.DisplayName} starting.");
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DebugLogger.Info(
            $"BeatInsight exiting with code {e.ApplicationExitCode}.");
        base.OnExit(e);
    }
}
