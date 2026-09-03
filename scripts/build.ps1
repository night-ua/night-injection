param(
    [switch]$SkipDependencies
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $projectRoot

if (-not $SkipDependencies) {
    python -m pip install -r requirements.txt
    python -m pip install "pyinstaller>=6.10"
}

python -c "from PIL import Image, ImageOps; src=Image.open('assets/logo.jpg').convert('RGB'); icon=ImageOps.fit(src,(256,256)); icon.save('assets/logo.ico',sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])"
python -m PyInstaller --noconfirm --clean night-injection.spec

$executable = Join-Path $projectRoot "dist\night-injection\night-injection.exe"
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Build completed without producing $executable"
}

Write-Host "Release ready: $executable"
