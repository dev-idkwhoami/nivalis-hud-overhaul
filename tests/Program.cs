using NivalisMods.HudOverhaul;

if (args.Length == 2 && args[0] == "--verify-history")
{
    using var store = new HistoryStore(args[1]);
    Console.WriteLine("History log replay succeeded.");
    return;
}
if (args.Length == 1 && args[0] == "--benchmark-history")
{
    Directory.CreateDirectory("work/history");
    var path = Path.GetFullPath(Path.Combine("work/history", "hud-bench-" + Guid.NewGuid() + ".log"));
    try
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using (var store = new HistoryStore(path))
        {
            store.Fork("new");
            for (var batch = 0; batch < 400; batch++)
                store.Append(Enumerable.Range(0, 250).Select(i => new HistoryEvent
                { Kind = "meal_sale", Venue = "venue", Subject = "meal", Name = "Beer", Day = batch / 10, Quantity = 1, Money = 1600 }));
            store.Checkpoint("saved", "slot");
        }
        Console.WriteLine($"100,000 events in 400 durable batches: {clock.Elapsed.TotalSeconds:F2}s; {new FileInfo(path).Length / 1048576.0:F1} MiB");
        clock.Restart();
        using var reopened = new HistoryStore(path);
        Console.WriteLine($"Replay 100,000 events: {clock.Elapsed.TotalMilliseconds:F0}ms");
        reopened.Fork("saved");
        clock.Restart();
        var found = reopened.Read("meal_sale", "venue", 30, 37);
        Console.WriteLine($"Seven-day query: {clock.Elapsed.TotalMilliseconds:F1}ms, {found.Count} events");
        if (found.Count != 17500) throw new Exception("Benchmark query mismatch");
    }
    finally { File.Delete(path); }
    return;
}

var checks = 0;
void Check(string label, double?[] values, bool descending, params int[] expected)
{
    var before = values.ToArray();
    var actual = SortOrder.Indices(values, descending);
    if (!actual.SequenceEqual(expected))
        throw new Exception($"{label}: [{string.Join(",", actual)}] != [{string.Join(",", expected)}]");
    if (!values.SequenceEqual(before)) throw new Exception($"{label}: input data changed");
    if (actual.Distinct().Count() != values.Length) throw new Exception($"{label}: entries lost or duplicated");
    checks++;
}

Check("ascending numeric order", [50, 5, 20], false, 1, 2, 0);
Check("descending numeric order", [50, 5, 20], true, 0, 2, 1);
Check("ties keep native relevance", [3, 1, 3, 1], false, 1, 3, 0, 2);
Check("missing values remain last ascending", [null, 2, 1, null], false, 2, 1, 0, 3);
Check("missing values remain last descending", [null, 2, 1, null], true, 1, 2, 0, 3);
Check("NaN does not displace valid values", [double.NaN, 2, null, 1], true, 1, 3, 0, 2);
Check("empty filtered list", [], false);
Check("all unknown preserves original order", [null, double.NaN, null], true, 0, 1, 2);
Check("active tasks preserve within-group relevance", [0, 1, 1, 0], true, 1, 2, 0, 3);
Check("negative and infinite values", [double.NegativeInfinity, -2, double.PositiveInfinity, 0], true, 2, 3, 1, 0);
Console.WriteLine($"Passed {checks} sorting checks (including input preservation and permutation integrity).");

var window = new ReviewWindow();
void Geometry(string name, bool ok)
{
    if (!ok) throw new Exception(name);
    checks++;
}
window.Reset([], 10);
Geometry("empty review window", window.Height == 0 && window.Visible(0, 500) == (0, -1));
window.Reset([100, 200, 50], 10);
Geometry("variable row heights and gaps", window.Height == 370 && window.Top(2) == 320);
Geometry("row boundary lookup", window.At(109) == 0 && window.At(110) == 1 && window.At(320) == 2);
Geometry("elastic scroll beyond either end", window.At(-500) == 0 && window.At(5000) == 2);
Geometry("visible rows without buffer", window.Visible(120, 100, 0) == (1, 1));
Geometry("buffer bounded by list ends", window.Visible(120, 100) == (0, 2));
window.Reset(Enumerable.Repeat(100f, 10000), 10);
var range = window.Visible(550000, 600);
Geometry("large list uses bounded visible range", range.Last - range.First + 1 <= 11 && range.First > 4000);
window.Reset([300, 100], 5);
Geometry("remeasuring updates total and offsets", window.Height == 405 && window.Top(1) == 305);
Console.WriteLine($"Passed {checks} total sorting and virtual-list geometry checks.");

