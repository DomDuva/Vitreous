namespace Vitreous;

/// <summary>
/// Mutable bag of annotations written by pipeline behaviors during a single dispatch.
/// <see cref="VitreousContext.Current"/> holds the instance for the current async call chain.
/// </summary>
/// <remarks>
/// Behaviors set properties on this object; outer behaviors (e.g. tracing) read them
/// after <c>next()</c> returns. Because it is a reference type stored in an
/// <see cref="System.Threading.AsyncLocal{T}"/>, mutations are visible across the
/// async call chain without needing to propagate values back up through return values.
/// </remarks>
public sealed class PipelineAnnotations
{
    /// <summary>
    /// Set to <see langword="true"/> by the caching behavior when the result was
    /// served from cache rather than from the handler.
    /// Always <see langword="false"/> when the caching package is not in use.
    /// </summary>
    public bool IsCacheHit { get; set; }
}

/// <summary>
/// Provides ambient access to the <see cref="PipelineAnnotations"/> for the current
/// async message dispatch. Scoped to the async execution context — safe across <c>await</c>.
/// </summary>
public static class VitreousContext
{
    private static readonly AsyncLocal<PipelineAnnotations?> _current = new();

    /// <summary>
    /// The annotations bag for the in-flight dispatch, or <see langword="null"/> when
    /// called outside a dispatch (e.g. background threads, startup code).
    /// </summary>
    public static PipelineAnnotations? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
