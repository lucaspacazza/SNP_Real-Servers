using DataCenterAutomatorServers;

if (args.Contains("--benchmark")) return PersistenceBenchmarks.Run(args.Contains("--assert-budget"));

int passed = 0, failed = 0;
var r440 = ServerVariantSpec.FindById("dell_r440")!;
var xe = ServerVariantSpec.FindById("dell_xe8640")!;
var r760 = ServerVariantSpec.FindById("dell_r760")!;
var roots = new List<string>();
void Test(string name, Action run)
{
    try { run(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}");
}
string Temp()
{
    string path = Path.Combine(Path.GetTempPath(), "RealServers.PersistenceTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path); roots.Add(path); return path;
}
string Save(string root, string name, string content = "revision-one")
{
    string path = Path.Combine(root, name + ".save"); File.WriteAllText(path, content); return path;
}
ServerIdentityRecord R(string id = "Server.Green1_-42", string loose = "cargo-42", int rack = 125, string variant = "dell_r440") => new(id, loose, rack, variant);
string Snapshot(string root) => Directory.GetFiles(Path.Combine(root, "UserData", "RealServers", "SaveIdentities"), "*.json").Single();

try
{
    Test("Record identity survives exact server ID and loose ID roundtrip", () => {
        string root = Temp(), file = Save(root, "A");
        var registry = new RuntimeVariantRegistry(root); registry.SelectSave("A", file);
        registry.RecordIdentity("Server.Green1_-42", "cargo-42", 125, r440);
        Equal("dell_r440", registry.Get("Server.Green1_-42")?.VariantId);
        Equal("dell_r440", registry.GetByLooseId("cargo-42")?.VariantId);
        Equal(0, Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).Length);
        registry.CommitSnapshot("A", file, new[] { R() });
        var reloaded = new RuntimeVariantRegistry(root); reloaded.SelectSave("A", file);
        Equal("dell_r440", reloaded.Get("Server.Green1_-42")?.VariantId);
        Equal("dell_r440", reloaded.GetByLooseId("cargo-42")?.VariantId);
    });
    Test("Distinct saves cannot cross identities or rack markers", () => {
        string root = Temp(), a = Save(root, "A"), b = Save(root, "B", "different-save");
        var registry = new RuntimeVariantRegistry(root);
        registry.CommitSnapshot("A", a, new[] { R() });
        registry.SelectSave("B", b);
        Equal<ServerVariantSpec?>(null, registry.Get("Server.Green1_-42"));
        Equal<ServerVariantSpec?>(null, registry.GetByLooseId("cargo-42"));
        Equal<ServerVariantSpec?>(null, registry.GetByRackPositionUid(125));
        registry.CommitSnapshot("B", b, new[] { R(variant: "lenovo", id: "Server.Yellow1_-42") });
        registry.SelectSave("A", a); Equal("dell_r440", registry.GetByLooseId("cargo-42")?.VariantId);
        registry.SelectSave("B", b); Equal("lenovo", registry.GetByLooseId("cargo-42")?.VariantId);
    });
    Test("Changed save fingerprint cannot use previous revision or backup", () => {
        string root = Temp(), file = Save(root, "A"); var registry = new RuntimeVariantRegistry(root);
        registry.CommitSnapshot("A", file, new[] { R() });
        registry.CommitSnapshot("A", file, new[] { R() });
        File.WriteAllText(file, "revision-two"); registry.SelectSave("A", file);
        Equal<ServerVariantSpec?>(null, registry.GetByLooseId("cargo-42"));
    });
    Test("Truncated primary recovers only backup for matching scope and bytes", () => {
        string root = Temp(), file = Save(root, "A"); var registry = new RuntimeVariantRegistry(root);
        registry.CommitSnapshot("A", file, new[] { R() }); registry.CommitSnapshot("A", file, new[] { R() });
        File.WriteAllText(Snapshot(root), "{truncated"); registry.SelectSave("A", file);
        Equal("dell_r440", registry.GetByLooseId("cargo-42")?.VariantId);
    });
    Test("Copied save matches an unambiguous content identity snapshot", () => {
        string root = Temp(), a = Save(root, "A"), copy = Save(root, "Copy"); var registry = new RuntimeVariantRegistry(root);
        registry.CommitSnapshot("A", a, new[] { R() }); registry.SelectSave("Copy", copy);
        Equal("dell_r440", registry.GetByLooseId("cargo-42")?.VariantId);
    });
    Test("Equal bytes with conflicting mappings are ambiguous for copied saves", () => {
        string root = Temp(), a = Save(root, "A"), b = Save(root, "B"), copy = Save(root, "Copy"); var registry = new RuntimeVariantRegistry(root);
        registry.CommitSnapshot("A", a, new[] { R() });
        registry.CommitSnapshot("B", b, new[] { R(id: "Server.Yellow1_-42", variant: "lenovo") });
        registry.SelectSave("Copy", copy); Equal<ServerVariantSpec?>(null, registry.GetByLooseId("cargo-42"));
        registry.SelectSave("A", a); Equal("dell_r440", registry.GetByLooseId("cargo-42")?.VariantId);
    });
    Test("Equal mappings from equal bytes are safe for copied saves", () => {
        string root = Temp(), a = Save(root, "A"), b = Save(root, "B"), copy = Save(root, "Copy"); var registry = new RuntimeVariantRegistry(root);
        registry.CommitSnapshot("A", a, new[] { R(), R("Server.Green2_-90", "cargo90", 0, "dell_xe8640") });
        registry.CommitSnapshot("B", b, new[] { R("Server.Green2_-90", "cargo90", 0, "dell_xe8640"), R() });
        registry.SelectSave("Copy", copy); Equal("dell_xe8640", registry.GetByLooseId("cargo90")?.VariantId);
    });
    Test("Snapshots remove sold and stale objects instead of merging history", () => {
        string root = Temp(), file = Save(root, "A"); var registry = new RuntimeVariantRegistry(root);
        registry.CommitSnapshot("A", file, new[] { R() }); registry.CommitSnapshot("A", file, Array.Empty<ServerIdentityRecord>());
        registry.SelectSave("A", file); Equal(0, registry.Count); Equal<ServerVariantSpec?>(null, registry.GetByLooseId("cargo-42"));
    });
    Test("100 objects with identical cart positions restore independently", () => {
        string root = Temp(), file = Save(root, "A"); var registry = new RuntimeVariantRegistry(root);
        var records = Enumerable.Range(1, 100).Select(i => R("Server.Green1_-" + i, "cargo-" + i, 0)).ToArray();
        registry.CommitSnapshot("A", file, records); registry.SelectSave("A", file); Equal(100, registry.Count);
        foreach (var r in records) Equal("dell_r440", registry.GetByLooseId(r.LooseId)?.VariantId);
    });
    Test("Rack position is never evidence of variant ownership", () => {
        var registry = new RuntimeVariantRegistry(Temp()); registry.SetRackPosition(125, r440);
        Equal<ServerVariantSpec?>(null, registry.GetByRackPositionUid(125));
    });
    Test("Legacy exact unique IDs migrate without marking stock base families", () => {
        string root = Temp(), dir = Path.Combine(root, "UserData", "BackplaneBoostServers"); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "server-variants.tsv"), "# serverId\tvariantId\nServer.Green1_-42\tdell_r440\nServer.Green1\tdell_r440\nServer.Green2_-99\tdell_r440\n");
        var registry = new RuntimeVariantRegistry(root); registry.Load();
        Equal("dell_r440", registry.Get("Server.Green1_-42")?.VariantId);
        Equal<ServerVariantSpec?>(null, registry.Get("Server.Green1"));
        Equal<ServerVariantSpec?>(null, registry.Get("Server.Green2_-99"));
        Equal(0, Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).Length);
    });
    Test("Explicit empty snapshot suppresses legacy identities", () => {
        string root = Temp(), dir = Path.Combine(root, "UserData", "DataCenterAutomatorServers"); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "server-variants.tsv"), "Server.Green1_-42\tdell_r440\n");
        string file = Save(root, "A"); var registry = new RuntimeVariantRegistry(root);
        registry.CommitSnapshot("A", file, Array.Empty<ServerIdentityRecord>()); registry.SelectSave("A", file);
        Equal<ServerVariantSpec?>(null, registry.Get("Server.Green1_-42"));
    });
    Test("Shared base ID or unknown variant is rejected; loose identity stays usable", () => {
        string root = Temp(), file = Save(root, "A"); var registry = new RuntimeVariantRegistry(root);
        registry.CommitSnapshot("A", file, new[] { R("Server.Green1", "unique-loose", 0), R("Server.Green1_-7", "unknown", 0, "missing") });
        registry.SelectSave("A", file);
        Equal<ServerVariantSpec?>(null, registry.Get("Server.Green1"));
        Equal("dell_r440", registry.GetByLooseId("unique-loose")?.VariantId);
        Equal<ServerVariantSpec?>(null, registry.GetByLooseId("unknown"));
    });
    Test("Conflicting server ID resolves only through independent loose IDs", () => {
        string root = Temp(), file = Save(root, "A"); var registry = new RuntimeVariantRegistry(root);
        registry.CommitSnapshot("A", file, new[] { R(), R("Server.Green1_-42", "other-loose", 0, "green-other") });
        registry.SelectSave("A", file);
        Equal<ServerVariantSpec?>(null, registry.Get("Server.Green1_-42"));
        Equal("dell_r440", registry.GetByLooseId("cargo-42")?.VariantId);
    });
    Test("Clear scope does not reuse current loose object mappings", () => {
        var registry = new RuntimeVariantRegistry(Temp()); registry.RecordIdentity("Server.Green1_-42", "cargo-42", 0, r440);
        registry.SelectSave("", ""); Equal<ServerVariantSpec?>(null, registry.GetByLooseId("cargo-42"));
    });
    Test("Legacy Set preserves the combined loose identity instead of duplicating it", () => {
        var registry = new RuntimeVariantRegistry(Temp()); registry.RecordIdentity("Server.Green1_-42", "cargo-42", 125, r440);
        registry.Set("Server.Green1_-42", r440);
        Equal(1, registry.Count); Equal("dell_r440", registry.Get("Server.Green1_-42")?.VariantId);
        Equal("dell_r440", registry.GetByLooseId("cargo-42")?.VariantId);
    });
    Test("Recovering one legacy server does not disable other exact legacy IDs", () => {
        string root = Temp(), dir = Path.Combine(root, "UserData", "BackplaneBoostServers"); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "server-variants.tsv"), "Server.Green1_-42\tdell_r440\nServer.Green2_-90\tdell_xe8640\n");
        var registry = new RuntimeVariantRegistry(root); registry.RecordIdentity("Server.Green1_-42", "cargo-42", 0, r440);
        Equal("dell_xe8640", registry.Get("Server.Green2_-90")?.VariantId);
    });
    Test("Many autosave revisions retain the oldest renamed backup identity", () => {
        string root = Temp(), file = Save(root, "AutoSave", "revision-one"); var registry = new RuntimeVariantRegistry(root);
        registry.CommitSnapshot("AutoSave", file, new[] { R() });
        foreach (int revision in Enumerable.Range(2, 5)) {
            File.WriteAllText(file, "revision-" + revision);
            registry.CommitSnapshot("AutoSave", file, new[] { R("Server.Green2_-90", "cargo90", 0, "dell_xe8640") });
        }
        string backup = Save(root, "AutoSave_Backup_Old", "revision-one"); registry.SelectSave("AutoSave_Backup_Old", backup);
        Equal("dell_r440", registry.GetByLooseId("cargo-42")?.VariantId);
        Equal(6, Directory.GetFiles(Path.Combine(root, "UserData", "RealServers", "SaveIdentities"), "*.json").Length);
    });
    Test("An unrelated or older revision backup cannot recover a truncated snapshot", () => {
        string root = Temp(), a = Save(root, "A"), b = Save(root, "B", "revision-two"); var registry = new RuntimeVariantRegistry(root);
        registry.CommitSnapshot("A", a, new[] { R() });
        string oldPath = Snapshot(root);
        registry.CommitSnapshot("B", b, new[] { R("Server.Green2_-90", "cargo90", 0, "dell_xe8640") });
        string bPath = Directory.GetFiles(Path.Combine(root, "UserData", "RealServers", "SaveIdentities"), "*.json").Single(p => p != oldPath);
        File.Copy(bPath, oldPath + ".bak"); File.WriteAllText(oldPath, "{");
        registry.SelectSave("A", a); Equal<ServerVariantSpec?>(null, registry.GetByLooseId("cargo90")); Equal<ServerVariantSpec?>(null, registry.GetByLooseId("cargo-42"));
    });
    Test("Changing native server ID keeps a single physical identity", () => {
        var registry = new RuntimeVariantRegistry(Temp()); registry.RecordIdentity("Server.Green1_-42", "cargo-42", 125, r440);
        registry.RecordIdentity("Server.Green1_-88", "cargo-42", 0, r440);
        Equal(1, registry.Count); Equal<ServerVariantSpec?>(null, registry.Get("Server.Green1_-42"));
        Equal("dell_r440", registry.Get("Server.Green1_-88")?.VariantId);
    });
    Test("Missing native saves and blank raw names cannot create snapshots", () => {
        string root = Temp(); var registry = new RuntimeVariantRegistry(root);
        bool blank = false, missing = false;
        try { registry.CommitSnapshot("", "", new[] { R() }); } catch (ArgumentException) { blank = true; }
        try { registry.CommitSnapshot("A", Path.Combine(root, "missing.save"), new[] { R() }); } catch (FileNotFoundException) { missing = true; }
        Equal(true, blank); Equal(true, missing); Equal(0, Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).Length);
    });
    Test("Invalid identity records never create stock server markers", () => {
        var registry = new RuntimeVariantRegistry(Temp());
        foreach (string id in new[] { "", "Server.Green1", "Server.Green1_", "Server.Green1_ with space", "dc_automator_owned", "Server.Unknown1_-42" }) Equal(false, registry.Set(id, r440));
        Equal(false, registry.RecordIdentity("Server.Green1", "", -9, r440));
        Equal(false, registry.RecordIdentity("", "", 0, r440)); Equal(0, registry.Count);
    });
    Test("Adding loose identity enriches a legacy server marker without duplication", () => {
        var registry = new RuntimeVariantRegistry(Temp()); registry.Set("Server.Green1_-42", r440);
        registry.RecordIdentity("Server.Green1_-42", "cargo-42", 0, r440);
        Equal(1, registry.Count); Equal("dell_r440", registry.Get("Server.Green1_-42")?.VariantId);
    });
    Test("Saving after recovery preserves a good backup instead of backing up corruption", () => {
        string root = Temp(), file = Save(root, "A"); var registry = new RuntimeVariantRegistry(root);
        registry.CommitSnapshot("A", file, new[] { R() }); registry.CommitSnapshot("A", file, new[] { R() });
        string primary = Snapshot(root); File.WriteAllText(primary, "{"); registry.SelectSave("A", file);
        Equal("dell_r440", registry.GetByLooseId("cargo-42")?.VariantId);
        registry.CommitSnapshot("A", file, new[] { R() }); File.WriteAllText(primary, "{"); registry.SelectSave("A", file);
        Equal("dell_r440", registry.GetByLooseId("cargo-42")?.VariantId);
    });
    Test("Saved rack state resolves exact rack and physical loose identities", () => {
        string root = Temp(), file = Save(root, "A"); var registry = new RuntimeVariantRegistry(root);
        registry.CommitSnapshot("A", file, new[] { R(), R("Server.Green2_-90", "cart-90", 0, "dell_xe8640") });
        registry.SelectSave("A", file);
        Equal<int?>(125, registry.GetSavedRackPositionUid("Server.Green1_-42", ""));
        Equal<int?>(-1, registry.GetSavedRackPositionUid("Server.Green2_-90", "cart-90"));
        Equal<int?>(-1, registry.GetSavedRackPositionUid("", "cart-90"));
    });
    Test("Transient native load changes cannot rewrite the saved rack contract", () => {
        string root = Temp(), file = Save(root, "A"); var registry = new RuntimeVariantRegistry(root);
        registry.CommitSnapshot("A", file, new[] { R("Server.Green1_-42", "cart-42", 0) }); registry.SelectSave("A", file);
        registry.RecordIdentity("Server.Green1_-42", "cart-42", 999, r440); registry.Set("Server.Green1_-42", r440);
        Equal<int?>(-1, registry.GetSavedRackPositionUid("Server.Green1_-42", "cart-42"));
    });
    Test("Legacy markers and cleared scope contain no authoritative saved rack state", () => {
        var registry = new RuntimeVariantRegistry(Temp()); registry.Set("Server.Green1_-42", r440);
        Equal<int?>(null, registry.GetSavedRackPositionUid("Server.Green1_-42", ""));
        registry.SelectSave("", ""); Equal<int?>(null, registry.GetSavedRackPositionUid("Server.Green1_-42", ""));
    });
    Test("New worlds and shared-byte loads suppress global legacy identities", () => {
        string root = Temp(), dir = Path.Combine(root, "UserData", "BackplaneBoostServers"); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "server-variants.tsv"), "Server.Green1_-42\tdell_r440\n");
        var registry = new RuntimeVariantRegistry(root);
        Equal("dell_r440", registry.Get("Server.Green1_-42")?.VariantId); Equal(1, registry.Count);
        registry.SelectSave("", "");
        Equal<ServerVariantSpec?>(null, registry.Get("Server.Green1_-42")); Equal(0, registry.Count);
        registry.RecordIdentity("Server.Green2_-90", "new-world-cargo", 0, xe);
        Equal("dell_xe8640", registry.Get("Server.Green2_-90")?.VariantId); Equal(1, registry.Count);
    });
    Test("A missing file suppresses legacy mappings; an existing file permits exact migration", () => {
        string root = Temp(), dir = Path.Combine(root, "UserData", "BackplaneBoostServers"); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "server-variants.tsv"), "Server.Green1_-42\tdell_r440\n");
        var registry = new RuntimeVariantRegistry(root);
        registry.SelectSave("missing", Path.Combine(root, "missing.save"));
        Equal<ServerVariantSpec?>(null, registry.Get("Server.Green1_-42")); Equal(0, registry.Count);
        string file = Save(root, "OldWorld"); registry.SelectSave("OldWorld", file);
        Equal("dell_r440", registry.Get("Server.Green1_-42")?.VariantId); Equal(1, registry.Count);
    });
    Test("Incremental updates preserve duplicate identity ambiguity and restore uniqueness", () => {
        var store = new ServerIdentityStore(Temp());
        store.Record(R("Server.Green1_-42", "cargo-one"));
        store.Record(R("server.green1_-42", "cargo-two", variant: "green-other"));
        Equal<string?>(null, store.GetByServerId("SERVER.GREEN1_-42"));
        Equal("dell_r440", store.GetByLooseId("cargo-one"));
        Equal("green-other", store.GetByLooseId("cargo-two"));
        store.Record(R("Server.Green1_-88", "cargo-two", variant: "green-other"));
        Equal(2, store.Records.Count);
        Equal("dell_r440", store.GetByServerId("Server.Green1_-42"));
        Equal("green-other", store.GetByServerId("Server.Green1_-88"));
    });
    Test("Exact duplicate updates cannot erase existing loose ID ambiguity", () => {
        string root = Temp(), file = Save(root, "Ambiguous");
        var store = new ServerIdentityStore(Path.Combine(root, "identities"));
        store.CommitSnapshot("Ambiguous", file, new[] { R(), R("Server.Green1_-88", "cargo-42", variant: "green-other") });
        Equal(false, store.Record(R()));
        Equal<string?>(null, store.GetByLooseId("cargo-42")); Equal(2, store.Records.Count);
        store.Record(R("Server.Green1_-99", "cargo-42", variant: "green-other"));
        Equal(1, store.Records.Count); Equal("green-other", store.GetByLooseId("cargo-42"));
        Equal<string?>(null, store.GetByServerId("Server.Green1_-42"));
    });
    Test("Placeholder enrichment removes only matching case-insensitive server markers", () => {
        var store = new ServerIdentityStore(Temp());
        store.Record(R("Server.Green1_-42", "cargo-one"));
        store.Record(R("server.green1_-42", "", 0, "green-other"));
        store.Record(R("Server.Green1_-90", "", 0));
        store.Record(R("SERVER.GREEN1_-42", "cargo-two", 0, "green-other"));
        Equal(3, store.Records.Count); Equal<string?>(null, store.GetByServerId("Server.Green1_-42"));
        Equal("dell_r440", store.GetByLooseId("cargo-one")); Equal("green-other", store.GetByLooseId("cargo-two"));
        Equal("dell_r440", store.GetByServerId("Server.Green1_-90"));
    });
    Test("Thousands of identity changes match the prior record semantics", () => {
        var store = new ServerIdentityStore(Temp());
        var reference = new List<ServerIdentityRecord>();
        var random = new Random(108);
        for (int i = 0; i < 4000; i++) {
            string id = "Server.Green1_-" + random.Next(1, 120);
            if (random.Next(3) == 0) id = id.ToLowerInvariant();
            string loose = random.Next(4) == 0 ? "" : "cargo-" + random.Next(1, 120);
            if (random.Next(8) == 0) loose = loose.ToUpperInvariant();
            var record = R(id, loose, random.Next(0, 4), random.Next(2) == 0 ? "dell_r440" : "green-other");
            bool expected = !reference.Contains(record);
            if (expected) {
                reference.RemoveAll(x => (loose.Length != 0 && string.Equals(x.LooseId, loose, StringComparison.Ordinal))
                    || (x.LooseId.Length == 0 && string.Equals(x.ServerId, id, StringComparison.OrdinalIgnoreCase)));
                reference.Add(record);
            }
            Equal(expected, store.Record(record)); Equal(reference.Count, store.Records.Count);
            var matchingServer = reference.Where(x => string.Equals(x.ServerId, id, StringComparison.OrdinalIgnoreCase)).ToArray();
            var matchingLoose = reference.Where(x => loose.Length != 0 && string.Equals(x.LooseId, loose, StringComparison.Ordinal)).ToArray();
            Equal(matchingServer.Length == 1 ? matchingServer[0].VariantId : null, store.GetByServerId(id));
            Equal(matchingLoose.Length == 1 ? matchingLoose[0].VariantId : null, store.GetByLooseId(loose));
            Equal(true, new HashSet<ServerIdentityRecord>(reference).SetEquals(store.Records));
        }
    });
}
finally
{
    string testBase = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RealServers.PersistenceTests")) + Path.DirectorySeparatorChar;
    foreach (string root in roots) {
        if (!Path.GetFullPath(root).StartsWith(testBase, StringComparison.OrdinalIgnoreCase)) throw new Exception("Unsafe test cleanup path.");
        Directory.Delete(root, recursive: true);
    }
}
Console.WriteLine($"Persistence: {passed}/{passed + failed} passed.");
return failed == 0 ? 0 : 1;
