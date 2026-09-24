using System.Globalization;
using System.Security.Principal;
using System.Windows;
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
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh-CN" : "en-US");
        using var identity = WindowsIdentity.GetCurrent();
        instance = new Mutex(true, "Local\\PhoneBridge-NG-Desktop-" + identity.User!.Value, out bool created);
        if (!created)
        {
            if (!startupLaunch) MessageBox.Show(TextCatalog.Get("AlreadyRunning"), "PhoneBridge NG");
            Shutdown(); return;
        }
        installerGuard = new Mutex(true, "Local\\PhoneBridge-NG-Installer-Guard");
        base.OnStartup(e);
        diagnostics = DiagnosticEventLog.OpenDefault();
        diagnostics.Write(new(DiagnosticEventName.AppStarted, State: startupLaunch ? DiagnosticState.Startup : DiagnosticState.Manual));
        var window = new MainWindow(diagnostics);
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
