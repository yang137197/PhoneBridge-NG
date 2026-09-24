using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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
    private readonly ConnectionClient client = new(PairingStore.Open());
    private readonly DiscoveryService discovery = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly ReconnectPolicy reconnect = new();
    private readonly ManualEndpointSession manualEndpoints = new();
    private readonly Dictionary<string, DeviceCandidate> candidates = new(StringComparer.Ordinal);
    private IReadOnlyList<PairingRecord> records = [];
    private Task discoveryTask = Task.CompletedTask, operation = Task.CompletedTask;
    private CancellationTokenSource? operationCancellation;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly AutoStartManager? autoStart;
    private readonly DiagnosticEventLog diagnostics;
    private readonly string? autoStartInitializationError;
    private bool closing, closed, storeUnavailable, supervisorBusy, discoveryFailed, trayEnabled, exitRequested, loadingAutoStart;
    private MountSnapshot? lastMountSnapshot;
    internal TrayStatus CurrentTrayStatus { get; private set; } = TrayStatus.Offline;
    internal event Action<TrayStatus>? TrayStatusChanged;

    private void OpenExternalLink(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        e.Handled = true;
    }

    internal MainWindow(DiagnosticEventLog diagnostics)
    {
        this.diagnostics = diagnostics;
        InitializeComponent();
        try { autoStart = new(new WindowsAutoStartStore(), Environment.ProcessPath ?? string.Empty); }
        catch (AutoStartException error) { autoStartInitializationError = error.Code; }
        timer.Tick += OnTimerTick;
    }
    private static string T(string key) => TextCatalog.Get(key);
    private DeviceRow? Selected => Devices.SelectedItem as DeviceRow;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        FillDrives();
        await ReloadRecords();
        RefreshAutoStart(reportFailure: true);
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
        var rows = candidates.Values.Select(c => new DeviceRow(c.Id, c, records.FirstOrDefault(r => r.DeviceId == c.DeviceIdHint))).ToList();
        rows.AddRange(records.Where(r => !candidates.Values.Any(c => c.DeviceIdHint == r.DeviceId)).Select(r => new DeviceRow(r.DeviceId, null, r)));
        Devices.ItemsSource = rows.OrderBy(r => r.Name, StringComparer.CurrentCulture).ToArray();
        Devices.SelectedItem = rows.FirstOrDefault(r => r.Id == selected);
        UpdateControls();
    }
    private async Task ReloadRecords()
    {
        try { records = await client.RecordsAsync(); storeUnavailable = false; }
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
        ManualAddress.Clear();
        ManualPort.Text = ManualEndpointSession.DefaultPort;
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
        bool busy = !operation.IsCompleted || supervisorBusy || closing;
        bool mounted = client.Mount.State == MountState.Mounted;
        var row = Selected;
        bool idle = client.Mount.State is MountState.Idle or MountState.Stopped or MountState.Failed;
        Pair.IsEnabled = !busy && !storeUnavailable && idle && row?.Record is null && row?.Candidate?.Protocol == CandidateProtocol.PairedV3 && row.Candidate.Pairing is not null;
        Connect.IsEnabled = !busy && !storeUnavailable && idle &&
            row?.Record?.State is (PairingRecordState.Pending or PairingRecordState.Active) && Endpoints.SelectedItem is DeviceEndpoint;
        Revoke.IsEnabled = !busy && !storeUnavailable && row?.Record is not null;
        Forget.IsEnabled = !busy && !storeUnavailable && row?.Record?.State == PairingRecordState.RevocationPending;
        DeleteConfirmed.IsEnabled = !busy && !storeUnavailable && row?.Record is { State: PairingRecordState.Active, Mode: not AccessMode.ReadOnly } && Endpoints.SelectedItem is DeviceEndpoint;
        Open.IsEnabled = !busy && mounted;
        Unmount.IsEnabled = !busy && !idle;
        Cancel.IsEnabled = !busy ? false : !closing && operationCancellation is not null;
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
        ConnectionStatus.Text = mounted && client.Connected is { } active
            ? string.Format(T("MountedAt"), active.DriveLetter, active.Record.DeviceName, T(active.Record.Mode.ToString()))
            : client.Mount.State == MountState.RecoveringWrites ? T("RecoveringWrites")
            : client.Mount.State == MountState.StopFailed ? T(client.Mount.ErrorCode ?? "unmount-not-confirmed") : T("NoMount");
        SetTrayStatus(TrayPolicy.ResolveStatus(
            storeUnavailable || discoveryFailed || client.Mount.State is MountState.Failed or MountState.StopFailed,
            mounted, busy, candidates.Count > 0));
        ObserveMount();
    }
    private void ObserveMount()
    {
        var current = client.Mount;
        if (lastMountSnapshot is not null && current.State == lastMountSnapshot.State && current.ErrorCode == lastMountSnapshot.ErrorCode &&
            current.ExitCode == lastMountSnapshot.ExitCode && current.Forced == lastMountSnapshot.Forced) return;
        diagnostics.Write(new(DiagnosticEventName.MountStateChanged,
            current.State is MountState.Failed or MountState.StopFailed ? DiagnosticLevel.Error : DiagnosticLevel.Information,
            DiagnosticCodeMap.From(current.ErrorCode),
            current.State switch { MountState.Starting => DiagnosticState.Starting, MountState.RecoveringWrites => DiagnosticState.Recovering, MountState.Mounted => DiagnosticState.Mounted,
                MountState.Stopping => DiagnosticState.Stopping, MountState.Stopped or MountState.Idle => DiagnosticState.Stopped, _ => DiagnosticState.Failed }));
        if (current.State == MountState.Mounted)
            diagnostics.Write(new(DiagnosticEventName.WinFspChecked, Code: DiagnosticResultCode.Success, State: DiagnosticState.Healthy));
        if (current.ErrorCode == "winfsp-missing")
            diagnostics.Write(new(DiagnosticEventName.WinFspChecked, DiagnosticLevel.Error, DiagnosticResultCode.WinFspMissing, DiagnosticState.Failed));
        if (current.ExitCode is not null && current.ExitCode != lastMountSnapshot?.ExitCode)
            diagnostics.Write(new(DiagnosticEventName.RcloneExited,
                current.ExitCode == 0 ? DiagnosticLevel.Information : DiagnosticLevel.Error,
                current.ExitCode == 0 ? DiagnosticResultCode.Success : DiagnosticResultCode.Failure,
                current.State is MountState.Stopped ? DiagnosticState.Stopped : DiagnosticState.Failed));
        lastMountSnapshot = current;
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
        var available = Enumerable.Range('D', 'Z' - 'D' + 1).Select(n => (char)n).Where(c => !used.Contains(c)).ToArray();
        Drives.ItemsSource = available; Drives.SelectedItem = available.Contains(current) ? current : available.FirstOrDefault();
    }
    private MountOptions Options() => new(Drives.SelectedItem is char letter ? letter : throw new ConnectionException("drive-occupied"),
        Path.Combine(AppContext.BaseDirectory, "tools", "rclone.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhoneBridge-NG", "Sessions"));
    private IProgress<ConnectionStage> Progress() => new Progress<ConnectionStage>(stage => Status.Text = T(stage.ToString()));
    private static bool Terminal(ConnectionException error) => error.Code is
        "unauthorized" or "identity-mismatch" or "record-changed" or "mode-changed" or "invalid-response";
    private static bool Terminal(MountException error) => error.Code is
        "drive-occupied" or "drive-reserved" or "winfsp-missing" or "rclone-hash-mismatch";

    private async Task SuperviseAsync()
    {
        if (supervisorBusy || !operation.IsCompleted || closing || storeUnavailable) return;
        supervisorBusy = true; UpdateControls();
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (client.Mount.State == MountState.Mounted && client.Connected is { } active)
            {
                if (!reconnect.Armed)
                    reconnect.Arm(active.Record.DeviceId, OptionsFor(active.DriveLetter), now);
                if (!reconnect.HealthDue(now)) return;
                try
                {
                    Status.Text = T("ConnectionChecking");
                    diagnostics.Write(new(DiagnosticEventName.NetworkCheckCompleted, State: DiagnosticState.Checking));
                    await client.CheckSessionAsync(active.Record.DeviceId, active.Endpoint, lifetime.Token);
                    reconnect.HealthSucceeded(DateTimeOffset.UtcNow);
                    diagnostics.Write(new(DiagnosticEventName.NetworkCheckCompleted, Code: DiagnosticResultCode.Success, State: DiagnosticState.Healthy));
                    Status.Text = T("ConnectionHealthy");
                }
                catch (ConnectionException error) when (error.Code == "operation-in-progress") { }
                catch (ConnectionException error) when (Terminal(error))
                {
                    diagnostics.Write(new(DiagnosticEventName.NetworkCheckCompleted, DiagnosticLevel.Error, DiagnosticCodeMap.From(error.Code), DiagnosticState.Failed));
                    reconnect.Suppress();
                    try { await client.StopAsync(); }
                    catch (ConnectionException stop) { Status.Text = T(stop.Code); return; }
                    if (error.Code == "mode-changed") await ReloadRecords();
                    FillDrives(); Status.Text = T(error.Code);
                }
                catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or ConnectionException)
                {
                    if (!reconnect.HealthFailed(DateTimeOffset.UtcNow))
                    {
                        diagnostics.WriteFailure(DiagnosticEventName.NetworkCheckCompleted, DiagnosticResultCode.Failure, error);
                        Status.Text = string.Format(T("ConnectionRetry"), reconnect.ConsecutiveHealthFailures, 3);
                        return;
                    }
                    Status.Text = T("ConnectionLost");
                    diagnostics.Write(new(DiagnosticEventName.NetworkCheckCompleted, DiagnosticLevel.Error, DiagnosticResultCode.Failure, DiagnosticState.Lost, reconnect.ConsecutiveHealthFailures));
                    try
                    {
                        await client.StopAsync();
                        reconnect.Disconnected(DateTimeOffset.UtcNow);
                        diagnostics.Write(new(DiagnosticEventName.ReconnectScheduled, State: DiagnosticState.Waiting));
                        FillDrives(); Status.Text = T("ReconnectWaiting");
                    }
                    catch (ConnectionException stop) { Status.Text = T(stop.Code); }
                }
                return;
            }

            if (!reconnect.Armed || client.Mount.State is not (MountState.Idle or MountState.Stopped or MountState.Failed)) return;
            if (client.Connected is not null)
            {
                try { await client.StopAsync(); }
                catch (ConnectionException stop) { Status.Text = T(stop.Code); return; }
                reconnect.Disconnected(DateTimeOffset.UtcNow); FillDrives();
            }
            var attemptTime = DateTimeOffset.UtcNow;
            if (reconnect.DeviceId is not { } reconnectDevice || !reconnect.CanReconnect(reconnectDevice, attemptTime)) return;
            DeviceEndpoint? endpoint = manualEndpoints.SelectForReconnect(reconnectDevice, candidates.Values);
            if (endpoint is null || reconnect.Options is not { } options) return;
            try
            {
                Status.Text = T("Reconnecting");
                diagnostics.Write(new(DiagnosticEventName.ConnectionStarted, State: DiagnosticState.Starting));
                var record = await client.ConnectAsync(reconnect.DeviceId!, endpoint, options, Progress(), lifetime.Token);
                reconnect.Arm(record.DeviceId, options, DateTimeOffset.UtcNow);
                diagnostics.Write(new(DiagnosticEventName.ConnectionCompleted, Code: DiagnosticResultCode.Success, State: DiagnosticState.Mounted));
                Status.Text = T("ReconnectComplete");
            }
            catch (ConnectionException error) when (Terminal(error))
            {
                diagnostics.Write(new(DiagnosticEventName.ConnectionCompleted, DiagnosticLevel.Error, DiagnosticCodeMap.From(error.Code), DiagnosticState.Failed));
                reconnect.Suppress(); Status.Text = T(error.Code);
            }
            catch (MountException error) when (Terminal(error))
            {
                diagnostics.Write(new(DiagnosticEventName.ConnectionCompleted, DiagnosticLevel.Error, DiagnosticCodeMap.From(error.Code), DiagnosticState.Failed));
                reconnect.Suppress(); Status.Text = T(error.Code);
            }
            catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or ConnectionException or MountException)
            {
                diagnostics.WriteFailure(DiagnosticEventName.ConnectionCompleted, DiagnosticResultCode.Failure, error);
                reconnect.ReconnectFailed(DateTimeOffset.UtcNow);
                var seconds = Math.Max(1, (int)Math.Ceiling((reconnect.NextAttempt - DateTimeOffset.UtcNow).TotalSeconds));
                Status.Text = string.Format(T("ReconnectBackoff"), seconds);
            }
        }
        catch (CredentialStoreException) { reconnect.Suppress(); storeUnavailable = true; Status.Text = T("StoreFailed"); }
        finally { supervisorBusy = false; UpdateControls(); }
    }

    private MountOptions OptionsFor(char letter) => new(letter,
        Path.Combine(AppContext.BaseDirectory, "tools", "rclone.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhoneBridge-NG", "Sessions"));
    private void Start(DiagnosticEventName completionEvent, Func<CancellationToken, Task> work, string successKey = "Completed")
    {
        if (!operation.IsCompleted || closing) return;
        operationCancellation = new();
        operation = Execute(completionEvent, work, successKey, operationCancellation.Token);
        UpdateControls();
    }
    private async Task Execute(DiagnosticEventName completionEvent, Func<CancellationToken, Task> work, string successKey, CancellationToken token)
    {
        // Ensure operation has been assigned before controls are recomputed.
        await Task.Yield();
        try { await work(token); diagnostics.Write(new(completionEvent, Code: DiagnosticResultCode.Success)); Status.Text = T(successKey); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { diagnostics.Write(new(completionEvent, DiagnosticLevel.Warning, DiagnosticResultCode.Cancelled)); Status.Text = T("OperationCancelled"); }
        catch (OperationCanceledException) { diagnostics.Write(new(completionEvent, DiagnosticLevel.Warning, DiagnosticResultCode.Timeout)); Status.Text = T("TimedOut"); }
        catch (ConnectionException error) { diagnostics.Write(new(completionEvent, DiagnosticLevel.Error, DiagnosticCodeMap.From(error.Code), DiagnosticState.Failed)); Status.Text = T(error.Code); }
        catch (MountException error) { diagnostics.Write(new(completionEvent, DiagnosticLevel.Error, DiagnosticCodeMap.From(error.Code), DiagnosticState.Failed)); Status.Text = T(error.Code); }
        catch (CredentialStoreException error) { diagnostics.WriteFailure(completionEvent, DiagnosticResultCode.StorageFailure, error); Status.Text = T("StoreFailed"); }
        catch (DiagnosticExportException error) { diagnostics.WriteFailure(DiagnosticEventName.DiagnosticExportFailed, DiagnosticResultCode.Failure, error); Status.Text = T("DiagnosticsExportFailed"); }
        catch (Exception error) { diagnostics.WriteFailure(completionEvent, DiagnosticResultCode.Failure, error); Status.Text = T("OperationFailed"); }
        finally
        {
            Code.Clear(); operationCancellation?.Dispose(); operationCancellation = null;
            await ReloadRecords();
            // Keep the selected letter while it is occupied by our active mount. Refill only
            // after the mount manager has confirmed that the drive disappeared.
            if (client.Mount.State != MountState.Mounted) FillDrives();
        }
    }
    private void PairClick(object sender, RoutedEventArgs e)
    {
        if (Selected?.Candidate is not { } candidate || Endpoints.SelectedItem is not DeviceEndpoint endpoint) return;
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
        var options = Options(); var progress = Progress(); reconnect.Suppress();
        diagnostics.Write(new(DiagnosticEventName.PairingStarted, State: DiagnosticState.Starting));
        Start(DiagnosticEventName.AuthenticationCompleted, async token =>
        {
            var record = await client.PairAndConnectAsync(candidate, endpoint, code, Environment.MachineName, options, progress, token);
            reconnect.Arm(record.DeviceId, options, DateTimeOffset.UtcNow);
        });
    }
    private void ConnectClick(object sender, RoutedEventArgs e)
    {
        if (Selected?.Record is not { } record || Endpoints.SelectedItem is not DeviceEndpoint endpoint) return;
        var options = Options(); var progress = Progress(); reconnect.Suppress();
        diagnostics.Write(new(DiagnosticEventName.ConnectionStarted, State: DiagnosticState.Starting));
        Start(DiagnosticEventName.ConnectionCompleted, async token =>
        {
            var connected = await client.ConnectAsync(record.DeviceId, endpoint, options, progress, token);
            reconnect.Arm(connected.DeviceId, options, DateTimeOffset.UtcNow);
        });
    }
    private void ManualAddressClick(object sender, RoutedEventArgs e)
    {
        if (Selected?.Record is not { } record) return;
        if (!manualEndpoints.TrySet(record.DeviceId, ManualAddress.Text, ManualPort.Text, out var endpoint))
        {
            diagnostics.Write(new(DiagnosticEventName.ManualEndpointChanged, DiagnosticLevel.Warning,
                DiagnosticResultCode.Failure, DiagnosticState.Failed));
            Status.Text = T("ManualEndpointInvalid");
            return;
        }
        ManualAddress.Clear();
        RefreshEndpoints(endpoint);
        diagnostics.Write(new(DiagnosticEventName.ManualEndpointChanged, Code: DiagnosticResultCode.Success,
            State: DiagnosticState.Added));
        Status.Text = T("ManualEndpointAdded");
        UpdateControls();
    }
    private void ClearManualAddressClick(object sender, RoutedEventArgs e)
    {
        if (Selected?.Record is not { } record || !manualEndpoints.Clear(record.DeviceId)) return;
        RefreshEndpoints();
        diagnostics.Write(new(DiagnosticEventName.ManualEndpointChanged, Code: DiagnosticResultCode.Success,
            State: DiagnosticState.Removed));
        Status.Text = T("ManualEndpointCleared");
        UpdateControls();
    }
    private void CancelClick(object sender, RoutedEventArgs e) => operationCancellation?.Cancel();
    private void UnmountClick(object sender, RoutedEventArgs e) { reconnect.Suppress(); Start(DiagnosticEventName.MountStateChanged, async _ => { await client.StopAsync(); }); }
    private void RevokeClick(object sender, RoutedEventArgs e)
    {
        if (Selected?.Record is not { } record || MessageBox.Show(this, T("RevokeConfirm"), "PhoneBridge NG", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        reconnect.Suppress();
        var endpoint = Endpoints.SelectedItem as DeviceEndpoint;
        Start(DiagnosticEventName.AuthenticationCompleted, async token => { if (!await client.RevokeAsync(record.DeviceId, endpoint, token)) throw new ConnectionException("revocation-unconfirmed"); });
    }
    private void ForgetClick(object sender, RoutedEventArgs e)
    {
        if (Selected?.Record is not { } record || MessageBox.Show(this, T("ForgetConfirm"), "PhoneBridge NG", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        reconnect.Suppress();
        Start(DiagnosticEventName.AuthenticationCompleted, async _ => { await client.ForgetLocallyAsync(record.DeviceId); });
    }
    private void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (Selected?.Record is not { } record || Endpoints.SelectedItem is not DeviceEndpoint endpoint) return;
        string path = DeletePath.Text;
        Start(DiagnosticEventName.ConnectionCompleted, async token =>
        {
            var preview = await client.PrepareDeletionAsync(record.DeviceId, endpoint, path, token);
            string kind = preview.Directory ? T("Directory") : T("File");
            string message = string.Format(T("DeleteConfirm"), preview.Path, kind, preview.Size);
            if (MessageBox.Show(this, message, "PhoneBridge NG", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                throw new ConnectionException("delete-cancelled");
            await client.ConfirmDeletionAsync(preview, endpoint, token);
            DeletePath.Clear();
        });
    }
    private void OpenClick(object sender, RoutedEventArgs e)
    {
        if (client.Mount.State != MountState.Mounted || client.Connected is not { } active) return;
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
        Start(DiagnosticEventName.DiagnosticExportCompleted,
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
        closing = true; reconnect.Suppress(); operationCancellation?.Cancel(); UpdateControls(); Status.Text = T("Closing");
        await operation;
        try
        {
            await client.DisposeAsync(); lifetime.Cancel(); await discoveryTask;
            timer.Stop(); lifetime.Dispose(); closed = true; Close();
        }
        catch { closing = false; ShowFromTray(); Status.Text = T("unmount-not-confirmed"); UpdateControls(); }
    }

    private sealed record DeviceRow(string Id, DeviceCandidate? Candidate, PairingRecord? Record)
    {
        public string Name => Record?.DeviceName ?? Candidate!.DisplayName;
        public string Address => Candidate is null ? T("Offline") : string.Join(", ", Candidate.Endpoints.Select(p => p.Address));
        public string Mode => Record is null ? T("NotPaired") : T(Record.Mode.ToString());
        public string State => Record is not null ? T(Record.State.ToString()) : Candidate?.Protocol == CandidateProtocol.ExperimentalV2 ? T("Experimental") : Candidate?.Pairing is null ? T("EnablePairing") : T("PairingAvailable");
    }
}