var labelsFolder = Path.Combine(Path.GetTempPath(), "hud-label-checks-" + Guid.NewGuid());
var labelsPath = Path.Combine(labelsFolder, Labels.FileName);
var warnings = new List<string>();
try
{
    Labels.Load(labelsPath, warnings.Add);
    Geometry("labels generated on first load", File.Exists(labelsPath) && Labels.Get("sort.price.asc") == "Price: low to high");
    var custom = "{\"sort.price.asc\":\"Preis: aufsteigend\",\"reviews.timestamp\":\"{day} — {time}\"}";
    File.WriteAllText(labelsPath, custom);
    Labels.Load(labelsPath, warnings.Add);
    Geometry("UTF-8 translations and missing-key fallback", Labels.Get("sort.price.asc") == "Preis: aufsteigend" && Labels.Get("journal.active") == "Active tasks first");
    Geometry("timestamp placeholders", Labels.ReviewDate("14:35", "Tag 24") == "Tag 24 — 14:35");
    Geometry("user translations never overwritten", File.ReadAllText(labelsPath) == custom);
    File.WriteAllText(labelsPath, "{\"sort.price.asc\":42,\"sort.count.asc\":\"\",\"reviews.timestamp\":\"bad\",\"reviews.positive\":\"Positiv zuerst\"}");
    Labels.Load(labelsPath, warnings.Add);
    Geometry("invalid entries fall back individually", Labels.Get("sort.price.asc") == "Price: low to high" && Labels.Get("sort.count.asc") == "Count: low to high" && Labels.ReviewDate("12:00", "Day 1") == "12:00 · Day 1" && Labels.Get("reviews.positive") == "Positiv zuerst");
    File.WriteAllText(labelsPath, "{broken");
    Labels.Load(labelsPath, warnings.Add);
    Geometry("malformed JSON preserved with English fallback", File.ReadAllText(labelsPath) == "{broken" && Labels.Get("reviews.positive") == "Positive first" && warnings.Count >= 4);
}
finally { Directory.Delete(labelsFolder, true); }
Console.WriteLine($"Passed {checks} total checks including editable labels.");

Geometry("tag matches without name match", SearchText.Matches("decoration", ["Wall lamp", "Decoration"]));
Geometry("recipe taste tags ignore case", SearchText.Matches("SWEET", ["Waffles", "Sweet", "Fruit"]));
Geometry("partial ingredient name", SearchText.Matches("tomat", ["Pasta", "Tomato"]));
Geometry("localized tag names", SearchText.Matches("süß", ["Waffeln", "Süß"]));
Geometry("equipment names match", SearchText.Matches("grill", ["Teppanyaki Grill"]));
Geometry("ordinary names still match", SearchText.Matches("raw milk", ["Raw Milk", "Seed"]));
Geometry("empty search keeps entries", SearchText.Matches("  ", []));
Geometry("unknown search rejects", !SearchText.Matches("unrelated", ["Waffles", "Sweet"]));
Geometry("query does not span fields", !SearchText.Matches("sweet fruit", ["Sweet", "Fruit"]));
Geometry("null metadata is harmless", !SearchText.Matches("fruit", [null, ""]));
Console.WriteLine($"Passed {checks} total checks including expanded search matching.");

var delay = new SearchDelay();
delay.Queue(1);
Geometry("debounce waits for typing pause", !delay.Take(1.20));
delay.Queue(1.20);
Geometry("new keystroke restarts debounce", !delay.Take(1.30));
Geometry("debounce fires after final keystroke", delay.Take(1.46));
Geometry("debounce fires only once", !delay.Take(2));
delay.Queue(3);
delay.Cancel();
Geometry("closing panel cancels pending search", !delay.Take(4));
delay.Queue(5, 0.75);
Geometry("configured debounce waits for requested delay", !delay.Take(5.70));
Geometry("configured debounce expires at requested delay", delay.Take(5.76));
Console.WriteLine($"Passed {checks} total checks including search debounce.");


