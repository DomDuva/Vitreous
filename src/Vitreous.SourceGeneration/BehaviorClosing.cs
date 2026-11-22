using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Vitreous.SourceGeneration;

// Open-generic pipeline behaviors closed over every message at build time. Native AOT can't close
// an open-generic service over a value type at runtime, so the generated AddVitreousHandlers()
// hands these closed registrations to AddVitreousBehavior.

/// <summary>
/// What the generator needs to know about a message or result type to check a behavior's
/// type-parameter constraints against it, without holding the symbol.
/// </summary>
internal sealed record TypeFacts(
    string Key,                       // BehaviorClosing.Format() of the type; matched against constraint templates
    string EmitName,                  // fully qualified, for typeof() in generated code
    bool IsReferenceType,
    bool IsValueType,
    bool IsNullableValueType,
    bool IsUnmanaged,
    bool HasPublicParameterlessConstructor,
    EquatableArray<string> ConvertibleTo,    // Keys of the type, its base types and its interfaces
    EquatableArray<string> Definitions);     // the same, as generic definitions (List<{0}>)

internal enum BehaviorKind
{
    Pipeline,       // IPipelineBehavior<TMessage, TResult>
    Stream,         // IStreamPipelineBehavior<TMessage, TResult>
    Notification,   // INotificationPipelineBehavior<TNotification>
}

/// <summary>An open-generic behavior implementation the generator can close.</summary>
internal sealed record BehaviorInfo(
    string ImplementationName,                      // fully qualified, without type arguments
    BehaviorKind Kind,
    EquatableArray<ParameterConstraints> Parameters);

/// <summary>The constraints on one type parameter.</summary>
internal sealed record ParameterConstraints(
    bool ReferenceType,
    bool ValueType,
    bool Unmanaged,
    bool Constructor,
    EquatableArray<ConstraintType> Types);

/// <summary>
/// A type constraint. <see cref="Template"/> is a <see cref="BehaviorClosing.Format"/> name where
/// <c>{i}</c> stands for the behavior's i-th type argument.
/// </summary>
internal sealed record ConstraintType(
    string Template,
    string Definition,   // the constraint's generic definition, e.g. IReadOnlyCollection<{0}>
    bool Variant);       // has in/out type parameters, so a different type argument may still convert

/// <summary>Everything needed to emit the closed behaviors.</summary>
internal sealed record BehaviorModel(
    EquatableArray<BehaviorInfo> Behaviors,
    EquatableArray<TypeFacts> Notifications,
    TypeFacts? Unit)
{
    public static readonly BehaviorModel Empty =
        new(EquatableArray<BehaviorInfo>.Empty, EquatableArray<TypeFacts>.Empty, null);
}

internal static class BehaviorClosing
{
    private static readonly SymbolDisplayFormat s_emitFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    private static readonly SymbolDisplayFormat s_openNameFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.None,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    // -------------------------------------------------------------------------
    // Discovery
    // -------------------------------------------------------------------------

    /// <summary>
    /// The behaviors <paramref name="type"/> implements, if it is an open-generic class the
    /// generated code can name and close the way Microsoft.Extensions.DependencyInjection would:
    /// the interface's type arguments are the class's own type parameters, in order.
    /// </summary>
    public static IEnumerable<BehaviorInfo> CollectBehaviors(INamedTypeSymbol type, WellKnownTypes types, bool sameAssembly)
    {
        if (type.TypeKind != TypeKind.Class || type.IsAbstract || !type.IsGenericType || type.IsStatic)
            yield break;

        for (var t = type; t is not null; t = t.ContainingType)
        {
            var accessible = t.DeclaredAccessibility == Accessibility.Public ||
                             (sameAssembly && t.DeclaredAccessibility == Accessibility.Internal);
            if (!accessible || (!ReferenceEquals(t, type) && t.IsGenericType))
                yield break;
        }

        foreach (var iface in type.AllInterfaces)
        {
            BehaviorKind kind;
            var original = iface.OriginalDefinition;
            if (Is(original, types.PipelineBehavior)) kind = BehaviorKind.Pipeline;
            else if (Is(original, types.StreamPipelineBehavior)) kind = BehaviorKind.Stream;
            else if (Is(original, types.NotificationPipelineBehavior)) kind = BehaviorKind.Notification;
            else continue;

            if (!MapsTypeParametersInOrder(iface, type))
                continue;

            var parameters = Constraints(type);
            if (parameters is null)
                continue;   // a constraint we can't check: leave it to DI as an open generic

            yield return new BehaviorInfo(type.ToDisplayString(s_openNameFormat), kind, parameters.Value);
        }
    }

