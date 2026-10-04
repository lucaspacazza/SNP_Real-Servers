using System.Collections;

namespace UnityEngine
{
    public class Object
    {
        public static implicit operator bool(Object? value) => value is not null;
    }

    public sealed class Transform : Object
    {
        public Transform(string name) => Name = name;
        public string Name { get; }
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

namespace Il2Cpp
{
    using Il2CppInterop.Runtime.InteropTypes.Arrays;
    using UnityEngine;

    public sealed class TrolleyLoadingBay : UnityEngine.Object
    {
        public Il2CppReferenceArray<Transform>? positionsOnTrolley;
        public Il2CppStructArray<int>? usedPositions;
    }
}
