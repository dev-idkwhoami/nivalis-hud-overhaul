using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Runtime;
using Nivalis.Locale.UI;
using Pair = Nivalis.Locale.UI.InventoryItemFilteringUi.SortingPair;

namespace NivalisMods.HudOverhaul;

internal static class NativeSortingPairs
{
    // Generated managed structs may have different tail padding from IL2CPP.
    // Il2CppStructArray<T>'s span indexer uses managed sizeof(T); query the
    // actual native stride instead, for both existing and newly allocated arrays.
    internal static int Stride(Il2CppStructArray<Pair> array) =>
        IL2CPP.il2cpp_array_element_size(IL2CPP.il2cpp_object_get_class(array.Pointer));

    private static IntPtr Element(Il2CppStructArray<Pair> array, int index)
    {
        if ((uint)index >= (uint)array.Length) throw new ArgumentOutOfRangeException(nameof(index));
        var stride = Stride(array);
        if (stride < 5) throw new InvalidOperationException($"Unexpected sorting pair stride: {stride}");
        return IntPtr.Add(array.Pointer, Marshal.SizeOf<Il2CppObject>() + 2 * IntPtr.Size + index * stride);
    }

    internal static Pair Read(Il2CppStructArray<Pair> array, int index)
    {
        var pointer = Element(array, index);
        return new Pair { Ordering = (ListSortingOption)Marshal.ReadInt32(pointer), Descending = Marshal.ReadByte(pointer, 4) != 0 };
    }

    internal static Il2CppStructArray<Pair> Create(IReadOnlyList<Pair> pairs)
    {
        var array = new Il2CppStructArray<Pair>(pairs.Count);
        for (var i = 0; i < pairs.Count; i++)
        {
            var pointer = Element(array, i);
            Marshal.WriteInt32(pointer, (int)pairs[i].Ordering);
            Marshal.WriteByte(pointer, 4, pairs[i].Descending ? (byte)1 : (byte)0);
        }
        return array;
    }
}
