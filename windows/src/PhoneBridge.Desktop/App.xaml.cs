using System.ComponentModel;
using System.Diagnostics;
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
    private PendingUpdate? pendingUpdate;
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
        if (!uiPreview) RunLegacyExplorerMigration(appDataRoot);
        var window = new MainWindow(diagnostics, previewStore, appDataRoot, uiPreview);
        MainWindow = window;
        window.UpdateInstallerReady += update =>
        {
            pendingUpdate = update;
            window.RequestExitFromTray();
        };
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

    private void RunLegacyExplorerMigration(string appDataRoot)
    {
        if (!LegacyExplorerMigration.TryRequiresAction(appDataRoot, out bool required))
        {
            diagnostics?.Write(new(DiagnosticEventName.ExplorerMigrationChanged, DiagnosticLevel.Warning,
                DiagnosticResultCode.Failure, DiagnosticState.Failed));
            MessageBox.Show(TextCatalog.Get("LegacyExplorerMigrationInspectionFailed"), "PhoneBridge NG",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!required) return;

        diagnostics?.Write(new(DiagnosticEventName.ExplorerMigrationChanged, State: DiagnosticState.Waiting));
        if (MessageBox.Show(TextCatalog.Get("LegacyExplorerMigrationPrompt"), "PhoneBridge NG",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            diagnostics?.Write(new(DiagnosticEventName.ExplorerMigrationChanged, Code: DiagnosticResultCode.Cancelled,
                State: DiagnosticState.Waiting));
            return;
        }

        diagnostics?.Write(new(DiagnosticEventName.ExplorerMigrationChanged, State: DiagnosticState.Starting));
        if (LegacyExplorerMigration.Apply(appDataRoot) == LegacyExplorerMigrationResult.Completed)
        {
            diagnostics?.Write(new(DiagnosticEventName.ExplorerMigrationChanged, Code: DiagnosticResultCode.Success,
                State: DiagnosticState.Healthy));
            MessageBox.Show(TextCatalog.Get("LegacyExplorerMigrationCompleted"), "PhoneBridge NG",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            diagnostics?.Write(new(DiagnosticEventName.ExplorerMigrationChanged, DiagnosticLevel.Error,
                DiagnosticResultCode.Failure, DiagnosticState.Failed));
            MessageBox.Show(TextCatalog.Get("LegacyExplorerMigrationFailed"), "PhoneBridge NG",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        diagnostics?.Write(new(DiagnosticEventName.AppStopping));
        ProcessStartInfo? installer = null;
        if (pendingUpdate is { } update)
        {
            if (UpdateService.VerifyInstaller(update))
            {
                diagnostics?.Write(new(DiagnosticEventName.UpdateChanged, Code: DiagnosticResultCode.Success,
                    State: DiagnosticState.Starting));
                installer = new(update.InstallerPath) { UseShellExecute = true };
            }
            else diagnostics?.Write(new(DiagnosticEventName.UpdateChanged, DiagnosticLevel.Error,
                DiagnosticResultCode.IntegrityFailure, DiagnosticState.Failed));
        }
        tray?.Dispose(); installerGuard?.Dispose(); instance?.Dispose();
        if (installer is not null)
        {
            try { Process.Start(installer); }
            catch (Exception error) when (error is InvalidOperationException or Win32Exception)
            {
                diagnostics?.WriteFailure(DiagnosticEventName.UpdateChanged, DiagnosticResultCode.Failure, error);
            }
        }
        diagnostics?.Dispose();
        base.OnExit(e);
    }
}
