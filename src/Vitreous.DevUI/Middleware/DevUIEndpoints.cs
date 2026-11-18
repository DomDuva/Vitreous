using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Vitreous.DevUI.Tracing;

namespace Vitreous.DevUI.Middleware;

/// <summary>
/// The developer UI's endpoints, shared by <see cref="VitreousDevUIMiddleware"/> and
/// <c>MapVitreousDevUI()</c>.
/// </summary>
internal static class DevUIEndpoints
{
    private static readonly byte[] s_heartbeat = Encoding.UTF8.GetBytes(": ping\n\n");

    // Cached embedded HTML and the Content-Security-Policy that allows exactly its inline blocks
    private static readonly Lazy<(byte[] Html, string Csp)> s_page = new(LoadPage);

    /// <summary><c>GET {prefix}</c> — the embedded single-file HTML UI.</summary>
    public static async Task WriteUiAsync(HttpContext context)
    {
        var (html, csp) = s_page.Value;

        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.ContentSecurityPolicy = csp;
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.XFrameOptions = "DENY";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        await context.Response.Body.WriteAsync(html, context.RequestAborted);
    }

    /// <summary><c>GET {prefix}/api/traces</c> — JSON snapshot of the ring buffer.</summary>
    public static async Task WriteTracesAsync(HttpContext context, TraceStore store, DevUISerializer serializer)
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(serializer.SerializeTrace(store.GetAll()), context.RequestAborted);
    }

    /// <summary><c>GET {prefix}/api/config</c> — settings the UI needs (e.g. how many traces to keep).</summary>
    public static async Task WriteConfigAsync(HttpContext context, VitreousDevUIOptions options, DevUISerializer serializer)
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            serializer.SerializeTrace(new { traceBufferCapacity = options.TraceBufferCapacity }),
            context.RequestAborted);
    }

    /// <summary><c>GET {prefix}/events</c> — SSE stream of live traces, after replaying the buffer.</summary>
    public static async Task StreamEventsAsync(
        HttpContext context,
        TraceStore store,
        SseConnectionManager sse,
        DevUISerializer serializer,
        TimeSpan heartbeatInterval)
    {
        var ct = context.RequestAborted;

        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection   = "keep-alive";
        context.Response.Headers["X-Accel-Buffering"] = "no";  // nginx: disable buffering

        try
        {
            await context.Response.StartAsync(ct);

            using var subscription = sse.Subscribe();

            // Replay historical traces on connect so the UI has context immediately
            foreach (var trace in store.GetAll())
                await WriteAsync(context, $"data: {serializer.SerializeTrace(trace)}\n\n", ct);
            await context.Response.Body.FlushAsync(ct);

            // Stream live events until the client disconnects, with a keep-alive comment
            // whenever the connection has been idle for heartbeatInterval.
            var reader = subscription.Reader;
            Task<bool>? waitForData = null;

            while (true)
            {
                waitForData ??= reader.WaitToReadAsync(ct).AsTask();

                var completed = await Task.WhenAny(waitForData, Task.Delay(heartbeatInterval, ct));
                if (completed != waitForData)
                {
                    ct.ThrowIfCancellationRequested();
                    await context.Response.Body.WriteAsync(s_heartbeat, ct);
                    await context.Response.Body.FlushAsync(ct);
                    continue;
                }

                // False when the channel was completed: this client was dropped for being too slow.
                if (!await waitForData)
                    break;
                waitForData = null;

                while (reader.TryRead(out var payload))
                    await WriteAsync(context, payload, ct);
                await context.Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client disconnected — normal exit
        }
    }

    private static ValueTask WriteAsync(HttpContext context, string payload, CancellationToken ct) =>
        context.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(payload), ct);

    /// <summary>
    /// Loads the page and builds a strict CSP for it: no external resources, no framing, and
    /// only the page's own inline script and style blocks, allowed by their SHA-256 hashes.
    /// </summary>
    private static (byte[] Html, string Csp) LoadPage()
    {
        var html = LoadEmbeddedHtml();
        var text = Encoding.UTF8.GetString(html);

        static string Hashes(string text, string tag) => string.Join(" ",
            Regex.Matches(text, $"<{tag}>(.*?)</{tag}>", RegexOptions.Singleline)
                .Select(m => $"'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(m.Groups[1].Value)))}'"));

        var csp =
            "default-src 'none'; " +
            $"script-src {Hashes(text, "script")}; " +
            $"style-src {Hashes(text, "style")}; " +
            "connect-src 'self'; img-src 'self' data:; " +
            "base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

        return (html, csp);
    }

    private static byte[] LoadEmbeddedHtml()
    {
        var assembly = typeof(DevUIEndpoints).Assembly;
        var name     = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("index.html", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                "Vitreous.DevUI: embedded resource 'index.html' not found in the assembly. " +
                "Ensure the file is marked as EmbeddedResource in Vitreous.DevUI.csproj.");

        using var stream = assembly.GetManifestResourceStream(name)!;
        var buffer = new byte[stream.Length];
        stream.ReadExactly(buffer);
        return buffer;
    }
}
