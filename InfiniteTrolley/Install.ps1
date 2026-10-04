[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Low')]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$GameDir,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$gameRoot = (Resolve-Path -LiteralPath $GameDir).Path
foreach ($requiredFile in @('Data Center.exe', 'MelonLoader\net6\MelonLoader.dll', 'MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $gameRoot $requiredFile) -PathType Leaf)) {
        throw "Missing $requiredFile. Select a Data Center installation with MelonLoader initialized."
    }
}
if ((Get-Process -Name 'Data Center' -ErrorAction SilentlyContinue) -and -not $WhatIfPreference) {
    throw 'Exit Data Center before installing. This script does not close the game.'
}

$project = Join-Path $PSScriptRoot 'InfiniteTrolley.csproj'
if (-not $SkipBuild) {
    & dotnet build $project -c Release "-p:GameDir=$gameRoot"
    if ($LASTEXITCODE -ne 0) { throw 'Build failed; installation cancelled.' }
}

$sourceDll = Join-Path $PSScriptRoot 'bin\Release\net6.0\InfiniteTrolley.dll'
if (-not (Test-Path -LiteralPath $sourceDll -PathType Leaf)) {
    throw 'InfiniteTrolley.dll is missing. Build Release first, or omit -SkipBuild.'
}
$modsDir = Join-Path $gameRoot 'Mods'
$destinationDll = Join-Path $modsDir 'InfiniteTrolley.dll'
$builtHash = (Get-FileHash -LiteralPath $sourceDll -Algorithm SHA256).Hash
if (-not $PSCmdlet.ShouldProcess($destinationDll, 'Back up the existing mod and install InfiniteTrolley.dll')) {
    return
}

New-Item -ItemType Directory -Path $modsDir -Force | Out-Null
if (Test-Path -LiteralPath $destinationDll -PathType Leaf) {
    $backupDir = Join-Path $PSScriptRoot 'Backups'
    New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
    $backupName = 'InfiniteTrolley-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff') + '.dll.bak'
    Copy-Item -LiteralPath $destinationDll -Destination (Join-Path $backupDir $backupName)
}
Copy-Item -LiteralPath $sourceDll -Destination $destinationDll -Force
$installedHash = (Get-FileHash -LiteralPath $destinationDll -Algorithm SHA256).Hash
if ($builtHash -ne $installedHash) { throw 'Installed DLL does not match the build.' }
Write-Output "Installed: $destinationDll"
Write-Output "SHA256: $installedHash"
Write-Output 'Start Data Center to load the mod.'
