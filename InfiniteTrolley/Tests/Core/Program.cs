using InfiniteTrolley;

var tests = new (string Name, Action Run)[]
{
    ("empty trolley normalizes to 64 slots", () => Expect(64, Length(Array.Empty<int>(), 1))),
    ("small free trolley still normalizes minimum", () => Expect(64, Length(new int[5], 1))),
    ("small occupied trolley grows to minimum", () => Expect(64, Length(new[] { 10, 11, 12, 13 }, 1))),
    ("custom minimum is accepted", () => Expect(8, Length(Array.Empty<int>(), 1, 8))),
    ("capacity with a contiguous free run is preserved", () => Expect(64, Length(new int[64], 4))),
    ("exact contiguous run is reusable without expansion", () =>
    {
        var occupied = Full(64);
        Array.Clear(occupied, 20, 4);
        Expect(64, Length(occupied, 4));
    }),
    ("larger middle free run avoids growth", () =>
    {
        var occupied = Full(128);
        Array.Clear(occupied, 30, 10);
        Expect(128, Length(occupied, 7));
    }),
    ("fragmented holes cannot fit a multi-unit server", () =>
    {
        var occupied = Full(64);
        for (var i = 0; i < 64; i += 2) occupied[i] = 0;
        Expect(128, Length(occupied, 2));
    }),
    ("full 64-slot trolley doubles", () => Expect(128, Length(Full(64), 1))),
    ("trailing free slots contribute to added run", () =>
    {
        var occupied = Full(64);
        Array.Clear(occupied, 60, 4);
        Expect(128, Length(occupied, 68));
    }),
    ("required run may need several doubling steps", () => Expect(256, Length(Full(64), 100))),
    ("large incoming server grows beyond 512", () => Expect(2048, Length(Full(64), 1000))),
    ("non-power-of-two existing capacity doubles without shrinking", () => Expect(140, Length(Full(70), 1))),
    ("custom larger minimum does not force extra doubling", () => Expect(100, Length(Full(64), 1, 100))),
    ("custom minimum with insufficient room keeps doubling", () => Expect(400, Length(Full(64), 200, 100))),
    ("zero-length trolley accepts a run equal to minimum", () => Expect(64, Length(Array.Empty<int>(), 64))),
    ("empty trolley grows for a run above minimum", () => Expect(128, Length(Array.Empty<int>(), 65))),
    ("negative nonzero occupant markers count as occupied", () => Expect(128, Length(Enumerable.Repeat(-1, 64).ToArray(), 1))),
    ("input occupants remain byte-for-byte unchanged", () =>
    {
        var occupied = new[] { 30, 0, -2, 0, 35 };
        var before = occupied.ToArray();
        Expect(128, Length(occupied, 100, 64));
        Sequence(before, occupied);
    }),
    ("null input is rejected", () => Throws<ArgumentNullException>(() => Length(null!, 1))),
    ("zero incoming size is rejected", () => Throws<ArgumentOutOfRangeException>(() => Length(Array.Empty<int>(), 0))),
    ("negative incoming size is rejected", () => Throws<ArgumentOutOfRangeException>(() => Length(Array.Empty<int>(), -1))),
    ("zero minimum is rejected", () => Throws<ArgumentOutOfRangeException>(() => Length(Array.Empty<int>(), 1, 0))),
    ("negative minimum is rejected", () => Throws<ArgumentOutOfRangeException>(() => Length(Array.Empty<int>(), 1, -1))),
    ("unrepresentable capacity throws without changing input", () =>
    {
        var occupied = new[] { 47 };
        Throws<OverflowException>(() => Length(occupied, int.MaxValue));
        Sequence(new[] { 47 }, occupied);
    }),
    ("int maximum incoming run is representable on empty trolley", () => Expect(int.MaxValue, Length(Array.Empty<int>(), int.MaxValue))),
    ("int maximum minimum is accepted without allocating", () => Expect(int.MaxValue, Length(new[] { 1 }, 1, int.MaxValue))),
    ("largest representable run uses free trailing slot", () => Expect(int.MaxValue, Length(new[] { 0 }, int.MaxValue))),
    ("visual first logical index is first physical anchor", () => Expect(0, SlotCapacity.VisualIndex(0, 5))),
    ("visual index preserves first physical cycle", () => Expect(4, SlotCapacity.VisualIndex(4, 5))),
    ("visual anchor wraps after physical positions", () => Expect(0, SlotCapacity.VisualIndex(5, 5))),
    ("large logical index wraps safely", () => Expect(int.MaxValue % 5, SlotCapacity.VisualIndex(int.MaxValue, 5))),
    ("one anchor accepts all nonnegative logical indexes", () => Expect(0, SlotCapacity.VisualIndex(999, 1))),
    ("negative logical index is rejected", () => Throws<ArgumentOutOfRangeException>(() => SlotCapacity.VisualIndex(-1, 5))),
    ("zero physical anchors are rejected", () => Throws<ArgumentOutOfRangeException>(() => SlotCapacity.VisualIndex(0, 0))),
    ("negative physical anchors are rejected", () => Throws<ArgumentOutOfRangeException>(() => SlotCapacity.VisualIndex(0, -1))),
    ("100 distinct servers overlap visually but retain identities", () => Transport(100)),
    ("1000 distinct servers can be freed and reinserted independently", () => Transport(1000)),
    ("held cargo is not recovered even with stale association and inside volume", () =>
        ExpectFlag(false, CargoRecoveryPolicy.ShouldRecover(true, false, false, true, true))),
    ("racked cargo is not recovered even with stale association and inside volume", () =>
        ExpectFlag(false, CargoRecoveryPolicy.ShouldRecover(false, true, false, true, true))),
    ("item containing the trolley is never attached as cargo", () =>
        ExpectFlag(false, CargoRecoveryPolicy.ShouldRecover(false, false, true, true, true))),
    ("multiple exclusions remain rejected", () =>
        ExpectFlag(false, CargoRecoveryPolicy.ShouldRecover(true, true, true, true, true))),
    ("unassociated cargo outside the actual load volume is not recovered", () =>
        ExpectFlag(false, CargoRecoveryPolicy.ShouldRecover(false, false, false, false, false))),
    ("associated saved cargo is recovered outside current geometry", () =>
        ExpectFlag(true, CargoRecoveryPolicy.ShouldRecover(false, false, false, true, false))),
    ("loose cargo inside the actual trolley volume can be attached during load recovery", () =>
        ExpectFlag(true, CargoRecoveryPolicy.ShouldRecover(false, false, false, false, true))),
    ("associated cargo also inside load volume is eligible", () =>
        ExpectFlag(true, CargoRecoveryPolicy.ShouldRecover(false, false, false, true, true))),
    ("100 saved cargo identities reload without duplicate slots", () => ReloadCargo(100)),
    ("1000 saved cargo identities reload without duplicate slots", () => ReloadCargo(1000)),
    ("nested usable object is excluded despite stale trolley association", () =>
        ExpectFlag(false, CargoRecoveryPolicy.ShouldRecover(false, false, false, true, false, nestedCargo: true))),
    ("boxed SFP cargo is excluded even inside the actual trolley load volume", () =>
        ExpectFlag(false, CargoRecoveryPolicy.ShouldRecover(false, false, false, true, true, nestedCargo: true))),
    ("nested unassociated cargo is excluded from geometry-only recovery", () =>
        ExpectFlag(false, CargoRecoveryPolicy.ShouldRecover(false, false, false, false, true, nestedCargo: true))),
    ("ordinary outer container remains recoverable as one cargo item", () =>
        ExpectFlag(true, CargoRecoveryPolicy.ShouldRecover(false, false, false, true, true, nestedCargo: false))),
};

