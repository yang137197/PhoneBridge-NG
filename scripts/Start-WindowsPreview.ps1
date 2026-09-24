param([string]$DotnetPath, [string]$RclonePath, [switch]$NoLaunch)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not $DotnetPath) { $DotnetPath = Join-Path $projectRoot '.audit/tools/dotnet-10.0.401/dotnet.exe' }
if (-not $RclonePath) { $RclonePath = Join-Path $projectRoot '.audit/tools/rclone-v1.75.1-windows-amd64/rclone.exe' }
$DotnetPath = (Resolve-Path -LiteralPath $DotnetPath).Path
$RclonePath = (Resolve-Path -LiteralPath $RclonePath).Path
if ((Get-FileHash -LiteralPath $RclonePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne '033eee51c9ad47c2de2624b6674d355274bcd6cf0027a5f85db4437ba24ae81c') {
    throw 'The pinned rclone 1.75.1 binary is required.'
}
$saved = @{}
$settings = @{
    DOTNET_ROOT = (Split-Path $DotnetPath -Parent)
    DOTNET_CLI_HOME = (Join-Path $projectRoot '.audit/dotnet-home')
    NUGET_PACKAGES = (Join-Path $projectRoot '.audit/nuget-packages')
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
}
foreach ($key in $settings.Keys) { $saved[$key] = [Environment]::GetEnvironmentVariable($key, 'Process'); [Environment]::SetEnvironmentVariable($key, $settings[$key], 'Process') }
Push-Location (Join-Path $projectRoot 'windows')
try {
    & $DotnetPath build src/PhoneBridge.Desktop/PhoneBridge.Desktop.csproj -c Release -p:RestoreLockedMode=true --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed. Close the preview before rebuilding.' }
    $output = Join-Path $projectRoot 'windows/src/PhoneBridge.Desktop/bin/Release/net10.0-windows10.0.19041.0'
    $toolsDirectory = Join-Path $output 'tools'
    New-Item -ItemType Directory -Path $toolsDirectory -Force | Out-Null
    Copy-Item -LiteralPath $RclonePath -Destination (Join-Path $toolsDirectory 'rclone.exe')
    if (-not $NoLaunch) {
        # This is the explicitly requested interactive preview, not a background helper.
        Start-Process -FilePath (Join-Path $output 'PhoneBridge.Desktop.exe') -WorkingDirectory $output -WindowStyle Normal | Out-Null
    }
    Write-Output $output
}
finally { Pop-Location; foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key], 'Process') } }
