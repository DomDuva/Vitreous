using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Vitreous.Caching;
using Vitreous.Caching.Serialization;

namespace Vitreous.CachingTests;

public sealed class SerializerTests
{
    public sealed record OrderDto(Guid Id, string Customer, int Quantity);

    private readonly SystemTextJsonVitreousCacheSerializer _serializer = new();

    [Fact]
    public void RoundTrip_ReturnsEqualValue()
    {
        var order = new OrderDto(Guid.NewGuid(), "Ada", 3);

        var bytes = _serializer.Serialize(order);

        _serializer.TryDeserialize<OrderDto>(bytes, out var back).Should().BeTrue();
        back.Should().Be(order);
    }

    [Fact]
    public void RoundTrip_GenericCollection()
    {
        IReadOnlyList<OrderDto> orders = [new(Guid.NewGuid(), "Ada", 1), new(Guid.NewGuid(), "Bob", 2)];

        var bytes = _serializer.Serialize(orders);

        _serializer.TryDeserialize<IReadOnlyList<OrderDto>>(bytes, out var back).Should().BeTrue();
        back.Should().Equal(orders);
    }

    [Fact]
    public void Envelope_RecordsTheResultType()
    {
        using var json = System.Text.Json.JsonDocument.Parse(_serializer.Serialize(new OrderDto(Guid.Empty, "Ada", 1)));

        json.RootElement.GetProperty("t").GetString().Should().Be("Vitreous.CachingTests.SerializerTests+OrderDto");
        json.RootElement.GetProperty("v").GetProperty("customer").GetString().Should().Be("Ada");
    }

    [Fact]
    public void DataWrittenForAnotherType_IsAMiss()
    {
        var bytes = _serializer.Serialize("not an order");

        _serializer.TryDeserialize<OrderDto>(bytes, out var value).Should().BeFalse();
        value.Should().BeNull();
    }

    [Theory]
    [InlineData("\"plain string\"")]
    [InlineData("""{"id":"00000000-0000-0000-0000-000000000000","customer":"Ada","quantity":1}""")]
    [InlineData("[1,2,3]")]
    public void DataInTheOldUnwrappedFormat_IsAMiss(string json)
    {
        _serializer.TryDeserialize<OrderDto>(Encoding.UTF8.GetBytes(json), out _).Should().BeFalse();
    }

    [Fact]
    public async Task DistributedBackend_KeySharedByTwoResultTypes_DoesNotReturnTheOtherType()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddDistributedMemoryCache();
        services.AddVitreousCaching();
        using var sp = services.BuildServiceProvider();
        var cache = sp.GetRequiredService<IVitreousCache>();

        await cache.SetAsync("shared", "a string", [], new DistributedCacheEntryOptions());

        (await cache.GetAsync<OrderDto>("shared")).Hit.Should().BeFalse();
        (await cache.GetAsync<string>("shared")).Should().Be((true, "a string"));
    }

    [Fact]
    public async Task DistributedBackend_UsesARegisteredCustomSerializer()
    {
        var custom = new CountingSerializer();
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddDistributedMemoryCache();
        services.AddSingleton<IVitreousCacheSerializer>(custom);
        services.AddVitreousCaching();
        using var sp = services.BuildServiceProvider();
        var cache = sp.GetRequiredService<IVitreousCache>();

        await cache.SetAsync("k", 42, [], new DistributedCacheEntryOptions());
        await cache.GetAsync<int>("k");

        custom.Serialized.Should().Be(1);
        custom.Deserialized.Should().Be(1);
    }

    private sealed class CountingSerializer : IVitreousCacheSerializer
    {
        private readonly SystemTextJsonVitreousCacheSerializer _inner = new();

        public int Serialized { get; private set; }
        public int Deserialized { get; private set; }

        public byte[] Serialize<T>(T value)
        {
            Serialized++;
            return _inner.Serialize(value);
        }

        public bool TryDeserialize<T>(ReadOnlySpan<byte> data, out T? value)
        {
            Deserialized++;
            return _inner.TryDeserialize(data, out value);
        }
    }
}
