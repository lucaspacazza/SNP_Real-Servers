using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DataCenterAutomatorServers;

internal sealed class ServerIdentityStore
{
    private readonly string _directory;
    private readonly HashSet<ServerIdentityRecord> _records = new();
    private readonly Dictionary<string, HashSet<ServerIdentityRecord>> _byServer = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<ServerIdentityRecord>> _byLoose = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<ServerIdentityRecord>> _withoutLooseByServer = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ServerIdentityRecord?> _savedByServer = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ServerIdentityRecord?> _savedByLoose = new(StringComparer.Ordinal);
    private readonly Action<string>? _warning;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal ServerIdentityStore(string directory, Action<string>? warning = null)
    {
        _directory = Path.GetFullPath(directory);
        _warning = warning;
    }

    internal bool HasSnapshot { get; private set; }
    internal IReadOnlyCollection<ServerIdentityRecord> Records => _records;

    internal void SelectSave(string rawSaveName, string saveFilePath)
    {
        ClearLiveRecords(); _savedByServer.Clear(); _savedByLoose.Clear();
        HasSnapshot = false;
        string raw = NormalizeScope(rawSaveName);
        string? fingerprint = TryFingerprint(saveFilePath);
        if (raw.Length == 0 || fingerprint == null || !Directory.Exists(_directory)) return;

        string primaryPath = SnapshotPath(raw, fingerprint);
        ServerIdentitySnapshot? primary = Read(primaryPath);
        if (Matches(primary, raw, fingerprint)) { Adopt(primary!); return; }
        // Backups recover interrupted writes, never a different save revision.
        if (primary == null)
        {
            ServerIdentitySnapshot? backup = Read(primaryPath + ".bak");
            if (Matches(backup, raw, fingerprint)) { Adopt(backup!); return; }
        }
        // Autosaves can be renamed or copied. Equal save bytes are safe only
        // when every matching snapshot agrees on the complete mapping.
        ServerIdentitySnapshot[] candidates = Directory.EnumerateFiles(_directory, "*." + fingerprint + ".json")
            .Select(path => (Path: path, Snapshot: Read(path)))
            .Where(x => x.Snapshot != null
                && string.Equals(x.Snapshot.SaveFingerprint, fingerprint, StringComparison.Ordinal)
                && string.Equals(Path.GetFileNameWithoutExtension(x.Path), ScopeKey(x.Snapshot.RawSaveName) + "." + fingerprint, StringComparison.Ordinal))
            .Select(x => x.Snapshot!).ToArray();
        if (candidates.Length == 0) return;
        ServerIdentityRecord[] first = Canonical(candidates[0]);
        if (candidates.Skip(1).Any(candidate => !SameMappings(first, Canonical(candidate))))
        {
            _warning?.Invoke("Identical save bytes have conflicting RealServers identity snapshots; restoration was skipped.");
            return;
        }
        Adopt(candidates[0]);
    }

