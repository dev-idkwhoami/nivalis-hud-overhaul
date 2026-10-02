using Nivalis.CraftingSystem;
using Nivalis.InventorySystem;
using UnityEngine;

namespace NivalisMods.HudOverhaul;

internal sealed class SearchCatalog
{
    private readonly Dictionary<IntPtr, string[]> _items = new();
    private List<(ItemType Item, RecipeTypes[] Recipes)>? _equipment;

    internal string[] ItemFields(ItemType item)
    {
        if (_items.TryGetValue(item.Pointer, out var fields)) return fields;
        var names = new List<string> { item.Name };
        if (item.tags != null)
            foreach (var tag in item.tags)
                if (tag != null) names.Add(tag.DisplayName);
        if (item.IsFurniture) names.Add(item.FurnitureCategory.ToString());
        return _items[item.Pointer] = names.ToArray();
    }

    internal bool ItemMatches(ItemType item, string query) => SearchText.Matches(query, ItemFields(item));

    internal bool RecipeMatches(IRecipe recipe, string query)
    {
        if (ItemMatches(recipe.Output.type, query)) return true;
        var tags = new Il2CppSystem.Collections.Generic.HashSet<ObjectTag>();
        recipe.CalculateTags(tags, out _, out _);
        foreach (var tag in tags)
            if (tag != null && SearchText.Matches(query, new[] { tag.DisplayName })) return true;
        // Runtime Inputs contain the player's substitutions, unlike the base
        // recipe definition. Search the actual ingredients and their tags.
        foreach (var input in recipe.Inputs)
            if (input.DefaultItem != null && ItemMatches(input.DefaultItem, query)) return true;
        var processors = new Il2CppSystem.Collections.Generic.List<IngredientProcessorType>();
        recipe.RequiredProcessorTypes(processors);
        foreach (var processor in processors)
        {
            if (processor == null) continue;
            if (SearchText.Matches(query, new[] { processor.ProcessorName })) return true;
            if (processor.BaseType != null && ItemMatches(processor.BaseType, query)) return true;
        }
        // Final cooking appliances are separate from ingredient preparation
        // processors. Match only appliances supporting this recipe category.
        _equipment ??= ReadEquipment();
        var kind = recipe.BaseDefinition.RecipeType;
        foreach (var equipment in _equipment)
            if (equipment.Recipes.Contains(kind) && SearchText.Matches(query, new[] { equipment.Item.Name })) return true;
        return false;
    }

    private static List<(ItemType, RecipeTypes[])> ReadEquipment()
    {
        var result = new List<(ItemType, RecipeTypes[])>();
        foreach (var item in Resources.FindObjectsOfTypeAll<ItemType>())
        {
            if (item.entityPrefab == null) continue;
            var processor = item.entityPrefab.GetComponentInChildren<FoodProcessor>(true);
            if (processor?.recipes != null) result.Add((item, processor.recipes.ToArray()));
        }
        return result;
    }
}
