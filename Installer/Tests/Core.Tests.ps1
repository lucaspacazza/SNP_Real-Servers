$ErrorActionPreference = 'Stop'
$modulePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'Setup.Core.psm1'
$script:CoreAvailable = Test-Path -LiteralPath $modulePath -PathType Leaf
if ($script:CoreAvailable) { Import-Module $modulePath -Force }
$script:fixtureRoots = @()

function New-CoreFixture {
    $root = Join-Path ([IO.Path]::GetTempPath()) ('SnpInstallerCoreTests\' + [Guid]::NewGuid().ToString('N'))
    $game = Join-Path $root 'Steam outside x86\steamapps\common\Data Center'
    $package = Join-Path $root 'Package with spaces'
    foreach ($relative in @('Mods', 'UserData\RealServers\SaveIdentities', 'Mods\RealServers\Assets', 'saves', 'Data Center_Data')) {
        [void][IO.Directory]::CreateDirectory((Join-Path $game $relative))
    }
    [IO.File]::WriteAllText((Join-Path $game 'Data Center.exe'), 'fake executable for path discovery only')
    [IO.File]::WriteAllText((Join-Path $game 'saves\world.save'), 'protected save')
    [IO.File]::WriteAllText((Join-Path $game 'UserData\RealServers\SaveIdentities\world.json'), 'protected identity')
    [IO.File]::WriteAllText((Join-Path $game 'Mods\RealServers\Assets\private.glb'), 'protected personal model')
    [IO.File]::WriteAllText((Join-Path $game 'Mods\OtherMod.dll'), 'unrelated mod')
    $files = @()
    foreach ($id in @('RealServers', 'AutoCustomerDhcp', 'InfiniteTrolley')) {
        $path = 'Mods/' + $id + '.dll'
        $source = Join-Path $package ('Payload\' + $path)
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($source))
        [IO.File]::WriteAllText($source, 'new-' + $id)
        $files += [PSCustomObject]@{ mod = $id; path = $path; sha256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash }
    }
    $manifest = Join-Path $package 'Payload\manifest.json'
    @{ format = 1; files = $files } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifest -Encoding UTF8
    $script:fixtureRoots += $root
    return [PSCustomObject]@{ Root = $root; GameDir = $game; PackageRoot = $package; ManifestPath = $manifest }
}
function Save-CoreManifest($fixture, $manifest) { $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $fixture.ManifestPath -Encoding UTF8 }
function Assert-CoreProtected($fixture) {
    foreach ($entry in @{
        'saves\world.save' = 'protected save'
        'UserData\RealServers\SaveIdentities\world.json' = 'protected identity'
        'Mods\RealServers\Assets\private.glb' = 'protected personal model'
        'Mods\OtherMod.dll' = 'unrelated mod'
    }.GetEnumerator()) {
        [IO.File]::ReadAllText((Join-Path $fixture.GameDir $entry.Key)) | Should Be $entry.Value
    }
}

Describe 'SNP installer core contracts' {
    BeforeEach {
        if (-not $script:CoreAvailable) { throw 'Setup.Core.psm1 has not been implemented.' }
        Mock -ModuleName Setup.Core Get-Process { @() }
    }

    It 'publishes only the three supported mod names and DLLs' {
        $catalog = @(Get-SnpModCatalog)
        $catalog.Count | Should Be 3
        ($catalog.Id -join ',') | Should Be 'RealServers,AutoCustomerDhcp,InfiniteTrolley'
        ($catalog.Dll -join ',') | Should Be 'RealServers.dll,AutoCustomerDhcp.dll,InfiniteTrolley.dll'
    }
    It 'supports every single numeric choice and all' {
        (Get-SnpSelectedModIds -Selection '1') | Should Be 'RealServers'
        (Get-SnpSelectedModIds -Selection '2') | Should Be 'AutoCustomerDhcp'
        (Get-SnpSelectedModIds -Selection '3') | Should Be 'InfiniteTrolley'
        @(Get-SnpSelectedModIds -Selection '4').Count | Should Be 3
        @(Get-SnpSelectedModIds -Selection '0').Count | Should Be 0
    }
    It 'supports comma or space separated selections without duplicates' {
        ((Get-SnpSelectedModIds -Selection '1,3,1') -join ',') | Should Be 'RealServers,InfiniteTrolley'
        ((Get-SnpSelectedModIds -Selection '1 3') -join ',') | Should Be 'RealServers,InfiniteTrolley'
    }
    It 'rejects unsupported or mixed cancellation choices' {
        foreach ($invalid in @('', '5', 'abc', '0,1', '4,2', '1;3')) { { Get-SnpSelectedModIds -Selection $invalid } | Should Throw }
    }
    It 'finds a game path, pasted quoted executable and Steam outside Program Files' {
        $fixture = New-CoreFixture
        (Resolve-SnpGameDirectory -Path $fixture.GameDir) | Should Be $fixture.GameDir
        (Resolve-SnpGameDirectory -Path ('"' + (Join-Path $fixture.GameDir 'Data Center.exe') + '"')) | Should Be $fixture.GameDir
        (Resolve-SnpGameDirectory -Path (Join-Path $fixture.Root 'Steam outside x86')) | Should Be $fixture.GameDir
    }
    It 'accepts steamapps and common directories with spaces' {
        $fixture = New-CoreFixture
        (Resolve-SnpGameDirectory -Path (Join-Path $fixture.Root 'Steam outside x86\steamapps')) | Should Be $fixture.GameDir
        (Resolve-SnpGameDirectory -Path (Join-Path $fixture.Root 'Steam outside x86\steamapps\common')) | Should Be $fixture.GameDir
    }
    It 'reads nondefault Steam libraries from the modern libraryfolders manifest' {
        $fixture = New-CoreFixture
        $steam = Join-Path $fixture.Root 'Different Steam client'
        [void][IO.Directory]::CreateDirectory((Join-Path $steam 'steamapps'))
        $library = Join-Path $fixture.Root 'Steam outside x86'
        $escaped = $library.Replace('\', '\\')
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        [IO.File]::WriteAllText($vdf, '"libraryfolders" { "0" { "path" "' + $escaped + '" } }')
        (Resolve-SnpGameDirectory -Path $steam) | Should Be $fixture.GameDir
        (Resolve-SnpGameDirectory -Path $vdf) | Should Be $fixture.GameDir
    }
    It 'supports legacy libraryfolders paths' {
        $fixture = New-CoreFixture
        $steam = Join-Path $fixture.Root 'Legacy Steam'
        [void][IO.Directory]::CreateDirectory((Join-Path $steam 'steamapps'))
        $escaped = (Join-Path $fixture.Root 'Steam outside x86').Replace('\', '\\')
        [IO.File]::WriteAllText((Join-Path $steam 'steamapps\libraryfolders.vdf'), '"LibraryFolders" { "1" "' + $escaped + '" }')
        (Resolve-SnpGameDirectory -Path $steam) | Should Be $fixture.GameDir
    }
    It 'does not search recursively below an arbitrary unrelated folder' {
        $fixture = New-CoreFixture
        @(Find-SnpGameDirectories -Path $fixture.Root).Count | Should Be 0
    }
    It 'requires a unique game result instead of choosing an arbitrary installation' {
        $fixture = New-CoreFixture
        $common = Join-Path $fixture.Root 'Steam outside x86\steamapps\common'
        $other = Join-Path $common 'Another Data Center install'
        [void][IO.Directory]::CreateDirectory($other)
        [IO.File]::WriteAllText((Join-Path $other 'Data Center.exe'), 'second fake game')
        @(Find-SnpGameDirectories -Path $common).Count | Should Be 2
        { Resolve-SnpGameDirectory -Path $common } | Should Throw
        { Resolve-SnpGameDirectory -Path (Join-Path $fixture.Root 'missing')) } | Should Throw
    }
    It 'enforces child paths and accepts the Relative alias' {
        $fixture = New-CoreFixture
        (Get-SnpChildPath -Root $fixture.GameDir -RelativePath 'Mods/RealServers.dll') | Should Be (Join-Path $fixture.GameDir 'Mods\RealServers.dll')
        (Get-SnpChildPath -Root $fixture.GameDir -Relative 'Mods/RealServers.dll') | Should Be (Join-Path $fixture.GameDir 'Mods\RealServers.dll')
        foreach ($relative in @('..\outside.dll', 'Mods\..\saves\world.save', $fixture.GameDir, '.', 'Mods\fake.dll:secret')) {
            { Get-SnpChildPath -Root $fixture.GameDir -RelativePath $relative } | Should Throw
        }
    }
    It 'blocks a real junction leading outside the game root' {
        $fixture = New-CoreFixture
        $outside = Join-Path $fixture.Root 'Outside game'
        [void][IO.Directory]::CreateDirectory($outside)
        New-Item -ItemType Junction -Path (Join-Path $fixture.GameDir 'Redirected') -Target $outside | Out-Null
        { Get-SnpChildPath -Root $fixture.GameDir -RelativePath 'Redirected\must-not-write.dll' } | Should Throw
    }
    It 'allows ordinary OneDrive placeholder ancestors without a link target' {
        (Get-SnpChildPath -Root (Split-Path -Parent $PSScriptRoot) -RelativePath 'Tests/Core.Tests.ps1') | Should Be ([IO.Path]::GetFullPath($PSCommandPath))
    }
    It 'rejects a running game without closing or launching processes' {
        $fixture = New-CoreFixture
        Mock -ModuleName Setup.Core Get-Process { [PSCustomObject]@{ ProcessName = 'Data Center'; Id = 123 } }
        { Assert-SnpGameClosed -GameDir $fixture.GameDir } | Should Throw
    }
    It 'installs all DLLs and records ownership while preserving private files' {
        $fixture = New-CoreFixture
        Install-SnpMods -GameDir $fixture.GameDir -PackageRoot $fixture.PackageRoot -ModIds @('RealServers', 'AutoCustomerDhcp', 'InfiniteTrolley') | Out-Null
        foreach ($id in @('RealServers', 'AutoCustomerDhcp', 'InfiniteTrolley')) {
            [IO.File]::ReadAllText((Join-Path $fixture.GameDir ('Mods\' + $id + '.dll'))) | Should Be ('new-' + $id)
        }
        $state = Get-Content -LiteralPath (Join-Path $fixture.GameDir 'SNPInstaller\mod-state.json') -Raw | ConvertFrom-Json
        $state.format | Should Be 1
        @($state.files).Count | Should Be 3
        Assert-CoreProtected $fixture
    }
    It 'preserves earlier installed ownership during a partial later install' {
        $fixture = New-CoreFixture
        Install-SnpMods -GameDir $fixture.GameDir -PackageRoot $fixture.PackageRoot -ModIds @('RealServers') | Out-Null
        Install-SnpMods -GameDir $fixture.GameDir -PackageRoot $fixture.PackageRoot -ModIds @('AutoCustomerDhcp') | Out-Null
        (Test-Path -LiteralPath (Join-Path $fixture.GameDir 'Mods\InfiniteTrolley.dll')) | Should Be $false
        $state = Get-Content -LiteralPath (Join-Path $fixture.GameDir 'SNPInstaller\mod-state.json') -Raw | ConvertFrom-Json
        @($state.files).Count | Should Be 2
        Assert-CoreProtected $fixture
    }
    It 'backs up an existing DLL before replacement' {
        $fixture = New-CoreFixture
        [IO.File]::WriteAllText((Join-Path $fixture.GameDir 'Mods\RealServers.dll'), 'original version')
        Install-SnpMods -GameDir $fixture.GameDir -PackageRoot $fixture.PackageRoot -ModIds @('RealServers') | Out-Null
        $backups = @(Get-ChildItem -LiteralPath (Join-Path $fixture.GameDir 'SNPInstaller\Backups') -Filter RealServers.dll -File -Recurse)
        $backups.Count | Should Be 1
        [IO.File]::ReadAllText($backups[0].FullName) | Should Be 'original version'
    }
    It 'rejects any invalid payload hash before creating mod state or copying a DLL' {
        $fixture = New-CoreFixture
        [IO.File]::AppendAllText((Join-Path $fixture.PackageRoot 'Payload\Mods\InfiniteTrolley.dll'), 'tampered')
        { Install-SnpMods -GameDir $fixture.GameDir -PackageRoot $fixture.PackageRoot -ModIds @('RealServers') } | Should Throw
        (Test-Path -LiteralPath (Join-Path $fixture.GameDir 'Mods\RealServers.dll')) | Should Be $false
        (Test-Path -LiteralPath (Join-Path $fixture.GameDir 'SNPInstaller')) | Should Be $false
    }
    It 'rejects a missing selected DLL without partially installing other selected mods' {
        $fixture = New-CoreFixture
        [IO.File]::Delete((Join-Path $fixture.PackageRoot 'Payload\Mods\AutoCustomerDhcp.dll'))
        { Install-SnpMods -GameDir $fixture.GameDir -PackageRoot $fixture.PackageRoot -ModIds @('RealServers', 'AutoCustomerDhcp') } | Should Throw
        (Test-Path -LiteralPath (Join-Path $fixture.GameDir 'Mods\RealServers.dll')) | Should Be $false
    }
    It 'rejects a payload manifest targeting saves, assets or an unrelated DLL' {
        foreach ($badPath in @('../outside.dll', 'saves/world.save', 'Mods/RealServers/Assets/private.glb', 'Mods/OtherMod.dll')) {
            $fixture = New-CoreFixture
            $manifest = Get-Content -LiteralPath $fixture.ManifestPath -Raw | ConvertFrom-Json
            $manifest.files[0].path = $badPath
            Save-CoreManifest $fixture $manifest
            { Install-SnpMods -GameDir $fixture.GameDir -PackageRoot $fixture.PackageRoot -ModIds @('RealServers') } | Should Throw
            Assert-CoreProtected $fixture
        }
    }
    It 'rejects a malformed or duplicate manifest and an unknown mod ID' {
        $fixture = New-CoreFixture
        $manifest = Get-Content -LiteralPath $fixture.ManifestPath -Raw | ConvertFrom-Json
        $manifest.files = @($manifest.files[0], $manifest.files[0]); Save-CoreManifest $fixture $manifest
        { Install-SnpMods -GameDir $fixture.GameDir -PackageRoot $fixture.PackageRoot -ModIds @('RealServers') } | Should Throw
        { Install-SnpMods -GameDir $fixture.GameDir -PackageRoot $fixture.PackageRoot -ModIds @('OtherMod') } | Should Throw
    }
    It 'rolls every replaced DLL and ownership state back if a later copy fails' {
        $fixture = New-CoreFixture
        Install-SnpMods -GameDir $fixture.GameDir -PackageRoot $fixture.PackageRoot -ModIds @('InfiniteTrolley') | Out-Null
        $stateFile = Join-Path $fixture.GameDir 'SNPInstaller\mod-state.json'
        $before = (Get-FileHash -LiteralPath $stateFile).Hash
        [IO.File]::WriteAllText((Join-Path $fixture.GameDir 'Mods\RealServers.dll'), 'old real')
        [IO.File]::WriteAllText((Join-Path $fixture.GameDir 'Mods\AutoCustomerDhcp.dll'), 'old dhcp')
        $global:SnpCoreFailDestination = Join-Path $fixture.GameDir 'Mods\AutoCustomerDhcp.dll'
        Mock -ModuleName Setup.Core Copy-Item {
            param($LiteralPath, $Destination)
            if ($Destination -eq $global:SnpCoreFailDestination) { throw 'Injected second destination failure' }
            Microsoft.PowerShell.Management\Copy-Item -LiteralPath $LiteralPath -Destination $Destination -Force -ErrorAction Stop
        }
        { Install-SnpMods -GameDir $fixture.GameDir -PackageRoot $fixture.PackageRoot -ModIds @('RealServers', 'AutoCustomerDhcp') } | Should Throw
        [IO.File]::ReadAllText((Join-Path $fixture.GameDir 'Mods\RealServers.dll')) | Should Be 'old real'
        [IO.File]::ReadAllText((Join-Path $fixture.GameDir 'Mods\AutoCustomerDhcp.dll')) | Should Be 'old dhcp'
        (Get-FileHash -LiteralPath $stateFile).Hash | Should Be $before
        Assert-CoreProtected $fixture
    }
    It 'WhatIf install and uninstall make no filesystem changes' {
        $fixture = New-CoreFixture
        Install-SnpMods -GameDir $fixture.GameDir -PackageRoot $fixture.PackageRoot -ModIds @('RealServers') -WhatIf | Out-Null
        (Test-Path -LiteralPath (Join-Path $fixture.GameDir 'SNPInstaller')) | Should Be $false
        (Test-Path -LiteralPath (Join-Path $fixture.GameDir 'Mods\RealServers.dll')) | Should Be $false
        [IO.File]::WriteAllText((Join-Path $fixture.GameDir 'Mods\RealServers.dll'), 'existing real')
        Uninstall-SnpMods -GameDir $fixture.GameDir -ModIds @('RealServers') -WhatIf | Out-Null
        [IO.File]::ReadAllText((Join-Path $fixture.GameDir 'Mods\RealServers.dll')) | Should Be 'existing real'
    }
    It 'uninstalls selected mods only and retains the remaining ownership entries' {
        $fixture = New-CoreFixture
        Install-SnpMods -GameDir $fixture.GameDir -PackageRoot $fixture.PackageRoot -ModIds @('RealServers', 'AutoCustomerDhcp', 'InfiniteTrolley') | Out-Null
        Uninstall-SnpMods -GameDir $fixture.GameDir -ModIds @('AutoCustomerDhcp') | Out-Null
        (Test-Path -LiteralPath (Join-Path $fixture.GameDir 'Mods\AutoCustomerDhcp.dll')) | Should Be $false
        (Test-Path -LiteralPath (Join-Path $fixture.GameDir 'Mods\RealServers.dll')) | Should Be $true
        (Test-Path -LiteralPath (Join-Path $fixture.GameDir 'Mods\InfiniteTrolley.dll')) | Should Be $true
        $state = Get-Content -LiteralPath (Join-Path $fixture.GameDir 'SNPInstaller\mod-state.json') -Raw | ConvertFrom-Json
        @($state.files).Count | Should Be 2
        Assert-CoreProtected $fixture
    }
    It 'uninstalls all three mods while preserving loader, backups, private assets and saves' {
        $fixture = New-CoreFixture
        [void][IO.Directory]::CreateDirectory((Join-Path $fixture.GameDir 'MelonLoader'))
        [IO.File]::WriteAllText((Join-Path $fixture.GameDir 'MelonLoader\keep.txt'), 'loader must remain')
        [IO.File]::WriteAllText((Join-Path $fixture.GameDir 'Mods\RealServers.dll'), 'original before installer')
        Install-SnpMods -GameDir $fixture.GameDir -PackageRoot $fixture.PackageRoot -ModIds @('RealServers', 'AutoCustomerDhcp', 'InfiniteTrolley') | Out-Null
        Uninstall-SnpMods -GameDir $fixture.GameDir -ModIds @('RealServers', 'AutoCustomerDhcp', 'InfiniteTrolley') | Out-Null
        foreach ($id in @('RealServers', 'AutoCustomerDhcp', 'InfiniteTrolley')) { (Test-Path -LiteralPath (Join-Path $fixture.GameDir ('Mods\' + $id + '.dll'))) | Should Be $false }
        [IO.File]::ReadAllText((Join-Path $fixture.GameDir 'MelonLoader\keep.txt')) | Should Be 'loader must remain'
        @(Get-ChildItem -LiteralPath (Join-Path $fixture.GameDir 'SNPInstaller\Backups') -File -Recurse).Count | Should BeGreaterThan 0
        Assert-CoreProtected $fixture
    }
    It 'can remove an exact DLL from a manual install without ownership state' {
        $fixture = New-CoreFixture
        [IO.File]::WriteAllText((Join-Path $fixture.GameDir 'Mods\RealServers.dll'), 'manual real')
        [IO.File]::WriteAllText((Join-Path $fixture.GameDir 'Mods\InfiniteTrolley.dll'), 'manual trolley')
        Uninstall-SnpMods -GameDir $fixture.GameDir -ModIds @('RealServers') | Out-Null
        (Test-Path -LiteralPath (Join-Path $fixture.GameDir 'Mods\RealServers.dll')) | Should Be $false
        [IO.File]::ReadAllText((Join-Path $fixture.GameDir 'Mods\InfiniteTrolley.dll')) | Should Be 'manual trolley'
        Assert-CoreProtected $fixture
    }
    It 'rejects a forged ownership state naming private assets or another mod' {
        $fixture = New-CoreFixture
        [void][IO.Directory]::CreateDirectory((Join-Path $fixture.GameDir 'SNPInstaller'))
        @{ format = 1; files = @(@{ mod = 'RealServers'; path = 'Mods/OtherMod.dll'; sha256 = ('a' * 64) }) } | ConvertTo-Json -Depth 5 |
            Set-Content -LiteralPath (Join-Path $fixture.GameDir 'SNPInstaller\mod-state.json') -Encoding UTF8
        { Uninstall-SnpMods -GameDir $fixture.GameDir -ModIds @('RealServers') } | Should Throw
        Assert-CoreProtected $fixture
    }
    It 'refuses mutations while the game is running' {
        $fixture = New-CoreFixture
        Mock -ModuleName Setup.Core Get-Process { [PSCustomObject]@{ ProcessName = 'Data Center'; Id = 123 } }
        { Install-SnpMods -GameDir $fixture.GameDir -PackageRoot $fixture.PackageRoot -ModIds @('RealServers') } | Should Throw
        { Uninstall-SnpMods -GameDir $fixture.GameDir -ModIds @('RealServers') } | Should Throw
        (Test-Path -LiteralPath (Join-Path $fixture.GameDir 'SNPInstaller')) | Should Be $false
    }
}

Remove-Variable -Name SnpCoreFailDestination -Scope Global -ErrorAction SilentlyContinue
