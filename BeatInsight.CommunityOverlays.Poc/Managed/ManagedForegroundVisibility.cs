using System.Windows;

namespace BeatInsight.CommunityOverlays.Poc.Managed;

/// <summary>
/// BUG-003 : un overlay géré ne recouvre plus les autres applications. Quand BeatInsight signale qu'osu! n'est pas
/// au premier plan (hors édition), la fenêtre devient entièrement transparente ; elle redevient opaque au retour sur osu!.
/// <list type="bullet">
/// <item>Ni <c>Hide</c>/<c>Show</c>, ni changement de <c>Topmost</c> : la fenêtre garde son handle, sa position, sa taille,
/// son click-through et son focus (aucune activation possible au retour).</item>
/// <item>WebView2 et le bridge continuent de tourner : aucune relance, aucun état périmé au retour.</item>
/// </list>
/// Sans effet en mode CLI autonome (aucun hôte géré).
/// </summary>
public static class ManagedForegroundVisibility
{
    public const double VisibleOpacity = 1;
    public const double HiddenOpacity = 0;

    public static void Attach(Window window, ManagedCompanionHost host)
    {
        Apply(window, host.ShouldDisplay);
        host.DisplayChanged += displayed => Apply(window, displayed);
    }

    public static void Apply(Window window, bool displayed) =>
        window.Opacity = displayed ? VisibleOpacity : HiddenOpacity;
}
