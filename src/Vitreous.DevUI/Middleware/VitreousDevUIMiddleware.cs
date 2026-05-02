using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Vitreous.DevUI.Tracing;

namespace Vitreous.DevUI.Middleware;

/// <summary>
/// ASP.NET Core middleware that serves the Vitreous Developer UI.
/// Mounts three endpoints under the configured path prefix (default <c>/vitreous</c>):
/// <list type="bullet">
///   <item><c>GET /vitreous</c> — serves the embedded single-file HTML UI.</item>
///   <item><c>GET /vitreous/events</c> — SSE stream of live <see cref="ExecutionTrace"/> events.</item>
///   <item><c>GET /vitreous/api/traces</c> — JSON snapshot of the current ring buffer (for initial load).</item>
/// </list>
/// </summary>
public sealed class VitreousDevUIMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TraceStore _store;
    private readonly SseConnectionManager _sse;
    private readonly string _pathPrefix;
    private readonly JsonSerializerOptions _json;

    // Cached embedded HTML
    private static readonly Lazy<byte[]> _embeddedHtml = new(LoadEmbeddedHtml);

    public VitreousDevUIMiddleware(
        RequestDelegate next,
        TraceStore store,
        SseConnectionManager sse,
        VitreousDevUIOptions options)
    {
        _next       = next;
        _store      = store;
        _sse        = sse;
        _pathPrefix = options.PathPrefix.TrimEnd('/');
        _json       = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented        = false,
            Converters           = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // GET /vitreous/events — SSE stream
        if (path.Equals($"{_pathPrefix}/events", StringComparison.OrdinalIgnoreCase) &&
            context.Request.Method == HttpMethods.Get)
        {
            await HandleSseAsync(context);
            return;
        }

        // GET /vitreous/api/traces — JSON snapshot
        if (path.Equals($"{_pathPrefix}/api/traces", StringComparison.OrdinalIgnoreCase) &&
            context.Request.Method == HttpMethods.Get)
        {
            await HandleTracesApiAsync(context);
            return;
        }

        // GET /vitreous — UI page (also matches /vitreous/)
        if ((path.Equals(_pathPrefix, StringComparison.OrdinalIgnoreCase) ||
             path.Equals(_pathPrefix + "/", StringComparison.OrdinalIgnoreCase)) &&
            context.Request.Method == HttpMethods.Get)
        {
            await HandleUiAsync(context);
            return;
        }

        await _next(context);
    }

    // -------------------------------------------------------------------------
    // Handlers
    // -------------------------------------------------------------------------

    private async Task HandleSseAsync(HttpContext context)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection   = "keep-alive";
        context.Response.Headers["X-Accel-Buffering"] = "no";  // nginx: disable buffering

        await context.Response.StartAsync(context.RequestAborted);

        using var subscription = _sse.Subscribe();

        // Replay historical traces on connect so the UI has context immediately
        var history = _store.GetAll();
        foreach (var trace in history)
        {
            var json    = JsonSerializer.Serialize(trace, _json);
            var payload = Encoding.UTF8.GetBytes($"data: {json}\n\n");
            await context.Response.Body.WriteAsync(payload, context.RequestAborted);
        }
        await context.Response.Body.FlushAsync(context.RequestAborted);

        // Stream live events until client disconnects
        try
        {
            await foreach (var payload in subscription.Reader.ReadAllAsync(context.RequestAborted))
            {
                var bytes = Encoding.UTF8.GetBytes(payload);
                await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — normal exit
        }
    }

    private async Task HandleTracesApiAsync(HttpContext context)
    {
        context.Response.ContentType = "application/json";
        var traces = _store.GetAll();
        await context.Response.WriteAsync(JsonSerializer.Serialize(traces, _json));
    }

    private async Task HandleUiAsync(HttpContext context)
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.Body.WriteAsync(_embeddedHtml.Value);
    }

    // -------------------------------------------------------------------------
    // Embedded asset loading
    // -------------------------------------------------------------------------

    private static byte[] LoadEmbeddedHtml()
    {
        var assembly   = typeof(VitreousDevUIMiddleware).Assembly;
        var name       = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("index.html", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                "Vitreous.DevUI: embedded resource 'index.html' not found in the assembly. " +
                "Ensure the file is marked as EmbeddedResource in Vitreous.DevUI.csproj.");

        using var stream = assembly.GetManifestResourceStream(name)!;
        var buffer = new byte[stream.Length];
        _ = stream.Read(buffer, 0, buffer.Length);
        return buffer;
    }
}
