# SNP Real Servers

[English](README.md) | [Português (Brasil)](README.pt-BR.md)

**Build bigger datacenters with less busywork.**

SNP Real Servers is a collection of three modular MelonLoader mods for **Data Center by Waseku**. It brings real hardware profiles to your server catalog, assigns customer IP addresses automatically, and expands trolley capacity so large deployments take fewer trips.

Each mod is a separate DLL. Install the features you want, or use all three together.

## The mods

| Mod | What it does |
|---|---|
| **Real Servers** | Adds four Dell and Lenovo server entries, with individual performance, network profiles, prices, and rack sizes. Tracks each server's model identity across native saves and save revisions. |
| **Auto Customer DHCP** | When you select a customer using a server's native customer arrows, finds an available IP in a compatible customer range and applies it through the game's normal IP configuration. |
| **Infinite Trolley** | Adds logical cargo slots as the trolley fills up. Extra items share the original visual loading positions while remaining separate objects that you can unload through the normal controls. |

### Real Servers

The custom shop has four entries:

- Dell PowerEdge R440
- Dell PowerEdge R760
- Dell PowerEdge XE8640
- Lenovo rack server (shop label: ThinkSystem SR630 V3)

The catalog uses the game's native server interactions: rack installation, customer assignment, power, and network controls. Model identity follows durable server/object IDs rather than guessing from Green/Yellow colors or nearby rack positions. The shop cleanup in **0.2.2** removes the extra placeholder entries; the game's original server catalog remains available.

**This repository includes the four runtime server models, their four distance LOD models, and the available R440 and R760 shop icons.** Detailed chassis are shown nearby and lighter models are used at a distance. Blender projects, unrelated models, and personal development files are excluded. Additional server icons can be added as they become available.

### Auto Customer DHCP

Customer selection triggers assignment on the following frame, after the native UI has updated. The allocator preserves an existing valid address, avoids occupied addresses, and checks the appropriate customer/application/server type. If no compatible address is available, it reports the reason and keeps the previous IP.

Addresses are stored by the game's normal save system. This mod works independently and does not require the separate DHCP and IPAM mod.

### Infinite Trolley

The trolley grows on demand, with no fixed mod-imposed item count. Overlapping visual positions avoid spreading a large load across the world; collision handling keeps stacked cargo from pushing the trolley around. Available memory and game performance still determine practical capacity.

Cargo recovery uses objects restored by the native game. Servers in your hands, installed in racks, or belonging to another container are kept separate from trolley recovery.

## Requirements

- Windows x64 and a local installation of Data Center.
- MelonLoader **0.7.2** with its generated IL2CPP assemblies.
- .NET **6 SDK** to build the source.

The mods were developed against Unity **6000.4.12f1** and the game's installed IL2CPP API. Other game or loader versions may require adapting the hooks and references.

Game, Unity, MelonLoader, and generated interop DLLs are resolved from your own installation. They are not redistributed here.

## Build

Clone or download the repository, then pass your game directory:

```powershell
.\Build.ps1 -GameDir "C:\Program Files (x86)\Steam\steamapps\common\Data Center"
```

Or build the solution directly:

```powershell
dotnet build .\SNP.RealServers.sln -c Release '-p:GameDir=C:\Program Files (x86)\Steam\steamapps\common\Data Center'
```

For repeated builds, you can set `SNP_DATACENTER_DIR` to your game directory. An explicit `-p:GameDir=...` takes precedence. The build checks the local references and reports missing dependencies before compiling.

| Output | Location |
|---|---|
| RealServers.dll | `RealServers/bin/Release/netcoreapp6.0/` |
| AutoCustomerDhcp.dll | `AutoCustomerDhcp/bin/Release/net6.0/` |
| InfiniteTrolley.dll | `InfiniteTrolley/bin/Release/net6.0/` |

## Install

With the game closed, run the installers for the mods you want:

```powershell
$gameDir = "C:\Program Files (x86)\Steam\steamapps\common\Data Center"
.\RealServers\Install.ps1 -GameDir $gameDir
.\AutoCustomerDhcp\Install.ps1 -GameDir $gameDir
.\InfiniteTrolley\Install.ps1 -GameDir $gameDir
```

The scripts build against that installation, back up existing mod files, copy the mod into `Mods`, and verify the copied file hashes. They never close or start the game and do not modify saves. `-WhatIf` previews the installation without copying files. Restart the game yourself to load a new DLL.

Real Servers installs its DLL and the ten bundled runtime assets by default. The layout is described in [RealServers/Assets](RealServers/Assets/README.md). To use an alternative pack with that same layout, pass its root directory:

```powershell
.\RealServers\Install.ps1 -GameDir $gameDir -AssetSource "C:\MyAssetPacks\RealServers"
```

Only the ten runtime files listed in the asset manifest are selected. Source Blender projects and unrelated development files remain outside the public package.

## Save compatibility and removal

The game remains responsible for native saves. Real Servers also stores model identities under `UserData/RealServers/SaveIdentities/` in the game directory. **Keep that directory together with the native saves when backing up or moving a world.**

If an old save has already lost both the model identity and its matching record, the mod cannot safely reconstruct the original hardware from a stock Green/Yellow chassis alone.

To remove a mod, close the game and remove its DLL from `Mods`. Before removing Infinite Trolley, unload cargo that exceeds the base game's capacity. Preserve the Real Servers identity directory if you plan to use those worlds with the mod again.

## Development and verification

The source includes regression tests for allocation, trolley slots and recovery, physics policies, model identity, save transactions, catalog data, and hook compatibility.

Pure managed utility tests can run without the game:

```powershell
dotnet run --project .\AutoCustomerDhcp\Tests\Allocator.Tests.csproj -c Release
dotnet run --project .\InfiniteTrolley\Tests\Core\Core.Tests.csproj -c Release
```

Real Servers metadata checks need your installed assemblies. Set the runtime directory as well as the build property:

```powershell
$env:SNP_DATACENTER_DIR = $gameDir
dotnet run --project .\RealServers\Tests\SaveHooks\SaveHooks.Tests.csproj -c Release "-p:GameDir=$gameDir"
```

Managed and metadata tests do not replace playing through purchase, pickup, rack installation, cabling, and save/reload in Unity. Real Servers **0.2.5** keeps the four-entry shop, registers manual rack slots through the native UID mechanism, restores physical models independently of identity lookup, and avoids per-vertex native calls during model imports. Legacy rack recovery requires a unique exact saved pose and a free matching slot. Its complete native gameplay cycle still requires in-game verification.

## Repository layout

```text
RealServers/          Server catalog, identity persistence, optional visual loader
AutoCustomerDhcp/     Automatic customer IP allocation
InfiniteTrolley/      Expandable trolley slots, cargo physics and recovery
Build.ps1             Build all three mods
Directory.Build.props Shared local game-directory configuration
Directory.Build.targets Local assembly validation
```

Only the runtime assets listed in the manifest are included. Unrelated 3D assets, Blender projects, game/dependency binaries, compiled output, local backups, saves, crash dumps, and logs are excluded. Installed mods remain separate from this repository.

Data Center and the hardware brand names belong to their respective owners. This is an independent modding project and is not affiliated with Waseku or the hardware manufacturers.

[Repository](https://github.com/lucaspacazza/SNP_Real-Servers)
