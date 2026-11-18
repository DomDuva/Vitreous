using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Vitreous.DevUI.Tracing;

/// <summary>
/// Serializes captured messages and results for display, masking sensitive values, and
/// serializes traces for the UI. One instance (and one set of
/// <see cref="JsonSerializerOptions"/>, with its metadata cache) is shared by every tracing
/// behavior instead of one per closed generic type.
/// </summary>
public sealed class DevUISerializer
{
    private const string Mask = "***";

    private readonly JsonSerializerOptions _payloadOptions;
    private readonly JsonSerializerOptions _traceOptions;

    public DevUISerializer(VitreousDevUIOptions options)
    {
        var redacted = options.RedactedPropertyNames.ToArray();

        _payloadOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented        = false,
            ReferenceHandler     = ReferenceHandler.IgnoreCycles,
            TypeInfoResolver     = new DefaultJsonTypeInfoResolver { Modifiers = { typeInfo => Redact(typeInfo, redacted) } },
        };

        _traceOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented        = false,
            Converters           = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };
    }

    /// <summary>
    /// Serializes a captured message, result or stream item with sensitive values masked.
    /// Returns <see langword="null"/> for <see langword="null"/> or values that cannot be serialized.
    /// </summary>
    public string? SerializePayload<T>(T? value)
    {
        if (value is null) return null;
        try { return JsonSerializer.Serialize(value, _payloadOptions); }
        catch { return null; }
    }

    /// <summary>Serializes a trace (or a list of traces) for the UI.</summary>
    public string SerializeTrace<T>(T trace) => JsonSerializer.Serialize(trace, _traceOptions);

    private static void Redact(JsonTypeInfo typeInfo, string[] redactedNames)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object || typeInfo.Properties.Count == 0)
            return;

        // [Sensitive] on a record's positional parameter applies to the parameter, not the
        // generated property, so collect those by name too.
        var sensitiveParameters = typeInfo.Type
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(static c => c.GetParameters())
            .Where(static p => p.IsDefined(typeof(SensitiveAttribute), inherit: true))
            .Select(static p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < typeInfo.Properties.Count; i++)
        {
            var property = typeInfo.Properties[i];
            var memberName = (property.AttributeProvider as MemberInfo)?.Name ?? property.Name;

            var sensitive =
                property.AttributeProvider?.IsDefined(typeof(SensitiveAttribute), inherit: true) == true ||
                sensitiveParameters.Contains(memberName) ||
                redactedNames.Any(n =>
                    property.Name.Contains(n, StringComparison.OrdinalIgnoreCase) ||
                    memberName.Contains(n, StringComparison.OrdinalIgnoreCase));

            if (!sensitive)
                continue;

            // Replace the property with a string-typed one that always writes the mask, so
            // masking works whatever the original property type is.
            var masked = typeInfo.CreateJsonPropertyInfo(typeof(string), property.Name);
            masked.Get = static _ => Mask;
            masked.Order = property.Order;
            typeInfo.Properties[i] = masked;
        }
    }
}
