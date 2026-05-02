using System.Reflection;
using Vitreous.Abstractions;
using Vitreous.Internal;

namespace Vitreous;

/// <summary>
/// Extension methods for registering Vitreous in the ASP.NET Core DI container.
/// </summary>
public static partial class VitreousServiceCollectionExtensions
{
    /// <summary>
    /// Registers Vitreous core services (sender, publisher).
    /// <para>
    /// When the <c>Vitreous.SourceGeneration</c> package is referenced, it generates a concrete
    /// <c>AddVitreousHandlers()</c> call and a <c>VitreousDispatcher</c> that replace the
    /// reflection-based fallbacks registered here.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration callback.</param>
    public static IServiceCollection AddVitreous(
        this IServiceCollection services,
        Action<VitreousOptions>? configure = null)
    {
        var options = new VitreousOptions();
        configure?.Invoke(options);

        // Register options for other packages (DevUI, etc.) to consume
        services.AddSingleton(options);

        // Fallback sender/publisher — overridden by source-generated VitreousDispatcher when present
        services.TryAddSingleton<ISender, ReflectionSender>();
        services.TryAddSingleton<IPublisher, SequentialPublisher>();
        // Always register the concrete publisher so the generated dispatcher can resolve it
        services.TryAddSingleton<SequentialPublisher>();

        return services;
    }

    /// <summary>
    /// Registers Vitreous core services and scans the provided <paramref name="assemblies"/>
    /// for handler implementations at startup.
    /// <para>
    /// Use this overload in multi-project solutions when you cannot or do not want to use
    /// the <c>Vitreous.SourceGeneration</c> package. Handlers are discovered via reflection
    /// and dispatched through <c>ReflectionSender</c> at runtime.
    /// </para>
    /// <para>
    /// For zero-reflection, AOT-compatible dispatch install <c>Vitreous.SourceGeneration</c>
    /// on the entry-point project and call the parameterless <c>AddVitreousHandlers()</c>
    /// overload instead.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assemblies">
    /// One or more assemblies to scan for <c>ICommandHandler</c>, <c>IQueryHandler</c>,
    /// and <c>INotificationHandler</c> implementations.
    /// </param>
    public static IServiceCollection AddVitreousHandlers(
        this IServiceCollection services,
        params Assembly[] assemblies)
        => services.AddVitreousHandlers(configure: null, assemblies);

    /// <summary>
    /// Registers Vitreous core services, applies <paramref name="configure"/>, and scans
    /// the provided <paramref name="assemblies"/> for handler implementations at startup.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration callback.</param>
    /// <param name="assemblies">
    /// One or more assemblies to scan for handler implementations.
    /// </param>
    public static IServiceCollection AddVitreousHandlers(
        this IServiceCollection services,
        Action<VitreousOptions>? configure,
        params Assembly[] assemblies)
    {
        services.AddVitreous(configure);

        if (assemblies.Length == 0)
            return services;

        var handlerInterfaces = new[]
        {
            typeof(ICommandHandler<,>),
            typeof(ICommandHandler<>),
            typeof(IQueryHandler<,>),
            typeof(INotificationHandler<>),
        };

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface || !type.IsClass)
                    continue;

                foreach (var iface in type.GetInterfaces())
                {
                    if (!iface.IsGenericType)
                        continue;

                    var definition = iface.GetGenericTypeDefinition();

                    if (definition == typeof(ICommandHandler<,>)     ||
                        definition == typeof(ICommandHandler<>)       ||
                        definition == typeof(IQueryHandler<,>)        ||
                        definition == typeof(INotificationHandler<>))
                    {
                        services.TryAddSingleton(iface, type);
                    }
                }
            }
        }

        return services;
    }
}
