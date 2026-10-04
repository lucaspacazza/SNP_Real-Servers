using System;

namespace DataCenterAutomatorServers;

internal static class RuntimeOwnershipPolicy
{
    internal const int MaximumPendingPurchases = 1000;

    public static bool AllowNativePower(bool isOwnedCarrier, bool isMounted) => !isOwnedCarrier || isMounted;

    public static bool CanRestoreRack(int rackPositionUid, bool inHands, bool onTrolley) =>
        rackPositionUid > 0 && !inHands && !onTrolley;

    public static bool ShouldRetryLoadedRacks(int pendingCount, DateTime now, DateTime until) => pendingCount > 0 && now <= until;

    public static bool CanCaptureIdentity(bool validScene, bool cachedPrefab) => validScene && !cachedPrefab;

    public static bool IsMatchingRackReservation(int savedRackPositionUid, int currentRackPositionUid) =>
        savedRackPositionUid > 0 && savedRackPositionUid == currentRackPositionUid;

    public static bool RackRangesOverlap(int firstIndex, int firstUnits, int secondIndex, int secondUnits) =>
        (long)firstIndex < (long)secondIndex + Math.Max(1, secondUnits) &&
        (long)secondIndex < (long)firstIndex + Math.Max(1, firstUnits);
}
