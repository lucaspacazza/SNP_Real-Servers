using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DataCenterAutomatorServers;

internal sealed record SaveFileStamp(long Length, long LastWriteUtcTicks);

internal static class SaveFileLocator
{
    internal static IReadOnlyDictionary<string, SaveFileStamp> Capture(string directory)
    {
        var files = new Dictionary<string, SaveFileStamp>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directory))
            return files;
        foreach (string path in Directory.EnumerateFiles(directory, "*.save"))
        {
            var info = new FileInfo(path);
            files[info.FullName] = new SaveFileStamp(info.Length, info.LastWriteTimeUtc.Ticks);
        }
        return files;
    }

    internal static string? ResolveExisting(string directory, string? name, Func<string, string?>? resolveRaw = null)
    {
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(name))
            return null;
        // Native entries are raw|display. The label is never a second file
        // identity, even when it happens to equal another world's raw name.
        int separator = name.IndexOf('|');
        string? path = ExistingCandidate(directory, separator < 0 ? name : name.Substring(0, separator));
        if (path != null || separator >= 0)
            return path;
        if (resolveRaw != null)
        {
            string? raw = resolveRaw(name);
            if (!string.IsNullOrWhiteSpace(raw) && !string.Equals(raw, name, StringComparison.Ordinal))
                return ResolveExisting(directory, raw);
        }
        return null;
    }

    internal static string? ResolveWritten(string directory, string? requestedName,
        IReadOnlyDictionary<string, SaveFileStamp> before, Func<string, string?>? resolveRaw = null)
    {
        var current = Capture(directory);
        var changed = current.Where(pair => !before.TryGetValue(pair.Key, out var previous) || previous != pair.Value).ToArray();
        string? requestedPath = ResolveExisting(directory, requestedName, resolveRaw);
        if (requestedPath != null && changed.Any(pair => string.Equals(pair.Key, requestedPath, StringComparison.OrdinalIgnoreCase)))
            return requestedPath;
        if (changed.Length == 0)
            return null;
        // Autosave can rotate older files. Choose only a uniquely most recent
        // write, and never attach identities to an unchanged previous save.
        long newest = changed.Max(pair => pair.Value.LastWriteUtcTicks);
        var newestFiles = changed.Where(pair => pair.Value.LastWriteUtcTicks == newest).ToArray();
        return newestFiles.Length == 1 ? newestFiles[0].Key : null;
    }

    private static string? ExistingCandidate(string directory, string name)
    {
        name = name.Trim();
        if (name.Length == 0)
            return null;
        try
        {
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(root, name));
            if (!path.EndsWith(".save", StringComparison.OrdinalIgnoreCase))
                path += ".save";
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                return null;
            return path;
        }
        catch (ArgumentException) { return null; }
        catch (NotSupportedException) { return null; }
    }
}
