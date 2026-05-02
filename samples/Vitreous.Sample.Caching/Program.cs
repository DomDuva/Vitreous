using Scalar.AspNetCore;
using Vitreous;
using Vitreous.Abstractions;
using Vitreous.Caching;
using Vitreous.DevUI.Extensions;
using Vitreous.Sample.Caching.Behaviors;
using Vitreous.Sample.Caching.Orders;
using Vitreous.Sample.Caching.Orders.Commands;
using Vitreous.Sample.Caching.Orders.Notifications;
using Vitreous.Sample.Caching.Orders.Queries;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────────────────────

builder.Services.AddVitreousHandlers();

// Register the logging pipeline behavior (wraps every command & query)
builder.Services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

// Sample infrastructure
builder.Services.AddSingleton<OrderRepository>();

// In-memory distributed cache — single node, no external dependencies required.
// Swap this for services.AddStackExchangeRedisCache(...) to use a real Redis instance,
// but prefer the Vitreous.Sample.Redis project for full tag-based invalidation support.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddVitreousCaching();

// OpenAPI document (used by Scalar)
builder.Services.AddOpenApi();

// DevUI — only in Development
if (builder.Environment.IsDevelopment())
    builder.Services.AddVitreousDevUI();

// ── Build ─────────────────────────────────────────────────────────────────────

var app = builder.Build();

// Scalar API reference — only in Development
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();               // /openapi/v1.json
    app.MapScalarApiReference();    // /scalar/v1
    app.UseVitreousDevUI();         // /vitreous
}

// ── Endpoints ─────────────────────────────────────────────────────────────────

// POST /orders
app.MapPost("/orders", async (CreateOrderRequest req, ISender sender, IPublisher publisher, CancellationToken ct) =>
{
    var result = await sender.Send(
        new CreateOrderCommand(req.CustomerName, req.Product, req.Quantity), ct);

    if (result.IsFailure)
        return Results.BadRequest(new { error = result.Error.Code, message = result.Error.Message });

    // Fire-and-forget notification (in real app this might be on a background channel)
    await publisher.Publish(
        new OrderCreatedNotification(result.Value, req.CustomerName, req.Product), ct);

    return Results.Created($"/orders/{result.Value}", new { id = result.Value });
})
.WithName("CreateOrder")
.WithSummary("Create a new order")
.WithDescription("Creates an order for the given customer. Returns 400 if CustomerName is empty or Quantity ≤ 0. Invalidates the orders list cache on success.")
.WithTags("Orders");

// GET /orders
app.MapGet("/orders", async (ISender sender, CancellationToken ct) =>
{
    var result = await sender.Send(new ListOrdersQuery(), ct);
    return result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.Problem(result.Error.Message);
})
.WithName("ListOrders")
.WithSummary("List all orders")
.WithDescription("Returns all orders. Response is cached for 2 minutes.")
.WithTags("Orders");

// GET /orders/{id}
app.MapGet("/orders/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
{
    var result = await sender.Send(new GetOrderQuery(id), ct);
    return result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.NotFound(new { error = result.Error.Code, message = result.Error.Message });
})
.WithName("GetOrder")
.WithSummary("Get an order by ID")
.WithDescription("Returns 404 with error code NOT_FOUND if the order does not exist. Response is cached for 5 minutes per order ID.")
.WithTags("Orders");

// DELETE /orders/{id}
app.MapDelete("/orders/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
{
    var result = await sender.Send(new DeleteOrderCommand(id), ct);
    return result.IsSuccess
        ? Results.NoContent()
        : Results.NotFound(new { error = result.Error.Code, message = result.Error.Message });
})
.WithName("DeleteOrder")
.WithSummary("Delete an order by ID")
.WithDescription("Returns 404 with error code NOT_FOUND if the order does not exist. Invalidates the order's individual cache entry and the orders list on success.")
.WithTags("Orders");

app.Run();

// ── Request models ────────────────────────────────────────────────────────────

public sealed record CreateOrderRequest(string CustomerName, string Product, int Quantity);

// Allow integration tests to reference the implicit Program class
public partial class Program { }
