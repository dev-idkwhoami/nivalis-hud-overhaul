using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP.Hook;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Runtime;
using Nivalis.CraftingSystem;
using Nivalis.InventorySystem;
using Nivalis.Locale;
using Nivalis.Locale.UI;

namespace NivalisMods.HudOverhaul;

internal static class ExpandedSearch
{
    // Preserve IL2CPP's trailing MethodInfo / generic context verbatim. Harmony
    // generic trampolines cannot safely substitute a single closed type here.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ListFilter(IntPtr self, IntPtr items, IntPtr venue, IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte RecipePredicate(IntPtr self, IntPtr item, IntPtr method);
    private static readonly List<INativeDetour> Hooks = new();
    private static RecipePredicate _recipeOriginal = null!;
    [ThreadStatic] internal static SearchCatalog? Catalog;
    private static bool _reportedError;

    internal static void Install()
    {
        try
        {
            var store = typeof(InventoryItemFilteringUi).GetNestedTypes(BindingFlags.NonPublic)
                .Single(t => t.Name.StartsWith("MethodInfoStoreGeneric_FilterResult_Public_Void_"));
            // IL2CPP emits separate native implementations for boxed struct
            // entries (shops) and reference entries (venue inventory).
            var installed = new HashSet<IntPtr>();
            foreach (var entryType in new[] { typeof(ContainerItemStack), typeof(ItemStack) })
            {
                var listInfo = (IntPtr)store.MakeGenericType(entryType)
                    .GetField("Pointer", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                var pointer = MethodPointer(listInfo);
                if (!installed.Add(pointer)) continue;
                ListFilter original = null!;
                ListFilter replacement = (self, items, venue, method) => FilterItems(original, self, items, venue, method);
                var hook = INativeDetour.Create(pointer, replacement);
                Hooks.Add(hook);
                original = hook.GenerateTrampoline<ListFilter>();
                if (Plugin.IsVerbose) Plugin.Verbose($"Prepared item search hook for {entryType.Name}.");
            }
            var predicateType = typeof(InventoryItemFilteringUi.__c__DisplayClass17_0<IRecipe>);
            var predicateInfo = (IntPtr)predicateType.GetFields(BindingFlags.Static | BindingFlags.NonPublic)
                .Single(f => f.Name.StartsWith("NativeMethodInfoPtr__FilterResult_b__0_")).GetValue(null)!;
            var recipeHook = INativeDetour.Create(MethodPointer(predicateInfo), (RecipePredicate)FilterRecipe);
            Hooks.Add(recipeHook);
            _recipeOriginal = recipeHook.GenerateTrampoline<RecipePredicate>();
            foreach (var hook in Hooks) hook.Apply();
            if (Plugin.IsVerbose) Plugin.Verbose("Expanded search enabled: item tags, recipe tags, ingredients and equipment.");
        }
        catch (Exception e)
        {
            foreach (var hook in Hooks) hook.Dispose();
            Hooks.Clear();
            Plugin.Logger.LogError($"Expanded search setup failed; native search retained: {e}");
        }
    }

    private static unsafe IntPtr MethodPointer(IntPtr metadata)
    {
        if (metadata == IntPtr.Zero) throw new InvalidOperationException("Search method metadata missing.");
        var pointer = UnityVersionHandler.Wrap((Il2CppMethodInfo*)metadata).MethodPointer;
        if (pointer == IntPtr.Zero) throw new InvalidOperationException("Search native method missing.");
        return pointer;
    }

    private static void FilterItems(ListFilter original, IntPtr self, IntPtr items, IntPtr venue, IntPtr method)
    {
        if (!ModOptions.Search.Value) { original(self, items, venue, method); return; }
        InventoryItemFilteringUi? filter = null;
        string? query = null;
        var cleared = false;
        var expectedCount = -1;
        try
        {
            filter = new InventoryItemFilteringUi(self);
            query = filter.searchField?.text;
            if (!string.IsNullOrWhiteSpace(query))
            {
                // IList boxes value-type entries correctly; never assume the
                // layout/stride of a native generic List<T> backing array.
                var list = new Il2CppSystem.Object(items).TryCast<Il2CppSystem.Collections.IList>()
                    ?? throw new InvalidOperationException("Item search list does not implement IList.");
                var catalog = new SearchCatalog();
                var remove = new List<int>();
                var tagMatches = new List<string>();
                var tagSamples = new List<string>();
                var count = list.Cast<Il2CppSystem.Collections.ICollection>().Count;
                for (var i = 0; i < count; i++)
                {
                    var entry = list[i] ?? throw new InvalidOperationException("Null item search entry.");
                    // The interface wrapper invokes a value-type getter with
                    // the boxed pointer instead of its unboxed payload. Read
                    // the concrete generated field accessor for these entries.
                    var stack = entry.TryCast<ContainerItemStack>()?.ItemStack
                        ?? entry.TryCast<ItemStack>();
                    if (stack == null || stack.Type == null)
                        throw new InvalidOperationException($"Unsupported item search entry: {entry.GetIl2CppType().FullName}");
                    var fields = catalog.ItemFields(stack.Type);
                    if (Plugin.IsVerbose && fields.Length > 1 && tagSamples.Count < 5)
                        tagSamples.Add(string.Join(" | ", fields));
                    if (!catalog.ItemMatches(stack.Type, query)) remove.Add(i);
                    else if (Plugin.IsVerbose && !SearchText.Matches(query, new[] { stack.Type.Name }))
                        tagMatches.Add($"{stack.Type.Name} [{string.Join(", ", catalog.ItemFields(stack.Type))}]");
                }
                // Evaluate everything before mutating; if metadata lookup fails,
                // fall back to the untouched native search.
                // Change only the backing value consumed by native filtering.
                // TMP's text setter also resets caret/selection and rebuilds
                // its label, even when called with notifications disabled.
                filter.searchField!.m_Text = "";
                cleared = true;
                for (var i = remove.Count - 1; i >= 0; i--) list.RemoveAt(remove[i]);
                expectedCount = count - remove.Count;
                if (Plugin.IsVerbose) Plugin.Verbose($"Item search '{query}': {count - remove.Count}/{count} matches; {tagMatches.Count} tag/category hits. {string.Join("; ", tagMatches.Take(3))}");
                if (Plugin.IsVerbose && tagMatches.Count == 0) Plugin.Verbose($"Item search tag samples: {string.Join("; ", tagSamples)}");
            }
        }
        catch (Exception e) { Report(e); }
        try
        {
            original(self, items, venue, method);
            if (expectedCount >= 0)
            {
                var remaining = new Il2CppSystem.Object(items).Cast<Il2CppSystem.Collections.ICollection>().Count;
                if (remaining != expectedCount)
                    Plugin.Logger.LogWarning($"Native item filter changed expanded matches: {expectedCount} -> {remaining} for '{query}'.");
            }
        }
        finally
        {
            if (cleared)
                try { filter!.searchField!.m_Text = query!; }
                catch (Exception e) { Report(e); }
        }
    }

    private static byte FilterRecipe(IntPtr self, IntPtr item, IntPtr method)
    {
        var original = _recipeOriginal(self, item, method);
        if (original != 0 || !ModOptions.Search.Value) return original;
        try
        {
            var field = IL2CPP.il2cpp_class_get_field_from_name(IL2CPP.il2cpp_object_get_class(self), "<>4__this");
            if (field == IntPtr.Zero) throw new InvalidOperationException("Recipe search owner field missing.");
            var filterPtr = Marshal.ReadIntPtr(self, checked((int)IL2CPP.il2cpp_field_get_offset(field)));
            var filter = new InventoryItemFilteringUi(filterPtr);
            var query = filter.searchField.text;
            var entry = new Il2CppSystem.Object(item);
            var recipe = entry.TryCast<IRecipe>() ?? entry.TryCast<MealMenuItem>()?.Recipe;
            if (recipe != null && (Catalog ?? new SearchCatalog()).RecipeMatches(recipe, query)) return 1;
        }
        catch (Exception e) { Report(e); }
        return original;
    }

    private static void Report(Exception e)
    {
        if (_reportedError) return;
        _reportedError = true;
        Plugin.Logger.LogError($"Expanded search fell back to native matching: {e}");
    }
}

[HarmonyPatch]
internal static class RecipeSearchScopePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(MenuModificationWindow), nameof(MenuModificationWindow.RefreshRecipeList));
        yield return AccessTools.Method(typeof(MenuModificationWindow), nameof(MenuModificationWindow.RefreshMenuList));
    }
    [HarmonyPrefix]
    private static void Prefix(out SearchCatalog? __state)
    {
        __state = ExpandedSearch.Catalog;
        ExpandedSearch.Catalog = new SearchCatalog();
    }
    [HarmonyFinalizer]
    private static void Finalizer(SearchCatalog? __state) => ExpandedSearch.Catalog = __state;
}
