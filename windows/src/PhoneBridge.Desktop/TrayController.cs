using System.Drawing;
using Forms = System.Windows.Forms;

namespace PhoneBridge.Desktop;

internal sealed class TrayController : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly Forms.ContextMenuStrip menu;
    private readonly Forms.ToolStripMenuItem open;
    private readonly Forms.ToolStripMenuItem exit;
    private readonly Icon? applicationIcon;
    private TrayStatus status;
    internal event Action? OpenRequested;
    internal event Action? ExitRequested;

    internal TrayController()
    {
        open = new Forms.ToolStripMenuItem(TextCatalog.Get("TrayOpen"));
        exit = new Forms.ToolStripMenuItem(TextCatalog.Get("TrayExit"));
        open.Click += (_, _) => OpenRequested?.Invoke();
        exit.Click += (_, _) => ExitRequested?.Invoke();
        menu = new Forms.ContextMenuStrip();
        menu.Items.Add(open);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exit);
        applicationIcon = Environment.ProcessPath is { } processPath ? Icon.ExtractAssociatedIcon(processPath) : null;
        icon = new Forms.NotifyIcon { ContextMenuStrip = menu, Visible = true };
        icon.MouseDoubleClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) OpenRequested?.Invoke(); };
        TextCatalog.CultureChanged += RefreshText;
        SetStatus(TrayStatus.Offline);
    }

    internal void SetStatus(TrayStatus status)
    {
        this.status = status;
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

    private void RefreshText()
    {
        open.Text = TextCatalog.Get("TrayOpen");
        exit.Text = TextCatalog.Get("TrayExit");
        SetStatus(status);
    }

    public void Dispose()
    {
        TextCatalog.CultureChanged -= RefreshText;
        icon.Visible = false;
        icon.Dispose();
        applicationIcon?.Dispose();
        menu.Dispose();
    }
}
