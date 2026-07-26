[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$Build
)

$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $workspace '.devtools\dotnet\dotnet.exe'

if (-not (Test-Path -LiteralPath $dotnet)) {
    throw "Portable .NET runtime not found: $dotnet"
}

if ($Build) {
    & (Join-Path $PSScriptRoot 'Build.ps1') -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$publishDirectory = Join-Path $PSScriptRoot "bin\$Configuration\net10.0-windows\win-x64\publish"
$publishedExe = Join-Path $publishDirectory 'Better HSR-Currency Wars V11.exe'
if (Test-Path -LiteralPath $publishedExe) {
    Start-Process -FilePath $publishedExe -WorkingDirectory $publishDirectory
    return
}

$outputDirectory = Join-Path $PSScriptRoot "bin\$Configuration\net10.0-windows\win-x64"
$dll = Join-Path $outputDirectory 'Better HSR-Currency Wars V11.dll'
if (-not (Test-Path -LiteralPath $dll)) {
    throw "Build output not found. Run Build.ps1 first: $dll"
}

$env:DOTNET_ROOT = Split-Path -Parent $dotnet
Start-Process -FilePath $dotnet -ArgumentList @("`"$dll`"") -WorkingDirectory $outputDirectory
