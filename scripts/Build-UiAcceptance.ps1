param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^r[1-9][0-9]*$')]
    [string]$Revision,
    [string]$DeviceSerial
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$revisionNumber = [int]$Revision.Substring(1)
if ($revisionNumber -gt 65535) { throw 'The acceptance revision must fit a Windows assembly version component.' }

$dotnet = Join-Path $projectRoot '.audit/tools/dotnet-10.0.401/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$gradle = Join-Path $projectRoot '.audit/tools/gradle-9.3.1/bin/gradle.bat'
$jdk = Join-Path $projectRoot '.audit/tools/jdk/jbrsdk_jcef-21.0.10-windows-x64-b1163.110'
$androidSdk = Join-Path $projectRoot '.audit/tools/android-sdk'
$adb = Join-Path $androidSdk 'platform-tools/adb.exe'
$rclone = Join-Path $projectRoot '.audit/tools/rclone-v1.75.1-windows-amd64/rclone.exe'
foreach ($required in @($dotnet, $gradle, $jdk, $androidSdk, $rclone)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Required pinned dependency is missing: $required" }
}

$acceptanceRoot = Join-Path $projectRoot '.audit/ui-acceptance'
$windowsDir = Join-Path $acceptanceRoot "windows-$Revision"
$windowsZip = Join-Path $acceptanceRoot "PhoneBridge-NG-Windows-v0.2-ui-preview-$Revision.zip"
$androidDir = Join-Path $acceptanceRoot 'android'
$androidApk = Join-Path $androidDir "PhoneBridge-NG-v0.2-ui-preview-$Revision.apk"
foreach ($target in @($windowsDir, $windowsZip, $androidApk)) {
    if (Test-Path -LiteralPath $target) { throw "Refusing to reuse an acceptance revision: $target" }
}
New-Item -ItemType Directory -Path $windowsDir -ErrorAction Stop | Out-Null
New-Item -ItemType Directory -Path $androidDir -Force | Out-Null

$savedEnvironment = @{}
$settings = @{
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    DOTNET_NOLOGO = '1'
    NUGET_PACKAGES = (Join-Path $projectRoot '.audit/nuget-packages')
    JAVA_HOME = $jdk
    ANDROID_HOME = $androidSdk
    GRADLE_USER_HOME = (Join-Path $projectRoot '.audit/gradle-user')
}
foreach ($key in $settings.Keys) {
    $savedEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
    [Environment]::SetEnvironmentVariable($key, $settings[$key], 'Process')
}
try {
    Push-Location $projectRoot
    try {
        & $dotnet restore windows/src/PhoneBridge.Desktop/PhoneBridge.Desktop.csproj --runtime win-x64 --locked-mode
        if ($LASTEXITCODE -ne 0) { throw 'Windows locked restore failed.' }
        $assemblyVersion = "0.2.0.$revisionNumber"
        & $dotnet publish windows/src/PhoneBridge.Desktop/PhoneBridge.Desktop.csproj `
            --configuration Release --runtime win-x64 --self-contained true --no-restore --output $windowsDir `
            -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false `
            -p:AssemblyVersion=$assemblyVersion -p:FileVersion=$assemblyVersion `
            -p:InformationalVersion="0.2.0-ui-preview-$Revision"
        if ($LASTEXITCODE -ne 0) { throw 'Windows UI acceptance publish failed.' }
        & $gradle -p (Join-Path $projectRoot 'android') :app:assembleUiPreview :app:lintUiPreview `
            "-PuiPreviewRevision=$Revision" --dependency-verification strict --console plain
        if ($LASTEXITCODE -ne 0) { throw 'Android UI acceptance build or lint failed.' }
    }
    finally { Pop-Location }
}
finally {
    foreach ($key in $savedEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($key, $savedEnvironment[$key], 'Process')
    }
}

$tools = New-Item -ItemType Directory -Path (Join-Path $windowsDir 'tools')
Copy-Item -LiteralPath $rclone -Destination (Join-Path $tools.FullName 'rclone.exe')
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $windowsDir 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $projectRoot 'NOTICE.md') -Destination $windowsDir
Compress-Archive -Path (Join-Path $windowsDir '*') -DestinationPath $windowsZip -CompressionLevel Optimal

$builtApk = Join-Path $projectRoot 'android/app/build/outputs/apk/uiPreview/app-uiPreview.apk'
if (-not (Test-Path -LiteralPath $builtApk)) { throw 'The Android UI acceptance APK was not produced.' }
Copy-Item -LiteralPath $builtApk -Destination $androidApk

$packageName = "org.phonebridge.ng.uipreview$Revision"
if ($DeviceSerial) {
    & $adb -s $DeviceSerial install $androidApk
    if ($LASTEXITCODE -ne 0) { throw 'Fresh Android UI acceptance installation failed.' }
    & $adb -s $DeviceSerial shell pm grant $packageName android.permission.POST_NOTIFICATIONS
    if ($LASTEXITCODE -ne 0) { throw 'Notification permission provisioning failed.' }
    & $adb -s $DeviceSerial shell appops set $packageName MANAGE_EXTERNAL_STORAGE allow
    if ($LASTEXITCODE -ne 0) { throw 'Test storage permission provisioning failed.' }
    & $adb -s $DeviceSerial shell dumpsys deviceidle whitelist "+$packageName" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Test battery exemption provisioning failed.' }
}

Get-FileHash -Algorithm SHA256 -LiteralPath $windowsZip, $androidApk
Write-Output "Windows launch: PhoneBridge.Desktop.exe --ui-preview"
Write-Output "Windows isolated data revision: 0.2.0.$revisionNumber"
Write-Output "Android fresh package: $packageName"
