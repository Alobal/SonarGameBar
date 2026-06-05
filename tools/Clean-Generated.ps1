$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path.TrimEnd('\')
$targets = @(
    '.vs',
    'artifacts',
    'SonarGameBar.Bridge\bin',
    'SonarGameBar.Bridge\obj',
    'SonarGameBar.Core\bin',
    'SonarGameBar.Core\obj',
    'SonarGameBar.Widget\AppPackages',
    'SonarGameBar.Widget\bin',
    'SonarGameBar.Widget\obj'
)

foreach ($relativePath in $targets) {
    $candidate = Join-Path $root $relativePath
    $fullPath = [System.IO.Path]::GetFullPath($candidate)

    if (-not $fullPath.StartsWith($root + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove path outside the workspace: $fullPath"
    }

    if (Test-Path $fullPath) {
        Remove-Item $fullPath -Recurse -Force
        Write-Host "Removed $relativePath"
    }
}

Write-Host 'Generated workspace output removed.'
