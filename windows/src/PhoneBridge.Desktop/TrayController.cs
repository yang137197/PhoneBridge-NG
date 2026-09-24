using System.Drawing;
using Forms = System.Windows.Forms;

namespace PhoneBridge.Desktop;

internal sealed class TrayController : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly Forms.ContextMenuStrip menu;
    private readonly Icon? applicationIcon;
    internal event Action? OpenRequested;
    internal event Action? ExitRequested;

    internal TrayController()
    {
        var open = new Forms.ToolStripMenuItem(TextCatalog.Get("TrayOpen"));
        var exit = new Forms.ToolStripMenuItem(TextCatalog.Get("TrayExit"));
        open.Click += (_, _) => OpenRequested?.Invoke();
        exit.Click += (_, _) => ExitRequested?.Invoke();
        menu = new Forms.ContextMenuStrip();
        menu.Items.Add(open);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exit);
        applicationIcon = Environment.ProcessPath is { } processPath ? Icon.ExtractAssociatedIcon(processPath) : null;
        icon = new Forms.NotifyIcon { ContextMenuStrip = menu, Visible = true };
        icon.MouseDoubleClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) OpenRequested?.Invoke(); };
        SetStatus(TrayStatus.Offline);
    }

    internal void SetStatus(TrayStatus status)
    {
        icon.Icon = status switch
        {
            TrayStatus.Discovered => SystemIcons.Question,
            TrayStatus.Connecting => SystemIcons.Warning,
            TrayStatus.Mounted => SystemIcons.Shield,
            TrayStatus.Error => SystemIcons.Error,
            _ => applicationIcon ?? SystemIcons.Application
        };
        icon.Text = TextCatalog.Get("Tray" + status);
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
        applicationIcon?.Dispose();
        menu.Dispose();
    }
}
