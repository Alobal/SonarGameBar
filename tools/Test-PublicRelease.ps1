$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ignoredDirectoryNames = @(
    '.agents',
    '.codex',
    '.git',
    '.vs',
    'AppPackages',
    'BundleArtifacts',
    'TestResults',
    'artifacts',
    'bin',
    'obj'
)
$forbiddenExtensions = @(
    '.appx',
    '.appxbundle',
    '.appxsym',
    '.cer',
    '.key',
    '.log',
    '.msix',
    '.msixbundle',
    '.p12',
    '.pdb',
    '.pem',
    '.pfx',
    '.snk',
    '.suo',
    '.user'
)
$binaryExtensions = @(
    '.dll',
    '.exe',
    '.png',
    '.pri',
    '.winmd',
    '.xbf'
)

function Test-IsIgnoredDirectory {
    param([string]$FullName)

    $relative = $FullName.Substring($root.Length).TrimStart('\')
    $segments = $relative -split '[\\/]'
    return @($segments | Where-Object { $ignoredDirectoryNames -contains $_ }).Count -gt 0
}

$allFiles = Get-ChildItem $root -Recurse -Force -File -ErrorAction SilentlyContinue
$publishableFiles = @($allFiles | Where-Object { -not (Test-IsIgnoredDirectory $_.FullName) })
$findings = [System.Collections.Generic.List[object]]::new()

foreach ($file in $publishableFiles) {
    if ($forbiddenExtensions -contains $file.Extension.ToLowerInvariant()) {
        $findings.Add([pscustomobject]@{
            File = $file.FullName.Substring($root.Length).TrimStart('\')
            Kind = 'Forbidden file type'
            Detail = $file.Extension
        })
    }
}

$dynamicPrivatePatterns = @()
if ($env:USERNAME) {
    $dynamicPrivatePatterns += [regex]::Escape($env:USERNAME)
}
if ($env:COMPUTERNAME) {
    $dynamicPrivatePatterns += [regex]::Escape($env:COMPUTERNAME)
}
if ($env:USERPROFILE) {
    $dynamicPrivatePatterns += [regex]::Escape($env:USERPROFILE)
}

$secretPatterns = @(
    '-----BEGIN [A-Z ]*PRIVATE KEY-----',
    '(?i)authorization\s*:\s*bearer\s+[A-Za-z0-9._-]{12,}',
    '(?i)gh[pousr]_[A-Za-z0-9_]{20,}',
    '(?i)github_pat_[A-Za-z0-9_]{20,}',
    '(?i)sk-[A-Za-z0-9_-]{20,}',
    '(?i)AIza[A-Za-z0-9_-]{20,}',
    '(?i)[A-Z]:[\\/]Users[\\/][^\\/\s]+'
) + $dynamicPrivatePatterns

foreach ($file in $publishableFiles) {
    if ($binaryExtensions -contains $file.Extension.ToLowerInvariant() -or $file.Length -gt 5MB) {
        continue
    }

    $lineNumber = 0
    foreach ($line in Get-Content $file.FullName -ErrorAction SilentlyContinue) {
        $lineNumber++
        foreach ($pattern in $secretPatterns) {
            if ($line -match $pattern) {
                $findings.Add([pscustomobject]@{
                    File = $file.FullName.Substring($root.Length).TrimStart('\')
                    Kind = 'Sensitive text'
                    Detail = "line $lineNumber"
                })
                break
            }
        }
    }
}

$riskLocations = @(
    '.agents',
    '.codex',
    '.vs',
    'artifacts',
    'SonarGameBar.Widget\AppPackages'
)

Write-Host 'Local privacy-risk locations present:'
foreach ($relativePath in $riskLocations) {
    $path = Join-Path $root $relativePath
    if (Test-Path $path) {
        Write-Host "  present, ignored: $relativePath"
    }
}

if ($findings.Count -gt 0) {
    Write-Host ''
    Write-Host 'Public-release audit failed:'
    $findings | Sort-Object File, Kind, Detail | Format-Table -AutoSize
    exit 1
}

Write-Host ''
Write-Host "Public-release audit passed for $($publishableFiles.Count) publishable files."
