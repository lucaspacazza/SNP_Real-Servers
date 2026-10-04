using System;
using System.Runtime.CompilerServices;

namespace DataCenterAutomatorServers;

/// <summary>Managed reference identity with an explicit, live native pointer boundary.</summary>
internal static class NativeReferenceIdentity
{
    internal static bool AreSame(object? first, IntPtr firstPointer, object? second, IntPtr secondPointer)
    {
        if (ReferenceEquals(first, second)) return true;
        if (first == null || second == null) return false;
        // A collected/destroyed wrapper has no native identity. Distinct such
        // wrappers must never collapse into the same ownership key.
        return firstPointer != IntPtr.Zero && secondPointer != IntPtr.Zero && firstPointer == secondPointer;
    }

    internal static int Hash(object? instance, IntPtr pointer)
    {
        if (instance == null) return 0;
        return pointer != IntPtr.Zero ? pointer.GetHashCode() : RuntimeHelpers.GetHashCode(instance);
    }
}
