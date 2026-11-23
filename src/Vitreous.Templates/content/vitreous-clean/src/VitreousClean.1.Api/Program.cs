using Scalar.AspNetCore;
using Vitreous;
#if (UseDevUI)
using Vitreous.DevUI.Extensions;
#endif
using VitreousClean._1.Api.Orders;
using VitreousClean._1.Application;
using VitreousClean._1.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Generated at build time by Vitreous.SourceGeneration: registers the handlers of every layer and
// a dispatcher that calls them without reflection. Handlers are scoped, like a DbContext.
builder.Services.AddVitreousHandlers();

// Application first: its behaviors (logging, validation) run outside Infrastructure's (caching).
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
#if (UseDevUI)

// Live traces of every dispatch at /vitreous. Off outside Development unless you enable it.
builder.Services.AddVitreousDevUI();
#endif

#if (Net8)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
#else
builder.Services.AddOpenApi();
#endif

var app = builder.Build();

await app.Services.EnsureDatabaseCreatedAsync();

if (app.Environment.IsDevelopment())
{
#if (Net8)
    app.UseSwagger(static o => o.RouteTemplate = "openapi/{documentName}.json");
#else
    app.MapOpenApi();                 // /openapi/v1.json
#endif
    app.MapScalarApiReference();      // /scalar/v1
#if (UseDevUI)
    app.MapVitreousDevUI();           // /vitreous
#endif
}

app.MapOrders();

await app.RunAsync();

// Lets the tests start the app with WebApplicationFactory<Program>.
public partial class Program;
