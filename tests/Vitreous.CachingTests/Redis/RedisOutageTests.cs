using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Vitreous.Caching.Redis;

namespace Vitreous.CachingTests.Redis;

/// <summary>
/// The cache is a performance layer: Redis being unreachable must never make dispatch fail,
/// and messages that don't use the cache must never touch Redis at all.
/// </summary>
public sealed class RedisOutageTests
{
    public sealed record PlainCommand(int Value) : ICommand<int>;

    public sealed class PlainCommandHandler : ICommandHandler<PlainCommand, int>
    {
        public ValueTask<Result<int>> Handle(PlainCommand command, CancellationToken ct = default) => new(command.Value);
    }

    public sealed record CachedQuery(int Id) : ICacheableQuery<string>
    {
        public string CacheKey              => $"outage:{Id}";
        public TimeSpan? AbsoluteExpiration => TimeSpan.FromMinutes(1);
        public TimeSpan? SlidingExpiration  => null;
    }

    public sealed class CachedQueryHandler : IQueryHandler<CachedQuery, string>
    {
        public ValueTask<Result<string>> Handle(CachedQuery query, CancellationToken ct = default) => new($"fresh:{query.Id}");
    }

    public sealed record InvalidatingCommand : ICommand, IInvalidatesCache
    {
        public IReadOnlyList<string> CacheKeys       => ["outage:1"];
        public IReadOnlyList<string> InvalidatesTags => ["outage"];
    }

    public sealed class InvalidatingCommandHandler : ICommandHandler<InvalidatingCommand>
    {
        public ValueTask<Result> Handle(InvalidatingCommand command, CancellationToken ct = default) => new(Result.Success);
    }

    private static ServiceProvider Build(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddVitreousHandlers(o => o.DefaultHandlerLifetime = Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton,
            typeof(RedisOutageTests).Assembly);
        configure(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task MessagesThatDoNotUseTheCache_NeverResolveTheConnection()
    {
        var resolved = 0;
        using var sp = Build(services =>
        {
            services.AddSingleton<IConnectionMultiplexer>(_ =>
            {
                Interlocked.Increment(ref resolved);
                throw new InvalidOperationException("Redis must not be touched");
            });
            services.AddVitreousRedisCaching();
        });

        var result = await sp.GetRequiredService<ISender>().Send(new PlainCommand(7));

        result.Value.Should().Be(7);
        resolved.Should().Be(0);
    }

    [Fact]
    public async Task RedisUnreachable_DispatchStillSucceeds_AndFailsFast()
    {
        using var sp = Build(services => services.AddVitreousRedisCaching(o =>
            o.ConnectionString = "127.0.0.1:1,connectTimeout=200,connectRetry=0"));
        var sender = sp.GetRequiredService<ISender>();

        (await sender.Send(new PlainCommand(1))).Value.Should().Be(1);

        // First cacheable call pays the (short) connect attempt; after that, fail-fast.
        (await sender.Send(new CachedQuery(1))).Value.Should().Be("fresh:1");

        var sw = Stopwatch.StartNew();
        (await sender.Send(new CachedQuery(2))).Value.Should().Be("fresh:2");
        (await sender.Send(new InvalidatingCommand())).IsSuccess.Should().BeTrue();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
            "commands must fail fast while Redis is down instead of waiting for a timeout");
    }

    [Fact]
    public async Task NoConnectionConfigured_RegistrationSucceeds_AndDispatchFallsBackToHandler()
    {
        // Registering the multiplexer is allowed after AddVitreousRedisCaching(), so a missing
        // one only surfaces (as a logged cache failure) when the cache is first used.
        using var sp = Build(services => services.AddVitreousRedisCaching());

        (await sp.GetRequiredService<ISender>().Send(new CachedQuery(3))).Value.Should().Be("fresh:3");
    }

    [Fact]
    public void MultiplexerRegisteredAfterAddVitreousRedisCaching_IsUsed()
    {
        var multiplexer = MultiplexerProbe.Create();
        using var sp = Build(services =>
        {
            services.AddVitreousRedisCaching();
            services.AddSingleton(multiplexer.Instance);
        });

        _ = sp.GetRequiredService<IVitreousCache>().GetAsync<string>("k");

        multiplexer.Calls.Should().BeGreaterThan(0);
    }

    /// <summary>A proxy multiplexer that records GetDatabase calls and returns a null database.</summary>
    public class MultiplexerProbe : DispatchProxy
    {
        public int Calls;
        public IConnectionMultiplexer Instance { get; private set; } = null!;

        public static MultiplexerProbe Create()
        {
            var proxy = DispatchProxy.Create<IConnectionMultiplexer, MultiplexerProbe>();
            var probe = (MultiplexerProbe)(object)proxy;
            probe.Instance = proxy;
            return probe;
        }

        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
        {
            Interlocked.Increment(ref Calls);
            throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "probe");
        }
    }
}
