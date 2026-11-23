using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using VitreousApi._1.Features.Todos;

namespace VitreousApi._1.Common;

/// <summary>
/// Every type the API reads or writes as JSON. Native AOT can't serialize with reflection, so the
/// System.Text.Json source generator writes the code for these types at compile time. Add a type
/// here when an endpoint starts to accept or return it.
/// </summary>
[JsonSerializable(typeof(CreateTodoCommand))]
[JsonSerializable(typeof(TodoDto))]
[JsonSerializable(typeof(IAsyncEnumerable<TodoDto>))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(HttpValidationProblemDetails))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
