[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $workspace '.devtools\dotnet\dotnet.exe'
$project = Join-Path $PSScriptRoot 'Better HSR-Currency Wars V11.csproj'

if (-not (Test-Path -LiteralPath $dotnet)) {
    throw "Portable .NET SDK not found: $dotnet"
}

$env:DOTNET_ROOT = Split-Path -Parent $dotnet
$env:DOTNET_CLI_HOME = Join-Path $workspace '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $workspace '.nuget\packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$assets = Join-Path $PSScriptRoot 'obj\project.assets.json'
if (-not (Test-Path -LiteralPath $assets)) {
    & $dotnet restore $project --configfile (Join-Path $PSScriptRoot 'NuGet.Config') -v minimal
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

& $dotnet publish $project -c $Configuration --no-restore -v minimal
exit $LASTEXITCODE
