param(
    [string]$OutputDirectory
)

& (Join-Path $PSScriptRoot 'Build-Package.ps1') `
    -Configuration Release `
    -OutputDirectory $OutputDirectory

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
