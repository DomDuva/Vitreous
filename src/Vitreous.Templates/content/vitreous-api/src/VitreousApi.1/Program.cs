#if (aot)
using System.Text.Json;
#endif
#if (UseValidation)
using FluentValidation;
#endif
#if (UseOpenApi)
using Scalar.AspNetCore;
#endif
using Vitreous;
#if (caching == "memory")
using Vitreous.Caching;
#elif (caching == "redis")
using Vitreous.Caching.Redis;
#endif
#if (UseDevUI)
using Vitreous.DevUI.Extensions;
#endif
#if (UseValidation)
using Vitreous.FluentValidation;
#endif
using VitreousApi._1.Common;
using VitreousApi._1.Features.Todos;

#if (aot)
var builder = WebApplication.CreateSlimBuilder(args);

// Native AOT has no reflection-based JSON: every type that crosses the wire is listed in
// AppJsonContext, and the System.Text.Json source generator writes its serializer.
builder.Services.ConfigureHttpJsonOptions(static o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
#else
var builder = WebApplication.CreateBuilder(args);
#endif

// Generated at build time by Vitreous.SourceGeneration: registers every handler in this project
// and a dispatcher that calls them without reflection. Handlers are scoped, like a DbContext.
builder.Services.AddVitreousHandlers();

// Pipeline behaviors wrap every command and query. They run in registration order, outermost first.
builder.Services.AddVitreousBehavior(typeof(LoggingBehavior<,>));
#if (UseValidation)

// Runs each message's validators before its handler. Invalid messages return Error.Validation.
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddVitreousValidation();
#endif
#if (caching == "memory")

// Caches the result of every ICacheableQuery. The in-memory IDistributedCache suits one instance;
// switch to Vitreous.Caching.Redis when you run several.
builder.Services.AddDistributedMemoryCache();
#if (aot)
builder.Services.AddVitreousCaching(static o =>
    o.JsonSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = AppJsonContext.Default });
#else
builder.Services.AddVitreousCaching();
#endif
#elif (caching == "redis")

// Caches the result of every ICacheableQuery in Redis (compose.yaml runs one locally). If Redis is
// down, queries still run: cache errors count as misses.
builder.Services.AddVitreousRedisCaching(o =>
{
    o.ConnectionString = builder.Configuration.GetConnectionString("Redis");
#if (aot)
    o.JsonSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = AppJsonContext.Default };
#endif
});
#endif
#if (UseDevUI)

// Live traces of every dispatch at /vitreous. Off outside Development unless you enable it.
builder.Services.AddVitreousDevUI();
#endif

builder.Services.AddSingleton<TodoStore>();
#if (UseOpenApi)

#if (Net8)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
#else
builder.Services.AddOpenApi();
#endif
#endif

var app = builder.Build();

#if (UseOpenApi || UseDevUI)
if (app.Environment.IsDevelopment())
{
#if (UseOpenApi)
#if (Net8)
    app.UseSwagger(static o => o.RouteTemplate = "openapi/{documentName}.json");
#else
    app.MapOpenApi();                 // /openapi/v1.json
#endif
    app.MapScalarApiReference();      // /scalar/v1
#endif
#if (UseDevUI)
    app.MapVitreousDevUI();           // /vitreous
#endif
}

#endif
app.MapTodos();

app.Run();

// Lets the tests start the app with WebApplicationFactory<Program>.
public partial class Program;
