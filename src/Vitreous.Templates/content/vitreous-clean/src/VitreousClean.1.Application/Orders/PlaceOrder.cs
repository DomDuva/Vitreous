#if (UseValidation)
using FluentValidation;
#endif
using Vitreous;
using Vitreous.Abstractions;
using VitreousClean._1.Application.Abstractions;
using VitreousClean._1.Domain;
using VitreousClean._1.Domain.Orders;

namespace VitreousClean._1.Application.Orders;

// A use case is a message and its handler, in one file. Add the next one with:
//   dotnet new vitreous-command -n <Name>

public sealed record PlaceOrderCommand(
    string CustomerEmail,
    string Currency,
    IReadOnlyList<PlaceOrderLine> Lines) : ICommand<Guid>;

public sealed record PlaceOrderLine(string Product, int Quantity, decimal UnitPrice);
#if (UseValidation)

// Checks the input before the handler runs, and reports everything that is wrong with it at once.
// The business rules stay in the domain, which doesn't rely on a validator having run.
public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderValidator()
    {
        RuleFor(c => c.CustomerEmail).NotEmpty().EmailAddress();
        RuleFor(c => c.Currency).NotEmpty().Length(3);
        RuleFor(c => c.Lines).NotEmpty();
        RuleForEach(c => c.Lines).NotNull().ChildRules(line =>
        {
            line.RuleFor(l => l.Product).NotEmpty().MaximumLength(OrderLine.MaxProductLength);
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0);
        });
    }
}
#endif

public sealed class PlaceOrderHandler(IAppDbContext db, TimeProvider time) : ICommandHandler<PlaceOrderCommand, Guid>
{
    public async ValueTask<Result<Guid>> Handle(PlaceOrderCommand command, CancellationToken ct = default)
    {
        // The handler coordinates, the domain decides: every rule is checked while these are created.
        var lines = new List<OrderLine>();
        foreach (var line in command.Lines ?? [])
        {
            var price = Money.Create(line.UnitPrice, command.Currency);
            if (price.IsFailure)
                return price.Error;

            var created = OrderLine.Create(line.Product, line.Quantity, price.Value);
            if (created.IsFailure)
                return created.Error;

            lines.Add(created.Value);
        }

        var placed = Order.Place(command.CustomerEmail, lines, time.GetUtcNow());
        if (placed.IsFailure)
            return placed.Error;

        // Saving also publishes the OrderPlaced event that the order recorded.
        db.Orders.Add(placed.Value);
        await db.SaveChangesAsync(ct);

        return placed.Value.Id.Value;
    }
}