var staffFolder = Path.Combine(Path.GetTempPath(), "hud-staff-checks-" + Guid.NewGuid());
var staffPath = Path.Combine(staffFolder, StaffOrder.FileName);
var staffWarnings = new List<string>();
try
{
    var staff = new StaffOrder(staffPath, staffWarnings.Add);
    Geometry("unarranged staff retain game order", staff.Resolve("venue-a", ["c", "a", "b"]).SequenceEqual(["c", "a", "b"]));
    Geometry("opening staff does not write preferences", !File.Exists(staffPath));
    staff.Remember("venue-a", ["b", "c", "a"]);
    Geometry("saved order survives native row refresh", staff.Resolve("venue-a", ["a", "b", "c"]).SequenceEqual(["b", "c", "a"]));
    staff = new StaffOrder(staffPath, staffWarnings.Add);
    Geometry("order survives restart", staff.Resolve("venue-a", ["a", "b", "c"]).SequenceEqual(["b", "c", "a"]));
    Geometry("new hire appends without shifting existing order", staff.Resolve("venue-a", ["d", "a", "b", "c"]).SequenceEqual(["b", "c", "a", "d"]));
    Geometry("firing removes employee without gap", staff.Resolve("venue-a", ["a", "c", "d"]).SequenceEqual(["c", "a", "d"]));
    Geometry("rehired employee goes to end", staff.Resolve("venue-a", ["a", "b", "c", "d"]).SequenceEqual(["c", "a", "d", "b"]));
    Geometry("venues keep independent order", staff.Resolve("venue-b", ["b", "a", "c"]).SequenceEqual(["b", "a", "c"]));
    Geometry("empty staff is safe", staff.Resolve("venue-a", []).Length == 0);
    Geometry("successful preference writes leave no temp file", !File.Exists(staffPath + ".tmp") && staffWarnings.Count == 0);
    File.WriteAllText(staffPath, "{broken");
    staff = new StaffOrder(staffPath, staffWarnings.Add);
    staff.Remember("venue-a", ["b", "a"]);
    Geometry("invalid preference file kept intact with session fallback", File.ReadAllText(staffPath) == "{broken" && staffWarnings.Count == 1 && staff.Resolve("venue-a", ["a", "b"]).SequenceEqual(["b", "a"]));
}
finally { if (Directory.Exists(staffFolder)) Directory.Delete(staffFolder, true); }
Console.WriteLine($"Passed {checks} total checks including staff order persistence and roster changes.");

// Exercise the same selection across HUD rows and compass destinations, including
// shared destinations, save reloads and the last pinned quest leaving the roster.
var questStates = new[] {
    new QuestPinState(11, true, false), new QuestPinState(22, true, false),
    new QuestPinState(33, true, false), new QuestPinState(44, false, true)
};
var markers = new[] { (Quest: 0L, Place: "shop"), (Quest: 11L, Place: "shared"),
    (Quest: 22L, Place: "shared"), (Quest: 33L, Place: "station") };
