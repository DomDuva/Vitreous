using Vitreous.Abstractions;
using VitreousClean._1.Application.Orders;

namespace VitreousClean._1.Worker.Orders;

/// <summary>Stands in for the app that places the orders.</summary>
public static class SampleOrders
{
    public static async Task Place(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        await sender.Send(new PlaceOrderCommand("ada@example.com", "EUR", [new PlaceOrderLine("Keyboard", 2, 49.90m)]));
        await sender.Send(new PlaceOrderCommand("grace@example.com", "USD", [new PlaceOrderLine("Monitor", 1, 219m), new PlaceOrderLine("Cable", 3, 5.50m)]));
    }
}