    internal string? GetByServerId(string? id) => FindByServerId(id)?.VariantId;
    internal string? GetByLooseId(string? id) => FindLive(_byLoose, id)?.VariantId;
    internal ServerIdentityRecord? FindByServerId(string? id) => FindLive(_byServer, id);
    internal bool ContainsServerId(string id) => _byServer.ContainsKey(id.Trim());
    internal ServerIdentityRecord? FindSaved(string? serverId, string? looseId) =>
        Find(_savedByLoose, looseId) ?? Find(_savedByServer, serverId);
    private static ServerIdentityRecord? Find(Dictionary<string, ServerIdentityRecord?> index, string? id)
    {
        return !string.IsNullOrWhiteSpace(id) && index.TryGetValue(id.Trim(), out ServerIdentityRecord? record) ? record : null;
    }
    private static ServerIdentityRecord? FindLive(Dictionary<string, HashSet<ServerIdentityRecord>> index, string? id)
    {
        return !string.IsNullOrWhiteSpace(id) && index.TryGetValue(id.Trim(), out var records) && records.Count == 1
            ? records.First() : null;
    }
    private void ClearLiveRecords()
    {
        _records.Clear(); _byServer.Clear(); _byLoose.Clear(); _withoutLooseByServer.Clear();
    }
    private void AddRecord(ServerIdentityRecord record)
    {
        _records.Add(record);
        AddToIndex(_byServer, record.ServerId, record);
        AddToIndex(_byLoose, record.LooseId, record);
        if (record.LooseId.Length == 0) AddToIndex(_withoutLooseByServer, record.ServerId, record);
    }
    private static void AddToIndex(Dictionary<string, HashSet<ServerIdentityRecord>> index, string key, ServerIdentityRecord record)
    {
        if (key.Length == 0) return;
        if (!index.TryGetValue(key, out var records)) index[key] = records = new();
        records.Add(record);
    }
    private void RemoveRecord(ServerIdentityRecord record)
    {
        _records.Remove(record);
        RemoveFromIndex(_byServer, record.ServerId, record);
        RemoveFromIndex(_byLoose, record.LooseId, record);
        if (record.LooseId.Length == 0) RemoveFromIndex(_withoutLooseByServer, record.ServerId, record);
    }
    private static void RemoveFromIndex(Dictionary<string, HashSet<ServerIdentityRecord>> index, string key, ServerIdentityRecord record)
    {
        if (key.Length == 0 || !index.TryGetValue(key, out var records)) return;
        records.Remove(record);
        if (records.Count == 0) index.Remove(key);
    }
    private static void Index(Dictionary<string, ServerIdentityRecord?> index, string key, ServerIdentityRecord record)
    {
        if (key.Length == 0) return;
        // Null marks an ambiguous key; never pick one of its physical items.
        if (index.ContainsKey(key)) index[key] = null;
        else index[key] = record;
    }

    internal bool Record(ServerIdentityRecord record)
    {
        ServerIdentityRecord? normalized = Normalize(record);
        if (normalized == null || _records.Contains(normalized)) return false;
        // Save capture visits every physical object. Touch only that object's
        // identity groups instead of scanning and rebuilding the whole scene.
        if (normalized.LooseId.Length != 0 && _byLoose.TryGetValue(normalized.LooseId, out var sameLoose))
            foreach (var existing in sameLoose.ToArray()) RemoveRecord(existing);
        if (_withoutLooseByServer.TryGetValue(normalized.ServerId, out var placeholders))
            foreach (var existing in placeholders.ToArray()) RemoveRecord(existing);
        AddRecord(normalized);
        return true;
    }

    internal void CommitSnapshot(string rawSaveName, string saveFilePath, IEnumerable<ServerIdentityRecord> records)
    {
        string raw = NormalizeScope(rawSaveName);
        if (raw.Length == 0) throw new ArgumentException("A raw save name is required.", nameof(rawSaveName));
        string fingerprint = TryFingerprint(saveFilePath) ?? throw new FileNotFoundException("The native save must exist before committing its identity snapshot.", saveFilePath);
        var snapshot = new ServerIdentitySnapshot {
            SchemaVersion = 2, RawSaveName = raw, SaveFingerprint = fingerprint,
            Records = Canonical(records).ToList()
        };
        Directory.CreateDirectory(_directory);
        string target = SnapshotPath(raw, fingerprint), temporary = target + ".tmp";
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }
            if (!File.Exists(target)) File.Move(temporary, target);
            else if (Matches(Read(target), raw, fingerprint))
                File.Replace(temporary, target, target + ".bak", ignoreMetadataErrors: true);
            else
                // Keep the known good backup when the old primary is corrupt.
                File.Move(temporary, target, overwrite: true);
            Adopt(snapshot);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private void Adopt(ServerIdentitySnapshot snapshot)
    {
        ClearLiveRecords();
        foreach (var record in Canonical(snapshot)) AddRecord(record);
        HasSnapshot = true;
        _savedByServer.Clear(); _savedByLoose.Clear();
        foreach (ServerIdentityRecord record in _records)
        {
            Index(_savedByServer, record.ServerId, record);
            Index(_savedByLoose, record.LooseId, record);
        }
    }

