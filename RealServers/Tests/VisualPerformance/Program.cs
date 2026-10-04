using DataCenterAutomatorServers;
using System.Numerics;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Diagnostics;

int passed = 0, failed = 0;
void Check(string name, Action check)
{
    try { check(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception exception) { Console.WriteLine("FAIL " + name + ": " + exception.Message); failed++; }
}
void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new InvalidOperationException("Expected " + typeof(T).Name);
}
GltfPrimitive<Vector3, Vector2> Triangle(int material = 0, float x = 0, bool normals = true, bool uv = true)
{
    return new GltfPrimitive<Vector3, Vector2>(
        new[] { new Vector3(x, 0, 0), new Vector3(x + 1, 0, 0), new Vector3(x, 1, 0) },
        normals ? new[] { Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ } : null,
        uv ? new[] { Vector2.Zero, Vector2.UnitX, Vector2.UnitY } : null,
        new[] { 0, 2, 1 }, material);
}
string MaterialKey(string json)
{
    using var document = JsonDocument.Parse(json);
    return GltfMaterialIdentity.Build(document.RootElement);
}

Check("Hundreds of glTF materials share one renderer without dropping material assignments", () =>
{
    var primitives = Enumerable.Range(0, 388).Select(i => Triangle(i, i)).ToArray();
    var batches = GltfMeshBatcher.Build(primitives);
    Assert(batches.Count == 1, "XE8640-style geometry still requires hundreds of renderers.");
    Assert(batches[0].Submeshes.Length == 388, "Materials were collapsed or lost.");
    Assert(batches[0].Vertices.Length == 1164, "Geometry was dropped.");
    Assert(batches[0].Submeshes.Select(s => s.MaterialIndex).SequenceEqual(Enumerable.Range(0, 388)), "Material order changed.");
});
Check("Positions normals and UVs retain exact local values", () =>
{
    var a = Triangle(2, 10); var b = Triangle(5, -3);
    var batch = GltfMeshBatcher.Build(new[] { a, b })[0];
    Assert(batch.Vertices.SequenceEqual(a.Vertices.Concat(b.Vertices)), "Mesh node transforms would change.");
    Assert(batch.Normals!.SequenceEqual(a.Normals!.Concat(b.Normals!)), "Authored normals changed.");
    Assert(batch.Uvs!.SequenceEqual(a.Uvs!.Concat(b.Uvs!)), "UVs changed.");
});
Check("Indices are rebased once while retaining reflected triangle winding", () =>
{
    var batch = GltfMeshBatcher.Build(new[] { Triangle(1), Triangle(2) })[0];
    Assert(batch.Submeshes[0].Indices.SequenceEqual(new[] { 0, 2, 1 }), "First winding changed.");
    Assert(batch.Submeshes[1].Indices.SequenceEqual(new[] { 3, 5, 4 }), "Second primitive did not retain winding and offset.");
});
Check("Batch construction never mutates cached accessor buffers", () =>
{
    var primitive = Triangle(); var original = primitive.Indices.ToArray();
    var a = GltfMeshBatcher.Build(new[] { primitive, primitive })[0];
    var b = GltfMeshBatcher.Build(new[] { primitive })[0];
    Assert(primitive.Indices.SequenceEqual(original) && b.Submeshes[0].Indices.SequenceEqual(original), "Source indices mutated.");
    Assert(!ReferenceEquals(a.Vertices, primitive.Vertices), "Combined vertices alias source data.");
});
Check("Missing authored normals remain in an independent batch for native recalculation", () =>
{
    var batches = GltfMeshBatcher.Build(new[] { Triangle(normals: true), Triangle(normals: false), Triangle(normals: true) });
    Assert(batches.Count == 3, "Normal presence was mixed or material order changed.");
    Assert(batches[0].Normals != null && batches[1].Normals == null && batches[2].Normals != null, "Authored normals were overwritten.");
});
Check("Missing UVs do not erase textured primitive attributes", () =>
{
    var batches = GltfMeshBatcher.Build(new[] { Triangle(uv: true), Triangle(uv: false) });
    Assert(batches.Count == 2 && batches[0].Uvs != null && batches[1].Uvs == null, "Attribute layouts mixed.");
});
Check("Configured vertex budget splits batches without changing index addresses", () =>
{
    var batches = GltfMeshBatcher.Build(new[] { Triangle(1), Triangle(2), Triangle(3) }, maximumVertices: 6);
    Assert(batches.Count == 2 && batches[0].Vertices.Length == 6 && batches[1].Vertices.Length == 3, "Vertex cap ignored.");
    Assert(batches[1].Submeshes[0].Indices.SequenceEqual(new[] { 0, 2, 1 }), "New batch inherited old offset.");
});
Check("Single oversized primitive remains renderable in its own batch", () =>
{
    var batches = GltfMeshBatcher.Build(new[] { Triangle(1), Triangle(2) }, maximumVertices: 2);
    Assert(batches.Count == 2 && batches.All(b => b.Vertices.Length == 3), "Existing large primitive became invalid.");
});
Check("Meshes beyond 16-bit vertex range request 32-bit indices", () =>
{
    var batches = GltfMeshBatcher.Build(Enumerable.Range(0, 22000).Select(i => Triangle(i)).ToArray());
    Assert(batches.Count == 1 && batches[0].Uses32BitIndices, "65535 index boundary was not handled.");
    Assert(batches[0].Submeshes[^1].Indices.Max() == 65999, "High indices were truncated.");
});
Check("Exactly 65535 vertices still permit 16-bit indices", () =>
{
    var batch = GltfMeshBatcher.Build(Enumerable.Range(0, 21845).Select(i => Triangle(i)).ToArray())[0];
    Assert(batch.Vertices.Length == 65535 && !batch.Uses32BitIndices, "Boundary selection changed.");
});
Check("Empty primitive list creates no renderer", () => Assert(GltfMeshBatcher.Build(Array.Empty<GltfPrimitive<Vector3, Vector2>>()).Count == 0, "Empty geometry created a batch."));
Check("Invalid triangle index is rejected before any native mesh allocation", () =>
{
    var p = Triangle() with { Indices = new[] { 0, 2, 3 } };
    Throws<ArgumentException>(() => GltfMeshBatcher.Build(new[] { p }));
});
Check("Negative indices are rejected", () => Throws<ArgumentException>(() => GltfMeshBatcher.Build(new[] { Triangle() with { Indices = new[] { -1, 2, 1 } } })));
Check("Truncated triangle index arrays are rejected", () => Throws<ArgumentException>(() => GltfMeshBatcher.Build(new[] { Triangle() with { Indices = new[] { 0, 1 } } })));
Check("Inconsistent normals and UV lengths are rejected", () =>
{
    Throws<ArgumentException>(() => GltfMeshBatcher.Build(new[] { Triangle() with { Normals = new[] { Vector3.UnitZ } } }));
    Throws<ArgumentException>(() => GltfMeshBatcher.Build(new[] { Triangle() with { Uvs = new[] { Vector2.Zero } } }));
});
Check("Nonpositive vertex budget is rejected", () => Throws<ArgumentOutOfRangeException>(() => GltfMeshBatcher.Build(new[] { Triangle() }, maximumVertices: 0)));
Check("Exactly matching material settings share a signature despite different export names", () =>
{
    Assert(MaterialKey("{\"name\":\"Metal.001\",\"pbrMetallicRoughness\":{\"baseColorFactor\":[0.2,0.2,0.2,1],\"metallicFactor\":1}}")
        == MaterialKey("{\"name\":\"Other\",\"pbrMetallicRoughness\":{\"metallicFactor\":1,\"baseColorFactor\":[0.2,0.2,0.2,1]}}"), "Exporter labels/property order blocked exact deduplication.");
});
Check("Distinct material colors cannot be approximated or collapsed", () =>
{
    Assert(MaterialKey("{\"pbrMetallicRoughness\":{\"baseColorFactor\":[0.2,0.2,0.2,1]}}")
        != MaterialKey("{\"pbrMetallicRoughness\":{\"baseColorFactor\":[0.21,0.2,0.2,1]}}"), "Color approximation changed appearance.");
});
Check("Texture slots transparency and material extensions remain significant", () =>
{
    Assert(MaterialKey("{\"pbrMetallicRoughness\":{\"baseColorTexture\":{\"index\":0}}}")
        != MaterialKey("{\"pbrMetallicRoughness\":{\"baseColorTexture\":{\"index\":1}}}"), "Different textures merged.");
    Assert(MaterialKey("{\"alphaMode\":\"BLEND\"}") != MaterialKey("{\"alphaMode\":\"OPAQUE\"}"), "Transparency settings merged.");
    Assert(MaterialKey("{\"extensions\":{\"KHR_materials_unlit\":{}}}") != MaterialKey("{}"), "Extensions were discarded.");
});
Check("Existing name-derived fallback colors are retained in material identity", () =>
{
    Assert(MaterialKey("{\"name\":\"Metal\"}") != MaterialKey("{\"name\":\"Panel\"}"), "Legacy fallback colors became equal.");
    Assert(MaterialKey("{\"name\":\"Metal.001\"}") == MaterialKey("{\"name\":\"GreyTexture\"}"), "Identical fallback colors were not deduplicated.");
});
Check("Repeated material identity coalesces triangle slots only when explicitly requested", () =>
{
    var a = GltfMeshBatcher.Build(new[] { Triangle(7), Triangle(7) }, combineEqualMaterialSlots: true)[0];
    Assert(a.Submeshes.Length == 1 && a.Submeshes[0].Indices.SequenceEqual(new[] { 0, 2, 1, 3, 5, 4 }), "Exact material slot batching failed.");
    var b = GltfMeshBatcher.Build(new[] { Triangle(7), Triangle(7) })[0];
    Assert(b.Submeshes.Length == 2, "Default path changed material draw order.");
});
string modAssembly = args.Length > 0 ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "bin", "Release", "netcoreapp6.0", "RealServers.dll"));
Check("Position and normal decoding cannot invoke native Unity vector constructors per vertex", () =>
{
    int calls = ImportContracts.VectorConstructorCalls(modAssembly, "ReadVec3Accessor");
    Assert(calls == 0, $"Decoder retains {calls} native constructor call site(s): XE8640 positions+normals execute these 1,908,450 times.");
});
Check("UV decoding cannot invoke native Unity vector constructors per vertex", () =>
{
    int calls = ImportContracts.VectorConstructorCalls(modAssembly, "ReadVec2Accessor");
    Assert(calls == 0, $"Decoder retains {calls} native constructor call site(s): XE8640 UVs execute these 954,225 times.");
});
Check("All three typed upload helpers avoid per-element native array pointer lookups", () =>
{
    int calls = ImportContracts.PerElementUploadCalls(modAssembly);
    Assert(calls == 0, $"Upload helpers retain {calls} element setters, each recalculating the native span/pointer per value.");
});
Check("A full-size XE8640 byte-buffer fixture retains geometry and attributes in one batch", () =>
{
    const int vertices = 954225;
    var clock = Stopwatch.StartNew();
    var positionBytes = new byte[vertices * 12];
    BitConverter.GetBytes(12.5f).CopyTo(positionBytes, 0);
    BitConverter.GetBytes(-7.25f).CopyTo(positionBytes, positionBytes.Length - 4);
    var positions = MemoryMarshal.Cast<byte, Vector3>(positionBytes).ToArray();
    var normals = new Vector3[vertices]; normals[0] = Vector3.UnitZ; normals[^1] = Vector3.UnitY;
    var uvBytes = new byte[vertices * 8];
    BitConverter.GetBytes(0.25f).CopyTo(uvBytes, 0);
    BitConverter.GetBytes(0.75f).CopyTo(uvBytes, uvBytes.Length - 4);
    var uvs = MemoryMarshal.Cast<byte, Vector2>(uvBytes).ToArray();
    var batch = GltfMeshBatcher.Build(new[] { new GltfPrimitive<Vector3, Vector2>(positions, normals, uvs, new[] { 0, vertices - 1, 1 }, 3) })[0];
    Assert(batch.Vertices.Length == vertices && batch.Normals!.Length == vertices && batch.Uvs!.Length == vertices, "Large input lost attributes.");
    Assert(batch.Vertices[0].X == 12.5f && batch.Vertices[^1].Z == -7.25f, "Position bytes changed.");
    Assert(batch.Normals![0] == Vector3.UnitZ && batch.Normals[^1] == Vector3.UnitY, "Normal bytes changed.");
    Assert(batch.Uvs![0].X == 0.25f && batch.Uvs[^1].Y == 0.75f, "UV bytes changed.");
    Assert(batch.Uses32BitIndices && batch.Submeshes[0].Indices.SequenceEqual(new[] { 0, vertices - 1, 1 }), "High indices or winding changed.");
    Console.WriteLine($"GIANT FIXTURE {vertices:N0} vertices, {vertices * 3:N0} vector values retained, managed batch {clock.ElapsedMilliseconds} ms. Native import timing requires Unity.");
});

Console.WriteLine($"Visual batching: {passed}/{passed + failed} passed.");
return failed == 0 ? 0 : 1;
