using System.Globalization;
using Vitreous.Abstractions;
using VitreousClean._1.Application.Orders;
using VitreousClean._1.Cli.Common;
using VitreousClean._1.Domain.Orders;

namespace VitreousClean._1.Cli.Orders;

/// <summary>
/// Translates the command line to messages, and their Results to output and an exit code. A new
/// use case in Application adds a line here.
/// </summary>
public static class OrderCli
{
    private const string Help = """
        Usage:
          place <email> <product> <quantity> <price>   Place an order, in EUR
          list [placed|shipped]                        List the orders, oldest first
          show <id>                                    Show an order
          ship <id>                                    Ship an order
        """;

    public static Task<int> Run(string[] args, ISender sender, CliOutput output, CancellationToken ct) => args switch
    {
        ["place", var email, var product, var q, var p] when ParseInt(q) is { } quantity && ParseDecimal(p) is { } price
            => Place(new PlaceOrderCommand(email, "EUR", [new PlaceOrderLine(product, quantity, price)]), sender, output, ct),

        ["list"]                                              => List(null, sender, output, ct),
        ["list", "placed"]                                    => List(OrderStatus.Placed, sender, output, ct),
        ["list", "shipped"]                                   => List(OrderStatus.Shipped, sender, output, ct),
        ["show", var arg] when Guid.TryParse(arg, out var id) => Show(id, sender, output, ct),
        ["ship", var arg] when Guid.TryParse(arg, out var id) => Ship(id, sender, output, ct),
        [] or ["help" or "-h" or "--help"]                    => Print(output.Out, ExitCodes.Success),
        _                                                     => Print(output.Error, ExitCodes.Usage),
    };

    private static async Task<int> Place(PlaceOrderCommand command, ISender sender, CliOutput output, CancellationToken ct) =>
        (await sender.Send(command, ct)).ToExitCode(output, id => output.Out.WriteLine(id));

    private static async Task<int> List(OrderStatus? status, ISender sender, CliOutput output, CancellationToken ct)
    {
        var result = await sender.CreateStream(new ListOrdersQuery(status), ct);
        if (result.IsFailure)
            return result.Error.ToExitCode(output);

        await foreach (var order in result.Value.WithCancellation(ct))
            Write(order, output);

        return ExitCodes.Success;
    }

    private static async Task<int> Show(Guid id, ISender sender, CliOutput output, CancellationToken ct) =>
        (await sender.Send(new GetOrderQuery(id), ct)).ToExitCode(output, order =>
        {
            Write(order, output);
            foreach (var line in order.Lines)
                output.Out.WriteLine(FormattableString.Invariant($"  {line.Quantity} x {line.Product} at {line.UnitPrice:0.00}"));
        });

    private static async Task<int> Ship(Guid id, ISender sender, CliOutput output, CancellationToken ct) =>
        (await sender.Send(new ShipOrderCommand(id), ct)).ToExitCode(output);

    private static void Write(OrderDto order, CliOutput output) =>
        output.Out.WriteLine(FormattableString.Invariant(
            $"{order.Id}  {order.Status,-7}  {order.Total,10:0.00} {order.Currency}  {order.CustomerEmail}"));

    private static Task<int> Print(TextWriter writer, int exitCode)
    {
        writer.WriteLine(Help);
        return Task.FromResult(exitCode);
    }

    private static int? ParseInt(string arg) =>
        int.TryParse(arg, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static decimal? ParseDecimal(string arg) =>
        decimal.TryParse(arg, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : null;
}
