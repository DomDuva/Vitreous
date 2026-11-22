using System.Runtime.CompilerServices;

namespace Vitreous.FluentValidationTests;

// Messages, handlers and validators for the dispatch tests. They are internal, not private
// nested types, so the generated dispatcher can name them.

/// <summary>Records which handlers ran, so tests can check that invalid messages never reach them.</summary>
internal sealed class HandlerLog
{
    public List<string> Calls { get; } = [];
}

internal sealed record CreateOrder(string Customer, int Quantity) : ICommand<Guid>;

internal sealed class CreateOrderHandler(HandlerLog log) : ICommandHandler<CreateOrder, Guid>
{
    public ValueTask<Result<Guid>> Handle(CreateOrder command, CancellationToken ct = default)
    {
        log.Calls.Add(nameof(CreateOrder));
        return new(Guid.NewGuid());
    }
}

internal sealed class CreateOrderValidator : AbstractValidator<CreateOrder>
{
    public CreateOrderValidator()
    {
        RuleFor(c => c.Customer).NotEmpty().WithMessage("Customer is required.");
        RuleFor(c => c.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than zero.");
    }
}

internal sealed record DeleteOrder(Guid Id) : ICommand;

internal sealed class DeleteOrderHandler(HandlerLog log) : ICommandHandler<DeleteOrder>
{
    public ValueTask<Result> Handle(DeleteOrder command, CancellationToken ct = default)
    {
        log.Calls.Add(nameof(DeleteOrder));
        return new(Result.Success);
    }
}

internal sealed class DeleteOrderValidator : AbstractValidator<DeleteOrder>
{
    public DeleteOrderValidator() => RuleFor(c => c.Id).NotEmpty().WithMessage("Id is required.");
}

internal sealed record GetOrder(Guid Id) : IQuery<string>;

internal sealed class GetOrderHandler(HandlerLog log) : IQueryHandler<GetOrder, string>
{
    public ValueTask<Result<string>> Handle(GetOrder query, CancellationToken ct = default)
    {
        log.Calls.Add(nameof(GetOrder));
        return new($"order {query.Id}");
    }
}

internal sealed class GetOrderValidator : AbstractValidator<GetOrder>
{
    public GetOrderValidator() => RuleFor(q => q.Id).NotEmpty().WithMessage("Id is required.");
}

/// <summary>A query with no validator, which must pass straight through.</summary>
internal sealed record Ping : IQuery<string>;

internal sealed class PingHandler(HandlerLog log) : IQueryHandler<Ping, string>
{
    public ValueTask<Result<string>> Handle(Ping query, CancellationToken ct = default)
    {
        log.Calls.Add(nameof(Ping));
        return new("pong");
    }
}

/// <summary>A struct command with a value-type result, the case that needs generator-closed behaviors under AOT.</summary>
internal readonly record struct Add(int A, int B) : ICommand<int>;

internal sealed class AddHandler(HandlerLog log) : ICommandHandler<Add, int>
{
    public ValueTask<Result<int>> Handle(Add command, CancellationToken ct = default)
    {
        log.Calls.Add(nameof(Add));
        return new(command.A + command.B);
    }
}

internal sealed class AddValidator : AbstractValidator<Add>
{
    public AddValidator() => RuleFor(c => c.A).GreaterThanOrEqualTo(0).WithMessage("A must not be negative.");
}

internal sealed record CountTo(int N) : IStreamQuery<int>;

internal sealed class CountToHandler(HandlerLog log) : StreamQueryHandler<CountTo, int>
{
    protected override async IAsyncEnumerable<int> Stream(CountTo query, [EnumeratorCancellation] CancellationToken ct)
    {
        log.Calls.Add(nameof(CountTo));
        for (var i = 1; i <= query.N; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }
}

internal sealed class CountToValidator : AbstractValidator<CountTo>
{
    public CountToValidator() => RuleFor(q => q.N).InclusiveBetween(1, 10).WithMessage("N must be between 1 and 10.");
}
