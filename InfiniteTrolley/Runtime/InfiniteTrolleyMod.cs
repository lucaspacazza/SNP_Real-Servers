using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPolyStang;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(InfiniteTrolley.Runtime.InfiniteTrolleyMod), "Infinite Trolley", "0.1.1", "Lucas SNP")]
[assembly: MelonGame("Waseku", "Data Center")]

namespace InfiniteTrolley.Runtime;

public sealed class InfiniteTrolleyMod : MelonMod
{
    private sealed record ColliderState(Collider Collider, bool Enabled);
    private sealed record HeldItem(UsableObject Item, ColliderState[] Colliders);
    private sealed class CargoEntry
    {
        internal readonly UsableObject Item;
        internal readonly TrolleyLoadingBay Bay;
        internal readonly ColliderState[] Colliders;
        internal readonly float ReadyAt;
        internal bool Finalized;

        internal CargoEntry(UsableObject item, TrolleyLoadingBay bay, ColliderState[] colliders)
        {
            Item = item;
            Bay = bay;
            Colliders = colliders;
            ReadyAt = Time.realtimeSinceStartup + 0.65f;
        }
    }

    private static InfiniteTrolleyMod? _instance;
    private readonly Dictionary<int, CargoEntry> _cargo = new();
    private readonly List<KeyValuePair<int, CargoEntry>> _maintenanceCargo = new();
    private readonly CargoPhysics _physics = new();
    private SaveSystem.OnLoadingData? _loadingHandler;
    private SaveSystem.OnLoadingDataLater? _loadedHandler;
    private bool _restoring;
    private bool _recoverPending;
    private float _recoverAt;
    private int _recoverFailures;
    private float _nextMaintenance;
    private float _nextError;

    public override void OnInitializeMelon()
    {
        _instance = this;
        try
        {
            Patch(typeof(TrolleyLoadingBay), nameof(TrolleyLoadingBay.Start), postfix: nameof(BayReady));
            Patch(typeof(TrolleyLoadingBay), nameof(TrolleyLoadingBay.ResetAllSlots), postfix: nameof(BayReady));
            Patch(typeof(TrolleyLoadingBay), nameof(TrolleyLoadingBay.InteractOnHover), prefix: nameof(PrepareCapacity));
            Patch(typeof(TrolleyLoadingBay), nameof(TrolleyLoadingBay.InteractOnClick), prefix: nameof(BeforePlacement), postfix: nameof(AfterPlacement), finalizer: nameof(PlacementFailed));
            Patch(typeof(TrolleyLoadingBay._ParentTheObjectWithDelay_d__10), "MoveNext", postfix: nameof(AfterParentStep));
            Patch(typeof(UsableObject), nameof(UsableObject.InteractOnClick), postfix: nameof(AfterPickup));
            Patch(typeof(UsableObject), nameof(UsableObject.MoveToHand), postfix: nameof(AfterPickup));
            Patch(typeof(UsableObject), nameof(UsableObject.ApplyFixedUpdatePhysics), prefix: nameof(KeepCargoKinematic));
            Patch(typeof(CarController), nameof(CarController.TurnBackOnCollidersInTRolley), postfix: nameof(AfterChassisCollidersEnabled));
            Patch(typeof(Interact), nameof(Interact.OnLoad), postfix: nameof(AfterObjectLoaded));

            _loadingHandler = (Action)BeginLoad;
            _loadedHandler = (Action)FinishLoad;
            SaveSystem.onLoadingData += _loadingHandler;
            SaveSystem.onLoadingDataLater += _loadedHandler;
            MelonLogger.Msg("Infinite Trolley 0.1.1: capacidade dinâmica, posições sobrepostas e objetos separados.");
        }
        catch
        {
            HarmonyInstance.UnpatchSelf();
            throw;
        }
    }

    private void Patch(Type type, string method, string? prefix = null, string? postfix = null, string? finalizer = null)
    {
        var target = AccessTools.Method(type, method) ?? throw new MissingMethodException(type.FullName, method);
        HarmonyInstance.Patch(target,
            prefix: prefix == null ? null : new HarmonyMethod(typeof(InfiniteTrolleyMod), prefix),
            postfix: postfix == null ? null : new HarmonyMethod(typeof(InfiniteTrolleyMod), postfix),
            finalizer: finalizer == null ? null : new HarmonyMethod(typeof(InfiniteTrolleyMod), finalizer));
    }

