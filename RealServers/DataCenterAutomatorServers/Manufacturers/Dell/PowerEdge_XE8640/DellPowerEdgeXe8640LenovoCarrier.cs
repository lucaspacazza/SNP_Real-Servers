namespace DataCenterAutomatorServers;

internal static class DellPowerEdgeXe8640LenovoCarrier
{
	// Copy the R440's proven purchase, persistence, rack and two-port interaction
	// flow, but clone the native Green2 large-server item and reserve a real 4U.
	internal static readonly ServerVariantSpec Spec = new ServerVariantSpec(
		"dell_xe8640_lenovo_carrier",
		"GPU 7U 12000 IOPS",
		"ShopItemSO_Server_Green2",
		5,
		"real_servers_dell_xe8640",
		"Dell PowerEdge XE8640 20000 IOPS",
		20000,
		15000,
		0,
		"RJ45",
		25f,
		2,
		1,
		DirectFiberPort: true,
		RackUnits: 4,
		ModelAssetRelativePath: "RealServers\\Assets\\Manufacturers\\Dell\\PowerEdge_XE8640\\Models\\PowerEdge_XE8640_runtime_compatible.glb");
}
