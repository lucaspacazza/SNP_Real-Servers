namespace DataCenterAutomatorServers;

internal static class SupermicroServerCatalog
{
	public static readonly ServerVariantSpec[] All = new ServerVariantSpec[1]
	{
		new ServerVariantSpec("supermicro_1024", "GPU 7U 12000 IOPS", "ShopItemSO_Server_Green2", 5, "real_servers_supermicro_1024", "Supermicro AS-1024US 500K IOPS", 500000, 118000, 0, "QSFP+", 40f, 3, 4, DirectFiberPort: true, RackUnits: 4)
	};
}
