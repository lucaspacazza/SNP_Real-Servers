using System.Reflection;
using System.Runtime.CompilerServices;
using Il2CppInterop.Runtime.InteropTypes;
using Comparer = DataCenterAutomatorServers.ReferenceEqualityComparer;

int passed = 0, failed = 0;
var comparer = Comparer.Instance;
void Test(string name, Action body)
{
    try { body(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.GetBaseException().Message); }
}
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}."); }
NativeWrapper Native(long pointer, bool collected = false) => new(new IntPtr(pointer), collected);
object? Helper(string method, params object?[] arguments)
{
    Type type = Assembly.GetExecutingAssembly().GetType("DataCenterAutomatorServers.NativeReferenceIdentity")
        ?? throw new Exception("Native pointer identity policy has not been implemented.");
    return type.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments);
}

Test("Different wrappers for the same native server compare equal", () =>
{
    var first = Native(0x12345678); var second = Native(0x12345678);
    Check(!ReferenceEquals(first, second), "Fixture did not create two distinct managed wrappers.");
    Check(comparer.Equals(first, second) && comparer.Equals(second, first), "A recast server lost its physical ownership key.");
});
Test("Equal native pointers always produce the same dictionary hash", () =>
{
    Equal(comparer.GetHashCode(Native(0x12345678)), comparer.GetHashCode(Native(0x12345678)));
});
Test("Dictionary mount state survives a recast of its native server", () =>
{
    var mounts = new Dictionary<object, string>(comparer) { [Native(11)] = "exact-slot-42" };
    Check(mounts.TryGetValue(Native(11), out string? mount) && mount == "exact-slot-42", "Player/hook/resource wrappers cannot find their shared exact mount.");
    mounts[Native(11)] = "exact-slot-43";
    Equal(1, mounts.Count); Equal("exact-slot-43", mounts[Native(11)]);
});
Test("Prepared guards deduplicate every wrapper of one physical object", () =>
{
    var prepared = new HashSet<object>(comparer);
    for (int index = 0; index < 1_000; index++) prepared.Add(Native(99));
    Equal(1, prepared.Count); Check(prepared.Contains(Native(99)), "A fresh wrapper causes duplicate physical setup.");
});
Test("Removing state through another wrapper removes only that native instance", () =>
{
    var state = new Dictionary<object, string>(comparer) { [Native(1)] = "first", [Native(2)] = "second" };
    Check(state.Remove(Native(1)), "Pickup through another wrapper could not release its mount.");
    Equal(1, state.Count); Equal("second", state[Native(2)]);
});
Test("Distinct native instances remain distinct even when their hash collides", () =>
{
    var first = Native(0x100000001); var second = Native(1);
    Check(!comparer.Equals(first, second), "64-bit pointer comparison truncated the native identity.");
    var state = new HashSet<object>(comparer) { first, second }; Equal(2, state.Count);
});
Test("Ordinary managed objects use reference identity despite value equality", () =>
{
    var first = new EqualValue(7); var second = new EqualValue(7);
    Check(first.Equals(second), "Fixture does not expose value equality.");
    Check(!comparer.Equals(first, second) && comparer.Equals(first, first), "Managed DTOs were merged by value.");
    Equal(RuntimeHelpers.GetHashCode(first), comparer.GetHashCode(first));
});
Test("A managed DTO named Pointer is never treated as an Il2Cpp object", () =>
{
    var first = new PointerLookalike(42); var second = new PointerLookalike(42);
    Check(!comparer.Equals(first, second) && !comparer.Equals(first, Native(42)), "A managed pointer-like field aliased a native object.");
    comparer.GetHashCode(first); Equal(0, first.Reads); Equal(0, second.Reads);
});
Test("Two destroyed zero-pointer wrappers never become the same object", () =>
{
    var first = Native(0); var second = Native(0);
    Check(!comparer.Equals(first, second), "Zero-pointer destroyed objects collapsed into one ownership key.");
    Check(comparer.Equals(first, first), "An object's self identity must remain valid.");
    Equal(RuntimeHelpers.GetHashCode(first), comparer.GetHashCode(first));
});
Test("Collected wrappers retain managed self identity without throwing", () =>
{
    var first = Native(42, true); var second = Native(42, true);
    Check(!comparer.Equals(first, second) && comparer.Equals(first, first), "Collected wrappers cannot provide a shared native identity.");
    Equal(RuntimeHelpers.GetHashCode(first), comparer.GetHashCode(first));
});
Test("Null and identical object semantics remain those of a reference comparer", () =>
{
    var native = Native(42);
    Check(comparer.Equals(null, null), "Null/null should compare equal.");
    Check(!comparer.Equals(native, null) && !comparer.Equals(null, native), "Null cannot equal a live object.");
    Check(comparer.Equals(native, native), "Identical native wrapper did not compare equal.");
});
Test("Pure native identity helper rejects missing objects and zero pointers", () =>
{
    var first = new object(); var second = new object();
    Check((bool)Helper("AreSame", first, new IntPtr(8), second, new IntPtr(8))!, "Helper cannot compare two wrappers of one native object.");
    Check(!(bool)Helper("AreSame", first, IntPtr.Zero, second, IntPtr.Zero)!, "Helper merged destroyed wrappers.");
    Check(!(bool)Helper("AreSame", null, new IntPtr(8), second, new IntPtr(8))!, "Missing managed object cannot own a native identity.");
});
Test("Pure helper preserves full-width pointer equality and managed hashes", () =>
{
    var first = new object(); var second = new object();
    Check(!(bool)Helper("AreSame", first, new IntPtr(0x100000001), second, new IntPtr(1))!, "Helper truncated 64-bit pointers.");
    Equal((int)Helper("Hash", first, new IntPtr(8))!, (int)Helper("Hash", second, new IntPtr(8))!);
    Equal(RuntimeHelpers.GetHashCode(first), (int)Helper("Hash", first, IntPtr.Zero)!);
});

Console.WriteLine($"Reference identity: {passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;

sealed class NativeWrapper : Il2CppObjectBase
{
    internal NativeWrapper(IntPtr pointer, bool collected) : base(pointer, collected) { }
}
sealed record EqualValue(int Value);
sealed class PointerLookalike
{
    private readonly long _pointer;
    internal int Reads;
    internal PointerLookalike(long pointer) => _pointer = pointer;
    public IntPtr Pointer { get { Reads++; return new IntPtr(_pointer); } }
}
