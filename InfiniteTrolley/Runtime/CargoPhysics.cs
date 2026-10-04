using Il2Cpp;
using UnityEngine;

namespace InfiniteTrolley.Runtime;

internal sealed class CargoPhysics
{
    private sealed record ChassisState(TrolleyLoadingBay Bay, Rigidbody Body, Transform Root, Collider[] Colliders);

    private sealed class BodyState
    {
        internal readonly UsableObject Item;
        internal Rigidbody Body;
        internal bool WasKinematic;
        internal bool UsedGravity;
        internal readonly Dictionary<(int, int), (Collider Cargo, Collider Chassis, bool Owned)> Pairs = new();

        internal BodyState(UsableObject item, Rigidbody body)
        {
            Item = item;
            Body = body;
            WasKinematic = body.isKinematic;
            UsedGravity = body.useGravity;
        }
    }

    private readonly Dictionary<int, BodyState> _items = new();
    private readonly Dictionary<int, ChassisState> _chassis = new();

    internal void InvalidateChassis(TrolleyLoadingBay bay)
    {
        if (bay != null)
            _chassis.Remove(bay.GetInstanceID());
    }

    private Collider[] ChassisColliders(TrolleyLoadingBay bay, Rigidbody body)
    {
        var id = bay.GetInstanceID();
        var root = bay.transform.root;
        if (_chassis.TryGetValue(id, out var cached) && cached.Bay != null &&
            cached.Body == body && cached.Root == root)
            return cached.Colliders;

        var colliders = new List<Collider>();
        foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            if (collider != null && !collider.isTrigger && collider.attachedRigidbody == body)
                colliders.Add(collider);
        var result = colliders.ToArray();
        _chassis[id] = new ChassisState(bay, body, root, result);
        return result;
    }

    internal bool Contains(UsableObject item) => item != null && _items.ContainsKey(item.GetInstanceID());

    internal void Hold(UsableObject item, TrolleyLoadingBay bay)
    {
        if (item == null || bay == null)
            return;

        // GetComponent, rather than item.rb, proves this body belongs to the
        // cargo itself. A missing body would attach its shapes to the chassis.
        var body = item.GetComponent<Rigidbody>();
        if (body == null)
        {
            item.RestoreRigidbody();
            body = item.GetComponent<Rigidbody>();
            // Native restoration may trust a stale item.rb reference. Never
            // let overlapping cargo shapes fall back to the chassis body.
            if (body == null)
                body = item.gameObject.AddComponent<Rigidbody>();
        }
        if (body == null)
            return;
        item.rb = body;

        var id = item.GetInstanceID();
        if (!_items.TryGetValue(id, out var state))
            _items[id] = state = new BodyState(item, body);
        else if (state.Body != body)
        {
            state.Body = body;
            state.WasKinematic = body.isKinematic;
            state.UsedGravity = body.useGravity;
        }

        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        body.isKinematic = true;
        body.useGravity = false;

        var car = bay.carController;
        var chassisBody = car != null ? car.carRb : null;
        if (chassisBody == null)
            return;

        // Native collider reactivation explicitly invalidates this cache. Its
        // contents exclude cargo bodies, so N cargo objects do not each walk
        // the complete hierarchy containing those same N objects.
        var chassis = ChassisColliders(bay, chassisBody);
        foreach (var cargoCollider in item.GetComponentsInChildren<Collider>(true))
        {
            if (cargoCollider == null || cargoCollider.isTrigger)
                continue;
            foreach (var chassisCollider in chassis)
            {
                if (chassisCollider == null || chassisCollider.isTrigger || chassisCollider.attachedRigidbody != chassisBody)
                    continue;

                var key = (cargoCollider.GetInstanceID(), chassisCollider.GetInstanceID());
                var ignored = Physics.GetIgnoreCollision(cargoCollider, chassisCollider);
                state.Pairs.TryAdd(key, (cargoCollider, chassisCollider, !ignored));
                if (!ignored)
                {
                    Physics.IgnoreCollision(cargoCollider, chassisCollider, true);
                }
            }
        }
    }

    internal void Release(UsableObject item)
    {
        if (item != null)
            ReleaseById(item.GetInstanceID());
    }

    internal void ReleaseToHand(UsableObject item)
    {
        if (item != null)
            ReleaseById(item.GetInstanceID(), restoreBody: false);
    }

    internal void ReleaseById(int id, bool restoreBody = true)
    {
        if (_items.Remove(id, out var state))
            Restore(state, restoreBody);
    }

    internal void Clear()
    {
        foreach (var state in _items.Values)
            Restore(state);
        _items.Clear();
        _chassis.Clear();
    }

    private static void Restore(BodyState state, bool restoreBody = true)
    {
        // Respect collision exclusions made by the game or another mod before
        // this cargo was registered; restore only pairs changed by this mod.
        foreach (var pair in state.Pairs.Values)
        {
            if (pair.Cargo != null && pair.Chassis != null)
                Physics.IgnoreCollision(pair.Cargo, pair.Chassis, !pair.Owned);
        }
        // Pickup has already configured the body for the player's hand. Only
        // release our collision exclusions in that path, preserving its result.
        if (restoreBody && state.Item != null && state.Body != null && state.Item.GetComponent<Rigidbody>() == state.Body)
        {
            state.Body.isKinematic = state.WasKinematic;
            state.Body.useGravity = state.UsedGravity;
        }
    }
}
