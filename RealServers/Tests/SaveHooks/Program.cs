using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;

internal static class Program
{
    private static string GameRoot = string.Empty;
    private static int _passed, _failed;
    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(op => op.Value);

    private static int Main(string[] args)
    {
        string? configuredGameRoot = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("SNP_DATACENTER_DIR");
        if (string.IsNullOrWhiteSpace(configuredGameRoot))
        {
            Console.Error.WriteLine("Set SNP_DATACENTER_DIR to your Data Center installation folder, or pass that folder as the first test argument.");
            return 1;
        }
        GameRoot = Path.GetFullPath(configuredGameRoot);
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            foreach (string folder in new[] { Path.Combine(GameRoot, "MelonLoader", "net6"), Path.Combine(GameRoot, "MelonLoader", "Il2CppAssemblies") })
            {
                string path = Path.Combine(folder, name.Name + ".dll");
                if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            }
            return null;
        };
        Assembly mod = Assembly.Load("RealServers");
        Assembly game = Assembly.Load("Assembly-CSharp");
        Type patches = mod.GetType("DataCenterAutomatorServers.PatchRegistry", true)!;
        Type entry = mod.GetType("DataCenterAutomatorServers.DataCenterAutomatorServersMod", true)!;
        Type native = game.GetType("Il2Cpp.SaveSystem", true)!;