    private static bool MapsTypeParametersInOrder(INamedTypeSymbol iface, INamedTypeSymbol type)
    {
        if (iface.TypeArguments.Length != type.TypeParameters.Length)
            return false;

        for (var i = 0; i < iface.TypeArguments.Length; i++)
        {
            if (!SymbolEqualityComparer.Default.Equals(iface.TypeArguments[i], type.TypeParameters[i]))
                return false;
        }

        return true;
    }

    private static EquatableArray<ParameterConstraints>? Constraints(INamedTypeSymbol type)
    {
        var parameters = new List<ParameterConstraints>();
        foreach (var parameter in type.TypeParameters)
        {
            var templates = new List<ConstraintType>();
            foreach (var constraint in parameter.ConstraintTypes)
            {
                if (Format(constraint, type.TypeParameters) is not { } template ||
                    Definition(constraint) is not { } definition)
                {
                    return null;
                }

                var variant = constraint is INamedTypeSymbol named &&
                    named.OriginalDefinition.TypeParameters.Any(static p => p.Variance != VarianceKind.None);
                templates.Add(new ConstraintType(template, definition, variant));
            }

            parameters.Add(new ParameterConstraints(
                parameter.HasReferenceTypeConstraint,
                parameter.HasValueTypeConstraint,
                parameter.HasUnmanagedTypeConstraint,
                parameter.HasConstructorConstraint,
                templates.ToEquatableArray()));
        }

        return parameters.ToEquatableArray();
    }

    /// <summary>
    /// The facts for <paramref name="type"/>, or <see langword="null"/> when it can't be described
    /// (pointers, <c>dynamic</c>, error types). Then nothing is closed for that kind of behavior.
    /// </summary>
    public static TypeFacts? Facts(ITypeSymbol type)
    {
        if (Format(type, ImmutableArrayOfNone) is not { } key)
            return null;

        var convertibleTo = new List<string>();
        var definitions = new List<string>();
        foreach (var t in SelfBasesAndInterfaces(type))
        {
            if (Format(t, ImmutableArrayOfNone) is { } name)
                convertibleTo.Add(name);
            if (Definition(t) is { } definition)
                definitions.Add(definition);
        }

        var hasConstructor = type.IsValueType ||
            (type is INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: false } named &&
             named.InstanceConstructors.Any(static c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public));

