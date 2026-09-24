param([string]$DotnetPath, [string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not $DotnetPath) {
    $bundled = Join-Path $projectRoot '.audit/tools/dotnet-10.0.401/dotnet.exe'
    $DotnetPath = if (Test-Path -LiteralPath $bundled) { $bundled } else { (Get-Command dotnet -ErrorAction Stop).Source }
}
$DotnetPath = (Resolve-Path -LiteralPath $DotnetPath).Path
if (-not $ResultsDirectory) { $ResultsDirectory = Join-Path $projectRoot ('.audit/windows-verification/' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$ResultsDirectory = [IO.Path]::GetFullPath($ResultsDirectory)
New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null
$savedEnvironment = @{}
$settings = @{
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'; TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'; DOTNET_NOLOGO = '1'
    DOTNET_CLI_HOME = (Join-Path $projectRoot '.audit/dotnet-home')
    NUGET_PACKAGES = (Join-Path $projectRoot '.audit/nuget-packages')
}
foreach ($key in $settings.Keys) {
    $savedEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
    [Environment]::SetEnvironmentVariable($key, $settings[$key], 'Process')
}
Push-Location (Join-Path $projectRoot 'windows')
try {
    $sdk = & $DotnetPath --version
    if ($LASTEXITCODE -ne 0 -or $sdk -ne '10.0.401') { throw 'The pinned .NET SDK 10.0.401 is required.' }
    & $DotnetPath restore PhoneBridge.Windows.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    & $DotnetPath build PhoneBridge.Windows.slnx -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & $DotnetPath test PhoneBridge.Windows.slnx -c Release --no-build --no-restore --logger 'trx' --results-directory $ResultsDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
finally {
    Pop-Location
    foreach ($key in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($key, $savedEnvironment[$key], 'Process') }
}