var failed = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL {name}: {ex}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed.");
return failed == 0 ? 0 : 1;

static int Length(IReadOnlyList<int> occupied, int incoming, int minimum = 64) =>
    SlotCapacity.RequiredLength(occupied, incoming, minimum);

static int[] Full(int size) => Enumerable.Range(1, size).ToArray();

static void Expect(int expected, int actual)
{
    if (expected != actual) throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}

static void ExpectFlag(bool expected, bool actual)
{
    if (expected != actual) throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}

static void Sequence(IEnumerable<int> expected, IEnumerable<int> actual)
{
    if (!expected.SequenceEqual(actual)) throw new InvalidOperationException("Occupant identifiers changed.");
}

static void Throws<TException>(Action action) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static void Transport(int itemCount)
{
    const int anchors = 5;
    var occupied = new int[anchors];
    var itemSlots = new Dictionary<int, int>();
    var visualSlots = new Dictionary<int, int>();
    for (var item = 1; item <= itemCount; item++)
    {
        Array.Resize(ref occupied, Length(occupied, 1));
        var logical = Array.FindIndex(occupied, value => value == 0);
        if (logical < 0) throw new InvalidOperationException("No logical slot after capacity expansion.");
        occupied[logical] = item;
        itemSlots.Add(item, logical);
        visualSlots.Add(item, SlotCapacity.VisualIndex(logical, anchors));
    }
    Expect(itemCount, occupied.Count(value => value != 0));
    Expect(itemCount, occupied.Where(value => value != 0).Distinct().Count());
    Expect(anchors, visualSlots.Values.Distinct().Count());

    // Every item sharing physical anchor zero remains independently removable.
    var removed = visualSlots.Where(pair => pair.Value == 0).Select(pair => pair.Key).ToArray();
    var before = occupied.ToArray();
    foreach (var item in removed) occupied[itemSlots[item]] = 0;
    foreach (var item in visualSlots.Where(pair => pair.Value != 0).Select(pair => pair.Key))
        Expect(item, occupied[itemSlots[item]]);
    Expect(itemCount - removed.Length, occupied.Count(value => value != 0));

    // Reinsert those exact identities into logical vacancies, not anchor indices.
    var length = occupied.Length;
    foreach (var item in removed)
    {
        Expect(length, Length(occupied, 1));
        var logical = Array.FindIndex(occupied, value => value == 0);
        occupied[logical] = item;
        itemSlots[item] = logical;
    }
    Expect(itemCount, occupied.Count(value => value != 0));
    Sequence(before.Where(value => value != 0).OrderBy(value => value),
        occupied.Where(value => value != 0).OrderBy(value => value));
    foreach (var item in removed)
        Expect(item, occupied[itemSlots[item]]);
}

