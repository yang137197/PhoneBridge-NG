param(
    [string]$PrimaryKeystore = (Join-Path $env:LOCALAPPDATA 'PhoneBridge-NG\Signing\phonebridge-release.p12'),
    [Parameter(Mandatory)]
    [string]$BackupDirectory,
    [string]$StorePasswordEnvironment = 'PBNG_ANDROID_STORE_PASSWORD',
    [string]$KeyPasswordEnvironment = 'PBNG_ANDROID_KEY_PASSWORD',
    [string]$JdkHome
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$keyAlias = 'phonebridge-release'
$identityFileName = 'android-release-identity.json'
$projectRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
if (-not $JdkHome) { $JdkHome = Join-Path $projectRoot '.audit/tools/jdk/jbrsdk_jcef-21.0.10-windows-x64-b1163.110' }

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
    $value = [Environment]::GetEnvironmentVariable($Name, 'Process')
    if ([string]::IsNullOrEmpty($value)) { throw "$Label is not set in the current process environment." }
    if ($value.Length -lt 16 -or $value -match '[\x00-\x1F\x7F]') {
        throw "$Label must contain at least 16 non-control characters."
    }
    return $value
}

function Assert-PlainDirectory([string]$Path, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { throw "$Label does not exist: $Path" }
    $directory = Get-Item -LiteralPath $Path -Force
    if ($directory.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw "$Label cannot be a reparse point." }
    return $directory.FullName
}

function Get-StorageDescriptor([string]$Path, [string]$Label) {
    $root = [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($Path))
    if ([string]::IsNullOrWhiteSpace($root)) { throw "$Label does not have a storage root." }
    if ($root.StartsWith('\\', [StringComparison]::Ordinal)) {
        return [ordered]@{ kind = 'network'; disk_number = $null }
    }
    if ($root -notmatch '^[A-Za-z]:\\$') { throw "$Label uses an unsupported storage root." }
    $drive = $root.Substring(0, 2)
    $logicalDisk = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='$drive'" -ErrorAction Stop
    if ($null -eq $logicalDisk) { throw "$Label storage volume was not found." }
    switch ([int]$logicalDisk.DriveType) {
        2 { return [ordered]@{ kind = 'removable'; disk_number = $null } }
        4 { return [ordered]@{ kind = 'network'; disk_number = $null } }
        3 {
            $partition = @(Get-Partition -DriveLetter $drive.Substring(0, 1) -ErrorAction Stop)
            if ($partition.Count -ne 1) { throw "$Label could not be mapped to one physical disk." }
            return [ordered]@{ kind = 'fixed'; disk_number = [int]$partition[0].DiskNumber }
        }
        default { throw "$Label must use a fixed, removable, or network storage volume." }
    }
}

function Set-PrivateAcl([string]$Path, [bool]$Directory) {
    $currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $security = Get-Acl -LiteralPath $Path
    if ($Directory) {
        $accessSddl = "D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;$currentSid)"
    }
    else {
        $accessSddl = "D:P(A;;FA;;;SY)(A;;FA;;;$currentSid)"
    }
    $security.SetSecurityDescriptorSddlForm($accessSddl, [Security.AccessControl.AccessControlSections]::Access)
    try {
        Set-Acl -LiteralPath $Path -AclObject $security
    }
    catch {
        throw "Could not restrict the signing path ACL: $Path. $($_.Exception.Message)"
    }
}

function Read-Certificate([string]$KeyTool, [string]$Keystore, [string]$PasswordEnvironment) {
    $pem = & $KeyTool -exportcert -rfc -keystore $Keystore -alias $keyAlias '-storepass:env' $PasswordEnvironment 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $pem) { throw 'The signing certificate could not be read from the generated keystore.' }
    return [Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPem(($pem -join "`n"))
}

