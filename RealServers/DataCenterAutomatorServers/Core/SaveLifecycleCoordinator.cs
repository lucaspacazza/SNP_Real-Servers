using System;
using System.Collections.Generic;
using System.IO;

namespace DataCenterAutomatorServers;

internal sealed class SaveTransaction<TSnapshot>
{
    internal readonly string Directory;
    internal readonly string? RequestedName;
    internal readonly IReadOnlyDictionary<string, SaveFileStamp> Before;
    internal bool Closed;

    internal SaveTransaction(string directory, string? name, IReadOnlyDictionary<string, SaveFileStamp> before)
    {
        Directory = directory;
        RequestedName = name;
        Before = before;
    }
}

/// <summary>Save ordering and file selection, independent of the Unity runtime.</summary>
internal sealed class SaveLifecycleCoordinator<TSnapshot>
{
    private readonly Func<string> _directory;
    private readonly Func<TSnapshot> _capture;
    private readonly Action<string, string, TSnapshot> _commit;
    private readonly Action<string, string> _select;
    private readonly Action _recover;
    private readonly Func<string, string?>? _resolveRaw;
    private readonly HashSet<SaveTransaction<TSnapshot>> _activeTransactions = new();

    internal SaveLifecycleCoordinator(Func<string> directory, Func<TSnapshot> capture,
        Action<string, string, TSnapshot> commit, Action<string, string> select,
        Action recover, Func<string, string?>? resolveRaw = null)
    {
        _directory = directory;
        _capture = capture;
        _commit = commit;
        _select = select;
        _recover = recover;
        _resolveRaw = resolveRaw;
    }

    internal SaveTransaction<TSnapshot> PrepareSave(string? name)
    {
        // Register exact identities before native serialization starts. Capture
        // again after it returns because the native saver may finalize IDs.
        _capture();
        string directory = _directory();
        var transaction = new SaveTransaction<TSnapshot>(directory, name, SaveFileLocator.Capture(directory));
        _activeTransactions.Add(transaction);
        return transaction;
    }

    internal bool CaptureOutsideTransaction()
    {
        // SaveGame's prefix already registers exact IDs before native saving.
        // Keep the event fallback for native saves outside that hook, without
        // scanning the full inventory a third time for the usual save path.
        if (_activeTransactions.Count != 0)
            return false;
        _capture();
        return true;
    }

    internal bool CompleteSave(SaveTransaction<TSnapshot>? transaction)
    {
        if (transaction == null || transaction.Closed)
            return false;
        transaction.Closed = true;
        _activeTransactions.Remove(transaction);
        string? file = SaveFileLocator.ResolveWritten(transaction.Directory, transaction.RequestedName, transaction.Before, _resolveRaw);
        if (file == null)
            return false;
        _commit(Path.GetFileName(file), file, _capture());
        return true;
    }

    internal void CancelSave(SaveTransaction<TSnapshot>? transaction)
    {
        if (transaction != null)
        {
            transaction.Closed = true;
            _activeTransactions.Remove(transaction);
        }
    }

    internal void BeginLoad(string? name)
    {
        string? file = SaveFileLocator.ResolveExisting(_directory(), name, _resolveRaw);
        // Loading shared bytes or a new world has no file identity; clear the
        // preceding world's markers instead of silently borrowing that world.
        _select(file == null ? string.Empty : Path.GetFileName(file), file ?? string.Empty);
    }

    internal void CompleteLoad() => _recover();
}