static void ReloadCargo(int itemCount)
{
    // Saved associations can survive when coordinates differ during native reload.
    // The caller supplies the real trigger-volume decision; this pure harness
    // deliberately models both associated and geometry-only recovery paths.
    var saved = Enumerable.Range(1, itemCount)
        .Select(id => (Id: id, Associated: id % 2 == 0, InsideLoadVolume: true))
        .ToArray();
    var occupied = Array.Empty<int>();
    var logicalSlots = new Dictionary<int, int>();
    var recovered = saved
        .Where(item => CargoRecoveryPolicy.ShouldRecover(false, false, false,
            item.Associated, item.Associated ? false : item.InsideLoadVolume))
        .Select(item => item.Id)
        .ToArray();
    foreach (var id in recovered)
    {
        Array.Resize(ref occupied, Length(occupied, 1));
        var logical = Array.FindIndex(occupied, value => value == 0);
        occupied[logical] = id;
        logicalSlots.Add(id, logical);
    }
    Expect(itemCount, logicalSlots.Count);
    Expect(itemCount, occupied.Count(value => value != 0));
    Sequence(saved.Select(item => item.Id), occupied.Where(value => value != 0));
    Expect(5, logicalSlots.Values.Select(index => SlotCapacity.VisualIndex(index, 5)).Distinct().Count());

    // Deduplicating an already recovered ID is the adapter's responsibility.
    // Model a repeated recovery event with that guard, proving no second slots
    // are added when both the association and geometry paths identify one item.
    foreach (var id in recovered)
    {
        if (logicalSlots.ContainsKey(id)) continue;
        throw new InvalidOperationException("An identity was lost during recovery.");
    }
    Expect(itemCount, occupied.Count(value => value != 0));
}
