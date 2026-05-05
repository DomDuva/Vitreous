using BenchmarkDotNet.Attributes;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Vitreous;
using MH = Vitreous.Benchmarks.MediatRHandlers;
using VH = Vitreous.Benchmarks.VitreousHandlers;
using VSender    = Vitreous.Abstractions.ISender;
using VPublisher = Vitreous.Abstractions.IPublisher;

namespace Vitreous.Benchmarks.Benchmarks;

/// <summary>
/// Identifies which mediator library (and handler lifetime) is under test.
/// </summary>
/// <remarks>
/// MediatR's out-of-the-box default is <see cref="ServiceLifetime.Transient"/>: a new
/// handler object is created on every <c>Send()</c> call. Vitreous registers handlers
/// as <see cref="ServiceLifetime.Singleton"/> via <c>TryAddSingleton</c> in the
/// source-generated <c>AddVitreousHandlers()</c>.
///
/// Including <see cref="MediatRSingleton"/> lets readers separate two distinct costs:
///   1. Handler instantiation overhead  (Transient only)
///   2. Dispatch / pipeline overhead    (visible when all three rows are compared)
/// </remarks>
public enum LibraryKind
{
    /// <summary>MediatR with its default Transient handler lifetime.</summary>
    MediatR,

    /// <summary>MediatR with Singleton handler lifetime — isolates dispatch cost from instantiation cost.</summary>
    MediatRSingleton,

    /// <summary>Vitreous with its default Singleton handler lifetime and source-generated dispatcher.</summary>
    Vitreous,
}

/// <summary>
/// Hot-path dispatch benchmarks.
///
/// [Params] produces a single consolidated table per scenario:
///
///   | Method                          | Mediator        | Mean (ns) | Allocated (B) |
///   | Send (return result)            | MediatR         |    ~92 ns |         272 B |
///   | Send (return result)            | MediatR Singletn|    ~60 ns |          32 B |
///   | Send (return result)            | Vitreous        |    ~40 ns |           0 B |
///   | ...                             |                 |           |               |
///
/// Notes on allocation differences:
///   • MediatR (Transient) allocates a new handler instance per call on top of
///     dispatch overhead — this is its documented default behaviour.
///   • MediatR (Singleton) removes that per-call instantiation cost; remaining
///     allocations come from Task&lt;T&gt; (heap object) and pipeline delegates.
///   • Vitreous returns ValueTask&lt;Result&lt;T&gt;&gt;; both are readonly structs so no
///     heap object is created for the return value itself.
///
/// Messages are pre-allocated as static fields — not counted in Allocated.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
public class DispatchBenchmarks
{
    [Params(LibraryKind.MediatR, LibraryKind.MediatRSingleton, LibraryKind.Vitreous)]
    public LibraryKind Mediator { get; set; }

    // ── MediatR — Transient (default) ─────────────────────────────────────────
    private ISender    _mediatRSender         = null!;
    private ISender    _mediatRBehaviorSender = null!;
    private IPublisher _mediatRPublisher      = null!;

    // ── MediatR — Singleton (apples-to-apples lifetime comparison) ────────────
    private ISender    _mediatRSingletonSender         = null!;
    private ISender    _mediatRSingletonBehaviorSender = null!;
    private IPublisher _mediatRSingletonPublisher      = null!;

    // ── Vitreous — Singleton (source-generated dispatcher) ────────────────────
    private VSender    _vitreous         = null!;
    private VSender    _vitreousBehavior = null!;
    private VPublisher _vitreousPublisher = null!;

    // Pre-allocated messages — excluded from the Allocated column
    private static readonly MH.BenchQuery        MQ  = new();
    private static readonly MH.BenchVoidCommand  MVC = new();
    private static readonly MH.BenchNotification MN  = new();
    private static readonly VH.BenchQuery        VQ  = new();
    private static readonly VH.BenchVoidCommand  VVC = new();
    private static readonly VH.BenchNotification VN  = new();

    [GlobalSetup]
    public void Setup()
    {
        // ── MediatR — Transient (default) ─────────────────────────────────────
        // Handlers are registered as Transient: each Send() allocates a new handler.
        var m1 = new ServiceCollection();
        m1.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<DispatchBenchmarks>());
        var mp1 = m1.BuildServiceProvider();
        _mediatRSender    = mp1.GetRequiredService<ISender>();
        _mediatRPublisher = mp1.GetRequiredService<IPublisher>();

