using System;
using System.Linq;

namespace DataCenterAutomatorServers;

internal static class ServerCatalog
{
	public static readonly ServerVariantSpec[] All = DellServerCatalog.All
		.Concat(CiscoServerCatalog.All)
		.Concat(HpeServerCatalog.All)
		.Concat(LenovoServerCatalog.All)
		.Concat(SupermicroServerCatalog.All)
		.ToArray();

	// Preserve every historical identity for inventory/save recovery, but only
	// advertise hardware that has its own shipped runtime model in the shop.
	public static readonly ServerVariantSpec[] ShopVariants = All
		.Where(IsShopVariant)
		.ToArray();

	public static bool IsShopVariant(ServerVariantSpec spec) => !string.IsNullOrWhiteSpace(spec.ModelAssetRelativePath);

	public static ServerVariantSpec? FindOwnedShopVariant(string? guid, string? objectName, string? payloadName)
	{
		foreach (ServerVariantSpec spec in All)
		{
			if (string.Equals(guid, spec.VariantId, StringComparison.OrdinalIgnoreCase)
				|| IsGeneratedName(objectName, spec.VariantId)
				|| IsGeneratedName(payloadName, spec.VariantId))
				return spec;
		}
		return null;
	}

	private static bool IsGeneratedName(string? name, string variantId)
	{
		if (string.IsNullOrWhiteSpace(name)) return false;
		if (name.EndsWith("(Clone)", StringComparison.OrdinalIgnoreCase))
			name = name.Substring(0, name.Length - "(Clone)".Length);
		return string.Equals(name, variantId, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(name, variantId + "_button", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(name, variantId + "_so", StringComparison.OrdinalIgnoreCase);
	}
}
