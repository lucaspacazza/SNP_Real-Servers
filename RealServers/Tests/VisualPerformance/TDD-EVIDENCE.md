# Runtime glTF batching verification

The loader previously created one GameObject, MeshFilter and MeshRenderer per
glTF primitive. The supplied Dell visuals contain 193 (R440), 388 (XE8640) and
368 (R760) primitives in one mesh node each. Markers are separate nodes.

## RED

`dotnet run --project RealServers/Tests/VisualPerformance/VisualPerformance.Tests.csproj -c Release`

The test project compiled its own test source and failed with CS0246 because the
new batching API did not exist. Its wildcard production includes do not require
missing input files. The checkpoint contains 21 expected behavior guarantees.

## GREEN

The same command passed **21/21** after adding the production helpers.

Checks cover positions, authored normals and UVs; original triangle winding;
material assignments and ordering; immutable input buffers; incompatible
attribute layouts; vertex budgets and the 65535/32-bit boundary; malformed
geometry; exact material identities including textures, alpha modes, extensions
and existing name-derived fallback colors; and opt-in material slot merging.

## Native integration

The loader creates one renderer per compatible mesh batch, retains each glTF
node and transform, sets submeshes and shared material slots, and releases CPU
mesh data after upload. Clones continue to share cached meshes/materials.
Identical material recipes are shared without color approximation. Opaque Dell
profiles can coalesce identical material slots; transparent profiles retain
slot order. Prefab cache identity includes opacity behavior.

The optional distance LOD remains a separate imported mesh with a 0.55 screen
height transition to limit full-detail rendering to close inspection. All
original GLB files and native colliders remain unchanged. Native outline uses
the existing `AddRenderer(renderer, null)` call.

Local generated Unity bindings contain `Mesh.set_subMeshCount`,
`Mesh.SetTriangles(Il2CppStructArray<int>, int, bool)` and
`Renderer.set_sharedMaterials`. Their wrappers use `il2cpp_runtime_invoke`.
The outline binding's second argument is an optional `OutlineTargetProvider`,
not a submesh index. No new `Mesh.CombineMeshes` call was introduced.

Unity documents [submesh triangle assignment](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Mesh.SetTriangles.html),
[submesh counts](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Mesh-subMeshCount.html),
and [shared material slots](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Renderer-sharedMaterials.html).

These are managed behavior tests and binding checks. Unity visual equivalence,
outline coverage, frame rate and live memory consumption still require checking
inside the game after loading the updated DLL. Reduced renderer counts do not
imply measured FPS improvement, and full-detail triangle counts are unchanged.
