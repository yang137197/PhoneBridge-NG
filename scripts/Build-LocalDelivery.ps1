param(
    [string]$DotnetPath,
    [string]$GradlePath,
    [string]$JdkHome,
    [string]$AndroidSdk,
    [string]$RclonePath,
    [string]$WinFspMsi,
    [string]$InnoCompiler,
    [string]$AndroidKeystore,
    [string]$AndroidSigningIdentity,
    [ValidateSet('LocalTest', 'ThirdPartyRelease')]
    [string]$AndroidSigningMode = 'LocalTest',
    [string]$AndroidStorePasswordEnvironment = 'PBNG_ANDROID_STORE_PASSWORD',
    [string]$AndroidKeyPasswordEnvironment = 'PBNG_ANDROID_KEY_PASSWORD',
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$productVersion = '0.1.0'
$rcloneVersion = '1.75.1'
$rcloneSha256 = '033EEE51C9AD47C2DE2624B6674D355274BCD6CF0027A5F85DB4437BA24AE81C'
$winFspVersion = '2.1.25156'
$winFspSha256 = '073A70E00F77423E34BED98B86E600DEF93393BA5822204FAC57A29324DB9F7A'
$innoVersion = '7.1.0'

$projectRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
if (-not $DotnetPath) { $DotnetPath = Join-Path $projectRoot '.audit/tools/dotnet-10.0.401/dotnet.exe' }
if (-not $GradlePath) { $GradlePath = Join-Path $projectRoot '.audit/tools/gradle-9.3.1/bin/gradle.bat' }
if (-not $JdkHome) { $JdkHome = Join-Path $projectRoot '.audit/tools/jdk/jbrsdk_jcef-21.0.10-windows-x64-b1163.110' }
if (-not $AndroidSdk) { $AndroidSdk = Join-Path $projectRoot '.audit/tools/android-sdk' }
if (-not $RclonePath) { $RclonePath = Join-Path $projectRoot '.audit/tools/rclone-v1.75.1-windows-amd64/rclone.exe' }
if (-not $WinFspMsi) { $WinFspMsi = Join-Path $projectRoot '.audit/downloads/winfsp-2.1.25156.msi' }
if (-not $InnoCompiler) { $InnoCompiler = Join-Path $projectRoot '.audit/tools/innosetup-7.1.0/ISCC.exe' }
$deliveryRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '.audit/delivery'))
if (-not $OutputRoot) { $OutputRoot = $deliveryRoot }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if ($OutputRoot -ne $deliveryRoot -and -not $OutputRoot.StartsWith($deliveryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputRoot must stay within $deliveryRoot"
}

function Resolve-RequiredFile([string]$Path, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Label was not found: $Path" }
    return (Resolve-Path -LiteralPath $Path).Path
}

function Resolve-RequiredDirectory([string]$Path, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { throw "$Label was not found: $Path" }
    return (Resolve-Path -LiteralPath $Path).Path
}

function Assert-Sha256([string]$Path, [string]$Expected, [string]$Label) {
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if ($actual -ne $Expected) { throw "$Label SHA-256 mismatch. Expected $Expected; actual $actual" }
}

function Invoke-Checked([scriptblock]$Command, [string]$Failure) {
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Failure (exit code $LASTEXITCODE)" }
}

function Test-PathWithin([string]$Path, [string]$Parent) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullParent = [IO.Path]::GetFullPath($Parent).TrimEnd([IO.Path]::DirectorySeparatorChar)
    return $fullPath.Equals($fullParent, [StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.StartsWith($fullParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-EnvironmentName([string]$Name, [string]$Label) {
    if ([string]::IsNullOrWhiteSpace($Name) -or $Name -notmatch '^[A-Za-z_][A-Za-z0-9_]{0,63}$') {
        throw "$Label must name one process environment variable."
    }
    if ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($Name, 'Process'))) {
        throw "$Label is not set in the current process environment."
    }
}

function ConvertTo-IdentityUtc([object]$Value) {
    if ($Value -is [DateTimeOffset]) { return $Value.ToUniversalTime() }
    if ($Value -is [DateTime]) { return ([DateTimeOffset]::new($Value)).ToUniversalTime() }
    $parsed = [DateTimeOffset]::MinValue
    if ([DateTimeOffset]::TryParse([string]$Value, [ref]$parsed)) { return $parsed.ToUniversalTime() }
    throw 'invalid identity timestamp'
}

function Read-AndroidSigningIdentity([string]$Path) {
    $resolved = Resolve-RequiredFile $Path 'Android signing identity record'
    if (Test-PathWithin $resolved $projectRoot) {
        throw 'The Android signing identity record must be stored outside the project directory.'
    }
    if ((Get-Item -LiteralPath $resolved -Force).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
        throw 'The Android signing identity record cannot be a reparse point.'
    }
    try { $identity = Get-Content -LiteralPath $resolved -Raw | ConvertFrom-Json }
    catch { throw 'The Android signing identity record is not valid JSON.' }
    $requiredProperties = @(
        'schema', 'product', 'purpose', 'key_alias', 'keystore_type', 'keystore_file',
        'keystore_sha256', 'certificate_sha256', 'certificate_subject',
        'certificate_not_before_utc', 'certificate_not_after_utc', 'created_at_utc', 'password_storage'
    )
    foreach ($property in $requiredProperties) {
        if ($null -eq $identity.PSObject.Properties[$property]) {
            throw "The Android signing identity record is missing required property: $property"
        }
    }
    if ($identity.schema -ne 1 -or $identity.product -cne 'PhoneBridge NG' -or
        $identity.purpose -cne 'Android third-party release signing' -or
        $identity.keystore_type -cne 'PKCS12' -or
        $identity.password_storage -cne 'operator-managed; not stored by PhoneBridge NG') {
        throw 'The Android signing identity record has an unsupported schema or purpose.'
    }
    $alias = [string]$identity.key_alias
    if ($alias -cne 'phonebridge-release') {
        throw 'The Android signing identity record does not use the required release alias.'
    }
    $keystoreFile = [string]$identity.keystore_file
    if ([string]::IsNullOrWhiteSpace($keystoreFile) -or
        [IO.Path]::GetFileName($keystoreFile) -cne $keystoreFile -or
        [IO.Path]::GetExtension($keystoreFile) -cne '.p12') {
        throw 'The Android signing identity record has an invalid keystore file name.'
    }
    $keystoreSha256 = [string]$identity.keystore_sha256
    $certificateSha256 = [string]$identity.certificate_sha256
    if ($keystoreSha256 -notmatch '^[0-9A-Fa-f]{64}$' -or $certificateSha256 -notmatch '^[0-9A-Fa-f]{64}$') {
        throw 'The Android signing identity record has an invalid SHA-256 value.'
    }
    $subject = [string]$identity.certificate_subject
    if ([string]::IsNullOrWhiteSpace($subject) -or $subject -match '(^|,)\s*CN=Android Debug(,|$)') {
        throw 'The Android signing identity record has an invalid certificate subject.'
    }
    try {
        $notBefore = ConvertTo-IdentityUtc $identity.certificate_not_before_utc
        $notAfter = ConvertTo-IdentityUtc $identity.certificate_not_after_utc
    }
    catch { throw 'The Android signing identity record has an invalid certificate validity period.' }
    if ($notAfter -le $notBefore) {
        throw 'The Android signing identity record has an invalid certificate validity period.'
    }
    $now = [DateTimeOffset]::UtcNow
    if ($notBefore -gt $now.AddMinutes(5) -or $notAfter -le $now) {
        throw 'The Android signing identity certificate is not currently valid.'
    }
    $identityDirectory = Split-Path $resolved -Parent
    if ((Get-Item -LiteralPath $identityDirectory -Force).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
        throw 'The Android signing identity directory cannot be a reparse point.'
    }
    $keystore = Resolve-RequiredFile (Join-Path $identityDirectory $keystoreFile) 'Android release keystore from identity record'
    if ((Get-Item -LiteralPath $keystore -Force).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
        throw 'The Android release keystore cannot be a reparse point.'
    }
    Assert-Sha256 $keystore $keystoreSha256.ToUpperInvariant() 'Android release keystore identity binding'
    return [ordered]@{
        identity_path = $resolved
        keystore_path = $keystore
        key_alias = $alias
        keystore_sha256 = $keystoreSha256.ToUpperInvariant()
        certificate_sha256 = $certificateSha256.ToUpperInvariant()
        certificate_subject = $subject
        certificate_not_before_utc_ticks = ($notBefore.UtcDateTime).Ticks
        certificate_not_after_utc_ticks = ($notAfter.UtcDateTime).Ticks
    }
}

$DotnetPath = Resolve-RequiredFile $DotnetPath '.NET SDK'
$GradlePath = Resolve-RequiredFile $GradlePath 'Gradle'
$JdkHome = Resolve-RequiredDirectory $JdkHome 'JDK'
$AndroidSdk = Resolve-RequiredDirectory $AndroidSdk 'Android SDK'
$RclonePath = Resolve-RequiredFile $RclonePath 'rclone'
$WinFspMsi = Resolve-RequiredFile $WinFspMsi 'WinFsp MSI'
$InnoCompiler = Resolve-RequiredFile $InnoCompiler 'Inno Setup compiler'
$zipAlign = Resolve-RequiredFile (Join-Path $AndroidSdk 'build-tools/36.0.0/zipalign.exe') 'zipalign'
$apkSigner = Resolve-RequiredFile (Join-Path $AndroidSdk 'build-tools/36.0.0/apksigner.bat') 'apksigner'
$keyTool = Resolve-RequiredFile (Join-Path $JdkHome 'bin/keytool.exe') 'keytool'

if ($AndroidSigningMode -eq 'LocalTest') {
    if (-not [string]::IsNullOrWhiteSpace($AndroidSigningIdentity)) {
        throw 'LocalTest does not accept -AndroidSigningIdentity.'
    }
    if (-not $AndroidKeystore) { $AndroidKeystore = Join-Path $env:USERPROFILE '.android/debug.keystore' }
    $AndroidKeystore = Resolve-RequiredFile $AndroidKeystore 'local Android test keystore'
    $AndroidKeyAlias = 'androiddebugkey'
    $keyToolPasswordArguments = @('-storepass', 'android')
    $apkSignerPasswordArguments = @('--ks-pass', 'pass:android', '--key-pass', 'pass:android')
    $apkFileName = "PhoneBridge-NG-$productVersion-local-test.apk"
    $releaseKind = 'local-preview'
    $signingDescription = 'local-test Android Debug certificate; not for public release'
    $manifestSigningMode = 'local-test'
}
else {
    if (-not [string]::IsNullOrWhiteSpace($AndroidKeystore)) {
        throw 'ThirdPartyRelease gets its keystore only from -AndroidSigningIdentity; do not pass -AndroidKeystore.'
    }
    if ([string]::IsNullOrWhiteSpace($AndroidSigningIdentity)) {
        $AndroidSigningIdentity = Join-Path $env:LOCALAPPDATA 'PhoneBridge-NG\Signing\android-release-identity.json'
    }
    $signingIdentity = Read-AndroidSigningIdentity $AndroidSigningIdentity
    $AndroidKeystore = $signingIdentity.keystore_path
    $AndroidKeyAlias = $signingIdentity.key_alias
    Assert-EnvironmentName $AndroidStorePasswordEnvironment 'Android store password environment variable'
    Assert-EnvironmentName $AndroidKeyPasswordEnvironment 'Android key password environment variable'
    $AndroidExpectedSignerSha256 = $signingIdentity.certificate_sha256
    $keyToolPasswordArguments = @('-storepass:env', $AndroidStorePasswordEnvironment)
    $apkSignerPasswordArguments = @('--ks-pass', "env:$AndroidStorePasswordEnvironment", '--key-pass', "env:$AndroidKeyPasswordEnvironment")
    $apkFileName = "PhoneBridge-NG-$productVersion.apk"
    $releaseKind = 'third-party-sideload'
    $signingDescription = 'dedicated third-party Android release certificate'
    $manifestSigningMode = 'third-party-release'
}

$certificatePem = & $keyTool -exportcert -rfc -keystore $AndroidKeystore -alias $AndroidKeyAlias @keyToolPasswordArguments 2>$null
if ($LASTEXITCODE -ne 0 -or -not $certificatePem) { throw 'The Android signing certificate could not be read from the configured keystore.' }
$preflightCertificate = $null
try {
    $preflightCertificate = [Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPem(($certificatePem -join "`n"))
    $preflightCertificateSha256 = $preflightCertificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256)
    $preflightCertificateSubject = $preflightCertificate.Subject
    $preflightCertificateNotBeforeUtcTicks = ($preflightCertificate.NotBefore.ToUniversalTime()).Ticks
    $preflightCertificateNotAfterUtcTicks = ($preflightCertificate.NotAfter.ToUniversalTime()).Ticks
}
catch { throw 'The Android signing certificate is invalid.' }
finally { if ($null -ne $preflightCertificate) { $preflightCertificate.Dispose() } }
if ($AndroidSigningMode -eq 'ThirdPartyRelease') {
    if ($preflightCertificateSubject -match '(^|,)\s*CN=Android Debug(,|$)' -or
        $preflightCertificateSha256 -ne $AndroidExpectedSignerSha256) {
        throw 'The Android release certificate is a Debug identity or does not match the expected SHA-256.'
    }
    if ($preflightCertificateSubject -cne $signingIdentity.certificate_subject) {
        throw 'The Android release certificate subject does not match the signing identity record.'
    }
    if ($preflightCertificateNotBeforeUtcTicks -ne $signingIdentity.certificate_not_before_utc_ticks) {
        throw 'The Android release certificate start time does not match the signing identity record.'
    }
    if ($preflightCertificateNotAfterUtcTicks -ne $signingIdentity.certificate_not_after_utc_ticks) {
        throw 'The Android release certificate end time does not match the signing identity record.'
    }
}

if ((& $DotnetPath --version) -ne '10.0.401') { throw 'The pinned .NET SDK 10.0.401 is required.' }
if ((& $InnoCompiler --version) -ne $innoVersion) { throw "The pinned Inno Setup $innoVersion compiler is required." }
$rcloneVersionOutput = & $RclonePath version
if ($LASTEXITCODE -ne 0 -or $rcloneVersionOutput[0] -ne "rclone v$rcloneVersion") { throw "The pinned rclone $rcloneVersion is required." }
Assert-Sha256 $RclonePath $rcloneSha256 'rclone'
Assert-Sha256 $WinFspMsi $winFspSha256 'WinFsp MSI'

$winFspSignature = Get-AuthenticodeSignature -FilePath $WinFspMsi
if ($winFspSignature.Status -ne [Management.Automation.SignatureStatus]::Valid -or
    $winFspSignature.SignerCertificate.Subject -notmatch '^CN=NAVIMATICS LLC,') {
    throw 'The pinned WinFsp MSI does not have the expected valid NAVIMATICS signature.'
}
$innoSignature = Get-AuthenticodeSignature -FilePath $InnoCompiler
if ($innoSignature.Status -ne [Management.Automation.SignatureStatus]::Valid -or
    $innoSignature.SignerCertificate.Subject -notmatch '^CN=Pyrsys B\.V\.,') {
    throw 'The Inno Setup compiler does not have the expected valid Pyrsys signature.'
}

if (Test-Path -LiteralPath $OutputRoot) {
    $resolvedOutput = [IO.Path]::GetFullPath($OutputRoot)
    if ($resolvedOutput -ne $deliveryRoot -and -not $resolvedOutput.StartsWith($deliveryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to remove an output path outside .audit/delivery.'
    }
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}
$publishDir = New-Item -ItemType Directory -Path (Join-Path $OutputRoot 'windows-publish') -Force
$outputDir = New-Item -ItemType Directory -Path (Join-Path $OutputRoot 'output') -Force
$noticesDir = New-Item -ItemType Directory -Path (Join-Path $publishDir.FullName 'notices') -Force
$licenseDir = New-Item -ItemType Directory -Path (Join-Path $noticesDir.FullName 'licenses') -Force
$toolsDir = New-Item -ItemType Directory -Path (Join-Path $publishDir.FullName 'tools') -Force

$savedEnvironment = @{}
$settings = @{
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    DOTNET_NOLOGO = '1'
    NUGET_PACKAGES = (Join-Path $projectRoot '.audit/nuget-packages')
    JAVA_HOME = $JdkHome
    ANDROID_HOME = $AndroidSdk
    GRADLE_USER_HOME = (Join-Path $projectRoot '.audit/gradle-user')
}
foreach ($key in $settings.Keys) {
    $savedEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
    [Environment]::SetEnvironmentVariable($key, $settings[$key], 'Process')
}

try {
    $gradleVersion = & $GradlePath --version --console plain
    if ($LASTEXITCODE -ne 0 -or -not ($gradleVersion -match '^Gradle 9\.3\.1$')) { throw 'The pinned Gradle 9.3.1 is required.' }
    Push-Location $projectRoot
    try {
        Invoke-Checked {
            & $DotnetPath restore windows/src/PhoneBridge.Desktop/PhoneBridge.Desktop.csproj `
                --runtime win-x64 --locked-mode
        } '.NET locked runtime restore failed'
        Invoke-Checked {
            & $DotnetPath publish windows/src/PhoneBridge.Desktop/PhoneBridge.Desktop.csproj `
                --configuration Release --runtime win-x64 --self-contained true --no-restore `
                --output $publishDir.FullName -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false
        } 'Windows self-contained publish failed'
        Invoke-Checked {
            & $GradlePath -p (Join-Path $projectRoot 'android') :app:assembleRelease `
                --dependency-verification strict --console plain
        } 'Android Release build failed'
    }
    finally { Pop-Location }
}
finally {
    foreach ($key in $savedEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($key, $savedEnvironment[$key], 'Process')
    }
}

Copy-Item -LiteralPath $RclonePath -Destination (Join-Path $toolsDir.FullName 'rclone.exe') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $publishDir.FullName 'LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/INSTALL_LOCAL.txt') -Destination (Join-Path $publishDir.FullName 'README.txt') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/INSTALL_LOCAL.txt') -Destination (Join-Path $outputDir.FullName 'README.txt') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'NOTICE.md') -Destination $noticesDir.FullName -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/LOCAL_DELIVERY.md') -Destination $noticesDir.FullName -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'windows/DEPENDENCIES.md') -Destination $noticesDir.FullName -Force
Copy-Item -Path (Join-Path $projectRoot 'licenses/*') -Destination $licenseDir.FullName -Force
Copy-Item -LiteralPath (Join-Path (Split-Path $DotnetPath -Parent) 'LICENSE.txt') -Destination (Join-Path $licenseDir.FullName 'dotnet-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path (Split-Path $DotnetPath -Parent) 'ThirdPartyNotices.txt') -Destination (Join-Path $licenseDir.FullName 'dotnet-ThirdPartyNotices.txt') -Force

$unsignedApk = Resolve-RequiredFile (Join-Path $projectRoot 'android/app/build/outputs/apk/release/app-release-unsigned.apk') 'unsigned Android Release APK'
$alignedApk = Join-Path $OutputRoot 'PhoneBridge-NG-0.1.0-aligned-unsigned.apk'
$signedApk = Join-Path $outputDir.FullName $apkFileName
Invoke-Checked { & $zipAlign -p -f 4 $unsignedApk $alignedApk } 'APK zip alignment failed'
$savedJavaHome = [Environment]::GetEnvironmentVariable('JAVA_HOME', 'Process')
[Environment]::SetEnvironmentVariable('JAVA_HOME', $JdkHome, 'Process')
try {
    Invoke-Checked {
        & $apkSigner sign --ks $AndroidKeystore --ks-key-alias $AndroidKeyAlias `
            @apkSignerPasswordArguments --v4-signing-enabled false `
            --out $signedApk $alignedApk
    } 'APK signing failed'
    $apkVerification = & $apkSigner verify --verbose --print-certs $signedApk
    if ($LASTEXITCODE -ne 0 -or -not ($apkVerification -match 'Verified using v2 scheme \(APK Signature Scheme v2\): true')) {
        throw 'The signed APK did not pass APK Signature Scheme v2 verification.'
    }
}
finally {
    [Environment]::SetEnvironmentVariable('JAVA_HOME', $savedJavaHome, 'Process')
}
$certificateMatch = [regex]::Match(($apkVerification -join "`n"), 'Signer #1 certificate SHA-256 digest: ([0-9a-f]+)')
if (-not $certificateMatch.Success) { throw 'The Android signer certificate digest could not be read.' }
$certificateSha256 = $certificateMatch.Groups[1].Value.ToUpperInvariant()
if ($certificateSha256 -ne $preflightCertificateSha256 -or
    ($AndroidSigningMode -eq 'ThirdPartyRelease' -and $certificateSha256 -ne $AndroidExpectedSignerSha256)) {
    throw 'The signed APK certificate does not match the preflight signing identity.'
}

$installerScript = Resolve-RequiredFile (Join-Path $projectRoot 'windows/installer/PhoneBridge-NG.iss') 'Inno Setup script'
Invoke-Checked {
    & $InnoCompiler "/DAppVersion=$productVersion" "/DPublishDir=$($publishDir.FullName)" `
        "/DWinFspMsi=$WinFspMsi" "/DOutputDir=$($outputDir.FullName)" $installerScript
} 'Windows installer compilation failed'

$installer = Resolve-RequiredFile (Join-Path $outputDir.FullName "PhoneBridge-NG-Setup-$productVersion.exe") 'Windows installer'
$instructions = Resolve-RequiredFile (Join-Path $outputDir.FullName 'README.txt') 'local installation instructions'
$installerSignature = Get-AuthenticodeSignature -FilePath $installer
$artifacts = @(
    [ordered]@{ file = (Split-Path $installer -Leaf); sha256 = (Get-FileHash $installer -Algorithm SHA256).Hash },
    [ordered]@{ file = (Split-Path $signedApk -Leaf); sha256 = (Get-FileHash $signedApk -Algorithm SHA256).Hash },
    [ordered]@{ file = (Split-Path $instructions -Leaf); sha256 = (Get-FileHash $instructions -Algorithm SHA256).Hash }
)
$manifest = [ordered]@{
    schema = 1
    product = 'PhoneBridge NG'
    version = $productVersion
    built_at_utc = [DateTimeOffset]::UtcNow.ToString('O')
    release_kind = $releaseKind
    instructions = $artifacts[2]
    windows = [ordered]@{
        minimum_os = 'Windows 11 21H2 (10.0.22000)'
        architecture = 'x64'
        installer = $artifacts[0]
        authenticode = $installerSignature.Status.ToString()
        dotnet_sdk = '10.0.401'
        dotnet_runtime = '10.0.12 self-contained'
        rclone = [ordered]@{ version = $rcloneVersion; sha256 = $rcloneSha256 }
        winfsp = [ordered]@{ version = $winFspVersion; sha256 = $winFspSha256; signer = $winFspSignature.SignerCertificate.Subject }
        inno_setup = $innoVersion
    }
    android = [ordered]@{
        minimum_sdk = 26
        target_sdk = 36
        apk = $artifacts[1]
        signing = $signingDescription
        signing_mode = $manifestSigningMode
        signer_certificate_sha256 = $certificateSha256
    }
}
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $outputDir.FullName 'delivery-manifest.json'), ($manifest | ConvertTo-Json -Depth 8) + "`n", $utf8)
$sumLines = $artifacts | ForEach-Object { "{0}  {1}" -f $_.sha256, $_.file }
[IO.File]::WriteAllText((Join-Path $outputDir.FullName 'SHA256SUMS.txt'), ($sumLines -join "`n") + "`n", $utf8)
Remove-Item -LiteralPath $alignedApk -Force

$manifest | ConvertTo-Json -Depth 8
