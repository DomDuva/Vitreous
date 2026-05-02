using System.Diagnostics;
using Vitreous.Abstractions;

namespace Vitreous.Sample.Caching.Behaviors;

/// <summary>
/// Example pipeline behavior: logs the message type and duration for every command/query.
/// Demonstrates how to write a cross-cutting behavior in Vitreous.
/// </summary>
public sealed class LoggingBehavior<TMessage, TResult> : IPipelineBehavior<TMessage, TResult>
{
    private readonly ILogger<LoggingBehavior<TMessage, TResult>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TMessage, TResult>> logger)
        => _logger = logger;

    public async ValueTask<Result<TResult>> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResult> next,
        CancellationToken ct = default)
    {
        var name = typeof(TMessage).Name;
        _logger.LogDebug("→ Dispatching {MessageType}", name);

        var sw     = Stopwatch.StartNew();
        var result = await next(message, ct);
        sw.Stop();

        if (result.IsSuccess)
            _logger.LogDebug("← {MessageType} succeeded in {ElapsedMs}ms", name, sw.ElapsedMilliseconds);
        else
            _logger.LogWarning("← {MessageType} failed in {ElapsedMs}ms — [{Code}] {Message}",
                name, sw.ElapsedMilliseconds, result.Error.Code, result.Error.Message);

        return result;
    }
}
