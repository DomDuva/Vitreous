namespace Vitreous.DevUI;

/// <summary>
/// Configuration options for the Vitreous Developer UI.
/// </summary>
public sealed class VitreousDevUIOptions
{
    /// <summary>
    /// Whether the developer UI and tracing are active. <see langword="null"/> (the default)
    /// means active only when the host environment is <c>Development</c>. Set to
    /// <see langword="true"/> to enable it in other environments (protect it with
    /// <c>MapVitreousDevUI().RequireAuthorization()</c>), or <see langword="false"/> to turn it off.
    /// </summary>
    /// <remarks>
    /// While inactive, the tracing behaviors pass straight through and record nothing, and
    /// <c>UseVitreousDevUI()</c> / <c>MapVitreousDevUI()</c> do not expose any endpoints.
    /// </remarks>
    public bool? Enabled { get; set; }

    /// <summary>
    /// URL path prefix where the developer UI is mounted.
    /// Defaults to <c>/vitreous</c>.
    /// </summary>
    public string PathPrefix { get; set; } = "/vitreous";

    /// <summary>
    /// Maximum number of execution traces to keep in memory.
    /// When the buffer is full, the oldest entry is dropped.
    /// Defaults to <c>500</c>.
    /// </summary>
    public int TraceBufferCapacity { get; set; } = 500;

    /// <summary>
    /// Maximum number of stream query items whose serialized form is captured for the
    /// "Output" tab of a stream trace. Items beyond this limit are still counted but not
    /// stored, which keeps memory bounded for long-running or infinite streams.
    /// Defaults to <c>100</c>.
    /// </summary>
    public int MaxCapturedStreamItems { get; set; } = 100;

    /// <summary>
    /// Property names whose values are masked as <c>"***"</c> in captured input, output and
    /// stream items. Matching is case-insensitive and by substring, so <c>"token"</c> also masks
    /// <c>accessToken</c> and <c>refreshToken</c>. Properties marked with
    /// <see cref="Vitreous.SensitiveAttribute"/> are always masked.
    /// </summary>
    public ISet<string> RedactedPropertyNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "password", "secret", "token", "apiKey", "authorization",
        "creditCard", "cardNumber", "cvv", "ssn",
    };

    /// <summary>
    /// How often an idle live-trace connection receives a keep-alive comment, so proxies
    /// don't close it. Defaults to 15 seconds.
    /// </summary>
    public TimeSpan SseHeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);
}
