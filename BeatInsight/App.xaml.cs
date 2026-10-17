using BeatInsight.Diagnostics;
using BeatInsight.Services.Settings;
using System.Windows;
using Velopack;
using Velopack.Locators;

namespace BeatInsight;

public partial class App : Application
{
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build()
            .SetAutoApplyOnStartup(false)
            .OnBeforeUninstallFastCallback(_ =>
                WindowsStartupRegistrationService.RemoveIfTargetsInstallation(
                    VelopackLocator.Current.RootAppDir))
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        DebugLogger.Initialize();
        GlobalExceptionHandler.Register(this);
        DebugLogger.Info($"{ReleaseInfo.DisplayName} starting.");
        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        if (!window.StartHiddenToTrayIfConfigured())
        {
            DebugLogger.Info("STARTUP | window presentation=visible");
            window.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (MainWindow is BeatInsight.MainWindow window)
            window.BeginShutdown();

        DebugLogger.Info(
            $"BeatInsight exiting with code {e.ApplicationExitCode}.");
        base.OnExit(e);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if (MainWindow is BeatInsight.MainWindow window)
            window.BeginShutdown();

        base.OnSessionEnding(e);
    }
}
