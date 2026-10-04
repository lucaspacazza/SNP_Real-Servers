# Infinite Trolley

Move a whole deployment in one trip. Infinite Trolley expands the trolley in **Waseku Data Center** as you add cargo, making large datacenter builds less repetitive.

## How it works

The mod grows the trolley's logical inventory on demand and reuses its original visual positions. Objects overlap visually while remaining separate items with their own inventory slots. Load and unload them using the game's normal controls; removing an exposed object makes another overlapped object accessible.

- No fixed item-count cap imposed by the mod; memory and game performance determine the practical capacity.
- Cargo remains kinematic without gravity while loaded, and collisions with the trolley chassis are ignored to reduce unwanted forces.
- Pickup restores each object's recorded physics state.
- Save-load recovery works with objects restored by the game and excludes held, racked, and nested cargo from individual reattachment.

Capacity grows automatically. There are no separate configuration settings. RealServers and Auto Customer DHCP are independent companion mods.

## Requirements and installation

Requires Windows x64, Data Center, MelonLoader **0.7.2** with its IL2CPP assemblies initialized, and the **.NET 6 SDK** to build. Game and loader assemblies are read from your installation and are not included here.

From the repository root, with the game closed:

```powershell
.\InfiniteTrolley\Install.ps1 -GameDir 'C:\Path\To\Data Center'
```

The script builds Release, backs up an existing copy under this project's `Backups` directory, installs only `Mods/InfiniteTrolley.dll`, and checks its SHA256. It does not modify saves or other mods. `-WhatIf` previews the file installation; `-SkipBuild` uses an existing Release build.

Alternatively, build the project and copy its output DLL to the game's `Mods` directory yourself:

```powershell
dotnet build .\InfiniteTrolley\InfiniteTrolley.csproj -c Release '-p:GameDir=C:\Path\To\Data Center'
```

Start the game, load servers into the trolley, transport them, and unload through normal gameplay. Before uninstalling, empty cargo that exceeds the original trolley's capacity. Then close the game and remove `Mods/InfiniteTrolley.dll`.

## Tests

Run from the repository root:

```powershell
dotnet run --project .\InfiniteTrolley\Tests\Core\Core.Tests.csproj -c Release
dotnet run --project .\InfiniteTrolley\Tests\Runtime\Runtime.Tests.csproj -c Release
dotnet run --project .\InfiniteTrolley\Tests\Physics\Physics.Tests.csproj -c Release
dotnet run --project .\InfiniteTrolley\Tests\Lifecycle\Lifecycle.Tests.csproj -c Release
```

Tests use the mod's logic with simulated game objects. They cover inventory growth, insertion and removal of large cargo sets, slot reservations, physics restoration, and recovery eligibility. These checks run without the installed game; movement, pickup, and save-load behavior also need verification in the real Unity/IL2CPP runtime.
