param([string]$GradlePath, [string]$JdkHome, [string]$AndroidSdk)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not $GradlePath) { $GradlePath = Join-Path $projectRoot '.audit/tools/gradle-9.3.1/bin/gradle.bat' }
if (-not $JdkHome) { $JdkHome = Join-Path $projectRoot '.audit/tools/jdk/jbrsdk_jcef-21.0.10-windows-x64-b1163.110' }
if (-not $AndroidSdk) { $AndroidSdk = Join-Path $projectRoot '.audit/tools/android-sdk' }
$GradlePath = (Resolve-Path -LiteralPath $GradlePath).Path
$JdkHome = (Resolve-Path -LiteralPath $JdkHome).Path
$AndroidSdk = (Resolve-Path -LiteralPath $AndroidSdk).Path
$savedEnvironment = @{}
$settings = @{
    JAVA_HOME = $JdkHome
    ANDROID_HOME = $AndroidSdk
    GRADLE_USER_HOME = (Join-Path $projectRoot '.audit/gradle-user')
}
foreach ($key in $settings.Keys) {
    $savedEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
    [Environment]::SetEnvironmentVariable($key, $settings[$key], 'Process')
}
try {
    $version = & $GradlePath --version --console plain
    if ($LASTEXITCODE -ne 0 -or -not ($version -match '^Gradle 9\.3\.1$')) { throw 'The pinned Gradle 9.3.1 is required.' }
    if (-not (Test-Path -LiteralPath (Join-Path $AndroidSdk 'platforms/android-36/android.jar'))) { throw 'Android platform 36 is required.' }
    if (-not (Test-Path -LiteralPath (Join-Path $AndroidSdk 'build-tools/36.0.0/source.properties'))) { throw 'Android build tools 36.0.0 are required.' }
    & $GradlePath -p (Join-Path $projectRoot 'android/credentials-store') assembleDebug assembleRelease assembleDebugAndroidTest lintDebug lintRelease --dependency-verification strict --console plain
    if ($LASTEXITCODE -ne 0) { throw 'Android credentials build or lint failed.' }
}
finally {
    foreach ($key in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($key, $savedEnvironment[$key], 'Process') }
}
