using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Vitreous.Abstractions;
using Vitreous.Internal;

namespace Vitreous;

public static partial class VitreousServiceCollectionExtensions
{
    /// <summary>
    /// Registers a pipeline behavior for every message: an open generic such as
    /// <c>typeof(LoggingBehavior&lt;,&gt;)</c>, or a closed behavior for one message type.
    /// Adding the same behavior again does nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Behaviors run in registration order, outermost first, together with behaviors
    /// registered directly as <c>IPipelineBehavior&lt;,&gt;</c> services.
    /// </para>
    /// <para>
    /// With <c>Vitreous.SourceGeneration</c>, an open-generic behavior is registered closed over
    /// every message the generator found, so it also works under Native AOT for void commands,
    /// value-type results and struct messages. Registering it as an open-generic
    /// <c>IPipelineBehavior&lt;,&gt;</c> service directly fails under Native AOT for those messages.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="behaviorType">
    /// A type implementing <see cref="IPipelineBehavior{TMessage, TResult}"/>,
    /// <see cref="IStreamPipelineBehavior{TMessage, TResult}"/> or
    /// <see cref="INotificationPipelineBehavior{TNotification}"/>. An open generic must pass its
    /// type parameters to the interface in order, e.g. <c>Behavior&lt;TMessage, TResult&gt; : IPipelineBehavior&lt;TMessage, TResult&gt;</c>.
    /// </param>
    /// <param name="lifetime">The behavior's lifetime. Default: transient.</param>
    /// <exception cref="ArgumentException"><paramref name="behaviorType"/> is not a Vitreous pipeline behavior.</exception>
    public static IServiceCollection AddVitreousBehavior(
        this IServiceCollection services,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.Interfaces)]
        Type behaviorType,
        ServiceLifetime lifetime = ServiceLifetime.Transient)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(behaviorType);

        var registrations = BehaviorRegistrations.GetOrAdd(services);
        var found = false;

        foreach (var iface in behaviorType.GetInterfaces())
        {
            if (!iface.IsGenericType || !IsBehaviorInterface(iface.GetGenericTypeDefinition()))
                continue;

            found = true;

            if (!behaviorType.IsGenericTypeDefinition)
            {
                services.TryAddEnumerable(new ServiceDescriptor(iface, behaviorType, lifetime));
                continue;
            }

            // Microsoft.Extensions.DependencyInjection closes the implementation with the service's
            // type arguments, so they must be the behavior's own type parameters, in order.
            if (!iface.GetGenericArguments().SequenceEqual(behaviorType.GetGenericArguments()))
            {
                throw new ArgumentException(
                    $"'{behaviorType.Name}' must pass its type parameters to '{iface.Name}' in the order it declares them.",
                    nameof(behaviorType));
            }

            var service = iface.GetGenericTypeDefinition();
            if (registrations.TryClose(service, behaviorType, lifetime) is { } closed)
            {
                services.TryAddEnumerable(closed);
            }
            else if (!services.Any(d => !d.IsKeyedService && d.ServiceType == service && d.ImplementationType == behaviorType))
            {
                // No generated registrations (yet): AddVitreousGeneratedBehaviors closes it later.
                var open = new ServiceDescriptor(service, behaviorType, lifetime);
                services.Add(open);
                registrations.Open.Add(open);
            }
        }

        if (!found)
        {
            throw new ArgumentException(
                $"'{behaviorType.Name}' does not implement IPipelineBehavior<,>, IStreamPipelineBehavior<,> or INotificationPipelineBehavior<>.",
                nameof(behaviorType));
        }

        return services;
    }

    /// <summary>
    /// Registers the closed behaviors the source generator produced, and closes the open-generic
    /// behaviors <see cref="AddVitreousBehavior"/> already added. Called by the generated
    /// <c>AddVitreousHandlers()</c>; application code should not call this.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IServiceCollection AddVitreousGeneratedBehaviors(
        this IServiceCollection services,
        GeneratedBehaviors behaviors)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(behaviors);

        var registrations = BehaviorRegistrations.GetOrAdd(services);
        registrations.Generated.Add(behaviors);

        // Replace each open generic in place, so behaviors keep their order.
        for (var i = registrations.Open.Count - 1; i >= 0; i--)
        {
            var open = registrations.Open[i];
            var index = services.IndexOf(open);
            if (index < 0)
            {
                registrations.Open.RemoveAt(i);
                continue;
            }

            if (!behaviors.TryClose(open.ServiceType, open.ImplementationType!, open.Lifetime, out var closed))
                continue;

            services.RemoveAt(index);
            foreach (var descriptor in closed)
            {
                if (!services.Any(d => !d.IsKeyedService && d.ServiceType == descriptor.ServiceType && d.ImplementationType == descriptor.ImplementationType))
                    services.Insert(index++, descriptor);
            }

            registrations.Open.RemoveAt(i);
        }

        return services;
    }

    private static bool IsBehaviorInterface(Type definition) =>
        definition == typeof(IPipelineBehavior<,>) ||
        definition == typeof(IStreamPipelineBehavior<,>) ||
        definition == typeof(INotificationPipelineBehavior<>);
}
