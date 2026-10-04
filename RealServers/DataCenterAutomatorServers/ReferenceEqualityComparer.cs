using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;

namespace DataCenterAutomatorServers;

internal sealed class ReferenceEqualityComparer : IEqualityComparer<object>
{
	public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

	private ReferenceEqualityComparer()
	{
	}

	public new bool Equals(object? x, object? y)
	{
		if (ReferenceEquals(x, y)) return true;
		return NativeReferenceIdentity.AreSame(x, GetNativePointer(x), y, GetNativePointer(y));
	}

	public int GetHashCode(object obj)
	{
		return NativeReferenceIdentity.Hash(obj, GetNativePointer(obj));
	}

	private static IntPtr GetNativePointer(object? value)
	{
		// Only actual interop wrappers carry native identity. Managed DTOs with
		// similarly named fields/properties retain ordinary reference semantics.
		if (value is not Il2CppObjectBase native) return IntPtr.Zero;
		try { return native.Pointer; }
		catch (ObjectCollectedException) { return IntPtr.Zero; }
	}
}
