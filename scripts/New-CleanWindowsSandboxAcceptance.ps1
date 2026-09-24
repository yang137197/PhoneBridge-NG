param(
    [switch]$Launch
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$delivery = [IO.Path]::GetFullPath((Join-Path $projectRoot '.audit\delivery\output'))
$runsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '.audit\runs\P1-046'))
$runId = [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss')
$hostShare = Join-Path $runsRoot "sandbox-$runId"
$bootstrapDirectory = Join-Path $hostShare 'bootstrap'
$evidence = Join-Path $hostShare 'evidence'
$bootstrap = Join-Path $bootstrapDirectory 'Start-Acceptance.ps1'
$configuration = Join-Path $hostShare 'PhoneBridge-CleanInstall.wsb'
$manifestPath = Join-Path $delivery 'delivery-manifest.json'
$sandboxExe = Join-Path $env:WINDIR 'System32\WindowsSandbox.exe'
$expectedInstallerHash = '02B63BE25590FD1F86BC661CF15B7A3941E2FE13440920AAA0F850B4E7961262'
$utf8 = [Text.UTF8Encoding]::new($false)

function ConvertTo-XmlText([string]$Value) {
    return [Security.SecurityElement]::Escape($Value)
}

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'Formal delivery manifest is missing.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -DateKind String
if ($manifest.release_kind -cne 'third-party-sideload' -or $manifest.android.signing_mode -cne 'third-party-release') {
    throw 'Default delivery is not the formal third-party release.'
}
$installerPath = Join-Path $delivery ([string]$manifest.windows.installer.file)
$installerHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
if ($installerHash -ne $expectedInstallerHash -or $installerHash -ne ([string]$manifest.windows.installer.sha256).ToUpperInvariant()) {
    throw 'Formal installer hash mismatch.'
}
if (@(Get-Process PhoneBridge.Desktop, rclone -ErrorAction SilentlyContinue).Count -ne 0 -or (Test-Path 'P:\')) {
    throw 'Host PhoneBridge resources must be stopped before preparing the sandbox.'
}
if (@(Get-Process WindowsSandbox, WindowsSandboxRemoteSession, WindowsSandboxServer -ErrorAction SilentlyContinue).Count -ne 0) {
    throw 'Close the existing Windows Sandbox before preparing a new acceptance run.'
}

New-Item -ItemType Directory -Path $bootstrapDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$bootstrapContent = @'
$ErrorActionPreference = 'Stop'
$evidence = 'C:\PhoneBridge-Evidence'
$resultPath = Join-Path $evidence 'sandbox-result.json'
$transcriptPath = Join-Path $evidence 'sandbox-console.txt'
Start-Transcript -Path $transcriptPath -Force | Out-Null
try {
    & 'C:\PhoneBridge-Tools\Test-CleanWindowsInstall.ps1' `
        -DeliveryDirectory 'C:\PhoneBridge-Delivery' `
        -EvidenceDirectory $evidence `
        -Mode Preflight `
        -AllowWindowsSandboxAdministrator
    $preflightExit = $LASTEXITCODE
    $installExit = $null
    if ($preflightExit -eq 0) {
        & 'C:\PhoneBridge-Tools\Test-CleanWindowsInstall.ps1' `
            -DeliveryDirectory 'C:\PhoneBridge-Delivery' `
            -EvidenceDirectory $evidence `
            -Mode InstallAndVerify `
            -AllowWindowsSandboxAdministrator
        $installExit = $LASTEXITCODE
    }
    $result = [ordered]@{
        schema = 1
        task = 'P1-046'
        captured_at_utc = [DateTimeOffset]::UtcNow.ToString('O')
        preflight_exit_code = $preflightExit
        install_exit_code = $installExit
        passed = ($preflightExit -eq 0) -and ($installExit -in @(0, 3010))
    }
    [IO.File]::WriteAllText($resultPath, ($result | ConvertTo-Json -Depth 5) + "`n", [Text.UTF8Encoding]::new($false))
    Write-Host ''
    Write-Host 'PhoneBridge clean-install acceptance finished.'
    Write-Host "Preflight exit: $preflightExit; install exit: $installExit"
    Write-Host 'Evidence has been copied to the mapped host folder.'
}
catch {
    $failure = [ordered]@{
        schema = 1
        task = 'P1-046'
        captured_at_utc = [DateTimeOffset]::UtcNow.ToString('O')
        passed = $false
        error_type = $_.Exception.GetType().FullName
        error_message = $_.Exception.Message
    }
    [IO.File]::WriteAllText($resultPath, ($failure | ConvertTo-Json -Depth 5) + "`n", [Text.UTF8Encoding]::new($false))
    Write-Error $_
}
finally {
    Stop-Transcript | Out-Null
}
Read-Host 'Press Enter to close this window after reviewing the result'
'@
[IO.File]::WriteAllText($bootstrap, $bootstrapContent, $utf8)

$deliveryXml = ConvertTo-XmlText $delivery
$scriptsXml = ConvertTo-XmlText ([IO.Path]::GetFullPath($PSScriptRoot))
$bootstrapXml = ConvertTo-XmlText $bootstrapDirectory
$evidenceXml = ConvertTo-XmlText $evidence
$configurationContent = @"
<Configuration>
  <VGpu>Disable</VGpu>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>$deliveryXml</HostFolder>
      <SandboxFolder>C:\PhoneBridge-Delivery</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$scriptsXml</HostFolder>
      <SandboxFolder>C:\PhoneBridge-Tools</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$bootstrapXml</HostFolder>
      <SandboxFolder>C:\PhoneBridge-Bootstrap</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$evidenceXml</HostFolder>
      <SandboxFolder>C:\PhoneBridge-Evidence</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <Networking>Disable</Networking>
  <AudioInput>Disable</AudioInput>
  <VideoInput>Disable</VideoInput>
  <PrinterRedirection>Disable</PrinterRedirection>
  <ClipboardRedirection>Disable</ClipboardRedirection>
  <LogonCommand>
    <Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\PhoneBridge-Bootstrap\Start-Acceptance.ps1</Command>
  </LogonCommand>
</Configuration>
"@
[IO.File]::WriteAllText($configuration, $configurationContent, $utf8)

$prepared = [ordered]@{
    schema = 1
    task = 'P1-046'
    created_at_utc = [DateTimeOffset]::UtcNow.ToString('O')
    host_os = (Get-CimInstance Win32_OperatingSystem).Caption
    hypervisor_present = (Get-CimInstance Win32_ComputerSystem).HypervisorPresent
    windows_sandbox_available = Test-Path -LiteralPath $sandboxExe -PathType Leaf
    networking = 'disabled'
    vgpu = 'disabled'
    delivery_read_only = $true
    scripts_read_only = $true
    bootstrap_read_only = $true
    evidence_read_write = $true
    installer_sha256 = $installerHash
    configuration = $configuration
    evidence_directory = $evidence
}
[IO.File]::WriteAllText((Join-Path $hostShare 'prepared.json'), ($prepared | ConvertTo-Json -Depth 6) + "`n", $utf8)
$prepared | ConvertTo-Json -Depth 6

if ($Launch) {
    if (-not (Test-Path -LiteralPath $sandboxExe -PathType Leaf)) {
        throw 'Windows Sandbox is not enabled. Enable Containers-DisposableClientVM and restart first.'
    }
    # Start-Process joins ArgumentList values into one command line. Preserve
    # quotes because the generated .wsb path can contain spaces.
    Start-Process -FilePath $sandboxExe -ArgumentList ('"{0}"' -f $configuration)
}
