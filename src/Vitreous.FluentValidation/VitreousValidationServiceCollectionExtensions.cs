using Vitreous.FluentValidation.Behaviors;

namespace Vitreous.FluentValidation;

/// <summary>
/// Extension methods to register the Vitreous FluentValidation pipeline behaviors.
/// </summary>
public static class VitreousValidationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ValidationBehavior{TMessage, TResult}"/> for commands and queries and
    /// <see cref="StreamValidationBehavior{TMessage, TResult}"/> for stream queries. Each runs the
    /// message's <see cref="IValidator{T}"/> services before the handler and returns
    /// <see cref="Error.Validation"/> when they fail. Calling this more than once does nothing.
    /// </summary>
    /// <remarks>
    /// Validators are not registered by this method. Register them yourself, for example with
    /// <c>services.AddValidatorsFromAssemblyContaining&lt;Program&gt;()</c> from the
    /// <c>FluentValidation.DependencyInjectionExtensions</c> package.
    /// <para>
    /// Behaviors run in registration order, outermost first. Call this before
    /// <c>AddVitreousCaching()</c> so that invalid queries never reach the cache.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddVitreousValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Transient: AddValidatorsFromAssembly* registers validators as scoped by default, and a
        // longer-lived behavior would hold on to the first scope's validators.
        // AddVitreousBehavior closes them over every message when the source generator is used.
        services.AddVitreousBehavior(typeof(ValidationBehavior<,>), ServiceLifetime.Transient);
        services.AddVitreousBehavior(typeof(StreamValidationBehavior<,>), ServiceLifetime.Transient);

        return services;
    }
}
