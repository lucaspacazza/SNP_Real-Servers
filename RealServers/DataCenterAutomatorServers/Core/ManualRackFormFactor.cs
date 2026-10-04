namespace DataCenterAutomatorServers;

internal static class ManualRackFormFactor
{
	public static void ApplyToServer(object server, ServerVariantSpec? spec)
	{
		if (server == null || spec == null)
		{
			return;
		}
		// This is the physical contract owned by the pack. No stock server or
		// switch transform is read here: the rack reservation is always the exact
		// RackUnits declared by the manufacturer catalog.
		ReflectionUtil.WriteMember(server, spec.RackUnits, "sizeInU", "SizeInU");
		ReflectionUtil.WriteMember(server, spec.HeldObjectType, "objectInHandType", "ObjectInHandType");
	}
}
