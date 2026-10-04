using System.Reflection;
using System.Runtime.ExceptionServices;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPolyStang;
using InfiniteTrolley.Runtime;
using UnityEngine;

internal static class Program
{
    private static int _passed, _failed;
    private static int Main()
    {
        Run("Initialization registers lifecycle hooks and rollback finalizer", () =>
        {
            var (mod, _, _, _) = Cargo();
            SaveSystem.onLoadingData = null;
            SaveSystem.onLoadingDataLater = null;
            mod.OnInitializeMelon();
            Assert(mod.HarmonyInstance.Patches.Count == 10, "All ten native lifecycle targets must be patched.");
            Assert(mod.HarmonyInstance.Patches.Any(patch => patch.Method.DeclaringType == typeof(TrolleyLoadingBay) && patch.Method.Name == "InteractOnClick" && patch.Finalizer?.Name == "PlacementFailed"), "Placement needs the rollback finalizer.");
            Assert(SaveSystem.onLoadingData != null && SaveSystem.onLoadingDataLater != null, "Both save load phase callbacks must stay registered.");
        });
        Run("Initialization removes partial hooks when a target cannot be found", () =>
        {
            var (mod, _, _, _) = Cargo();
            HarmonyLib.AccessTools.FailLookup = true;
            try
            {
                try { mod.OnInitializeMelon(); throw new InvalidOperationException("Expected missing target error."); }
                catch (MissingMethodException) { }
                Assert(mod.HarmonyInstance.Unpatched, "Failed initialization must remove partial hooks.");
            }
            finally { HarmonyLib.AccessTools.FailLookup = false; }
        });
        Run("Hover prepares capacity for all held objects while ignoring stale entries", () =>
        {
            var (_, bay, item, _) = Cargo();
            item.sizeInU = 80;
            var other = new GameObject().AddComponent<UsableObject>();
            other.objectInHands = true;
            other.sizeInU = 70;
            var stale = new GameObject().AddComponent<UsableObject>();
            stale.sizeInU = int.MaxValue;
            PlayerManager.instance!.objectInHandGO = new[] { null!, new GameObject(), stale.gameObject, item.gameObject, other.gameObject };
            Call("PrepareCapacity", bay);
            Assert(bay.usedPositions.Length >= 150 && bay.usedPositions.All(value => value == 0), "Sum of the held objects needs contiguous free capacity.");
        });
        Run("Bay initialization works without a player or held item array", () =>
        {
            var (_, bay, _, _) = Cargo();
            PlayerManager.instance = null;
            Call("BayReady", bay);
            Call("PrepareCapacity", bay);
            Assert(bay.usedPositions.Length >= 64, "Native bay still needs initial usable capacity.");
            PlayerManager.instance = new PlayerManager();
            var state = (Array)BeginPlacement(bay);
            Assert(state.Length == 0, "Missing held item array must be empty.");
        });
        Run("Unavailable native positions do not disable held colliders", () =>
        {
            var (_, bay, _, child) = Cargo();
            bay.positionsOnTrolley = new Il2CppReferenceArray<Transform>(0);
            bay.usedPositions = new Il2CppStructArray<int>(0);
            BeginPlacement(bay);
            Assert(child.enabled, "Invalid bay must not alter held object colliders.");
        });
        Run("A refused native placement restores its captured collider states", () =>
        {
            var (_, bay, _, child) = Cargo();
            var state = BeginPlacement(bay);
            Call("AfterPlacement", bay, state);
            Assert(child.enabled, "Item still in hands must not keep disabled child colliders.");
            Assert(Call("PlacementFailed", bay, state, null) == null, "Successful placement finalizer must keep a null exception.");
        });
        Run("Pickup before delayed finalization restores child colliders", () =>
        {
            var (mod, bay, item, child) = Cargo();
            var state = BeginPlacement(bay);
            Assert(!child.enabled, "Prefix must disable the captured child collider.");
            item.objectInHands = false;
            item.transform.SetParent(bay.transform);
            Call("AfterPlacement", bay, state);
            item.GetComponent<Collider>()!.enabled = true;
            item.objectInHands = true;
            item.rb!.isKinematic = true;
            item.rb.useGravity = false;
            Time.realtimeSinceStartup = 0.55f;
            Call("AfterPickup", item);
            Assert(child.enabled, "Captured child collider remains disabled after early pickup.");
        });
        Run("Pickup retains the native hand Rigidbody settings", () =>
        {
            var (_, bay, item, _) = Cargo();
            var state = BeginPlacement(bay);
            item.objectInHands = false;
            Call("AfterPlacement", bay, state);
            item.objectInHands = true;
            item.rb!.isKinematic = true;
            item.rb.useGravity = false;
            Call("AfterPickup", item);
            Assert(item.rb.isKinematic && !item.rb.useGravity, "Postfix overwrote the native hand body's state with its previous cargo state.");
        });
        Run("Cleanup after native rack placement preserves its new body state", () =>
        {
            var (mod, bay, item, child) = Cargo();
            var state = BeginPlacement(bay);
            item.objectInHands = false;
            Call("AfterPlacement", bay, state);
            item.isOnTrolley = false;
            item.currentRackPosition = new GameObject().AddComponent<RackPosition>();
            item.rb!.isKinematic = true;
            item.rb.useGravity = false;
            Time.realtimeSinceStartup = 0.55f;
            mod.OnUpdate();
            Assert(item.rb.isKinematic && !item.rb.useGravity, "Maintenance restored obsolete body flags over native rack placement.");
            Assert(child.enabled, "Early removal discarded pending collider restoration.");
        });
        Run("Placement failure restores captured colliders and preserves the exception", () =>
        {
            var (_, bay, item, child) = Cargo();
            var state = BeginPlacement(bay);
            var failure = new InvalidOperationException("Simulated native placement failure.");
            object? returned = Call("PlacementFailed", bay, state, failure);
            Assert(ReferenceEquals(failure, returned), "Finalizer must return the exact native exception.");
            Assert(child.enabled && item.GetComponent<Collider>()!.enabled, "Failed placement left captured colliders disabled.");
        });
        Run("Placement failure keeps already loaded cargo registered", () =>
        {
            var (_, bay, item, child) = Cargo();
            var state = BeginPlacement(bay);
            item.objectInHands = false;
            item.isOnTrolley = true;
            var failure = new InvalidOperationException("Failure after first item was loaded.");
            Assert(ReferenceEquals(failure, Call("PlacementFailed", bay, state, failure)), "Original failure must remain visible.");
            Assert(child.enabled, "Partial placement discarded collider restoration.");
            Assert(!(bool)Call("KeepCargoKinematic", item)!, "Already placed item must remain in cargo physics tracking.");
        });
        Run("Parent coroutine and delayed maintenance keep cargo stable and accessible", () =>
        {
            var (mod, bay, item, child) = Cargo();
            var state = BeginPlacement(bay);
            item.objectInHands = false;
            Call("AfterPlacement", bay, state);
            item.rb!.isKinematic = false;
            item.rb.useGravity = true;
            Call("AfterParentStep", new TrolleyLoadingBay._ParentTheObjectWithDelay_d__10 { uo = item });
            Assert(item.rb.isKinematic && !item.rb.useGravity, "Coroutine step must restore cargo body protection.");
            item.rb.isKinematic = false;
            item.rb.useGravity = true;
            Call("AfterChassisCollidersEnabled");
            Assert(item.rb.isKinematic && !item.rb.useGravity, "Chassis re-enable callback must retain cargo protection.");
            Time.realtimeSinceStartup = 1;
            mod.OnUpdate();
            mod.OnUpdate();
            Assert(child.enabled && item.GetComponent<Collider>()!.enabled, "Finalized cargo must allow native raycast interaction.");
            Assert(item.transform.IsChildOf(bay.transform), "Cargo must follow the cart after native delayed parenting.");
        });
        Run("Begin load restores pending collider captures before replacing cargo", () =>
        {
            var (mod, bay, item, child) = Cargo();
            var state = BeginPlacement(bay);
            item.objectInHands = false;
            Call("AfterPlacement", bay, state);
            InstanceCall(mod, "BeginLoad");
            Assert(child.enabled, "New load phase cannot discard pending collider states.");
            Assert((bool)Call("KeepCargoKinematic", item)!, "Old cargo must leave the physics registry before replacement.");
        });
        Run("Rotated trolley rejects an AABB corner outside its actual loading box", () =>
        {
            var (_, bay, item, _) = Cargo();
            item.objectInHands = false;
            bay.transform.YawDegrees = 45;
            var trigger = bay.carController!.trolleyTrigger!.boxColliderTrigger!;
            trigger.size = new Vector3(2, 2, 4);
            item.transform.position = new Vector3(2, 0, 0);
            Assert(trigger.bounds.Contains(item.transform.position), "Fixture must lie inside the world AABB.");
            Assert(!new Bounds(trigger.center, trigger.size).Contains(trigger.transform.InverseTransformPoint(item.transform.position)), "Fixture must lie outside the oriented box.");
            Assert(!(bool)Call("IsLoadCandidate", item, bay)!, "World AABB captured a server beside the rotated trolley.");
        });
        Run("Loading volume remains playable when installed Bounds.Contains wrapper is invalid", () =>
        {
            var (_, bay, item, _) = Cargo();
            item.objectInHands = false;
            bay.transform.YawDegrees = 45;
            var trigger = bay.carController!.trolleyTrigger!.boxColliderTrigger!;
            trigger.center = new Vector3(0.3f, 0.2f, -0.4f);
            item.transform.position = trigger.transform.TransformPoint(new Vector3(0.5f, 0.2f, 0.5f));
            Bounds.SimulateInvalidNativeContains = true;
            try
            {
                Assert((bool)Call("IsLoadCandidate", item, bay)!, "Valid cargo cannot depend on the invalid native Contains wrapper.");
                item.transform.position = trigger.transform.TransformPoint(new Vector3(10, 0, 0));
                item.GetComponent<Collider>()!.WorldCenterOverride = trigger.transform.TransformPoint(new Vector3(0.5f, 0.2f, 0.5f));
                Assert((bool)Call("IsLoadCandidate", item, bay)!, "Collider-center recovery cannot depend on the invalid native Contains wrapper.");
                item.GetComponent<Collider>()!.WorldCenterOverride = trigger.transform.TransformPoint(new Vector3(10, 0, 0));
                Assert(!(bool)Call("IsLoadCandidate", item, bay)!, "Cargo outside the real box must remain excluded without native Contains.");
            }
            finally { Bounds.SimulateInvalidNativeContains = false; }
        });
        Run("Rotated trolley still accepts a point inside the real loading box", () =>
        {
            var (_, bay, item, _) = Cargo();
            item.objectInHands = false;
            bay.transform.YawDegrees = 45;
            var trigger = bay.carController!.trolleyTrigger!.boxColliderTrigger!;
            trigger.size = new Vector3(2, 2, 4);
            item.transform.position = trigger.transform.TransformPoint(new Vector3(0.5f, 0, 1));
            Assert((bool)Call("IsLoadCandidate", item, bay)!, "Point inside the oriented loading box should recover.");
        });
        Run("Disabled trigger still provides its authored loading volume", () =>
        {
            var (_, bay, item, _) = Cargo();
            item.objectInHands = false;
            item.transform.position = new Vector3(0.4f, 0, 0);
            var trigger = bay.carController!.trolleyTrigger!.boxColliderTrigger!;
            trigger.enabled = false;
            Assert(!trigger.bounds.Contains(item.transform.position), "Disabled collider fixture must expose empty world bounds.");
            Assert((bool)Call("IsLoadCandidate", item, bay)!, "Parked cart's disabled trigger lost its authored loading volume.");
        });
        Run("Disabled item collider's empty world bounds cannot capture a distant object", () =>
        {
            var (_, bay, item, _) = Cargo();
            item.objectInHands = false;
            item.transform.position = new Vector3(10, 0, 0);
            item.GetComponent<Collider>()!.enabled = false;
            Assert(item.GetComponent<Collider>()!.bounds.Contains(Vector3.zero), "Fixture must match empty native bounds at world origin.");
            Assert(!(bool)Call("IsLoadCandidate", item, bay)!, "Empty disabled collider bounds captured a server far outside the trolley.");
        });
        Run("Visible collider center can recover an item whose model pivot lies outside", () =>
        {
            var (_, bay, item, _) = Cargo();
            item.objectInHands = false;
            item.transform.position = new Vector3(10, 0, 0);
            item.GetComponent<Collider>()!.WorldCenterOverride = new Vector3(0.4f, 0, 0);
            Assert((bool)Call("IsLoadCandidate", item, bay)!, "Valid visible cargo must not be lost only because its model pivot differs.");
        });
        Run("A boxed SFP module never becomes independent trolley cargo", () =>
        {
            var (_, bay, _, _) = Cargo();
            var box = new GameObject().AddComponent<SFPBox>();
            var module = new GameObject().AddComponent<SFPModule>();
            module.isInTheBox = true;
            module.transform.SetParent(box.transform);
            Assert((bool)Call("IsLoadCandidate", box, bay)!, "Its container is valid cargo in the loading volume.");
            Assert(!(bool)Call("IsLoadCandidate", module, bay)!, "Boxed module was classified as an independent load item.");
        });
        Run("Box membership flag excludes a module even before its parent is restored", () =>
        {
            var (_, bay, _, _) = Cargo();
            var module = new GameObject().AddComponent<SFPModule>();
            module.isInTheBox = true;
            Assert(!(bool)Call("IsLoadCandidate", module, bay)!, "Box flag must exclude modules with temporarily missing parent hierarchy.");
        });
        Run("A nested usable child remains attached to its cargo container", () =>
        {
            var (_, bay, _, _) = Cargo();
            var container = new GameObject().AddComponent<UsableObject>();
            var child = new GameObject().AddComponent<UsableObject>();
            child.transform.SetParent(container.transform);
            Assert(!(bool)Call("IsLoadCandidate", child, bay)!, "Nested cargo would be detached from its container during recovery.");
        });
        Run("Save load rebuilds independent slots only after network loading is complete", () =>
        {
            var (mod, bay, item, _) = Cargo();
            item.objectInHands = false;
            item.sizeInU = 2;
            item.trolleySlotIndex = 50;
            var other = new GameObject().AddComponent<UsableObject>();
            other.sizeInU = 3;
            other.trolleySlotIndex = 3;
            other.gameObject.AddComponent<Collider>();
            InstanceCall(mod, "BeginLoad");
            Call("AfterObjectLoaded", item);
            Call("AfterObjectLoaded", other);
            Assert(item.isOnTrolley && other.isOnTrolley, "Objects loaded inside the cart must be protected promptly.");
            InstanceCall(mod, "FinishLoad");
            WaypointInitializationSystem.Instance = new WaypointInitializationSystem { isLoading = true };
            Time.realtimeSinceStartup = 0.8f;
            mod.OnUpdate();
            Assert(item.trolleySlotIndex == 50, "Recovery must wait for native network loading to finish.");
            WaypointInitializationSystem.Instance.isLoading = false;
            Time.realtimeSinceStartup = 1;
            mod.OnUpdate();
            Assert(other.trolleySlotIndex == 0 && item.trolleySlotIndex == 3, "Loaded objects must receive separate contiguous logical slots.");
            Assert(item.storedPosition == 3 && other.storedPosition == 0, "Native pickup metadata must match the rebuilt slots.");
            Assert(bay.usedPositions.Count(value => value != 0) == 5, "Exactly the five loaded units must be reserved.");
            Assert(item.transform.IsChildOf(bay.transform) && other.transform.IsChildOf(bay.transform), "Recovered objects must follow the cart.");
            var later = new GameObject().AddComponent<UsableObject>();
            Call("AfterObjectLoaded", later);
            Assert(!later.isOnTrolley, "Recovery window must close after completion.");
        });
        Run("Recovery failure schedules a retry while maintaining existing cargo", () =>
        {
            var (mod, bay, item, _) = Cargo();
            item.objectInHands = false;
            InstanceCall(mod, "BeginLoad");
            Call("AfterObjectLoaded", item);
            bay.positionsOnTrolley = new Il2CppReferenceArray<Transform>(0);
            bay.usedPositions = new Il2CppStructArray<int>(0);
            InstanceCall(mod, "FinishLoad");
            Time.realtimeSinceStartup = 0.8f;
            mod.OnUpdate();
            Assert(MelonLoader.MelonLogger.Errors.Count > 0, "Recovery failure must be reported.");
            Assert(item.rb!.isKinematic && !item.rb.useGravity, "Recovery failure cannot skip maintenance of cargo already protected.");
            var retryAt = (float)typeof(InfiniteTrolleyMod).GetField("_recoverAt", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(mod)!;
            Assert(retryAt >= 1.8f, "Recovery retry must not busy-spin every maintenance tick.");
        });
        Run("Hover capacity checks never enumerate held-object colliders", () =>
        {
            var (_, bay, _, _) = Cargo();
            Call("BayReady", bay);
            GameObject.ColliderDiscoveryCalls = 0;
            for (var index = 0; index < 60; index++) Call("PrepareCapacity", bay);
            Console.WriteLine($"MEASURE 60 hover frames: {GameObject.ColliderDiscoveryCalls} collider traversals.");
            Assert(GameObject.ColliderDiscoveryCalls == 0, $"Hover performed {GameObject.ColliderDiscoveryCalls} collider tree traversals in sixty frames.");
        });
        Run("Fatal native recovery failure stops global rescans while preserving protected cargo", () =>
        {
            var (mod, _, item, _) = Cargo();
            item.objectInHands = false;
            InstanceCall(mod, "BeginLoad");
            Call("AfterObjectLoaded", item);
            InstanceCall(mod, "FinishLoad");
            UnityEngine.Object.DiscoveryFailure = new InvalidProgramException("Broken native discovery wrapper.");
            try
            {
                for (var index = 0; index < 20; index++)
                {
                    Time.realtimeSinceStartup = 0.8f + index;
                    mod.OnUpdate();
                }
                Console.WriteLine($"MEASURE fatal recovery: {UnityEngine.Object.GlobalDiscoveryCalls} scene scans in 20 attempts.");
                Assert(UnityEngine.Object.GlobalDiscoveryCalls == 1, $"Fatal native failure caused {UnityEngine.Object.GlobalDiscoveryCalls} scene scans.");
                Assert(item.rb!.isKinematic && !item.rb.useGravity, "Fatal recovery cannot release existing cargo protection.");
            }
            finally { UnityEngine.Object.DiscoveryFailure = null; }
        });
        Run("Incomplete native slot initialization backs off instead of scanning the scene every second", () =>
        {
            var (mod, bay, _, _) = Cargo();
            InstanceCall(mod, "BeginLoad");
            bay.positionsOnTrolley = new Il2CppReferenceArray<Transform>(0);
            bay.usedPositions = new Il2CppStructArray<int>(0);
            InstanceCall(mod, "FinishLoad");
            for (var index = 0; index < 40; index++)
            {
                Time.realtimeSinceStartup = 0.8f + index;
                mod.OnUpdate();
            }
            Console.WriteLine($"MEASURE unready trolley: {UnityEngine.Object.GlobalDiscoveryCalls} scene scans in 40 seconds.");
            Assert(UnityEngine.Object.GlobalDiscoveryCalls <= 6, $"Unready trolley triggered {UnityEngine.Object.GlobalDiscoveryCalls} scene scans in forty seconds.");
        });
        Run("Racked and held servers skip geometry while legitimate loose cargo is recovered", () =>
        {
            var (_, bay, item, _) = Cargo();
            Transform.GeometryCalls = 0;
            item.objectInHands = true;
            Assert(!(bool)Call("IsLoadCandidate", item, bay)!, "Held server must be excluded.");
            item.objectInHands = false;
            item.rackPositionUID = 42;
            Assert(!(bool)Call("IsLoadCandidate", item, bay)!, "Server with a saved rack identity must be excluded.");
            item.rackPositionUID = 0;
            item.currentRackPosition = new RackPosition();
            Assert(!(bool)Call("IsLoadCandidate", item, bay)!, "Server mounted in a rack must be excluded.");
            Assert(Transform.GeometryCalls == 0, $"Excluded servers triggered {Transform.GeometryCalls} geometry calls.");
            item.currentRackPosition = null;
            Assert((bool)Call("IsLoadCandidate", item, bay)!, "Loose cargo inside the loading box must remain eligible.");
            Assert(Transform.GeometryCalls > 0, "Loose cargo must still undergo the geometry check.");
        });
        Console.WriteLine($"{_passed} passed, {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }

    private static (InfiniteTrolleyMod Mod, TrolleyLoadingBay Bay, UsableObject Item, Collider Child) Cargo()
    {
        UnityEngine.Object.ResetRegistry();
        UnityEngine.Object.GlobalDiscoveryCalls = 0;
        UnityEngine.Object.DiscoveryFailure = null;
        MelonLoader.MelonLogger.Errors.Clear();
        WaypointInitializationSystem.Instance = null;
        Time.realtimeSinceStartup = 0;
        var mod = new InfiniteTrolleyMod();
        typeof(InfiniteTrolleyMod).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, mod);
        var trolley = new GameObject();
        var bay = trolley.AddComponent<TrolleyLoadingBay>();
        bay.positionsOnTrolley = new Il2CppReferenceArray<Transform>(Enumerable.Range(0, 8).Select(_ => new GameObject().transform).ToArray());
        bay.usedPositions = new Il2CppStructArray<int>(8);
        bay.carController = trolley.AddComponent<CarController>();
        bay.carController.trolleyTrigger = trolley.AddComponent<TrolleyTrigger>();
        var trigger = trolley.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(2, 2, 4);
        bay.carController.trolleyTrigger.boxColliderTrigger = trigger;
        var item = new GameObject().AddComponent<UsableObject>();
        item.rb = item.gameObject.AddComponent<Rigidbody>();
        item.gameObject.AddComponent<Collider>();
        item.objectInHands = true;
        var child = new GameObject().AddComponent<Collider>();
        child.transform.SetParent(item.transform);
        PlayerManager.instance = new PlayerManager { objectInHandGO = new[] { item.gameObject } };
        MainGameManager.instance = new MainGameManager { trolleyLoadingBay = bay };
        return (mod, bay, item, child);
    }

    private static object BeginPlacement(TrolleyLoadingBay bay)
    {
        object?[] args = { bay, null };
        Invoke("BeforePlacement", args);
        return args[1]!;
    }

    private static object? Call(string name, params object?[] args) => Invoke(name, args);
    private static object? InstanceCall(InfiniteTrolleyMod mod, string name) => typeof(InfiniteTrolleyMod).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(mod, null);
    private static object? Invoke(string name, object?[] args)
    {
        var method = typeof(InfiniteTrolleyMod).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(typeof(InfiniteTrolleyMod).FullName, name);
        try { return method.Invoke(null, args); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    private static void Run(string name, Action body)
    {
        try { body(); _passed++; Console.WriteLine($"PASS {name}"); }
        catch (Exception error) { _failed++; Console.WriteLine($"FAIL {name}: {error.Message}"); }
    }
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
