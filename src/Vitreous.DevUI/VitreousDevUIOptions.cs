namespace Vitreous.DevUI;

/// <summary>
/// Configuration options for the Vitreous Developer UI.
/// </summary>
public sealed class VitreousDevUIOptions
{
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
}
