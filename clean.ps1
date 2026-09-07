param(
    [switch]$KeepArtifacts
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
$repoPrefix = $repoRoot + [IO.Path]::DirectorySeparatorChar

function Remove-GeneratedItem([string]$Path, [switch]$Recurse) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    if ($fullPath -eq $repoRoot -or
        -not $fullPath.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside the repository: $fullPath"
    }

    if (-not (Test-Path -LiteralPath $fullPath)) {
        return
    }

    if ($Recurse) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
    else {
        Remove-Item -LiteralPath $fullPath -Force
    }

    Write-Host "Removed $($fullPath.Substring($repoPrefix.Length))"
}

$projects = @(
    'installer\NightInjection.Setup',
    'installer\NightInjection.Uninstall',
    'src\NightInjection.Core',
    'src\NightInjection.Infrastructure',
    'src\NightInjection.UI',
    'tests\NightInjection.Tests'
)

foreach ($project in $projects) {
    Remove-GeneratedItem (Join-Path $repoRoot "$project\bin") -Recurse
    Remove-GeneratedItem (Join-Path $repoRoot "$project\obj") -Recurse
}

foreach ($directory in @('.vs', 'TestResults', 'AppPackages', 'BundleArtifacts', 'publish')) {
    Remove-GeneratedItem (Join-Path $repoRoot $directory) -Recurse
}

foreach ($directory in @(
    'build\out',
    'build\e2e-import-data',
    'build\e2e-import-temp',
    'build\e2e-import-ui-data',
    'build\e2e-import-ui-temp'
)) {
    Remove-GeneratedItem (Join-Path $repoRoot $directory) -Recurse
}

Get-ChildItem -LiteralPath (Join-Path $repoRoot 'build') -File -Filter '*.png' -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-GeneratedItem $_.FullName }

Get-ChildItem -LiteralPath (Join-Path $repoRoot 'build') -Directory -Filter 'close-probe-*' -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-GeneratedItem $_.FullName -Recurse }

if (-not $KeepArtifacts) {
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'artifacts') -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ne 'README.md' } |
        ForEach-Object { Remove-GeneratedItem $_.FullName -Recurse:$_.PSIsContainer }
}

Write-Host 'Workspace is clean for source upload.' -ForegroundColor Green