if (-not [IO.Path]::IsPathFullyQualified($PrimaryKeystore)) { throw 'PrimaryKeystore must be an absolute path.' }
if (-not [IO.Path]::IsPathFullyQualified($BackupDirectory)) { throw 'BackupDirectory must be an absolute path.' }
$PrimaryKeystore = [IO.Path]::GetFullPath($PrimaryKeystore)
$BackupDirectory = [IO.Path]::GetFullPath($BackupDirectory)
if ([IO.Path]::GetExtension($PrimaryKeystore) -ne '.p12') { throw 'PrimaryKeystore must use the .p12 extension.' }
if ((Test-PathWithin $PrimaryKeystore $projectRoot) -or (Test-PathWithin $BackupDirectory $projectRoot)) {
    throw 'Signing identity files must stay outside the project directory.'
}

$primaryDirectory = Split-Path -Parent $PrimaryKeystore
if ((Test-PathWithin $BackupDirectory $primaryDirectory) -or (Test-PathWithin $primaryDirectory $BackupDirectory)) {
    throw 'Primary and backup signing directories must be independent paths.'
}
$primaryStorage = Get-StorageDescriptor $primaryDirectory 'Primary signing directory'
$backupStorage = Get-StorageDescriptor $BackupDirectory 'Backup signing directory'
if ($primaryStorage.kind -ne 'fixed') {
    throw 'The primary signing directory must use a local fixed disk.'
}
if ($backupStorage.kind -eq 'fixed' -and $backupStorage.disk_number -eq $primaryStorage.disk_number) {
    throw 'The backup signing directory must be on a different physical disk; another partition or drive letter on the same disk is not independent.'
}
$backupKeystore = Join-Path $BackupDirectory (Split-Path -Leaf $PrimaryKeystore)
$primaryIdentity = Join-Path $primaryDirectory $identityFileName
$backupIdentity = Join-Path $BackupDirectory $identityFileName
foreach ($target in @($PrimaryKeystore, $backupKeystore, $primaryIdentity, $backupIdentity)) {
    if (Test-Path -LiteralPath $target) { throw "Refusing to overwrite an existing signing identity file: $target" }
}

$storePassword = Assert-EnvironmentName $StorePasswordEnvironment 'Store password environment variable'
$keyPassword = Assert-EnvironmentName $KeyPasswordEnvironment 'Key password environment variable'
$storePasswordBytes = [Text.Encoding]::UTF8.GetBytes($storePassword)
$keyPasswordBytes = [Text.Encoding]::UTF8.GetBytes($keyPassword)
$passwordsMatch = $storePasswordBytes.Length -eq $keyPasswordBytes.Length -and
    [Security.Cryptography.CryptographicOperations]::FixedTimeEquals($storePasswordBytes, $keyPasswordBytes)
[Array]::Clear($storePasswordBytes, 0, $storePasswordBytes.Length)
[Array]::Clear($keyPasswordBytes, 0, $keyPasswordBytes.Length)
$storePasswordBytes = $null
$keyPasswordBytes = $null
if (-not $passwordsMatch) {
    throw 'PKCS12 store and key passwords must be identical for this signing identity.'
}
$storePassword = $null
$keyPassword = $null

$JdkHome = (Resolve-Path -LiteralPath $JdkHome).Path
$keyTool = Join-Path $JdkHome 'bin/keytool.exe'
if (-not (Test-Path -LiteralPath $keyTool -PathType Leaf)) { throw 'The pinned keytool executable was not found.' }

if (-not (Test-Path -LiteralPath $primaryDirectory)) { New-Item -ItemType Directory -Path $primaryDirectory | Out-Null }
if (-not (Test-Path -LiteralPath $BackupDirectory)) { New-Item -ItemType Directory -Path $BackupDirectory | Out-Null }
$primaryDirectory = Assert-PlainDirectory $primaryDirectory 'Primary signing directory'
$BackupDirectory = Assert-PlainDirectory $BackupDirectory 'Backup signing directory'
Set-PrivateAcl $primaryDirectory $true

