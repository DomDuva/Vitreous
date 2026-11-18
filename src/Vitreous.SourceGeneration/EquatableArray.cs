using System.Collections;
using System.Collections.Immutable;

namespace Vitreous.SourceGeneration;

/// <summary>
/// An immutable array with value (sequence) equality, so incremental pipeline steps holding it
/// compare equal when their contents do and Roslyn can skip downstream work.
/// <see cref="ImmutableArray{T}"/> itself compares by reference.
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    public static readonly EquatableArray<T> Empty = new(ImmutableArray<T>.Empty);

    private readonly ImmutableArray<T> _items;

    public EquatableArray(ImmutableArray<T> items) => _items = items;

    private ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

    public int Count => Items.Length;

    public T this[int index] => Items[index];

    public ImmutableArray<T> AsImmutableArray() => Items;

    public bool Equals(EquatableArray<T> other)
    {
        var left = Items;
        var right = other.Items;
        if (left.Length != right.Length)
            return false;

        for (var i = 0; i < left.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            foreach (var item in Items)
                hash = (hash * 31) + (item?.GetHashCode() ?? 0);
            return hash;
        }
    }

    public ImmutableArray<T>.Enumerator GetEnumerator() => Items.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)Items).GetEnumerator();
}

internal static class EquatableArray
{
    public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> items)
        where T : IEquatable<T> =>
        new(items.ToImmutableArray());
}
