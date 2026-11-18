namespace Vitreous.Caching.Abstractions;

/// <summary>
/// Converts cached values to and from bytes. Used by both the <c>IDistributedCache</c> backend
/// and the Redis backend.
/// </summary>
/// <remarks>
/// The default implementation is <see cref="Serialization.SystemTextJsonVitreousCacheSerializer"/>.
/// Register your own implementation before calling <c>AddVitreousCaching()</c> or
/// <c>AddVitreousRedisCaching()</c> to replace it.
/// <para>
/// Implementations must make <see cref="TryDeserialize{T}"/> return <see langword="false"/>
/// when the data was not written for <typeparamref name="T"/>, so that two queries that
/// share a cache key but return different types can never read each other's entries.
/// </para>
/// </remarks>
public interface IVitreousCacheSerializer
{
    /// <summary>Serializes <paramref name="value"/>, recording that it is a <typeparamref name="T"/>.</summary>
    byte[] Serialize<T>(T value);

    /// <summary>
    /// Deserializes <paramref name="data"/> as a <typeparamref name="T"/>. Returns
    /// <see langword="false"/> when the data holds a different type or an unrecognised format;
    /// callers treat that as a cache miss.
    /// </summary>
    bool TryDeserialize<T>(ReadOnlySpan<byte> data, out T? value);
}
