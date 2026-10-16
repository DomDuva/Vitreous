namespace Vitreous.Caching;

/// <summary>
/// Helpers for building cache keys consistently across all backends.
/// </summary>
public static class CacheKeys
{
    /// <summary>
    /// Joins <paramref name="prefix"/> and <paramref name="key"/> with exactly one
    /// <c>':'</c> separator. A prefix that already ends with <c>':'</c> is not doubled,
    /// and an empty prefix returns <paramref name="key"/> unchanged.
    /// </summary>
    /// <example>
    /// <code>
    /// CacheKeys.Combine("vitreous:", "order:1") // "vitreous:order:1"
    /// CacheKeys.Combine("vitreous",  "order:1") // "vitreous:order:1"
    /// CacheKeys.Combine("",          "order:1") // "order:1"
    /// </code>
    /// </example>
    public static string Combine(string? prefix, string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (string.IsNullOrEmpty(prefix))
            return key;

        return prefix.EndsWith(':') ? prefix + key : prefix + ":" + key;
    }
}