var originalMarkers = markers.ToArray();
var pins = new QuestPinFilter(questStates);
Geometry("no active pins preserve native quest and compass candidates", !pins.HasPins && markers.All(m => pins.ShowCompassMarker(m.Quest)) && questStates.All(q => pins.ShowQuest(q.QuestId)));
questStates[0] = questStates[0] with { Pinned = true };
pins = new QuestPinFilter(questStates);
Geometry("one pin excludes other HUD quests and synthetic reminders", pins.HasPins && pins.ShowQuest(11) && !pins.ShowQuest(22) && !pins.ShowQuest(0));
Geometry("ordinary compass destinations stay visible with pins", pins.ShowCompassMarker(0));
var destinations = markers.Where(m => pins.ShowCompassMarker(m.Quest)).GroupBy(m => m.Place).ToDictionary(g => g.Key, g => g.Select(m => m.Quest).ToArray());
Geometry("shared compass destination loses only unpinned quest numbers", destinations["shared"].SequenceEqual([11L]) && destinations.ContainsKey("shop") && !destinations.ContainsKey("station"));
Geometry("filtering never edits registered marker source", markers.SequenceEqual(originalMarkers));
questStates[2] = questStates[2] with { Pinned = true };
pins = new QuestPinFilter(questStates);
Geometry("multiple pins appear together", questStates.Where(q => pins.ShowQuest(q.QuestId)).Select(q => q.QuestId).SequenceEqual([11L, 33L]));
questStates[0] = questStates[0] with { Pinned = false };
pins = new QuestPinFilter(questStates);
Geometry("unpin one keeps filtering to remaining pin", !pins.ShowQuest(11) && pins.ShowQuest(33) && pins.HasPins);
questStates[2] = questStates[2] with { Pinned = false };
pins = new QuestPinFilter(questStates);
Geometry("unpin last restores all candidates", !pins.HasPins && markers.All(m => pins.ShowCompassMarker(m.Quest)));
questStates[2] = questStates[2] with { Pinned = true };
pins = new QuestPinFilter(questStates);
Geometry("loading native saved pins restores selection", pins.HasPins && pins.ShowQuest(33) && !pins.ShowQuest(11));
questStates[2] = questStates[2] with { Active = false };
pins = new QuestPinFilter(questStates);
Geometry("completed or failed last pin restores normal display", !pins.HasPins && pins.ShowQuest(11));
pins = new QuestPinFilter(Array.Empty<QuestPinState>());
Geometry("empty or newly loaded quest roster cannot retain previous pins", !pins.HasPins && pins.ShowCompassMarker(22));
Console.WriteLine($"Passed {checks} total checks including quest pin transitions and shared compass destinations.");


// Slot preferences must survive reload without replacing occupied slots or
// silently overwriting malformed files with an empty layout.
var slotsFolder = Path.Combine(Path.GetTempPath(), "hud-quick-slots-" + Guid.NewGuid());
var slotsPath = Path.Combine(slotsFolder, QuickSlots.FileName);
var slotWarnings = new List<string>();
try
{
    var slots = new QuickSlots(slotsPath, slotWarnings.Add);
    Geometry("fresh quick slots are empty", Enumerable.Range(0, QuickSlots.Count).All(i => slots[i] == null));
    Geometry("invalid assignments cannot write preferences", !slots.Assign(-1, "rod") && !slots.Assign(8, "rod") && !slots.Assign(0, " ") && !File.Exists(slotsPath));
    Geometry("first assignment persists without a temporary file", slots.Assign(2, "fishing-rod") && File.Exists(slotsPath) && !File.Exists(slotsPath + ".tmp"));
    var saved = File.ReadAllText(slotsPath);
    Geometry("occupied slot cannot be overwritten", !slots.Assign(2, "meal") && slots[2] == "fishing-rod" && File.ReadAllText(slotsPath) == saved);
    slots = new QuickSlots(slotsPath, slotWarnings.Add);
    Geometry("reloading preserves positions and empty slots", slots[2] == "fishing-rod" && Enumerable.Range(0, QuickSlots.Count).Where(i => i != 2).All(i => slots[i] == null));
    for (var i = 0; i < QuickSlots.Count; i++) if (i != 2) slots.Assign(i, "item-" + i);
    slots = new QuickSlots(slotsPath, slotWarnings.Add);
    Geometry("full wheel preserves every occupied slot", Enumerable.Range(0, QuickSlots.Count).All(i => slots[i] == (i == 2 ? "fishing-rod" : "item-" + i)) && !slots.Assign(7, "replacement"));
    var beforeUnassign = File.ReadAllText(slotsPath);
    Geometry("invalid removals leave preferences unchanged", !slots.Unassign(-1) && !slots.Unassign(8) && File.ReadAllText(slotsPath) == beforeUnassign);
    Geometry("occupied slot can be cleared", slots.Unassign(2) && slots[2] == null && !File.Exists(slotsPath + ".tmp"));
    slots = new QuickSlots(slotsPath, slotWarnings.Add);
    Geometry("removal persists without disturbing other slots", slots[2] == null && Enumerable.Range(0, QuickSlots.Count).Where(i => i != 2).All(i => slots[i] == "item-" + i));
    Geometry("empty removal is harmless and slot can be reassigned", !slots.Unassign(2) && slots.Assign(2, "meal") && new QuickSlots(slotsPath, slotWarnings.Add)[2] == "meal");
    Geometry("successful preference operations produce no warnings", slotWarnings.Count == 0);
    File.WriteAllText(slotsPath, "{broken");
    slots = new QuickSlots(slotsPath, slotWarnings.Add);
    Geometry("malformed preferences permit session assignment without overwriting", slots.Assign(0, "rod") && slots[0] == "rod" && File.ReadAllText(slotsPath) == "{broken" && slotWarnings.Count == 1);
    Geometry("session removal preserves malformed preferences", slots.Unassign(0) && slots[0] == null && File.ReadAllText(slotsPath) == "{broken");
    File.WriteAllText(slotsPath, "[null]");
    slots = new QuickSlots(slotsPath, slotWarnings.Add);
    Geometry("wrong slot count is rejected and preserved", slots.Assign(1, "rod") && File.ReadAllText(slotsPath) == "[null]" && slotWarnings.Count == 2);
}
finally { if (Directory.Exists(slotsFolder)) Directory.Delete(slotsFolder, true); }
Console.WriteLine($"Passed {checks} total checks including quick-slot persistence and occupied-slot protection.");