    private static TrolleyLoadingBay? Bay => MainGameManager.instance != null ? MainGameManager.instance.trolleyLoadingBay : null;

    private static HeldItem[] HeldItems()
    {
        var player = PlayerManager.instance;
        var result = new List<HeldItem>();
        if (player == null || player.objectInHandGO == null)
            return result.ToArray();
        foreach (var go in player.objectInHandGO)
        {
            if (go == null)
                continue;
            var item = go.GetComponent<UsableObject>();
            if (item == null || !item.objectInHands)
                continue;
            var colliders = new List<ColliderState>();
            foreach (var collider in item.GetComponentsInChildren<Collider>(true))
                if (collider != null)
                    colliders.Add(new ColliderState(collider, collider.enabled));
            result.Add(new HeldItem(item, colliders.ToArray()));
        }
        return result.ToArray();
    }

    private static int Units(UsableObject item) => Math.Max(1, item.sizeInU);

    private static int HeldUnits()
    {
        var player = PlayerManager.instance;
        if (player == null || player.objectInHandGO == null)
            return 0;
        var units = 0;
        foreach (var go in player.objectInHandGO)
        {
            if (go == null)
                continue;
            var item = go.GetComponent<UsableObject>();
            if (item != null && item.objectInHands)
                units = checked(units + Units(item));
        }
        return units;
    }

    private static void BayReady(TrolleyLoadingBay __instance)
    {
        try { NativeSlotExpander.Ensure(__instance, 1); }
        catch (Exception error) { _instance?.Report(error); }
    }

    private static void PrepareCapacity(TrolleyLoadingBay __instance)
    {
        try
        {
            NativeSlotExpander.Ensure(__instance, Math.Max(1, HeldUnits()));
        }
        catch (Exception error) { _instance?.Report(error); }
    }

    private static void BeforePlacement(TrolleyLoadingBay __instance, out HeldItem[] __state)
    {
        __state = HeldItems();
        var units = 0;
        foreach (var held in __state)
            units = checked(units + Units(held.Item));
        if (!NativeSlotExpander.Ensure(__instance, Math.Max(1, units)))
            return;
        // Prevent a newly parented cargo collider becoming part of the moving
        // chassis before its own Rigidbody and exclusions are established.
        foreach (var held in __state)
            foreach (var state in held.Colliders)
                if (state.Collider != null)
                    state.Collider.enabled = false;
    }

    private static void AfterPlacement(TrolleyLoadingBay __instance, HeldItem[]? __state)
    {
        if (__state == null)
            return;
        foreach (var held in __state)
        {
            if (_instance != null && held.Item != null && !held.Item.objectInHands)
                _instance.Register(held.Item, __instance, held.Colliders);
            else
                RestoreColliders(held.Colliders);
        }
    }

    private static Exception? PlacementFailed(TrolleyLoadingBay __instance, HeldItem[]? __state, Exception? __exception)
    {
        if (__exception == null)
            return null;
        if (__state != null)
        {
            foreach (var held in __state)
            {
                RestoreColliders(held.Colliders);
                // If native placement got as far as releasing this object,
                // keep it stable while preserving the original exception.
                if (_instance != null && held.Item != null && !held.Item.objectInHands && held.Item.isOnTrolley)
                {
                    try { _instance.Register(held.Item, __instance, held.Colliders); }
                    catch (Exception error) { _instance.Report(error); }
                }
            }
        }
        _instance?.Report(__exception);
        return __exception;
    }

    private void Register(UsableObject item, TrolleyLoadingBay bay, ColliderState[]? colliders = null)
    {
        item.isOnTrolley = true;
        _cargo.TryAdd(item.GetInstanceID(), new CargoEntry(item, bay, colliders ?? Array.Empty<ColliderState>()));
        _physics.Hold(item, bay);
    }

    private static void AfterParentStep(TrolleyLoadingBay._ParentTheObjectWithDelay_d__10 __instance)
    {
        var item = __instance.uo;
        if (_instance != null && item != null && _instance._cargo.TryGetValue(item.GetInstanceID(), out var entry))
            _instance._physics.Hold(item, entry.Bay);
    }

