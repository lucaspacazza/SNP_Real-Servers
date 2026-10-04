using System.Reflection;

int passed = 0, failed = 0;
void Check(string name, Action action)
{
    try { action(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
void Equal(string? expected, string? actual)
{
    if (expected != actual) throw new Exception($"Expected '{expected}', got '{actual}'.");
}
string? Icon(string? id)
{
    var type = Assembly.GetExecutingAssembly().GetType("DataCenterAutomatorServers.ServerShopIcons")
        ?? throw new Exception("ServerShopIcons API is missing.");
    var method = type.GetMethod("GetRelativePath", BindingFlags.Public | BindingFlags.Static)
        ?? throw new Exception("GetRelativePath API is missing.");
    return (string?)method.Invoke(null, new object?[] { id });
}

Check("Active R440 uses its own icon", () => Equal("Assets/Manufacturers/Dell/PowerEdge_R440/icon.png", Icon("real_servers_dell_r440_lenovo")));
Check("R760 uses the newly supplied icon", () => Equal("Assets/Manufacturers/Dell/PowerEdge_R760/icon.png", Icon("real_servers_dell_r760")));
Check("Models without supplied icons keep the native fallback", () => {
    Equal(null, Icon("real_servers_dell_xe8640")); Equal(null, Icon("real_servers_lenovo_sr650"));
});
Check("Unsupported legacy and fake cards cannot borrow another model icon", () => {
    Equal(null, Icon("real_servers_dell_r440")); Equal(null, Icon("real_servers_hpe_dl380")); Equal(null, Icon("not-real_servers_dell_r760"));
});
Check("Missing identity has no custom icon", () => { Equal(null, Icon(null)); Equal(null, Icon("")); });
Check("Exact identities tolerate identifier casing", () => Equal("Assets/Manufacturers/Dell/PowerEdge_R760/icon.png", Icon("REAL_SERVERS_DELL_R760")));
Console.WriteLine($"Shop icons: {passed}/{passed + failed} passed.");
return failed == 0 ? 0 : 1;