        var m2 = new ServiceCollection();
        m2.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<DispatchBenchmarks>());
        m2.AddSingleton(typeof(IPipelineBehavior<,>), typeof(MH.BenchPassThroughBehavior<,>));
        _mediatRBehaviorSender = m2.BuildServiceProvider().GetRequiredService<ISender>();

        // ── MediatR — Singleton ────────────────────────────────────────────────
        // Same as above but with ServiceLifetime.Singleton: handler is reused.
        // Isolates pure dispatch cost from handler instantiation cost.
        var ms1 = new ServiceCollection();
        ms1.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<DispatchBenchmarks>();
            cfg.Lifetime = ServiceLifetime.Singleton;
        });
        var msp1 = ms1.BuildServiceProvider();
        _mediatRSingletonSender    = msp1.GetRequiredService<ISender>();
        _mediatRSingletonPublisher = msp1.GetRequiredService<IPublisher>();

        var ms2 = new ServiceCollection();
        ms2.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<DispatchBenchmarks>();
            cfg.Lifetime = ServiceLifetime.Singleton;
        });
        ms2.AddSingleton(typeof(IPipelineBehavior<,>), typeof(MH.BenchPassThroughBehavior<,>));
        _mediatRSingletonBehaviorSender = ms2.BuildServiceProvider().GetRequiredService<ISender>();

        // ── Vitreous — Singleton (source-generated) ───────────────────────────
        var v1 = new ServiceCollection();
        v1.AddVitreousHandlers();
        var vp1 = v1.BuildServiceProvider();
        _vitreous          = vp1.GetRequiredService<VSender>();
        _vitreousPublisher = vp1.GetRequiredService<VPublisher>();

        var v2 = new ServiceCollection();
        v2.AddVitreousHandlers();
        v2.AddSingleton(
            typeof(Vitreous.Abstractions.IPipelineBehavior<,>),
            typeof(VH.BenchPassThroughBehavior<,>));
        _vitreousBehavior = v2.BuildServiceProvider().GetRequiredService<VSender>();
    }

    // ── Send (return result) ──────────────────────────────────────────────────

    [Benchmark(Description = "Send (return result)")]
    public async Task Send_Result()
    {
        switch (Mediator)
        {
            case LibraryKind.MediatR:         _ = await _mediatRSender.Send(MQ);         break;
            case LibraryKind.MediatRSingleton: _ = await _mediatRSingletonSender.Send(MQ); break;
            default:                           _ = await _vitreous.Send(VQ);              break;
        }
    }

    // ── Send (return result, with pipeline behavior) ───────────────────────────

    [Benchmark(Description = "Send (return result, with behavior)")]
    public async Task Send_Result_WithBehavior()
    {
        switch (Mediator)
        {
            case LibraryKind.MediatR:         _ = await _mediatRBehaviorSender.Send(MQ);         break;
            case LibraryKind.MediatRSingleton: _ = await _mediatRSingletonBehaviorSender.Send(MQ); break;
            default:                           _ = await _vitreousBehavior.Send(VQ);              break;
        }
    }

    // ── Send (return void) ────────────────────────────────────────────────────

    [Benchmark(Description = "Send (return void)")]
    public async Task Send_Void()
    {
        switch (Mediator)
        {
            case LibraryKind.MediatR:         await _mediatRSender.Send(MVC);         break;
            case LibraryKind.MediatRSingleton: await _mediatRSingletonSender.Send(MVC); break;
            default:                           await _vitreous.Send(VVC);              break;
        }
    }

    // ── Publish ───────────────────────────────────────────────────────────────

    [Benchmark(Description = "Publish")]
    public async Task Publish()
    {
        switch (Mediator)
        {
            case LibraryKind.MediatR:         await _mediatRPublisher.Publish(MN);         break;
            case LibraryKind.MediatRSingleton: await _mediatRSingletonPublisher.Publish(MN); break;
            default:                           await _vitreousPublisher.Publish(VN);        break;
        }
    }
}
