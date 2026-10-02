namespace NivalisMods.HudOverhaul;

internal sealed class MealSalesTotals
{
    internal sealed class Total
    {
        internal string Name = "";
        internal string? CountLabel = null;
        internal long Current, Previous, Revenue;
        internal bool Compare;
        internal bool InRange;
    }
    private readonly Dictionary<string, Total> _items = new();
    internal void Add(string id, string name, int day, int count, int amount, int from, int to, int previousFrom, int previousTo, bool compare)
    {
        if (!_items.TryGetValue(id, out var total)) _items[id] = total = new Total { Name = name, Compare = compare };
        if (compare && day >= previousFrom && day < previousTo) total.Previous += count;
        if (day >= from && day < to) { total.Current += count; total.Revenue += amount; total.InRange = true; }
    }
    internal Total? Find(string name, int revenue)
    {
        Total? match = null;
        foreach (var total in _items.Values)
        {
            if (!total.InRange || total.Name != name || total.Revenue != revenue) continue;
            // The native row drops item identity. Never assign another recipe's
            // counts when two distinct recipes have identical names and revenue.
            if (match != null) return null;
            match = total;
        }
        return match;
    }
}