        return new TypeFacts(
            key,
            type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(s_emitFormat),
            type.IsReferenceType,
            type.IsValueType,
            type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T,
            type.IsUnmanagedType,
            hasConstructor,
            convertibleTo.Distinct().OrderBy(static n => n, StringComparer.Ordinal).ToEquatableArray(),
            definitions.Distinct().OrderBy(static n => n, StringComparer.Ordinal).ToEquatableArray());
    }

    private static IEnumerable<ITypeSymbol> SelfBasesAndInterfaces(ITypeSymbol type)
    {
        yield return type;
        for (var t = type.BaseType; t is not null; t = t.BaseType)
            yield return t;
        foreach (var iface in type.AllInterfaces)
            yield return iface;
    }

    /// <summary>The generic definition of <paramref name="type"/> (<c>List&lt;{0}&gt;</c>), or its name if it isn't generic.</summary>
    private static string? Definition(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsGenericType: true } named
            ? Format(named.OriginalDefinition, AllTypeParameters(named.OriginalDefinition))
            : Format(type, ImmutableArrayOfNone);

    private static ImmutableArray<ITypeParameterSymbol> AllTypeParameters(INamedTypeSymbol type)
    {
        var all = new List<ITypeParameterSymbol>();
        for (var t = type; t is not null; t = t.ContainingType)
            all.InsertRange(0, t.TypeParameters);
        return all.ToImmutableArray();
    }

    /// <summary>
    /// Whether <paramref name="type"/> is a notification type the generator should close
    /// notification behaviors over (it can be published even without a handler).
    /// </summary>
    public static bool IsNotification(INamedTypeSymbol type, WellKnownTypes types) =>
        !type.IsGenericType && !type.IsAbstract && type.TypeKind is TypeKind.Class or TypeKind.Struct &&
        types.Notification is not null &&
        type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, types.Notification));

    private static readonly ImmutableArray<ITypeParameterSymbol> ImmutableArrayOfNone =
        ImmutableArray<ITypeParameterSymbol>.Empty;

    /// <summary>
    /// A canonical name for <paramref name="type"/>, ignoring nullable annotations and tuple
    /// element names. The behavior's own type parameters become <c>{index}</c>.
    /// </summary>
    internal static string? Format(ITypeSymbol type, ImmutableArray<ITypeParameterSymbol> placeholders)
    {
        switch (type)
        {
            case ITypeParameterSymbol parameter:
                for (var i = 0; i < placeholders.Length; i++)
                {
                    if (SymbolEqualityComparer.Default.Equals(placeholders[i], parameter))
                        return "{" + i + "}";
                }
                return null;

            case IArrayTypeSymbol array:
                return Format(array.ElementType, placeholders) is { } element
                    ? element + "[" + new string(',', array.Rank - 1) + "]"
                    : null;

            case INamedTypeSymbol named when named.TypeKind != TypeKind.Error:
            {
                var sb = new StringBuilder();
                if (named.ContainingType is { } containing)
                {
                    if (Format(containing, placeholders) is not { } outer)
                        return null;
                    sb.Append(outer).Append('.');
                }
                else if (named.ContainingNamespace is { IsGlobalNamespace: false } ns)
                {
                    sb.Append(ns.ToDisplayString()).Append('.');
                }

                sb.Append(named.Name);
                if (named.TypeArguments.Length > 0)
                {
                    sb.Append('<');
                    for (var i = 0; i < named.TypeArguments.Length; i++)
                    {
                        if (Format(named.TypeArguments[i], placeholders) is not { } argument)
                            return null;
                        if (i > 0) sb.Append(',');
                        sb.Append(argument);
                    }
                    sb.Append('>');
                }

                return sb.ToString();
            }

            default:
                return null;
        }
    }

    // -------------------------------------------------------------------------
    // Emission
    // -------------------------------------------------------------------------

    /// <summary>
    /// The <c>services.AddVitreousGeneratedBehaviors(...)</c> statement for the generated
    /// <c>AddVitreousHandlers()</c>, or <see langword="null"/> when there is nothing to close.
    /// </summary>
    public static string? EmitRegistration(IReadOnlyList<HandlerInfo> handlers, BehaviorModel model)
    {
        if (model.Behaviors.Count == 0)
            return null;

        var pipeline = new List<TypeFacts?[]>();
        var stream = new List<TypeFacts?[]>();
        var notification = new List<TypeFacts?[]>();

        foreach (var h in handlers)
        {
            switch (h.Kind)
            {
                case HandlerKind.Command or HandlerKind.Query:
                    pipeline.Add([h.MessageFacts, h.ResultFacts]);
                    break;
                case HandlerKind.VoidCommand:
                    pipeline.Add([h.MessageFacts, model.Unit]);
                    break;
                case HandlerKind.StreamQuery:
                    stream.Add([h.MessageFacts, h.ResultFacts]);
                    break;
                case HandlerKind.Notification:
                    notification.Add([h.MessageFacts]);
                    break;
            }
        }

        foreach (var n in model.Notifications)
            notification.Add([n]);

        var sb = new StringBuilder();
        foreach (var behavior in model.Behaviors.OrderBy(static b => b.ImplementationName, StringComparer.Ordinal).ThenBy(static b => b.Kind))
        {
            var (service, candidates) = behavior.Kind switch
            {
                BehaviorKind.Pipeline => ("IPipelineBehavior", pipeline),
                BehaviorKind.Stream   => ("IStreamPipelineBehavior", stream),
                _                     => ("INotificationPipelineBehavior", notification),
            };

            // A message we can't describe: leave this behavior to DI as an open generic.
            if (candidates.Any(static c => c.Any(static f => f is null)))
                continue;

            var arity = behavior.Parameters.Count;
            if (candidates.Any(c => c.Length != arity))
                continue;

            var checks = candidates
                .Select(static c => c.Select(static f => f!).ToArray())
                .Select(c => (Arguments: c, Satisfied: Satisfies(behavior, c)))
                .ToList();

            // Undecidable for some message: leave this behavior to DI as an open generic.
            if (checks.Any(static c => c.Satisfied is null))
                continue;

            var closed = checks
                .Where(static c => c.Satisfied == true)
                .Select(static c => string.Join(", ", c.Arguments.Select(static f => f.EmitName)))
                .Distinct()
                .OrderBy(static a => a, StringComparer.Ordinal)
                .ToList();

            var commas = new string(',', arity - 1);
            sb.AppendLine($"            .Add(typeof({service}<{commas}>), typeof({behavior.ImplementationName}<{commas}>), static lifetime => new ServiceDescriptor[]");
            sb.AppendLine("            {");
            foreach (var arguments in closed)
                sb.AppendLine($"                new ServiceDescriptor(typeof({service}<{arguments}>), typeof({behavior.ImplementationName}<{arguments}>), lifetime),");
            sb.AppendLine("            })");
        }

        if (sb.Length == 0)
            return null;

        return
            "        // Open-generic behaviors added with AddVitreousBehavior (Vitreous.Caching uses it), closed over\n" +
            "        // every message here: Native AOT can't close them at runtime for value types.\n" +
            "#nullable disable\n" +
            "        services.AddVitreousGeneratedBehaviors(new global::Vitreous.Internal.GeneratedBehaviors()\n" +
            sb.ToString().TrimEnd('\r', '\n') + ");\n" +
            "#nullable restore\n";
    }

    /// <summary>
    /// Whether closing <paramref name="behavior"/> over <paramref name="arguments"/> satisfies its
    /// constraints, or <see langword="null"/> when that depends on a variant conversion the facts
    /// don't cover.
    /// </summary>
    internal static bool? Satisfies(BehaviorInfo behavior, TypeFacts[] arguments)
    {
        for (var i = 0; i < arguments.Length; i++)
        {
            var constraints = behavior.Parameters[i];
            var argument = arguments[i];

            if (constraints.ReferenceType && !argument.IsReferenceType) return false;
            if (constraints.ValueType && (!argument.IsValueType || argument.IsNullableValueType)) return false;
            if (constraints.Unmanaged && !argument.IsUnmanaged) return false;
            if (constraints.Constructor && !argument.HasPublicParameterlessConstructor) return false;

            foreach (var constraint in constraints.Types)
            {
                var required = constraint.Template;
                for (var j = 0; j < arguments.Length; j++)
                    required = required.Replace("{" + j + "}", arguments[j].Key);

                if (argument.ConvertibleTo.Contains(required))
                    continue;

                // It implements the constraint's generic type with other type arguments, which may
                // still convert to it through variance.
                if (constraint.Variant && argument.Definitions.Contains(constraint.Definition))
                    return null;

                return false;
            }
        }

        return true;
    }

    private static bool Is(INamedTypeSymbol candidate, INamedTypeSymbol? wellKnown) =>
        wellKnown is not null && SymbolEqualityComparer.Default.Equals(candidate, wellKnown);
}
