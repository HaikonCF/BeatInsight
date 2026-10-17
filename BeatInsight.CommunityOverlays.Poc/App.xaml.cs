using System.Windows;
using BeatInsight.CommunityOverlays.Poc.Adapter;
using BeatInsight.CommunityOverlays.Poc.Adapter.Live;

namespace BeatInsight.CommunityOverlays.Poc;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var options = PocOptions.Parse(e.Args);

        // FEAT-003 : mode géré uniquement avec --managed-by ; sans lui, rien ne change.
        Managed.ManagedCompanionHost? managed = null;
        if (options.ManagedByPid is not null)
        {
            managed = Managed.ManagedCompanionHost.TryAcquire(
                CommunityOverlayWindow.ProfileNameFor(options.OverlayFolder), Console.Out);
            if (managed is null)
            {
                Shutdown(BeatInsight.Integration.CommunityOverlays.Managed.CommunityOverlayCompanionProtocol.ExitDuplicate);
                return;
            }
            Exit += (_, _) => managed.Dispose();
        }

        // Une seule source : l'absence de données live ne bascule jamais vers le mock (V3.14-COM-09).
        ICommunityOverlayDataSource source = options.Source switch
        {
            "synthetic-live" => new SyntheticLiveCommunityOverlayDataSource(options.BeatmapAssetsRoot, options.PublishTimer),
            "live-pipe" => new PipeLiveCommunityOverlayDataSource(timerMode: options.PublishTimer, selectionPipe: options.SelectionPipe),
            _ => new MockCommunityOverlayDataSource(priority: options.PublishPriority, timerMode: options.PublishTimer),
        };
        var window = new CommunityOverlayWindow(options, source, managed);
        MainWindow = window;
        if (managed is not null)
            managed.Attach(window, options.ManagedByPid!.Value, Console.In);
        // Un overlay ne doit jamais prendre le focus au jeu à son ouverture (V3.14-COM-03).
        window.ShowActivated = false;
        window.Show();
    }
}
