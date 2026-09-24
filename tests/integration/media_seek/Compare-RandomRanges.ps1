param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$BaselinePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$target = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
$baselineFile = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($BaselinePath)
if (-not [System.IO.File]::Exists($target) -or -not [System.IO.File]::Exists($baselineFile)) {
    throw 'Target or baseline does not exist.'
}

$baseline = Get-Content -LiteralPath $baselineFile -Raw | ConvertFrom-Json
if ($baseline.schema -ne 1 -or $baseline.fileLength -lt 1 -or
    $baseline.ranges.Count -lt 1 -or $baseline.ranges.Count -gt 16) {
    throw 'Invalid range baseline.'
}
if ((Get-Item -LiteralPath $target).Length -ne [long]$baseline.fileLength) {
    throw 'Target length does not match the baseline.'
}

$results = [System.Collections.Generic.List[object]]::new()
$totalBytesRead = 0L
$stream = [System.IO.FileStream]::new($target, [System.IO.FileMode]::Open,
    [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read, 1048576,
    [System.IO.FileOptions]::RandomAccess)
try {
    foreach ($range in $baseline.ranges) {
        $offset = [long]$range.offset
        $length = [int]$range.length
        if ($offset -lt 0 -or $length -lt 1 -or $length -gt 4194304 -or
            $offset + $length -gt $stream.Length -or
            $range.sha256 -notmatch '^[0-9a-f]{64}$') {
            throw 'Invalid range entry.'
        }
        $buffer = [byte[]]::new($length)
        $stream.Position = $offset
        $readTotal = 0
        while ($readTotal -lt $length) {
            $read = $stream.Read($buffer, $readTotal, $length - $readTotal)
            if ($read -eq 0) { throw "Short read at offset $offset." }
            $readTotal += $read
        }
        $actual = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($buffer))
        $match = $actual -ceq [string]$range.sha256
        $results.Add([ordered]@{
            offset = $offset
            length = $length
            expectedSha256 = [string]$range.sha256
            actualSha256 = $actual
            match = $match
        })
        $totalBytesRead += $length
        if (-not $match) { throw "Range hash mismatch at offset $offset." }
    }
}
finally { $stream.Dispose() }

[ordered]@{
    schema = 1
    path = $target
    fileLength = [long]$baseline.fileLength
    rangeCount = $results.Count
    totalBytesRead = $totalBytesRead
    allMatched = $true
    ranges = $results
} | ConvertTo-Json -Depth 5 -Compress
