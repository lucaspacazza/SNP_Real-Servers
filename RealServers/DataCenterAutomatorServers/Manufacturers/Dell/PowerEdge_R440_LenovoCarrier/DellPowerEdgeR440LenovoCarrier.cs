namespace DataCenterAutomatorServers;

internal static class DellPowerEdgeR440LenovoCarrier
{
	// The chassis remains visually 1U, while the gameplay reservation is 3U.
	// It shares the proven Lenovo interaction path while the GLB is Dell-only.
	internal static readonly ServerVariantSpec Spec = new ServerVariantSpec(
		"dell_r440_lenovo_carrier",
		"Dell PowerEdge R440 5000 IOPS",
		"ShopItemSO_Server_Green1",
		4,
		"real_servers_dell_r440_lenovo",
		"Dell PowerEdge R440 5000 IOPS",
		5000,
		7000,
		0,
		"RJ45",
		25f,
		2,
		1,
		DirectFiberPort: true,
		RackUnits: 3,
		ModelAssetRelativePath: "RealServers\\Assets\\Manufacturers\\Dell\\PowerEdge_R440_LenovoCarrier\\Models\\PowerEdge_R440_static_game_ready.glb");
}
