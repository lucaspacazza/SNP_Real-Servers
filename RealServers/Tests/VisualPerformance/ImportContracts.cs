using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

internal static class ImportContracts
{
    public static int PerElementUploadCalls(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var type = metadata.TypeDefinitions.Select(metadata.GetTypeDefinition)
            .Single(t => metadata.GetString(t.Name) == "GltfMeshReader");
        var methods = type.GetMethods().Select(metadata.GetMethodDefinition)
            .Where(m => metadata.GetString(m.Name) == "ToIl2Cpp").ToArray();
        if (methods.Length != 3) throw new Exception("Expected all three typed upload helpers.");
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(op => op.Value);
        int calls = 0;
        foreach (var method in methods)
        {
            byte[] bytes = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
            for (int offset = 0; offset < bytes.Length;)
            {
                short opcode = bytes[offset++];
                if (opcode == 0xfe) opcode = (short)(0xfe00 | bytes[offset++]);
                var operand = opcodes[opcode].OperandType;
                if (operand == OperandType.InlineMethod)
                {
                    var handle = MetadataTokens.EntityHandle(BitConverter.ToInt32(bytes, offset));
                    if (handle.Kind == HandleKind.MemberReference
                        && metadata.GetString(metadata.GetMemberReference((MemberReferenceHandle)handle).Name) == "set_Item") calls++;
                }
                offset += operand switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, offset),
                    _ => 4
                };
            }
        }
        return calls;
    }

    public static int VectorConstructorCalls(string assemblyPath, string methodName)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var type = metadata.TypeDefinitions.Select(metadata.GetTypeDefinition)
            .Single(t => metadata.GetString(t.Name) == "GltfMeshReader");
        var method = type.GetMethods().Select(metadata.GetMethodDefinition)
            .Single(m => metadata.GetString(m.Name) == methodName);
        byte[] bytes = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(op => op.Value);
        int calls = 0;
        for (int offset = 0; offset < bytes.Length;)
        {
            short opcode = bytes[offset++];
            if (opcode == 0xfe) opcode = (short)(0xfe00 | bytes[offset++]);
            var operand = opcodes[opcode].OperandType;
            if (operand == OperandType.InlineMethod)
            {
                var handle = MetadataTokens.EntityHandle(BitConverter.ToInt32(bytes, offset));
                if (handle.Kind == HandleKind.MemberReference)
                {
                    var member = metadata.GetMemberReference((MemberReferenceHandle)handle);
                    if (metadata.GetString(member.Name) == ".ctor" && member.Parent.Kind == HandleKind.TypeReference)
                    {
                        var target = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
                        if (metadata.GetString(target.Namespace) == "UnityEngine"
                            && metadata.GetString(target.Name) is "Vector3" or "Vector2") calls++;
                    }
                }
            }
            offset += operand switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, offset),
                _ => 4
            };
        }
        return calls;
    }
}
