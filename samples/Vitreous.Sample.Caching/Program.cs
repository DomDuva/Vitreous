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
using Vitreous.Sample.Caching.Showcase;

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

// ── Failure Showcase Endpoints ────────────────────────────────────────────────
// These commands and queries always return a Result failure so that you can
// observe every error type in the Vitreous DevUI and connected API clients.
// No input is required — just invoke the endpoint and inspect the result.

// POST /showcase/fail/not-found
app.MapPost("/showcase/fail/not-found", async (ISender sender, CancellationToken ct) =>
{
    var result = await sender.Send(new FailNotFoundCommand(), ct);
    return Results.NotFound(new { error = result.Error.Code, message = result.Error.Message });
})
.WithName("ShowcaseFailNotFoundCommand")
.WithSummary("Always fails — NOT_FOUND (command)")
.WithDescription("Sends a FailNotFoundCommand that always returns Error.NotFound. Demonstrates how a NOT_FOUND result failure appears in the DevUI.")
.WithTags("Failure Showcase");

// GET /showcase/fail/not-found
app.MapGet("/showcase/fail/not-found", async (ISender sender, CancellationToken ct) =>
{
    var result = await sender.Send(new FailNotFoundQuery(), ct);
    return Results.NotFound(new { error = result.Error.Code, message = result.Error.Message });
})
.WithName("ShowcaseFailNotFoundQuery")
.WithSummary("Always fails — NOT_FOUND (query)")
.WithDescription("Sends a FailNotFoundQuery that always returns Error.NotFound. Demonstrates how a NOT_FOUND result failure appears in the DevUI.")
.WithTags("Failure Showcase");

// POST /showcase/fail/validation
app.MapPost("/showcase/fail/validation", async (ISender sender, CancellationToken ct) =>
{
    var result = await sender.Send(new FailValidationCommand(), ct);
    return Results.BadRequest(new { error = result.Error.Code, message = result.Error.Message, extensions = result.Error.Extensions });
})
.WithName("ShowcaseFailValidationCommand")
.WithSummary("Always fails — VALIDATION (command)")
.WithDescription("Sends a FailValidationCommand that always returns Error.Validation with per-field failures. Demonstrates how a VALIDATION result failure appears in the DevUI.")
.WithTags("Failure Showcase");

// GET /showcase/fail/validation
app.MapGet("/showcase/fail/validation", async (ISender sender, CancellationToken ct) =>
{
    var result = await sender.Send(new FailValidationQuery(), ct);
    return Results.BadRequest(new { error = result.Error.Code, message = result.Error.Message, extensions = result.Error.Extensions });
})
.WithName("ShowcaseFailValidationQuery")
.WithSummary("Always fails — VALIDATION (query)")
.WithDescription("Sends a FailValidationQuery that always returns Error.Validation with per-field failures. Demonstrates how a VALIDATION result failure appears in the DevUI.")
.WithTags("Failure Showcase");

// POST /showcase/fail/unauthorized
app.MapPost("/showcase/fail/unauthorized", async (ISender sender, CancellationToken ct) =>
{
    var result = await sender.Send(new FailUnauthorizedCommand(), ct);
    return Results.Problem(detail: result.Error.Message, statusCode: 401, title: result.Error.Code);
})
.WithName("ShowcaseFailUnauthorizedCommand")
.WithSummary("Always fails — UNAUTHORIZED (command)")
.WithDescription("Sends a FailUnauthorizedCommand that always returns Error.Unauthorized. Demonstrates how an UNAUTHORIZED result failure appears in the DevUI.")
.WithTags("Failure Showcase");

// GET /showcase/fail/unauthorized
app.MapGet("/showcase/fail/unauthorized", async (ISender sender, CancellationToken ct) =>
{
    var result = await sender.Send(new FailUnauthorizedQuery(), ct);
    return Results.Problem(detail: result.Error.Message, statusCode: 401, title: result.Error.Code);
})
.WithName("ShowcaseFailUnauthorizedQuery")
.WithSummary("Always fails — UNAUTHORIZED (query)")
.WithDescription("Sends a FailUnauthorizedQuery that always returns Error.Unauthorized. Demonstrates how an UNAUTHORIZED result failure appears in the DevUI.")
.WithTags("Failure Showcase");

