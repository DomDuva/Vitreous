using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Vitreous.Abstractions;
using Vitreous.Internal;

namespace Vitreous;

/// <summary>
/// Extension methods for registering Vitreous in the ASP.NET Core DI container.
/// </summary>
public static partial class VitreousServiceCollectionExtensions
{
    // Shown by the trim/AOT analyzers when an app calls one of the reflection-based APIs.
    internal const string ReflectionMessage =
        "Vitreous dispatches with reflection when it is registered this way. For trimmed or " +
        "Native AOT apps, install Vitreous.SourceGeneration and call the generated " +
        "AddVitreousHandlers() overload with no assemblies.";

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
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static IServiceCollection AddVitreous(
        this IServiceCollection services,
        Action<VitreousOptions>? configure = null)
    {
        var options = new VitreousOptions();
        configure?.Invoke(options);
        return services.AddVitreous(options);
    }

    /// <summary>
    /// Registers Vitreous core services using an already-built <see cref="VitreousOptions"/>.
    /// </summary>
    /// <remarks>
    /// This overload exists so the source-generated <c>AddVitreousHandlers()</c> can build
    /// the options once and read them back (e.g. <see cref="VitreousOptions.DefaultHandlerLifetime"/>).
    /// Application code should call <see cref="AddVitreous(IServiceCollection, Action{VitreousOptions})"/>.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static IServiceCollection AddVitreous(
        this IServiceCollection services,
        VitreousOptions options)
    {
        AddCoreServices(services, options);

        // Fallback sender — overridden by source-generated VitreousDispatcher when present
        services.TryAdd(new ServiceDescriptor(typeof(ISender), typeof(ReflectionSender), GetDispatcherLifetime(options)));

        return services;
    }

    /// <summary>
    /// Registers Vitreous core services for the source-generated <c>AddVitreousHandlers()</c>:
    /// everything <see cref="AddVitreous(IServiceCollection, VitreousOptions)"/> registers
    /// except the reflection-based sender, so trimmed and Native AOT apps don't keep it.
    /// </summary>
    /// <remarks>
    /// Application code should call the generated <c>AddVitreousHandlers()</c>, which calls this.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IServiceCollection AddVitreousGenerated(
        this IServiceCollection services,
        VitreousOptions options)
    {
        AddCoreServices(services, options);
        return services;
    }

    private static void AddCoreServices(IServiceCollection services, VitreousOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        // Register options for other packages (DevUI, source-generated dispatcher, etc.) to consume.
        // TryAdd: calling AddVitreous more than once must not register a second instance.
        services.TryAddSingleton(options);

        // The sender/publisher resolve handlers from the IServiceProvider they were created
        // with. Unless handlers are singletons, the dispatching services must be scoped so
        // they resolve from the request scope rather than the root provider (a transient
        // handler resolved from the root would capture scoped dependencies).
        var lifetime = GetDispatcherLifetime(options);

        // Always register both concrete publishers so the source-generated dispatcher
        // can resolve either by concrete type based on VitreousOptions at runtime.
        services.TryAdd(new ServiceDescriptor(typeof(SequentialPublisher), typeof(SequentialPublisher), lifetime));
        services.TryAdd(new ServiceDescriptor(typeof(ParallelPublisher), typeof(ParallelPublisher), lifetime));

        // Register IPublisher to the chosen strategy (used by the reflection-based path).
        // The source-generated dispatcher overrides IPublisher with VitreousDispatcher,
        // but still resolves the concrete publisher by type internally.
        var publisherType = options.NotificationPublishStrategy == global::Vitreous.NotificationPublishStrategy.Parallel
            ? typeof(ParallelPublisher)
            : typeof(SequentialPublisher);

        services.TryAdd(new ServiceDescriptor(typeof(IPublisher), publisherType, lifetime));
    }

    /// <summary>
    /// Returns the lifetime that dispatching services (sender, publisher) should be
    /// registered with for the configured handler lifetime.
    /// </summary>
    private static ServiceLifetime GetDispatcherLifetime(VitreousOptions options) =>
        options.DefaultHandlerLifetime == ServiceLifetime.Singleton
            ? ServiceLifetime.Singleton
            : ServiceLifetime.Scoped;

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
    /// <c>IStreamQueryHandler</c>, and <c>INotificationHandler</c> implementations.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no assemblies are supplied. This usually means the
    /// <c>Vitreous.SourceGeneration</c> package is not installed on the entry-point project.
    /// </exception>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
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
    /// <exception cref="InvalidOperationException">
    /// Thrown when no assemblies are supplied. This usually means the
    /// <c>Vitreous.SourceGeneration</c> package is not installed on the entry-point project.
    /// </exception>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static IServiceCollection AddVitreousHandlers(
        this IServiceCollection services,
        Action<VitreousOptions>? configure,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (assemblies is null || assemblies.Length == 0)
        {
            throw new InvalidOperationException(
                "AddVitreousHandlers() was called without any assemblies to scan, and no " +
                "source-generated overload is available. Either:\n" +
                "  1. Install the 'Vitreous.SourceGeneration' package on this project (the one " +
                "that calls AddVitreousHandlers()) so handlers are discovered at build time, or\n" +
                "  2. Pass the assemblies that contain your handlers explicitly, e.g. " +
                "services.AddVitreousHandlers(typeof(Program).Assembly).");
        }

        var options = new VitreousOptions();
        configure?.Invoke(options);
        services.AddVitreous(options);

        var lifetime = options.DefaultHandlerLifetime;

        foreach (var assembly in assemblies)
        {
            foreach (var type in GetLoadableTypes(assembly))
            {
                if (type.IsAbstract || type.IsInterface || !type.IsClass || type.IsGenericTypeDefinition)
                    continue;

                foreach (var iface in type.GetInterfaces())
                {
                    if (!iface.IsGenericType)
                        continue;

                    var definition = iface.GetGenericTypeDefinition();

                    if (definition == typeof(INotificationHandler<>))
                    {
                        services.TryAddEnumerable(new ServiceDescriptor(iface, type, lifetime));
                    }
                    else if (definition == typeof(ICommandHandler<,>)   ||
                             definition == typeof(ICommandHandler<>)    ||
                             definition == typeof(IQueryHandler<,>)     ||
                             definition == typeof(IStreamQueryHandler<,>))
                    {
                        services.TryAdd(new ServiceDescriptor(iface, type, lifetime));
                    }
                }
            }
        }

        return services;
    }

    [RequiresUnreferencedCode(ReflectionMessage)]
    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Skip types that failed to load (missing optional dependencies, etc.)
            return ex.Types.Where(static t => t is not null)!;
        }
    }
}
