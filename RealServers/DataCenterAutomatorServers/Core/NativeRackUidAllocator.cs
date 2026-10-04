using System;
using System.Collections.Generic;

namespace DataCenterAutomatorServers;

internal static class NativeRackUidAllocator
{
    internal static int Reserve(Func<int> readNext, Action<int> writeNext,
        Func<int, bool> isRegistered, Action<int> assign, Func<int> verify,
        int maximumAttempts = 10000)
    {
        if (maximumAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        int candidate = Math.Max(1, readNext());
        for (int attempt = 0; attempt < maximumAttempts; attempt++)
        {
            if (candidate == int.MaxValue) throw new OverflowException("Native rack UID counter is exhausted.");
            if (!isRegistered(candidate))
            {
                // Match the native coroutine: register the slot, confirm it,
                // then advance the manager's next-ID counter.
                assign(candidate);
                if (verify() != candidate) throw new InvalidOperationException("Native rack UID registration was not confirmed.");
                writeNext(candidate + 1);
                return candidate;
            }
            candidate++;
        }
        throw new InvalidOperationException("No free native rack UID was found within the bounded search.");
    }

    internal static bool ShouldMark(int uid, IReadOnlyList<int> used, int index, int units)
    {
        if (uid <= 0 || index < 0 || units < 1 || (long)index + units > used.Count) return false;
        bool missing = false;
        for (int offset = 0; offset < units; offset++)
        {
            int existing = used[index + offset];
            if (existing > 0 && existing != uid) return false;
            missing |= existing != uid;
        }
        return missing;
    }
}
