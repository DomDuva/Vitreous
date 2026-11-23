using System.Net;
using System.Net.Http.Json;
using VitreousClean._1.Application.Orders;

namespace VitreousClean._1.Api.Tests;

/// <summary>
/// Calls the app over HTTP, the way a client does: routing, the generated dispatcher, the pipeline
/// behaviors, the handlers and the database.
/// </summary>
public sealed class OrderEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_placed_order_can_be_read_back()
    {
        var id = await PlaceOrder();

        var order = await GetOrder(id);

        Assert.Equal("Placed", order.Status);
        Assert.Equal(99.80m, order.Total);
        Assert.Equal([new OrderLineDto("Keyboard", 2, 49.90m)], order.Lines);
    }

    [Fact]
    public async Task An_order_without_lines_is_rejected()
    {
        var response = await _client.PostAsJsonAsync("/orders", new { customerEmail = "ada@example.com", currency = "EUR" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"Lines\"", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task An_order_that_breaks_a_business_rule_is_rejected()
    {
        // Passes the validator: that a price has at most two decimals is a rule of the domain.
        var response = await Place(new PlaceOrderLine("Keyboard", 1, 0.001m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"Amount\"", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task An_unknown_order_is_not_found()
    {
        var response = await _client.GetAsync($"/orders/{Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_order_ships_once()
    {
        var id = await PlaceOrder();
        await GetOrder(id); // a cached read, when caching is on

        var first  = await _client.PostAsync($"/orders/{id}/ship", content: null, Ct);
        var second = await _client.PostAsync($"/orders/{id}/ship", content: null, Ct);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("Shipped", (await GetOrder(id)).Status);
    }

    [Fact]
    public async Task Orders_are_listed_by_status()
    {
        var placed  = await PlaceOrder();
        var shipped = await PlaceOrder();
        await _client.PostAsync($"/orders/{shipped}/ship", content: null, Ct);

        var orders = await _client.GetFromJsonAsync<List<OrderDto>>("/orders?status=Placed", Ct);

        Assert.Contains(orders!, o => o.Id == placed);
        Assert.DoesNotContain(orders!, o => o.Id == shipped);
    }

    private async Task<OrderDto> GetOrder(Guid id) =>
        (await _client.GetFromJsonAsync<OrderDto>($"/orders/{id}", Ct))!;

    private async Task<Guid> PlaceOrder()
    {
        var response = await Place(new PlaceOrderLine("Keyboard", 2, 49.90m));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<Placed>(Ct))!.Id;
    }

    private Task<HttpResponseMessage> Place(params PlaceOrderLine[] lines) =>
        _client.PostAsJsonAsync("/orders", new PlaceOrderCommand("ada@example.com", "EUR", lines), Ct);

    private sealed record Placed(Guid Id);
}
