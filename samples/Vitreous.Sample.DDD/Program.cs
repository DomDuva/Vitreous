using Scalar.AspNetCore;
using Vitreous;
using Vitreous.Abstractions;
using Vitreous.DevUI.Extensions;
using Vitreous.Sample.DDD.Behaviors;
using Vitreous.Sample.DDD.Application.Orders;
using Vitreous.Sample.DDD.Application.Orders.Commands;
using Vitreous.Sample.DDD.Application.Orders.Notifications;
using Vitreous.Sample.DDD.Application.Orders.Queries;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────────────────────

// This single call is all that's needed for a multi-project solution.
//
// The Vitreous source generator runs two discovery passes at compile time:
//
//   Pass 1 — syntax nodes in this project (Vitreous.Sample.DDD)
//   Pass 2 — metadata scanning of every referenced assembly that directly
//             references Vitreous. This automatically picks up:
//               • Vitreous.Sample.DDD.Application  (commands, queries, notifications)
//               • Vitreous.Sample.DDD.Infrastructure (notification handlers)
//
// No assembly arguments, no manual registration — zero extra configuration.
builder.Services.AddVitreousHandlers();

// Register the logging pipeline behavior (wraps every command & query)
builder.Services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

// Sample infrastructure — shared singleton across all handlers
builder.Services.AddSingleton<OrderRepository>();

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
    app.MapOpenApi();                  // /openapi/v1.json
    app.MapScalarApiReference();       // /scalar/v1
    app.UseVitreousDevUI();            // /vitreous
}

// ── Endpoints ─────────────────────────────────────────────────────────────────

// POST /orders
app.MapPost("/orders", async (CreateOrderRequest req, ISender sender, IPublisher publisher, CancellationToken ct) =>
{
    var result = await sender.Send(
        new CreateOrderCommand(req.CustomerName, req.Product, req.Quantity), ct);

    if (result.IsFailure)
        return Results.BadRequest(new { error = result.Error.Code, message = result.Error.Message });

    // Publish notification — both Application (LogOrderCreatedHandler) and
    // Infrastructure (SendConfirmationEmailHandler) will handle it.
    await publisher.Publish(
        new OrderCreatedNotification(result.Value, req.CustomerName, req.Product), ct);

    return Results.Created($"/orders/{result.Value}", new { id = result.Value });
})
.WithName("CreateOrder")
.WithSummary("Create a new order")
.WithDescription("Creates an order for the given customer. Returns 400 if CustomerName is empty or Quantity ≤ 0.")
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
.WithDescription("Returns 404 with error code NOT_FOUND if the order does not exist.")
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
.WithDescription("Returns 404 with error code NOT_FOUND if the order does not exist.")
.WithTags("Orders");

app.Run();

// ── Request models ────────────────────────────────────────────────────────────

public sealed record CreateOrderRequest(string CustomerName, string Product, int Quantity);

// Allow integration tests to reference the implicit Program class
public partial class Program { }
