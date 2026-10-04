using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

namespace DataCenterAutomatorServers;

internal sealed record ServerVariantSpec(string FamilyKey, string BaseDisplayName, string BaseAssetName, int BaseItemId, string VariantId, string VariantDisplayName, int Iops, int Price, int XpToUnlock, string ConnectorHint, float NetworkSpeedGbps, int SfpType, int FiberLaneCount, bool DirectFiberPort, int RackUnits, string? ModelAssetRelativePath = null)
{
	public string IopsText => Iops.ToString(CultureInfo.InvariantCulture);

	public float RuntimeProcessingSpeed => (float)Iops / 100000f;

	public float RuntimeNetworkSpeed => NetworkSpeedGbps / 5f;

	public float BaseRuntimeProcessingSpeed
	{
		get
		{
			// Carrier speed belongs to the stock source item, not to the custom
			// server performance. Both current thin models clone a 5000-IOPS item.
			return BaseDisplayName.Contains("12000", StringComparison.OrdinalIgnoreCase) ? 0.12f : 0.05f;
		}
	}

	public string BaseRuntimeToken => BaseAssetName.Replace("ShopItemSO_", string.Empty, StringComparison.Ordinal);

	// Stable cart identity. Native ShopItemSO keeps BaseItemId for Start's
	// stock name/array lookups; only purchase arguments carry this SKU.
	public int PurchaseItemId => VariantId switch
	{
		"real_servers_dell_r440_lenovo" => 870440,
		"real_servers_dell_r440" => 870441,
		"real_servers_dell_xe8640" => 878640,
		"real_servers_dell_r650" => 870650,
		"real_servers_dell_r760" => 870760,
		"real_servers_cisco_c220" => 880220,
		"real_servers_cisco_c240" => 880240,
		"real_servers_hpe_dl360" => 890360,
		"real_servers_hpe_dl380" => 890380,
		"real_servers_lenovo_sr650" => 900630,
		"real_servers_supermicro_1024" => 910024,
		_ => throw new InvalidOperationException("No stable purchase SKU for " + VariantId)
	};

	public static ServerVariantSpec? FindByPurchaseItemId(int itemId) => All.FirstOrDefault(spec => spec.PurchaseItemId == itemId);

	public string SizeKey
	{
		get
		{
			if (Iops > 125000)
			{
				return "500k";
			}
			return "125k";
		}
	}

	public string VariantSuffix => FamilyKey + "_" + SizeKey;

	// These variants use the proven 1U carrier, rack and interaction path.
	// The Dell clone intentionally differs only in its imported visual asset.
	public bool UsesLenovoCarrierProfile => string.Equals(FamilyKey, "lenovo_sr650", StringComparison.OrdinalIgnoreCase)
		|| UsesDellStaticCarrierProfile;

	// Static Dell guide models share the R440's carrier-only rendering, exact
	// collider, rear-port and 180-degree orientation corrections. Keep this
	// separate from the Lenovo gameplay carrier so Dell-specific visuals never
	// leak into the Lenovo model.
	public bool UsesDellStaticCarrierProfile => string.Equals(FamilyKey, "dell_r440_lenovo_carrier", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(FamilyKey, "dell_xe8640_lenovo_carrier", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(FamilyKey, "dell_r760", StringComparison.OrdinalIgnoreCase);

	// The game only exposes Server1U, Server2U and Server3U as held-object
	// enum values. Rack occupancy itself uses sizeInU, so 4U hardware uses the
	// 3U held pose together with its explicit four-unit rack reservation.
	public int HeldObjectType => RackUnits switch
	{
		1 => 1,
		2 => 2,
		_ => 3
	};

	public static readonly ServerVariantSpec[] All = ServerCatalog.All;

	public static ServerVariantSpec? FindById(string? variantId)
	{
		if (string.IsNullOrWhiteSpace(variantId))
		{
			return null;
		}
		string normalized = NormalizeVariantToken(variantId);
		return All.FirstOrDefault((ServerVariantSpec spec) => VariantIdMatches(spec, normalized));
	}

	public static string? CanonicalVariantId(string? variantId)
	{
		return FindById(variantId)?.VariantId;
	}

	public static ServerVariantSpec? FindByIops(int iops)
	{
		return All.FirstOrDefault((ServerVariantSpec spec) => spec.Iops == iops);
	}

	public static ServerVariantSpec? FindByPurchase(int itemId, int price, object? itemType, string? displayName = null)
	{
		if (!(itemType?.ToString() ?? string.Empty).Contains("Server", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		ServerVariantSpec? exactSku = FindByPurchaseItemId(itemId);
		if (exactSku != null) return exactSku;
		string text = displayName ?? string.Empty;
		return All.FirstOrDefault(spec => spec.BaseItemId == itemId && spec.Price == price &&
			(text.Contains(spec.VariantDisplayName, StringComparison.OrdinalIgnoreCase) || text.Contains(spec.VariantId, StringComparison.OrdinalIgnoreCase)));
	}

	private static bool VariantIdMatches(ServerVariantSpec spec, string normalized)
	{
		string normalized2 = normalized;
		ServerVariantSpec spec2 = spec;
		string text = NormalizeVariantToken(spec2.VariantSuffix);
		if (normalized2 == NormalizeVariantToken(spec2.VariantId) || normalized2 == NormalizeVariantToken(spec2.VariantDisplayName) || normalized2 == text)
		{
			return true;
		}
		return new string[4] { "real_servers_", "realservers_", "real_server_", "rs_" }.Any((string prefix) => normalized2 == NormalizeVariantToken(prefix + spec2.VariantSuffix));
	}

	private static string NormalizeVariantToken(string text)
	{
		Span<char> span = stackalloc char[text.Length];
		int num = 0;
		bool flag = false;
		string text2 = text.Trim();
		foreach (char c in text2)
		{
			char c2 = (char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');
			if (c2 == '_')
			{
				if (flag)
				{
					continue;
				}
				flag = true;
			}
			else
			{
				flag = false;
			}
			span[num++] = c2;
		}
		while (num > 0 && span[num - 1] == '_')
		{
			num--;
		}
		return new string(span.Slice(0, num));
	}

	[CompilerGenerated]
	private ServerVariantSpec(ServerVariantSpec original)
	{
		FamilyKey = original.FamilyKey;
		BaseDisplayName = original.BaseDisplayName;
		BaseAssetName = original.BaseAssetName;
		BaseItemId = original.BaseItemId;
		VariantId = original.VariantId;
		VariantDisplayName = original.VariantDisplayName;
		Iops = original.Iops;
		Price = original.Price;
		XpToUnlock = original.XpToUnlock;
		ConnectorHint = original.ConnectorHint;
		NetworkSpeedGbps = original.NetworkSpeedGbps;
		SfpType = original.SfpType;
		FiberLaneCount = original.FiberLaneCount;
		DirectFiberPort = original.DirectFiberPort;
		RackUnits = original.RackUnits;
		ModelAssetRelativePath = original.ModelAssetRelativePath;
	}
}
