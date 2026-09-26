using System.IO;
using System.Security.Principal;
using System.Windows;
using PhoneBridge.Credentials;
using MessageBox = System.Windows.MessageBox;

namespace PhoneBridge.Desktop;

public partial class App : System.Windows.Application
{
    private Mutex? instance;
    private Mutex? installerGuard;
    private TrayController? tray;
    private DiagnosticEventLog? diagnostics;
    protected override void OnStartup(StartupEventArgs e)
    {
        bool startupLaunch = AutoStartManager.IsStartupLaunch(e.Args);
        bool uiPreview = e.Args.Contains("--ui-preview", StringComparer.Ordinal);
        string? previewRoot = null;
        string appDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhoneBridge-NG");
        if (uiPreview)
        {
            string revision = typeof(App).Assembly.GetName().Version?.ToString() ?? throw new InvalidOperationException("ui-preview-version-missing");
            previewRoot = PairingStore.UiPreviewDataRoot(revision);
            appDataRoot = previewRoot;
        }
        TextCatalog.SetCulture(LanguageSettings.Load(appDataRoot));
        using var identity = WindowsIdentity.GetCurrent();
        instance = new Mutex(true, "Local\\PhoneBridge-NG-Desktop-" + identity.User!.Value + (uiPreview ? "-UiPreview" : string.Empty), out bool created);
        if (!created)
        {
            if (!startupLaunch) MessageBox.Show(TextCatalog.Get("AlreadyRunning"), "PhoneBridge NG");
            Shutdown(); return;
        }
        installerGuard = new Mutex(true, "Local\\PhoneBridge-NG-Installer-Guard");
        base.OnStartup(e);
        PairingStore? previewStore = null;
        if (uiPreview)
        {
            string revision = typeof(App).Assembly.GetName().Version?.ToString() ?? throw new InvalidOperationException("ui-preview-version-missing");
            previewStore = PairingStore.OpenUiPreview(revision);
        }
        diagnostics = previewRoot is null
            ? DiagnosticEventLog.OpenDefault()
            : DiagnosticEventLog.Open(Path.Combine(previewRoot, "Logs"), DiagnosticEventLog.ProductVersion());
        diagnostics.Write(new(DiagnosticEventName.AppStarted, State: startupLaunch ? DiagnosticState.Startup : DiagnosticState.Manual));
        var window = new MainWindow(diagnostics, previewStore, appDataRoot, uiPreview);
        MainWindow = window;
        try
        {
            tray = new TrayController();
            window.ConfigureTray(true);
            tray.OpenRequested += window.ShowFromTray;
            tray.ExitRequested += window.RequestExitFromTray;
            window.TrayStatusChanged += tray.SetStatus;
            tray.SetStatus(window.CurrentTrayStatus);
        }
        catch
        {
            tray?.Dispose(); tray = null;
            window.ConfigureTray(false);
            MessageBox.Show(TextCatalog.Get("TrayUnavailable"), "PhoneBridge NG");
        }
        window.Closed += (_, _) => { tray?.Dispose(); tray = null; Shutdown(); };
        if (startupLaunch && tray is not null) window.ShowHiddenAtStartup();
        else window.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        diagnostics?.Write(new(DiagnosticEventName.AppStopping));
        tray?.Dispose(); diagnostics?.Dispose(); installerGuard?.Dispose(); instance?.Dispose();
        base.OnExit(e);
    }
}
