---
title: Putting it all together
description: A Program.cs that registers the mediator, a behavior, validation, caching and the developer UI.
sidebar:
  order: 7
---

```csharp
var builder = WebApplication.CreateBuilder(args);

// Core mediator (source-generated dispatcher + handler discovery)
builder.Services.AddVitreousHandlers();

// Custom pipeline behavior
builder.Services.AddVitreousBehavior(typeof(LoggingBehavior<,>), ServiceLifetime.Singleton);

// Validation — before caching, so invalid queries never reach the cache
builder.Services.AddVitreousValidation();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// Caching — choose one:
builder.Services.AddDistributedMemoryCache();
builder.Services.AddVitreousCaching();
// or: builder.Services.AddVitreousRedisCaching(options =>
// {
//     options.ConnectionString = builder.Configuration.GetConnectionString("Redis");
// });

// Developer UI — development only
if (builder.Environment.IsDevelopment())
    builder.Services.AddVitreousDevUI();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.UseVitreousDevUI();

app.MapPost("/orders", async (CreateOrderCommand cmd, ISender sender) =>
    (await sender.Send(cmd)).Match(
        id  => Results.Created($"/orders/{id}", new { id }),
        err => Results.Problem(err.Message)));

app.MapGet("/orders/{id:guid}", async (Guid id, ISender sender) =>
    (await sender.Send(new GetOrderQuery(id))).Match(
        order => Results.Ok(order),
        err   => Results.Problem(err.Message)));

app.Run();
```

Each piece has its own page: [Pipeline behaviors](/Vitreous/concepts/pipeline-behaviors/), [Validation](/Vitreous/guides/validation/), [Caching](/Vitreous/guides/caching/) and [Developer UI](/Vitreous/guides/developer-ui/). To return the right status code for each error, see [Mapping errors to HTTP status codes](/Vitreous/guides/http-error-mapping/).
