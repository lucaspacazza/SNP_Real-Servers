[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $GameDir
)

$ErrorActionPreference = 'Stop'
$gameRoot = (Resolve-Path -LiteralPath $GameDir).Path
if (-not (Test-Path -LiteralPath (Join-Path $gameRoot 'Data Center.exe') -PathType Leaf)) {
    throw 'GameDir must point to the directory containing Data Center.exe.'
}

$solution = Join-Path $PSScriptRoot 'SNP.RealServers.sln'
& dotnet build $solution -c Release -v:minimal "-p:GameDir=$gameRoot"
if ($LASTEXITCODE -ne 0) { throw 'Build failed. No game files were installed or changed.' }
Write-Output 'Built RealServers, AutoCustomerDhcp and InfiniteTrolley. Use the individual installers to install them.'
