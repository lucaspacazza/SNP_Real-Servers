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

        var occupied = new int[Math.Max(oldPositions.Length, oldUsed.Length)];
        for (var i = 0; i < oldUsed.Length; i++)
            occupied[i] = oldUsed[i];
        var length = SlotCapacity.RequiredLength(occupied, incomingUnits);
        if (oldPositions.Length == length && oldUsed.Length == length)
            return true;

        var positions = new Il2CppReferenceArray<Transform>(length);
        var used = new Il2CppStructArray<int>(length);
        for (var i = 0; i < length; i++)
        {
            positions[i] = oldPositions[SlotCapacity.VisualIndex(i, oldPositions.Length)];
            if (i < occupied.Length)
                used[i] = occupied[i];
        }

        // Construct both arrays before publishing either: existing reservations
        // and object slot IDs survive growth even though visual anchors repeat.
        bay.positionsOnTrolley = positions;
        bay.usedPositions = used;
        return true;
    }
}
