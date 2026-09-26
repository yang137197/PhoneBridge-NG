using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WpfButton = System.Windows.Controls.Button;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using MessageBox = System.Windows.MessageBox;
using PhoneBridge.Connection;
using PhoneBridge.Credentials;
using PhoneBridge.Discovery;
using PhoneBridge.Discovery.Windows;
using PhoneBridge.Mounting;

namespace PhoneBridge.Desktop;

public partial class MainWindow : Window
{
    private readonly DeviceSessionCoordinator sessions;
    private readonly string appDataRoot;
    private readonly DiscoveryService discovery = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly ManualEndpointSession manualEndpoints = new();
    private readonly Dictionary<string, DeviceCandidate> candidates = new(StringComparer.Ordinal);
    private readonly Dictionary<int, MountSnapshot> lastMountSnapshots = [];
    private IReadOnlyList<PairingRecord> records = [];
    private Task discoveryTask = Task.CompletedTask, globalOperation = Task.CompletedTask;
    private CancellationTokenSource? globalOperationCancellation;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly AutoStartManager? autoStart;
    private readonly DiagnosticEventLog diagnostics;
    private readonly string? autoStartInitializationError;
    private bool closing, closed, storeUnavailable, discoveryFailed, trayEnabled, exitRequested, loadingAutoStart;
    internal TrayStatus CurrentTrayStatus { get; private set; } = TrayStatus.Offline;
    internal event Action<TrayStatus>? TrayStatusChanged;

    private void OpenExternalLink(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        e.Handled = true;
    }

    internal MainWindow(DiagnosticEventLog diagnostics, PairingStore? pairingStore = null, string? isolatedDataRoot = null, bool uiPreview = false)
    {
        this.diagnostics = diagnostics;
        appDataRoot = isolatedDataRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhoneBridge-NG");
        sessions = new(pairingStore ?? PairingStore.Open());
        InitializeComponent();
        try { if (!uiPreview) autoStart = new(new WindowsAutoStartStore(), Environment.ProcessPath ?? string.Empty); }
        catch (AutoStartException error) { autoStartInitializationError = error.Code; }
        timer.Tick += OnTimerTick;
    }
    private static string T(string key) => TextCatalog.Get(key);
    private DeviceRow? Selected => Devices.SelectedItem as DeviceRow;
    private DeviceSession? SelectedSession => Selected?.DeviceId is { } deviceId && sessions.TryGet(deviceId, out var session) ? session : null;

