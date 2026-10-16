using System.Text.Json;

namespace Vitreous.IntegrationTests.DevUI;

/// <summary>
/// Exercises the embedded developer UI endpoints against the sample app.
/// </summary>
public sealed class DevUiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public DevUiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory
            .WithWebHostBuilder(b => b.UseSetting("ASPNETCORE_ENVIRONMENT", "Development"))
            .CreateClient();
    }

    [Fact]
    public async Task Dashboard_IsServedAtPrefix()
    {
        var response = await _client.GetAsync("/vitreous");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await response.Content.ReadAsStringAsync()).Should().Contain("<html");
    }

    [Fact]
    public async Task Traces_IncludeCommandAndNotification_WithAllNotificationHandlerLogs()
    {
        await _client.PostAsJsonAsync("/orders", new { customerName = "Trace", product = "Widget", quantity = 1 });

        var traces = await _client.GetFromJsonAsync<JsonElement[]>("/vitreous/api/traces");
        traces.Should().NotBeNull();

        traces!.Should().Contain(t =>
            t.GetProperty("category").GetString() == "command" &&
            t.GetProperty("messageType").GetString() == "CreateOrderCommand");

        var notification = traces.Should().Contain(t =>
                t.GetProperty("category").GetString() == "notification" &&
                t.GetProperty("messageType").GetString() == "OrderCreatedNotification")
            .Which;

        // Both notification handlers in the sample log a line — both must have run.
        var logs = notification.GetProperty("logs").EnumerateArray()
            .Select(l => l.GetProperty("message").GetString())
            .ToList();

        logs.Should().Contain(m => m!.Contains("Confirmation email sent"));
        logs.Should().Contain(m => m!.Contains("Analytics dashboard updated"));
    }

    [Fact]
    public async Task Traces_StreamQuery_EmitsOpenedAndClosedWithItemCount()
    {
        var items = await _client.GetFromJsonAsync<JsonElement[]>("/orders/stream/tick?count=1");
        items.Should().HaveCount(1);

        var traces = await _client.GetFromJsonAsync<JsonElement[]>("/vitreous/api/traces");

        var stream = traces!.Where(t => t.GetProperty("messageType").GetString() == "TickStreamQuery").ToList();
        stream.Should().Contain(t => t.GetProperty("streamPhase").GetString() == "opened");
        stream.Should().Contain(t =>
            t.GetProperty("streamPhase").GetString() == "closed" &&
            t.GetProperty("itemCount").GetInt32() == 1);
    }
}
