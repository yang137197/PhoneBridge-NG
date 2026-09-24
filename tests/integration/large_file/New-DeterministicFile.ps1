param(
    [Parameter(Mandatory = $true)][string]$Path,
    [long]$Length = 1000000000,
    [int]$Seed = 5246998,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Length -lt 1 -or $Length -gt 20000000000) { throw 'Length must be between 1 and 20,000,000,000 bytes.' }
$target = [System.IO.Path]::GetFullPath($Path)
$directory = [System.IO.Path]::GetDirectoryName($target)
if (-not $directory -or -not [System.IO.Directory]::Exists($directory)) { throw 'Destination directory does not exist.' }
if ([System.IO.File]::Exists($target) -and -not $Force) { throw 'Destination already exists.' }

$temporary = [System.IO.Path]::Combine($directory, '.pbng-generate-' + [guid]::NewGuid().ToString('N') + '.tmp')
$buffer = [byte[]]::new(1000000)
$hash = [System.Security.Cryptography.IncrementalHash]::CreateHash([System.Security.Cryptography.HashAlgorithmName]::SHA256)
$written = 0L
try {
    $stream = [System.IO.FileStream]::new($temporary, [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write, [System.IO.FileShare]::None, $buffer.Length,
        [System.IO.FileOptions]::SequentialScan)
    try {
        $block = 0L
        while ($written -lt $Length) {
            $blockSeed = [int](($Seed + ($block * 104729L)) -band 0x7fffffffL)
            [System.Random]::new($blockSeed).NextBytes($buffer)
            $count = [int][Math]::Min([long]$buffer.Length, $Length - $written)
            $stream.Write($buffer, 0, $count)
            $hash.AppendData($buffer, 0, $count)
            $written += $count
            $block++
        }
        $stream.Flush($true)
    }
    finally { $stream.Dispose() }
    if ([System.IO.File]::Exists($target)) { [System.IO.File]::Delete($target) }
    [System.IO.File]::Move($temporary, $target)
    $temporary = $null
    [pscustomobject]@{
        schema = 1
        path = $target
        length = $written
        seed = $Seed
        blockBytes = $buffer.Length
        sha256 = [Convert]::ToHexStringLower($hash.GetHashAndReset())
    } | ConvertTo-Json -Compress
}
finally {
    $hash.Dispose()
    if ($temporary -and [System.IO.File]::Exists($temporary)) { [System.IO.File]::Delete($temporary) }
}