var sales = new MealSalesTotals();
sales.Add("a", "Noodles", 24, 4, 80, 24, 25, 23, 24, true);
sales.Add("a", "Noodles", 24, 2, 44, 24, 25, 23, 24, true);
sales.Add("a", "Noodles", 23, 7, 140, 24, 25, 23, 24, true);
Geometry("daily counts sum merged sales with yesterday comparison", sales.Find("Noodles", 124) is { Current: 6, Previous: 7, Compare: true });
Geometry("cost rows remain unchanged", sales.Find("Tomato", -10) == null);
var week = new MealSalesTotals();
foreach (var day in new[] { 14, 15, 21, 22, 25, 28, 29 }) week.Add("a", "Meal", day, 2, 10, 22, 29, 15, 22, true);
Geometry("week counts use complete calendar ranges with exclusive ends", week.Find("Meal", 30) is { Current: 6, Previous: 4 });
var month = new MealSalesTotals();
foreach (var day in new[] { 0, 1, 28, 31, 32, 59, 60 }) month.Add("a", "Meal", day, 1, 10, 32, 60, 1, 32, true);
Geometry("month comparison supports different month lengths", month.Find("Meal", 20) is { Current: 2, Previous: 3 });
var lifetime = new MealSalesTotals();
lifetime.Add("a", "Meal", 1, 5, 80, 0, 25, 0, 0, false);
lifetime.Add("a", "Meal", 24, 4, 60, 0, 25, 0, 0, false);
Geometry("lifetime has one count and no comparison", lifetime.Find("Meal", 140) is { Current: 9, Previous: 0, Compare: false });
var rollover = new MealSalesTotals();
rollover.Add("a", "Meal", 24, 6, 124, 25, 26, 24, 25, true);
rollover.Add("a", "Meal", 25, 1, 20, 25, 26, 24, 25, true);
Geometry("rollover moves old sales into prior period", rollover.Find("Meal", 20) is { Current: 1, Previous: 6 });
Geometry("venue isolation", new MealSalesTotals().Find("Meal", 20) == null);
sales.Add("b", "Noodles", 24, 1, 20, 24, 25, 23, 24, true);
Geometry("same name different revenue stays separate", sales.Find("Noodles", 20) is { Current: 1 });
sales.Add("c", "Noodles", 24, 2, 20, 24, 25, 23, 24, true);
Geometry("ambiguous rows cannot receive incorrect counts", sales.Find("Noodles", 20) == null);
var free = new MealSalesTotals();
free.Add("a", "Free", 1, 2, 0, 1, 2, 0, 1, true);
Geometry("free meals count normally", free.Find("Free", 0) is { Current: 2, Previous: 0 });
Console.WriteLine($"Passed {checks} total checks including calendar-period meal counts.");

