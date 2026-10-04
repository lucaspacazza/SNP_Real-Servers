using Il2Cpp;
using InfiniteTrolley.Runtime;
using UnityEngine;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        Run("Stored dynamic cargo becomes motionless, kinematic and gravity-free", () =>
        {
            var f = Fixture.Create();
            var body = f.Item.rb!;
            body.linearVelocity = new Vector3(12, -3, 4);
            body.angularVelocity = new Vector3(1, 2, 3);
            f.Physics.Hold(f.Item, f.Bay);
            Assert(body.isKinematic && !body.useGravity);
            Equal(Vector3.zero, body.linearVelocity);
            Equal(Vector3.zero, body.angularVelocity);
            Equal(0, body.VelocityWritesWhileKinematic);
            Assert(f.Physics.Contains(f.Item));
        });
        Run("Stored cargo ignores chassis collision and remains clickable", () =>
        {
            var f = Fixture.Create();
            var disabled = f.Item.gameObject.AddComponent(new Collider { enabled = false });
            f.Physics.Hold(f.Item, f.Bay);
            Assert(UnityEngine.Physics.GetIgnoreCollision(f.ItemCollider, f.ChassisCollider));
            Assert(f.ItemCollider.enabled);
            Assert(!disabled.enabled);
        });
        Run("Other cargo sharing the trolley hierarchy is not mistaken for chassis", () =>
        {
            var f = Fixture.Create();
            var otherCargo = new GameObject();
            otherCargo.transform.parent = f.Bay.transform.root;
            otherCargo.AddComponent<Rigidbody>();
            var otherCollider = otherCargo.AddComponent<Collider>();
            f.Physics.Hold(f.Item, f.Bay);
            Assert(!UnityEngine.Physics.GetIgnoreCollision(f.ItemCollider, otherCollider));
        });
        Run("Triggers are excluded from physical collision isolation", () =>
        {
            var f = Fixture.Create();
            var chassisTrigger = f.ChassisCollider.gameObject.AddComponent(new Collider { isTrigger = true });
            var cargoTrigger = f.Item.gameObject.AddComponent(new Collider { isTrigger = true });
            f.Physics.Hold(f.Item, f.Bay);
            Assert(!UnityEngine.Physics.GetIgnoreCollision(f.ItemCollider, chassisTrigger));
            Assert(!UnityEngine.Physics.GetIgnoreCollision(cargoTrigger, f.ChassisCollider));
        });
        Run("Isolation includes cargo colliders nested below its root", () =>
        {
            var f = Fixture.Create();
            var child = new GameObject();
            child.transform.parent = f.Item.transform;
            var childCollider = child.AddComponent<Collider>();
            f.Physics.Hold(f.Item, f.Bay);
            Assert(UnityEngine.Physics.GetIgnoreCollision(childCollider, f.ChassisCollider));
            f.Physics.Release(f.Item);
            Assert(!UnityEngine.Physics.GetIgnoreCollision(childCollider, f.ChassisCollider));
        });
        Run("Release preserves pre-existing ignored pairs", () =>
        {
            var f = Fixture.Create();
            UnityEngine.Physics.IgnoreCollision(f.ItemCollider, f.ChassisCollider, true);
            f.Physics.Hold(f.Item, f.Bay);
            f.Physics.Release(f.Item);
            Assert(UnityEngine.Physics.GetIgnoreCollision(f.ItemCollider, f.ChassisCollider));
            Assert(!f.Physics.Contains(f.Item));
        });
        Run("Native collider reactivation does not transfer ownership of pre-existing ignored pairs", () =>
        {
            var f = Fixture.Create();
            UnityEngine.Physics.IgnoreCollision(f.ItemCollider, f.ChassisCollider, true);
            f.Physics.Hold(f.Item, f.Bay);
            UnityEngine.Physics.ForgetIgnorePair(f.ItemCollider, f.ChassisCollider);
            f.Physics.Hold(f.Item, f.Bay);
            Assert(UnityEngine.Physics.GetIgnoreCollision(f.ItemCollider, f.ChassisCollider));
            f.Physics.Release(f.Item);
            Assert(UnityEngine.Physics.GetIgnoreCollision(f.ItemCollider, f.ChassisCollider));
        });
        Run("Release restores the original body flags and mod-owned collision pairs", () =>
        {
            var f = Fixture.Create();
            f.Physics.Hold(f.Item, f.Bay);
            f.Physics.Release(f.Item);
            Assert(!f.Item.rb!.isKinematic && f.Item.rb.useGravity);
            Assert(!UnityEngine.Physics.GetIgnoreCollision(f.ItemCollider, f.ChassisCollider));
            Assert(f.ItemCollider.enabled);
            Assert(!f.Physics.Contains(f.Item));
        });
        Run("ReleaseToHand preserves the body's newly configured native hand flags", () =>
        {
            var f = Fixture.Create();
            f.Physics.Hold(f.Item, f.Bay);
            // The native pickup has already established its own hand state.
            f.Item.rb!.isKinematic = true;
            f.Item.rb.useGravity = false;
            f.Physics.ReleaseToHand(f.Item);
            Assert(f.Item.rb.isKinematic && !f.Item.rb.useGravity);
            Assert(!UnityEngine.Physics.GetIgnoreCollision(f.ItemCollider, f.ChassisCollider));
            Assert(!f.Physics.Contains(f.Item));
        });
        Run("Originally kinematic gravity-free cargo retains its flags on release", () =>
        {
            var f = Fixture.Create();
            f.Item.rb!.isKinematic = true;
            f.Item.rb.useGravity = false;
            f.Physics.Hold(f.Item, f.Bay);
            f.Physics.Release(f.Item);
            Assert(f.Item.rb.isKinematic && !f.Item.rb.useGravity);
        });
        Run("A missing cargo-owned Rigidbody is restored instead of using the chassis body", () =>
        {
            var f = Fixture.Create(withBody: false);
            f.Item.transform.parent = f.Bay.transform.root;
            f.Physics.Hold(f.Item, f.Bay);
            Equal(1, f.Item.RestoreCalls);
            var body = f.Item.GetComponent<Rigidbody>();
            Assert(body is not null && body.isKinematic && !body.useGravity);
            Assert(!ReferenceEquals(body, f.Bay.carController.carRb));
            f.Physics.Release(f.Item);
            Assert(ReferenceEquals(body, f.Item.GetComponent<Rigidbody>()));
        });
        Run("A stale item.rb reference cannot normalize the trolley Rigidbody", () =>
        {
            var f = Fixture.Create();
            var ownBody = f.Item.GetComponent<Rigidbody>()!;
            f.Item.rb = f.Bay.carController.carRb;
            f.Physics.Hold(f.Item, f.Bay);
            Assert(ownBody.isKinematic && !ownBody.useGravity);
            Assert(!f.Bay.carController.carRb.isKinematic && f.Bay.carController.carRb.useGravity);
        });
        Run("Repeated Hold keeps the original flags until Release", () =>
        {
            var f = Fixture.Create();
            f.Physics.Hold(f.Item, f.Bay);
            f.Physics.Hold(f.Item, f.Bay);
            f.Physics.Release(f.Item);
            Assert(!f.Item.rb!.isKinematic && f.Item.rb.useGravity);
            Assert(!UnityEngine.Physics.GetIgnoreCollision(f.ItemCollider, f.ChassisCollider));
        });
        Run("Repeated Hold reapplies isolation after native collider reactivation", () =>
        {
            var f = Fixture.Create();
            f.Physics.Hold(f.Item, f.Bay);
            UnityEngine.Physics.ForgetIgnorePair(f.ItemCollider, f.ChassisCollider);
            f.Physics.Hold(f.Item, f.Bay);
            Assert(UnityEngine.Physics.GetIgnoreCollision(f.ItemCollider, f.ChassisCollider));
            f.Physics.Release(f.Item);
            Assert(!UnityEngine.Physics.GetIgnoreCollision(f.ItemCollider, f.ChassisCollider));
        });
        Run("A replacement Rigidbody does not receive the old body's snapshot on Release", () =>
        {
            var f = Fixture.Create();
            var original = f.Item.rb!;
            f.Physics.Hold(f.Item, f.Bay);
            f.Item.gameObject.RemoveComponent(original);
            var replacement = f.Item.gameObject.AddComponent(new Rigidbody { isKinematic = true, useGravity = false });
            f.Item.rb = replacement;
            f.Physics.Release(f.Item);
            Assert(replacement.isKinematic && !replacement.useGravity);
            Assert(ReferenceEquals(replacement, f.Item.GetComponent<Rigidbody>()));
        });
        Run("Release of untracked cargo leaves its body unchanged", () =>
        {
            var f = Fixture.Create();
            f.Physics.Release(f.Item);
            Assert(!f.Item.rb!.isKinematic && f.Item.rb.useGravity);
            Equal(0, UnityEngine.Physics.IgnoreCalls);
        });
        Run("Clear restores all tracked cargo without changing the chassis", () =>
        {
            var f = Fixture.Create();
            var second = NewItem(withBody: true);
            var secondCollider = second.GetComponent<Collider>()!;
            f.Physics.Hold(f.Item, f.Bay);
            f.Physics.Hold(second, f.Bay);
            f.Physics.Clear();
            Assert(!f.Physics.Contains(f.Item) && !f.Physics.Contains(second));
            Assert(!f.Item.rb!.isKinematic && f.Item.rb.useGravity);
            Assert(!second.rb!.isKinematic && second.rb.useGravity);
            Assert(!UnityEngine.Physics.GetIgnoreCollision(f.ItemCollider, f.ChassisCollider));
            Assert(!UnityEngine.Physics.GetIgnoreCollision(secondCollider, f.ChassisCollider));
            Assert(!f.Bay.carController.carRb.isKinematic && f.Bay.carController.carRb.useGravity);
            f.Physics.Clear();
        });
        Run("A failed native restoration still creates an isolated cargo body", () =>
        {
            var f = Fixture.Create(withBody: false);
            f.Item.RestorationSucceeds = false;
            f.Item.transform.SetParent(f.Bay.transform);
            Assert(ReferenceEquals(f.ItemCollider.attachedRigidbody, f.Bay.carController.carRb));
            f.Physics.Hold(f.Item, f.Bay);
            var ownBody = f.Item.GetComponent<Rigidbody>();
            Assert(ownBody is not null && ReferenceEquals(ownBody, f.Item.rb));
            Assert(ReferenceEquals(f.ItemCollider.attachedRigidbody, ownBody));
            Assert(ownBody!.isKinematic && !ownBody.useGravity);
            Assert(f.Physics.Contains(f.Item));
            Assert(!f.Bay.carController.carRb.isKinematic && f.Bay.carController.carRb.useGravity);
            f.Physics.Release(f.Item);
            Assert(!ownBody.isKinematic && ownBody.useGravity);
        });

        Console.WriteLine($"Physics: {_passed} passed; {_failed} failed. These tests use Unity/IL2CPP fakes, not an in-game physics simulation.");
        return _failed == 0 ? 0 : 1;
    }

    private static UsableObject NewItem(bool withBody)
    {
        var go = new GameObject();
        var item = go.AddComponent<UsableObject>();
        if (withBody) item.rb = go.AddComponent<Rigidbody>();
        go.AddComponent<Collider>();
        return item;
    }

    private sealed record Fixture(CargoPhysics Physics, UsableObject Item, TrolleyLoadingBay Bay,
        Collider ItemCollider, Collider ChassisCollider)
    {
        internal static Fixture Create(bool withBody = true)
        {
            var chassis = new GameObject();
            var carBody = chassis.AddComponent<Rigidbody>();
            var controller = chassis.AddComponent(new Il2Cpp.PolyStang.CarController { carRb = carBody });
            var chassisCollider = chassis.AddComponent<Collider>();
            var loadingBay = new GameObject();
            loadingBay.transform.parent = chassis.transform;
            var bay = loadingBay.AddComponent(new TrolleyLoadingBay { carController = controller });
            var item = NewItem(withBody);
            return new Fixture(new CargoPhysics(), item, bay, item.GetComponent<Collider>()!, chassisCollider);
        }
    }

    private static void Run(string name, Action test)
    {
        UnityEngine.Physics.Reset();
        try { test(); _passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { _failed++; Console.Error.WriteLine("FAIL " + name + ": " + error.Message); }
    }

    private static void Assert(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Expected condition was false.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}
