using System.Collections.Generic;

namespace VL.NTS;

/// <summary>
/// The one change-detection rule the package's stateful nodes share: an input SET has changed when
/// its count differs or any position holds a different object. Reference equality only.
/// </summary>
/// <remarks>
/// <para>Why not the collection reference: VL's only change signal is object identity, and a static
/// node upstream hands out a fresh <c>Spread</c> every frame even when every element inside is the
/// same object — comparing wrappers would rebuild every frame. Why not structural comparison:
/// <c>EqualsExact</c> over a hundred thousand geometries costs more than the structure it guards.
/// Element-wise <c>ReferenceEquals</c> is tens of microseconds for 100k and catches the one case
/// wrapper identity misses — the same mutable list with an item added.</para>
/// <para>What it cannot see, by contract: an element mutated in place. This package's creation
/// nodes copy on the way in, so that state is only reachable with a raw NTS handle from elsewhere.
/// First written for <c>SpatialIndex</c>; shared here so the second stateful node makes the same
/// promise in the same words.</para>
/// </remarks>
internal static class InputSets
{
    internal static bool SameReferences<T>(T?[] a, T?[] b) where T : class
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
            if (!ReferenceEquals(a[i], b[i])) return false;
        return true;
    }

    internal static T?[] ToArray<T>(IEnumerable<T?> items) where T : class
    {
        if (items is T?[] array) return array;
        if (items is IReadOnlyCollection<T?> known)
        {
            var result = new T?[known.Count];
            var i = 0;
            foreach (var item in known) result[i++] = item;
            return result;
        }
        return new List<T?>(items).ToArray();
    }
}
