namespace NivalisMods.HudOverhaul;

internal static class DemandEstimate
{
    internal static bool InHistory(int day, int today) => day >= Math.Max(1, today - 7) && day < today;
    internal static double Daily(long sold, int today) => Math.Max(0, sold) / (double)Math.Clamp(today - 1, 1, 7);
    internal static int Target(double ingredients, int fallback) => (int)Math.Min(int.MaxValue, Math.Max(Math.Max(0, fallback), Math.Ceiling(Math.Max(0, ingredients))));
    internal static int Missing(int target, int stock) => Math.Max(0, target - Math.Max(0, stock));
    internal static int ShoppingNeed(int target, int stock, bool deductStock) => Missing(target, deductStock ? stock : 0);
}
