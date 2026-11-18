using Microsoft.AspNetCore.Http;
using Vitreous.DevUI.Tracing;

namespace Vitreous.DevUI.Middleware;

/// <summary>
/// ASP.NET Core middleware that serves the Vitreous Developer UI.
/// Mounts these endpoints under the configured path prefix (default <c>/vitreous</c>):
/// <list type="bullet">
///   <item><c>GET /vitreous</c> — serves the embedded single-file HTML UI.</item>
///   <item><c>GET /vitreous/events</c> — SSE stream of live <see cref="ExecutionTrace"/> events.</item>
///   <item><c>GET /vitreous/api/traces</c> — JSON snapshot of the current ring buffer (for initial load).</item>
///   <item><c>GET /vitreous/api/config</c> — settings the UI needs.</item>
/// </list>
/// Prefer <c>MapVitreousDevUI()</c>, which supports endpoint conventions such as
/// <c>RequireAuthorization()</c>.
/// </summary>
public sealed class VitreousDevUIMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TraceStore _store;
    private readonly SseConnectionManager _sse;
    private readonly DevUISerializer _serializer;
    private readonly VitreousDevUIOptions _options;
    private readonly string _pathPrefix;

    public VitreousDevUIMiddleware(
        RequestDelegate next,
        TraceStore store,
        SseConnectionManager sse,
        DevUISerializer serializer,
        VitreousDevUIOptions options)
    {
        _next       = next;
        _store      = store;
        _sse        = sse;
        _serializer = serializer;
        _options    = options;
        _pathPrefix = options.PathPrefix.TrimEnd('/');
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method))
            return _next(context);

        var path = context.Request.Path.Value ?? string.Empty;

        if (path.Equals($"{_pathPrefix}/events", StringComparison.OrdinalIgnoreCase))
            return DevUIEndpoints.StreamEventsAsync(context, _store, _sse, _serializer, _options.SseHeartbeatInterval);

        if (path.Equals($"{_pathPrefix}/api/traces", StringComparison.OrdinalIgnoreCase))
            return DevUIEndpoints.WriteTracesAsync(context, _store, _serializer);

        if (path.Equals($"{_pathPrefix}/api/config", StringComparison.OrdinalIgnoreCase))
            return DevUIEndpoints.WriteConfigAsync(context, _options, _serializer);

        if (path.Equals(_pathPrefix, StringComparison.OrdinalIgnoreCase) ||
            path.Equals(_pathPrefix + "/", StringComparison.OrdinalIgnoreCase))
            return DevUIEndpoints.WriteUiAsync(context);

        return _next(context);
    }
}
