# Real Servers 0.2.2

A MelonLoader mod for **Data Center by Waseku** that adds four Dell and Lenovo server entries to the shop:

- Dell PowerEdge R440
- Dell PowerEdge R760
- Dell PowerEdge XE8640
- Lenovo rack server (shop label: ThinkSystem SR630 V3)

Each entry has its own gameplay specifications, price, and rack reservation. The mod uses the game's native purchase, pickup, rack, power, and network interactions. Version 0.2.2 removes the extra placeholder entries from the custom shop. Historical variant definitions remain available internally to restore existing saves; they are not offered as new purchases.

Model identity is stored separately for each native save and its revisions, using durable server and loose-object IDs. Back up `UserData/RealServers/SaveIdentities/` in the game directory together with your native saves. A stock chassis alone cannot identify a model whose saved identity has already been lost.

## Build and install

The source requires Windows x64, .NET 6 SDK, MelonLoader 0.7.2, and the generated IL2CPP assemblies from your own game installation. From the repository root:

```powershell
$gameDir = "C:\Program Files (x86)\Steam\steamapps\common\Data Center"
dotnet build .\RealServers\RealServers.csproj -c Release "-p:GameDir=$gameDir"
.\RealServers\Install.ps1 -GameDir $gameDir -SkipBuild
```

The installer backs up replaced mod files and verifies their hashes. It does not close or start the game or modify saves. A new DLL loads on the next game start.

## Optional visuals

**The public source does not include 3D models, textures, or icons.** Without a separate compatible asset pack, servers use the game's original chassis with the mod's specifications and identity tracking. Existing installed visual assets are preserved by a DLL-only installation.

See [Assets/README.md](Assets/README.md) for the expected file layout and the installer's `-AssetSource` option.

## Verification

Regression tests cover catalog selection, purchase routing, identity persistence, save handling, and compatibility with installed game APIs. Managed and metadata checks do not execute Unity gameplay; purchase, pickup, rack placement, cabling, and save/reload still need in-game verification.

See the [main README](../README.md) for all three mods, shared build instructions, and test commands.
