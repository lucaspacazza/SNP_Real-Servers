$ErrorActionPreference = 'Stop'
$setupPath = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'Setup.ps1'
if (Test-Path -LiteralPath $setupPath) { . $setupPath }

# Test-only command seams. The real modules are imported by the entry point.
function Import-SnpSetupModules { param($PackageRoot) }
function Get-SnpModCatalog { }
function Get-SnpSelectedModIds { param($Selection) }
function Find-SnpGameDirectories { param($Path) }
function Resolve-SnpGameDirectory { param($Path) }
function Assert-SnpGameClosed { param($GameDir) }
function Assert-SnpPayload { param($PackageRoot, $ModIds) }
function Ensure-SnpMelonLoader { [CmdletBinding(SupportsShouldProcess=$true)] param($GameDir) }
function Install-SnpMods { [CmdletBinding(SupportsShouldProcess=$true)] param($GameDir, $PackageRoot, $ModIds) }
function Uninstall-SnpMods { [CmdletBinding(SupportsShouldProcess=$true)] param($GameDir, $ModIds) }
function Uninstall-SnpMelonLoader { [CmdletBinding(SupportsShouldProcess=$true)] param($GameDir) }

Describe 'Guided installer entry point' {
    BeforeEach {
        $script:callOrder = New-Object 'System.Collections.Generic.List[string]'
        Mock Import-SnpSetupModules { }
        Mock Get-SnpModCatalog { @(
            [pscustomobject]@{ Id='RealServers'; Name='Real Servers'; Dll='RealServers.dll' },
            [pscustomobject]@{ Id='AutoCustomerDhcp'; Name='Auto Customer DHCP'; Dll='AutoCustomerDhcp.dll' },
            [pscustomobject]@{ Id='InfiniteTrolley'; Name='Infinite Trolley'; Dll='InfiniteTrolley.dll' }
        ) }
        Mock Get-SnpSelectedModIds {
            switch ($Selection) {
                '0' { @() }
                '1' { @('RealServers') }
                '2' { @('AutoCustomerDhcp') }
                '1,3' { @('RealServers','InfiniteTrolley') }
                '4' { @('RealServers','AutoCustomerDhcp','InfiniteTrolley') }
                default { throw 'Invalid selection' }
            }
        }
        Mock Find-SnpGameDirectories { @('X:\Steam Library\steamapps\common\Data Center') }
        Mock Resolve-SnpGameDirectory { $Path }
        Mock Assert-SnpGameClosed { $script:callOrder.Add('closed') }
        Mock Assert-SnpPayload { $script:callOrder.Add('payload') }
        Mock Ensure-SnpMelonLoader { $script:callOrder.Add('loader'); [pscustomobject]@{ Installed=$true; Version='0.7.2'; FirstRunRequired=$true } }
        Mock Install-SnpMods { $script:callOrder.Add('mods') }
        Mock Uninstall-SnpMods { $script:callOrder.Add('remove-mods') }
        Mock Uninstall-SnpMelonLoader { $script:callOrder.Add('remove-loader') }
        Mock Write-Host { }
    }

    It 'Cancelling does not bootstrap or copy any files' {
        Invoke-SnpSetup -Action Install -GameDir 'X:\Steam Library' -Selection '0' -NonInteractive
        Assert-MockCalled Ensure-SnpMelonLoader -Times 0 -Exactly
        Assert-MockCalled Install-SnpMods -Times 0 -Exactly
    }
    It 'Noninteractive mode rejects missing selections without starting installation' {
        { Invoke-SnpSetup -Action Install -GameDir 'X:\Steam Library' -NonInteractive } | Should Throw
        Assert-MockCalled Ensure-SnpMelonLoader -Times 0 -Exactly
    }
    It 'Validates the payload before downloading the loader and installs only the selected mods' {
        Invoke-SnpSetup -Action Install -GameDir 'X:\Steam Library' -Selection '1,3' -NonInteractive
        ($script:callOrder -join ',') | Should Be 'closed,payload,loader,mods'
        Assert-MockCalled Install-SnpMods -Times 1 -Exactly -ParameterFilter {
            $GameDir -eq 'X:\Steam Library\steamapps\common\Data Center' -and
            ($ModIds -join ',') -eq 'RealServers,InfiniteTrolley'
        }
    }
    It 'A missing or invalid payload cannot install even the loader' {
        Mock Assert-SnpPayload { throw 'Payload is missing' }
        { Invoke-SnpSetup -Action Install -GameDir 'X:\Steam Library' -Selection '4' -NonInteractive } | Should Throw
        Assert-MockCalled Ensure-SnpMelonLoader -Times 0 -Exactly
        Assert-MockCalled Install-SnpMods -Times 0 -Exactly
    }
    It 'An open game blocks all mutations without closing it' {
        Mock Assert-SnpGameClosed { throw 'Game running' }
        { Invoke-SnpSetup -Action Install -GameDir 'X:\Steam Library' -Selection '4' -NonInteractive } | Should Throw
        Assert-MockCalled Ensure-SnpMelonLoader -Times 0 -Exactly
        Assert-MockCalled Install-SnpMods -Times 0 -Exactly
    }
    It 'Uninstalling one mod does not require the binary payload or remove MelonLoader' {
        Invoke-SnpSetup -Action Uninstall -GameDir 'X:\Steam Library' -Selection '2' -NonInteractive
        Assert-MockCalled Uninstall-SnpMods -Times 1 -Exactly -ParameterFilter { ($ModIds -join ',') -eq 'AutoCustomerDhcp' }
        Assert-MockCalled Assert-SnpPayload -Times 0 -Exactly
        Assert-MockCalled Ensure-SnpMelonLoader -Times 0 -Exactly
        Assert-MockCalled Uninstall-SnpMelonLoader -Times 0 -Exactly
    }
    It 'Explicit full uninstall removes mods before requesting owned loader removal' {
        Invoke-SnpSetup -Action Uninstall -GameDir 'X:\Steam Library' -Selection '4' -RemoveLoader -NonInteractive
        ($script:callOrder -join ',') | Should Be 'closed,remove-mods,remove-loader'
    }
    It 'WhatIf is propagated to the loader and every selected mod operation' {
        Invoke-SnpSetup -Action Install -GameDir 'X:\Steam Library' -Selection '4' -NonInteractive -WhatIf
        Assert-MockCalled Ensure-SnpMelonLoader -Times 1 -Exactly -ParameterFilter { $WhatIfPreference }
        Assert-MockCalled Install-SnpMods -Times 1 -Exactly -ParameterFilter { $WhatIfPreference }
    }
    It 'Several detected games require a specific path in noninteractive mode' {
        Mock Find-SnpGameDirectories { @('X:\First\Data Center','Y:\Second\Data Center') }
        { Invoke-SnpSetup -Action Install -GameDir 'X:\Steam' -Selection '1' -NonInteractive } | Should Throw
        Assert-MockCalled Install-SnpMods -Times 0 -Exactly
    }
    It 'The interactive game chooser can select a second Steam library' {
        Mock Find-SnpGameDirectories { @('X:\First\Data Center','Y:\Second\Data Center') }
        Mock Read-Host { '2' }
        Invoke-SnpSetup -Action Install -GameDir 'X:\Steam' -Selection '1'
        Assert-MockCalled Install-SnpMods -Times 1 -Exactly -ParameterFilter { $GameDir -eq 'Y:\Second\Data Center' }
    }
}
