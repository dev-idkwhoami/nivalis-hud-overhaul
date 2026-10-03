using System.Reflection;
using Nivalis.GhostSystem.Ai;
using Nivalis.Locale.UI;
using NivalisMods.HudOverhaul;
using PersonList = Il2CppSystem.Collections.Generic.List<Nivalis.GhostSystem.Ai.Person>;

var flags = BindingFlags.NonPublic | BindingFlags.Static;
var prefix = typeof(VendorSearch).GetMethod("Prefix", flags)!.CreateDelegate<Begin>();
var finalizer = typeof(VendorSearch).GetMethod("Finalizer", flags)!.CreateDelegate<Action<VendorFilteringUI, string?>>();
var checks = 0;
void Check(string name, bool ok) { if (!ok) throw new Exception(name); checks++; }
Person Seller(string name, int location, int type, params string[] items) => new()
{
    Name = name, Location = location, Type = type,
    Vendor = new Vendor { items = items.Select(name => new VendorItem { ItemType = new Item { Name = name }, CurrentStock = 0 }).ToDictionary(item => item.ItemType!) }
};
var amy = Seller("Amy", 1, 1, "Red Rice", "Tea");
var bob = Seller("Bob", 2, 1, "Red Rice");
var rice = Seller("Rice Merchant", 1, 2, "Soap");
var empty = new Person { Name = "Empty" };
var ui = new VendorFilteringUI();
PersonList Search(string query, params Person[] candidates)
{
    ui.searchField.m_Text = query;
    var people = new PersonList();
    people.AddRange(candidates);
    prefix(ui, people, out var state);
    try { ui.FilterResult(people); }
    finally { finalizer(ui, state); }
    Check("query restored exactly", ui.searchField.text == query);
    return people;
}
Check("names and sold-out item names match, preserving order", Search("rIcE", amy, bob, rice, empty).SequenceEqual(new[] { amy, bob, rice }));
Check("partial vendor name still works", Search("am", amy, bob).SequenceEqual(new[] { amy }));
Check("partial item names and trimmed input", Search("  TE  ", amy, bob).SequenceEqual(new[] { amy }));
ui.Location = 1;
Check("item matching respects native location filter", Search("rice", amy, bob, rice).SequenceEqual(new[] { amy, rice }));
ui.Type = 1;
Check("name and item matching respect both native filters", Search("rice", amy, bob, rice).SequenceEqual(new[] { amy }));
ui.Location = ui.Type = 0;
Check("empty query retains native results", Search("", amy, bob, empty).Count == 3);
Check("unknown items return no vendors", Search("nonexistent", amy, bob, empty).Count == 0);
ModOptions.Search.Value = false;
Check("disabled feature retains name-only search", Search("rice", amy, rice).SequenceEqual(new[] { rice }));
ModOptions.Search.Value = true;
var broken = new Person { Name = "Broken", FailLookup = true };
Check("lookup failure falls back without partially pruning candidates", Search("rice", amy, broken, rice).SequenceEqual(new[] { rice }));
Check("lookup error is reported", Plugin.Logger.Errors == 1);
ui.searchField.m_Text = "Tea";
var list = new PersonList { amy };
prefix(ui, list, out var saved);
try { throw new InvalidOperationException("Native failure"); }
catch (InvalidOperationException) { }
finally { finalizer(ui, saved); }
Check("native exception cleanup restores text", ui.searchField.text == "Tea");
Console.WriteLine($"Passed {checks} vendor search checks (production callbacks with managed fixtures).");

delegate void Begin(VendorFilteringUI ui, PersonList people, out string? state);
