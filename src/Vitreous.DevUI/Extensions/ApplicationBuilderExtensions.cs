using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    public static IServiceCollection AddVitreousDevUI(
        this IServiceCollection services,
        Action<VitreousDevUIOptions>? configure = null)
    {
        var options = new VitreousDevUIOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton(new TraceStore(options.TraceBufferCapacity));
        services.AddSingleton<SseConnectionManager>();

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

        return services;
    }

    /// <summary>
    /// Enables the Vitreous Developer UI middleware.
    /// Wires up the <see cref="SseConnectionManager"/> to the <see cref="TraceStore"/>
    /// and mounts the middleware at the configured path prefix.
    /// </summary>
    /// <remarks>
    /// Only call this in development. Example:
    /// <code>
    /// if (app.Environment.IsDevelopment())
    ///     app.UseVitreousDevUI();
    /// </code>
    /// </remarks>
    public static IApplicationBuilder UseVitreousDevUI(this IApplicationBuilder app)
    {
        // Wire TraceStore → SseConnectionManager broadcast
        var store = app.ApplicationServices.GetRequiredService<TraceStore>();
        var sse   = app.ApplicationServices.GetRequiredService<SseConnectionManager>();

        store.TraceAdded += sse.Broadcast;

        // Register the custom log provider so handler ILogger calls are captured
        var loggerFactory = app.ApplicationServices.GetRequiredService<ILoggerFactory>();
        var traceProvider = new TraceLoggerProvider();
        loggerFactory.AddProvider(traceProvider);

        return app.UseMiddleware<VitreousDevUIMiddleware>();
    }
}
