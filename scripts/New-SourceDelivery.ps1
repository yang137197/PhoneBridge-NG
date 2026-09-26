param(
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$productVersion = '0.2.0'
$projectRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$deliveryRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '.audit\delivery'))
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $deliveryRoot 'output' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$runDirectory = Join-Path $projectRoot '.audit\runs\P2-007'
$archiveName = "PhoneBridge-NG-$productVersion-source.zip"
$archiveRoot = "PhoneBridge-NG-$productVersion-source"
$expectedCertificate = '66FF69D71637D215C2F104DB95B93CC1F991FA1F23580F95AF3316B0763B14D4'
$expectedApkHash = '5F31E9B0E3EC20C25715014053802C97304443D8A4279BF27F9E82B198F76CF8'
$expectedInstallerHash = 'F3856096DB4882071C4B61B9522ADDFE1CD30F3E33C654A1C2B935BCEBB5EFC7'
$fixedTimestamp = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
$utf8 = [Text.UTF8Encoding]::new($false)

function Assert-Within([string]$Path, [string]$Parent, [string]$Label) {
    $full = [IO.Path]::GetFullPath($Path)
    $fullParent = [IO.Path]::GetFullPath($Parent).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if (-not $full.StartsWith($fullParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label is outside the allowed directory: $full"
    }
}

function Get-RelativePath([string]$Path) {
    return [IO.Path]::GetRelativePath($projectRoot, $Path).Replace('\', '/')
}

function Test-ExcludedSourcePath([string]$Path) {
    $relative = Get-RelativePath $Path
    $segments = $relative.Split('/')
    $excludedSegments = @('.git', '.audit', '.vs', '.gradle', '.kotlin', 'bin', 'obj', 'build', 'TestResults', '__pycache__')
    foreach ($segment in $segments) {
        if ($excludedSegments -contains $segment) { return $true }
    }
    $extension = [IO.Path]::GetExtension($relative).ToLowerInvariant()
    if ($extension -in @('.pyc', '.pyo', '.p12', '.pfx', '.jks', '.keystore', '.key')) { return $true }
    return $false
}

function Get-SourceFiles {
    $topLevelNames = @(
        '.gitignore', 'AGENTS.md', 'ARCHITECTURE.md', 'DECISIONS.md', 'DEVELOPMENT_RULES.md',
        'LICENSE', 'NOTICE.md', 'README.md'
    )
    $items = [Collections.Generic.List[IO.FileInfo]]::new()
    foreach ($name in $topLevelNames) {
        $path = Join-Path $projectRoot $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required source file is missing: $name" }
        $items.Add((Get-Item -LiteralPath $path -Force))
    }
    foreach ($directory in @('android', 'licenses', 'scripts', 'tests', 'windows', 'docs\design\v0.2\icons')) {
        $path = Join-Path $projectRoot $directory
        foreach ($file in Get-ChildItem -LiteralPath $path -Recurse -File -Force) {
            if (-not (Test-ExcludedSourcePath $file.FullName)) { $items.Add($file) }
        }
    }
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $projectRoot 'docs') -File -Force) {
        if (-not (Test-ExcludedSourcePath $file.FullName)) { $items.Add($file) }
    }
    return @($items | Sort-Object FullName -Unique)
}

function Assert-SafeSourceFiles([IO.FileInfo[]]$Files) {
    $privateKeyPattern = [regex]'-----BEGIN (?:RSA |EC |DSA |OPENSSH |ENCRYPTED )?PRIVATE KEY-----'
    foreach ($file in $Files) {
        if ($file.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
            throw "Source file cannot be a reparse point: $(Get-RelativePath $file.FullName)"
        }
        if ($file.Length -gt 10MB) { throw "Unexpected large source file: $(Get-RelativePath $file.FullName)" }
        $text = [IO.File]::ReadAllText($file.FullName)
        if ($privateKeyPattern.IsMatch($text)) {
            throw "Private key material detected in source file: $(Get-RelativePath $file.FullName)"
        }
    }
}