Geometry("estimate excludes incomplete today and receipts older than seven days", !DemandEstimate.InHistory(30, 30) && !DemandEstimate.InHistory(22, 30) && DemandEstimate.InHistory(23, 30) && DemandEstimate.InHistory(29, 30));
Geometry("average includes zero-sale days", DemandEstimate.Daily(14, 30) == 2);
Geometry("new playthrough only uses completed existing days", DemandEstimate.Daily(6, 4) == 2);
Geometry("first day cannot divide by zero", DemandEstimate.Daily(0, 1) == 0 && !DemandEstimate.InHistory(1, 1));
var sharedTomatoes = DemandEstimate.Daily(14, 30) * 2 + DemandEstimate.Daily(7, 30) * 3;
Geometry("shared ingredients from two menu recipes combine before stock subtraction", DemandEstimate.Missing(DemandEstimate.Target(sharedTomatoes, 0), 3) == 4);
Geometry("small fractional needs round once after ingredient aggregation", DemandEstimate.Target(DemandEstimate.Daily(1, 30) * 2 + DemandEstimate.Daily(1, 30) * 3, 0) == 1);
Geometry("batch recipe output scales ingredient usage", DemandEstimate.Target(DemandEstimate.Daily(28, 30) / 2 * 3, 0) == 6);
Geometry("venue surplus never offsets another venue shortage", DemandEstimate.Missing(4, 10) + DemandEstimate.Missing(4, 1) == 3);
var combinedShortage = DemandEstimate.Missing(8, 2) + DemandEstimate.Missing(5, 1);
Geometry("carried inventory deducted once across venues", DemandEstimate.Missing(combinedShortage, 3) == 7);
Geometry("no-history fallback retains standard demand floor", DemandEstimate.Target(2, 10) == 10 && DemandEstimate.Target(14, 10) == 14);
Geometry("stock covering estimated demand yields no purchase", DemandEstimate.Missing(8, 8) == 0 && DemandEstimate.Missing(8, 12) == 0);
Geometry("known meal with zero recent sales has zero estimated need", DemandEstimate.Target(DemandEstimate.Daily(0, 30), 0) == 0);
Geometry("venue stock mode changes required purchase without changing target", DemandEstimate.ShoppingNeed(10, 6, true) == 4 && DemandEstimate.ShoppingNeed(10, 6, false) == 10);
Geometry("ignore stock includes fully stocked ingredients", DemandEstimate.ShoppingNeed(10, 20, false) == 10 && DemandEstimate.ShoppingNeed(10, 20, true) == 0);
Geometry("stock mode applies equally to estimated targets", DemandEstimate.ShoppingNeed(DemandEstimate.Target(DemandEstimate.Daily(21, 30), 0), 2, true) == 1 && DemandEstimate.ShoppingNeed(3, 2, false) == 3);
Geometry("ignore stock retains per-venue totals before carried inventory", DemandEstimate.Missing(DemandEstimate.ShoppingNeed(10, 6, false) + DemandEstimate.ShoppingNeed(5, 9, false), 4) == 11);
Console.WriteLine($"Passed {checks} total checks including estimated shopping demand.");

