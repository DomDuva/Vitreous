using BenchmarkDotNet.Attributes;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Vitreous;

namespace Vitreous.Benchmarks.Benchmarks;

/// <summary>
/// Measures cold DI-container startup cost — how long <c>BuildServiceProvider()</c>
/// takes and how much it allocates for each mediator configuration.
///
/// Three rows:
///   MediatR (Transient)  — default: reflection scans the assembly, registers handlers
///                          as Transient, creates several internal MediatR services.
///   MediatR (Singleton)  — same reflection scan but with Singleton lifetime.
///   Vitreous             — source-generated: only direct TryAddSingleton calls,
///                          zero reflection.
///
/// Expected output:
///   | Method                  | Mediator         | Mean (ns) | Allocated (B) |
///   | Build service provider  | MediatR          |  ~35000   |    ~100 800   |
///   | Build service provider  | MediatR Singleton|  ~35000   |    ~100 800   |
///   | Build service provider  | Vitreous         |   ~1200   |      ~7 500   |
///
/// Note: MediatR Transient vs Singleton make almost no difference here because
/// the reflection scanning and type registration cost dominates — the lifetime
/// setting is recorded but handlers are not yet instantiated at build time.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
public class StartupBenchmarks
{
    [Params(LibraryKind.MediatR, LibraryKind.MediatRSingleton, LibraryKind.Vitreous)]
    public LibraryKind Mediator { get; set; }

    [Benchmark(Description = "Build service provider")]
    public ServiceProvider BuildServiceProvider()
    {
        switch (Mediator)
        {
            case LibraryKind.MediatR:
            {
                var services = new ServiceCollection();
                services.AddMediatR(cfg =>
                    cfg.RegisterServicesFromAssemblyContaining<StartupBenchmarks>());
                return services.BuildServiceProvider();
            }

            case LibraryKind.MediatRSingleton:
            {
                var services = new ServiceCollection();
                services.AddMediatR(cfg =>
                {
                    cfg.RegisterServicesFromAssemblyContaining<StartupBenchmarks>();
                    cfg.Lifetime = ServiceLifetime.Singleton;
                });
                return services.BuildServiceProvider();
            }

            default: // Vitreous
            {
                var services = new ServiceCollection();
                services.AddVitreousHandlers();
                return services.BuildServiceProvider();
            }
        }
    }
}
