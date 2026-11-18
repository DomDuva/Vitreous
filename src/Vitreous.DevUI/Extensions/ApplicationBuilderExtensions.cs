using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vitreous.Abstractions;
using Vitreous.DevUI.Middleware;
using Vitreous.DevUI.Tracing;

namespace Vitreous.DevUI.Extensions;

/// <summary>
/// Extension methods to register and enable the Vitreous Developer UI.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Registers Vitreous DevUI services in the DI container.
    /// Call this in <c>builder.Services</c> before <c>builder.Build()</c>.
    /// </summary>
    /// <remarks>
    /// Registering the services is safe in every environment: nothing is traced until
    /// <see cref="UseVitreousDevUI"/> or <see cref="MapVitreousDevUI"/> activates the UI, which by
    /// default only happens in <c>Development</c> (see <see cref="VitreousDevUIOptions.Enabled"/>).
    /// </remarks>
    public static IServiceCollection AddVitreousDevUI(
        this IServiceCollection services,
        Action<VitreousDevUIOptions>? configure = null)
    {
        var options = new VitreousDevUIOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton(new TraceStore(options.TraceBufferCapacity));
        services.TryAddSingleton<DevUIState>();
        services.TryAddSingleton<DevUISerializer>();
        services.TryAddSingleton<SseConnectionManager>();

        // TracingBehavior must be the outermost pipeline behavior so it records
        // every dispatch — including cases where an inner behavior (e.g. CachingBehavior)
        // short-circuits and never calls next().
        //
        // TryAddEnumerable would append, making it innermost. Instead we insert at
        // the index of the first existing IPipelineBehavior<,> registration (or at
        // the end if none exist yet) so TracingBehavior always wraps everything else.
        var pipelineBehaviorType = typeof(IPipelineBehavior<,>);
        var tracingBehaviorType  = typeof(TracingBehavior<,>);

        if (!services.Any(sd =>
                sd.ServiceType       == pipelineBehaviorType &&
                sd.ImplementationType == tracingBehaviorType))
        {
            var insertAt = services
                .Select((sd, i) => (sd, i))
                .Where(x => x.sd.ServiceType == pipelineBehaviorType)
                .Select(x => x.i)
                .DefaultIfEmpty(services.Count)
                .First();

            services.Insert(insertAt, ServiceDescriptor.Singleton(
                pipelineBehaviorType,
                tracingBehaviorType));
        }

        // StreamTracingBehavior must be the outermost IStreamPipelineBehavior<,> registration
        // for the same reason — it records the full setup phase including any inner behaviors.
        var streamPipelineBehaviorType = typeof(IStreamPipelineBehavior<,>);
        var streamTracingBehaviorType  = typeof(StreamTracingBehavior<,>);

        if (!services.Any(sd =>
                sd.ServiceType        == streamPipelineBehaviorType &&
                sd.ImplementationType == streamTracingBehaviorType))
        {
            var insertAt = services
                .Select((sd, i) => (sd, i))
                .Where(x => x.sd.ServiceType == streamPipelineBehaviorType)
                .Select(x => x.i)
                .DefaultIfEmpty(services.Count)
                .First();

            services.Insert(insertAt, ServiceDescriptor.Singleton(
                streamPipelineBehaviorType,
                streamTracingBehaviorType));
        }

        // NotificationTracingBehavior — outermost INotificationPipelineBehavior<> for the same reason.
        var notificationBehaviorType        = typeof(INotificationPipelineBehavior<>);
        var notificationTracingBehaviorType = typeof(NotificationTracingBehavior<>);

        if (!services.Any(sd =>
                sd.ServiceType        == notificationBehaviorType &&
                sd.ImplementationType == notificationTracingBehaviorType))
        {
            var insertAt = services
                .Select((sd, i) => (sd, i))
                .Where(x => x.sd.ServiceType == notificationBehaviorType)
                .Select(x => x.i)
                .DefaultIfEmpty(services.Count)
                .First();

            services.Insert(insertAt, ServiceDescriptor.Singleton(
                notificationBehaviorType,
                notificationTracingBehaviorType));
        }

        return services;
    }

    /// <summary>
    /// Enables the Vitreous Developer UI as middleware at the configured path prefix and
    /// activates tracing — when <see cref="VitreousDevUIOptions.Enabled"/> allows it (by default,
    /// only in the <c>Development</c> environment). Otherwise this does nothing.
    /// </summary>
    /// <remarks>
    /// To protect the UI with authorization, use <see cref="MapVitreousDevUI"/> instead:
    /// <code>
    /// app.MapVitreousDevUI().RequireAuthorization("Admins");
    /// </code>
    /// </remarks>
    public static IApplicationBuilder UseVitreousDevUI(this IApplicationBuilder app)
    {
        if (!TryActivate(app.ApplicationServices))
            return app;

        return app.UseMiddleware<VitreousDevUIMiddleware>();
    }

    /// <summary>
    /// Maps the Vitreous Developer UI endpoints under <paramref name="pathPrefix"/> (default:
    /// <see cref="VitreousDevUIOptions.PathPrefix"/>) and activates tracing — when
    /// <see cref="VitreousDevUIOptions.Enabled"/> allows it (by default, only in the
    /// <c>Development</c> environment). Otherwise no endpoints are mapped.
    /// </summary>
    /// <returns>
    /// The route group, so endpoint conventions apply to every DevUI endpoint, e.g.
    /// <c>app.MapVitreousDevUI().RequireAuthorization()</c>.
    /// </returns>
    public static RouteGroupBuilder MapVitreousDevUI(this IEndpointRouteBuilder endpoints, string? pathPrefix = null)
    {
        var services = endpoints.ServiceProvider;
        var options  = GetDevUIService<VitreousDevUIOptions>(services);
        var group    = endpoints.MapGroup(pathPrefix ?? options.PathPrefix);

        if (!TryActivate(services))
            return group;

        // Routing tolerates a trailing slash, so this also serves "{prefix}/".
        group.MapGet("", static (HttpContext context) => DevUIEndpoints.WriteUiAsync(context));

        group.MapGet("/events", static (HttpContext context, TraceStore store, SseConnectionManager sse,
                DevUISerializer serializer, VitreousDevUIOptions o) =>
            DevUIEndpoints.StreamEventsAsync(context, store, sse, serializer, o.SseHeartbeatInterval));

        group.MapGet("/api/traces", static (HttpContext context, TraceStore store, DevUISerializer serializer) =>
            DevUIEndpoints.WriteTracesAsync(context, store, serializer));

        group.MapGet("/api/config", static (HttpContext context, VitreousDevUIOptions o, DevUISerializer serializer) =>
            DevUIEndpoints.WriteConfigAsync(context, o, serializer));

        group.ExcludeFromDescription();
        return group;
    }

    /// <summary>
    /// Activates the developer UI once, if enabled: connects the trace store to live clients,
    /// starts capturing handler logs, and switches the tracing behaviors on.
    /// </summary>
    private static bool TryActivate(IServiceProvider services)
    {
        var options = GetDevUIService<VitreousDevUIOptions>(services);
        var enabled = options.Enabled ?? services.GetService<IHostEnvironment>()?.IsDevelopment() ?? false;
        if (!enabled)
            return false;

        var state = services.GetRequiredService<DevUIState>();
        lock (state.Gate)
        {
            if (!state.Wired)
            {
                // Wire TraceStore → SseConnectionManager broadcast
                var store = services.GetRequiredService<TraceStore>();
                var sse   = services.GetRequiredService<SseConnectionManager>();
                store.TraceAdded += sse.Broadcast;

                // Register the custom log provider so handler ILogger calls are captured
                services.GetRequiredService<ILoggerFactory>().AddProvider(new TraceLoggerProvider());

                state.Wired = true;
            }

            state.IsActive = true;
        }

        return true;
    }

    private static T GetDevUIService<T>(IServiceProvider services) where T : notnull =>
        services.GetService<T>() ?? throw new InvalidOperationException(
            "Vitreous DevUI services are not registered. Call builder.Services.AddVitreousDevUI() before building the app.");
}
