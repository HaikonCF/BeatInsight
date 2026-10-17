using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BeatInsight.CommunityOverlays.Poc.Managed;

/// <summary>
/// FEAT-003.2 : click-through des overlays gérés, sans effet en mode CLI autonome.
/// <list type="bullet">
/// <item>Passif : <c>WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE</c>. Le hit-test Windows ignore toute la fenêtre
/// (WebView2 compris) : le survol ne change plus le curseur et osu! reçoit tous les clics.</item>
/// <item>Édition : <c>WS_EX_TRANSPARENT</c> et <c>WS_EX_NOACTIVATE</c> retirés, la fenêtre redevient déplaçable et redimensionnable.</item>
/// </list>
/// Seul le style étendu de la fenêtre change : ni <c>ShowCursor</c>, ni curseurs système, ni osu!.
/// </summary>
public static class ManagedWindowInteraction
{
    private const int GwlExStyle = -20;
    private const int WmStyleChanging = 0x007C;
    public const int WsExTransparent = 0x00000020;
    public const int WsExLayered = 0x00080000;
    public const int WsExNoActivate = 0x08000000;

    /// <summary>Styles étendus attendus pour le mode demandé, les autres bits restant inchangés.</summary>
    public static int ExtendedStylesFor(int styles, bool editing) => editing
        ? styles & ~(WsExTransparent | WsExNoActivate)
        : styles | WsExTransparent | WsExLayered | WsExNoActivate;

    /// <summary>
    /// Raccorde la fenêtre : mode courant appliqué dès la création du handle natif, puis maintenu si WPF réécrit
    /// le style étendu (changement de ResizeMode, Topmost...). Le hook disparaît avec le handle à la fermeture.
    /// </summary>
    public static void Attach(Window window, Func<bool> isEditing)
    {
        window.SourceInitialized += (_, _) =>
        {
            if (PresentationSource.FromVisual(window) is HwndSource source)
                source.AddHook((IntPtr _, int msg, IntPtr wParam, IntPtr lParam, ref bool _) =>
                {
                    if (msg == WmStyleChanging && wParam.ToInt32() == GwlExStyle)
                    {
                        // STYLESTRUCT { DWORD styleOld; DWORD styleNew; }
                        int requested = Marshal.ReadInt32(lParam, 4);
                        Marshal.WriteInt32(lParam, 4, ExtendedStylesFor(requested, isEditing()));
                    }
                    return IntPtr.Zero;
                });
            Apply(window, isEditing());
        };
    }

    /// <summary>Applique le mode à la fenêtre. Sans handle natif (avant SourceInitialized ou après fermeture), ne fait rien.</summary>
    public static void Apply(Window window, bool editing)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        int styles = GetWindowLong(handle, GwlExStyle);
        int updated = ExtendedStylesFor(styles, editing);
        if (updated != styles)
            _ = SetWindowLong(handle, GwlExStyle, updated);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr windowHandle, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr windowHandle, int index, int newStyle);
}