$nonce = [guid]::NewGuid().ToString('N')
$primaryTemp = Join-Path $primaryDirectory (".$nonce.tmp.p12")
$backupTemp = Join-Path $BackupDirectory (".$nonce.tmp.p12")
$primaryIdentityTemp = Join-Path $primaryDirectory (".$nonce.tmp.json")
$backupIdentityTemp = Join-Path $BackupDirectory (".$nonce.tmp.json")
try {
    & $keyTool -genkeypair -alias $keyAlias -keyalg RSA -keysize 4096 -sigalg SHA256withRSA -validity 10000 `
        -dname 'CN=PhoneBridge NG Android Release,O=PhoneBridge NG' -storetype PKCS12 -keystore $primaryTemp `
        '-storepass:env' $StorePasswordEnvironment '-keypass:env' $KeyPasswordEnvironment -noprompt 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $primaryTemp -PathType Leaf)) {
        throw 'Android release keystore generation failed.'
    }
    Set-PrivateAcl $primaryTemp $false
    Copy-Item -LiteralPath $primaryTemp -Destination $backupTemp

    $primaryHash = (Get-FileHash -LiteralPath $primaryTemp -Algorithm SHA256).Hash
    $backupHash = (Get-FileHash -LiteralPath $backupTemp -Algorithm SHA256).Hash
    if ($primaryHash -ne $backupHash) { throw 'The backup keystore does not match the generated primary keystore.' }

    $certificate = Read-Certificate $keyTool $primaryTemp $StorePasswordEnvironment
    try {
        $certificateSha256 = $certificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256)
        $certificateSubject = $certificate.Subject
        $notBefore = $certificate.NotBefore.ToUniversalTime().ToString('O')
        $notAfter = $certificate.NotAfter.ToUniversalTime().ToString('O')
    }
    finally { $certificate.Dispose() }
    if ($certificateSubject -match '(^|,)\s*CN=Android Debug(,|$)') { throw 'Generated certificate unexpectedly uses an Android Debug identity.' }

    $identity = [ordered]@{
        schema = 1
        product = 'PhoneBridge NG'
        purpose = 'Android third-party release signing'
        key_alias = $keyAlias
        keystore_type = 'PKCS12'
        keystore_file = (Split-Path -Leaf $PrimaryKeystore)
        keystore_sha256 = $primaryHash
        certificate_sha256 = $certificateSha256
        certificate_subject = $certificateSubject
        certificate_not_before_utc = $notBefore
        certificate_not_after_utc = $notAfter
        created_at_utc = [DateTimeOffset]::UtcNow.ToString('O')
        password_storage = 'operator-managed; not stored by PhoneBridge NG'
    }
    $json = ($identity | ConvertTo-Json -Depth 5) + "`n"
    [IO.File]::WriteAllText($primaryIdentityTemp, $json, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($backupIdentityTemp, $json, [Text.UTF8Encoding]::new($false))
    Set-PrivateAcl $primaryIdentityTemp $false

    Move-Item -LiteralPath $backupTemp -Destination $backupKeystore
    Move-Item -LiteralPath $backupIdentityTemp -Destination $backupIdentity
    Move-Item -LiteralPath $primaryTemp -Destination $PrimaryKeystore
    Move-Item -LiteralPath $primaryIdentityTemp -Destination $primaryIdentity

    if ((Get-FileHash -LiteralPath $PrimaryKeystore -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $backupKeystore -Algorithm SHA256).Hash) {
        throw 'Committed primary and backup keystores do not match.'
    }
    $backupCertificate = Read-Certificate $keyTool $backupKeystore $StorePasswordEnvironment
    try { $backupCertificateSha256 = $backupCertificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256) }
    finally { $backupCertificate.Dispose() }
    if ($backupCertificateSha256 -ne $certificateSha256) { throw 'Backup certificate identity does not match the primary identity.' }

    [ordered]@{
        result = 'created'
        primary_keystore = $PrimaryKeystore
        backup_keystore = $backupKeystore
        primary_identity = $primaryIdentity
        backup_identity = $backupIdentity
        primary_storage = 'fixed'
        backup_storage = $backupStorage.kind
        key_alias = $keyAlias
        keystore_sha256 = $primaryHash
        certificate_sha256 = $certificateSha256
        certificate_not_after_utc = $notAfter
    } | ConvertTo-Json -Depth 5
}
finally {
    foreach ($temporary in @($primaryTemp, $backupTemp, $primaryIdentityTemp, $backupIdentityTemp)) {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}
