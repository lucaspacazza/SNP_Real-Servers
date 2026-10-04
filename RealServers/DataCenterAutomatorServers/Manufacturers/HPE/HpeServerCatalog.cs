namespace DataCenterAutomatorServers;

internal static class HpeServerCatalog
{
	public static readonly ServerVariantSpec[] All = new ServerVariantSpec[2]
	{
		new ServerVariantSpec("hpe_dl360", "Mainframe 3U 5000 IOPs", "ShopItemSO_Server_Purple1", 6, "real_servers_hpe_dl360", "HPE ProLiant DL360 Gen11 125K IOPS", 125000, 25500, 0, "SFP28", 25f, 2, 1, DirectFiberPort: true, RackUnits: 1),
		new ServerVariantSpec("hpe_dl380", "Mainframe 7U 12000 IOPs", "ShopItemSO_Server_Purple2", 7, "real_servers_hpe_dl380", "HPE ProLiant DL380 Gen11 500K IOPS", 500000, 122000, 0, "QSFP+", 40f, 3, 4, DirectFiberPort: true, RackUnits: 2)
	};
}
