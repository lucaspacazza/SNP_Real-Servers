using DataCenterAutomatorServers;

int passed = 0, failed = 0;
void Check(string name, Action check)
{
    try { check(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception exception) { Console.WriteLine("FAIL " + name + ": " + exception.Message); failed++; }
}
void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

Check("Reapply uses original chassis bottom, never a surviving RJ45 bottom", () =>
{
    var cache = new OriginalVisualBoundsCache<float>();
    Assert(cache.Capture(1, -0.11f) == -0.11f, "Initial chassis bottom missing.");
    Assert(cache.Capture(1, 0.02f) == -0.11f, "Port-only residual bounds replaced the chassis bottom.");
    Assert(cache.Capture(1, null) == -0.11f, "Hiding the native carrier discarded the original alignment.");
});
Check("Different live servers keep independent original bounds", () =>
{
    var cache = new OriginalVisualBoundsCache<float>();
    cache.Capture(1, -0.11f);
    cache.Capture(2, -0.23f);
    Assert(cache.Get(1) == -0.11f && cache.Get(2) == -0.23f, "Server alignment leaked between objects.");
});
Check("Unknown bounds stay unknown until a trustworthy initial capture", () =>
{
    var cache = new OriginalVisualBoundsCache<float>();
    Assert(cache.Get(7) == null && cache.Capture(7, null) == null, "Unknown carrier bounds became a fictitious bottom.");
    Assert(cache.Capture(7, 0f) == 0f, "Zero is a valid original bottom.");
});
Check("Scene reset prevents recycled instance IDs reusing old alignment", () =>
{
    var cache = new OriginalVisualBoundsCache<float>();
    cache.Capture(1, -0.11f);
    cache.Clear();
    Assert(cache.Get(1) == null && cache.Capture(1, -0.28f) == -0.28f, "Scene cache kept a destroyed object's bottom.");
});
Console.WriteLine($"Visual alignment cache: {passed}/{passed + failed} passed.");
return failed == 0 ? 0 : 1;
