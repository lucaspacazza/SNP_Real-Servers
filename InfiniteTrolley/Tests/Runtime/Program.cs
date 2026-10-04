using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using InfiniteTrolley.Runtime;
using UnityEngine;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        Run("Missing trolley is rejected", () => Assert(!NativeSlotExpander.Ensure(null!, 1)));
        Run("Missing anchors are rejected without changing reservations", () =>
        {
            var bay = new TrolleyLoadingBay { usedPositions = new[] { 1, 2, 3 } };
            var originalUsed = bay.usedPositions;
            Assert(!NativeSlotExpander.Ensure(bay, 1));
            Same(originalUsed, bay.usedPositions);
        });
        Run("Missing reservations are rejected without changing anchors", () =>
        {
            var bay = new TrolleyLoadingBay { positionsOnTrolley = Anchors(4) };
            var originalPositions = bay.positionsOnTrolley;
            Assert(!NativeSlotExpander.Ensure(bay, 1));
            Same(originalPositions, bay.positionsOnTrolley);
        });
        Run("No native anchor slots cannot invent a loading location", () =>
        {
            var bay = Bay(0);
            Assert(!NativeSlotExpander.Ensure(bay, 100));
            Equal(0, bay.positionsOnTrolley!.Length);
            Equal(0, bay.usedPositions!.Length);
        });
        Run("Empty reservations are rejected safely", () =>
        {
            var bay = Bay(4, Array.Empty<int>());
            Assert(!NativeSlotExpander.Ensure(bay, 1));
            Equal(4, bay.positionsOnTrolley!.Length);
            Equal(0, bay.usedPositions!.Length);
        });
        Run("A missing individual anchor cannot create an invalid native slot", () =>
        {
            var bay = Bay(4);
            bay.positionsOnTrolley![2] = null!;
            var positions = bay.positionsOnTrolley;
            var used = bay.usedPositions;
            Assert(!NativeSlotExpander.Ensure(bay, 100));
            Same(positions, bay.positionsOnTrolley);
            Same(used, bay.usedPositions);
        });
        Run("Empty trolley is prepared with matching arrays and free logical slots", () =>
        {
            var bay = Bay(8);
            var nativeAnchors = bay.positionsOnTrolley!.ToArray();
            Assert(NativeSlotExpander.Ensure(bay, 1));
            MatchingArrays(bay);
            Assert(bay.usedPositions!.Length >= 64);
            Assert(bay.usedPositions.All(value => value == 0));
            AnchorCycle(bay, nativeAnchors);
        });
        Run("Capacity for 100 units keeps every existing reservation", () =>
        {
            var values = Enumerable.Range(1, 8).ToArray();
            var bay = Bay(8, values);
            var nativeAnchors = bay.positionsOnTrolley!.ToArray();
            Assert(NativeSlotExpander.Ensure(bay, 100));
            MatchingArrays(bay);
            Assert(FindFree(bay.usedPositions!, 100) >= 8);
            PrefixReservations(bay, values);
            AnchorCycle(bay, nativeAnchors);
        });
        Run("Capacity for 1000 units shares the existing visual locations", () =>
        {
            var bay = Bay(8, Enumerable.Repeat(1, 8).ToArray());
            var nativeAnchors = bay.positionsOnTrolley!.ToArray();
            Assert(NativeSlotExpander.Ensure(bay, 1000));
            MatchingArrays(bay);
            Assert(FindFree(bay.usedPositions!, 1000) >= 8);
            Equal(8, bay.positionsOnTrolley!.Distinct().Count());
            AnchorCycle(bay, nativeAnchors);
        });
        Run("A full trolley grows repeatedly without losing previous objects", () =>
        {
            var bay = Bay(8);
            var nativeAnchors = bay.positionsOnTrolley!.ToArray();
            Assert(NativeSlotExpander.Ensure(bay, 64));
            Reserve(bay, 64, 71);
            int firstLength = bay.usedPositions!.Length;
            Assert(NativeSlotExpander.Ensure(bay, 100));
            Assert(bay.usedPositions.Length > firstLength);
            Reserve(bay, 100, 72);
            int secondLength = bay.usedPositions.Length;
            Assert(NativeSlotExpander.Ensure(bay, 1000));
            Assert(bay.usedPositions.Length > secondLength);
            Equal(64, bay.usedPositions.Count(value => value == 71));
            Equal(100, bay.usedPositions!.Count(value => value == 72));
            Assert(FindFree(bay.usedPositions, 1000) >= 0);
            AnchorCycle(bay, nativeAnchors);
        });
        Run("Uneven anchor counts keep using only original visual locations after growth", () =>
        {
            var bay = Bay(3);
            var nativeAnchors = bay.positionsOnTrolley!.ToArray();
            Assert(NativeSlotExpander.Ensure(bay, 64));
            Reserve(bay, 64, 31);
            Assert(NativeSlotExpander.Ensure(bay, 100));
            Reserve(bay, 100, 32);
            Assert(NativeSlotExpander.Ensure(bay, 1000));
            Assert(bay.positionsOnTrolley!.All(anchor => nativeAnchors.Contains(anchor)));
            Equal(64, bay.usedPositions!.Count(value => value == 31));
            Equal(100, bay.usedPositions!.Count(value => value == 32));
        });
        Run("Ready arrays keep their identity when they already have enough space", () =>
        {
            var bay = Bay(64);
            bay.usedPositions![0] = 42;
            var positions = bay.positionsOnTrolley;
            var used = bay.usedPositions;
            Assert(NativeSlotExpander.Ensure(bay, 3));
            Same(positions, bay.positionsOnTrolley);
            Same(used, bay.usedPositions);
            Equal(42, bay.usedPositions![0]);
        });
        Run("Short reservation array is padded without erasing occupied slots", () =>
        {
            var values = new[] { 17, 0, -3, 41 };
            var bay = Bay(8, values);
            var nativeAnchors = bay.positionsOnTrolley!.ToArray();
            Assert(NativeSlotExpander.Ensure(bay, 6));
            MatchingArrays(bay);
            PrefixReservations(bay, values);
            Assert(bay.usedPositions!.Skip(values.Length).All(value => value == 0));
            AnchorCycle(bay, nativeAnchors);
        });
        Run("Long reservation array retains entries beyond the original anchor count", () =>
        {
            var values = new[] { 11, 12, 0, 0, 55, 56, 57, 58, 59, 60, 61, 62 };
            var bay = Bay(4, values);
            var nativeAnchors = bay.positionsOnTrolley!.ToArray();
            Assert(NativeSlotExpander.Ensure(bay, 100));
            MatchingArrays(bay);
            PrefixReservations(bay, values);
            Assert(FindFree(bay.usedPositions!, 100) >= values.Length);
            AnchorCycle(bay, nativeAnchors);
        });
        Run("Fragmented free slots do not substitute for a contiguous object reservation", () =>
        {
            var values = Enumerable.Range(0, 64).Select(i => i % 2 == 0 ? 0 : 9).ToArray();
            var bay = Bay(64, values);
            Assert(NativeSlotExpander.Ensure(bay, 3));
            Assert(bay.usedPositions!.Length > 64);
            Assert(FindFree(bay.usedPositions!, 3) >= 64);
            PrefixReservations(bay, values);
        });
        Run("Objects sharing a visual anchor retain independent reservations", () =>
        {
            var bay = Bay(4);
            Assert(NativeSlotExpander.Ensure(bay, 100));
            int first = Reserve(bay, 3, 101);
            int second = Reserve(bay, 3, 102);
            Assert(second >= first + 3);
            for (int i = first; i < first + 3; i++) bay.usedPositions![i] = 0;
            Equal(3, bay.usedPositions!.Count(value => value == 102));
            Assert(NativeSlotExpander.Ensure(bay, 3));
            Equal(first, FindFree(bay.usedPositions!, 3));
            Equal(3, bay.usedPositions!.Count(value => value == 102));
        });
        Run("Preparing one trolley cannot share reservations with another", () =>
        {
            var first = Bay(4);
            var second = Bay(4);
            Assert(NativeSlotExpander.Ensure(first, 100));
            Assert(NativeSlotExpander.Ensure(second, 100));
            first.usedPositions![5] = 99;
            Equal(0, second.usedPositions![5]);
            Assert(!ReferenceEquals(first.usedPositions, second.usedPositions));
        });

        Console.WriteLine($"{_passed} passed, {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }

    private static TrolleyLoadingBay Bay(int anchorCount, int[]? reservations = null) => new()
    {
        positionsOnTrolley = Anchors(anchorCount),
        usedPositions = new Il2CppStructArray<int>(reservations ?? new int[anchorCount])
    };

    private static Il2CppReferenceArray<Transform> Anchors(int count) =>
        new(Enumerable.Range(0, count).Select(i => new Transform($"native-{i}")).ToArray());

    private static void MatchingArrays(TrolleyLoadingBay bay) => Equal(bay.positionsOnTrolley!.Length, bay.usedPositions!.Length);

    private static void AnchorCycle(TrolleyLoadingBay bay, Transform[] originals)
    {
        for (int i = 0; i < bay.positionsOnTrolley!.Length; i++) Same(originals[i % originals.Length], bay.positionsOnTrolley[i]);
    }

    private static void PrefixReservations(TrolleyLoadingBay bay, int[] values)
    {
        for (int i = 0; i < values.Length; i++) Equal(values[i], bay.usedPositions![i]);
    }

    private static int Reserve(TrolleyLoadingBay bay, int size, int reservation)
    {
        int start = FindFree(bay.usedPositions!, size);
        Assert(start >= 0, $"No contiguous free reservation for {size} units.");
        for (int i = start; i < start + size; i++) bay.usedPositions![i] = reservation;
        return start;
    }

    private static int FindFree(Il2CppStructArray<int> values, int size)
    {
        int run = 0;
        for (int i = 0; i < values.Length; i++)
        {
            run = values[i] == 0 ? run + 1 : 0;
            if (run >= size) return i - size + 1;
        }
        return -1;
    }

    private static void Run(string name, Action body)
    {
        try { body(); _passed++; Console.WriteLine($"PASS {name}"); }
        catch (Exception error) { _failed++; Console.WriteLine($"FAIL {name}: {error.Message}"); }
    }

    private static void Assert(bool condition, string message = "Expected condition to hold.")
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    private static void Same(object? expected, object? actual) => Assert(ReferenceEquals(expected, actual), "Object identity changed unexpectedly.");
}