var payroll = new PayrollLedger();
payroll.Add("v1", "a", "Alice", 24, 1, -50);
payroll.Add("v1", "a", "Alice", 24, 1, -60);
payroll.Add("v1", "b", "Alice", 24, 1, -90);
payroll.Add("v2", "a", "Alice", 24, 1, -20);
Geometry("payroll groups employee payments per venue and day", payroll.Payments.Count == 3 && payroll.Payments[0].Count == 2 && payroll.Payments[0].Amount == -110);
Geometry("same employee names preserve separate identities", payroll.Payments[1].Person == "b" && payroll.Payments[1].Amount == -90);
payroll.Add("v1", "a", "Alice", 24, 0, -30);
payroll.Add("v1", "a", "Alice", 24, 1, 20);
Geometry("failed or invalid payments are ignored", payroll.Payments[0].Count == 2);
var payrollPath = Path.Combine(Path.GetTempPath(), "hud-payroll-" + Guid.NewGuid(), "slot.json");
try
{
    payroll.Save(payrollPath, "save-A");
    var restored = new PayrollLedger();
    restored.Load(payrollPath, "save-A");
    Geometry("payroll persists alongside the matching save", restored.Payments.Count == 3 && restored.Payments[0].Amount == -110);
    restored.Load(payrollPath, "replaced-save");
    Geometry("replaced or rolled-back saves reject stale payroll", restored.Payments.Count == 0);
    restored.Load(payrollPath + ".missing", "save-A");
    Geometry("saves without detail start with no attributed history", restored.Payments.Count == 0);
}
finally { Directory.Delete(Path.GetDirectoryName(payrollPath)!, true); }
var ingredients = new MealSalesTotals();
ingredients.Add("ingredient:carrot", "Carrot", 24, 6, -10, 24, 25, 23, 24, true);
ingredients.Add("ingredient:carrot", "Carrot", 24, 4, -10, 24, 25, 23, 24, true);
ingredients.Add("ingredient:carrot", "Carrot", 23, 3, -10, 24, 25, 23, 24, true);
Geometry("ingredients combine across dishes and compare periods", ingredients.Find("Carrot", -20) is { Current: 10, Previous: 3 });

Console.WriteLine($"Passed {checks} total checks including ingredient counts and save-bound payroll.");

HistoryChecks.Run(Geometry);
Console.WriteLine($"Passed {checks} total checks including file history, save branches and adjustment grouping.");

var farmPath = Path.Combine(Path.GetTempPath(), "hud-farm-targets-" + Guid.NewGuid());
try
{
    var targets = new FarmTargets(farmPath);
    Geometry("previously unconfigured module defaults to auto", targets.Get("unconfigured:type-A") == FarmTargets.Auto);
    targets.Load("slot-A", "content-A");
    targets.Set("module-1:type-A", "tomato");
    targets.Set("module-2:type-A", "carrot");
    var loaded = new FarmTargets(farmPath);
    loaded.Load("slot-A", "content-A");
    Geometry("farm targets persist independently for identical machine types", loaded.Get("module-1:type-A") == "tomato" && loaded.Get("module-2:type-A") == "carrot");
    loaded.Load("slot-B", "content-B");
    Geometry("another save defaults to auto instead of importing a manual target", loaded.Get("module-1:type-A") == FarmTargets.Auto);
    loaded.Load("slot-A", "older-content");
    Geometry("replaced or older save rejects stale farm targets", loaded.Get("module-1:type-A") == FarmTargets.Auto);
    loaded.Load("slot-A", "content-A");
    loaded.Set("module-1:type-A", null);
    targets.Load("slot-A", "content-A");
    Geometry("clearing a farm target is persisted", targets.Get("module-1:type-A") == null && targets.Get("module-2:type-A") == "carrot");
    loaded.Save("slot-C", "content-C");
    targets.Load("slot-C", "content-C");
    Geometry("save-as carries the current farm targets", targets.Get("module-2:type-A") == "carrot");
    targets.Clear();
    Geometry("new game defaults to automatic farm labels", targets.Get("module-2:type-A") == FarmTargets.Auto);
    targets.Set("new-module:type-C", "corn");
    targets.Save("first-save", "first-content");
    loaded.Load("first-save", "first-content");
    Geometry("targets selected before first game save are saved with it", loaded.Get("new-module:type-C") == "corn");
    targets.Load("auto-slot", "auto-content");
    targets.Set("automatic:type-A", FarmTargets.Auto);
    loaded.Load("auto-slot", "auto-content");
    Geometry("automatic mode survives a reload", loaded.Get("automatic:type-A") == FarmTargets.Auto);
    loaded.Set("automatic:type-A", "tomato");
    targets.Load("auto-slot", "auto-content");
    Geometry("manual target replaces automatic mode", targets.Get("automatic:type-A") == "tomato");
    targets.Set("automatic:type-A", FarmTargets.Auto);
    targets.Set("automatic:type-A", null);
    loaded.Load("auto-slot", "auto-content");
    Geometry("remove disables automatic mode persistently", loaded.Get("automatic:type-A") == null);
    var slotCFile = Path.Combine(farmPath, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("slot-C"))) + ".json");
    // A blocked atomic replacement must roll back the in-memory edit.
    loaded.Load("slot-C", "content-C");
    File.Delete(slotCFile); Directory.CreateDirectory(slotCFile);
    var failed = false;
    try { loaded.Set("module-2:type-A", "egg"); } catch (IOException) { failed = true; }
    Geometry("failed farm target save preserves the previous selection", failed && loaded.Get("module-2:type-A") == "carrot");
    failed = false;
    try { loaded.Set("never-set:type-A", null); } catch (IOException) { failed = true; }
    Geometry("failed clearing preserves an unconfigured module's auto default", failed && loaded.Get("never-set:type-A") == FarmTargets.Auto);
    Geometry("failed writes do not alter an explicitly blank screen", loaded.Get("module-1:type-A") == null);
}
finally { if (Directory.Exists(farmPath)) Directory.Delete(farmPath, true); }
Console.WriteLine($"Passed {checks} checks including farm target persistence and save isolation.");


