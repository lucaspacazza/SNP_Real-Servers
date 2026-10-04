using System;

namespace DataCenterAutomatorServers;

internal static class ServerShopIcons
{
    public static string? GetRelativePath(string? variantId)
    {
        if (string.Equals(variantId, "real_servers_dell_r440_lenovo", StringComparison.OrdinalIgnoreCase))
            return "Assets/Manufacturers/Dell/PowerEdge_R440/icon.png";
        if (string.Equals(variantId, "real_servers_dell_r760", StringComparison.OrdinalIgnoreCase))
            return "Assets/Manufacturers/Dell/PowerEdge_R760/icon.png";
        return null;
    }
}
