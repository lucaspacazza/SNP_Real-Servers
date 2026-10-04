namespace DataCenterAutomatorServers;

internal static class DellServerCatalog
{
	public static readonly ServerVariantSpec[] All = new ServerVariantSpec[4]
	{
		DellPowerEdgeR440LenovoCarrier.Spec,
		DellPowerEdgeXe8640LenovoCarrier.Spec,
		DellPowerEdgeR650.Spec,
		DellPowerEdgeR760.Spec
	};
}
