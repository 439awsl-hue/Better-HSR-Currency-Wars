param(
    [string]$Python = "python"
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$toolsRoot = Join-Path $projectRoot "Tools"
$bridgeScript = Join-Path $toolsRoot "rapidocr_bridge.py"
$requirements = Join-Path $PSScriptRoot "requirements.txt"
$buildRoot = Join-Path $projectRoot ".ocr-build"
$venvRoot = Join-Path $buildRoot "venv"
$workRoot = Join-Path $buildRoot "pyinstaller-work"
$specRoot = Join-Path $buildRoot "pyinstaller-spec"
$distRoot = Join-Path $buildRoot "dist"
$runtimeRoot = Join-Path $projectRoot "OCRRuntime"
$targetRoot = Join-Path $runtimeRoot "rapidocr_bridge"

if (Test-Path -LiteralPath $buildRoot) {
    Remove-Item -LiteralPath $buildRoot -Recurse -Force
}

& $Python -m venv $venvRoot
$venvPython = Join-Path $venvRoot "Scripts\python.exe"
& $venvPython -m pip install --disable-pip-version-check --no-input -r $requirements
& $venvPython -m PyInstaller `
    --noconfirm `
    --clean `
    --onedir `
    --name rapidocr_bridge `
    --collect-all rapidocr_onnxruntime `
    --distpath $distRoot `
    --workpath $workRoot `
    --specpath $specRoot `
    $bridgeScript

if (Test-Path -LiteralPath $targetRoot) {
    Remove-Item -LiteralPath $targetRoot -Recurse -Force
}

Copy-Item -LiteralPath (Join-Path $distRoot "rapidocr_bridge") -Destination $targetRoot -Recurse
Write-Host "OCR runtime created at: $targetRoot"
