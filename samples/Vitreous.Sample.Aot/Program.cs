using System.Text.Json;
using System.Text.Json.Serialization;
using Vitreous;
using Vitreous.Abstractions;
using Vitreous.Caching;
using Vitreous.Sample.Aot;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(static o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));

// Generated at build time: registers every handler and the reflection-free dispatcher.
builder.Services.AddVitreousHandlers(static o => o.NotificationPublishStrategy = NotificationPublishStrategy.Parallel);
builder.Services.AddVitreousBehavior(typeof(CountDispatchesBehavior<,>), ServiceLifetime.Singleton);
builder.Services.AddVitreousBehavior(typeof(RequireTitleBehavior));

// Cached values are serialized with the JSON source generator, not reflection.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddVitreousCaching(static o =>
    o.JsonSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = AppJsonContext.Default });

builder.Services.AddSingleton<TodoStore>();
builder.Services.AddSingleton<HandlerCalls>();

var app = builder.Build();

app.MapGet("/health", static () => "ok");

app.MapPost("/todos", static async (CreateTodoRequest request, ISender sender, CancellationToken ct) =>
    (await sender.Send(new CreateTodoCommand(request.Title), ct)).Match(
        static todo => Results.Created($"/todos/{todo.Id}", todo),
        static error => Results.Problem(error.Message, statusCode: StatusCodes.Status400BadRequest)));

app.MapGet("/todos/{id:guid}", static async (Guid id, ISender sender, CancellationToken ct) =>
    (await sender.Send(new GetTodoQuery(id), ct)).Match(
        static todo => Results.Ok(todo),
        static error => Results.NotFound()));

app.MapPost("/todos/{id:guid}/complete", static async (Guid id, ISender sender, CancellationToken ct) =>
    (await sender.Send(new CompleteTodoCommand(id), ct)).IsSuccess ? Results.NoContent() : Results.NotFound());

app.MapGet("/add", static async (int a, int b, ISender sender, CancellationToken ct) =>
    (await sender.Send(new AddNumbersCommand(a, b), ct)).Value);

app.MapGet("/todos", static async (ISender sender, CancellationToken ct) =>
    (await sender.CreateStream(new ListTodosQuery(), ct)).Value);

app.MapGet("/stats", static (HandlerCalls calls) => new Stats(calls.GetTodo, calls.Created, calls.Dispatches));

app.Run();

public sealed record CreateTodoRequest(string Title);

public sealed record Stats(int GetTodoCalls, int CreatedNotifications, int Dispatches);

[JsonSerializable(typeof(CreateTodoRequest))]
[JsonSerializable(typeof(TodoDto))]
[JsonSerializable(typeof(IAsyncEnumerable<TodoDto>))]
[JsonSerializable(typeof(Stats))]
[JsonSerializable(typeof(Microsoft.AspNetCore.Mvc.ProblemDetails))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
