using System;
using System.Collections.Generic;

namespace InfiniteTrolley;

/// <summary>Keeps logical inventory slots independent from reusable visual anchors.</summary>
public static class SlotCapacity
{
    /// <summary>Returns enough slots for one contiguous incoming object without changing occupants.</summary>
    public static int RequiredLength(IReadOnlyList<int> occupied, int incomingUnits, int minimumLength = 64)
    {
        if (occupied is null) throw new ArgumentNullException(nameof(occupied));
        if (incomingUnits <= 0) throw new ArgumentOutOfRangeException(nameof(incomingUnits));
        if (minimumLength <= 0) throw new ArgumentOutOfRangeException(nameof(minimumLength));

        var currentLength = occupied.Count;
        var trailingFree = 0;
        for (var index = 0; index < currentLength; index++)
        {
            trailingFree = occupied[index] == 0 ? trailingFree + 1 : 0;
            if (currentLength >= minimumLength && trailingFree >= incomingUnits)
                return currentLength;
        }

        // New zero-filled slots extend the existing trailing vacancy. Use wide
        // arithmetic before the caller allocates or mutates any inventory.
        var requiredLength = (long)currentLength + incomingUnits - trailingFree;
        if (requiredLength > int.MaxValue)
            throw new OverflowException("The incoming object exceeds the addressable inventory capacity.");

        var capacity = currentLength < minimumLength
            ? minimumLength
            : Math.Min((long)int.MaxValue, (long)currentLength * 2);
        while (capacity < requiredLength)
            capacity = Math.Min((long)int.MaxValue, capacity * 2);

        return (int)capacity;
    }

    /// <summary>Maps only the display position; logical inventory indices remain unchanged.</summary>
    public static int VisualIndex(int logicalIndex, int anchorCount)
    {
        if (logicalIndex < 0) throw new ArgumentOutOfRangeException(nameof(logicalIndex));
        if (anchorCount <= 0) throw new ArgumentOutOfRangeException(nameof(anchorCount));
        return logicalIndex % anchorCount;
    }
}
