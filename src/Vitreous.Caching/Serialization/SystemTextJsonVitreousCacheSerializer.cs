using System.Buffers;

namespace Vitreous.Caching.Serialization;

/// <summary>
/// Default <see cref="IVitreousCacheSerializer"/>, backed by System.Text.Json.
/// </summary>
/// <remarks>
/// Values are stored in an envelope that records the result type:
/// <code>{ "t": "MyApp.OrderDto", "v": { ...the value... } }</code>
/// Reading an entry written for another type, or in an older format, is reported as a miss.
/// <para>
/// System.Text.Json only round-trips what it can serialize: get-only properties without a
/// matching constructor parameter come back with default values. Use types with settable
/// properties or a matching constructor (records are fine), or supply
/// <see cref="JsonSerializerOptions"/> / a custom serializer that handles your types.
/// </para>
/// </remarks>
public sealed class SystemTextJsonVitreousCacheSerializer : IVitreousCacheSerializer
{
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// Creates a serializer using <paramref name="options"/>, or camelCase defaults when
    /// <see langword="null"/>.
    /// </summary>
    public SystemTextJsonVitreousCacheSerializer(JsonSerializerOptions? options = null) =>
        _options = options ?? new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <inheritdoc/>
    public byte[] Serialize<T>(T value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("t", CacheTypeName<T>.Value);
            writer.WritePropertyName("v");
            JsonSerializer.Serialize(writer, value, _options);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <inheritdoc/>
    public bool TryDeserialize<T>(ReadOnlySpan<byte> data, out T? value)
    {
        value = default;
        var reader = new Utf8JsonReader(data);

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            return false;

        if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals("t"u8))
            return false;

        if (!reader.Read() || reader.TokenType != JsonTokenType.String || !reader.ValueTextEquals(CacheTypeName<T>.Value))
            return false;

        if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals("v"u8))
            return false;

        if (!reader.Read())
            return false;

        value = JsonSerializer.Deserialize<T>(ref reader, _options);
        return true;
    }
}

/// <summary>
/// A stable, assembly-version-independent name for <typeparamref name="T"/>, e.g.
/// <c>System.Collections.Generic.IReadOnlyList&lt;MyApp.OrderDto&gt;</c>.
/// </summary>
internal static class CacheTypeName<T>
{
    public static readonly string Value = Format(typeof(T));

    private static string Format(Type type)
    {
        if (type.IsArray)
            return Format(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";

        if (!type.IsGenericType)
            return type.FullName ?? type.Name;

        var definition = type.GetGenericTypeDefinition().FullName ?? type.Name;
        var tick = definition.IndexOf('`');
        if (tick >= 0)
            definition = definition.Substring(0, tick);

        return definition + "<" + string.Join(",", type.GetGenericArguments().Select(Format)) + ">";
    }
}
