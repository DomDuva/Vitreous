using System.Diagnostics;
using Vitreous;
using Vitreous.Abstractions;

namespace VitreousApi._1.Common;

/// <summary>
/// Logs how long every command and query takes, and why it failed. One open-generic behavior
/// covers them all: for a command without a result, <typeparamref name="TResult"/> is <see cref="Unit"/>.
/// </summary>
public sealed class LoggingBehavior<TMessage, TResult>(ILogger<LoggingBehavior<TMessage, TResult>> logger)
    : IPipelineBehavior<TMessage, TResult>
{
    public async ValueTask<Result<TResult>> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResult> next,
        CancellationToken ct = default)
    {
        var started = Stopwatch.GetTimestamp();
        var result  = await next(message, ct);
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        if (result.IsSuccess)
            logger.LogInformation("{Message} handled in {ElapsedMs:0.0} ms", typeof(TMessage).Name, elapsed);
        else
            logger.LogWarning("{Message} failed in {ElapsedMs:0.0} ms: {Error}", typeof(TMessage).Name, elapsed, result.Error);

        return result;
    }
}
