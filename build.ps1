param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$workRoot = Join-Path $repoRoot 'build\out'
$portableOutput = Join-Path $workRoot 'portable'
$uninstallerOutput = Join-Path $workRoot 'uninstaller'
$setupOutput = Join-Path $workRoot 'setup'
$artifacts = Join-Path $repoRoot 'artifacts'
$portableZip = Join-Path $artifacts 'NightInjection-Portable.zip'
$setupFile = Join-Path $artifacts 'NightInjection-Setup.exe'
$hashFile = Join-Path $artifacts 'SHA256.txt'

function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code $LASTEXITCODE."
    }
}

if (Test-Path -LiteralPath $workRoot) {
    $resolvedWork = [IO.Path]::GetFullPath($workRoot)
    $requiredPrefix = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedWork.StartsWith($requiredPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a build folder outside the repository: $resolvedWork"
    }
    Remove-Item -LiteralPath $resolvedWork -Recurse -Force
}

New-Item -ItemType Directory -Path $portableOutput, $uninstallerOutput, $setupOutput, $artifacts -Force | Out-Null

& (Join-Path $repoRoot 'build\Generate-AppAssets.ps1')

Invoke-DotNet @('restore', (Join-Path $repoRoot 'NightInjection.sln'), '-p:Platform=x64', '-r', 'win-x64')
Invoke-DotNet @('build', (Join-Path $repoRoot 'NightInjection.sln'), '-c', $Configuration, '-p:Platform=x64', '--no-restore')
if (-not $SkipTests) {
    Invoke-DotNet @('test', (Join-Path $repoRoot 'tests\NightInjection.Tests\NightInjection.Tests.csproj'), '-c', $Configuration, '--no-build', '--no-restore')
}

Invoke-DotNet @(
    'publish',
    (Join-Path $repoRoot 'src\NightInjection.UI\NightInjection.UI.csproj'),
    '-c', $Configuration,
    '-p:Platform=x64',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '--no-restore',
    '-o', $portableOutput
)

$publishedExecutable = Join-Path $portableOutput 'NightInjection.UI.exe'
$simpleExecutable = Join-Path $portableOutput 'NightInjection.exe'
if (-not (Test-Path -LiteralPath $publishedExecutable)) {
    throw "Published executable was not created: $publishedExecutable"
}
Move-Item -LiteralPath $publishedExecutable -Destination $simpleExecutable -Force

Get-ChildItem -LiteralPath $portableOutput -Filter '*.pdb' -File -Recurse | Remove-Item -Force
if (Test-Path -LiteralPath $portableZip) {
    Remove-Item -LiteralPath $portableZip -Force
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory(
    $portableOutput,
    $portableZip,
    [IO.Compression.CompressionLevel]::Optimal,
    $false)

Invoke-DotNet @(
    'publish',
    (Join-Path $repoRoot 'installer\NightInjection.Uninstall\NightInjection.Uninstall.csproj'),
    '-c', $Configuration,
    '-r', 'win-x64',
    '--self-contained', 'true',
    '--no-restore',
    '-o', $uninstallerOutput
)
$uninstallerFile = Join-Path $uninstallerOutput 'Uninstall.exe'
if (-not (Test-Path -LiteralPath $uninstallerFile)) {
    throw "Uninstaller output was not created: $uninstallerFile"
}

Invoke-DotNet @(
    'publish',
    (Join-Path $repoRoot 'installer\NightInjection.Setup\NightInjection.Setup.csproj'),
    '-c', $Configuration,
    '-r', 'win-x64',
    "-p:InstallerPayload=$portableZip",
    "-p:UninstallerPayload=$uninstallerFile",
    '--self-contained', 'true',
    '--no-restore',
    '-o', $setupOutput
)

$builtSetup = Join-Path $setupOutput 'NightInjection-Setup.exe'
if (-not (Test-Path -LiteralPath $builtSetup)) {
    throw "Setup output was not created: $builtSetup"
}
Copy-Item -LiteralPath $builtSetup -Destination $setupFile -Force

$verification = Start-Process -FilePath $setupFile -ArgumentList '/verify' -WindowStyle Hidden -Wait -PassThru
if ($verification.ExitCode -ne 0) {
    throw "Setup payload verification failed with exit code $($verification.ExitCode)."
}

$hashLines = foreach ($file in @($portableZip, $setupFile)) {
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    "$hash  $([IO.Path]::GetFileName($file))"
}
$hashLines | Set-Content -LiteralPath $hashFile -Encoding ascii

Write-Host ''
Write-Host 'Build complete:' -ForegroundColor Green
Get-Item -LiteralPath $portableZip, $setupFile, $hashFile |
    Select-Object Name, Length, LastWriteTime |
    Format-Table -AutoSize
