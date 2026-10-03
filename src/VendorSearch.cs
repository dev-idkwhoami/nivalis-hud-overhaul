using HarmonyLib;
using Nivalis.GhostSystem.Ai;
using Nivalis.Locale.UI;
using PersonList = Il2CppSystem.Collections.Generic.List<Nivalis.GhostSystem.Ai.Person>;

namespace NivalisMods.HudOverhaul;

[HarmonyPatch(typeof(VendorFilteringUI), nameof(VendorFilteringUI.FilterResult))]
internal static class VendorSearch
{
    private static bool _reportedError;

    [HarmonyPrefix]
    private static void Prefix(VendorFilteringUI __instance, PersonList persons, out string? __state)
    {
        __state = null;
        if (!ModOptions.Search.Value) return;
        var input = __instance.searchField;
        var query = input?.text;
        if (string.IsNullOrWhiteSpace(query)) return;
        try
        {
            var remove = new List<int>();
            // The caller supplies known vendors. Never discover/add other people,
            // or initialize shop stock just to search it.
            for (var i = 0; i < persons.Count; i++)
                if (!Matches(persons[i], query)) remove.Add(i);

            // Evaluate before mutating so lookup failures retain native search.
            // Bypass only native name matching; it still applies location/type.
            // The backing field avoids caret resets and text-change callbacks.
            __state = query;
            input!.m_Text = "";
            for (var i = remove.Count - 1; i >= 0; i--) persons.RemoveAt(remove[i]);
        }
        catch (Exception e)
        {
            if (_reportedError) return;
            _reportedError = true;
            Plugin.Logger.LogError($"Vendor item search failed: {e}");
        }
    }

    private static bool Matches(Person? person, string query)
    {
        if (person == null) return false;
        if (SearchText.Matches(query, new[] { person.Name })) return true;
        var vendor = person.VendorDefinition;
        if (vendor?.items == null) return false;
        foreach (var item in vendor.items.Values)
            // Include sold-out lines: this answers who sells it, not current stock.
            if (item?.ItemType != null && SearchText.Matches(query, new[] { item.ItemType.Name })) return true;
        return false;
    }

    [HarmonyFinalizer]
    private static void Finalizer(VendorFilteringUI __instance, string? __state)
    {
        if (__state != null) __instance.searchField.m_Text = __state;
    }
}