    private static void AfterPickup(UsableObject __instance)
    {
        if (_instance != null && __instance != null && __instance.objectInHands)
        {
            if (_instance._cargo.Remove(__instance.GetInstanceID(), out var entry) && !entry.Finalized)
                RestoreColliders(entry.Colliders);
            _instance._physics.ReleaseToHand(__instance);
        }
    }

    private static bool KeepCargoKinematic(UsableObject __instance) =>
        _instance == null || !_instance._physics.Contains(__instance);

    private static void AfterChassisCollidersEnabled()
    {
        if (_instance == null)
            return;
        foreach (var entry in _instance._cargo.Values)
            if (entry.Bay != null)
                _instance._physics.InvalidateChassis(entry.Bay);
        foreach (var entry in _instance._cargo.Values)
            if (entry.Item != null && entry.Bay != null)
                _instance._physics.Hold(entry.Item, entry.Bay);
    }

    private void BeginLoad()
    {
        _restoring = true;
        _recoverPending = false;
        _recoverFailures = 0;
        foreach (var entry in _cargo.Values)
            if (!entry.Finalized)
                RestoreColliders(entry.Colliders);
        _physics.Clear();
        _cargo.Clear();
        _maintenanceCargo.Clear();
    }

    private void FinishLoad()
    {
        _recoverPending = true;
        _recoverFailures = 0;
        _recoverAt = Time.realtimeSinceStartup + 0.75f;
    }

    private static bool IsLoadCandidate(UsableObject item, TrolleyLoadingBay bay)
    {
        // These ownership exclusions do not depend on trolley geometry. A
        // large datacenter otherwise checks every racked server's transforms
        // only to reject it at the end of the same recovery policy.
        if (item.objectInHands || item.currentRackPosition != null || item.rackPositionUID > 0)
            return false;
        var sfp = item.TryCast<SFPModule>();
        var nested = sfp != null && sfp.isInTheBox;
        for (var parent = item.transform.parent; !nested && parent != null && parent != bay.transform; parent = parent.parent)
            nested = parent.GetComponent<UsableObject>() != null;
        var associated = item.isOnTrolley || item.transform.IsChildOf(bay.transform);
        var inside = false;
        var trigger = bay.carController != null && bay.carController.trolleyTrigger != null
            ? bay.carController.trolleyTrigger.boxColliderTrigger : null;
        if (trigger != null)
        {
            // The world AABB includes empty corners when the trolley rotates.
            // Use the actual box in local coordinates, even if disabled during load.
            inside = IsInsideLoadingBox(trigger, item.transform.position);
            var collider = item.GetComponent<Collider>();
            if (!inside && collider != null && collider.enabled)
                inside = IsInsideLoadingBox(trigger, collider.bounds.center);
        }
        return CargoRecoveryPolicy.ShouldRecover(item.objectInHands,
            item.currentRackPosition != null || item.rackPositionUID > 0,
            bay.transform.IsChildOf(item.transform), associated, inside, nested);
    }

    private static bool IsInsideLoadingBox(BoxCollider trigger, Vector3 worldPoint)
    {
        // Bounds.Contains calls a broken Internal_Contains wrapper in this
        // installed IL2CPP build. Read the authored box and compare its axes
        // in managed code, retaining the rotated/disabled trigger behavior.
        var point = trigger.transform.InverseTransformPoint(worldPoint);
        var center = trigger.center;
        var size = trigger.size;
        return Math.Abs(point.x - center.x) <= size.x * 0.5f &&
            Math.Abs(point.y - center.y) <= size.y * 0.5f &&
            Math.Abs(point.z - center.z) <= size.z * 0.5f;
    }

    private static void AfterObjectLoaded(Interact __instance)
    {
        if (_instance == null || !_instance._restoring)
            return;
        var item = __instance.TryCast<UsableObject>();
        var bay = Bay;
        if (item != null && bay != null && IsLoadCandidate(item, bay))
            _instance.Register(item, bay);
    }

