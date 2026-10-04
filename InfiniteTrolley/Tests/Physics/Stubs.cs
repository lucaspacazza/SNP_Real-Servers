namespace UnityEngine
{
    public class Object
    {
        private static int _nextId;
        private readonly int _id = ++_nextId;
        public int GetInstanceID() => _id;
        public static implicit operator bool(Object? value) => value is not null;
    }

    public class Component : Object
    {
        public GameObject gameObject { get; internal set; } = null!;
        public virtual Transform transform => gameObject.transform;
        public T? GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component =>
            gameObject.GetComponentsInChildren<T>(includeInactive);
    }

    public sealed class GameObject : Object
    {
        private readonly List<Component> _components = new();
        public GameObject()
        {
            transform = new Transform { gameObject = this };
            _components.Add(transform);
        }
        public Transform transform { get; }
        public T AddComponent<T>() where T : Component, new() => AddComponent(new T());
        public T AddComponent<T>(T component) where T : Component
        {
            component.gameObject = this;
            _components.Add(component);
            return component;
        }
        public void RemoveComponent(Component component) => _components.Remove(component);
        public T? GetComponent<T>() where T : Component => _components.OfType<T>().FirstOrDefault();
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component =>
            _components.OfType<T>().Concat(transform.Children.SelectMany(child =>
                child.gameObject.GetComponentsInChildren<T>(includeInactive))).ToArray();
    }

    public sealed class Transform : Component
    {
        private Transform? _parent;
        public readonly List<Transform> Children = new();
        public override Transform transform => this;
        public Transform root => _parent?.root ?? this;
        public Transform? parent
        {
            get => _parent;
            set
            {
                _parent?.Children.Remove(this);
                _parent = value;
                value?.Children.Add(this);
            }
        }
        public void SetParent(Transform? value, bool worldPositionStays = true) => parent = value;
        public bool IsChildOf(Transform ancestor) => ReferenceEquals(this, ancestor) ||
            (_parent?.IsChildOf(ancestor) ?? false);
    }

    public readonly record struct Vector3(float x, float y, float z)
    {
        public static Vector3 zero => new(0, 0, 0);
    }

    public sealed class Rigidbody : Component
    {
        private Vector3 _linearVelocity;
        private Vector3 _angularVelocity;
        public bool isKinematic;
        public bool useGravity = true;
        public int VelocityWritesWhileKinematic;
        public Vector3 linearVelocity
        {
            get => _linearVelocity;
            set { if (isKinematic) VelocityWritesWhileKinematic++; _linearVelocity = value; }
        }
        public Vector3 angularVelocity
        {
            get => _angularVelocity;
            set { if (isKinematic) VelocityWritesWhileKinematic++; _angularVelocity = value; }
        }
    }

    public sealed class Collider : Component
    {
        public static int AttachedBodyReads;
        public bool enabled = true;
        public bool isTrigger;
        public Rigidbody? attachedRigidbody
        {
            get
            {
                AttachedBodyReads++;
                for (Transform? node = transform; node is not null; node = node.parent)
                    if (node.gameObject.GetComponent<Rigidbody>() is { } body) return body;
                return null;
            }
        }
    }

    public static class Physics
    {
        private static readonly HashSet<(int, int)> Ignored = new();
        public static int IgnoreCalls;
        private static (int, int) Key(Collider first, Collider second)
        {
            int a = first.GetInstanceID(), b = second.GetInstanceID();
            return a <= b ? (a, b) : (b, a);
        }
        public static bool GetIgnoreCollision(Collider first, Collider second) => Ignored.Contains(Key(first, second));
        public static void IgnoreCollision(Collider first, Collider second, bool ignore = true)
        {
            IgnoreCalls++;
            if (ignore) Ignored.Add(Key(first, second)); else Ignored.Remove(Key(first, second));
        }
        public static void ForgetIgnorePair(Collider first, Collider second) => Ignored.Remove(Key(first, second));
        public static void Reset() { Ignored.Clear(); IgnoreCalls = 0; }
        public static void SyncTransforms() { }
    }
}

namespace Il2Cpp.PolyStang
{
    public sealed class CarController : UnityEngine.Component
    {
        public UnityEngine.Rigidbody carRb = null!;
    }
}

namespace Il2Cpp
{
    public sealed class TrolleyLoadingBay : UnityEngine.Component
    {
        public PolyStang.CarController carController = null!;
    }

    public sealed class UsableObject : UnityEngine.Component
    {
        public UnityEngine.Rigidbody? rb;
        public int RestoreCalls;
        public bool RestorationSucceeds = true;
        public void RestoreRigidbody()
        {
            RestoreCalls++;
            if (RestorationSucceeds)
                rb = GetComponent<UnityEngine.Rigidbody>() ?? gameObject.AddComponent<UnityEngine.Rigidbody>();
        }
    }
}
