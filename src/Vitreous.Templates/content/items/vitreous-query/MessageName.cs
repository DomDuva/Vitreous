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

public sealed record MessageNameQuery : IQuery<ResultType>;
#if (!HasResult)

public sealed record MessageNameResponse;
#endif
#if (validation)

public sealed class MessageNameValidator : AbstractValidator<MessageNameQuery>
{
    public MessageNameValidator()
    {
        // RuleFor(q => q.Id).NotEmpty();
    }
}
#endif

public sealed class MessageNameHandler : IQueryHandler<MessageNameQuery, ResultType>
{
    public ValueTask<Result<ResultType>> Handle(MessageNameQuery query, CancellationToken ct = default)
    {
        // Return the value, or an Error such as Error.NotFound("...") when it fails.
        throw new NotImplementedException();
    }
}
