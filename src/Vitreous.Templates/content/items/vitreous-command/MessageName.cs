#if (AddSystemUsings)
using System;
using System.Threading;
using System.Threading.Tasks;
#endif
#if (validation)
using FluentValidation;
#endif
using Vitreous;
using Vitreous.Abstractions;

namespace ItemNamespace;

#if (HasResult)
public sealed record MessageNameCommand : ICommand<ResultType>;
#else
public sealed record MessageNameCommand : ICommand;
#endif
#if (validation)

public sealed class MessageNameValidator : AbstractValidator<MessageNameCommand>
{
    public MessageNameValidator()
    {
        // RuleFor(c => c.Name).NotEmpty();
    }
}
#endif

#if (HasResult)
public sealed class MessageNameHandler : ICommandHandler<MessageNameCommand, ResultType>
{
    public ValueTask<Result<ResultType>> Handle(MessageNameCommand command, CancellationToken ct = default)
    {
        // Return the value, or an Error such as Error.NotFound("...") when it fails.
        throw new NotImplementedException();
    }
}
#else
public sealed class MessageNameHandler : ICommandHandler<MessageNameCommand>
{
    public ValueTask<Result> Handle(MessageNameCommand command, CancellationToken ct = default)
    {
        // Return Result.Success, or an Error such as Error.NotFound("...") when it fails.
        throw new NotImplementedException();
    }
}
#endif
