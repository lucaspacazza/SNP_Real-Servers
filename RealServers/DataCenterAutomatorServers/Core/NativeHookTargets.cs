using System;
using System.Reflection;

namespace DataCenterAutomatorServers;

// Harmony keys must use the declaration, not a method reflected through a
// derived IL2CPP wrapper. Aliases otherwise install two detours on one pointer.
internal static class NativeHookTargets
{
    internal static MethodInfo ResolveDeclared(MethodInfo method)
    {
        if (method == null) throw new ArgumentNullException(nameof(method));
        if (method.ReflectedType == method.DeclaringType) return method;
        foreach (MethodInfo candidate in method.DeclaringType.GetMethods(
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (candidate.MetadataToken == method.MetadataToken && candidate.Module == method.Module)
                return candidate;
        }
        throw new MissingMethodException(method.DeclaringType.FullName, method.Name);
    }
}
