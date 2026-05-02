namespace Vitreous;

/// <summary>
/// Configuration options for the Vitreous mediator.
/// </summary>
public sealed class VitreousOptions
{
    /// <summary>
    /// Default service lifetime for registered handlers.
    /// Defaults to <see cref="ServiceLifetime.Singleton"/> — appropriate for stateless handlers.
    /// </summary>
    public ServiceLifetime DefaultHandlerLifetime { get; set; } = ServiceLifetime.Singleton;
}
