param(
    [string]$DeliveryDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) '.audit\delivery\output'),
    [string]$EvidenceDirectory,
    [ValidateSet('Preflight', 'InstallAndVerify')]
    [string]$Mode = 'Preflight',
    [switch]$AllowWindowsSandboxAdministrator
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$expectedUninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{937C0A0D-1CE2-41C6-B750-0C7137829D0A}_is1'
$expectedAppDirectory = Join-Path $env:LOCALAPPDATA 'Programs\PhoneBridge NG'
$evidenceFileName = 'clean-windows-install-verification.json'
$setupLogFileName = 'phonebridge-setup.log'

function Get-RegistryString(
    [Microsoft.Win32.RegistryHive]$Hive,
    [Microsoft.Win32.RegistryView]$View,
    [string]$SubKey,
    [string]$Name
) {
    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey($Hive, $View)
    try {
        $key = $baseKey.OpenSubKey($SubKey, $false)
        if ($null -eq $key) { return $null }
        try {
            $value = $key.GetValue($Name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            if ($value -is [string]) { return $value }
            return $null
        }
        finally { $key.Dispose() }
    }
    finally { $baseKey.Dispose() }
}

function Test-RegistryKey(
    [Microsoft.Win32.RegistryHive]$Hive,
    [Microsoft.Win32.RegistryView]$View,
    [string]$SubKey
) {
    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey($Hive, $View)
    try {
        $key = $baseKey.OpenSubKey($SubKey, $false)
        if ($null -eq $key) { return $false }
        $key.Dispose()
        return $true
    }
    finally { $baseKey.Dispose() }
}

function Get-WinFspState {
    $locations = @()
    foreach ($view in @([Microsoft.Win32.RegistryView]::Registry32, [Microsoft.Win32.RegistryView]::Registry64)) {
        $installDirectory = Get-RegistryString ([Microsoft.Win32.RegistryHive]::LocalMachine) $view 'SOFTWARE\WinFsp' 'InstallDir'
        if (-not [string]::IsNullOrWhiteSpace($installDirectory)) {
            $locations += [ordered]@{
                registry_view = $view.ToString()
                registration_present = $true
                dll_present = Test-Path -LiteralPath (Join-Path $installDirectory 'bin\winfsp-x64.dll') -PathType Leaf
            }
        }
    }
    $servicePresent = @(
        Get-Service -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like 'WinFsp*' -or $_.DisplayName -like '*WinFsp*' }
    ).Count -gt 0
    return [ordered]@{
        present = $locations.Count -gt 0 -or $servicePresent
        registry_locations = $locations
        service_present = $servicePresent
    }
}

function Test-PendingRestart {
    $componentBasedServicing = Test-RegistryKey ([Microsoft.Win32.RegistryHive]::LocalMachine) `
        ([Microsoft.Win32.RegistryView]::Registry64) 'SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending'
    $windowsUpdate = Test-RegistryKey ([Microsoft.Win32.RegistryHive]::LocalMachine) `
        ([Microsoft.Win32.RegistryView]::Registry64) 'SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired'
    return $componentBasedServicing -or $windowsUpdate
}

