using Vitreous.Abstractions;
using VitreousClean._1.Api.Common;
using VitreousClean._1.Application.Orders;
using VitreousClean._1.Domain.Orders;

namespace VitreousClean._1.Api.Orders;

/// <summary>
/// Translates HTTP to messages, and their Results to responses. A new use case in Application
/// adds an endpoint here.
/// </summary>
public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrders(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/orders").WithTags("Orders");

        orders.MapPost("/", static async (PlaceOrderCommand command, ISender sender, CancellationToken ct) =>
                (await sender.Send(command, ct)).ToHttpResult(static id => Results.Created($"/orders/{id}", new { id })))
            .WithName("PlaceOrder")
            .WithSummary("Place an order");

        orders.MapGet("/{id:guid}", static async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetOrderQuery(id), ct)).ToHttpResult(static order => Results.Ok(order)))
            .WithName("GetOrder")
            .WithSummary("Get an order by id");

        orders.MapGet("/", static async (OrderStatus? status, ISender sender, CancellationToken ct) =>
                (await sender.CreateStream(new ListOrdersQuery(status), ct)).ToHttpResult(static stream => Results.Ok(stream)))
            .WithName("ListOrders")
            .WithSummary("List the orders, oldest first");

        orders.MapPost("/{id:guid}/ship", static async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ShipOrderCommand(id), ct)).ToHttpResult(static () => Results.NoContent()))
            .WithName("ShipOrder")
            .WithSummary("Ship an order");

        return app;
    }
}
