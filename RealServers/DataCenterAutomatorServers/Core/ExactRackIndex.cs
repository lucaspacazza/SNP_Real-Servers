using System;
using System.Collections.Generic;

namespace DataCenterAutomatorServers;

/// <summary>One lazy scene snapshot per restoration batch, keyed only by the saved rack UID.</summary>
internal sealed class ExactRackIndex<T> where T : class
{
    private readonly Func<IEnumerable<T>> _source;
    private readonly Func<T, int> _readUid;
    private readonly Func<T, bool> _isValid;
    private Dictionary<int, List<T>> _byUid = new Dictionary<int, List<T>>();
    private bool _initialized;
    private bool _refreshed;

    internal ExactRackIndex(Func<IEnumerable<T>> source, Func<T, int> readUid, Func<T, bool> isValid)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _readUid = readUid ?? throw new ArgumentNullException(nameof(readUid));
        _isValid = isValid ?? throw new ArgumentNullException(nameof(isValid));
    }

    internal IReadOnlyList<T> GetCandidates(int uid)
    {
        if (uid <= 0) return Array.Empty<T>();
        if (!_initialized) Rebuild();
        IReadOnlyList<T> candidates = LiveCandidates(uid);
        // A slot can be destroyed or created while the native loader completes.
        // Allow one fresh snapshot, never one scene scan for every missing server.
        if (candidates.Count == 0 && !_refreshed)
        {
            _refreshed = true;
            Rebuild();
            candidates = LiveCandidates(uid);
        }
        return candidates;
    }

    internal void Invalidate()
    {
        _byUid.Clear();
        _initialized = false;
        _refreshed = false;
    }

    private void Rebuild()
    {
        var rebuilt = new Dictionary<int, List<T>>();
        foreach (T item in _source())
        {
            if (item == null || !_isValid(item)) continue;
            int uid = _readUid(item);
            if (uid <= 0) continue;
            if (!rebuilt.TryGetValue(uid, out var candidates))
                rebuilt[uid] = candidates = new List<T>();
            candidates.Add(item);
        }
        _byUid = rebuilt;
        _initialized = true;
    }

    private IReadOnlyList<T> LiveCandidates(int uid)
    {
        if (!_byUid.TryGetValue(uid, out var stored)) return Array.Empty<T>();
        var result = new List<T>(stored.Count);
        foreach (T candidate in stored)
            if (_isValid(candidate) && _readUid(candidate) == uid) result.Add(candidate);
        return result.AsReadOnly();
    }
}
