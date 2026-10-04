using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace DataCenterAutomatorServers;

internal static class PurchaseRoutingPolicy
{
    internal const string MarkerPrefix = "RealServers_PurchaseVariant_";

    internal static string CreateMarkerName(ServerVariantSpec spec) => MarkerPrefix + spec.VariantId;

    internal static ServerVariantSpec? ResolveMarkerVariant(IEnumerable<string> names)
    {
        string[] markers = names.Where(name => name.StartsWith(MarkerPrefix, StringComparison.Ordinal)).Take(2).ToArray();
        if (markers.Length != 1) return null;
        ServerVariantSpec? spec = ServerVariantSpec.FindById(markers[0].Substring(MarkerPrefix.Length));
        return spec != null && string.Equals(markers[0], CreateMarkerName(spec), StringComparison.Ordinal) ? spec : null;
    }

    internal static int[] SelectNewSpawnUids(IReadOnlyDictionary<int, int> before, IReadOnlyDictionary<int, int> after) =>
        after.Keys.Where(uid => uid >= 0 && after[uid] != 0 &&
            (!before.TryGetValue(uid, out int prior) || prior != after[uid])).ToArray();

    internal static ServerVariantSpec? ResolvePurchase(int itemId, int price, object? itemType, string? name) =>
        ServerVariantSpec.FindByPurchase(itemId, price, itemType, name);

    internal static ServerVariantSpec? RoutePrefab(ref int itemId, object? itemType)
    {
        if (!(itemType?.ToString() ?? string.Empty).Contains("Server", StringComparison.OrdinalIgnoreCase)) return null;
        ServerVariantSpec? spec = ServerVariantSpec.FindByPurchaseItemId(itemId);
        if (spec != null) itemId = spec.BaseItemId;
        return spec;
    }

    internal static bool CanBeginPhysicalSpawn(ServerVariantSpec? spec, int price, object? itemType) =>
        spec != null && spec.Price == price &&
        (itemType?.ToString() ?? string.Empty).Contains("Server", StringComparison.OrdinalIgnoreCase);

    internal static int? SelectSpawnUid(IReadOnlyDictionary<int, int> before, IReadOnlyDictionary<int, int> after, int returnedUid)
    {
        bool IsNew(int uid) => after.TryGetValue(uid, out int instance) && instance != 0 &&
            (!before.TryGetValue(uid, out int prior) || prior != instance);
        if (returnedUid != int.MinValue) return returnedUid >= 0 && IsNew(returnedUid) ? returnedUid : null;
        int[] created = after.Keys.Where(uid => uid >= 0 && IsNew(uid)).Take(2).ToArray();
        return created.Length == 1 ? created[0] : null;
    }

    internal static int ReadSpawnUid(object? result)
    {
        if (result == null) return int.MinValue;
        if (result is int managed) return managed;
        // Nullable boxing can point at an actual native Int32 despite the
        // CLR wrapper's Nullable<int> type. Verify the native type first.
        if (result is Il2CppSystem.Object nativeObject)
        {
            try
            {
                string typeName = nativeObject.GetIl2CppType()?.FullName ?? string.Empty;
                if (typeName == "System.Int32") return nativeObject.Unbox<int>();
                if (typeName.StartsWith("System.Nullable`1", StringComparison.Ordinal) &&
                    typeName.Contains("System.Int32", StringComparison.Ordinal) && result is Il2CppSystem.Nullable<int> native)
                    return native.hasValue ? native.value : int.MinValue;
                return int.MinValue;
            }
            catch { return int.MinValue; }
        }
        try
        {
            object? Read(params string[] names)
            {
                foreach (string name in names)
                {
                    FieldInfo? field = result.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field != null) return field.GetValue(result);
                    PropertyInfo? property = result.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (property?.GetMethod != null) return property.GetValue(result);
                }
                return null;
            }
            if (Read("hasValue", "HasValue", "has_value") is bool hasValue && !hasValue) return int.MinValue;
            object? value = Read("value", "Value");
            return value == null ? int.MinValue : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch { return int.MinValue; }
    }
}

internal sealed class PurchaseRoutingContext
{
    private readonly Dictionary<(int ShopId, int PrefabId), ServerVariantSpec> _routes = new();
    internal void SetPrefab(int shopId, int prefabId, ServerVariantSpec? spec)
    {
        if (shopId == 0 || prefabId == 0) return;
        var key = (shopId, prefabId);
        if (spec == null) _routes.Remove(key);
        else _routes[key] = spec;
    }
    internal ServerVariantSpec? GetSpec(int shopId, int prefabId) =>
        _routes.TryGetValue((shopId, prefabId), out var spec) ? spec : null;
    internal void EndCheckout(int shopId)
    {
        foreach (var key in _routes.Keys.Where(key => key.ShopId == shopId).ToArray()) _routes.Remove(key);
    }
    internal void Clear() => _routes.Clear();
}

internal sealed class PurchaseSpawnState
{
    internal int ShopInstanceId { get; }
    internal ServerVariantSpec Spec { get; }
    internal IReadOnlyDictionary<int, int> Before { get; }
    internal PurchaseSpawnState(int shopId, ServerVariantSpec spec, IReadOnlyDictionary<int, int> before)
    {
        ShopInstanceId = shopId; Spec = spec; Before = new Dictionary<int, int>(before);
    }
}

internal sealed class PurchaseCheckoutState
{
    internal int ShopInstanceId { get; }
    internal IReadOnlyDictionary<int, int> Before { get; }
    internal PurchaseCheckoutState(int shopId, IReadOnlyDictionary<int, int> before)
    {
        ShopInstanceId = shopId;
        Before = new Dictionary<int, int>(before);
    }
}
