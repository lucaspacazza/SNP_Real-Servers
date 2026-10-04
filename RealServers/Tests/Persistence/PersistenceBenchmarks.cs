using System.Diagnostics;
using DataCenterAutomatorServers;

internal static class PersistenceBenchmarks
{
    internal static int Run(bool assertBudget)
    {
        var spec = ServerVariantSpec.FindById("dell_r440")!;
        // No save reads/writes: isolate the in-memory Set + RecordIdentity cost.
        string root = Path.Combine(Path.GetTempPath(), "RealServers.PersistenceBenchmark.NotCreated");
        Console.WriteLine("Set + RecordIdentity: milliseconds; initial single, repeats median of 3; no native game serialization.");
        bool withinBudget = true;
        foreach (int count in new[] { 1000, 5000, 10000 })
        {
            var records = Enumerable.Range(1, count)
                .Select(i => new ServerIdentityRecord("Server.Green1_-" + i, "cargo-" + i, 0, "dell_r440"))
                .ToArray();
            var registry = new RuntimeVariantRegistry(root);
            double initial = Time(() => Capture(registry, records, spec));
            double stable = Median(() => Capture(registry, records, spec));
            int rackUid = 100;
            double changed = Median(() => {
                rackUid++;
                foreach (var record in records)
                {
                    registry.Set(record.ServerId, spec);
                    registry.RecordIdentity(record.ServerId, record.LooseId, rackUid, spec);
                }
            });
            if (registry.Count != count || records.Any(record => registry.GetByLooseId(record.LooseId)?.VariantId != "dell_r440"))
                throw new Exception("Large capture changed the number or model of physical objects.");
            Console.WriteLine($"objects={count} initial={initial:F2} stable={stable:F2} changedRack={changed:F2}");
            if (count == 10000 && Math.Max(initial, Math.Max(stable, changed)) > 1000)
                withinBudget = false;
        }
        if (assertBudget) Console.WriteLine((withinBudget ? "PASS" : "FAIL") + " 10,000-object capture completes within one second.");
        return !assertBudget || withinBudget ? 0 : 1;
    }

    private static void Capture(RuntimeVariantRegistry registry, ServerIdentityRecord[] records, ServerVariantSpec spec)
    {
        foreach (var record in records)
        {
            registry.Set(record.ServerId, spec);
            registry.RecordIdentity(record.ServerId, record.LooseId, record.RackPositionUid, spec);
        }
    }
    private static double Time(Action action)
    {
        var timer = Stopwatch.StartNew(); action(); timer.Stop(); return timer.Elapsed.TotalMilliseconds;
    }
    private static double Median(Action action)
    {
        double[] timings = Enumerable.Range(0, 3).Select(_ => Time(action)).OrderBy(x => x).ToArray();
        return timings[1];
    }
}