        Run("Current native save and load APIs expose the required arguments", () =>
        {
            Require(native.GetMethod("SaveGame", new[] { typeof(string), typeof(string) }) != null, "SaveGame(string,string) missing");
            Require(native.GetMethod("Load", new[] { typeof(string), typeof(bool) }) != null, "Load(string,bool) missing");
            Require(native.GetMethod("LoadGame", new[] { typeof(string) }) != null, "LoadGame(string) missing");
            Require(native.GetMethod("GetRawSaveEntry", new[] { typeof(string) }) != null, "GetRawSaveEntry(string) missing");
            Require(native.GetProperty("saveDirPath")?.PropertyType == typeof(string), "Native save directory inaccessible");
        });
        Run("Harmony initialization installs the save ordering hooks", () =>
            Require(Calls(Method(patches, "Apply")).Contains("PatchPersistenceMethods"), "Persistence hooks are absent from initialization"));
        Run("Reading save metadata cannot switch the active world identity scope", () =>
        {
            var names = ScanIL(Method(patches, "PatchPersistenceMethods"), strings: true);
            Require(!names.Contains("LoadGame"), "LoadGame is also used by save listing and must not select another world's identities");
            Require(names.Contains("Load") && names.Contains("LoadFromBytes"), "Actual world-loading entry points must still be hooked");
        });
        Run("Native save prefix and postfix share a transaction state", () =>
        {
            var prefix = Method(patches, "NativeSavePrefix");
            var postfix = Method(patches, "NativeSavePostfix");
            var finalizer = Method(patches, "NativeSaveFinalizer");
            Require(prefix.GetParameters().Any(p => p.Name == "__state" && p.ParameterType.IsByRef), "Prefix transaction state missing");
            Require(postfix.GetParameters().Any(p => p.Name == "__state"), "Postfix transaction state missing");
            Require(finalizer.ReturnType == typeof(Exception), "Finalizer must preserve the native exception");
        });
        Run("Save postfix routes through the completed native save bridge", () =>
            Require(Calls(Method(patches, "NativeSavePostfix")).Contains("CompleteNativeSave"), "No identity commit after the native save"));
        Run("File load and shared-byte load have separate scope selection", () =>
        {
            Require(Calls(Method(patches, "NativeLoadPrefix")).Contains("BeginNativeLoad"), "Missing file scope selection before load");
            Require(Calls(Method(patches, "NativeBytesLoadPrefix")).Contains("BeginNativeLoad"), "Shared loads retain the previous file identity");
        });
        Run("Object load repair runs after native object state restoration", () =>
            Require(Calls(Method(patches, "LoadedObjectPostfix")).Contains("RestoreLoadedObject"), "No per-object identity restoration hook"));
        Run("Native save and late-load delegates are subscribed at startup", () =>
        {
            var names = Calls(Method(entry, "SubscribeSaveEvents"));
            Require(names.Contains("set_onSavingData"), "Native save callback missing");
            Require(names.Contains("set_onLoadingDataLater"), "Late loading callback missing");
            Require(names.Count(name => name == "op_Addition") >= 2, "Existing native callback subscribers must be preserved");
        });
        Run("Callback subscriptions are removed when the mod unloads", () =>
        {
            var names = Calls(Method(entry, "OnDeinitializeMelon"));
            Require(names.Contains("set_onSavingData") && names.Contains("set_onLoadingDataLater"), "Save callbacks remain subscribed after unloading");
        });
        Run("Late recovery waits for native network initialization", () =>
            Require(Calls(Method(entry, "OnUpdate")).Contains("CanCompletePersistenceLoad"), "Recovery is not gated on completion of native loading"));
        Run("A failed native load releases the loading state", () =>
            Require(Calls(Method(patches, "NativeLoadFinalizer")).Contains("AbortNativeLoad"), "Failed load can permanently block identity snapshots"));
        Run("Starting a new world clears the preceding world's identities", () =>
        {
            Require(ScanIL(Method(patches, "PatchPersistenceMethods"), strings: true).Contains("NewGame"), "NewGame is missing its independent scope hook");
            Require(Calls(Method(patches, "NewWorldPrefix")).Contains("ResetNativeWorld"), "NewGame does not reset the previous scope");
        });
        Run("Pickup hooks resolve the declared IL2CPP method instead of the inherited reflected wrapper", () =>
        {
            Type server = game.GetType("Il2Cpp.Server", true)!;
            Type usable = game.GetType("Il2Cpp.UsableObject", true)!;
            Type? selector = mod.GetType("DataCenterAutomatorServers.NativeHookTargets");
            Require(selector != null, "Declared native hook target resolver missing");
            foreach (string name in new[] { "MoveToHand", "DropObject" })
            {
                MethodInfo inherited = server.GetMethod(name)!;
                Require(inherited.DeclaringType == usable && inherited.ReflectedType == server, "Installed wrapper does not match the reported inherited pickup hook");
                MethodInfo declared = (MethodInfo)Method(selector!, "ResolveDeclared").Invoke(null, new object[] { inherited })!;
                Require(declared.DeclaringType == usable && declared.ReflectedType == usable,
                    "Harmony must receive the actual UsableObject declaration, not the Server-reflected inherited method");
                Require(declared.MetadataToken == inherited.MetadataToken, "Resolving a declaration changed the native method identity");
            }
            MethodInfo power = server.GetMethod("PowerButton")!;
            MethodInfo preserved = (MethodInfo)Method(selector!, "ResolveDeclared").Invoke(null, new object[] { power })!;
            Require(preserved.DeclaringType == server && preserved.MetadataToken == power.MetadataToken, "Actual server-specific methods must retain their implementation");
        });
        Run("Lifecycle Harmony registration uses declared targets before patching", () =>
            Require(Calls(Method(patches, "PatchServerLifecycleRepairMethods")).Contains("ResolveDeclared"),
                "Lifecycle registration still passes inherited reflected methods directly to IL2CPP Harmony"));
        Run("Base pickup hooks cast to Server before applying server-only controls", () =>
        {
            Require(Calls(Method(patches, "ServerMoveToHandPrefix")).Contains("ResolveServerHookInstance"), "Base pickup prefix receives a UsableObject wrapper without a native Server cast");
            Require(Calls(Method(patches, "ServerLifecycleRepairPostfix")).Contains("ResolveServerHookInstance"), "Base pickup postfix can configure unrelated usable objects");
        });
        Run("Current installed shop API uses a separate cart and checkout phase", () =>
        {
            Type shop = game.GetType("Il2Cpp.ComputerShop", true)!;
            var shopMethods = shop.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (string name in new[] { "AddNewCartItem", "SpawnAllPurchasedItems", "GetPrefabForItem", "SpawnPhysicalItem" })
                Require(shopMethods.Any(method => method.Name == name), "Current native shop method missing: " + name);
        });
        Run("Custom shop purchase identity is carried in the cart item ID", () =>
        {
            var prefix = Method(patches, "ComputerShopBuyPrefix");
            Require(prefix.GetParameters().Any(parameter => parameter.Name == "itemID" && parameter.ParameterType == typeof(int).MakeByRefType()),
                "Custom SKU cannot reach the deferred native cart because purchase itemID is not rewritten by reference");
            Require(Calls(prefix).Contains("PrepareShopPurchaseFromPatch"), "Native purchase does not enter the exact custom SKU bridge");
        });
        Run("Native prefab lookup maps a custom SKU to its carrier with a per-call state", () =>
        {
            var prefix = Method(patches, "ComputerShopGetPrefabPrefix");
            var postfix = Method(patches, "ComputerShopGetPrefabPostfix");
            Require(prefix.GetParameters().Any(parameter => parameter.Name == "itemID" && parameter.ParameterType == typeof(int).MakeByRefType()), "Native prefab lookup still receives an unknown custom item ID");
            Require(prefix.GetParameters().Any(parameter => parameter.Name == "__state" && parameter.IsOut), "Exact model is not retained while carrier ID is mapped");
            Require(postfix.GetParameters().Any(parameter => parameter.Name == "__state"), "Postfix loses the original custom model after carrier lookup");
            Require(Calls(prefix).Contains("PreparePurchasePrefabFromPatch") && Calls(postfix).Contains("FinalizePurchasePrefabFromPatch"), "Prefab prefix/postfix do not use the checkout-safe bridges");
            Require(Calls(Method(patches, "PatchComputerShopPrefabMethods")).Contains("Patch"), "Prefab integration is not installed");
        });
        Run("Void checkout captures the batch before native spawning and associates it afterwards", () =>
        {
            var prefix = Method(patches, "ComputerShopCheckoutPrefix");
            var postfix = Method(patches, "ComputerShopCheckoutPostfix");
            Require(prefix.GetParameters().Any(parameter => parameter.Name == "__state" && parameter.IsOut), "Checkout has no isolated before-inventory snapshot");
            Require(postfix.GetParameters().Any(parameter => parameter.Name == "__state"), "Checkout completion cannot distinguish newly spawned objects from old inventory");
            Require(Calls(prefix).Contains("BeginPurchaseCheckoutFromPatch") && Calls(postfix).Contains("CompletePurchaseCheckoutFromPatch"), "Checkout batch bridges do not retain exact purchase ownership");
        });
        Run("Small native value-type returns are never detoured for purchase association", () =>
        {
            Type shop = game.GetType("Il2Cpp.ComputerShop", true)!;
            Require(shop.GetMethod("SpawnAllPurchasedItems", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.ReturnType == typeof(void), "Safe batch target is no longer void");
            Require(shop.GetMethod("SpawnPhysicalItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.ReturnType.FullName!.StartsWith("Il2CppSystem.Nullable`1", StringComparison.Ordinal), "Installed API no longer matches the native 8-byte Nullable ABI");
            Require(!ScanIL(Method(patches, "Apply"), strings: true).Contains("SpawnPhysicalItem"), "Initialization detours the ABI-sensitive Nullable-returning method");
            Require(!ScanIL(Method(patches, "PatchComputerShopPurchaseMethods"), strings: true).Contains("SpawnPhysicalItem"), "Purchase registration retains a Nullable-returning detour branch");
        });
        Run("Checkout completion or failure clears purchase context", () =>
        {
            Require(ScanIL(Method(patches, "PatchComputerShopPurchaseMethods"), strings: true).Contains("SpawnAllPurchasedItems"), "Deferred checkout does not have a cleanup hook");
            Require(Calls(Method(patches, "ComputerShopCheckoutPostfix")).Contains("EndPurchaseCheckoutFromPatch"), "Successful checkout retains a previous custom SKU");
            Require(Calls(Method(patches, "ComputerShopCheckoutFinalizer")).Contains("EndPurchaseCheckoutFromPatch"), "Failed checkout retains custom purchase state");
        });
        Console.WriteLine($"Save hooks: {_passed} passed; {_failed} failed. Metadata checks do not execute IL2CPP injection.");
        return _failed == 0 ? 0 : 1;
    }

    private static MethodInfo Method(Type type, string name) =>
        type.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(type.FullName, name);
    private static List<string> Calls(MethodInfo method) => ScanIL(method, strings: false);
    private static List<string> ScanIL(MethodInfo method, bool strings)
    {
        byte[] bytes = method.GetMethodBody()?.GetILAsByteArray() ?? Array.Empty<byte>();
        var calls = new List<string>();
        for (int index = 0; index < bytes.Length;)
        {
            short value = bytes[index++];
            if (value == 0xfe) value = (short)(0xfe00 | bytes[index++]);
            OpCode op = OpCodesByValue[value];
            int size = op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, index),
                _ => 4
            };
            if (!strings && op.OperandType == OperandType.InlineMethod)
            {
                MethodBase? target = method.Module.ResolveMethod(BitConverter.ToInt32(bytes, index));
                if (target != null) calls.Add(target.Name);
            }
            else if (strings && op.OperandType == OperandType.InlineString)
                calls.Add(method.Module.ResolveString(BitConverter.ToInt32(bytes, index)));
            index += size;
        }
        return calls;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Run(string name, Action body)
    {
        try { body(); _passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { _failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
    }
}
