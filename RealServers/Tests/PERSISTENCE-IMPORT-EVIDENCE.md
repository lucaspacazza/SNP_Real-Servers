# RealServers 0.2.5 regression evidence

The in-game reload failure revealed defects that managed tests had not covered. A valid model identity was being treated as proof that the physical model had already been configured. Unity resource scans also depended on incidental wrapper caching. Manual insertion bypassed the native coroutine's rack UID assignment, so mounted custom servers were serialized with `rackPositionUID = -1`.

## Verified contracts

- Resource results are explicitly converted to the requested IL2CPP type before their server or rack fields are read.
- Native pointer identity lets distinct managed wrappers retrieve the same server's mount and preparation state. Plain managed objects retain reference equality.
- Identity lookup and completed physical preparation are separate. Native insertion captures the serialized ID before duplicate-ID remapping.
- Custom placement uses `RackPosition.SetUID` and the `MainGameManager` counter before publishing the server's reservation. Free negative/zero occupancy entries are repaired without overwriting another server's positive UID.
- Snapshot preparation republishes the exact manually selected slot before native serialization. Schema 2 preserves explicit loose state; legacy unknown slots do not clear valid native references.
- Legacy migration requires a uniquely matching saved position and rotation and a free range. It does not select the nearest rack, an occupied slot, or an ambiguous match.
- Model import fills Vector3/Vector2 fields directly and copies native array spans in bulk. The actual DLL contains no per-attribute Unity vector constructor calls in those decoders and no per-element native array assignment in the three upload helpers.
- The LOD transition was corrected from 0.55 to 0.10. Runtime models and the available R440/R760 icons are included in the repository.

## RED to GREEN

| Target | Reproducer | Final result |
|---|---|---|
| RuntimeCompatibility | 28 passed / 7 failed for resource casts and physical preparation; later pre-remap/legacy guards also failed | 38/38 |
| Persistence | Schema migration initially 34 passed / 6 failed | 43/43 |
| ReferenceIdentity | 6 passed / 7 failed for same-pointer wrapper keys | 13/13 |
| RackPersistence | 0/17 before allocator; later 17 passed / 6 failed before exact pose recovery | 23/23 |
| VisualPerformance | 22/24 before constructor fix; 24/25 before bulk upload | 25/25 |
| ShopIcons | 0/6 before per-variant paths | 6/6 |
| SaveLifecycle | 21 passed / 5 failed before the active-save event guard | 26/26 |
| SaveHooks | Current installed API metadata checks | 21/21 |
| Performance | Shared rack index and idempotent hot paths | 19/19 |

All final results above were executed in Release configuration. The main source and public solution builds completed with zero warnings and errors. The public package also passed RuntimeCompatibility, Persistence, ReferenceIdentity, RackPersistence, VisualPerformance and ShopIcons checks after source mirroring.

The large mesh fixture retains 954,225 vertices and 2,862,675 vector values. Its managed processing time is not a measurement of Unity import time or frame rate. Managed/stub and metadata verification do not establish full native gameplay correctness or full native code coverage.

## Native evidence and remaining verification

Native logs for 0.2.4 confirmed new manual mounts received positive rack UIDs and completed native server registration. The corrected snapshot captured newly purchased models. An earlier empty autosave snapshot had already lost their predecessor identities; a local read-only NRBF comparison confirmed the older and newer worlds shared all original server IDs/prefabs/positions, rack anchors and object transform multisets. Only the verified missing model mappings were restored to the newer identity sidecar, with a backup. Native save bytes were preserved.

Full purchase latency, far-distance appearance, power/IP/cabling, and a successful save/reload of the mounted datacenter still require in-game confirmation. No claim of perfect native gameplay or measured FPS improvement is made.