    private void RecoverCargo(TrolleyLoadingBay bay)
    {
        // Do not scan a large datacenter while native cart initialization is
        // still incomplete. Keep protecting already registered cargo meanwhile.
        if (bay.positionsOnTrolley == null || bay.usedPositions == null ||
            bay.positionsOnTrolley.Length == 0 || bay.usedPositions.Length == 0)
            throw new InvalidOperationException("As posições nativas do carrinho ainda não foram inicializadas.");
        var items = new List<UsableObject>();
        var units = 0;
        foreach (var item in UnityEngine.Object.FindObjectsOfType<UsableObject>())
        {
            if (item != null && IsLoadCandidate(item, bay))
            {
                items.Add(item);
                units = checked(units + Units(item));
            }
        }
        if (!NativeSlotExpander.Ensure(bay, Math.Max(1, units)))
            throw new InvalidOperationException("Não foi possível preparar as posições do carrinho após carregar a partida.");

        // Slot numbers are transient; rebuild reservations from the actual
        // restored objects without duplicating, deleting or spawning inventory.
        bay.usedPositions = new Il2CppStructArray<int>(bay.positionsOnTrolley.Length);
        var nextSlot = 0;
        foreach (var item in items.OrderBy(item => item.trolleySlotIndex).ThenBy(item => item.GetInstanceID()))
        {
            var size = Units(item);
            item.trolleySlotIndex = nextSlot;
            item.storedPosition = nextSlot;
            for (var offset = 0; offset < size; offset++)
                bay.usedPositions[nextSlot + offset] = 1;
            nextSlot += size;
            item.transform.SetParent(bay.transform, true);
            Register(item, bay);
        }
        MelonLogger.Msg($"Carrinho: {items.Count} objetos recuperados; {bay.usedPositions.Length} posições lógicas.");
    }

    public override void OnUpdate()
    {
        var now = Time.realtimeSinceStartup;
        if (now < _nextMaintenance)
            return;
        _nextMaintenance = now + 0.1f;
        try
        {
            if (_recoverPending && now >= _recoverAt)
            {
                var bay = Bay;
                var system = WaypointInitializationSystem.Instance;
                if (bay != null && (system == null || !system.isLoading))
                {
                    try
                    {
                        RecoverCargo(bay);
                        _recoverPending = false;
                        _recoverFailures = 0;
                        _restoring = false;
                    }
                    catch (Exception error)
                    {
                        // A broken IL2CPP wrapper cannot recover by repeating a
                        // scene scan. Other initialization failures back off,
                        // while existing cargo continues to receive maintenance.
                        if (error is InvalidProgramException)
                        {
                            _recoverPending = false;
                            _restoring = false;
                        }
                        else
                        {
                            _recoverFailures = Math.Min(6, _recoverFailures + 1);
                            _recoverAt = now + Math.Min(30f, (float)Math.Pow(2, _recoverFailures - 1));
                        }
                        Report(error);
                    }
                }
            }
            _maintenanceCargo.Clear();
            _maintenanceCargo.AddRange(_cargo);
            foreach (var pair in _maintenanceCargo)
            {
                var entry = pair.Value;
                var item = entry.Item;
                if (item == null || entry.Bay == null || item.objectInHands || !item.isOnTrolley)
                {
                    if (!entry.Finalized)
                        RestoreColliders(entry.Colliders);
                    // Native hand/rack/drop logic has already prepared the body.
                    // Restore its former flags only if the cart disappeared
                    // while the object was still stored on it.
                    _physics.ReleaseById(pair.Key, restoreBody: item == null ||
                        (!item.objectInHands && item.isOnTrolley));
                    _cargo.Remove(pair.Key);
                    continue;
                }
                if (!entry.Finalized && now >= entry.ReadyAt)
                {
                    item.transform.SetParent(entry.Bay.transform, true);
                    RestoreColliders(entry.Colliders);
                    var collider = item.GetComponent<Collider>();
                    if (collider != null)
                        collider.enabled = true;
                    _physics.Hold(item, entry.Bay);
                    entry.Finalized = true;
                }
                else if (item.rb == null || !item.rb.isKinematic || item.rb.useGravity)
                    _physics.Hold(item, entry.Bay);
            }
        }
        catch (Exception error) { Report(error); }
    }

    private static void RestoreColliders(IEnumerable<ColliderState> colliders)
    {
        foreach (var state in colliders)
            if (state.Collider != null)
                state.Collider.enabled = state.Enabled;
    }

    private void Report(Exception error)
    {
        if (Time.realtimeSinceStartup < _nextError)
            return;
        _nextError = Time.realtimeSinceStartup + 5f;
        MelonLogger.Error("Infinite Trolley: " + error);
    }
}
