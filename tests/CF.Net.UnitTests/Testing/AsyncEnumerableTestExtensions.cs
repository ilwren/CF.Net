namespace CFNet.UnitTests.Testing;

/// <summary>
/// Test-only helpers. The BCL deliberately ships no LINQ operators for <see cref="IAsyncEnumerable{T}"/>
/// (they live in the separate System.Linq.Async package), and the unit tests should not take that
/// dependency just to drain a sequence.
/// </summary>
public static class AsyncEnumerableTestExtensions
{
    /// <summary>Drains an async sequence into a list.</summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="source">The sequence.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<List<T>> CollectAsync<T>(this IAsyncEnumerable<T> source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var items = new List<T>();
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            items.Add(item);
        }

        return items;
    }

    /// <summary>Drains an async sequence, counting the items without keeping them.</summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="source">The sequence.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<int> CountAsync<T>(this IAsyncEnumerable<T> source, CancellationToken cancellationToken = default)
    {
        var count = 0;
        await foreach (var _ in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            count++;
        }

        return count;
    }
}
