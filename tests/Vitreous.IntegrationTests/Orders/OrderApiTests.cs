using System.Text.Json;

namespace Vitreous.IntegrationTests.Orders;

/// <summary>
/// End-to-end tests that spin up the full Vitreous.Sample application in-process
/// using WebApplicationFactory and exercise every HTTP endpoint.
/// </summary>
public sealed class OrderApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public OrderApiTests(WebApplicationFactory<Program> factory)
    {
        // Set ASPNETCORE_ENVIRONMENT to Development so DevUI middleware is registered
        _client = factory
            .WithWebHostBuilder(b => b.UseSetting("ASPNETCORE_ENVIRONMENT", "Development"))
            .CreateClient();
    }

    // ── POST /orders ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PostOrders_ValidRequest_Returns201WithId()
    {
        var response = await _client.PostAsJsonAsync("/orders", new
        {
            customerName = "Alice",
            product = "Widget",
            quantity = 3
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("id", out var idProp).Should().BeTrue();
        Guid.TryParse(idProp.GetString(), out _).Should().BeTrue();
    }

    [Fact]
    public async Task PostOrders_ValidRequest_LocationHeaderPointsToNewOrder()
    {
        var response = await _client.PostAsJsonAsync("/orders", new
        {
            customerName = "Bob",
            product = "Gizmo",
            quantity = 1
        });

        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().StartWith("/orders/");
    }

    [Fact]
    public async Task PostOrders_EmptyCustomerName_Returns400WithValidationCode()
    {
        var response = await _client.PostAsJsonAsync("/orders", new
        {
            customerName = "",
            product = "Widget",
            quantity = 1
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("VALIDATION");
    }

    [Fact]
    public async Task PostOrders_ZeroQuantity_Returns400WithValidationCode()
    {
        var response = await _client.PostAsJsonAsync("/orders", new
        {
            customerName = "Alice",
            product = "Widget",
            quantity = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("VALIDATION");
    }

    [Fact]
    public async Task PostOrders_NegativeQuantity_Returns400WithValidationCode()
    {
        var response = await _client.PostAsJsonAsync("/orders", new
        {
            customerName = "Alice",
            product = "Widget",
            quantity = -5
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── GET /orders ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOrders_NoOrders_Returns200WithEmptyArray()
    {
        // Use a fresh factory instance so the in-memory repo is empty
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/orders");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var orders = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        orders.Should().NotBeNull();
        orders!.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOrders_AfterCreation_ReturnsCreatedOrders()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        // Create two orders
        await client.PostAsJsonAsync("/orders", new { customerName = "Alice", product = "A", quantity = 1 });
        await client.PostAsJsonAsync("/orders", new { customerName = "Bob", product = "B", quantity = 2 });

        var response = await client.GetAsync("/orders");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var orders = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        orders!.Should().HaveCount(2);
    }

    // ── GET /orders/{id} ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetOrderById_ExistingOrder_Returns200WithOrderDetails()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        // Create an order and capture the returned ID
        var createResponse = await client.PostAsJsonAsync("/orders", new
        {
            customerName = "Charlie",
            product = "Sprocket",
            quantity = 10
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString()!;

        // Fetch by ID
        var getResponse = await client.GetAsync($"/orders/{id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var order = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        order.GetProperty("customerName").GetString().Should().Be("Charlie");
        order.GetProperty("product").GetString().Should().Be("Sprocket");
        order.GetProperty("quantity").GetInt32().Should().Be(10);
    }

    [Fact]
    public async Task GetOrderById_NonExistentId_Returns404WithNotFoundCode()
    {
        var response = await _client.GetAsync($"/orders/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task GetOrderById_MalformedGuid_Returns400OrNotFound()
    {
        var response = await _client.GetAsync("/orders/not-a-guid");

        // ASP.NET Core returns 400 when the route constraint rejects the value
        ((int)response.StatusCode).Should().BeOneOf(400, 404);
    }

    // ── DELETE /orders/{id} ───────────────────────────────────────────────────

    [Fact]
    public async Task DeleteOrder_ExistingOrder_Returns204()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/orders", new
        {
            customerName = "Dave",
            product = "Cog",
            quantity = 5
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString()!;

        var deleteResponse = await client.DeleteAsync($"/orders/{id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteOrder_ExistingOrder_OrderNoLongerReturnedByList()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/orders", new
        {
            customerName = "Eve",
            product = "Bolt",
            quantity = 7
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString()!;

        await client.DeleteAsync($"/orders/{id}");

        var listResponse = await client.GetAsync("/orders");
        var orders = await listResponse.Content.ReadFromJsonAsync<JsonElement[]>();
        orders!.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteOrder_NonExistentId_Returns404WithNotFoundCode()
    {
        var response = await _client.DeleteAsync($"/orders/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("NOT_FOUND");
    }

    // ── DevUI ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DevUI_IndexPage_Returns200WithHtml()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseSetting("ASPNETCORE_ENVIRONMENT", "Development"));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/vitreous");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
    }

    [Fact]
    public async Task DevUI_ApiTraces_Returns200WithJsonArray()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseSetting("ASPNETCORE_ENVIRONMENT", "Development"));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/vitreous/api/traces");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }
}
