using System.Collections;
using System.Reflection;

namespace HarmonyLib
{
    public sealed class Harmony
    {
        public List<(MethodInfo Method, HarmonyMethod? Finalizer)> Patches { get; } = new();
        public bool Unpatched;
        public void Patch(MethodInfo method, HarmonyMethod? prefix = null, HarmonyMethod? postfix = null, HarmonyMethod? finalizer = null) => Patches.Add((method, finalizer));
        public void UnpatchSelf() => Unpatched = true;
    }
    public sealed class HarmonyMethod { public string Name { get; } public HarmonyMethod(Type type, string name) => Name = name; }
    public static class AccessTools
    {
        public static bool FailLookup;
        public static MethodInfo? Method(Type type, string name) => FailLookup ? null : type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
    }
}

namespace MelonLoader
{
    public abstract class MelonMod
    {
        public HarmonyLib.Harmony HarmonyInstance { get; } = new();
        public virtual void OnInitializeMelon() { }
        public virtual void OnUpdate() { }
    }
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class MelonInfoAttribute : Attribute
    {
        public MelonInfoAttribute(Type type, string name, string version, string author) { }
    }
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class MelonGameAttribute : Attribute
    {
        public MelonGameAttribute(string developer, string name) { }
    }
    public static class MelonLogger
    {
        public static List<string> Errors { get; } = new();
        public static void Msg(string message) { }
        public static void Error(string message) => Errors.Add(message);
    }
}

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public sealed class Il2CppReferenceArray<T> : IEnumerable<T> where T : class
    {
        private readonly T[] _items;
        public Il2CppReferenceArray(long length) => _items = new T[checked((int)length)];
        public Il2CppReferenceArray(T[] items) => _items = (T[])items.Clone();
        public int Length => _items.Length;
        public T this[int index] { get => _items[index]; set => _items[index] = value; }
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public static implicit operator Il2CppReferenceArray<T>(T[] items) => new(items);
    }
    public sealed class Il2CppStructArray<T> : IEnumerable<T> where T : struct
    {
        private readonly T[] _items;
        public Il2CppStructArray(long length) => _items = new T[checked((int)length)];
        public Il2CppStructArray(T[] items) => _items = (T[])items.Clone();
        public int Length => _items.Length;
        public T this[int index] { get => _items[index]; set => _items[index] = value; }
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public static implicit operator Il2CppStructArray<T>(T[] items) => new(items);
    }
}

namespace UnityEngine
{
    public class Object
    {
        private static int _nextId;
        private static readonly List<Object> Registry = new();
        private readonly int _id = ++_nextId;
        protected Object() => Registry.Add(this);
        public int GetInstanceID() => _id;
        public T? TryCast<T>() where T : class => this as T;
        public static int GlobalDiscoveryCalls;
        public static Exception? DiscoveryFailure;
        public static T[] FindObjectsOfType<T>() where T : Object
        {
            GlobalDiscoveryCalls++;
            if (DiscoveryFailure != null) throw DiscoveryFailure;
            return Registry.OfType<T>().ToArray();
        }
        public static void ResetRegistry() => Registry.Clear();
        public static implicit operator bool(Object? value) => value is not null;
    }
    public sealed class GameObject : Object
    {
        private readonly List<Component> _components = new();
        public static int ColliderDiscoveryCalls;
        public GameObject() { transform = new Transform(); Attach(transform); }
        public Transform transform { get; }
        private void Attach(Component component) { component.gameObject = this; _components.Add(component); }
        public T AddComponent<T>() where T : Component, new() { var result = new T(); Attach(result); return result; }
        public T? GetComponent<T>() where T : Component => _components.OfType<T>().FirstOrDefault();
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : Component
        {
            if (typeof(T) == typeof(Collider)) ColliderDiscoveryCalls++;
            return _components.OfType<T>().Concat(transform.Children.SelectMany(child => child.gameObject.GetComponentsInChildren<T>(includeInactive))).ToArray();
        }
    }
    public class Component : Object
    {
        public GameObject gameObject { get; internal set; } = null!;
        public Transform transform => gameObject.transform;
        public T? GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : Component => gameObject.GetComponentsInChildren<T>(includeInactive);
    }
    public sealed class Transform : Component
    {
        public static int GeometryCalls;
        public Vector3 position;
        public float YawDegrees;
        public Transform? parent { get; private set; }
        public List<Transform> Children { get; } = new();
        public Transform root => parent?.root ?? this;
        public bool IsChildOf(Transform ancestor) => ReferenceEquals(this, ancestor) || (parent?.IsChildOf(ancestor) ?? false);
        public void SetParent(Transform? target, bool worldPositionStays = true)
        {
            parent?.Children.Remove(this);
            parent = target;
            parent?.Children.Add(this);
        }
        public Vector3 InverseTransformPoint(Vector3 point)
        {
            GeometryCalls++;
            var difference = point - position;
            var angle = -YawDegrees * MathF.PI / 180f;
            return new Vector3(MathF.Cos(angle) * difference.x + MathF.Sin(angle) * difference.z,
                difference.y, -MathF.Sin(angle) * difference.x + MathF.Cos(angle) * difference.z);
        }
        public Vector3 TransformPoint(Vector3 point)
        {
            var angle = YawDegrees * MathF.PI / 180f;
            return position + new Vector3(MathF.Cos(angle) * point.x + MathF.Sin(angle) * point.z,
                point.y, -MathF.Sin(angle) * point.x + MathF.Cos(angle) * point.z);
        }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new(0, 0, 0);
        public static Vector3 operator +(Vector3 left, Vector3 right) => new(left.x + right.x, left.y + right.y, left.z + right.z);
        public static Vector3 operator -(Vector3 left, Vector3 right) => new(left.x - right.x, left.y - right.y, left.z - right.z);
    }
    public struct Bounds
    {
        public static bool SimulateInvalidNativeContains;
        public Vector3 center, size;
        public Bounds(Vector3 center, Vector3 size) { this.center = center; this.size = size; }
        public bool Contains(Vector3 point)
        {
            if (SimulateInvalidNativeContains)
                throw new InvalidProgramException("The installed IL2CPP Bounds.Internal_Contains wrapper is invalid.");
            return MathF.Abs(point.x - center.x) <= size.x / 2 &&
                MathF.Abs(point.y - center.y) <= size.y / 2 && MathF.Abs(point.z - center.z) <= size.z / 2;
        }
    }
    public class Collider : Component
    {
        public bool enabled = true;
        public bool isTrigger;
        public Vector3? WorldCenterOverride;
        public virtual Bounds bounds => enabled ? new(WorldCenterOverride ?? transform.position, new Vector3(0.1f, 0.1f, 0.1f)) : new(Vector3.zero, Vector3.zero);
        public Rigidbody? attachedRigidbody
        {
            get
            {
                for (Transform? current = transform; current != null; current = current.parent)
                {
                    var body = current.GetComponent<Rigidbody>();
                    if (body != null) return body;
                }
                return null;
            }
        }
    }
    public sealed class BoxCollider : Collider
    {
        public Vector3 center = Vector3.zero;
        public Vector3 size = new(1, 1, 1);
        public override Bounds bounds
        {
            get
            {
                if (!enabled) return new Bounds(Vector3.zero, Vector3.zero);
                var angle = transform.YawDegrees * MathF.PI / 180f;
                return new Bounds(transform.TransformPoint(center), new Vector3(
                    MathF.Abs(MathF.Cos(angle)) * size.x + MathF.Abs(MathF.Sin(angle)) * size.z,
                    size.y, MathF.Abs(MathF.Sin(angle)) * size.x + MathF.Abs(MathF.Cos(angle)) * size.z));
            }
        }
    }
    public sealed class Rigidbody : Component
    {
        public bool isKinematic;
        public bool useGravity = true;
        public Vector3 linearVelocity, angularVelocity;
    }
    public static class Physics
    {
        private static readonly Dictionary<(int, int), bool> Pairs = new();
        public static bool GetIgnoreCollision(Collider first, Collider second) => Pairs.GetValueOrDefault((first.GetInstanceID(), second.GetInstanceID()));
        public static void IgnoreCollision(Collider first, Collider second, bool value) => Pairs[(first.GetInstanceID(), second.GetInstanceID())] = value;
    }
    public static class Time { public static float realtimeSinceStartup; }
    public struct RaycastHit { }
}