// POST /showcase/fail/conflict
app.MapPost("/showcase/fail/conflict", async (ISender sender, CancellationToken ct) =>
{
    var result = await sender.Send(new FailConflictCommand(), ct);
    return Results.Problem(detail: result.Error.Message, statusCode: 409, title: result.Error.Code);
})
.WithName("ShowcaseFailConflictCommand")
.WithSummary("Always fails — CONFLICT (command)")
.WithDescription("Sends a FailConflictCommand that always returns Error.Conflict. Demonstrates how a CONFLICT result failure appears in the DevUI.")
.WithTags("Failure Showcase");

// GET /showcase/fail/conflict
app.MapGet("/showcase/fail/conflict", async (ISender sender, CancellationToken ct) =>
{
    var result = await sender.Send(new FailConflictQuery(), ct);
    return Results.Problem(detail: result.Error.Message, statusCode: 409, title: result.Error.Code);
})
.WithName("ShowcaseFailConflictQuery")
.WithSummary("Always fails — CONFLICT (query)")
.WithDescription("Sends a FailConflictQuery that always returns Error.Conflict. Demonstrates how a CONFLICT result failure appears in the DevUI.")
.WithTags("Failure Showcase");

// POST /showcase/fail/internal
app.MapPost("/showcase/fail/internal", async (ISender sender, CancellationToken ct) =>
{
    var result = await sender.Send(new FailInternalCommand(), ct);
    return Results.Problem(detail: result.Error.Message, statusCode: 500, title: result.Error.Code);
})
.WithName("ShowcaseFailInternalCommand")
.WithSummary("Always fails — INTERNAL (command)")
.WithDescription("Sends a FailInternalCommand that always returns Error.Internal wrapping a sample exception. Demonstrates how an INTERNAL result failure appears in the DevUI.")
.WithTags("Failure Showcase");

// GET /showcase/fail/internal
app.MapGet("/showcase/fail/internal", async (ISender sender, CancellationToken ct) =>
{
    var result = await sender.Send(new FailInternalQuery(), ct);
    return Results.Problem(detail: result.Error.Message, statusCode: 500, title: result.Error.Code);
})
.WithName("ShowcaseFailInternalQuery")
.WithSummary("Always fails — INTERNAL (query)")
.WithDescription("Sends a FailInternalQuery that always returns Error.Internal wrapping a sample exception. Demonstrates how an INTERNAL result failure appears in the DevUI.")
.WithTags("Failure Showcase");

// POST /showcase/fail/custom
app.MapPost("/showcase/fail/custom", async (ISender sender, CancellationToken ct) =>
{
    var result = await sender.Send(new FailCustomCommand(), ct);
    return Results.BadRequest(new { error = result.Error.Code, message = result.Error.Message, extensions = result.Error.Extensions });
})
.WithName("ShowcaseFailCustomCommand")
.WithSummary("Always fails — DEMO (custom, command)")
.WithDescription("Sends a FailCustomCommand that always returns Error.Custom with code DEMO. Demonstrates how a custom result failure appears in the DevUI.")
.WithTags("Failure Showcase");

// GET /showcase/fail/custom
app.MapGet("/showcase/fail/custom", async (ISender sender, CancellationToken ct) =>
{
    var result = await sender.Send(new FailCustomQuery(), ct);
    return Results.BadRequest(new { error = result.Error.Code, message = result.Error.Message, extensions = result.Error.Extensions });
})
.WithName("ShowcaseFailCustomQuery")
.WithSummary("Always fails — DEMO (custom, query)")
.WithDescription("Sends a FailCustomQuery that always returns Error.Custom with code DEMO. Demonstrates how a custom result failure appears in the DevUI.")
.WithTags("Failure Showcase");

app.Run();

// ── Request models ────────────────────────────────────────────────────────────

public sealed record CreateOrderRequest(string CustomerName, string Product, int Quantity);

// Allow integration tests to reference the implicit Program class
public partial class Program { }
