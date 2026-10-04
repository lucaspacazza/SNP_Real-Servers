namespace DataCenterAutomatorServers;

internal sealed record ServerVariantSpec(string VariantId, string BaseRuntimeToken)
{
    private static readonly ServerVariantSpec[] All = {
        new("dell_r440", "Server_Green1"), new("dell_xe8640", "Server_Green2"),
        new("dell_r760", "Server_Yellow2"), new("lenovo", "Server_Yellow1"), new("green-other", "Server_Green1")
    };
    public static ServerVariantSpec? FindById(string? id) => All.FirstOrDefault(x => string.Equals(x.VariantId, id, StringComparison.OrdinalIgnoreCase));
}

internal static class Log
{
    public static void Info(string message) { }
    public static void Warning(string message) { }
}