namespace Il2Cpp
{
    using Il2CppInterop.Runtime.InteropTypes.Arrays;
    using UnityEngine;
    public class Interact : Component { public virtual void OnLoad(object? data = null) { } }
    public class UsableObject : Interact
    {
        public bool objectInHands, isOnTrolley;
        public int sizeInU = 1, rackPositionUID = -1, trolleySlotIndex, storedPosition;
        public RackPosition? currentRackPosition;
        public Rigidbody? rb;
        public virtual void InteractOnClick() { }
        public virtual void MoveToHand() { }
        public void ApplyFixedUpdatePhysics() { }
        public void RestoreRigidbody() => rb = GetComponent<Rigidbody>() ?? gameObject.AddComponent<Rigidbody>();
    }
    public sealed class SFPModule : UsableObject { public bool isInTheBox; }
    public sealed class SFPBox : UsableObject { }
    public sealed class RackPosition : Component { }
    public sealed class TrolleyLoadingBay : Interact
    {
        public Il2CppReferenceArray<Transform> positionsOnTrolley = null!;
        public Il2CppStructArray<int> usedPositions = null!;
        public Il2CppPolyStang.CarController? carController;
        public void Start() { }
        public void ResetAllSlots() { }
        public void InteractOnHover(RaycastHit hit) { }
        public void InteractOnClick() { }
        public sealed class _ParentTheObjectWithDelay_d__10 { public UsableObject? uo; public bool MoveNext() => false; }
    }
    public sealed class TrolleyTrigger : Component { public BoxCollider? boxColliderTrigger; }
    public sealed class MainGameManager { public static MainGameManager? instance; public TrolleyLoadingBay? trolleyLoadingBay; }
    public sealed class PlayerManager { public static PlayerManager? instance; public GameObject[]? objectInHandGO; }
    public sealed class WaypointInitializationSystem { public static WaypointInitializationSystem? Instance; public bool isLoading; }
    public static class SaveSystem
    {
        public sealed class OnLoadingData
        {
            private readonly Action _callback;
            private OnLoadingData(Action callback) => _callback = callback;
            public static implicit operator OnLoadingData(Action callback) => new(callback);
            public static OnLoadingData operator +(OnLoadingData? first, OnLoadingData second) => new(first == null ? second._callback : first._callback + second._callback);
        }
        public sealed class OnLoadingDataLater
        {
            private readonly Action _callback;
            private OnLoadingDataLater(Action callback) => _callback = callback;
            public static implicit operator OnLoadingDataLater(Action callback) => new(callback);
            public static OnLoadingDataLater operator +(OnLoadingDataLater? first, OnLoadingDataLater second) => new(first == null ? second._callback : first._callback + second._callback);
        }
        public static OnLoadingData? onLoadingData;
        public static OnLoadingDataLater? onLoadingDataLater;
    }
}

namespace Il2CppPolyStang
{
    public sealed class CarController : UnityEngine.Component
    {
        public UnityEngine.Rigidbody? carRb;
        public Il2Cpp.TrolleyTrigger? trolleyTrigger;
        public void TurnBackOnCollidersInTRolley() { }
    }
}
