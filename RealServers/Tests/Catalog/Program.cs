using DataCenterAutomatorServers;

int passed = 0, failed = 0;
void Check(string name, Action check)
{
    try { check(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception exception) { Console.WriteLine("FAIL " + name + ": " + exception.Message); failed++; }
}
void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

foreach (var spec in ServerVariantSpec.All)
{
    Check("Record copy preserves model, rack units and every other property for " + spec.VariantId, () =>
    {
        var clone = spec with { Price = spec.Price + 1 };
        Assert(clone.RackUnits == spec.RackUnits, $"Expected {spec.RackUnits}U; got {clone.RackUnits}U.");
        Assert(clone.ModelAssetRelativePath == spec.ModelAssetRelativePath, "Visual asset path was dropped.");
        Assert(clone == spec with { Price = clone.Price }, "Record values differ after copy.");
        Assert(clone.HeldObjectType == spec.HeldObjectType, "Held pose changed after copy.");
    });
    Check("Every catalog identity round-trips for " + spec.VariantId, () =>
    {
        Assert(ServerVariantSpec.FindById(spec.VariantId) == spec, "Canonical variant not found.");
        Assert(ServerVariantSpec.FindById(spec.VariantDisplayName) == spec, "Display name not found.");
        Assert(ServerVariantSpec.FindById(spec.VariantSuffix) == spec, "Legacy suffix not found.");
        foreach (var prefix in new[] { "real_servers_", "realservers_", "real_server_", "rs_" })
            Assert(ServerVariantSpec.FindById("  " + (prefix + spec.VariantSuffix).ToUpperInvariant().Replace('_', '-') + "___ ") == spec, "Legacy identity normalization changed.");
        Assert(ServerVariantSpec.FindByPurchase(spec.BaseItemId, spec.Price, "Server", spec.VariantDisplayName) == spec, "Shop variant lookup changed.");
        Assert(ServerVariantSpec.FindByPurchase(spec.BaseItemId, spec.Price, "Server") == null, "A stock item of equal price became a custom variant.");
        Assert(ServerVariantSpec.FindByPurchase(spec.PurchaseItemId, spec.Price, "Server") == spec, "Exact custom SKU identity changed.");
        Assert(!string.IsNullOrEmpty(spec.BaseRuntimeToken) && spec.IopsText == spec.Iops.ToString(System.Globalization.CultureInfo.InvariantCulture), "UI identity text changed.");
        Assert(spec.BaseRuntimeProcessingSpeed > 0 && spec.RuntimeProcessingSpeed > 0 && spec.RuntimeNetworkSpeed > 0, "Invalid runtime metadata.");
    });
    if (spec.UsesLenovoCarrierProfile)
    {
        Check("Copper carrier reports RJ45 in the UI for " + spec.VariantId, () =>
            Assert(spec.ConnectorHint == "RJ45", "UI promises fiber, but this profile uses native copper ports."));
    }
}
Check("Catalog identities are unique and sizes are supported", () =>
{
    Assert(ServerVariantSpec.All.Select(spec => spec.VariantId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == ServerVariantSpec.All.Length, "Duplicate variant identity.");
    Assert(ServerVariantSpec.All.All(spec => spec.RackUnits is >= 1 and <= 4), "Unsupported rack size.");
    Assert(ServerVariantSpec.FindById(null) == null && ServerVariantSpec.FindById(" ") == null && ServerVariantSpec.FindById("unknown") == null, "Unknown lookup not rejected.");
    Assert(ServerVariantSpec.CanonicalVariantId("unknown") == null && ServerVariantSpec.CanonicalVariantId(ServerVariantSpec.All[0].VariantDisplayName) == ServerVariantSpec.All[0].VariantId, "Canonical identity changed.");
    Assert(ServerVariantSpec.FindByPurchase(0, 7000, null) == null && ServerVariantSpec.FindByPurchase(0, 7000, "Switch") == null && ServerVariantSpec.FindByPurchase(-1, -1, "Server") == null, "A non-server purchase became a variant.");
    Assert(ServerVariantSpec.FindByIops(-1) == null && ServerVariantSpec.FindByIops(20000)?.VariantId == "real_servers_dell_xe8640", "Performance lookup changed.");
});
Check("Shop exposes exactly the four servers with their own runtime models", () =>
{
    var shopField = typeof(ServerCatalog).GetField("ShopVariants");
    Assert(shopField != null, "Shop still uses the complete legacy catalog.");
    var shop = (ServerVariantSpec[])shopField!.GetValue(null)!;
    var expected = new[] { "real_servers_dell_r440_lenovo", "real_servers_dell_xe8640", "real_servers_dell_r760", "real_servers_lenovo_sr650" };
    Assert(shop.Select(spec => spec.VariantId).OrderBy(id => id).SequenceEqual(expected.OrderBy(id => id)), "Shop contains unsupported servers or omits a finished model.");
    Assert(shop.All(spec => !string.IsNullOrWhiteSpace(spec.ModelAssetRelativePath)), "A shop offer has no custom runtime model.");
    Assert(ServerVariantSpec.All.Length == 10, "Removing shop offers must not remove saved inventory identities.");
    foreach (var legacy in ServerVariantSpec.All.Except(shop))
        Assert(ServerVariantSpec.FindById(legacy.VariantId) == legacy, "Existing legacy server can no longer load.");
});
Check("Shop cleanup recognizes only exact RealServers clone markers", () =>
{
    var method = typeof(ServerCatalog).GetMethod("FindOwnedShopVariant");
    Assert(method != null, "Old unsupported cards have no safe ownership lookup.");
    ServerVariantSpec? Find(string? guid, string? objectName, string? payloadName) =>
        (ServerVariantSpec?)method!.Invoke(null, new object?[] { guid, objectName, payloadName });
    foreach (var spec in ServerVariantSpec.All)
    {
        Assert(Find(spec.VariantId, "stock", "stock") == spec, "Exact generated GUID was missed.");
        Assert(Find(null, spec.VariantId + "_button", null) == spec, "Exact generated card name was missed.");
        Assert(Find(null, "stock", spec.VariantId + "_so") == spec, "Exact generated SO name was missed.");
        Assert(Find(null, spec.VariantId + "_button(Clone)", null) == spec, "Unity clone suffix was not normalized.");
        Assert(Find(null, spec.VariantDisplayName, null) == null, "A display name cannot prove ownership of a native or other mod card.");
        Assert(Find("prefix_" + spec.VariantId, null, null) == null, "Substring GUID claimed an unrelated item.");
    }
    Assert(Find(null, "Server.Green1", "ShopItemSO_ServerGreen1") == null && Find(null, null, null) == null, "Vanilla shop cards were claimed.");
});
Console.WriteLine($"Catalog regression: {passed}/{passed + failed} passed.");
return failed == 0 ? 0 : 1;
