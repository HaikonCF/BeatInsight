namespace BeatInsight.CommunityOverlays.Poc.OverlayVisibility;

/// <summary>Raisons pour lesquelles un overlay n'est pas visible à l'écran.</summary>
[Flags]
public enum OverlayHiddenReason
{
    None = 0,
    /// <summary>Fenêtre de l'overlay réduite.</summary>
    Minimized = 1,
    /// <summary>Overlay désactivé explicitement (réglage utilisateur ou hôte).</summary>
    Disabled = 2,
    /// <summary>Une application DirectX est en plein écran exclusif : Windows ne compose plus l'overlay.</summary>
    ExclusiveFullscreen = 4,
}

/// <summary>
/// Décide quand suspendre et reprendre un overlay (O1, V3.14-COM-06), sans dépendance WPF/WebView2.
/// <list type="bullet">
/// <item>La perte de focus et l'occlusion ne sont jamais des raisons de suspendre : un overlay
/// transparent au-dessus du jeu reste visible quand une autre fenêtre est active.</item>
/// <item>Suspension seulement après <see cref="SuspendDelay"/> d'invisibilité continue
/// (évite les oscillations, ex. Alt+Tab rapide).</item>
/// <item>Reprise immédiate dès que l'overlay redevient visible (réactivité).</item>
/// </list>
/// </summary>
public sealed class OverlayVisibilityPolicy
{
    private TimeSpan? _hiddenSince;

    public OverlayVisibilityPolicy(TimeSpan? suspendDelay = null)
    {
        SuspendDelay = suspendDelay ?? TimeSpan.FromSeconds(1);
    }

    public TimeSpan SuspendDelay { get; }

    public bool IsSuspended { get; private set; }

    /// <summary>
    /// Évalue l'état à l'instant <paramref name="now"/> (horloge monotone).
    /// Retourne true si l'overlay doit changer d'état (suspendre ou reprendre).
    /// </summary>
    public bool Update(TimeSpan now, OverlayHiddenReason hidden)
    {
        if (hidden == OverlayHiddenReason.None)
        {
            _hiddenSince = null;
            if (!IsSuspended) return false;
            IsSuspended = false;
            return true;
        }

        _hiddenSince ??= now;
        if (IsSuspended || now - _hiddenSince.Value < SuspendDelay)
            return false;

        IsSuspended = true;
        return true;
    }
}
