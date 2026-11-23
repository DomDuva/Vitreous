#if (AddSystemUsings)
using System.Threading;
using System.Threading.Tasks;
#endif
using Vitreous;
using Vitreous.Abstractions;

namespace ItemNamespace;

// Wraps every command and query once it is registered. Behaviors run in registration order:
//   services.AddVitreousBehavior(typeof(MessageNameBehavior<,>));
// For a command without a result, TResult is Unit.
public sealed class MessageNameBehavior<TMessage, TResult> : IPipelineBehavior<TMessage, TResult>
{
    public async ValueTask<Result<TResult>> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResult> next,
        CancellationToken ct = default)
    {
        // Before the handler. Return an Error instead of calling next to skip it.
        var result = await next(message, ct);

        // After the handler.
        return result;
    }
}
