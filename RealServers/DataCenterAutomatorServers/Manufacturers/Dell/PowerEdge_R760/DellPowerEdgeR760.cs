namespace DataCenterAutomatorServers;

internal static class DellPowerEdgeR760
{
	internal static readonly ServerVariantSpec Spec = new ServerVariantSpec(
		"dell_r760",
		"System X 7U 12000 IOPS",
		"ShopItemSO_Server_Yellow2",
		1,
		"real_servers_dell_r760",
		"Dell PowerEdge R760 500K IOPS",
		500000,
		120000,
		0,
		"RJ45",
		40f,
		3,
		4,
		DirectFiberPort: true,
		RackUnits: 2,
		ModelAssetRelativePath: "RealServers\\Assets\\Manufacturers\\Dell\\PowerEdge_R760\\Models\\PowerEdge_R760_static_game_ready.glb");
}
