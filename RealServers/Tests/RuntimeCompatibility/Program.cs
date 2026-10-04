using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.ExceptionServices;

internal static class Program
{
    private static readonly Assembly Mod = Assembly.Load("RealServers");
    private static readonly Type Injector = Mod.GetType("DataCenterAutomatorServers.CatalogInjector", true)!;
    private static readonly Type SpecType = Mod.GetType("DataCenterAutomatorServers.ServerVariantSpec", true)!;
    private static int _passed, _failed;

    private static int Main()
    {
        Run("Form factor updates preserve the native hand and trolley state", () =>
        {
            var spec = Spec("dell_xe8640_lenovo_carrier");
            var apply = Mod.GetType("DataCenterAutomatorServers.ManualRackFormFactor", true)!.GetMethod("ApplyToServer")!;
            foreach (bool held in new[] { false, true })
            foreach (bool trolley in new[] { false, true })
            {
                var server = new FakeServer { objectInHands = held, isOnTrolley = trolley };
                Invoke(apply, null, server, spec);
                Assert(server.sizeInU == 4 && server.objectInHandType == 3, "XE8640 must still receive its physical form factor.");
                Assert(server.objectInHands == held && server.isOnTrolley == trolley, "Form-factor refresh changed native ownership flags.");
            }
        });
        Run("Configuring a large Green carrier preserves its native workload family", () =>
        {
            var spec = Spec("dell_xe8640_lenovo_carrier");
            var server = new FakeServer { serverType = 1, sizeInU = 4, maxProcessingSpeed = 0.20f };
            Call(null, "ConfigureServer", server, spec);
            Assert(server.serverType == 1, "4U Dell configuration rewrote the native Green2 workload family.");
        });
        Run("Mounted carrier power prefix lets the native IP and network transition run", () =>
        {
            var strings = Strings("ShouldAllowPowerButtonInteraction");
            Assert(!strings.Contains("TurnOnCommonFunction") && !strings.Contains("TurnOffCommonFunctions"), "Prefix bypasses native PowerButton with a partial common transition.");
            Assert(Policy("AllowNativePower", true, true), "Mounted custom server must reach native IP duplicate checks and network changes.");
            Assert(!Policy("AllowNativePower", true, false), "A loose custom chassis must remain blocked from powering on.");
            Assert(Policy("AllowNativePower", false, false), "Ordinary stock power remains under the game handler.");
        });
        Run("Computer app power uses the same native PowerButton as physical controls", () =>
        {
            var strings = Strings("ToggleComputerAppPower");
            Assert(strings.Contains("PowerButton"), "App must invoke the native PowerButton method.");
            Assert(!strings.Contains("TurnOnCommonFunction") && !strings.Contains("TurnOffCommonFunctions"), "App must not invoke partial common power functions.");
        });
        Run("Pending purchase identity cannot convert an existing stock server", () =>
        {
            Assert(Injector.GetMethod("TryApplyPendingSpecsToSceneServers", BindingFlags.NonPublic | BindingFlags.Instance) == null,
                "Pending purchases still scan and configure unrelated scene servers.");
            Assert(!Called("TryConfigurePendingLifecycleServer").Contains("ActivePendingSpecs"), "Unproven lifecycle instances still consume pending purchase identity.");
            Assert(!Called("TryFinalizeInsertedServer").Contains("DequeueMatchingPendingSpec"), "Inserting an old stock server can still consume a new Dell purchase.");
            Assert(!Called("TryAssociatePendingSpawnedItems").Contains("ActivePendingSpecs"), "UID retry can still match a stock spawn merely by carrier appearance.");
        });
        Run("Save recovery never infers Dell identity from native color or a rack position", () =>
        {
            var calls = Called("RecoverLoadedServerVariantIdentity");
            Assert(!calls.Contains("GetByRackPositionUid") && !calls.Contains("PersistedServerMatchesDellCarrierSpec"), "Recovery still treats an old rack/color hint as exact identity.");
        });
        Run("A loose server without a saved rack slot never claims a nearby rack", () =>
        {
            Assert(!Policy("CanRestoreRack", -1, false, false), "Loose server cannot claim a rack.");
            Assert(!Policy("CanRestoreRack", 0, false, false), "Zero rack UID is not a mounted reservation.");
            Assert(!Policy("CanRestoreRack", 42, true, false), "Hand state wins over stale saved rack UID.");
            Assert(!Policy("CanRestoreRack", 42, false, true), "Trolley state wins over stale saved rack UID.");
            Assert(Policy("CanRestoreRack", 42, false, false), "An actual positive saved slot remains recoverable.");
        });
        Run("Adjacent mixed-height rack devices remain playable without overlap", () =>
        {
            Assert(!Policy("RackRangesOverlap", 1, 1, 2, 4), "A 1U before a 4U is adjacent, not overlapping.");
            Assert(!Policy("RackRangesOverlap", 2, 4, 6, 1), "A 1U after a 4U is adjacent, not overlapping.");
            Assert(Policy("RackRangesOverlap", 2, 4, 5, 1), "The last occupied unit must block a second device.");
            Assert(Policy("RackRangesOverlap", 5, 1, 2, 4), "Overlap must be symmetric for reversed insertion order.");
            Assert(Policy("RackRangesOverlap", int.MaxValue - 1, 4, int.MaxValue, 1), "Rack interval arithmetic cannot overflow into an available slot.");
        });
        Run("Rack recovery retries expire and never run for an empty pending set", () =>
        {
            var now = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
            Assert(!Policy("ShouldRetryLoadedRacks", 0, now, now.AddSeconds(5)), "Empty pending set must be idle.");
            Assert(!Policy("ShouldRetryLoadedRacks", 5, now, now.AddSeconds(-1)), "Expired recovery cannot scan racks indefinitely.");
            Assert(Policy("ShouldRetryLoadedRacks", 5, now, now.AddSeconds(5)), "Known pending slots must get their short post-load retry.");
        });
        Run("Runtime carrier prototypes never enter an inventory identity snapshot", () =>
        {
            Assert(!Policy("CanCaptureIdentity", false, false), "Asset prefabs without a valid game scene cannot be inventory.");
            Assert(!Policy("CanCaptureIdentity", true, true), "Cached DontDestroyOnLoad carrier prototype is not physical purchased stock.");
            Assert(Policy("CanCaptureIdentity", true, false), "Real instantiated game inventory must remain capturable.");
        });
        Run("Equal-price Lenovo and Dell purchases require their actual named variant", () =>
        {
            var dell = Spec("dell_r440_lenovo_carrier");
            var lenovo = Spec("lenovo_sr650");
            int id = (int)SpecType.GetProperty("BaseItemId")!.GetValue(dell)!;
            int price = (int)SpecType.GetProperty("Price")!.GetValue(dell)!;
            Assert(Call(null, "ResolveSpecFromPurchase", id, price, "Server", "Green server") == null, "A stock purchase cannot become Dell merely because id and price match.");
            Assert(ReferenceEquals(Call(null, "ResolveSpecFromPurchase", id, price, "Server", SpecType.GetProperty("VariantDisplayName")!.GetValue(dell)), dell), "Dell purchase must retain its named identity.");
            int lenovoId = (int)SpecType.GetProperty("BaseItemId")!.GetValue(lenovo)!;
            Assert(ReferenceEquals(Call(null, "ResolveSpecFromPurchase", lenovoId, price, "Server", SpecType.GetProperty("VariantDisplayName")!.GetValue(lenovo)), lenovo), "Equal-price Lenovo purchase must retain its separate identity.");
            Assert(Call(null, "ResolveSpecFromPurchase", id, price, "Cable", SpecType.GetProperty("VariantDisplayName")!.GetValue(dell)) == null, "Non-server purchase is never a server.");
        });
        Run("Form factor ignores absent state without touching the game", () =>
        {
            var apply = Mod.GetType("DataCenterAutomatorServers.ManualRackFormFactor", true)!.GetMethod("ApplyToServer")!;
            var server = new FakeServer { objectInHands = true, isOnTrolley = true };
            Invoke(apply, null, null, Spec("dell_r440_lenovo_carrier"));
            Invoke(apply, null, server, null);
            Assert(server.objectInHands && server.isOnTrolley, "A missing spec cannot mutate state.");
        });
        Run("Snapshot eligibility records held and trolley servers as loose inventory", () =>
        {
            var server = new FakeServer { rackPositionUID = 42 };
            Assert((int)Call(null, "ReadRestorableRackUid", server)! == 42, "Actually mounted server loses its slot.");
            server.objectInHands = true;
            Assert((int)Call(null, "ReadRestorableRackUid", server)! == -1, "Held server retains its obsolete rack reservation.");
            server.objectInHands = false;
            server.isOnTrolley = true;
            Assert((int)Call(null, "ReadRestorableRackUid", server)! == -1, "Trolley server retains its obsolete rack reservation.");
            Assert(server.rackPositionUID == 42 && server.isOnTrolley, "Snapshot preparation cannot mutate native ownership fields.");
        });
        Run("Aborting a failed native load releases the gate without clearing scoped identity", () =>
        {
            var abort = Injector.GetMethod("AbortPersistenceLoad", BindingFlags.Public | BindingFlags.Instance);
            Assert(abort != null, "Failed loads have no abort recovery callback.");
            Assert(!Called("AbortPersistenceLoad").Contains("SelectSave") && !Called("AbortPersistenceLoad").Contains("ResetRuntimeAttemptState"), "Abort must preserve selected identity and live objects.");
        });
        Run("New worlds clear prior-save identity without scanning or rewriting old inventory", () =>
        {
            var reset = Injector.GetMethod("ResetPersistenceForNewWorld", BindingFlags.Public | BindingFlags.Instance);
            Assert(reset != null, "New worlds have no scope reset callback.");
            var calls = Called("ResetPersistenceForNewWorld");
            Assert(calls.Contains("ResetRuntimeAttemptState") && calls.Contains("SelectSave"), "New world must clear runtime ownership and selected save scope.");
            Assert(!calls.Contains("RepairPersistedSceneVariants") && !calls.Contains("FindUnityObjectsOfTypeAllDirect"), "New world reset cannot configure servers from the previous world.");
        });
        Run("Completing recovery always releases the loading gate even if a native repair fails", () =>
        {
            var method = Injector.GetMethod("CompletePersistenceLoad", BindingFlags.Public | BindingFlags.Instance)!;
            Assert(method.GetMethodBody()!.ExceptionHandlingClauses.Any(clause => clause.Flags == ExceptionHandlingClauseOptions.Finally), "Completion must release its loading state in a finally block.");
        });
        Run("Snapshot and object restoration use exact durable identity and exclude prototypes", () =>
        {
            var capture = Called("CapturePersistenceSnapshot");
            Assert(capture.Contains("IsLiveInventory") && capture.Contains("ResolveExactOwnedSpec") && capture.Contains("RecordIdentity"), "Save snapshot must contain only proven physical inventory.");
            var restore = Called("RestoreLoadedObjectIdentity");
            Assert(restore.Contains("GetByLooseId") && restore.Contains("ClearStaleLoadedRackState"), "Loaded loose identity must survive before rack metadata is considered.");
        });
        Run("Rack restoration rejects a live reference to a different saved slot", () =>
        {
            Assert(Policy("IsMatchingRackReservation", 42, 42), "The exact saved rack position must remain valid.");
            Assert(!Policy("IsMatchingRackReservation", 42, 43), "A live reference to another rack cannot override durable identity.");
            Assert(!Policy("IsMatchingRackReservation", 0, 0), "Zero slot UID cannot prove a reservation.");
            Assert(!Policy("IsMatchingRackReservation", -1, -1), "Loose inventory cannot prove a reservation.");
            Assert(Called("RestorePersistedRackMountState").Contains("IsMatchingRackReservation"), "Existing rack references must pass the exact slot guard.");
            Assert(Called("RepairLoadedRackState").Contains("RackUidDuringLoad"), "Forced repair must respect the saved loose/mounted state during load.");
        });
        Run("Native Start label recovery keeps the stock SO family and the custom hidden name", () =>
        {
            var dell = Spec("dell_r440_lenovo_carrier");
            var payload = new FakeShopPayload { itemID = 987654, itemName = "Green server", price = 7000 };
            var card = new FakeShopCard { itemDisplayName = "Green server", shopItemSO = payload };
            Call(null, "RestoreShopPurchaseIdentity", card, dell);
            string label = (string)SpecType.GetProperty("VariantDisplayName")!.GetValue(dell)!;
            Assert(card.itemDisplayName == label && payload.itemName == label, "Visible Dell identity did not reach the hidden native purchase name.");
            Assert(payload.itemID == (int)SpecType.GetProperty("BaseItemId")!.GetValue(dell)!, "Native Start cannot receive the custom cart SKU as a stock array index.");
            Assert(payload.price == 7000, "Name repair changed the native price.");
        });
        Run("Unknown spawn callbacks and SKU routing cannot claim old scene inventory", () =>
        {
            Assert(Called("PreparePurchasePrefab").Contains("RoutePrefab"), "Custom SKU was not mapped for the native carrier lookup.");
            Assert(Called("CompletePurchaseCheckout").Contains("SelectNewSpawnUids"), "Checkout ownership bypassed proof from the before/after spawn delta.");
            Assert(!Called("TryConfigureSpawnedPhysicalItem").Contains("ActivePendingSpecs"), "Legacy physical callback still guesses a model from pending prices.");
            Assert(Called("TryRefreshShopItemText").Contains("RestoreShopPurchaseIdentity"), "Native Start can still overwrite the hidden custom card name.");
        });
        Run("Checkout prefab preloading keeps unique inactive prototypes and exact routes", () =>
        {
            Assert(!Called("PreparePurchasePrefab").Contains("EndCheckout"), "Preloading another SKU still discards the first route.");
            Assert(Called("FinalizePurchasePrefab").Contains("CreateDistinctPurchasePrefab"), "Two custom SKUs can still reuse the same native prefab.");
            Assert(Called("IsLiveInventory").Contains("IsPurchasePrototype"), "Cached native carrier prototypes can still enter an inventory snapshot.");
            Assert(!Called("CompletePurchaseCheckout").Contains("ResolveSpecFromPurchase"), "Checkout can still consume SKU identity from a price or display-name guess.");
        });
        Run("Purchase prototypes and new physical clones never share durable identities", () =>
        {
            var prototype = new FakeServer();
            Call(null, "ClearPurchasePrototypeIdentity", prototype);
            Assert(prototype.ServerID == "" && prototype.coopLooseId == "", "A cached prototype retained a durable native or loose identity.");
            var complete = Called("CompletePurchaseCheckout");
            Assert(complete.Contains("EnsureLooseIdentity") && complete.Contains("EnsureUniqueServerIdentity"), "Spawn completion did not ensure independent native and loose identities before recording.");
            Assert(!complete.Contains("ClearPurchasePrototypeIdentity"), "Spawn completion clears a valid native ID after the game has registered its new physical object.");
        });
        Run("Carrier prototype keeps the native name used to generate exact server IDs", () =>
        {
            Assert(!Strings("CreateDistinctPurchasePrefab").Contains("RealServers_PurchasePrefab_"),
                "GenerateUniqueServerId uses GameObject.name; custom prototype prefix breaks the native Server.* identity family.");
            var calls = Called("CreateDistinctPurchasePrefab");
            Assert(calls.Contains("get_name") && calls.Contains("set_name"),
                "The native source carrier name is not copied to the custom checkout prototype.");
        });
        Run("Checkout identity uses batch UID delta and an exact copied child marker", () =>
        {
            var complete = Called("CompletePurchaseCheckout");
            Assert(complete.Contains("SelectNewSpawnUids") && complete.Contains("ResolveMarkerVariant"),
                "Checkout cannot prove every fresh physical object and its exact custom SKU independently.");
            Assert(!complete.Contains("ReadNullableInt") && !complete.Contains("SelectSpawnUid"),
                "Batch checkout still depends on intercepting a Nullable<int> native return.");
            Assert(Called("CreateDistinctPurchasePrefab").Contains("CreateMarkerName"),
                "The custom carrier prototype lacks its independently copied SKU child marker.");
            Assert(Called("BeginPhysicalSpawn").Length == 0 && Called("CompletePhysicalSpawn").Length == 0 && Called("TryConfigureSpawnedPhysicalItem").Length == 0,
                "A legacy physical callback can still decode native nullable returns or configure scene inventory.");
        });
        Run("Registration exposes modeled offers while retaining the legacy catalog for recovery", () =>
        {
            var catalog = Mod.GetType("DataCenterAutomatorServers.ServerCatalog", true)!;
            Assert(catalog.GetField("ShopVariants") != null, "Modeled shop set is absent.");
            foreach (string method in new[] { "TryRegisterAll", "TryRegisterShopUiVariants", "RegisterCandidate", "get_HasRegisteredVariants" })
                Assert(Fields(method).Contains("ShopVariants") && !Fields(method).Contains("All"), method + " still registers or waits for unsupported shop offers.");
            Assert(Fields("ResolveSpecFromRuntimeObject").Contains("All"), "Shop cleanup removed historical inventory lookup.");
        });
        Run("Old unsupported shop cards are hidden without claiming native cards or live inventory", () =>
        {
            var obsolete = new FakeShopCard { guid = "real_servers_dell_r650", name = "real_servers_dell_r650_button" };
            Assert((bool)Call(null, "HideUnavailableShopItem", obsolete)!, "An old unsupported offer was retained.");
            Assert(!obsolete.active, "Old unsupported card remains visible.");
            foreach (var card in new[]
            {
                new FakeShopCard { guid = "real_servers_dell_r440_lenovo", name = "real_servers_dell_r440_lenovo_button" },
                new FakeShopCard { name = "Server.Green1", itemDisplayName = "Dell PowerEdge R650" },
                new FakeShopCard { guid = "another_mod_real_servers_dell_r650", name = "another_mod" },
            })
            {
                Assert(!(bool)Call(null, "HideUnavailableShopItem", card)! && card.active, "A supported/native/other mod card was hidden.");
            }
            Assert(Called("TryRefreshShopItemText").Contains("HideUnavailableShopItem") && Called("TryRegisterShopUiVariants").Contains("HideUnavailableShopItem"), "Shop lifecycle can reactivate unavailable offers.");
            Assert(!Called("HideUnavailableShopItem").Contains("ConfigureServer") && !Called("HideUnavailableShopItem").Contains("Destroy"), "Shop cleanup mutates physical inventory.");
        });
        Run("Lifecycle hooks avoid a second full model and port rebuild", () =>
        {
            var patches = Mod.GetType("DataCenterAutomatorServers.PatchRegistry", true)!;
            var calls = CalledBy(patches, "ServerLifecycleRepairPostfix");
            Assert(!calls.Contains("TryRepairKnownPersistedServerFromPatch"), "Every pickup/startup still rebuilds the entire custom model and ports after the lightweight callback.");
            Assert(calls.Contains("TryConfigurePowerControlsFromPatch") && calls.Contains("TryConfigurePendingLifecycleServerFromPatch") && calls.Contains("TryPersistKnownServerIdentityFromPatch"), "Removing duplicate repairs also removed required control or identity callbacks.");
        });
        Run("Native save events use the active-transaction snapshot guard", () =>
        {
            var entry = Mod.GetType("DataCenterAutomatorServers.DataCenterAutomatorServersMod", true)!;
            var callbacks = entry.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance).Where(method => method.Name.Contains("<SubscribeSaveEvents>"));
            var calls = callbacks.SelectMany(method => CalledBy(entry, method.Name)).ToArray();
            Assert(calls.Contains("CaptureOutsideTransaction"), "The native event still captures the entire datacenter again during an active save.");
            Assert(!calls.Contains("CapturePersistenceSnapshot"), "The event bypasses the transaction guard.");
        });
        Run("Resource discovery restores a Server wrapper before reading native save fields", () =>
        {
            var actual = new FakeServer { ServerID = "Server.Green1_-42", rackPositionUID = 73 };
            var wrapper = new FakeNativeWrapper(actual);
            object? restored = Call(null, "CastResourceObject", wrapper, typeof(FakeServer));
            Assert(ReferenceEquals(actual, restored), "Base Unity wrapper was not converted to its requested Server type.");
            Assert(((FakeServer)restored!).ServerID == "Server.Green1_-42" && ((FakeServer)restored).rackPositionUID == 73,
                "Typed resource lost the exact persisted native identity/rack UID.");
            Assert(wrapper.CastCount == 1, "A resource needs exactly one native cast, independent of cache state.");
        });
        Run("Resource discovery never returns an unrelated wrapper as its requested type", () =>
        {
            var wrapper = new FakeNativeWrapper(new FakeRackPosition { rackPosGlobalUID = 73 });
            Assert(Call(null, "CastResourceObject", wrapper, typeof(FakeServer)) == null, "A wrong resource type was retained as a Server.");
            Assert(Call(null, "CastResourceObject", null, typeof(FakeServer)) == null, "Null Unity resource was retained.");
            Assert(Call(null, "CastResourceObject", wrapper, null) == null, "Absent requested type cannot discover arbitrary resources.");
        });
        Run("Already typed resources retain their instance and rack results cast to RackPosition", () =>
        {
            var server = new FakeServer { ServerID = "Server.Green1_-42" };
            Assert(ReferenceEquals(server, Call(null, "CastResourceObject", server, typeof(FakeServer))), "A typed resource was unnecessarily recreated.");
            var rack = new FakeRackPosition { rackPosGlobalUID = 42 };
            Assert(ReferenceEquals(rack, Call(null, "CastResourceObject", new FakeNativeWrapper(rack), typeof(FakeRackPosition))), "RackPosition indexing still reads an untyped Unity wrapper.");
        });
        Run("One thousand uncached resources each produce one exact typed result", () =>
        {
            int casts = 0, found = 0;
            for (int index = 0; index < 1_000; index++)
            {
                var server = new FakeServer { ServerID = "Server.Green1_" + index, rackPositionUID = index + 1 };
                var wrapper = new FakeNativeWrapper(server);
                Assert(ReferenceEquals(server, Call(null, "CastResourceObject", wrapper, typeof(FakeServer))), "Uncached wrapper disappeared from the snapshot.");
                casts += wrapper.CastCount; found++;
            }
            Assert(casts == 1_000 && found == 1_000, "Discovery repeated casts or skipped uncached resources.");
        });
        Run("Direct Unity resource enumeration actually applies the requested cast", () =>
        {
            var iterator = Injector.GetNestedTypes(BindingFlags.NonPublic).Single(type => type.Name.StartsWith("<FindUnityObjectsOfTypeAllDirect>"));
            Assert(CalledBy(iterator, "MoveNext").Contains("CastResourceObject"), "The game resource iterator still yields base Unity.Object wrappers directly.");
        });
        Run("A known saved identity without a custom visual still requires its first physical setup", () =>
        {
            var server = new FakeServer { ServerID = "Server.Green1_-42", maxProcessingSpeed = .20f };
            var known = new HashSet<object> { server };
            var prepared = new HashSet<object>();
            Assert(known.Contains(server) && !(bool)Call(null, "IsPhysicalServerPreparedIn", prepared, server)!, "Identity-known stock visual incorrectly skips its first model/form-factor restoration.");
            Call(null, "MarkPhysicalServerPreparedIn", prepared, server);
            Assert((bool)Call(null, "IsPhysicalServerPreparedIn", prepared, server)!, "Completed physical setup was not remembered for subsequent startup callbacks.");
            prepared.Clear();
            Assert(!(bool)Call(null, "IsPhysicalServerPreparedIn", prepared, server)!, "Preparation leaked from the preceding scene into newly loaded servers.");
            Assert(Fields("ResetRuntimeAttemptState").Contains("_preparedPhysicalServers"), "Scene/load reset retains physical readiness from the previous world.");
        });
        Run("Saved insertion and native startup check physical readiness independently of identity", () =>
        {
            foreach (string method in new[] { "TryFinalizeInsertedServer", "TryConfigurePendingLifecycleServer" })
                Assert(Called(method).Contains("IsPhysicalServerPrepared"), method + " still treats identity knowledge as completed physical setup.");
            Assert(Fields("IsPhysicalServerPrepared").Contains("_preparedPhysicalServers") && !Fields("IsPhysicalServerPrepared").Contains("_knownModdedServers"), "Preparation guard still uses an identity-only marker.");
            Assert(Called("TryFinalizeInsertedServer").Contains("ConfigureServerAndPorts"), "An unprepared saved server cannot receive its first full restoration.");
            var calls = Called("ConfigureServerAndPorts");
            Assert(Array.IndexOf(calls, "MarkPhysicalServerPrepared") > Array.IndexOf(calls, "ConfigureServerLiveControls"), "Physical readiness is published before visual/port setup finishes.");
        });
        Run("Saved server identity is captured before native ID remapping", () =>
        {
            var patches=Mod.GetType("DataCenterAutomatorServers.PatchRegistry",true)!;
            var method=patches.GetMethod("ServerInsertedPrefix",BindingFlags.Public|BindingFlags.Static);
            Assert(method!=null,"Native insertion has no pre-remap saved identity hook.");
            Assert(CalledBy(patches,"ServerInsertedPrefix").Contains("TryPrepareLoadedServerIdentityFromPatch"),"The authoritative serialized ID is not captured before native insertion.");
            Assert(Called("RecoverLoadedServerVariantIdentity").Contains("ReadSavedSourceServerId"),"Reload still relies solely on the live ID after native remapping.");
        });
        Run("Native snapshot publishes the explicit manual rack reservation", () =>
        {
            Assert(Called("CapturePersistenceSnapshot").Contains("PrepareNativeSnapshotRackUid"),"The selected manual rack is omitted from native save preparation.");
            Assert(Called("PrepareNativeSnapshotRackUid").Contains("TryEnsureNativeRackPositionUid"),"Snapshotting accepts an unregistered native slot.");
        });
        Run("Legacy repair is limited to owned unknown records and exact poses", () =>
        {
            Assert(Called("TrackLoadedServerRackState").Contains("HasUnknownSavedRackLocation"),"Explicitly loose snapshots can be captured as mounted legacy servers.");
            Assert(Called("RepairLegacyExactRackMounts").Contains("FindUnique"),"Legacy migration guesses a nearby rack instead of requiring a unique saved pose.");
        });
        Console.WriteLine($"{_passed} passed, {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }

    private static object Spec(string family) => ((Array)SpecType.GetField("All")!.GetValue(null)!).Cast<object>()
        .Single(value => (string)SpecType.GetProperty("FamilyKey")!.GetValue(value)! == family);
    private static bool Policy(string name, params object[] args)
    {
        var type = Mod.GetType("DataCenterAutomatorServers.RuntimeOwnershipPolicy", true)!;
        return (bool)Invoke(type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!, null, args)!;
    }
    private static string[] Strings(string name) => Operands(name, OperandType.InlineString)
        .Select(token => Injector.Module.ResolveString(token)).ToArray();
    private static string[] Called(string name) => Operands(name, OperandType.InlineMethod)
        .Select(token => Injector.Module.ResolveMethod(token)!.Name).ToArray();
    private static string[] Fields(string name) => Operands(name, OperandType.InlineField)
        .Select(token => Injector.Module.ResolveField(token)!.Name).ToArray();
    private static string[] CalledBy(Type type, string name) => Operands(type, name, OperandType.InlineMethod)
        .Select(token => type.Module.ResolveMethod(token)!.Name).ToArray();
    private static IEnumerable<int> Operands(string name, OperandType desired) => Operands(Injector, name, desired);
    private static IEnumerable<int> Operands(Type type, string name, OperandType desired)
    {
        var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)!;
        var bytes = method.GetMethodBody()!.GetILAsByteArray()!;
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(OpCode))
            .Select(f => (OpCode)f.GetValue(null)!).ToDictionary(value => value.Value);
        for (int offset = 0; offset < bytes.Length;)
        {
            var first = bytes[offset++];
            short value = first == 0xfe ? (short)(0xfe00 | bytes[offset++]) : first;
            var operand = opcodes[value].OperandType;
            if (operand == desired) yield return BitConverter.ToInt32(bytes, offset);
            offset += operand switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + BitConverter.ToInt32(bytes, offset) * 4,
                _ => 4,
            };
        }
    }
    private static object? Call(object? instance, string name, params object?[] args) =>
        Invoke(Injector.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)!, instance, args);
    private static object? Invoke(MethodInfo method, object? instance, params object?[] args)
    {
        try { return method.Invoke(instance, args); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }
    private static void Run(string name, Action body)
    {
        try { body(); _passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { _failed++; Console.WriteLine("FAIL " + name + ": " + error); }
    }
    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    public sealed class FakeServer
    {
        public string name = "Server.Green1", ServerID = "Server.Green1_-101", coopLooseId = "test-loose-id";
        public int serverType, sizeInU = 1, objectInHandType = 1, rackPositionUID = -1;
        public float maxProcessingSpeed = 0.05f, currentProcessingSpeed = 0, effectiveProcessingSpeed = 0;
        public bool objectInHands, isOnTrolley, isOn;
        public object? currentRackPosition = null;
        public int CommonPowerCalls, NativePowerCalls, IpDuplicateChecks;
        public void TurnOnCommonFunction() { CommonPowerCalls++; isOn = true; }
        public void TurnOffCommonFunctions() { CommonPowerCalls++; isOn = false; }
        public void PowerButton(bool forceState) { NativePowerCalls++; IpDuplicateChecks++; isOn = forceState || !isOn; }
    }
    public sealed class FakeShopPayload { public int itemID, price; public string itemName = ""; }
    public sealed class FakeRackPosition { public int rackPosGlobalUID; }
    public sealed class FakeNativeWrapper
    {
        private readonly object _native;
        internal int CastCount;
        internal FakeNativeWrapper(object native) => _native = native;
        public T? TryCast<T>() where T : class { CastCount++; return _native as T; }
    }
    public sealed class FakeShopCard
    {
        public string itemDisplayName = "", guid = "", name = "";
        public object? shopItemSO;
        public bool active = true;
        public void SetActive(bool value) { active = value; }
    }
}
