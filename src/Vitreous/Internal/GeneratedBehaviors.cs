using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Vitreous.Internal;

/// <summary>
/// Open-generic pipeline behaviors closed over every message the source generator found.
/// Used by the generated <c>AddVitreousHandlers()</c>; not intended for direct use.
/// </summary>
/// <remarks>
/// Native AOT can't close an open-generic service over a value type at runtime, so
/// <see cref="VitreousServiceCollectionExtensions.AddVitreousBehavior"/> registers these closed
/// descriptors instead of the open generic when they exist.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class GeneratedBehaviors
{
    private readonly Dictionary<(Type Service, Type Implementation), Func<ServiceLifetime, ServiceDescriptor[]>> _closers = new();

    /// <summary>
    /// Adds the closed registrations of <paramref name="implementationType"/> (an open generic)
    /// for the open-generic <paramref name="serviceType"/>.
    /// </summary>
    public GeneratedBehaviors Add(
        Type serviceType,
        Type implementationType,
        Func<ServiceLifetime, ServiceDescriptor[]> close)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(implementationType);
        ArgumentNullException.ThrowIfNull(close);

        _closers[(serviceType, implementationType)] = close;
        return this;
    }

    internal bool TryClose(
        Type serviceType,
        Type implementationType,
        ServiceLifetime lifetime,
        [NotNullWhen(true)] out ServiceDescriptor[]? closed)
    {
        if (_closers.TryGetValue((serviceType, implementationType), out var close))
        {
            closed = close(lifetime);
            return true;
        }

        closed = null;
        return false;
    }
}

/// <summary>
/// Kept in the service collection: the generated behaviors registered so far, and the
/// open-generic behaviors <c>AddVitreousBehavior</c> added before any were.
/// </summary>
internal sealed class BehaviorRegistrations
{
    public List<GeneratedBehaviors> Generated { get; } = new();

    public List<ServiceDescriptor> Open { get; } = new();

    public static BehaviorRegistrations GetOrAdd(IServiceCollection services)
    {
        foreach (var descriptor in services)
        {
            if (!descriptor.IsKeyedService &&
                descriptor.ServiceType == typeof(BehaviorRegistrations) &&
                descriptor.ImplementationInstance is BehaviorRegistrations existing)
            {
                return existing;
            }
        }

        var registrations = new BehaviorRegistrations();
        services.AddSingleton(registrations);
        return registrations;
    }

    /// <summary>Closed registrations for an open-generic behavior, from every generated set that has them.</summary>
    public List<ServiceDescriptor>? TryClose(Type serviceType, Type implementationType, ServiceLifetime lifetime)
    {
        List<ServiceDescriptor>? closed = null;
        foreach (var generated in Generated)
        {
            if (generated.TryClose(serviceType, implementationType, lifetime, out var descriptors))
                (closed ??= new List<ServiceDescriptor>()).AddRange(descriptors);
        }

        return closed;
    }
}