    private ServerIdentitySnapshot? Read(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            ReadOnlySpan<byte> json = bytes;
            // Windows PowerShell and some editors write UTF-8 with a BOM.
            // System.Text.Json expects JSON bytes without that encoding marker.
            if (json.Length >= 3 && json[0] == 0xEF && json[1] == 0xBB && json[2] == 0xBF)
                json = json.Slice(3);
            ServerIdentitySnapshot? result = JsonSerializer.Deserialize<ServerIdentitySnapshot>(json);
            return result is { SchemaVersion: 1 or 2, Records: not null }
                && !string.IsNullOrWhiteSpace(result.RawSaveName)
                && result.SaveFingerprint?.Length == 64 ? result : null;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
        {
            _warning?.Invoke("Could not read RealServers identity snapshot '" + path + "': " + ex.Message);
            return null;
        }
    }

    private static bool Matches(ServerIdentitySnapshot? snapshot, string raw, string fingerprint) =>
        snapshot != null && string.Equals(NormalizeScope(snapshot.RawSaveName), raw, StringComparison.OrdinalIgnoreCase)
        && string.Equals(snapshot.SaveFingerprint, fingerprint, StringComparison.Ordinal);
    private string SnapshotPath(string raw, string fingerprint) => Path.Combine(_directory, ScopeKey(raw) + "." + fingerprint + ".json");
    private static string ScopeKey(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeScope(raw).ToUpperInvariant()))).ToLowerInvariant();
    private static string NormalizeScope(string? raw) => raw?.Trim() ?? string.Empty;
    private static string? TryFingerprint(string? saveFilePath)
    {
        if (string.IsNullOrWhiteSpace(saveFilePath) || !File.Exists(saveFilePath)) return null;
        using var stream = new FileStream(saveFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    internal static ServerIdentityRecord? Normalize(ServerIdentityRecord? record)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.VariantId)) return null;
        string server = IsUniqueServerIdentity(record.ServerId) ? record.ServerId.Trim() : string.Empty;
        string loose = record.LooseId?.Trim() ?? string.Empty;
        if (server.Length == 0 && loose.Length == 0) return null;
        int rackUid = record.RackPositionUid >= ServerIdentityRecord.LooseRackPositionUid
            ? record.RackPositionUid : ServerIdentityRecord.UnknownRackPositionUid;
        return new(server, loose, rackUid, record.VariantId.Trim());
    }
    internal static bool IsUniqueServerIdentity(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        string text = id.Trim(); int separator = text.IndexOf('_');
        if (separator <= 0 || separator == text.Length - 1 || text.Any(char.IsWhiteSpace)) return false;
        string family = text.Substring(0, separator);
        return new[] { "Green", "Yellow", "Blue", "Purple" }.Any(color =>
            string.Equals(family, "Server." + color + "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(family, "Server." + color + "2", StringComparison.OrdinalIgnoreCase));
    }
    private static ServerIdentityRecord[] Canonical(IEnumerable<ServerIdentityRecord> records) =>
        records.Select(Normalize).Where(x => x != null).Cast<ServerIdentityRecord>().Distinct()
        .OrderBy(x => x.ServerId, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.LooseId, StringComparer.Ordinal)
        .ThenBy(x => x.RackPositionUid).ThenBy(x => x.VariantId, StringComparer.OrdinalIgnoreCase).ToArray();
    private static ServerIdentityRecord[] Canonical(ServerIdentitySnapshot snapshot) =>
        Canonical(snapshot.SchemaVersion == 1
            ? snapshot.Records.Select(record => record is { RackPositionUid: <= 0 }
                ? record with { RackPositionUid = ServerIdentityRecord.UnknownRackPositionUid } : record)
            : snapshot.Records);
    private static bool SameMappings(ServerIdentityRecord[] left, ServerIdentityRecord[] right) =>
        left.Length == right.Length && left.Zip(right, (a, b) =>
            string.Equals(a.ServerId, b.ServerId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.LooseId, b.LooseId, StringComparison.Ordinal)
            && a.RackPositionUid == b.RackPositionUid
            && string.Equals(a.VariantId, b.VariantId, StringComparison.OrdinalIgnoreCase)).All(equal => equal);
}

internal sealed class ServerIdentitySnapshot
{
    public int SchemaVersion { get; set; }
    public string RawSaveName { get; set; } = string.Empty;
    public string SaveFingerprint { get; set; } = string.Empty;
    public List<ServerIdentityRecord> Records { get; set; } = new();
}
