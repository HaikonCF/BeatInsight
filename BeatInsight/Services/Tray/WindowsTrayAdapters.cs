using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace BeatInsight.Services.Tray;

internal sealed class WpfTrayWindowHost(Window window) : ITrayWindowHost
{
    public WindowState WindowState
    {
        get => window.WindowState;
        set => window.WindowState = value;
    }

    public bool IsVisible => window.IsVisible;

    public bool ShowInTaskbar
    {
        get => window.ShowInTaskbar;
        set => window.ShowInTaskbar = value;
    }

    public void Show() => window.Show();
    public void Hide() => window.Hide();
    public bool Activate() => window.Activate();
    public void Close() => window.Close();
}

internal sealed class BeatInsightTrayIcon : ITrayIconLifetime
{
    private readonly Forms.NotifyIcon notifyIcon;
    private readonly Forms.ContextMenuStrip contextMenu;
    private readonly Icon? ownedIcon;
    private bool disposed;

    public BeatInsightTrayIcon()
    {
        ownedIcon = LoadApplicationIcon();
        var openItem = new Forms.ToolStripMenuItem("Open BeatInsight");
        var quitItem = new Forms.ToolStripMenuItem("Quit");
        openItem.Click += OpenItem_Click;
        quitItem.Click += QuitItem_Click;
        contextMenu = new Forms.ContextMenuStrip();
        contextMenu.Items.Add(openItem);
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add(quitItem);
        notifyIcon = new Forms.NotifyIcon
        {
            Text = "BeatInsight",
            Icon = ownedIcon ?? SystemIcons.Application,
            ContextMenuStrip = contextMenu
        };
        notifyIcon.DoubleClick += NotifyIcon_DoubleClick;
    }

    public event EventHandler? OpenRequested;
    public event EventHandler? QuitRequested;

    public bool Visible
    {
        get => notifyIcon.Visible;
        set => notifyIcon.Visible = value;
    }

    public void ShowFirstMinimizeNotification()
    {
        notifyIcon.BalloonTipTitle = "BeatInsight";
        notifyIcon.BalloonTipText = "BeatInsight is still running in the system tray.";
        notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        notifyIcon.ShowBalloonTip(3000);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        notifyIcon.DoubleClick -= NotifyIcon_DoubleClick;
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        contextMenu.Dispose();
        ownedIcon?.Dispose();
    }

    private static Icon? LoadApplicationIcon()
    {
        try
        {
            string? path = Environment.ProcessPath;
            return string.IsNullOrWhiteSpace(path) ? null : Icon.ExtractAssociatedIcon(path);
        }
        catch (Exception exception) when (exception is ArgumentException or System.IO.FileNotFoundException)
        {
            return null;
        }
    }

    private void NotifyIcon_DoubleClick(object? sender, EventArgs e) => OpenRequested?.Invoke(this, EventArgs.Empty);
    private void OpenItem_Click(object? sender, EventArgs e) => OpenRequested?.Invoke(this, EventArgs.Empty);
    private void QuitItem_Click(object? sender, EventArgs e) => QuitRequested?.Invoke(this, EventArgs.Empty);
}
