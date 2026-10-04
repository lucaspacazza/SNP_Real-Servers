using System;

namespace System.Runtime.CompilerServices;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Interface | AttributeTargets.Delegate, Inherited = false)]
internal sealed class NullableContextAttribute : Attribute
{
	public NullableContextAttribute(byte flag)
	{
	}
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event | AttributeTargets.Parameter | AttributeTargets.ReturnValue | AttributeTargets.GenericParameter, Inherited = false)]
internal sealed class NullableAttribute : Attribute
{
	public NullableAttribute(byte flag)
	{
	}

	public NullableAttribute(byte[] flags)
	{
	}
}
