[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Low')]
param(
    [Parameter(Mandatory = $true)]
    [string] $GameDir,
    [string] $AssetSource,
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$gameRoot = [IO.Path]::GetFullPath($GameDir)

function Resolve-ChildPath([string] $Root, [string] $Relative) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative)) {
        throw "Expected a relative path: $Relative"
    }
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $full = [IO.Path]::GetFullPath([IO.Path]::Combine($prefix, $Relative))
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path escaped its installation root: $Relative"
    }
    return $full
}

foreach ($required in @('Data Center.exe', 'MelonLoader\net6\MelonLoader.dll', 'MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll')) {
    if (-not (Test-Path -LiteralPath (Resolve-ChildPath $gameRoot $required) -PathType Leaf)) {
        throw "Required game/MelonLoader file missing: $required"
    }
}

if (-not $SkipBuild) {
    & dotnet build (Join-Path $projectRoot 'RealServers.csproj') -c Release -v:q "-p:GameDir=$gameRoot"
    if ($LASTEXITCODE -ne 0) { throw 'RealServers build failed; nothing installed.' }
}

$modRoot = Resolve-ChildPath $gameRoot 'Mods'
$manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'Assets\runtime-files.json') -Raw | ConvertFrom-Json
if ($manifest.format -ne 1 -or $manifest.destinationRelativeRoot -ne 'RealServers') {
    throw 'Unsupported runtime asset manifest.'
}
$entries = @([PSCustomObject]@{
    Relative = 'RealServers.dll'
    Source = Resolve-ChildPath $projectRoot 'bin\Release\netcoreapp6.0\RealServers.dll'
    Destination = Resolve-ChildPath $modRoot 'RealServers.dll'
})
if (-not [string]::IsNullOrWhiteSpace($AssetSource)) {
    $assetRoot = [IO.Path]::GetFullPath($AssetSource)
    if (-not (Test-Path -LiteralPath $assetRoot -PathType Container)) {
        throw 'AssetSource must be a folder containing your external RealServers runtime asset pack.'
    }
    foreach ($relative in $manifest.files) {
        if ($relative -notmatch '^Assets[/\\].+\.(glb|png)$') {
            throw "Unsupported runtime asset path: $relative"
        }
        $entries += [PSCustomObject]@{
            Relative = 'RealServers\' + $relative
            Source = Resolve-ChildPath $assetRoot $relative
            Destination = Resolve-ChildPath $modRoot ('RealServers\' + $relative)
        }
    }
}
foreach ($entry in $entries) {
    if (-not (Test-Path -LiteralPath $entry.Source -PathType Leaf)) {
        throw "Runtime file missing; nothing installed: $($entry.Source)"
    }
}
if (@($entries.Destination | Select-Object -Unique).Count -ne $entries.Count) {
    throw 'Duplicate runtime destinations.'
}

$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($entries[0].Source).FileVersion
if (Get-Process -Name 'Data Center' -ErrorAction SilentlyContinue) {
    Write-Host 'Game is running. Files apply on its next start; the game will remain open.'
}
if (-not $PSCmdlet.ShouldProcess($modRoot, "Install RealServers $version and $($entries.Count - 1) runtime assets, preserving saves and other mods")) {
    return
}

$backupRoot = Resolve-ChildPath $projectRoot ('Backups\' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$installManifestPath = Resolve-ChildPath $modRoot 'RealServers\install-manifest.json'
$backupEntries = @($entries | Where-Object { Test-Path -LiteralPath $_.Destination -PathType Leaf })
if (Test-Path -LiteralPath $installManifestPath -PathType Leaf) {
    $backupEntries += [PSCustomObject]@{ Relative = 'RealServers\install-manifest.json'; Destination = $installManifestPath }
}
foreach ($entry in $backupEntries) {
    $backupPath = Resolve-ChildPath $backupRoot $entry.Relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($backupPath)) | Out-Null
    Copy-Item -LiteralPath $entry.Destination -Destination $backupPath
}

$installed = @()
foreach ($entry in $entries) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($entry.Destination)) | Out-Null
    Copy-Item -LiteralPath $entry.Source -Destination $entry.Destination -Force
    $sourceHash = (Get-FileHash -LiteralPath $entry.Source -Algorithm SHA256).Hash
    $destinationHash = (Get-FileHash -LiteralPath $entry.Destination -Algorithm SHA256).Hash
    if ($sourceHash -ne $destinationHash) { throw "Installed file checksum mismatch: $($entry.Relative)" }
    $installed += [PSCustomObject]@{
        relativePath = $entry.Relative.Replace('\', '/')
        bytes = (Get-Item -LiteralPath $entry.Destination).Length
        sha256 = $destinationHash
    }
}
[PSCustomObject]@{
    format = 1
    version = $version
    installedUtc = [DateTime]::UtcNow.ToString('o')
    files = $installed
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $installManifestPath -Encoding UTF8

Write-Host "Installed RealServers ${version}: $($installed.Count) files verified with SHA256."
if ($backupEntries.Count -gt 0) { Write-Host "Previous installed files backed up to $backupRoot" }
