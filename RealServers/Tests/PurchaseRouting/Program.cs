using DataCenterAutomatorServers;
using System.Reflection;

int passed = 0, failed = 0;
void Test(string name, Action run) { try { run(); passed++; Console.WriteLine("PASS " + name); } catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.Message); } }
void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
var dell = ServerVariantSpec.FindById("real_servers_dell_r440_lenovo")!;
var lenovo = ServerVariantSpec.FindById("real_servers_lenovo_sr650")!;
Test("Every current variant has a distinct stable numeric SKU outside native IDs", () => {
    Equal(ServerVariantSpec.All.Length, ServerVariantSpec.All.Select(s => s.PurchaseItemId).Distinct().Count());
    foreach (var spec in ServerVariantSpec.All) { Equal(true, spec.PurchaseItemId > 100000); Equal(spec, ServerVariantSpec.FindByPurchaseItemId(spec.PurchaseItemId)); }
    Equal(870440, dell.PurchaseItemId); Equal(900630, lenovo.PurchaseItemId);
    Equal<ServerVariantSpec?>(null, ServerVariantSpec.FindByPurchaseItemId(dell.BaseItemId));
});
Test("Named legacy purchase becomes its SKU and stock of equal price stays stock", () => {
    Equal(dell, PurchaseRoutingPolicy.ResolvePurchase(dell.BaseItemId, dell.Price, "Server1U", dell.VariantDisplayName));
    Equal<ServerVariantSpec?>(null, PurchaseRoutingPolicy.ResolvePurchase(dell.BaseItemId, dell.Price, "Server1U", "Green server"));
    Equal<ServerVariantSpec?>(null, PurchaseRoutingPolicy.ResolvePurchase(dell.BaseItemId, dell.Price, "Server1U", null));
    Equal<ServerVariantSpec?>(null, ServerVariantSpec.FindByPurchase(dell.BaseItemId, dell.Price, "Server1U"));
});
Test("A deferred SKU survives a translated or stock-looking display name", () => {
    Equal(dell, PurchaseRoutingPolicy.ResolvePurchase(dell.PurchaseItemId, dell.Price, "Server1U", "Servidor verde"));
    Equal(lenovo, PurchaseRoutingPolicy.ResolvePurchase(lenovo.PurchaseItemId, lenovo.Price, "Server3U", ""));
    Equal<ServerVariantSpec?>(null, PurchaseRoutingPolicy.ResolvePurchase(dell.PurchaseItemId, dell.Price, "Switch", dell.VariantDisplayName));
});
Test("Prefab routing changes only the native lookup argument", () => {
    int cartSku = dell.PurchaseItemId, nativeArgument = cartSku;
    Equal(dell, PurchaseRoutingPolicy.RoutePrefab(ref nativeArgument, "Server1U"));
    Equal(dell.BaseItemId, nativeArgument); Equal(dell.PurchaseItemId, cartSku);
    int stock = dell.BaseItemId; Equal<ServerVariantSpec?>(null, PurchaseRoutingPolicy.RoutePrefab(ref stock, "Server1U")); Equal(dell.BaseItemId, stock);
});
Test("Unknown or nonserver SKU cannot change native prefab arguments", () => {
    int unknown = 999999; Equal<ServerVariantSpec?>(null, PurchaseRoutingPolicy.RoutePrefab(ref unknown, "Server1U")); Equal(999999, unknown);
    int wrongType = dell.PurchaseItemId; Equal<ServerVariantSpec?>(null, PurchaseRoutingPolicy.RoutePrefab(ref wrongType, "Rack")); Equal(dell.PurchaseItemId, wrongType);
});
Test("Preloading every checkout prefab preserves all distinct SKU routes", () => {
    var routes = new PurchaseRoutingContext();
    routes.SetPrefab(1, 100, dell); Equal(dell, routes.GetSpec(1, 100));
    routes.SetPrefab(1, 200, lenovo); Equal(lenovo, routes.GetSpec(1, 200)); Equal(dell, routes.GetSpec(1, 100));
    routes.SetPrefab(1, 200, null); Equal<ServerVariantSpec?>(null, routes.GetSpec(1, 200));
    Equal(dell, routes.GetSpec(1, 100));
    routes.SetPrefab(1, 200, lenovo); routes.SetPrefab(2, 100, lenovo);
    routes.EndCheckout(1); Equal<ServerVariantSpec?>(null, routes.GetSpec(1, 100)); Equal<ServerVariantSpec?>(null, routes.GetSpec(1, 200));
    Equal(lenovo, routes.GetSpec(2, 100));
});
Test("The exact prefab route still requires its native price and server type", () => {
    Equal(true, PurchaseRoutingPolicy.CanBeginPhysicalSpawn(dell, dell.Price, "Server1U"));
    Equal(false, PurchaseRoutingPolicy.CanBeginPhysicalSpawn(dell, dell.Price + 1, "Server1U"));
    Equal(false, PurchaseRoutingPolicy.CanBeginPhysicalSpawn(dell, dell.Price, "Rack"));
    Equal(false, PurchaseRoutingPolicy.CanBeginPhysicalSpawn(null, dell.Price, "Server1U"));
    Equal(false, PurchaseRoutingPolicy.CanBeginPhysicalSpawn(dell, dell.Price, null));
});
Test("Context cannot leak into another shop or mismatching prefab", () => {
    var routes = new PurchaseRoutingContext(); routes.SetPrefab(1, 100, dell);
    Equal<ServerVariantSpec?>(null, routes.GetSpec(2, 100)); Equal<ServerVariantSpec?>(null, routes.GetSpec(1, 200));
    routes.EndCheckout(1); Equal<ServerVariantSpec?>(null, routes.GetSpec(1, 100));
});
Test("Repeated quantity can reuse exact checkout route until stock lookup clears it", () => {
    var routes = new PurchaseRoutingContext(); routes.SetPrefab(1, 100, dell);
    for (int quantity = 0; quantity < 100; quantity++) Equal(dell, routes.GetSpec(1, 100));
    routes.SetPrefab(1, 100, null); Equal<ServerVariantSpec?>(null, routes.GetSpec(1, 100));
});
Test("Nullable failure recovers only the one newly created spawn", () => {
    var before = new Dictionary<int, int> { [7] = 700 }; var after = new Dictionary<int, int> { [7] = 700, [8] = 800 };
    Equal<int?>(8, PurchaseRoutingPolicy.SelectSpawnUid(before, after, int.MinValue));
    Equal<int?>(null, PurchaseRoutingPolicy.SelectSpawnUid(before, before, int.MinValue));
});
Test("Existing returned UID never associates old stock", () => {
    var before = new Dictionary<int, int> { [7] = 700 }; var after = new Dictionary<int, int> { [7] = 700, [8] = 800 };
    Equal<int?>(null, PurchaseRoutingPolicy.SelectSpawnUid(before, after, 7));
});
Test("Recycled UID requires a new Unity instance", () => {
    var before = new Dictionary<int, int> { [7] = 700 }; var after = new Dictionary<int, int> { [7] = 701 };
    Equal<int?>(7, PurchaseRoutingPolicy.SelectSpawnUid(before, after, 7));
});
Test("Ambiguous spawn delta is rejected unless native UID names the new object", () => {
    var before = new Dictionary<int, int> { [7] = 700 }; var after = new Dictionary<int, int> { [7] = 700, [8] = 800, [9] = 900 };
    Equal<int?>(null, PurchaseRoutingPolicy.SelectSpawnUid(before, after, int.MinValue));
    Equal<int?>(9, PurchaseRoutingPolicy.SelectSpawnUid(before, after, 9));
});
Test("Null zero and boxed managed values are decoded without inventing a UID", () => {
    Equal(int.MinValue, PurchaseRoutingPolicy.ReadSpawnUid(null)); Equal(0, PurchaseRoutingPolicy.ReadSpawnUid(0)); Equal(42, PurchaseRoutingPolicy.ReadSpawnUid((int?)42));
    Equal(int.MinValue, PurchaseRoutingPolicy.ReadSpawnUid(new object()));
    Equal(44, PurchaseRoutingPolicy.ReadSpawnUid(new NullableFields { hasValue = true, value = 44 }));
    Equal(int.MinValue, PurchaseRoutingPolicy.ReadSpawnUid(new NullableFields { hasValue = false, value = 44 }));
});
Test("Current interop exposes typed nullable fields and deferred checkout APIs", () => {
    Type shop = typeof(Il2Cpp.ComputerShop), nullable = typeof(Il2CppSystem.Nullable<int>);
    Equal(nullable, shop.GetMethod("SpawnPhysicalItem")!.ReturnType);
    Equal(false, nullable.IsValueType); Equal(typeof(bool), nullable.GetProperty("hasValue")!.PropertyType); Equal(typeof(int), nullable.GetProperty("value")!.PropertyType);
    Equal(true, shop.GetMethod("SpawnAllPurchasedItems") != null); Equal(true, shop.GetMethod("AddNewCartItem") != null);
    Equal(true, shop.GetProperty("spawnedItems") != null); Equal(typeof(int), typeof(Il2Cpp.ShopCartItem).GetProperty("ItemID")!.PropertyType);
});
Test("Malformed nullable shapes and absent values fail closed", () => {
    Equal(55, PurchaseRoutingPolicy.ReadSpawnUid(new NullableProperties { HasValue = true, Value = 55 }));
    Equal(int.MinValue, PurchaseRoutingPolicy.ReadSpawnUid(new NullableProperties { HasValue = false, Value = 55 }));
    Equal(int.MinValue, PurchaseRoutingPolicy.ReadSpawnUid(new MissingNullableValue()));
    Equal(int.MinValue, PurchaseRoutingPolicy.ReadSpawnUid(new InvalidNullableValue()));
    Equal(int.MinValue, PurchaseRoutingPolicy.ReadSpawnUid(new ThrowingNullableValue()));
});
Test("Scope reset removes all checkout routes without borrowing prototypes", () => {
    var routes = new PurchaseRoutingContext(); routes.SetPrefab(1, 100, dell); routes.SetPrefab(2, 200, lenovo);
    routes.Clear(); Equal<ServerVariantSpec?>(null, routes.GetSpec(1, 100)); Equal<ServerVariantSpec?>(null, routes.GetSpec(2, 200));
    routes.SetPrefab(0, 100, dell); routes.SetPrefab(1, 0, dell); Equal<ServerVariantSpec?>(null, routes.GetSpec(0, 100)); Equal<ServerVariantSpec?>(null, routes.GetSpec(1, 0));
});
Test("Spawn state owns its immutable before-capture and rejects absent instances", () => {
    var before = new Dictionary<int, int> { [7] = 700 }; var state = new PurchaseSpawnState(1, dell, before);
    before.Clear(); Equal(700, state.Before[7]); Equal(1, state.ShopInstanceId); Equal(dell, state.Spec);
    Equal<int?>(null, PurchaseRoutingPolicy.SelectSpawnUid(state.Before, new Dictionary<int, int> { [8] = 0 }, int.MinValue));
    Equal<int?>(null, PurchaseRoutingPolicy.SelectSpawnUid(state.Before, new Dictionary<int, int> { [-8] = 800 }, -8));
});
Test("Checkout batch finds every new UID and excludes unchanged stock", () => {
    var before = new Dictionary<int, int> { [1] = 100, [2] = 200 };
    var after = new Dictionary<int, int> { [1] = 100, [2] = 200, [3] = 300, [4] = 400 };
    Equal("3,4", string.Join(",", PurchaseRoutingPolicy.SelectNewSpawnUids(before, after).OrderBy(id => id)));
    Equal(0, PurchaseRoutingPolicy.SelectNewSpawnUids(before, before).Length);
});
Test("Checkout batch requires new instances even with recycled UIDs", () => {
    var before = new Dictionary<int, int> { [1] = 100 };
    var after = new Dictionary<int, int> { [1] = 101, [2] = 0, [-3] = 300 };
    Equal("1", string.Join(",", PurchaseRoutingPolicy.SelectNewSpawnUids(before, after)));
});
Test("One thousand purchases are all proven from a single checkout before snapshot", () => {
    var before = Enumerable.Range(0, 1000).ToDictionary(id => id, id => id + 1);
    var after = Enumerable.Range(0, 2000).ToDictionary(id => id, id => id + 1);
    var created = PurchaseRoutingPolicy.SelectNewSpawnUids(before, after);
    Equal(1000, created.Length); Equal(true, created.All(id => id >= 1000));
});
Test("Every model round trips its exact copied child marker", () => {
    foreach (var spec in ServerVariantSpec.All)
        Equal(spec, PurchaseRoutingPolicy.ResolveMarkerVariant(new[] { "Server.Green1", PurchaseRoutingPolicy.CreateMarkerName(spec), "PowerButton" }));
});
Test("Stock names and prices cannot substitute for an exact child marker", () => {
    Equal<ServerVariantSpec?>(null, PurchaseRoutingPolicy.ResolveMarkerVariant(new[] { "Server.Green1", dell.VariantDisplayName, dell.VariantId, dell.Price.ToString() }));
    Equal<ServerVariantSpec?>(null, PurchaseRoutingPolicy.ResolveMarkerVariant(Array.Empty<string>()));
    Equal<ServerVariantSpec?>(null, PurchaseRoutingPolicy.ResolveMarkerVariant(new[] { "RealServers_PurchaseVariant_unknown" }));
    Equal<ServerVariantSpec?>(null, PurchaseRoutingPolicy.ResolveMarkerVariant(new[] { PurchaseRoutingPolicy.CreateMarkerName(dell).ToUpperInvariant() }));
});
Test("Duplicate mixed malformed and cloned marker names fail closed", () => {
    string marker = PurchaseRoutingPolicy.CreateMarkerName(dell);
    Equal<ServerVariantSpec?>(null, PurchaseRoutingPolicy.ResolveMarkerVariant(new[] { marker, marker }));
    Equal<ServerVariantSpec?>(null, PurchaseRoutingPolicy.ResolveMarkerVariant(new[] { marker, PurchaseRoutingPolicy.CreateMarkerName(lenovo) }));
    Equal<ServerVariantSpec?>(null, PurchaseRoutingPolicy.ResolveMarkerVariant(new[] { marker, "RealServers_PurchaseVariant_unknown" }));
    Equal<ServerVariantSpec?>(null, PurchaseRoutingPolicy.ResolveMarkerVariant(new[] { marker + "(Clone)" }));
});
Test("Batch state freezes the native inventory before checkout", () => {
    var before = new Dictionary<int, int> { [7] = 700 };
    var state = new PurchaseCheckoutState(1, before); before.Clear();
    Equal(700, state.Before[7]); Equal(1, state.ShopInstanceId);
});
Console.WriteLine($"PurchaseRouting: {passed}/{passed + failed} passed."); return failed == 0 ? 0 : 1;
internal sealed class NullableFields { public bool hasValue; public int value; }
internal sealed class NullableProperties { public bool HasValue { get; set; } public int Value { get; set; } }
internal sealed class MissingNullableValue { public bool HasValue => true; }
internal sealed class InvalidNullableValue { public bool HasValue => true; public string Value => "unparseable"; }
internal sealed class ThrowingNullableValue { public bool HasValue => throw new Exception("unavailable"); }