    private void ShowMainPage(UIElement page, WpfButton activeNavigation)
    {
        foreach (var item in new UIElement[] { DevicesPage, AddPhonePage, DeviceSettingsPage, SettingsPage, AboutPage })
            item.Visibility = item == page ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in new[] { DevicesNavigation, SettingsNavigation, AboutNavigation })
        {
            bool active = button == activeNavigation;
            button.Background = (WpfBrush)FindResource(active ? "SubtleBrush" : "SurfaceBrush");
            if (!active) button.Background = WpfBrushes.Transparent;
            button.Foreground = (WpfBrush)FindResource(active ? "PrimaryBrush" : "TextBrush");
        }
    }

    private void NavigateDevicesClick(object sender, RoutedEventArgs e) => ShowMainPage(DevicesPage, DevicesNavigation);
    private void NavigateSettingsClick(object sender, RoutedEventArgs e)
    {
        ShowMainPage(SettingsPage, SettingsNavigation);
        ShowGeneralSettingsClick(sender, e);
    }
    private void NavigateAboutClick(object sender, RoutedEventArgs e) => ShowMainPage(AboutPage, AboutNavigation);

    private void ShowGeneralSettingsClick(object sender, RoutedEventArgs e)
    {
        GeneralSettingsPage.Visibility = Visibility.Visible;
        AdvancedSettingsPage.Visibility = Visibility.Collapsed;
        GeneralSettingsNavigation.Background = (WpfBrush)FindResource("SubtleBrush");
        GeneralSettingsNavigation.Foreground = (WpfBrush)FindResource("PrimaryBrush");
        AdvancedSettingsNavigation.Background = WpfBrushes.Transparent;
        AdvancedSettingsNavigation.Foreground = (WpfBrush)FindResource("TextBrush");
    }

    private void ShowAdvancedSettingsClick(object sender, RoutedEventArgs e)
    {
        GeneralSettingsPage.Visibility = Visibility.Collapsed;
        AdvancedSettingsPage.Visibility = Visibility.Visible;
        GeneralSettingsNavigation.Background = WpfBrushes.Transparent;
        GeneralSettingsNavigation.Foreground = (WpfBrush)FindResource("TextBrush");
        AdvancedSettingsNavigation.Background = (WpfBrush)FindResource("SubtleBrush");
        AdvancedSettingsNavigation.Foreground = (WpfBrush)FindResource("PrimaryBrush");
    }

    private void SelectDevice(DeviceRow row)
    {
        Devices.SelectedItem = row;
        Devices.ScrollIntoView(row);
        RefreshEndpoints();
        UpdateControls();
    }

    private void AddPhoneClick(object sender, RoutedEventArgs e)
    {
        var available = (Devices.ItemsSource as IEnumerable<DeviceRow>)?.ToArray() ?? [];
        var selected = Selected;
        var row = selected is not null && CanPair(selected) ? selected : available.FirstOrDefault(CanPair);
        if (row is not null) SelectDevice(row);
        PairingPhoneName.Text = row?.Name ?? T("ChoosePhoneFirst");
        ShowMainPage(AddPhonePage, DevicesNavigation);
    }

    private void DevicePrimaryClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { DataContext: DeviceRow row }) return;
        SelectDevice(row);
        if (row.IsConnected) OpenClick(sender, e);
        else if (row.Record is null) AddPhoneClick(sender, e);
        else if (Connect.IsEnabled) ConnectClick(sender, e);
    }

    private void DeviceDisconnectClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { DataContext: DeviceRow row }) return;
        SelectDevice(row);
        if (Unmount.IsEnabled) UnmountClick(sender, e);
    }

    private void DeviceSettingsClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { DataContext: DeviceRow row }) return;
        SelectDevice(row);
        DeviceSettingsName.Text = row.Name;
        DeviceAlias.Text = row.Record?.DeviceAlias ?? string.Empty;
        ShowMainPage(DeviceSettingsPage, DevicesNavigation);
    }

    private void PairingCodeChanged(object sender, RoutedEventArgs e) => UpdateControls();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        FillDrives();
        await ReloadRecords();
        RefreshAutoStart(reportFailure: autoStart is not null);
        discoveryTask = Task.Run(async () =>
        {
            try { await discovery.RunAsync(change => Dispatcher.BeginInvoke(() => Apply(change)), lifetime.Token); }
            catch (Exception error) { await Dispatcher.InvokeAsync(() => { diagnostics.WriteFailure(DiagnosticEventName.DiscoveryFailed, DiagnosticResultCode.Failure, error); discoveryFailed = true; Status.Text = T("DiscoveryFailed"); UpdateControls(); }); }
        });
        timer.Start(); UpdateControls();
    }
    private void Apply(DiscoveryChange change)
    {
        if (closing) return;
        if (change.Candidate is { } candidate) candidates[change.Id] = candidate;
        else if (change.Kind == DiscoveryChangeKind.Removed) candidates.Remove(change.Id);
        diagnostics.Write(new(DiagnosticEventName.DiscoveryChanged,
            change.Kind == DiscoveryChangeKind.Rejected ? DiagnosticLevel.Warning : DiagnosticLevel.Information,
            change.Kind == DiscoveryChangeKind.Rejected ? DiagnosticResultCode.Failure : DiagnosticResultCode.Success,
            change.Kind switch { DiscoveryChangeKind.Added => DiagnosticState.Added, DiscoveryChangeKind.Removed => DiagnosticState.Removed, _ => DiagnosticState.Updated },
            candidates.Count));
        RebuildRows();
    }
    private void RebuildRows()
    {
        string? selected = Selected?.Id;
        var rows = candidates.Values.Where(c => DeviceListPolicy.IncludeCandidate(c.Pairing is not null,
            records.Any(r => r.DeviceId == c.DeviceIdHint))).Select(c =>
        {
            var record = records.FirstOrDefault(r => r.DeviceId == c.DeviceIdHint);
            string deviceId = record?.DeviceId ?? c.DeviceIdHint ?? c.Id;
            var session = sessions.GetOrCreate(deviceId);
            bool isConnected = DeviceListPolicy.IsConnected(record?.DeviceId,
                session.Client.Mount.State == MountState.Mounted ? session.Client.Connected?.Record.DeviceId : null);
            return new DeviceRow(c.Id, deviceId, c, record, isConnected,
                isConnected ? session.Client.Connected?.DriveLetter : null, session.OperationInProgress || session.SupervisorBusy);
        }).ToList();
        rows.AddRange(records.Where(r => !candidates.Values.Any(c => c.DeviceIdHint == r.DeviceId))
            .Select(r =>
            {
                var session = sessions.GetOrCreate(r.DeviceId);
                bool isConnected = DeviceListPolicy.IsConnected(r.DeviceId,
                    session.Client.Mount.State == MountState.Mounted ? session.Client.Connected?.Record.DeviceId : null);
                return new DeviceRow(r.DeviceId, r.DeviceId, null, r, isConnected,
                    isConnected ? session.Client.Connected?.DriveLetter : null, session.OperationInProgress || session.SupervisorBusy);
            }));
        var ordered = rows.OrderBy(r => r.Name, StringComparer.CurrentCulture).ToArray();
        Devices.ItemsSource = ordered;
        var restored = ordered.FirstOrDefault(r => r.Id == selected);
        if (restored is null && AddPhonePage.Visibility == Visibility.Visible)
            restored = ordered.FirstOrDefault(CanPair);
        Devices.SelectedItem = restored;
        UpdateControls();
    }
    private static bool CanPair(DeviceRow row) => row.Record is null &&
        row.Candidate is { Protocol: CandidateProtocol.PairedV3, Pairing: not null };
    private async Task ReloadRecords()
    {
        try
        {
            records = await sessions.RecordsAsync();
            foreach (var record in records) sessions.GetOrCreate(record.DeviceId);
            storeUnavailable = false;
        }
        catch { records = []; storeUnavailable = true; Status.Text = T("StoreFailed"); }
        RebuildRows();
    }
    private void OnTimerTick(object? sender, EventArgs e)
    {
        UpdateControls();
        _ = SuperviseAsync();
    }
    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Endpoints is null) return;
        RefreshEndpoints(Endpoints.SelectedItem as DeviceEndpoint);
        FillDrives();
        ManualAddress.Clear();
        ManualPort.Text = ManualEndpointSession.DefaultPort;
        if (PairingPhoneName is not null) PairingPhoneName.Text = Selected?.Name ?? T("ChoosePhoneFirst");
        if (DeviceSettingsName is not null && DeviceSettingsPage.Visibility == Visibility.Visible)
            DeviceSettingsName.Text = Selected?.Name ?? string.Empty;
        UpdateControls();
    }
    private void RefreshEndpoints(DeviceEndpoint? preferred = null)
    {
        var row = Selected;
        IEnumerable<DeviceEndpoint> automatic = row?.Candidate?.Endpoints ?? [];
        var available = row?.Record is { } record
            ? manualEndpoints.Merge(record.DeviceId, automatic)
            : automatic.Distinct().ToArray();
        Endpoints.ItemsSource = available.OrderBy(p => p.Address.Contains(':')).ToArray();
        Endpoints.SelectedItem = available.FirstOrDefault(p => p == preferred);
        if (Endpoints.SelectedIndex < 0) Endpoints.SelectedIndex = 0;
    }
    private void UpdateControls()
    {
        if (Pair is null) return;
        var row = Selected;
        var session = SelectedSession;
        var mount = session?.Client.Mount ?? new MountSnapshot(MountState.Idle);
        bool globalBusy = !globalOperation.IsCompleted || closing;
        bool busy = globalBusy || session?.OperationInProgress == true || session?.SupervisorBusy == true;
        bool mounted = mount.State == MountState.Mounted && session?.Client.Connected?.Record.DeviceId == row?.Record?.DeviceId;
        bool idle = mount.State is MountState.Idle or MountState.Stopped or MountState.Failed;
        Pair.IsEnabled = !busy && !storeUnavailable && idle && Code.SecurePassword.Length == 8 && row?.Record is null && row?.Candidate?.Protocol == CandidateProtocol.PairedV3 && row.Candidate.Pairing is not null;
        Connect.IsEnabled = !busy && !storeUnavailable && idle &&
            row?.Record?.State is (PairingRecordState.Pending or PairingRecordState.Active) && Endpoints.SelectedItem is DeviceEndpoint;
        RemovePhone.IsEnabled = !busy && !storeUnavailable && row?.Record is not null;
        SaveDeviceDetails.IsEnabled = !busy && !storeUnavailable && row?.Record is not null;
        DeleteConfirmed.IsEnabled = !busy && !storeUnavailable && row?.Record is { State: PairingRecordState.Active, Mode: not AccessMode.ReadOnly } && Endpoints.SelectedItem is DeviceEndpoint;
        Open.IsEnabled = !busy && mounted;
        Unmount.IsEnabled = !busy && !idle;
        Cancel.IsEnabled = !closing && (session?.OperationInProgress == true || globalOperationCancellation is not null);
        Code.IsEnabled = !busy;
        DeletePath.IsEnabled = !busy;
        ExportBundle.IsEnabled = !busy;
        bool canUseManual = !busy && !storeUnavailable &&
            row?.Record?.State is (PairingRecordState.Pending or PairingRecordState.Active or PairingRecordState.RevocationPending);
        UseManualAddress.IsEnabled = canUseManual;
        ManualAddress.IsEnabled = canUseManual;
        ManualPort.IsEnabled = canUseManual;
        ClearManualAddress.IsEnabled = canUseManual && row?.Record is { } manualRecord && manualEndpoints.TryGet(manualRecord.DeviceId, out _);
        Drives.IsEnabled = !busy && idle; Endpoints.IsEnabled = !busy;
        ConnectionStatus.Text = mounted && session?.Client.Connected is { } active
            ? string.Format(T("MountedAt"), active.DriveLetter, active.Record.DisplayName, T(active.Record.Mode.ToString()))
            : mount.State == MountState.RecoveringWrites ? T("RecoveringWrites")
            : mount.State == MountState.StopFailed ? T(mount.ErrorCode ?? "unmount-not-confirmed") : T("NoMount");
        var allSessions = sessions.Sessions;
        SetTrayStatus(TrayPolicy.ResolveStatus(
            storeUnavailable || discoveryFailed || allSessions.Any(item => item.Client.Mount.State is MountState.Failed or MountState.StopFailed),
            allSessions.Any(item => item.Client.Mount.State == MountState.Mounted),
            !globalOperation.IsCompleted || allSessions.Any(item => item.OperationInProgress || item.SupervisorBusy),
            candidates.Count > 0));
        ObserveMounts(allSessions);
    }
    private void ObserveMounts(IReadOnlyList<DeviceSession> allSessions)
    {
        foreach (var session in allSessions)
        {
            var current = session.Client.Mount;
            lastMountSnapshots.TryGetValue(session.LogContext, out var previous);
            if (previous is not null && current.State == previous.State && current.ErrorCode == previous.ErrorCode &&
                current.ExitCode == previous.ExitCode && current.Forced == previous.Forced) continue;
            diagnostics.Write(new(DiagnosticEventName.MountStateChanged,
                current.State is MountState.Failed or MountState.StopFailed ? DiagnosticLevel.Error : DiagnosticLevel.Information,
                DiagnosticCodeMap.From(current.ErrorCode),
                current.State switch { MountState.Starting => DiagnosticState.Starting, MountState.RecoveringWrites => DiagnosticState.Recovering, MountState.Mounted => DiagnosticState.Mounted,
                    MountState.Stopping => DiagnosticState.Stopping, MountState.Stopped or MountState.Idle => DiagnosticState.Stopped, _ => DiagnosticState.Failed },
                Session: session.LogContext));
            if (current.State == MountState.Mounted)
                diagnostics.Write(new(DiagnosticEventName.WinFspChecked, Code: DiagnosticResultCode.Success, State: DiagnosticState.Healthy, Session: session.LogContext));
            if (current.ErrorCode == "winfsp-missing")
                diagnostics.Write(new(DiagnosticEventName.WinFspChecked, DiagnosticLevel.Error, DiagnosticResultCode.WinFspMissing, DiagnosticState.Failed, Session: session.LogContext));
            if (current.ExitCode is not null && current.ExitCode != previous?.ExitCode)
                diagnostics.Write(new(DiagnosticEventName.RcloneExited,
                    current.ExitCode == 0 ? DiagnosticLevel.Information : DiagnosticLevel.Error,
                    current.ExitCode == 0 ? DiagnosticResultCode.Success : DiagnosticResultCode.Failure,
                    current.State is MountState.Stopped ? DiagnosticState.Stopped : DiagnosticState.Failed,
                    Session: session.LogContext));
            lastMountSnapshots[session.LogContext] = current;
        }
    }
    private void SetTrayStatus(TrayStatus value)
    {
        if (CurrentTrayStatus == value) return;
        CurrentTrayStatus = value;
        TrayStatusChanged?.Invoke(value);
    }
    internal void ConfigureTray(bool enabled) => trayEnabled = enabled;
    internal void ShowFromTray()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        diagnostics.Write(new(DiagnosticEventName.TrayVisibilityChanged, State: DiagnosticState.Visible));
    }
    internal void ShowHiddenAtStartup()
    {
        ShowActivated = false; ShowInTaskbar = false; Opacity = 0;
        Show(); Hide();
        Opacity = 1; ShowInTaskbar = true; ShowActivated = true; WindowState = WindowState.Normal;
        diagnostics.Write(new(DiagnosticEventName.TrayVisibilityChanged, State: DiagnosticState.Hidden));
    }
    internal void RequestExitFromTray()
    {
        if (closing || closed) return;
        exitRequested = true;
        ShowFromTray();
        Close();
    }
    private void FillDrives()
    {
        char current = Drives.SelectedItem is char value ? value : 'P';
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        string? selectedDevice = Selected?.DeviceId;
        char? ownReservation = SelectedSession?.ReservedDrive;
        var available = Enumerable.Range('D', 'Z' - 'D' + 1).Select(n => (char)n)
            .Where(c => (!used.Contains(c) || ownReservation == c) && sessions.CanUseDrive(selectedDevice, c)).ToArray();
        Drives.ItemsSource = available; Drives.SelectedItem = available.Contains(current) ? current : available.FirstOrDefault();
    }
    private MountOptions Options(string deviceId) => OptionsFor(deviceId,
        Drives.SelectedItem is char letter ? letter : throw new ConnectionException("drive-occupied"));
    private IProgress<ConnectionStage> Progress(DeviceSession session) => new Progress<ConnectionStage>(stage =>
    {
        if (Selected?.DeviceId == session.DeviceId) Status.Text = T(stage.ToString());
    });
    private static bool Terminal(ConnectionException error) => error.Code is
        "unauthorized" or "identity-mismatch" or "record-changed" or "mode-changed" or "invalid-response";
    private static bool Terminal(MountException error) => error.Code is
        "drive-occupied" or "drive-reserved" or "winfsp-missing" or "rclone-hash-mismatch";

    private async Task SuperviseAsync()
    {
        if (closing || storeUnavailable) return;
        foreach (var session in sessions.Sessions)
            if (!session.OperationInProgress && session.TryEnterSupervisor()) _ = SuperviseSessionAsync(session);
        await Task.CompletedTask;
    }

    private async Task SuperviseSessionAsync(DeviceSession session)
    {
        UpdateControls();
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (session.Client.Mount.State == MountState.Mounted && session.Client.Connected is { } active)
            {
                if (!session.Reconnect.Armed)
                    session.Reconnect.Arm(active.Record.DeviceId, OptionsFor(active.Record.DeviceId, active.DriveLetter), now);
                if (!session.Reconnect.HealthDue(now)) return;
                try
                {
                    SetSessionStatus(session, "ConnectionChecking");
                    diagnostics.Write(new(DiagnosticEventName.NetworkCheckCompleted, State: DiagnosticState.Checking, Session: session.LogContext));
                    await session.Client.CheckSessionAsync(active.Record.DeviceId, active.Endpoint, lifetime.Token);
                    session.Reconnect.HealthSucceeded(DateTimeOffset.UtcNow);
                    diagnostics.Write(new(DiagnosticEventName.NetworkCheckCompleted, Code: DiagnosticResultCode.Success, State: DiagnosticState.Healthy, Session: session.LogContext));
                    SetSessionStatus(session, "ConnectionHealthy");
                }
                catch (ConnectionException error) when (error.Code == "operation-in-progress") { }
                catch (ConnectionException error) when (Terminal(error))
                {
                    diagnostics.Write(new(DiagnosticEventName.NetworkCheckCompleted, DiagnosticLevel.Error, DiagnosticCodeMap.From(error.Code), DiagnosticState.Failed, Session: session.LogContext));
                    session.Reconnect.Suppress();
                    try { await sessions.StopAsync(session.DeviceId); }
                    catch (ConnectionException stop) { SetSessionStatus(session, stop.Code); return; }
                    if (error.Code == "mode-changed") await ReloadRecords();
                    FillDrives(); SetSessionStatus(session, error.Code);
                }
                catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or ConnectionException)
                {
                    if (!session.Reconnect.HealthFailed(DateTimeOffset.UtcNow))
                    {
                        diagnostics.WriteFailure(DiagnosticEventName.NetworkCheckCompleted, DiagnosticResultCode.Failure, error, session.LogContext);
                        if (Selected?.DeviceId == session.DeviceId)
                            Status.Text = string.Format(T("ConnectionRetry"), session.Reconnect.ConsecutiveHealthFailures, 3);
                        return;
                    }
                    SetSessionStatus(session, "ConnectionLost");
                    diagnostics.Write(new(DiagnosticEventName.NetworkCheckCompleted, DiagnosticLevel.Error, DiagnosticResultCode.Failure,
                        DiagnosticState.Lost, session.Reconnect.ConsecutiveHealthFailures, Session: session.LogContext));
                    try
                    {
                        await sessions.StopAsync(session.DeviceId, preserveDriveReservation: true);
                        session.Reconnect.Disconnected(DateTimeOffset.UtcNow);
                        diagnostics.Write(new(DiagnosticEventName.ReconnectScheduled, State: DiagnosticState.Waiting, Session: session.LogContext));
                        FillDrives(); SetSessionStatus(session, "ReconnectWaiting");
                    }
                    catch (ConnectionException stop) { SetSessionStatus(session, stop.Code); }
                }
                return;
            }

            if (!session.Reconnect.Armed || session.Client.Mount.State is not (MountState.Idle or MountState.Stopped or MountState.Failed)) return;
            if (session.Client.Connected is not null)
            {
                try { await sessions.StopAsync(session.DeviceId, preserveDriveReservation: true); }
                catch (ConnectionException stop) { SetSessionStatus(session, stop.Code); return; }
                session.Reconnect.Disconnected(DateTimeOffset.UtcNow); FillDrives();
            }
            var attemptTime = DateTimeOffset.UtcNow;
            if (session.Reconnect.DeviceId is not { } reconnectDevice || !session.Reconnect.CanReconnect(reconnectDevice, attemptTime)) return;
            DeviceEndpoint? endpoint = manualEndpoints.SelectForReconnect(reconnectDevice, candidates.Values);
            if (endpoint is null || session.Reconnect.Options is not { } options) return;
            try
            {
                SetSessionStatus(session, "Reconnecting");
                diagnostics.Write(new(DiagnosticEventName.ConnectionStarted, State: DiagnosticState.Starting, Session: session.LogContext));
                var record = await sessions.ConnectAsync(session.DeviceId, endpoint, options, Progress(session), lifetime.Token);
                session.Reconnect.Arm(record.DeviceId, options, DateTimeOffset.UtcNow);
                diagnostics.Write(new(DiagnosticEventName.ConnectionCompleted, Code: DiagnosticResultCode.Success, State: DiagnosticState.Mounted, Session: session.LogContext));
                SetSessionStatus(session, "ReconnectComplete");
            }
            catch (ConnectionException error) when (Terminal(error))
            {
                diagnostics.Write(new(DiagnosticEventName.ConnectionCompleted, DiagnosticLevel.Error, DiagnosticCodeMap.From(error.Code), DiagnosticState.Failed, Session: session.LogContext));
                session.Reconnect.Suppress();
                try { await sessions.StopAsync(session.DeviceId); } catch { }
                SetSessionStatus(session, error.Code);
            }
            catch (MountException error) when (Terminal(error))
            {
                diagnostics.Write(new(DiagnosticEventName.ConnectionCompleted, DiagnosticLevel.Error, DiagnosticCodeMap.From(error.Code), DiagnosticState.Failed, Session: session.LogContext));
                session.Reconnect.Suppress();
                try { await sessions.StopAsync(session.DeviceId); } catch { }
                SetSessionStatus(session, error.Code);
            }
            catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or ConnectionException or MountException)
            {
                diagnostics.WriteFailure(DiagnosticEventName.ConnectionCompleted, DiagnosticResultCode.Failure, error, session.LogContext);
                session.Reconnect.ReconnectFailed(DateTimeOffset.UtcNow);
                var seconds = Math.Max(1, (int)Math.Ceiling((session.Reconnect.NextAttempt - DateTimeOffset.UtcNow).TotalSeconds));
                if (Selected?.DeviceId == session.DeviceId) Status.Text = string.Format(T("ReconnectBackoff"), seconds);
            }
        }
        catch (CredentialStoreException) { session.Reconnect.Suppress(); storeUnavailable = true; SetSessionStatus(session, "StoreFailed"); }
        finally
        {
            session.ExitSupervisor();
            RebuildRows();
            UpdateControls();
        }
    }

    private void SetSessionStatus(DeviceSession session, string key)
    {
        if (Selected?.DeviceId == session.DeviceId) Status.Text = T(key);
    }

    private MountOptions OptionsFor(string deviceId, char letter) => new(letter,
        Path.Combine(AppContext.BaseDirectory, "tools", "rclone.exe"),
        sessions.SessionRoot(Path.Combine(appDataRoot, "Sessions"), deviceId),
        Path.Combine(appDataRoot, "Sessions"));
    private void StartDevice(DeviceSession session, DiagnosticEventName completionEvent, Func<CancellationToken, Task> work,
        string successKey = "Completed", Action? succeeded = null)
    {
        if (closing || session.OperationInProgress) return;
        try { _ = session.StartOperation(token => ExecuteDevice(session, completionEvent, work, successKey, token, succeeded), lifetime.Token); }
        catch (ConnectionException error) { Status.Text = T(error.Code); }
        UpdateControls();
    }

    private async Task ExecuteDevice(DeviceSession session, DiagnosticEventName completionEvent,
        Func<CancellationToken, Task> work, string successKey, CancellationToken token, Action? succeeded)
    {
        bool completed=false;
        try
        {
            await work(token);
            diagnostics.Write(new(completionEvent, Code: DiagnosticResultCode.Success, Session: session.LogContext));
            SetSessionStatus(session, successKey);
            completed=true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { diagnostics.Write(new(completionEvent, DiagnosticLevel.Warning, DiagnosticResultCode.Cancelled, Session: session.LogContext)); SetSessionStatus(session, "OperationCancelled"); }
        catch (OperationCanceledException)
        { diagnostics.Write(new(completionEvent, DiagnosticLevel.Warning, DiagnosticResultCode.Timeout, Session: session.LogContext)); SetSessionStatus(session, "TimedOut"); }
        catch (ConnectionException error)
        { diagnostics.Write(new(completionEvent, DiagnosticLevel.Error, DiagnosticCodeMap.From(error.Code), DiagnosticState.Failed, Session: session.LogContext)); SetSessionStatus(session, error.Code); }
        catch (MountException error)
        { diagnostics.Write(new(completionEvent, DiagnosticLevel.Error, DiagnosticCodeMap.From(error.Code), DiagnosticState.Failed, Session: session.LogContext)); SetSessionStatus(session, error.Code); }
        catch (CredentialStoreException error)
        { diagnostics.WriteFailure(completionEvent, DiagnosticResultCode.StorageFailure, error, session.LogContext); SetSessionStatus(session, "StoreFailed"); }
        catch (Exception error)
        { diagnostics.WriteFailure(completionEvent, DiagnosticResultCode.Failure, error, session.LogContext); SetSessionStatus(session, "OperationFailed"); }
        finally
        {
            Code.Clear();
            await ReloadRecords();
            FillDrives();
            if(completed)succeeded?.Invoke();
        }
    }

    private void StartGlobal(DiagnosticEventName completionEvent, Func<CancellationToken, Task> work, string successKey)
    {
        if (!globalOperation.IsCompleted || closing) return;
        globalOperationCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        globalOperation = ExecuteGlobal(completionEvent, work, successKey, globalOperationCancellation);
        UpdateControls();
    }

    private async Task ExecuteGlobal(DiagnosticEventName completionEvent, Func<CancellationToken, Task> work,
        string successKey, CancellationTokenSource owned)
    {
        await Task.Yield();
        try { await work(owned.Token); diagnostics.Write(new(completionEvent, Code: DiagnosticResultCode.Success)); Status.Text = T(successKey); }
        catch (OperationCanceledException) when (owned.IsCancellationRequested) { diagnostics.Write(new(completionEvent, DiagnosticLevel.Warning, DiagnosticResultCode.Cancelled)); Status.Text = T("OperationCancelled"); }
        catch (DiagnosticExportException error) { diagnostics.WriteFailure(DiagnosticEventName.DiagnosticExportFailed, DiagnosticResultCode.Failure, error); Status.Text = T("DiagnosticsExportFailed"); }
        catch (Exception error) { diagnostics.WriteFailure(completionEvent, DiagnosticResultCode.Failure, error); Status.Text = T("OperationFailed"); }
        finally { owned.Dispose(); globalOperationCancellation = null; UpdateControls(); }
    }
    private void PairClick(object sender, RoutedEventArgs e)
    {
        if (Selected?.Candidate is not { DeviceIdHint: { } deviceId } candidate || Endpoints.SelectedItem is not DeviceEndpoint endpoint) return;
        var session = sessions.GetOrCreate(deviceId);
        char[] code;
        using (var password = Code.SecurePassword)
        {
            code = new char[password.Length];
            nint pointer = Marshal.SecureStringToGlobalAllocUnicode(password);
            try { Marshal.Copy(pointer, code, 0, code.Length); }
            finally { Marshal.ZeroFreeGlobalAllocUnicode(pointer); }
        }
        Code.Clear();
        if (code.Length != 8 || code.Any(c => c is < '0' or > '9')) { Array.Clear(code); Status.Text = T("InvalidCode"); return; }
        var progress = Progress(session); session.Reconnect.Suppress();
        diagnostics.Write(new(DiagnosticEventName.PairingStarted, State: DiagnosticState.Starting, Session: session.LogContext));
        StartDevice(session, DiagnosticEventName.AuthenticationCompleted, async token =>
        {
            await session.Client.PairAsync(candidate, endpoint, code, Environment.MachineName, progress, token);
        }, "PairingCompleted", () => ShowMainPage(DevicesPage, DevicesNavigation));
    }
    private void ConnectClick(object sender, RoutedEventArgs e)
    {
        if (Selected?.Record is not { } record || Endpoints.SelectedItem is not DeviceEndpoint endpoint) return;
        var session = sessions.GetOrCreate(record.DeviceId);
        var options = Options(record.DeviceId); var progress = Progress(session); session.Reconnect.Suppress();
        diagnostics.Write(new(DiagnosticEventName.ConnectionStarted, State: DiagnosticState.Starting, Session: session.LogContext));
        StartDevice(session, DiagnosticEventName.ConnectionCompleted, async token =>
        {
            var connected = await sessions.ConnectAsync(record.DeviceId, endpoint, options, progress, token);
            session.Reconnect.Arm(connected.DeviceId, options, DateTimeOffset.UtcNow);
        });
    }
    private void ManualAddressClick(object sender, RoutedEventArgs e)
    {
        if (Selected?.Record is not { } record) return;
        var session = sessions.GetOrCreate(record.DeviceId);
        if (!manualEndpoints.TrySet(record.DeviceId, ManualAddress.Text, ManualPort.Text, out var endpoint))
        {
            diagnostics.Write(new(DiagnosticEventName.ManualEndpointChanged, DiagnosticLevel.Warning,
                DiagnosticResultCode.Failure, DiagnosticState.Failed, Session: session.LogContext));
            Status.Text = T("ManualEndpointInvalid");
            return;
        }
        ManualAddress.Clear();
        RefreshEndpoints(endpoint);
        diagnostics.Write(new(DiagnosticEventName.ManualEndpointChanged, Code: DiagnosticResultCode.Success,
            State: DiagnosticState.Added, Session: session.LogContext));
        Status.Text = T("ManualEndpointAdded");
        UpdateControls();
    }
    private void ClearManualAddressClick(object sender, RoutedEventArgs e)
    {
        if (Selected?.Record is not { } record || !manualEndpoints.Clear(record.DeviceId)) return;
        var session = sessions.GetOrCreate(record.DeviceId);
        RefreshEndpoints();
        diagnostics.Write(new(DiagnosticEventName.ManualEndpointChanged, Code: DiagnosticResultCode.Success,
            State: DiagnosticState.Removed, Session: session.LogContext));
        Status.Text = T("ManualEndpointCleared");
        UpdateControls();
    }
    private void CancelClick(object sender, RoutedEventArgs e)
    {
        if (SelectedSession?.OperationInProgress == true) SelectedSession.CancelOperation();
        else globalOperationCancellation?.Cancel();
    }
    private void UnmountClick(object sender, RoutedEventArgs e)
    {
        if (Selected?.Record is not { } record) return;
        var session = sessions.GetOrCreate(record.DeviceId);
        session.Reconnect.Suppress();
        StartDevice(session, DiagnosticEventName.MountStateChanged, async _ => { await sessions.StopAsync(record.DeviceId); });
    }
    private void RemovePhoneClick(object sender, RoutedEventArgs e)
    {
        if (Selected?.Record is not { } record || MessageBox.Show(this, T("RevokeConfirm"), "PhoneBridge NG", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        var session = sessions.GetOrCreate(record.DeviceId);
        session.Reconnect.Suppress();
        StartDevice(session, DiagnosticEventName.AuthenticationCompleted, async _ =>
        {
            await sessions.RemoveLocallyAsync(record.DeviceId);
            manualEndpoints.Clear(record.DeviceId);
            ShowMainPage(DevicesPage, DevicesNavigation);
        });
    }
    private void SaveDeviceDetailsClick(object sender, RoutedEventArgs e)
    {
        if(Selected?.Record is not { } record)return;
        var session=sessions.GetOrCreate(record.DeviceId);
        string deviceNote=DeviceAlias.Text;
        StartDevice(session,DiagnosticEventName.DeviceMetadataChanged,async _=>
        {
            await sessions.UpdateDeviceNoteAsync(record.DeviceId,deviceNote);
        },"DeviceDetailsSaved",()=>
        {
            DeviceSettingsName.Text=Selected?.Name??string.Empty;
            DeviceAlias.Text=Selected?.Record?.DeviceAlias??string.Empty;
        });
    }
    private void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (Selected?.Record is not { } record || Endpoints.SelectedItem is not DeviceEndpoint endpoint) return;
        var session = sessions.GetOrCreate(record.DeviceId);
        string path = DeletePath.Text;
        StartDevice(session, DiagnosticEventName.ConnectionCompleted, async token =>
        {
            var preview = await session.Client.PrepareDeletionAsync(record.DeviceId, endpoint, path, token);
            string kind = preview.Directory ? T("Directory") : T("File");
            string message = string.Format(T("DeleteConfirm"), preview.Path, kind, preview.Size);
            if (MessageBox.Show(this, message, "PhoneBridge NG", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                throw new ConnectionException("delete-cancelled");
            await session.Client.ConfirmDeletionAsync(preview, endpoint, token);
            DeletePath.Clear();
        });
    }
    private void OpenClick(object sender, RoutedEventArgs e)
    {
        var session = SelectedSession;
        if (session?.Client.Mount.State != MountState.Mounted || session.Client.Connected is not { } active) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, Arguments = $"{active.DriveLetter}:\\" }); }
        catch { Status.Text = T("OpenFailed"); }
    }
    private void AutoStartChanged(object sender, RoutedEventArgs e)
    {
        if (loadingAutoStart || autoStart is null) return;
        bool enabled = AutoStart.IsChecked == true;
        try
        {
            autoStart.SetEnabled(enabled);
            diagnostics.Write(new(DiagnosticEventName.StartupSettingChanged, Code: DiagnosticResultCode.Success,
                State: enabled ? DiagnosticState.Enabled : DiagnosticState.Disabled));
            Status.Text = T(enabled ? "AutoStartEnabled" : "AutoStartDisabled");
        }
        catch (AutoStartException error)
        {
            diagnostics.Write(new(DiagnosticEventName.StartupSettingChanged, DiagnosticLevel.Error, DiagnosticResultCode.Failure, DiagnosticState.Failed));
            RefreshAutoStart();
            Status.Text = T(error.Code);
        }
    }
    private void RefreshAutoStart(bool reportFailure = false)
    {
        loadingAutoStart = true;
        try
        {
            if (autoStart is null)
            {
                AutoStart.IsChecked = false; AutoStart.IsEnabled = false;
                if (reportFailure) Status.Text = T(autoStartInitializationError ?? "autostart-read-failed");
                return;
            }
            var state = autoStart.GetState();
            AutoStart.IsChecked = state == AutoStartState.Enabled;
            AutoStart.IsEnabled = state != AutoStartState.Conflict;
            if (reportFailure && state == AutoStartState.Conflict) Status.Text = T("autostart-entry-conflict");
        }
        catch (AutoStartException error)
        {
            AutoStart.IsChecked = false; AutoStart.IsEnabled = false;
            if (reportFailure) Status.Text = T(error.Code);
        }
        finally { loadingAutoStart = false; }
    }
    private async void RefreshClick(object sender, RoutedEventArgs e) { discovery.RequestRefresh(); await ReloadRecords(); FillDrives(); }
    private void ExportDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = T("ExportDiagnostics"), Filter = T("DiagnosticsZipFilter"), DefaultExt = ".zip", AddExtension = true,
            FileName = "PhoneBridge-Diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;
        StartGlobal(DiagnosticEventName.DiagnosticExportCompleted,
            token => new DiagnosticBundleExporter(diagnostics).ExportAsync(dialog.FileName, token), "DiagnosticsExported");
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closed) return;
        e.Cancel = true;
        if (TrayPolicy.ResolveClose(trayEnabled, exitRequested) == WindowCloseAction.Hide)
        {
            Hide();
            diagnostics.Write(new(DiagnosticEventName.TrayVisibilityChanged, State: DiagnosticState.Hidden));
            return;
        }
        if (closing) return;
        closing = true;
        globalOperationCancellation?.Cancel();
        foreach (var session in sessions.Sessions) { session.Reconnect.Suppress(); session.CancelOperation(); }
        UpdateControls(); Status.Text = T("Closing");
        await globalOperation;
        try
        {
            await sessions.DisposeAsync(); lifetime.Cancel(); await discoveryTask;
            timer.Stop(); lifetime.Dispose(); closed = true; Close();
        }
        catch { closing = false; ShowFromTray(); Status.Text = T("unmount-not-confirmed"); UpdateControls(); }
    }

    private sealed record DeviceRow(string Id, string DeviceId, DeviceCandidate? Candidate, PairingRecord? Record,
        bool IsConnected, char? DriveLetter, bool IsBusy)
    {
        public string Name => Record?.DisplayName ?? Candidate!.DisplayName;
        public string Address => Candidate is null ? T("Offline") : string.Join(", ", Candidate.Endpoints.Select(p => p.Address));
        public string Mode => Record is null ? T("NotPaired") : T(Record.Mode.ToString());
        public string State => IsConnected ? T("ConnectedState") : Record?.State switch
        {
            PairingRecordState.Active => T("NotConnected"),
            PairingRecordState.Pending => T("Pending"),
            PairingRecordState.RevocationPending => T("RevocationPending"),
            PairingRecordState.NeedsRepair => T("NeedsRepair"),
            _ => Candidate?.Protocol == CandidateProtocol.ExperimentalV2 ? T("Experimental") : Candidate?.Pairing is null ? T("EnablePairing") : T("NotPaired")
        };
        public string PrimaryAction => IsConnected ? T("OpenFiles") : Record?.State == PairingRecordState.Pending ? T("ContinueConnecting") : Record is null ? T("PairAction") : T("ConnectAction");
        public string DriveSummary => IsConnected && DriveLetter is { } letter ? $"{letter}:\\" : Address;
        public string Accent => IsConnected ? "#16865B" : Record is null ? "#109DA8" : Record.State == PairingRecordState.Active ? "#8A96A6" : "#A85F00";
        public bool CanDisconnect => IsConnected;
        public bool ShowInDeviceList => Record is not null;
    }
}
