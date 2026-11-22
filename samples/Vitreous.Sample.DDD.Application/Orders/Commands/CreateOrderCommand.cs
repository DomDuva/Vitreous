using FluentValidation;
using Microsoft.Extensions.Logging;
using Vitreous.Abstractions;
using Vitreous.Sample.DDD.Application.Orders;

namespace Vitreous.Sample.DDD.Application.Orders.Commands;

// ── Command ──────────────────────────────────────────────────────────────────

public sealed record CreateOrderCommand(
    string CustomerName,
    string Product,
    int    Quantity) : ICommand<Guid>;

// ── Validator ────────────────────────────────────────────────────────────────

// Runs before the handler through AddVitreousValidation(). An invalid command never reaches
// the handler: Send returns Error.Validation with the failures grouped by property.
public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(c => c.CustomerName).NotEmpty().WithMessage("CustomerName is required.");
        RuleFor(c => c.Product).NotEmpty().WithMessage("Product is required.");
        RuleFor(c => c.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than zero.");
    }
}

// ── Handler ──────────────────────────────────────────────────────────────────

public sealed class CreateOrderCommandHandler
    : ICommandHandler<CreateOrderCommand, Guid>
{
    private readonly OrderRepository _repo;
    private readonly ILogger<CreateOrderCommandHandler> _logger;

    public CreateOrderCommandHandler(
        OrderRepository repo,
        ILogger<CreateOrderCommandHandler> logger)
    {
        _repo   = repo;
        _logger = logger;
    }

    public ValueTask<Result<Guid>> Handle(
        CreateOrderCommand command,
        CancellationToken ct = default)
    {
        var order = new Order(
            Guid.NewGuid(),
            command.CustomerName,
            command.Product,
            command.Quantity,
            DateTimeOffset.UtcNow);

        _repo.Add(order);

        _logger.LogInformation(
            "Order {OrderId} created for customer '{Customer}' — {Qty}x {Product}",
            order.Id, order.CustomerName, order.Quantity, order.Product);

        return ValueTask.FromResult(Result<Guid>.Ok(order.Id));
    }
}
