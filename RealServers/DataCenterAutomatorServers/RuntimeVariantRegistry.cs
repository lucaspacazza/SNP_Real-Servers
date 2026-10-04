using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DataCenterAutomatorServers;

internal sealed class RuntimeVariantRegistry
{
    private readonly string _gameRoot;
    private readonly ServerIdentityStore _store;
    private readonly Dictionary<string, string> _legacy = new(StringComparer.OrdinalIgnoreCase);
    private bool _legacyLoaded;
    private bool _allowLegacy = true;

    public RuntimeVariantRegistry() : this(FindGameRoot()) { }
    internal RuntimeVariantRegistry(string gameRoot)
    {
        _gameRoot = Path.GetFullPath(gameRoot);
        _store = new ServerIdentityStore(Path.Combine(_gameRoot, "UserData", "RealServers", "SaveIdentities"), Log.Warning);
    }
    public int Count { get { Load(); return _store.Records.Count + (_store.HasSnapshot || !_allowLegacy ? 0 : _legacy.Count(pair => pair.Value.Length != 0 && !_store.ContainsServerId(pair.Key))); } }

    public void Load()
    {
        if (_legacyLoaded) return;
        _legacyLoaded = true;
        string[] paths = {
            Path.Combine(_gameRoot, "UserData", "BackplaneBoostServers", "server-variants.tsv"),
            Path.Combine(_gameRoot, "UserData", "DataCenterAutomatorServers", "server-variants.tsv")
        };
        string? path = paths.FirstOrDefault(File.Exists);
        if (path == null) return;
        try
        {
            foreach (string line in File.ReadLines(path))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal)) continue;
                string[] parts = trimmed.Split('\t');
                if (parts.Length < 2) continue;
                ServerVariantSpec? spec = ServerVariantSpec.FindById(parts[1].Trim()); string id = parts[0].Trim();
                if (spec == null || !ServerIdentityStore.IsUniqueServerIdentity(id) || !CompatibleBase(id, spec)) continue;
                if (_legacy.TryGetValue(id, out string? prior) && !string.Equals(prior, spec.VariantId, StringComparison.OrdinalIgnoreCase))
                    _legacy[id] = string.Empty;
                else if (!_legacy.ContainsKey(id)) _legacy[id] = spec.VariantId;
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        { Log.Warning("Could not read legacy RealServers identities: " + ex.Message); }
    }

    public void SelectSave(string rawSaveName, string saveFilePath)
    {
        Load();
        // New worlds and shared-byte loads have no disk save from which an
        // exact legacy identity can be migrated. Never borrow another world.
        _allowLegacy = !string.IsNullOrWhiteSpace(rawSaveName)
            && !string.IsNullOrWhiteSpace(saveFilePath) && File.Exists(saveFilePath);
        _store.SelectSave(rawSaveName, saveFilePath);
    }
    public ServerVariantSpec? Get(string? serverId)
    {
        Load();
        if (!ServerIdentityStore.IsUniqueServerIdentity(serverId)) return null;
        string id = serverId!.Trim(); string? variant = _store.GetByServerId(id);
        if (variant == null && _allowLegacy && !_store.HasSnapshot && !_store.ContainsServerId(id)) _legacy.TryGetValue(id, out variant);
        ServerVariantSpec? spec = ServerVariantSpec.FindById(variant);
        return spec != null && CompatibleBase(id, spec) ? spec : null;
    }
    public ServerVariantSpec? GetByLooseId(string? looseId) => ServerVariantSpec.FindById(_store.GetByLooseId(looseId));
    public int? GetSavedRackPositionUid(string? serverId, string? looseId)
    {
        ServerIdentityRecord? saved = _store.FindSaved(serverId, looseId);
        return saved == null || saved.RackPositionUid == ServerIdentityRecord.UnknownRackPositionUid
            ? null : saved.RackPositionUid;
    }

    public bool HasUnknownSavedRackLocation(string? serverId, string? looseId) =>
        _store.FindSaved(serverId, looseId)?.RackPositionUid == 0;

    // Slots describe location, never ownership. Keep the APIs for old callers.
    public ServerVariantSpec? GetByRackPositionUid(int rackPositionUid) => null;
    public bool SetRackPosition(int rackPositionUid, ServerVariantSpec spec) => false;
    public bool Set(string? serverId, ServerVariantSpec spec)
    {
        if (spec == null || !ServerIdentityStore.IsUniqueServerIdentity(serverId) || !CompatibleBase(serverId!, spec)) return false;
        ServerIdentityRecord? existing = _store.FindByServerId(serverId);
        if (existing != null) return RecordIdentity(existing.ServerId, existing.LooseId, existing.RackPositionUid, spec);
        if (_store.ContainsServerId(serverId!)) return false;
        return RecordIdentity(serverId!, string.Empty, 0, spec);
    }
    public bool RecordIdentity(string? serverId, string? looseId, int rackUid, ServerVariantSpec spec)
    {
        if (spec == null) return false;
        string id = ServerIdentityStore.IsUniqueServerIdentity(serverId) && CompatibleBase(serverId!, spec) ? serverId!.Trim() : string.Empty;
        return _store.Record(new ServerIdentityRecord(id, looseId ?? string.Empty, rackUid, spec.VariantId));
    }
    public void CommitSnapshot(string rawSaveName, string saveFilePath, IEnumerable<ServerIdentityRecord> records)
    {
        IEnumerable<ServerIdentityRecord> known = records.Select(record =>
        {
            ServerVariantSpec? spec = ServerVariantSpec.FindById(record?.VariantId);
            if (spec == null || record == null) return null;
            string id = ServerIdentityStore.IsUniqueServerIdentity(record.ServerId) && CompatibleBase(record.ServerId, spec) ? record.ServerId.Trim() : string.Empty;
            return new ServerIdentityRecord(id, record.LooseId, record.RackPositionUid, spec.VariantId);
        }).Where(record => record != null).Cast<ServerIdentityRecord>();
        _store.CommitSnapshot(rawSaveName, saveFilePath, known);
    }
    private static bool CompatibleBase(string serverId, ServerVariantSpec spec) =>
        serverId.Trim().StartsWith(spec.BaseRuntimeToken.Replace('_', '.') + "_", StringComparison.OrdinalIgnoreCase);
    private static string FindGameRoot()
    {
        foreach (string path in new[] { AppContext.BaseDirectory, AppDomain.CurrentDomain.BaseDirectory, Environment.CurrentDirectory })
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, "Data Center.exe"))) return Path.GetFullPath(path);
        return Path.GetFullPath(AppContext.BaseDirectory);
    }
}
