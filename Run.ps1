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
    $project = Join-Path $PSScriptRoot 'Better HSR-Currency Wars V11.csproj'
    $env:DOTNET_ROOT = Split-Path -Parent $dotnet
    $env:DOTNET_CLI_HOME = Join-Path $workspace '.dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $workspace '.nuget\packages'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    & $dotnet build $project -c $Configuration -p:SelfContained=false -p:RuntimeIdentifier= -v minimal
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$previewOutputDirectory = Join-Path $PSScriptRoot "bin\$Configuration\net10.0-windows"
$previewDll = Join-Path $previewOutputDirectory 'Better HSR-Currency Wars V11.dll'
if (Test-Path -LiteralPath $previewDll) {
    $env:DOTNET_ROOT = Split-Path -Parent $dotnet
    Start-Process -FilePath $dotnet -ArgumentList @("`"$previewDll`"") -WorkingDirectory $previewOutputDirectory
    return
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
