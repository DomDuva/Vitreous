using FluentValidation;
using Microsoft.Extensions.Logging;
using Vitreous.Abstractions;
using Vitreous.Sample.DDD.Domain.Orders;

namespace Vitreous.Sample.DDD.Application.Orders.Commands;

// ── Command ──────────────────────────────────────────────────────────────────

public sealed record CreateOrderCommand(
    string CustomerName,
    string Product,
    int    Quantity) : ICommand<Guid>;

// ── Validator ────────────────────────────────────────────────────────────────

// Runs before the handler through AddVitreousValidation(). An invalid command never reaches
// the handler: Send returns Error.Validation with the failures grouped by property.
// It checks the input only; business rules such as Order.MaxQuantity stay in the aggregate.
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
    private readonly IOrderRepository _repo;
    private readonly ILogger<CreateOrderCommandHandler> _logger;

    public CreateOrderCommandHandler(
        IOrderRepository repo,
        ILogger<CreateOrderCommandHandler> logger)
    {
        _repo   = repo;
        _logger = logger;
    }

    public async ValueTask<Result<Guid>> Handle(
        CreateOrderCommand command,
        CancellationToken ct = default)
    {
        // The aggregate enforces its invariants and records the OrderCreated domain event.
        var created = Order.Create(
            command.CustomerName,
            command.Product,
            command.Quantity,
            DateTimeOffset.UtcNow);

        if (created.IsFailure)
            return created.Error;

        var order = created.Value;

        // Saving also publishes the recorded domain events (see InMemoryOrderRepository).
        await _repo.AddAsync(order, ct);

        _logger.LogInformation(
            "Order {OrderId} created for customer '{Customer}' — {Qty}x {Product}",
            order.Id, order.CustomerName, order.Quantity, order.Product);

        return order.Id;
    }
}