function Test-IsElevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-PathWithin([string]$Path, [string]$Parent) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullParent = [IO.Path]::GetFullPath($Parent).TrimEnd([IO.Path]::DirectorySeparatorChar)
    return $fullPath.Equals($fullParent, [StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.StartsWith($fullParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Write-Evidence([Collections.IDictionary]$Evidence, [string]$Path) {
    $json = ($Evidence | ConvertTo-Json -Depth 10) + "`n"
    [IO.File]::WriteAllText($Path, $json, [Text.UTF8Encoding]::new($false))
    Write-Output $json
}

$DeliveryDirectory = [IO.Path]::GetFullPath($DeliveryDirectory)
if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $EvidenceDirectory = Join-Path (Split-Path $DeliveryDirectory -Parent) `
        ('PhoneBridge-CleanInstall-Evidence-' + [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss'))
}
$EvidenceDirectory = [IO.Path]::GetFullPath($EvidenceDirectory)
if ((Test-PathWithin $EvidenceDirectory $DeliveryDirectory) -or (Test-PathWithin $DeliveryDirectory $EvidenceDirectory)) {
    throw 'EvidenceDirectory and DeliveryDirectory must be separate paths.'
}

if (-not (Test-Path -LiteralPath $EvidenceDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $EvidenceDirectory | Out-Null
}
$evidencePath = Join-Path $EvidenceDirectory $evidenceFileName
$setupLogPath = Join-Path $EvidenceDirectory $setupLogFileName

$issues = [Collections.Generic.List[string]]::new()
$manifestPath = Join-Path $DeliveryDirectory 'delivery-manifest.json'
$manifest = $null
$installerPath = $null
$installerSha256 = $null
$expectedInstallerSha256 = $null

if (-not [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows)) {
    $issues.Add('windows-required')
}
if ([Environment]::OSVersion.Version.Build -lt 22000) { $issues.Add('windows-11-required') }
if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [Runtime.InteropServices.Architecture]::X64) {
    $issues.Add('windows-x64-required')
}
$isElevated = Test-IsElevated
$sandboxAdministratorAllowed = $AllowWindowsSandboxAdministrator -and
    $isElevated -and $env:USERNAME -ceq 'WDAGUtilityAccount'
if ($isElevated -and -not $sandboxAdministratorAllowed) {
    $issues.Add('run-from-standard-user-session')
}
if (Test-PendingRestart) { $issues.Add('restart-pending-before-test') }

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    $issues.Add('delivery-manifest-missing')
}
else {
    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $installerFile = [string]$manifest.windows.installer.file
        $expectedInstallerSha256 = [string]$manifest.windows.installer.sha256
        if ([string]::IsNullOrWhiteSpace($installerFile) -or [IO.Path]::GetFileName($installerFile) -cne $installerFile) {
            $issues.Add('installer-file-invalid-in-manifest')
        }
        else { $installerPath = Join-Path $DeliveryDirectory $installerFile }
        if ($null -eq $installerPath -or -not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
            $issues.Add('installer-missing')
        }
        elseif ($expectedInstallerSha256 -notmatch '^[A-Fa-f0-9]{64}$') {
            $issues.Add('installer-hash-invalid-in-manifest')
        }
        else {
            $installerSha256 = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
            if ($installerSha256 -ne $expectedInstallerSha256.ToUpperInvariant()) {
                $issues.Add('installer-hash-mismatch')
            }
        }
    }
    catch { $issues.Add('delivery-manifest-invalid') }
}

$winFspBefore = Get-WinFspState
if ($winFspBefore.present) { $issues.Add('winfsp-already-present') }
$uninstallPresent = Test-RegistryKey ([Microsoft.Win32.RegistryHive]::CurrentUser) `
    ([Microsoft.Win32.RegistryView]::Registry64) $expectedUninstallKey
if ($uninstallPresent -or (Test-Path -LiteralPath $expectedAppDirectory)) {
    $issues.Add('phonebridge-already-installed')
}
if (@(Get-Process -Name 'PhoneBridge.Desktop' -ErrorAction SilentlyContinue).Count -gt 0) {
    $issues.Add('phonebridge-process-running')
}
if (@(Get-Process -Name 'msiexec' -ErrorAction SilentlyContinue).Count -gt 0) {
    $issues.Add('windows-installer-busy')
}

$evidence = [ordered]@{
    schema = 1
    task = 'P1-037'
    generated_at_utc = [DateTimeOffset]::UtcNow.ToString('O')
    mode = $Mode
    preflight = [ordered]@{
        eligible = $issues.Count -eq 0
        issues = @($issues)
        windows_build = [Environment]::OSVersion.Version.Build
        os_architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        elevated = $isElevated
        windows_sandbox_administrator_allowed = $sandboxAdministratorAllowed
        pending_restart = Test-PendingRestart
        winfsp_present = $winFspBefore.present
        phonebridge_installed = $uninstallPresent -or (Test-Path -LiteralPath $expectedAppDirectory)
        installer_sha256 = $installerSha256
        installer_matches_manifest = $null -ne $installerSha256 -and $installerSha256 -eq $expectedInstallerSha256
    }
    installation = $null
}

if ($issues.Count -gt 0) {
    Write-Evidence $evidence $evidencePath
    exit 2
}
if ($Mode -eq 'Preflight') {
    Write-Evidence $evidence $evidencePath
    exit 0
}

$beforePhoneBridgeIds = @(Get-Process -Name 'PhoneBridge.Desktop' -ErrorAction SilentlyContinue | ForEach-Object Id)
$beforeRcloneIds = @(Get-Process -Name 'rclone' -ErrorAction SilentlyContinue | ForEach-Object Id)
if (Test-Path -LiteralPath $setupLogPath) { [IO.File]::Delete($setupLogPath) }

$installerArguments = @(
    '/VERYSILENT'
    '/SUPPRESSMSGBOXES'
    '/NORESTART'
    '/RESTARTEXITCODE=3010'
    '/LANG=chinesesimplified'
    ('/LOG="{0}"' -f $setupLogPath)
)
$installerProcess = Start-Process -FilePath $installerPath -ArgumentList $installerArguments -Wait -PassThru
$installerExitCode = $installerProcess.ExitCode
$restartRequired = $installerExitCode -eq 3010
$postIssues = [Collections.Generic.List[string]]::new()
if ($installerExitCode -notin @(0, 3010)) { $postIssues.Add("installer-exit-$installerExitCode") }

$winFspAfter = Get-WinFspState
if (-not $winFspAfter.present -or -not ($winFspAfter.registry_locations | Where-Object dll_present)) {
    $postIssues.Add('winfsp-not-detected-after-install')
}
$installedExe = Join-Path $expectedAppDirectory 'PhoneBridge.Desktop.exe'
$installedRclone = Join-Path $expectedAppDirectory 'tools\rclone.exe'
$installedLicense = Join-Path $expectedAppDirectory 'LICENSE.txt'
$installedNotice = Join-Path $expectedAppDirectory 'notices\NOTICE.md'
foreach ($requiredFile in @($installedExe, $installedRclone, $installedLicense, $installedNotice)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        $postIssues.Add('required-product-file-missing')
        break
    }
}

$rcloneSha256 = $null
if (Test-Path -LiteralPath $installedRclone -PathType Leaf) {
    $rcloneSha256 = (Get-FileHash -LiteralPath $installedRclone -Algorithm SHA256).Hash
    if ($rcloneSha256 -ne ([string]$manifest.windows.rclone.sha256).ToUpperInvariant()) {
        $postIssues.Add('installed-rclone-hash-mismatch')
    }
}
$uninstallAfter = Test-RegistryKey ([Microsoft.Win32.RegistryHive]::CurrentUser) `
    ([Microsoft.Win32.RegistryView]::Registry64) $expectedUninstallKey
if (-not $uninstallAfter) { $postIssues.Add('uninstall-registration-missing') }
$newPhoneBridgeProcesses = @(
    Get-Process -Name 'PhoneBridge.Desktop' -ErrorAction SilentlyContinue |
        Where-Object { $_.Id -notin $beforePhoneBridgeIds }
).Count
$newRcloneProcesses = @(
    Get-Process -Name 'rclone' -ErrorAction SilentlyContinue |
        Where-Object { $_.Id -notin $beforeRcloneIds }
).Count
if ($newPhoneBridgeProcesses -ne 0) { $postIssues.Add('phonebridge-started-during-silent-install') }
if ($newRcloneProcesses -ne 0) { $postIssues.Add('rclone-started-during-silent-install') }
if (-not (Test-Path -LiteralPath $setupLogPath -PathType Leaf)) { $postIssues.Add('setup-log-missing') }

$evidence.installation = [ordered]@{
    passed = $postIssues.Count -eq 0
    issues = @($postIssues)
    installer_exit_code = $installerExitCode
    restart_required = $restartRequired
    winfsp_detected = $winFspAfter.present
    winfsp_dll_detected = @($winFspAfter.registry_locations | Where-Object dll_present).Count -gt 0
    client_executable_present = Test-Path -LiteralPath $installedExe -PathType Leaf
    rclone_sha256 = $rcloneSha256
    rclone_matches_manifest = $null -ne $rcloneSha256 -and $rcloneSha256 -eq ([string]$manifest.windows.rclone.sha256)
    license_present = Test-Path -LiteralPath $installedLicense -PathType Leaf
    notice_present = Test-Path -LiteralPath $installedNotice -PathType Leaf
    uninstall_registration_present = $uninstallAfter
    new_phonebridge_processes = $newPhoneBridgeProcesses
    new_rclone_processes = $newRcloneProcesses
    setup_log_file = $setupLogFileName
}
Write-Evidence $evidence $evidencePath
if ($postIssues.Count -gt 0) { exit 3 }
if ($restartRequired) { exit 3010 }
exit 0
