# Auto Customer DHCP

Choose a customer and let the server find an available IP. Auto Customer DHCP removes repetitive address entry when deploying large numbers of servers in **Waseku Data Center**.

## How it works

Use the customer arrows in the game's server panel. On the following frame, the mod selects a free address from that customer's application pools, matching the server type.

- Keeps the current IP when it is valid, compatible, and available.
- Skips occupied addresses and respects the game's duplicate and reservation checks.
- Prefers the current application; otherwise, selects a compatible application with an available address.
- Leaves the previous configuration intact and displays a reason when no compatible address is available.
- Uses the game's normal server configuration, so assigned IPs are stored in the regular game save.

The mod works automatically and has no separate configuration file. Save your game after making changes. Customer selection in another mod's custom editor does not trigger this mod's native arrow hook; cabling, power, VLANs, and routing remain part of normal gameplay.

## Requirements and installation

Requires Windows x64, Data Center, MelonLoader **0.7.2** with its IL2CPP assemblies initialized, and the **.NET 6 SDK** to build. Game and loader assemblies are read from your installation and are not included here.

From the repository root, with the game closed:

```powershell
.\AutoCustomerDhcp\Install.ps1 -GameDir 'C:\Path\To\Data Center'
```

The script builds Release, backs up an existing copy under this project's `Backups` directory, installs only `Mods/AutoCustomerDhcp.dll`, and checks its SHA256. It does not modify saves or other mods. `-WhatIf` previews the file installation; `-SkipBuild` uses an existing Release build.

Alternatively, build the project and copy its output DLL to the game's `Mods` directory yourself:

```powershell
dotnet build .\AutoCustomerDhcp\AutoCustomerDhcp.csproj -c Release '-p:GameDir=C:\Path\To\Data Center'
```

Start the game and choose a customer through the server panel's arrows. A DHCP notification reports the assigned address or explains why allocation was unavailable. Remove `Mods/AutoCustomerDhcp.dll` with the game closed to uninstall.

**DHCP and IPAM is not required.** The mod can coexist with it by applying an explicit address through the game's normal `Server.SetIP` method. Auto Customer DHCP is also independent of RealServers and Infinite Trolley.

## Tests

Run from the repository root:

```powershell
dotnet run --project .\AutoCustomerDhcp\Tests\Allocator.Tests.csproj -c Release
dotnet run --project .\AutoCustomerDhcp\Tests\Runtime\Runtime.Tests.csproj -c Release
```

These tests cover address selection and the runtime adapter using simulated game objects. They run without the installed game and do not replace verification in Unity/IL2CPP.
