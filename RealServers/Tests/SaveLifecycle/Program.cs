using DataCenterAutomatorServers;

internal static class Program
{
    private static int _passed, _failed;
    private static bool CaptureOutsideTransaction(SaveLifecycleCoordinator<string[]> flow)
    {
        var method = typeof(SaveLifecycleCoordinator<string[]>).GetMethod("CaptureOutsideTransaction", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (method == null) throw new InvalidOperationException("Standalone events have no active-transaction capture guard.");
        return (bool)method.Invoke(flow, null)!;
    }
    private static int Main()
    {
        Run("Raw save names resolve without an extension", root =>
        {
            var file = Save(root, "A.save", "native");
            Equal(file, SaveFileLocator.ResolveExisting(root, "A"));
        });
        Run("Native display to raw entries resolve the real file", root =>
        {
            var file = Save(root, "A.save", "native");
            Equal(file, SaveFileLocator.ResolveExisting(root, "Meu datacenter", _ => "A|Meu datacenter"));
        });
        Run("Existing raw names are preferred to display mappings", root =>
        {
            var a = Save(root, "A.save", "one");
            Save(root, "B.save", "two");
            Equal(a, SaveFileLocator.ResolveExisting(root, "A", _ => "B"));
        });
        Run("The locator accepts a native absolute path inside its directory", root =>
        {
            var file = Save(root, "A.save", "native");
            Equal(file, SaveFileLocator.ResolveExisting(root, file));
        });
        Run("A malformed entry cannot escape the native save directory", root =>
        {
            Equal<string?>(null, SaveFileLocator.ResolveExisting(root, "..\\outside"));
            Equal<string?>(null, SaveFileLocator.ResolveExisting(root, "..\\outside.save"));
        });
        Run("Missing save files do not fall back to some other latest save", root =>
        {
            Save(root, "A.save", "native");
            Equal<string?>(null, SaveFileLocator.ResolveExisting(root, "missing"));
        });
        Run("A display-name segment cannot impersonate a missing raw save", root =>
        {
            Save(root, "B.save", "different world");
            Equal<string?>(null, SaveFileLocator.ResolveExisting(root, "missing|B"));
        });
        Run("Only native .save files participate in change detection", root =>
        {
            Save(root, "A.save", "native");
            Save(root, "A.meta", "metadata");
            Equal(1, SaveFileLocator.Capture(root).Count);
        });
        Run("A newly generated native name selects the created save", root =>
        {
            var before = SaveFileLocator.Capture(root);
            var file = Save(root, "new.save", "native");
            Equal(file, SaveFileLocator.ResolveWritten(root, null, before));
        });
        Run("An overwrite is recognized by its changed metadata", root =>
        {
            var file = Save(root, "A.save", "old");
            var before = SaveFileLocator.Capture(root);
            File.WriteAllText(file, "new larger native state");
            Equal(file, SaveFileLocator.ResolveWritten(root, "A", before));
        });
        Run("An unchanged save is never stamped with a new snapshot", root =>
        {
            Save(root, "A.save", "native");
            var before = SaveFileLocator.Capture(root);
            Equal<string?>(null, SaveFileLocator.ResolveWritten(root, "A", before));
        });
        Run("Ambiguous simultaneous save writes cannot attach the wrong identity", root =>
        {
            var before = SaveFileLocator.Capture(root);
            var a = Save(root, "A.save", "one");
            var b = Save(root, "B.save", "two");
            var sameTime = DateTime.UtcNow.AddSeconds(-10);
            File.SetLastWriteTimeUtc(a, sameTime);
            File.SetLastWriteTimeUtc(b, sameTime);
            Equal<string?>(null, SaveFileLocator.ResolveWritten(root, null, before));
        });
        Run("An explicit save resolves even when other files also changed", root =>
        {
            var before = SaveFileLocator.Capture(root);
            var a = Save(root, "A.save", "one");
            Save(root, "B.save", "two");
            Equal(a, SaveFileLocator.ResolveWritten(root, "A", before));
        });
        Run("Post-save snapshots include IDs finalized by the native saver", root =>
        {
            string[] state = { "pending" };
            string[]? committed = null;
            var flow = Flow(root, () => state, (_, _, records) => committed = records);
            var transaction = flow.PrepareSave("A");
            Save(root, "A.save", "native");
            state = new[] { "Server.Green1_-1:Dell", "loose-1:Dell" };
            True(flow.CompleteSave(transaction));
            Equal(state, committed);
        });
        Run("Successful saves identify their exact file and canonical raw name", root =>
        {
            string? raw = null, path = null;
            var flow = Flow(root, () => new[] { "Dell" }, (r, p, _) => { raw = r; path = p; });
            var transaction = flow.PrepareSave(null);
            var file = Save(root, "generated.save", "native");
            True(flow.CompleteSave(transaction));
            Equal("generated.save", raw);
            Equal(file, path);
        });
        Run("Failed native saves never commit identities", root =>
        {
            var commits = 0;
            var flow = Flow(root, () => new[] { "Dell" }, (_, _, _) => commits++);
            var transaction = flow.PrepareSave("A");
            Save(root, "A.save", "partial");
            flow.CancelSave(transaction);
            True(!flow.CompleteSave(transaction));
            Equal(0, commits);
        });
        Run("A save transaction commits at most once", root =>
        {
            var commits = 0;
            var flow = Flow(root, () => new[] { "Dell" }, (_, _, _) => commits++);
            var transaction = flow.PrepareSave("A");
            Save(root, "A.save", "native");
            True(flow.CompleteSave(transaction));
            True(!flow.CompleteSave(transaction));
            Equal(1, commits);
        });
        Run("Save-as uses the newly written file instead of the loaded save", root =>
        {
            Save(root, "A.save", "old");
            string? committedRaw = null;
            var flow = Flow(root, () => new[] { "Dell" }, (raw, _, _) => committedRaw = raw);
            flow.BeginLoad("A");
            var transaction = flow.PrepareSave("B");
            Save(root, "B.save", "new");
            True(flow.CompleteSave(transaction));
            Equal("B.save", committedRaw);
        });
        Run("Load selection precedes object recovery", root =>
        {
            Save(root, "A.save", "native");
            var order = new List<string>();
            var flow = Flow(root, () => Array.Empty<string>(), (_, _, _) => { },
                (raw, _) => order.Add("select:" + raw), () => order.Add("recover"));
            flow.BeginLoad("A");
            flow.CompleteLoad();
            Equal("select:A.save,recover", string.Join(',', order));
        });
        Run("In-memory loading clears file identity instead of reusing the previous save", root =>
        {
            Save(root, "A.save", "native");
            string? raw = "previous", path = "previous";
            var flow = Flow(root, () => Array.Empty<string>(), (_, _, _) => { }, (r, p) => { raw = r; path = p; });
            flow.BeginLoad("A");
            flow.BeginLoad(null);
            Equal(string.Empty, raw);
            Equal(string.Empty, path);
        });
        Run("A failed sidecar commit closes the transaction", root =>
        {
            var flow = Flow(root, () => new[] { "Dell" }, (_, _, _) => throw new IOException("Sidecar storage unavailable"));
            var transaction = flow.PrepareSave("A");
            Save(root, "A.save", "native");
            try { flow.CompleteSave(transaction); throw new InvalidOperationException("Expected the commit failure"); }
            catch (IOException) { }
            True(!flow.CompleteSave(transaction));
        });
        Run("A native save event reuses the active prefix snapshot", root =>
        {
            var captures = 0;
            var flow = Flow(root, () => { captures++; return new[] { "Dell" }; }, (_, _, _) => { });
            var transaction = flow.PrepareSave("A");
            True(!CaptureOutsideTransaction(flow));
            Equal(1, captures);
            Save(root, "A.save", "native");
            True(flow.CompleteSave(transaction));
            Equal(2, captures);
            True(CaptureOutsideTransaction(flow));
            Equal(3, captures);
        });
        Run("Standalone native events still capture exact identities", root =>
        {
            var captures = 0;
            var flow = Flow(root, () => { captures++; return new[] { "Dell" }; }, (_, _, _) => { });
            True(CaptureOutsideTransaction(flow));
            Equal(1, captures);
        });
        Run("Cancelling a native save releases the standalone event guard", root =>
        {
            var flow = Flow(root, () => new[] { "Dell" }, (_, _, _) => { });
            var transaction = flow.PrepareSave("A");
            flow.CancelSave(transaction);
            True(CaptureOutsideTransaction(flow));
        });
        Run("A failed sidecar commit does not suppress later standalone events", root =>
        {
            var flow = Flow(root, () => new[] { "Dell" }, (_, _, _) => throw new IOException("commit failed"));
            var transaction = flow.PrepareSave("A");
            Save(root, "A.save", "native");
            try { flow.CompleteSave(transaction); } catch (IOException) { }
            True(CaptureOutsideTransaction(flow));
        });
        Run("Nested native saves guard events until every transaction closes", root =>
        {
            var flow = Flow(root, () => new[] { "Dell" }, (_, _, _) => { });
            var outer = flow.PrepareSave("A");
            var inner = flow.PrepareSave("B");
            flow.CancelSave(inner);
            True(!CaptureOutsideTransaction(flow));
            flow.CancelSave(outer);
            True(CaptureOutsideTransaction(flow));
        });
        Console.WriteLine($"Save lifecycle: {_passed} passed; {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }

    private static SaveLifecycleCoordinator<string[]> Flow(string root, Func<string[]> capture,
        Action<string, string, string[]> commit, Action<string, string>? select = null, Action? recover = null) =>
        new(() => root, capture, commit, select ?? ((_, _) => { }), recover ?? (() => { }));
    private static string Save(string root, string name, string contents)
    {
        string path = Path.Combine(root, name);
        File.WriteAllText(path, contents);
        return path;
    }
    private static void Run(string name, Action<string> body)
    {
        string root = Path.Combine(Path.GetTempPath(), "RealServers-SaveLifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { body(root); _passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { _failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
        finally
        {
            string verifiedRoot = Path.GetFullPath(root);
            if (!verifiedRoot.StartsWith(Path.Combine(Path.GetTempPath(), "RealServers-SaveLifecycle-"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Test cleanup escaped its temporary directory.");
            Directory.Delete(verifiedRoot, true);
        }
    }
    private static void True(bool condition) { if (!condition) throw new InvalidOperationException("Expected true"); }
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}"); }
}
