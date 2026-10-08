# Run with Windows PowerShell 5.1. All files and network substitutes stay in a temporary sandbox.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$script:Passed = 0
$script:Failed = 0
$script:Sandboxes = New-Object 'System.Collections.Generic.List[string]'
$script:ModulePath = Join-Path (Split-Path $PSScriptRoot -Parent) 'Bootstrap.psm1'
$script:Bootstrap = $null
if (Test-Path -LiteralPath $script:ModulePath) { $script:Bootstrap = Import-Module $script:ModulePath -Force -PassThru }
else {
    $script:Bootstrap = New-Module -Name SNPBootstrapMissing -ScriptBlock {
        function Ensure-SnpMelonLoader { param($GameDir, [switch]$WhatIf) throw 'Bootstrap implementation missing: Ensure-SnpMelonLoader.' }
        function Uninstall-SnpMelonLoader { param($GameDir, [switch]$WhatIf) throw 'Bootstrap implementation missing: Uninstall-SnpMelonLoader.' }
        Export-ModuleMember -Function Ensure-SnpMelonLoader, Uninstall-SnpMelonLoader
    }
    Import-Module $script:Bootstrap
}

function global:Get-SnpChildPath {
    param([string]$Root, [string]$RelativePath)
    $base = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $resolved = [IO.Path]::GetFullPath((Join-Path $Root $RelativePath))
    if (!$resolved.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe child path.' }
    $resolved
}
function global:Assert-SnpGameClosed { param([string]$GameDir) if ($script:GameOpen) { throw 'Close the game before installing.' } }

function Assert-True { param([bool]$Value, [string]$Message = 'Expected condition failed.') if (!$Value) { throw $Message } }
function Expect-Throw {
    param([scriptblock]$Action, [string]$Pattern)
    try { & $Action | Out-Null } catch { Assert-True ($_.Exception.Message -match $Pattern) "Expected '$Pattern', got '$($_.Exception.Message)'."; return }
    throw "Expected failure matching '$Pattern'."
}
function Invoke-Bootstrap {
    param([string]$Action = 'Ensure', [string]$GameDir, [switch]$WhatIf)
    if (!$script:Bootstrap) { throw 'Bootstrap implementation missing: Ensure-SnpMelonLoader and Uninstall-SnpMelonLoader.' }
    if ($Action -eq 'Ensure') { Ensure-SnpMelonLoader -GameDir $GameDir -WhatIf:$WhatIf }
    else { Uninstall-SnpMelonLoader -GameDir $GameDir -WhatIf:$WhatIf }
}
function New-Zip {
    param([string]$Path, [hashtable]$Entries)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Create)
    $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        foreach ($name in ($Entries.Keys | Sort-Object)) {
            $entry = $archive.CreateEntry($name)
            $writer = New-Object IO.StreamWriter($entry.Open())
            try { $writer.Write([string]$Entries[$name]) } finally { $writer.Dispose() }
        }
    } finally { $archive.Dispose(); $stream.Dispose() }
}
function New-Fixture {
    param([hashtable]$ExtraEntries = @{})
    $base = Join-Path ([IO.Path]::GetTempPath()) ('SNPBootstrapTests-' + [Guid]::NewGuid().ToString('N'))
    $game = Join-Path $base 'Steam & Games\Data Center'
    [IO.Directory]::CreateDirectory($game) | Out-Null
    [IO.File]::WriteAllText((Join-Path $game 'Data Center.exe'), 'sandbox game marker')
    $script:Sandboxes.Add($base)
    $entries = @{ 'version.dll' = 'proxy'; 'dobby.dll' = 'dobby'; 'MelonLoader/net6/MelonLoader.dll' = 'loader'; 'MelonLoader/net6/0Harmony.dll' = 'harmony' }
    foreach ($name in $ExtraEntries.Keys) { $entries[$name] = $ExtraEntries[$name] }
    $zip = Join-Path $base 'loader.zip'
    New-Zip -Path $zip -Entries $entries
    if ($script:Bootstrap) {
        & $script:Bootstrap {
            param($FixtureZip, $FixtureHash)
            $script:MelonLoaderSha256 = $FixtureHash
            $script:FixtureZip = $FixtureZip
            $script:FakeLoaderVersion = '0.7.2.0'
            $script:Downloads = 0
            $script:FailCopyAfter = -1
            $script:CopyCount = 0
            function script:Invoke-SnpLoaderDownload {
                param([string]$Uri, [string]$OutFile)
                $script:Downloads++
                [IO.File]::Copy($script:FixtureZip, $OutFile, $true)
            }
            function script:Get-SnpLoaderVersion { param([string]$Path) $script:FakeLoaderVersion }
            function script:Copy-SnpLoaderFile {
                param([string]$Source, [string]$Destination)
                $script:CopyCount++
                if ($script:FailCopyAfter -ge 0 -and $script:CopyCount -gt $script:FailCopyAfter) { throw 'Injected file copy failure.' }
                [IO.File]::Copy($Source, $Destination, $true)
            }
        } $zip (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
    }
    $script:GameOpen = $false
    [pscustomobject]@{ Base = $base; GameDir = $game; Zip = $zip }
}
function Get-Downloads { & $script:Bootstrap { $script:Downloads } }
function Run {
    param([string]$Name, [scriptblock]$Body)
    try { & $Body; $script:Passed++; Write-Host "PASS $Name" }
    catch { $script:Failed++; Write-Host "FAIL $Name : $($_.Exception.Message)" }
}

Run 'Fresh installation downloads pinned loader, installs exact files and records ownership' {
    $f = New-Fixture
    $result = Invoke-Bootstrap -GameDir $f.GameDir
    Assert-True ($result.Installed -and $result.Version -eq '0.7.2' -and $result.FirstRunRequired)
    Assert-True (Test-Path -LiteralPath (Join-Path $f.GameDir 'version.dll'))
    $state = Get-Content -LiteralPath (Join-Path $f.GameDir 'SNPInstaller/loader-state.json') -Raw | ConvertFrom-Json
    Assert-True ($state.Entries.Count -eq 4)
    Assert-True ((Get-Downloads) -eq 1)
}
Run 'WhatIf has no download and leaves the game directory untouched' {
    $f = New-Fixture
    $result = Invoke-Bootstrap -GameDir $f.GameDir -WhatIf
    Assert-True (!$result.Installed)
    Assert-True ((Get-Downloads) -eq 0)
    Assert-True ((Get-ChildItem -LiteralPath $f.GameDir -Force).Count -eq 1)
}
Run 'Existing matching loader is preserved and never becomes installer-owned' {
    $f = New-Fixture
    [IO.Directory]::CreateDirectory((Join-Path $f.GameDir 'MelonLoader/net6')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $f.GameDir 'MelonLoader/net6/MelonLoader.dll'), 'existing')
    [IO.File]::WriteAllText((Join-Path $f.GameDir 'version.dll'), 'existing proxy')
    $result = Invoke-Bootstrap -GameDir $f.GameDir
    Assert-True (!$result.Installed -and $result.Version -eq '0.7.2')
    Assert-True ((Get-Downloads) -eq 0)
    Assert-True (!(Test-Path -LiteralPath (Join-Path $f.GameDir 'SNPInstaller/loader-state.json')))
    Expect-Throw { Invoke-Bootstrap -Action Remove -GameDir $f.GameDir } 'not managed|ownership'
}
Run 'An existing different loader version is rejected without downgrade' {
    $f = New-Fixture
    [IO.Directory]::CreateDirectory((Join-Path $f.GameDir 'MelonLoader/net6')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $f.GameDir 'MelonLoader/net6/MelonLoader.dll'), 'other version')
    [IO.File]::WriteAllText((Join-Path $f.GameDir 'version.dll'), 'other proxy')
    & $script:Bootstrap { $script:FakeLoaderVersion = '0.8.0.0' }
    Expect-Throw { Invoke-Bootstrap -GameDir $f.GameDir } 'version|0.7.2'
    Assert-True ((Get-Downloads) -eq 0)
}
Run 'A partial existing loader installation is rejected' {
    $f = New-Fixture
    [IO.Directory]::CreateDirectory((Join-Path $f.GameDir 'MelonLoader/net6')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $f.GameDir 'MelonLoader/net6/MelonLoader.dll'), 'existing')
    Expect-Throw { Invoke-Bootstrap -GameDir $f.GameDir } 'incomplete|version.dll'
}
Run 'Wrong archive SHA256 is rejected before copying any game file' {
    $f = New-Fixture
    & $script:Bootstrap { $script:MelonLoaderSha256 = ('0' * 64) }
    Expect-Throw { Invoke-Bootstrap -GameDir $f.GameDir } 'SHA256|checksum'
    Assert-True (!(Test-Path -LiteralPath (Join-Path $f.GameDir 'version.dll')))
}
Run 'Archive traversal is rejected before copying any payload' {
    $f = New-Fixture -ExtraEntries @{ '../escape.dll' = 'bad' }
    Expect-Throw { Invoke-Bootstrap -GameDir $f.GameDir } 'archive|path|entry'
    Assert-True (!(Test-Path -LiteralPath (Join-Path $f.GameDir 'version.dll')))
    Assert-True (!(Test-Path -LiteralPath (Join-Path $f.Base 'escape.dll')))
}
Run 'Unexpected archive root files are rejected before copying' {
    $f = New-Fixture -ExtraEntries @{ 'unrelated.exe' = 'bad' }
    Expect-Throw { Invoke-Bootstrap -GameDir $f.GameDir } 'archive|entry|allow'
    Assert-True (!(Test-Path -LiteralPath (Join-Path $f.GameDir 'version.dll')))
}
Run 'Pre-existing proxy is backed up and restored on owned-loader uninstall' {
    $f = New-Fixture
    [IO.File]::WriteAllText((Join-Path $f.GameDir 'version.dll'), 'prior proxy')
    Invoke-Bootstrap -GameDir $f.GameDir | Out-Null
    Invoke-Bootstrap -Action Remove -GameDir $f.GameDir | Out-Null
    Assert-True (([IO.File]::ReadAllText((Join-Path $f.GameDir 'version.dll'))) -eq 'prior proxy')
    Assert-True (!(Test-Path -LiteralPath (Join-Path $f.GameDir 'dobby.dll')))
}
Run 'Rollback restores a previous proxy after a partial file-copy failure' {
    $f = New-Fixture
    [IO.File]::WriteAllText((Join-Path $f.GameDir 'version.dll'), 'prior proxy')
    & $script:Bootstrap { $script:FailCopyAfter = 2 }
    Expect-Throw { Invoke-Bootstrap -GameDir $f.GameDir } 'copy failure'
    Assert-True (([IO.File]::ReadAllText((Join-Path $f.GameDir 'version.dll'))) -eq 'prior proxy')
    Assert-True (!(Test-Path -LiteralPath (Join-Path $f.GameDir 'MelonLoader/net6/MelonLoader.dll')))
    Assert-True (!(Test-Path -LiteralPath (Join-Path $f.GameDir 'SNPInstaller/loader-state.json')))
}
Run 'Loader removal refuses when another mod remains' {
    $f = New-Fixture
    Invoke-Bootstrap -GameDir $f.GameDir | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $f.GameDir 'Mods')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $f.GameDir 'Mods/OtherMod.dll'), 'other')
    Expect-Throw { Invoke-Bootstrap -Action Remove -GameDir $f.GameDir } 'mods|plugins'
    Assert-True (Test-Path -LiteralPath (Join-Path $f.GameDir 'version.dll'))
}
Run 'Loader removal refuses when another plugin remains' {
    $f = New-Fixture
    Invoke-Bootstrap -GameDir $f.GameDir | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $f.GameDir 'Plugins/nested')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $f.GameDir 'Plugins/nested/OtherPlugin.dll'), 'other')
    Expect-Throw { Invoke-Bootstrap -Action Remove -GameDir $f.GameDir } 'mods|plugins'
}
Run 'Changed owned file is preserved and prevents loader removal' {
    $f = New-Fixture
    Invoke-Bootstrap -GameDir $f.GameDir | Out-Null
    [IO.File]::WriteAllText((Join-Path $f.GameDir 'version.dll'), 'changed externally')
    Expect-Throw { Invoke-Bootstrap -Action Remove -GameDir $f.GameDir } 'changed|modified|hash'
    Assert-True (([IO.File]::ReadAllText((Join-Path $f.GameDir 'version.dll'))) -eq 'changed externally')
}
Run 'Loader removal keeps UserData, save files and unrelated MelonLoader files' {
    $f = New-Fixture
    Invoke-Bootstrap -GameDir $f.GameDir | Out-Null
    foreach ($name in @('UserData/RealServers/identity.json', 'saves/player.save', 'MelonLoader/unrelated.txt')) {
        $path = Join-Path $f.GameDir $name
        [IO.Directory]::CreateDirectory((Split-Path $path -Parent)) | Out-Null
        [IO.File]::WriteAllText($path, 'keep')
    }
    Invoke-Bootstrap -Action Remove -GameDir $f.GameDir | Out-Null
    foreach ($name in @('UserData/RealServers/identity.json', 'saves/player.save', 'MelonLoader/unrelated.txt')) { Assert-True (Test-Path -LiteralPath (Join-Path $f.GameDir $name)) }
}
Run 'Game-open guard blocks installation and removal without closing any process' {
    $f = New-Fixture
    $script:GameOpen = $true
    Expect-Throw { Invoke-Bootstrap -GameDir $f.GameDir } 'Close the game'
    $script:GameOpen = $false
    Invoke-Bootstrap -GameDir $f.GameDir | Out-Null
    $script:GameOpen = $true
    Expect-Throw { Invoke-Bootstrap -Action Remove -GameDir $f.GameDir } 'Close the game'
    $script:GameOpen = $false
}

Write-Host "Bootstrap: $script:Passed passed; $script:Failed failed. No real game or network installer was used."
# Sandboxes are intentionally retained for inspection; no recursive deletion is performed.
if ($script:Failed -gt 0) { exit 1 }
