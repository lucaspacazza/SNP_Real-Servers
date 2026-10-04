using AutoCustomerDhcp;

var tests = new (string Name, Action Run)[]
{
    ("null current IP uses first free address", () =>
        Expect(new AddressChoice("10.42.0.1", 4), Choose(Pool(4, 1, "10.42.0.1", "10.42.0.2")))),
    ("client change replaces an IP outside the selected client pool", () =>
        Expect(new AddressChoice("172.18.0.1", 3), Choose(Pool(3, 1, "172.18.0.1"), current: "10.42.0.2"))),
    ("preserves a valid available IP even when preferred app has other IPs", () =>
        Expect(new AddressChoice("10.42.0.8", 8), AddressAllocator.Choose(
            new[] { Pool(8, 1, "10.42.0.8"), Pool(3, 1, "10.42.1.1") }, 1, 3, "10.42.0.8", _ => false))),
    ("skips addresses occupied by other servers", () =>
        Expect(new AddressChoice("10.42.0.2", 4), Choose(Pool(4, 1, "10.42.0.1", "10.42.0.2"), occupied: ip => ip == "10.42.0.1"))),
    ("a conflicting current IP is replaced", () =>
        Expect(new AddressChoice("10.42.0.2", 4), Choose(Pool(4, 1, "10.42.0.1", "10.42.0.2"), current: "10.42.0.1", occupied: ip => ip == "10.42.0.1"))),
    ("pool exhaustion returns null and leaves inputs unchanged", () =>
    {
        var addresses = new[] { "10.42.0.1", "10.42.0.2" };
        var pools = new[] { new AddressPool(4, 1, addresses) };
        Expect(null, AddressAllocator.Choose(pools, 1, 4, "10.42.0.1", _ => true));
        if (!addresses.SequenceEqual(new[] { "10.42.0.1", "10.42.0.2" }))
            throw new InvalidOperationException("Input addresses changed.");
        if (pools[0].AppId != 4 || pools[0].ServerType != 1)
            throw new InvalidOperationException("Input pool changed.");
    }),
    ("does not choose a pool of another server type", () =>
        Expect(null, AddressAllocator.Choose(new[] { Pool(4, 2, "10.42.0.1") }, 1, 4, "10.42.0.1", _ => false))),
    ("prefers a compatible selected app regardless of input ordering", () =>
        Expect(new AddressChoice("10.42.3.1", 3), AddressAllocator.Choose(
            new[] { Pool(1, 1, "10.42.1.1"), Pool(3, 1, "10.42.3.1") }, 1, 3, null, _ => false))),
    ("falls back to ascending app ID when preferred app has no free address", () =>
        Expect(new AddressChoice("10.42.1.1", 1), AddressAllocator.Choose(
            new[] { Pool(8, 1, "10.42.8.1"), Pool(3, 1, "10.42.3.1"), Pool(1, 1, "10.42.1.1") },
            1, 3, null, ip => ip == "10.42.3.1"))),
    ("incompatible preferred app falls back to matching type", () =>
        Expect(new AddressChoice("10.42.1.1", 1), AddressAllocator.Choose(
            new[] { Pool(3, 2, "10.42.3.1"), Pool(8, 1, "10.42.8.1"), Pool(1, 1, "10.42.1.1") },
            1, 3, null, _ => false))),
    ("missing preferred app falls back to ascending app ID", () =>
        Expect(new AddressChoice("10.42.1.1", 1), AddressAllocator.Choose(
            new[] { Pool(8, 1, "10.42.8.1"), Pool(1, 1, "10.42.1.1") }, 1, 99, null, _ => false))),
    ("duplicate current IP chooses preferred compatible app", () =>
        Expect(new AddressChoice("10.42.0.2", 3), AddressAllocator.Choose(
            new[] { Pool(8, 1, "10.42.0.1", "10.42.0.2"), Pool(3, 1, "10.42.0.2") },
            1, 3, "10.42.0.2", _ => false))),
    ("empty pools return null", () =>
        Expect(null, AddressAllocator.Choose(Array.Empty<AddressPool>(), 1, 4, null, _ => false))),
    ("empty pool addresses return null", () =>
        Expect(null, Choose(Pool(4, 1)))),
    ("null pool and address lists are ignored", () =>
        Expect(new AddressChoice("10.42.0.1", 4), AddressAllocator.Choose(
            new AddressPool[] { null!, new(1, 1, null!), Pool(4, 1, "10.42.0.1") }, 1, 4, null, _ => false))),
    ("null input pool collection is rejected", () =>
        Throws<ArgumentNullException>(() => AddressAllocator.Choose(null!, 1, 4, null, _ => false))),
    ("null occupancy callback is rejected", () =>
        Throws<ArgumentNullException>(() => AddressAllocator.Choose(Array.Empty<AddressPool>(), 1, 4, null, null!))),
    ("IPv4 upper octet boundary is accepted from game supplied pools", () =>
        Expect(new AddressChoice("255.255.255.255", 4), Choose(Pool(4, 1, "255.255.255.255")))),
    ("IPv4 lower octet boundary is accepted except the all-zero placeholder", () =>
        Expect(new AddressChoice("0.0.0.1", 4), Choose(Pool(4, 1, "0.0.0.0", "0.0.0.1"), current: "0.0.0.0"))),
    ("invalid pool addresses are ignored before checking occupancy", () =>
    {
        var seen = new List<string>();
        var pool = Pool(4, 1, null!, "", " ", "0.0.0.0", "10.0.0", "10.0.0.1.2", "10..0.1", "10.0.0.256", "10.0.0.-1", "10.0.0.+1",
            "10.0.0.1 ", " 10.0.0.1", "0x0A.0.0.1", "10.00.0.1", "10.0.0.0001", "10.a.0.1", "::1", "10.0.0.2");
        Expect(new AddressChoice("10.0.0.2", 4), Choose(pool, occupied: ip => { seen.Add(ip); return false; }));
        if (!seen.SequenceEqual(new[] { "10.0.0.2" }))
            throw new InvalidOperationException("Invalid addresses reached the occupancy callback.");
    }),
    ("invalid current IP is never preserved", () =>
        Expect(new AddressChoice("10.42.0.1", 4), Choose(Pool(4, 1, "127.1", "10.42.0.1"), current: "127.1"))),
};

var failed = 0;
foreach (var (name, run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed.");
return failed == 0 ? 0 : 1;

static AddressPool Pool(int appId, int serverType, params string[] addresses) => new(appId, serverType, addresses);

static AddressChoice? Choose(AddressPool pool, string? current = null, Func<string, bool>? occupied = null) =>
    AddressAllocator.Choose(new[] { pool }, 1, 4, current, occupied ?? (_ => false));

static void Expect(AddressChoice? expected, AddressChoice? actual)
{
    if (expected != actual)
        throw new InvalidOperationException($"Expected {expected?.ToString() ?? "null"}, got {actual?.ToString() ?? "null"}.");
}

static void Throws<TException>(Action action) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}
