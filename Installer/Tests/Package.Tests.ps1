[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$script:Passed = 0
$script:Failed = 0
$script:FixtureRoots = [Collections.Generic.List[string]]::new()
$sourceRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$packageSource = Join-Path $sourceRoot 'Package.ps1'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}
function Test-Case([string] $Name, [scriptblock] $Body) {
    try { & $Body; $script:Passed++; Write-Output "PASS $Name" }
    catch { $script:Failed++; Write-Output "FAIL $Name : $($_.Exception.Message)" }
}
function Write-FixtureFile([string] $Root, [string] $RelativePath, [string] $Content = 'fixture') {
    $path = Join-Path $Root $RelativePath
    [IO.Directory]::CreateDirectory((Split-Path -Parent $path)) | Out-Null
    [IO.File]::WriteAllText($path, $Content, [Text.UTF8Encoding]::new($false))
    return $path
}
function New-Fixture {
    Assert-True (Test-Path -LiteralPath $packageSource -PathType Leaf) 'Package.ps1 API has not been implemented.'
    $fixture = Join-Path ([IO.Path]::GetTempPath()) ('SNP package tests with spaces ' + [Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($fixture) | Out-Null
    $script:FixtureRoots.Add($fixture)
    Copy-Item -LiteralPath $packageSource -Destination (Join-Path $fixture 'Package.ps1')
    foreach ($relativePath in @(
        'Install.bat', 'Uninstall.bat', 'Setup.ps1',
        'Installer/Setup.Core.psm1', 'Installer/Bootstrap.psm1',
        'README.md', 'README.pt-BR.md', 'DESCRIPTION.txt',
        'RealServers/README.md', 'AutoCustomerDhcp/README.md', 'InfiniteTrolley/README.md',
        'RealServers/Assets/README.md', 'RealServers/Assets/runtime-files.json',
        'RealServers/bin/Release/netcoreapp6.0/RealServers.dll',
        'AutoCustomerDhcp/bin/Release/net6.0/AutoCustomerDhcp.dll',
        'InfiniteTrolley/bin/Release/net6.0/InfiniteTrolley.dll'
    )) { Write-FixtureFile $fixture $relativePath $relativePath | Out-Null }
    return $fixture
}
function Invoke-FixturePackage([string] $Fixture, [string] $OutputDirectory = '', [string] $Version = '1.2.3') {
    if (-not $OutputDirectory) { $OutputDirectory = Join-Path $Fixture 'output with spaces' }
    $result = @(& (Join-Path $Fixture 'Package.ps1') -SkipBuild -OutputDirectory $OutputDirectory -Version $Version)
    return @($result | Where-Object { $_.PSObject.Properties.Name -contains 'ZipPath' })[-1]
}
function Assert-PackageFailure([scriptblock] $Action, [string] $Pattern, [string] $OutputDirectory) {
    $caught = $null
    try { & $Action | Out-Null } catch { $caught = $_ }
    Assert-True ($null -ne $caught) 'Packaging unexpectedly succeeded.'
    Assert-True ($caught.Exception.Message -match $Pattern) "Unexpected error: $($caught.Exception.Message)"
    $archives = @(Get-ChildItem -LiteralPath $OutputDirectory -Filter '*.zip' -ErrorAction SilentlyContinue)
    Assert-True ($archives.Count -eq 0) 'A failed package left a publishable ZIP.'
}
function Get-ZipEntries([string] $ZipPath) {
    $archive = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    try { return @($archive.Entries | ForEach-Object FullName) } finally { $archive.Dispose() }
}
function Read-ZipText([string] $ZipPath, [string] $RelativePath) {
    $archive = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $entry = $archive.GetEntry($RelativePath)
        Assert-True ($null -ne $entry) "Missing ZIP entry: $RelativePath"
        $reader = [IO.StreamReader]::new($entry.Open())
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally { $archive.Dispose() }
}

Test-Case 'Release API exists before producing any package' {
    Assert-True (Test-Path -LiteralPath $packageSource -PathType Leaf) 'Package.ps1 API has not been implemented.'
}
Test-Case 'Ready-to-install archive contains launchers modules and three DLLs' {
    $fixture = New-Fixture
    $result = Invoke-FixturePackage $fixture
    Assert-True (Test-Path -LiteralPath $result.ZipPath -PathType Leaf) 'ZIP was not produced.'
    $entries = Get-ZipEntries $result.ZipPath
    foreach ($path in @('Install.bat', 'Uninstall.bat', 'Setup.ps1', 'Installer/Setup.Core.psm1', 'Installer/Bootstrap.psm1',
        'Payload/Mods/RealServers.dll', 'Payload/Mods/AutoCustomerDhcp.dll', 'Payload/Mods/InfiniteTrolley.dll', 'Payload/manifest.json')) {
        Assert-True ($entries -contains "SNP-RealServers-1.2.3/$path") "Missing installable file: $path"
    }
    Assert-True (@($entries | Where-Object { $_ -notlike 'SNP-RealServers-1.2.3/*' }).Count -eq 0) 'ZIP entries escaped its package root.'
}
Test-Case 'Manifest identifies each mod by path and exact SHA256' {
    $fixture = New-Fixture
    $result = Invoke-FixturePackage $fixture
    $manifest = Read-ZipText $result.ZipPath 'SNP-RealServers-1.2.3/Payload/manifest.json' | ConvertFrom-Json
    Assert-True ($manifest.format -eq 1 -and $manifest.version -eq '1.2.3' -and @($manifest.files).Count -eq 3) 'Manifest header/count is invalid.'
    foreach ($entry in $manifest.files) {
        $framework = if ($entry.mod -eq 'RealServers') { 'netcoreapp6.0' } else { 'net6.0' }
        $source = Join-Path $fixture "$($entry.mod)/bin/Release/$framework/$($entry.mod).dll"
        Assert-True ($entry.path -eq "Mods/$($entry.mod).dll") 'Manifest has an unexpected destination.'
        Assert-True ($entry.sha256 -eq (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()) 'Manifest hash does not match the shipped DLL.'
    }
}
Test-Case 'Exact allowlist excludes private assets dependency DLLs source secrets and logs' {
    $fixture = New-Fixture
    foreach ($path in @('private.glb', '.env', 'GameAssembly.dll', 'MelonLoader.zip', 'RealServers/Assets/private.glb',
        'RealServers/Assets/texture.png', 'RealServers/Backups/session.log', 'UserData/save.json',
        'Installer/Other.psm1', 'RealServers/bin/Release/netcoreapp6.0/UnityEngine.CoreModule.dll')) {
        Write-FixtureFile $fixture $path 'DO NOT SHIP' | Out-Null
    }
    $result = Invoke-FixturePackage $fixture
    $entries = Get-ZipEntries $result.ZipPath
    Assert-True (@($entries | Where-Object { $_ -match '\.glb$|\.png$|\.env$|GameAssembly|MelonLoader\.zip|session\.log|UserData|Other\.psm1|UnityEngine' }).Count -eq 0) 'Private or third-party files were included.'
    Assert-True (@($entries | Where-Object { $_ -match '\.dll$' }).Count -eq 3) 'Additional binaries were bundled.'
}
Test-Case 'English Portuguese and module documentation travel with the installer' {
    $fixture = New-Fixture
    $result = Invoke-FixturePackage $fixture
    $entries = Get-ZipEntries $result.ZipPath
    foreach ($path in @('README.md', 'README.pt-BR.md', 'RealServers/README.md', 'AutoCustomerDhcp/README.md',
        'InfiniteTrolley/README.md', 'RealServers/Assets/README.md', 'RealServers/Assets/runtime-files.json')) {
        Assert-True ($entries -contains "SNP-RealServers-1.2.3/$path") "Missing documentation: $path"
    }
}
Test-Case 'Missing mod DLL fails without creating a publishable ZIP' {
    $fixture = New-Fixture
    Remove-Item -LiteralPath (Join-Path $fixture 'InfiniteTrolley/bin/Release/net6.0/InfiniteTrolley.dll')
    $output = Join-Path $fixture 'failed-output'
    Assert-PackageFailure { Invoke-FixturePackage $fixture $output } 'DLL|InfiniteTrolley' $output
}
Test-Case 'Missing required installer module fails without a ZIP' {
    $fixture = New-Fixture
    Remove-Item -LiteralPath (Join-Path $fixture 'Installer/Bootstrap.psm1')
    $output = Join-Path $fixture 'failed-output'
    Assert-PackageFailure { Invoke-FixturePackage $fixture $output } 'Bootstrap|installer' $output
}
Test-Case 'Unsafe package version cannot create a path outside its output root' {
    $fixture = New-Fixture
    $output = Join-Path $fixture 'failed-output'
    Assert-PackageFailure { Invoke-FixturePackage $fixture $output '../escape' } 'Version|vers' $output
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $fixture 'escape.zip'))) 'Version escaped the output directory.'
}
Test-Case 'GameDir is required only when compilation is requested' {
    $fixture = New-Fixture
    $output = Join-Path $fixture 'failed-output'
    Assert-PackageFailure { & (Join-Path $fixture 'Package.ps1') -OutputDirectory $output -Version '1.2.3' } 'GameDir' $output
}
Test-Case 'Failed source build cannot be published as a complete release' {
    $fixture = New-Fixture
    Write-FixtureFile $fixture 'Build.ps1' "param([string]`$GameDir)`nthrow 'Intentional fixture build failure.'" | Out-Null
    $gameDir = Join-Path $fixture 'fake game'
    Write-FixtureFile $gameDir 'Data Center.exe' 'fixture' | Out-Null
    $output = Join-Path $fixture 'failed-output'
    Assert-PackageFailure { & (Join-Path $fixture 'Package.ps1') -GameDir $gameDir -OutputDirectory $output -Version '1.2.3' } 'build|compil' $output
}
Test-Case 'Same-version release is never silently overwritten' {
    $fixture = New-Fixture
    $result = Invoke-FixturePackage $fixture
    $before = (Get-FileHash -LiteralPath $result.ZipPath -Algorithm SHA256).Hash
    $caught = $false
    try { Invoke-FixturePackage $fixture | Out-Null } catch { $caught = $true }
    Assert-True $caught 'Existing release was silently replaced.'
    Assert-True ((Get-FileHash -LiteralPath $result.ZipPath -Algorithm SHA256).Hash -eq $before) 'Existing release bytes changed.'
}
Test-Case 'Identical input produces identical ZIP bytes in different output directories' {
    $fixture = New-Fixture
    $a = Invoke-FixturePackage $fixture (Join-Path $fixture 'output A')
    $b = Invoke-FixturePackage $fixture (Join-Path $fixture 'output B')
    Assert-True ((Get-FileHash -LiteralPath $a.ZipPath -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $b.ZipPath -Algorithm SHA256).Hash) 'Packaging depends on filesystem timestamps or random staging paths.'
}
Test-Case 'Default output directory is dist under the source root' {
    $fixture = New-Fixture
    $result = @(& (Join-Path $fixture 'Package.ps1') -SkipBuild -Version '1.2.3') | Where-Object { $_.PSObject.Properties.Name -contains 'ZipPath' } | Select-Object -Last 1
    Assert-True ($result.ZipPath -eq (Join-Path $fixture 'dist/SNP-RealServers-1.2.3.zip')) 'Default dist location is wrong.'
}
Test-Case 'Release version defaults to the RealServers DLL file version' {
    $fixture = New-Fixture
    $versionedAssembly = [object].Assembly.Location
    Copy-Item -LiteralPath $versionedAssembly -Destination (Join-Path $fixture 'RealServers/bin/Release/netcoreapp6.0/RealServers.dll') -Force
    $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($versionedAssembly).FileVersion
    Assert-True ($fileVersion -match '(\d+\.\d+\.\d+(?:\.\d+)?)') 'Fixture framework assembly has no file version.'
    $expected = $Matches[1] -replace '\.0$', ''
    $result = @(& (Join-Path $fixture 'Package.ps1') -SkipBuild) | Where-Object { $_.PSObject.Properties.Name -contains 'ZipPath' } | Select-Object -Last 1
    Assert-True ($result.Version -eq $expected) 'RealServers file version was not used.'
}

Write-Output "Package tests: $script:Passed/$($script:Passed + $script:Failed) passed. Fixtures stayed outside the repository and game."
if ($script:Failed -gt 0) { exit 1 }