function New-SourceArchive([IO.FileInfo[]]$Files, [string]$ArchivePath) {
    if (Test-Path -LiteralPath $ArchivePath) { Remove-Item -LiteralPath $ArchivePath -Force }
    $fileRecords = @($Files | ForEach-Object {
        [ordered]@{
            path = Get-RelativePath $_.FullName
            bytes = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    })
    $sourceManifest = [ordered]@{
        schema = 1
        product = 'PhoneBridge NG'
        version = $productVersion
        archive_root = $archiveRoot
        manifest_self_excluded_from_files = $true
        included_file_count = $fileRecords.Count
        excluded = @('.git', '.audit', 'build outputs', 'tool caches', 'test results', 'private key file types')
        files = $fileRecords
    }
    $manifestBytes = $utf8.GetBytes(($sourceManifest | ConvertTo-Json -Depth 8) + "`n")

    $fileStream = [IO.File]::Open($ArchivePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $archive = [IO.Compression.ZipArchive]::new($fileStream, [IO.Compression.ZipArchiveMode]::Create, $true, $utf8)
        try {
            foreach ($record in $fileRecords) {
                $entry = $archive.CreateEntry("$archiveRoot/$($record.path)", [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $fixedTimestamp
                $entryStream = $entry.Open()
                try {
                    $sourceStream = [IO.File]::OpenRead((Join-Path $projectRoot $record.path.Replace('/', [IO.Path]::DirectorySeparatorChar)))
                    try { $sourceStream.CopyTo($entryStream) }
                    finally { $sourceStream.Dispose() }
                }
                finally { $entryStream.Dispose() }
            }
            $manifestEntry = $archive.CreateEntry("$archiveRoot/SOURCE-MANIFEST.json", [IO.Compression.CompressionLevel]::Optimal)
            $manifestEntry.LastWriteTime = $fixedTimestamp
            $manifestStream = $manifestEntry.Open()
            try { $manifestStream.Write($manifestBytes, 0, $manifestBytes.Length) }
            finally { $manifestStream.Dispose() }
        }
        finally { $archive.Dispose() }
    }
    finally { $fileStream.Dispose() }
    return $sourceManifest
}

function Test-SourceArchive([string]$ArchivePath) {
    $stream = [IO.File]::OpenRead($ArchivePath)
    try {
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read, $false, $utf8)
        try {
            $entries = @($archive.Entries)
            foreach ($entry in $entries) {
                if ($entry.FullName.Contains('\') -or $entry.FullName.StartsWith('/') -or $entry.FullName.Split('/') -contains '..') {
                    throw "Unsafe ZIP entry: $($entry.FullName)"
                }
            }
            $manifestEntry = $entries | Where-Object { $_.FullName -ceq "$archiveRoot/SOURCE-MANIFEST.json" } | Select-Object -First 1
            if (-not $manifestEntry) { throw 'SOURCE-MANIFEST.json is missing from the source archive.' }
            $reader = [IO.StreamReader]::new($manifestEntry.Open(), $utf8, $true)
            try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json -DateKind String }
            finally { $reader.Dispose() }
            if ($manifest.schema -ne 1 -or $manifest.product -cne 'PhoneBridge NG' -or $manifest.version -cne $productVersion) {
                throw 'Source manifest identity mismatch.'
            }
            if ($entries.Count -ne ([int]$manifest.included_file_count + 1)) { throw 'Source archive entry count mismatch.' }
            foreach ($record in $manifest.files) {
                $expectedName = "$archiveRoot/$($record.path)"
                $entry = $entries | Where-Object { $_.FullName -ceq $expectedName } | Select-Object -First 1
                if (-not $entry) { throw "Source archive entry is missing: $expectedName" }
                $entryStream = $entry.Open()
                try {
                    $sha = [Security.Cryptography.SHA256]::Create()
                    try { $hash = [Convert]::ToHexString($sha.ComputeHash($entryStream)) }
                    finally { $sha.Dispose() }
                }
                finally { $entryStream.Dispose() }
                if ($hash -ne ([string]$record.sha256).ToUpperInvariant() -or $entry.Length -ne [long]$record.bytes) {
                    throw "Source archive content mismatch: $expectedName"
                }
            }
            foreach ($required in @('LICENSE', 'NOTICE.md', 'scripts/Build-LocalDelivery.ps1', 'android/app/build.gradle.kts', 'windows/PhoneBridge.Windows.slnx', 'docs/design/v0.2/icons/tray/offline.svg')) {
                if (-not ($entries.FullName -ccontains "$archiveRoot/$required")) { throw "Required source entry is missing: $required" }
            }
            return [ordered]@{ entries = $entries.Count; source_files = [int]$manifest.included_file_count }
        }
        finally { $archive.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Get-OutputEvidence([string]$Path) {
    $manifest = Get-Content -LiteralPath (Join-Path $Path 'delivery-manifest.json') -Raw | ConvertFrom-Json -DateKind String
    if ($manifest.release_kind -cne 'third-party-sideload' -or $manifest.android.signing_mode -cne 'third-party-release' -or
        ([string]$manifest.android.signer_certificate_sha256).ToUpperInvariant() -ne $expectedCertificate) {
        throw 'Default delivery is not the expected formal release.'
    }
    $apkHash = (Get-FileHash -LiteralPath (Join-Path $Path $manifest.android.apk.file) -Algorithm SHA256).Hash
    $installerHash = (Get-FileHash -LiteralPath (Join-Path $Path $manifest.windows.installer.file) -Algorithm SHA256).Hash
    $readmeHash = (Get-FileHash -LiteralPath (Join-Path $Path $manifest.instructions.file) -Algorithm SHA256).Hash
    $sourceHash = (Get-FileHash -LiteralPath (Join-Path $Path $manifest.source.file) -Algorithm SHA256).Hash
    if ($apkHash -ne $expectedApkHash -or $installerHash -ne $expectedInstallerHash -or
        $apkHash -ne ([string]$manifest.android.apk.sha256).ToUpperInvariant() -or
        $installerHash -ne ([string]$manifest.windows.installer.sha256).ToUpperInvariant() -or
        $readmeHash -ne ([string]$manifest.instructions.sha256).ToUpperInvariant() -or
        $sourceHash -ne ([string]$manifest.source.sha256).ToUpperInvariant()) {
        throw 'Default delivery hash mismatch.'
    }
    $expectedSums = @(
        "$installerHash  $($manifest.windows.installer.file)",
        "$apkHash  $($manifest.android.apk.file)",
        "$readmeHash  $($manifest.instructions.file)",
        "$sourceHash  $($manifest.source.file)"
    )
    $actualSums = @(Get-Content -LiteralPath (Join-Path $Path 'SHA256SUMS.txt') | Where-Object { $_ })
    if (@(Compare-Object $expectedSums $actualSums -SyncWindow 0).Count -ne 0) { throw 'Default SHA256SUMS mismatch.' }
    return [ordered]@{
        files = @(Get-ChildItem -LiteralPath $Path -File | Sort-Object Name | ForEach-Object {
            [ordered]@{ name = $_.Name; bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
        })
        apk_sha256 = $apkHash
        installer_sha256 = $installerHash
        readme_sha256 = $readmeHash
        source_sha256 = $sourceHash
    }
}

Assert-Within $OutputDirectory $deliveryRoot 'Output directory'
if (-not (Test-Path -LiteralPath $OutputDirectory -PathType Container)) { throw 'Default delivery directory is missing.' }
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null

$sourceFiles = Get-SourceFiles
Assert-SafeSourceFiles $sourceFiles
$temporaryArchive = Join-Path $runDirectory $archiveName
$sourceManifest = New-SourceArchive $sourceFiles $temporaryArchive
$archiveVerification = Test-SourceArchive $temporaryArchive
$archiveHash = (Get-FileHash -LiteralPath $temporaryArchive -Algorithm SHA256).Hash

$operationId = [guid]::NewGuid().ToString('N')
$staging = [IO.Path]::GetFullPath((Join-Path $deliveryRoot ".p1-044-staging-$operationId"))
$backup = [IO.Path]::GetFullPath((Join-Path $deliveryRoot ".p1-044-backup-$operationId"))
Assert-Within $staging $deliveryRoot 'Staging directory'
Assert-Within $backup $deliveryRoot 'Backup directory'
$destinationMoved = $false
try {
    New-Item -ItemType Directory -Path $staging | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $OutputDirectory -File) {
        if ($file.Name -cne $archiveName) { Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $staging $file.Name) }
    }
    Copy-Item -LiteralPath $temporaryArchive -Destination (Join-Path $staging $archiveName)
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\INSTALL_LOCAL.txt') -Destination (Join-Path $staging 'README.txt') -Force

    $manifestPath = Join-Path $staging 'delivery-manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -DateKind String
    $manifest.built_at_utc = ([DateTimeOffset]::Parse([string]$manifest.built_at_utc)).ToUniversalTime().ToString('O')
    $readmeHash = (Get-FileHash -LiteralPath (Join-Path $staging 'README.txt') -Algorithm SHA256).Hash
    $manifest.instructions.sha256 = $readmeHash
    $sourceProperty = [ordered]@{
        file = $archiveName
        sha256 = $archiveHash
        format = 'zip'
        license = 'GPL-3.0-or-later; see LICENSE and NOTICE.md in archive'
        included_file_count = $sourceManifest.included_file_count
    }
    $manifest | Add-Member -NotePropertyName source -NotePropertyValue $sourceProperty -Force
    $manifest | Add-Member -NotePropertyName packaged_at_utc -NotePropertyValue ([DateTimeOffset]::UtcNow.ToString('O')) -Force
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 8) + "`n", $utf8)
    $installerHash = (Get-FileHash -LiteralPath (Join-Path $staging $manifest.windows.installer.file) -Algorithm SHA256).Hash
    $apkHash = (Get-FileHash -LiteralPath (Join-Path $staging $manifest.android.apk.file) -Algorithm SHA256).Hash
    $sumLines = @(
        "$installerHash  $($manifest.windows.installer.file)",
        "$apkHash  $($manifest.android.apk.file)",
        "$readmeHash  $($manifest.instructions.file)",
        "$archiveHash  $archiveName"
    )
    [IO.File]::WriteAllText((Join-Path $staging 'SHA256SUMS.txt'), ($sumLines -join "`n") + "`n", $utf8)
    $null = Get-OutputEvidence $staging
    $null = Test-SourceArchive (Join-Path $staging $archiveName)

    Move-Item -LiteralPath $OutputDirectory -Destination $backup
    $destinationMoved = $true
    Move-Item -LiteralPath $staging -Destination $OutputDirectory
    $outputEvidence = Get-OutputEvidence $OutputDirectory
    $finalArchiveVerification = Test-SourceArchive (Join-Path $OutputDirectory $archiveName)

    Assert-Within $backup $deliveryRoot 'Backup directory'
    Remove-Item -LiteralPath $backup -Recurse -Force
}
catch {
    if (-not (Test-Path -LiteralPath $OutputDirectory) -and $destinationMoved -and (Test-Path -LiteralPath $backup)) {
        Move-Item -LiteralPath $backup -Destination $OutputDirectory
    }
    if (Test-Path -LiteralPath $staging) {
        Assert-Within $staging $deliveryRoot 'Staging directory'
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
    throw
}

$evidence = [ordered]@{
    schema = 1
    task = 'P2-007'
    captured_at_utc = [DateTimeOffset]::UtcNow.ToString('O')
    source_archive = [ordered]@{
        file = $archiveName
        sha256 = $archiveHash
        bytes = (Get-Item -LiteralPath (Join-Path $OutputDirectory $archiveName)).Length
        source_files = $archiveVerification.source_files
        entries = $archiveVerification.entries
        final_entries = $finalArchiveVerification.entries
    }
    output = $outputEvidence
    private_key_marker_scan = 'pass'
    excluded_local_audit_and_build_outputs = $true
    transient_paths_absent = (-not (Test-Path -LiteralPath $staging)) -and (-not (Test-Path -LiteralPath $backup))
    result = 'pass'
}
[IO.File]::WriteAllText((Join-Path $runDirectory 'source-delivery.json'), ($evidence | ConvertTo-Json -Depth 8) + "`n", $utf8)
$evidence | ConvertTo-Json -Depth 8
