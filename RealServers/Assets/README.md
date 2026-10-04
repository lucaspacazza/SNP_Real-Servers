# External visual assets

The public repository contains code and an asset manifest. It does not include
3D models, Blender projects, textures, icons, previews, or other personal asset
files.

RealServers can run with the game's original server chassis. If a custom GLB is
missing, the visual loader logs a warning and retains the original chassis. The
four Dell/Lenovo catalog entries, server specifications, and variant identity
records are handled by the code. Installing this repository alone does not provide
their custom visual models.

To install your own compatible runtime asset pack, pass the folder containing its
`Assets` directory to the installer:

```powershell
.\RealServers\Install.ps1 -GameDir $env:SNP_DATACENTER_DIR -AssetSource 'C:\path\to\external\RealServers'
```

The installer copies only the nine paths listed in `runtime-files.json`: four
main GLBs, their four distance LOD GLBs, and one shop icon. It validates all files
before installing and does not recursively copy the source folder. Without
`-AssetSource`, it installs the DLL and leaves any existing visual assets in the
game folder in place.

The Lenovo shop label is **ThinkSystem SR630 V3**, while its configured asset path
ends in `ThinkSystem_SR650_V3/Models/lenovo_rack_sr250.glb`. These names differ;
the path does not verify which Lenovo chassis the mesh represents. Keep the
manifest's exact paths when supplying a compatible pack.

Asset paths are also declared by each server specification in
`../DataCenterAutomatorServers/Manufacturers/`. Use only assets you have permission
to use and redistribute. This manifest documents expected paths; it does not
grant rights to any asset.
