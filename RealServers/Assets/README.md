# Runtime visual assets

This asset pack includes the four server visuals used by the shop: Dell
PowerEdge R440, PowerEdge R760, PowerEdge XE8640 and the Lenovo rack server
whose shop label is **ThinkSystem SR630 V3**.

`runtime-files.json` lists exactly **ten runtime files**: four main GLBs, their
four distance LOD GLBs, and two shop icons. The supplied icons belong to the
R440 and R760; the other models retain the game's shop icon until their own
icons are provided.

The runtime paths are grouped under `Manufacturers/<manufacturer>/<model>/`.
The installer copies the manifest's files to `Mods/RealServers/Assets` and
validates their hashes. Blender projects, 3DS source files, previews, debug
assets and unrelated models are outside this public pack.

The Lenovo asset path ends in
`ThinkSystem_SR650_V3/Models/lenovo_rack_sr250.glb`, while its shop label is
ThinkSystem SR630 V3. Those names differ; the filename alone does not verify
which Lenovo chassis the mesh represents. Keep the manifest's exact paths.

If a runtime GLB is missing, the visual loader retains the game's original
chassis and logs the missing file. The distance LODs preserve the model's
silhouette while reducing geometry shown from farther away.
