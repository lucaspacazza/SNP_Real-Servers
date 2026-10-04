using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

internal static class Program
{
    private static int _passed, _failed;
    private static readonly Dictionary<short, OpCode> Opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!).ToDictionary(op => op.Value);

    private static int Main(string[] args)
    {
        Test("1,000 exact server lookups enumerate 100,000 rack slots only once", () =>
        {
            var slots = Enumerable.Range(1, 100_000).Select(uid => new Slot(uid)).ToArray();
            int scans = 0, uidReads = 0;
            var index = Index(() => { scans++; return slots; }, slot => { uidReads++; return slot.Uid; });
            var timer = Stopwatch.StartNew();
            for (int uid = 1; uid <= 1_000; uid++) Equal(uid, Matches(index, uid).Single().Uid);
            Equal(1, scans); Equal(slots.Length + 1_000, uidReads);
            Console.WriteLine($"BENCH slots={slots.Length}, queries=1000, sceneEnumerations={scans}, uidReads={uidReads}, elapsedMs={timer.ElapsedMilliseconds}");
        });
        Test("Building is lazy when direct native references already suffice", () =>
        {
            int scans = 0;
            Index(() => { scans++; return Array.Empty<Slot>(); });
            Equal(0, scans);
        });
        Test("Duplicated saved UIDs retain every exact candidate", () =>
        {
            var first = new Slot(42); var second = new Slot(42); var other = new Slot(43);
            var matches = Matches(Index(() => new[] { first, other, second }), 42);
            Equal(2, matches.Length); Check(ReferenceEquals(first, matches[0]) && ReferenceEquals(second, matches[1]), "Duplicate UID candidates were lost or replaced with another UID.");
        });
        Test("Missing saved UID never claims an unrelated slot", () =>
        {
            var index = Index(() => new[] { new Slot(1), new Slot(3) });
            Equal(0, Matches(index, 2).Length);
        });
        Test("Repeated missing UIDs permit at most one scene rebuild per batch", () =>
        {
            int scans = 0;
            var index = Index(() => { scans++; return new[] { new Slot(1) }; });
            for (int uid = 2; uid <= 1_000; uid++) Equal(0, Matches(index, uid).Length);
            Equal(2, scans);
        });
        Test("Destroyed cached slot falls back once to its exact live replacement", () =>
        {
            var stale = new Slot(7); var replacement = new Slot(7); var current = new[] { stale }; int scans = 0;
            var index = Index(() => { scans++; return current; });
            Check(ReferenceEquals(stale, Matches(index, 7).Single()), "Initial slot was not indexed.");
            stale.Alive = false; current = new[] { replacement, new Slot(8) };
            Check(ReferenceEquals(replacement, Matches(index, 7).Single()), "Stale index did not recover the exact replacement.");
            Equal(2, scans);
        });
        Test("A slot created after first lookup is discovered by the bounded fallback", () =>
        {
            var current = new[] { new Slot(1) }; int scans = 0;
            var index = Index(() => { scans++; return current; });
            Equal(1, Matches(index, 1).Single().Uid);
            current = new[] { new Slot(1), new Slot(9) };
            Equal(9, Matches(index, 9).Single().Uid); Equal(2, scans);
        });
        Test("Dead candidates are excluded even when an exact live duplicate remains", () =>
        {
            var dead = new Slot(5) { Alive = false }; var live = new Slot(5);
            Check(ReferenceEquals(live, Matches(Index(() => new[] { dead, live }), 5).Single()), "A destroyed candidate remained available.");
        });
        Test("UID mutation invalidates the old entry instead of restoring the wrong reservation", () =>
        {
            var slot = new Slot(5); var index = Index(() => new[] { slot });
            Equal(5, Matches(index, 5).Single().Uid); slot.Uid = 6;
            Equal(0, Matches(index, 5).Length); Equal(6, Matches(index, 6).Single().Uid);
        });
        Test("Nonpositive UIDs do not scan or return rack reservations", () =>
        {
            int scans = 0;
            var index = Index(() => { scans++; return new[] { new Slot(0), new Slot(-1), new Slot(4) }; });
            Equal(0, Matches(index, 0).Length); Equal(0, Matches(index, -1).Length); Equal(0, scans);
        });
        Test("Invalidation discards the old world's slot objects", () =>
        {
            var first = new Slot(11); var next = new Slot(11); var current = new[] { first }; int scans = 0;
            var index = Index(() => { scans++; return current; });
            Check(ReferenceEquals(first, Matches(index, 11).Single()), "Initial world was not indexed.");
            current = new[] { next }; Invoke(index, "Invalidate");
            Check(ReferenceEquals(next, Matches(index, 11).Single()), "Previous world slot leaked after invalidation."); Equal(2, scans);
        });
        Test("Candidate snapshots cannot be mutated to rewrite later lookups", () =>
        {
            var slot = new Slot(13); var index = Index(() => new[] { slot });
            object snapshot = Invoke(index, "GetCandidates", 13)!;
            if (snapshot is IList mutable)
            {
                try { mutable[0] = new Slot(99); } catch (NotSupportedException) { }
            }
            Check(ReferenceEquals(slot, Matches(index, 13).Single()), "Caller mutated the internal index.");
        });

        if (args.Length != 0)
        {
            using var stream = File.OpenRead(Path.GetFullPath(args[0])); using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();
            Test("Batch rack repair shares a lazily built scene index", () => Check(Calls(pe, metadata, "RepairLoadedRackState").Contains("CreateRackPositionIndex"), "Batch recovery has no shared exact rack index."));
            Test("Each exact server restore queries the shared index", () => Check(!Calls(pe, metadata, "TryRestoreExactLoadedServerRackState").Contains("FindUnityObjectsOfTypeAllDirect"), "Every pending server still scans every rack slot."));
            Test("A direct valid rack reference does not require a global scene scan", () => Check(!Calls(pe, metadata, "RestorePersistedRackMountState").Contains("FindUnityObjectsOfTypeAllDirect"), "Direct rack repair still unconditionally enumerates every slot."));
            Test("Rack reconciliation uses the idempotent occupancy guard", () => Check(Calls(pe, metadata, "ReconcileRackOccupancy").Contains("EnsureRackPositionOccupied"), "Already occupied slots are still marked on every reconciliation."));
            Test("Hover rack validation performs no Info logging", () => Check(!Calls(pe, metadata, "TryOverrideLenovoRackPositionAllowed").Contains("Info"), "Hover validation still writes an Info log for every frame."));
            Test("Pickup prefix does not rebuild controls before the native motion", () => Check(!Calls(pe, metadata, "PrepareLenovoMoveToHand").Contains("ConfigureServerLiveControls"), "Pickup controls are still rebuilt both before and after native MoveToHand."));
            Test("Known lifecycle instances repair native scalar and renderer resets without a full reconfigure", () =>
            {
                var calls = Calls(pe, metadata, "TryConfigurePendingLifecycleServer");
                Check(calls.Contains("ConfigureServer") && calls.Contains("EnsureVisualStillApplied"), "Known startup instances skip the inexpensive scalar/renderer restore guard.");
            });
        }
        Console.WriteLine($"Performance regressions: {_passed} passed, {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }

    private static object Index(Func<IEnumerable<Slot>> source, Func<Slot, int>? readUid = null)
    {
        var type = Assembly.GetExecutingAssembly().GetType("DataCenterAutomatorServers.ExactRackIndex`1")?.MakeGenericType(typeof(Slot))
            ?? throw new Exception("The bounded exact rack index has not been implemented.");
        return Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            new object[] { source, readUid ?? (slot => slot.Uid), (Func<Slot, bool>)(slot => slot.Alive) }, null)!;
    }
    private static object? Invoke(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, args);
    private static Slot[] Matches(object index, int uid) => ((IEnumerable)Invoke(index, "GetCandidates", uid)!).Cast<Slot>().ToArray();
    private static void Test(string name, Action test) { try { test(); _passed++; Console.WriteLine("PASS " + name); } catch (Exception error) { _failed++; Console.WriteLine("FAIL " + name + ": " + error.GetBaseException().Message); } }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}."); }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Slot { internal int Uid; internal bool Alive = true; internal Slot(int uid) => Uid = uid; }

    private static HashSet<string> Calls(PEReader pe, MetadataReader metadata, string methodName)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var type = metadata.TypeDefinitions.Select(handle => metadata.GetTypeDefinition(handle)).Single(definition => metadata.GetString(definition.Name) == "CatalogInjector");
        var method = type.GetMethods().Select(handle => metadata.GetMethodDefinition(handle)).Single(definition => metadata.GetString(definition.Name) == methodName);
        byte[] il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
        for (int offset = 0; offset < il.Length;)
        {
            short value = il[offset++] == 0xfe ? (short)(0xfe00 | il[offset++]) : (short)il[offset - 1];
            OpCode opcode = Opcodes[value];
            if (opcode.OperandType == OperandType.InlineMethod)
            {
                EntityHandle target = MetadataTokens.EntityHandle(BitConverter.ToInt32(il, offset));
                if (target.Kind == HandleKind.MethodDefinition) names.Add(metadata.GetString(metadata.GetMethodDefinition((MethodDefinitionHandle)target).Name));
                else if (target.Kind == HandleKind.MemberReference) names.Add(metadata.GetString(metadata.GetMemberReference((MemberReferenceHandle)target).Name));
            }
            offset += opcode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + BitConverter.ToInt32(il, offset) * 4,
                _ => 4
            };
        }
        return names;
    }
}
