using System.Reflection;
using Nivalis.UI.InGameMenu.CharacterWindow;
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
var productsPrefix = typeof(VendorProducts).GetMethod("Prefix", flags)!.CreateDelegate<ProductsBegin>();
var productsRefresh = typeof(VendorProductsRefresh).GetMethod("Postfix", flags)!.CreateDelegate<Action<CharacterWindowUi>>();
var window = new CharacterWindowUi();
var details = window.vendorDetails;
details.Parent = window;
string? ProductQuery(string query, Person seller, string? native = null)
{
    window.vendorFilteringUI.searchField.m_Text = query;
    productsPrefix(details, seller, ref native);
    return native;
}
Check("ingredient query reaches native product filter", ProductQuery("rice", amy) == "rice");
Check("trimmed partial product query reaches native filter", ProductQuery("  TE  ", amy) == "TE");
Check("sold-out catalogue products still select product filter", ProductQuery("Tea", amy) == "Tea");
var nameAndItem = Seller("Rice Merchant", 1, 1, "Red Rice", "Tea");
Check("product match takes priority over a vendor-name match", ProductQuery("Rice", nameAndItem) == "Rice");
Check("vendor-name-only query keeps full catalogue", ProductQuery("Amy", amy, "Amy") == null);
Check("empty query restores full catalogue", ProductQuery("", amy, "rice") == null);
Check("whitespace restores full catalogue", ProductQuery("   ", amy, "rice") == null);
Check("query does not carry over between vendors", ProductQuery("Tea", amy) == "Tea" && ProductQuery("Bob", bob) == null);
Check("query retains game-provided unmatched filter", ProductQuery("missing", amy, "missing") == "missing");
ModOptions.Search.Value = false;
Check("disabled feature leaves native product filter untouched", ProductQuery("Tea", amy, "native") == "native");
ModOptions.Search.Value = true;
window.vendorCharactersToggle.isOn = false;
Check("story/staff view is untouched", ProductQuery("Tea", amy, "native") == "native");
window.vendorCharactersToggle.isOn = true;
details.Parent = null;
Check("unrelated vendor details are untouched", ProductQuery("Tea", amy, "native") == "native");
details.Parent = window;
Check("missing catalogue retains native query", ProductQuery("Tea", empty, "native") == "native");
Check("lookup exception retains native query", ProductQuery("Tea", broken, "native") == "native");
details._person = amy;
window.vendorFilteringUI.searchField.m_Text = "Tea";
window.vendorProductsListScrollRect.velocity = new UnityEngine.Vector2 { y = 10 };
productsRefresh(window);
Check("filtered products start at the top without inertia", window.vendorProductsListScrollRect.verticalNormalizedPosition == 1 && window.vendorProductsListScrollRect.velocity.y == 0);
Check("query refresh updates already-selected vendor", details.Initializations == 1 && details.LastFilter == "Tea");
window.vendorFilteringUI.searchField.m_Text = "";
productsRefresh(window);
Check("clearing query rebuilds same vendor's products", details.Initializations == 2 && details.LastFilter == null);
details.canvasGroup.alpha = 0;
productsRefresh(window);
Check("hidden details are not resurrected", details.Initializations == 2);
details.canvasGroup.alpha = 1;
window.isActiveAndEnabled = false;
productsRefresh(window);
Check("inactive windows are not refreshed", details.Initializations == 2);
window.isActiveAndEnabled = true;
ModOptions.Search.Value = false;
productsRefresh(window);
Check("disabled search does not intercept list refresh", details.Initializations == 2);
UnityEngine.Resources.Objects = new object[] { window };
VendorProducts.RefreshOpenWindows();
Check("setting change can restore native product list", details.Initializations == 3);
ModOptions.Search.Value = true;
Console.WriteLine($"Passed {checks} vendor search checks (production callbacks with managed fixtures).");

delegate void Begin(VendorFilteringUI ui, PersonList people, out string? state);

delegate void ProductsBegin(VendorDetails details, Person person, ref string? filter);
