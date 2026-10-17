using System.Runtime.InteropServices;

namespace BeatInsight.Overlay;

/// <summary>
/// BUG-003 : notification immédiate d'un changement de fenêtre au premier plan (Alt+Tab, clic, retour sur osu!),
/// pour réévaluer la règle de premier plan sans attendre le prochain tick de polling.
/// Hook hors contexte (<c>WINEVENT_OUTOFCONTEXT</c>) : rappel sur le thread qui l'installe (thread UI),
/// aucune injection dans les autres processus, aucun effet sur leurs fenêtres.
/// </summary>
internal sealed class ForegroundWindowChangedHook : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000;

    private readonly WinEventCallback callback; // gardé vivant tant que le hook existe
    private IntPtr handle;

    public ForegroundWindowChangedHook(Action changed)
    {
        callback = (_, _, _, _, _, _, _) => changed();
        handle = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            IntPtr.Zero,
            callback,
            0,
            0,
            WinEventOutOfContext);
    }

    public bool IsActive => handle != IntPtr.Zero;

    public void Dispose()
    {
        if (handle == IntPtr.Zero)
            return;
        _ = UnhookWinEvent(handle);
        handle = IntPtr.Zero;
    }

    private delegate void WinEventCallback(
        IntPtr hook,
        uint eventType,
        IntPtr windowHandle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr module,
        WinEventCallback callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);
}