var migrationRoot = Path.Combine(Path.GetTempPath(), "hud-migration-" + Guid.NewGuid());
try
{
    Directory.CreateDirectory(migrationRoot);
    var oldConfig = Path.Combine(migrationRoot, "local.nivalis.hudoverhaul.cfg");
    File.WriteAllText(oldConfig, "[Features]\nQuickActions = false\n");
    var historyBytes = new byte[] { 0, 1, 255, 80, 40, 0 };
    File.WriteAllBytes(Path.Combine(migrationRoot, "HUDOverhaul.history.log"), historyBytes);
    Directory.CreateDirectory(Path.Combine(migrationRoot, "HUDOverhaul.farm-targets", "nested"));
    File.WriteAllText(Path.Combine(migrationRoot, "HUDOverhaul.farm-targets", "nested", "save.json"), "target-data");
    File.WriteAllText(Path.Combine(migrationRoot, "OtherMod.cfg"), "unrelated");
    ModStorage.Initialize(migrationRoot);
    Geometry("config migration preserves settings", File.ReadAllText(ModStorage.FilePath("HUDOverhaul.cfg")).Contains("QuickActions = false"));
    Geometry("history migration preserves bytes", File.ReadAllBytes(ModStorage.FilePath("HUDOverhaul.history.log")).SequenceEqual(historyBytes));
    Geometry("nested save data migrated", File.ReadAllText(ModStorage.FilePath("HUDOverhaul.farm-targets/nested/save.json")) == "target-data");
    Geometry("legacy files and empty directories removed", !File.Exists(oldConfig) && !Directory.Exists(Path.Combine(migrationRoot, "HUDOverhaul.farm-targets")));
    Geometry("unrelated config retained", File.ReadAllText(Path.Combine(migrationRoot, "OtherMod.cfg")) == "unrelated");
    ModStorage.Initialize(migrationRoot);
    Geometry("migration repeat preserves history", File.ReadAllBytes(ModStorage.FilePath("HUDOverhaul.history.log")).SequenceEqual(historyBytes));
    File.Copy(ModStorage.FilePath("HUDOverhaul.cfg"), oldConfig);
    ModStorage.Initialize(migrationRoot);
    Geometry("identical legacy duplicate safely removed", !File.Exists(oldConfig));
    File.WriteAllText(oldConfig, "different settings");
    var oldLabels = Path.Combine(migrationRoot, "HUDOverhaul.labels.json");
    File.WriteAllText(oldLabels, "translation");
    var conflict = false;
    try { ModStorage.Initialize(migrationRoot); } catch (IOException) { conflict = true; }
    Geometry("conflicts stop migration without replacing data", conflict && File.ReadAllText(oldConfig) == "different settings" && File.ReadAllText(ModStorage.FilePath("HUDOverhaul.cfg")).Contains("QuickActions = false"));
    Geometry("conflict preflight leaves other files untouched", File.Exists(oldLabels) && !File.Exists(ModStorage.FilePath("HUDOverhaul.labels.json")));
}
finally { Directory.Delete(migrationRoot, true); }
Console.WriteLine($"Passed {checks} checks including config and history migration.");
