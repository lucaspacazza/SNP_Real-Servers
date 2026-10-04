using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace InfiniteTrolley.Runtime;

internal static class NativeSlotExpander
{
    internal static bool Ensure(TrolleyLoadingBay bay, int incomingUnits)
    {
        if (bay == null)
            return false;

        var oldPositions = bay.positionsOnTrolley;
        var oldUsed = bay.usedPositions;
        if (oldPositions == null || oldUsed == null || oldPositions.Length == 0 || oldUsed.Length == 0)
            return false;
        for (var i = 0; i < oldPositions.Length; i++)
            if (oldPositions[i] == null)
                return false;

        // The native array can be read directly. Hover calls this repeatedly;
        // copying every reservation into a managed array creates avoidable GC.
        var occupied = new NativeReservations(oldUsed, Math.Max(oldPositions.Length, oldUsed.Length));
        var length = SlotCapacity.RequiredLength(occupied, incomingUnits);
        if (oldPositions.Length == length && oldUsed.Length == length)
            return true;

        var positions = new Il2CppReferenceArray<Transform>(length);
        var used = new Il2CppStructArray<int>(length);
        for (var i = 0; i < length; i++)
        {
            positions[i] = oldPositions[SlotCapacity.VisualIndex(i, oldPositions.Length)];
            if (i < occupied.Count)
                used[i] = occupied[i];
        }

        // Construct both arrays before publishing either: existing reservations
        // and object slot IDs survive growth even though visual anchors repeat.
        bay.positionsOnTrolley = positions;
        bay.usedPositions = used;
        return true;
    }

    private sealed class NativeReservations : IReadOnlyList<int>
    {
        private readonly Il2CppStructArray<int> _items;
        internal NativeReservations(Il2CppStructArray<int> items, int length)
        {
            _items = items;
            Count = length;
        }
        public int Count { get; }
        public int this[int index] => index < _items.Length ? _items[index] : 0;
        public IEnumerator<int> GetEnumerator()
        {
            for (var index = 0; index < Count; index++) yield return this[index];
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
