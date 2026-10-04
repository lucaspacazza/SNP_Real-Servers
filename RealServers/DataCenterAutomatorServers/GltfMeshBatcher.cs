using System;
using System.Collections.Generic;

namespace DataCenterAutomatorServers;

// These buffers are independent of Unity. Batching only primitives belonging to
// the same glTF mesh keeps the original node transform and marker hierarchy.
internal sealed record GltfPrimitive<TVector3, TVector2>(
    TVector3[] Vertices, TVector3[]? Normals, TVector2[]? Uvs,
    int[] Indices, int MaterialIndex);

internal sealed record GltfSubmesh(int MaterialIndex, int[] Indices);

internal sealed record GltfMeshBatch<TVector3, TVector2>(
    TVector3[] Vertices, TVector3[]? Normals, TVector2[]? Uvs,
    GltfSubmesh[] Submeshes)
{
    public bool Uses32BitIndices => Vertices.Length > 65535;
}

internal static class GltfMeshBatcher
{
    public static IReadOnlyList<GltfMeshBatch<TVector3, TVector2>> Build<TVector3, TVector2>(
        IReadOnlyList<GltfPrimitive<TVector3, TVector2>> primitives,
        int maximumVertices = 1048576, bool combineEqualMaterialSlots = false)
    {
        if (maximumVertices < 1) throw new ArgumentOutOfRangeException(nameof(maximumVertices));
        // Reject malformed geometry before the caller allocates any native mesh.
        foreach (var primitive in primitives) Validate(primitive);
        var batches = new List<GltfMeshBatch<TVector3, TVector2>>();
        var pending = new List<GltfPrimitive<TVector3, TVector2>>();
        int vertexCount = 0;
        foreach (var primitive in primitives)
        {
            if (primitive.Vertices.Length == 0) continue;
            bool incompatible = pending.Count > 0 &&
                ((pending[0].Normals == null) != (primitive.Normals == null)
                || (pending[0].Uvs == null) != (primitive.Uvs == null));
            bool exceedsBudget = (long)vertexCount + primitive.Vertices.Length > maximumVertices;
            if (pending.Count > 0 && (incompatible || exceedsBudget))
            {
                batches.Add(Combine(pending, vertexCount, combineEqualMaterialSlots));
                pending.Clear();
                vertexCount = 0;
            }
            pending.Add(primitive);
            vertexCount = checked(vertexCount + primitive.Vertices.Length);
        }
        if (pending.Count > 0) batches.Add(Combine(pending, vertexCount, combineEqualMaterialSlots));
        return batches;
    }

    private static void Validate<TVector3, TVector2>(GltfPrimitive<TVector3, TVector2> primitive)
    {
        if ((primitive.Normals != null && primitive.Normals.Length != primitive.Vertices.Length)
            || (primitive.Uvs != null && primitive.Uvs.Length != primitive.Vertices.Length))
            throw new ArgumentException("glTF primitive attribute counts must match its vertices.");
        if (primitive.Indices.Length % 3 != 0)
            throw new ArgumentException("glTF triangle indices must contain complete triangles.");
        foreach (int index in primitive.Indices)
            if (index < 0 || index >= primitive.Vertices.Length)
                throw new ArgumentException("glTF triangle index lies outside its primitive vertices.");
    }

    private static GltfMeshBatch<TVector3, TVector2> Combine<TVector3, TVector2>(
        List<GltfPrimitive<TVector3, TVector2>> primitives, int vertexCount, bool combineEqualMaterialSlots)
    {
        var vertices = new TVector3[vertexCount];
        var normals = primitives[0].Normals == null ? null : new TVector3[vertexCount];
        var uvs = primitives[0].Uvs == null ? null : new TVector2[vertexCount];
        var materialOrder = new List<int>();
        var submeshIndices = new List<List<int>>();
        var knownMaterials = new Dictionary<int, int>();
        int offset = 0;
        foreach (var primitive in primitives)
        {
            Array.Copy(primitive.Vertices, 0, vertices, offset, primitive.Vertices.Length);
            if (normals != null) Array.Copy(primitive.Normals!, 0, normals, offset, primitive.Vertices.Length);
            if (uvs != null) Array.Copy(primitive.Uvs!, 0, uvs, offset, primitive.Vertices.Length);
            int slot;
            if (!combineEqualMaterialSlots || !knownMaterials.TryGetValue(primitive.MaterialIndex, out slot))
            {
                slot = materialOrder.Count;
                materialOrder.Add(primitive.MaterialIndex);
                submeshIndices.Add(new List<int>(primitive.Indices.Length));
                if (combineEqualMaterialSlots) knownMaterials.Add(primitive.MaterialIndex, slot);
            }
            var indices = submeshIndices[slot];
            foreach (int index in primitive.Indices) indices.Add(checked(index + offset));
            offset += primitive.Vertices.Length;
        }
        var submeshes = new GltfSubmesh[materialOrder.Count];
        for (int i = 0; i < submeshes.Length; i++)
            submeshes[i] = new GltfSubmesh(materialOrder[i], submeshIndices[i].ToArray());
        return new GltfMeshBatch<TVector3, TVector2>(vertices, normals, uvs, submeshes);
    }
}
