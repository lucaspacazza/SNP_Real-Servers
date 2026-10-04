namespace DataCenterAutomatorServers;

internal static class CiscoServerCatalog
{
	public static readonly ServerVariantSpec[] All = new ServerVariantSpec[2]
	{
		new ServerVariantSpec("cisco_c220", "RISC 3U 5000 IOPS", "ShopItemSO_Server_Blue1", 2, "real_servers_cisco_c220", "Cisco UCS C220 M7 125K IOPS", 125000, 26000, 0, "SFP28", 25f, 2, 1, DirectFiberPort: true, RackUnits: 1),
		new ServerVariantSpec("cisco_c240", "RISC 7U 12000 IOPS", "ShopItemSO_Server_Blue2", 3, "real_servers_cisco_c240", "Cisco UCS C240 M7 500K IOPS", 500000, 125000, 0, "QSFP+", 40f, 3, 4, DirectFiberPort: true, RackUnits: 2)
	};
}
