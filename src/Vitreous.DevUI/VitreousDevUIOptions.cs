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
}
