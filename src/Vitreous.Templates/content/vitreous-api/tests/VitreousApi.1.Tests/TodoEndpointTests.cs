using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using VitreousApi._1.Features.Todos;

namespace VitreousApi._1.Tests;

/// <summary>
/// Runs the whole app in memory: routing, the generated dispatcher, the pipeline behaviors and
/// the handlers, called over HTTP the way a client calls them.
/// </summary>
public sealed class TodoEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_created_todo_can_be_read_back()
    {
        var created = await CreateTodo("Write the first feature");

        var todo = await _client.GetFromJsonAsync<TodoDto>($"/todos/{created.Id}", Ct);

        Assert.Equal(created, todo);
    }

    [Fact]
    public async Task A_todo_without_a_title_is_rejected()
    {
        var response = await _client.PostAsJsonAsync("/todos", new { title = "" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_todo_is_not_found()
    {
        var response = await _client.GetAsync($"/todos/{Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Completing_a_todo_marks_it_done()
    {
        var created = await CreateTodo("Ship it");
        await _client.GetFromJsonAsync<TodoDto>($"/todos/{created.Id}", Ct); // a cached read, when caching is on

        var response = await _client.PostAsync($"/todos/{created.Id}/complete", content: null, Ct);
        var todo     = await _client.GetFromJsonAsync<TodoDto>($"/todos/{created.Id}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(todo!.Done);
    }

    [Fact]
    public async Task Listing_returns_every_todo()
    {
        var created = await CreateTodo("Listed");

        var todos = await _client.GetFromJsonAsync<List<TodoDto>>("/todos", Ct);

        Assert.Contains(created, todos!);
    }

    private async Task<TodoDto> CreateTodo(string title)
    {
        var response = await _client.PostAsJsonAsync("/todos", new { title }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TodoDto>(Ct))!;
    }
}
