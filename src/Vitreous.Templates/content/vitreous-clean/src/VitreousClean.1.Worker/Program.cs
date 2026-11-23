using Vitreous;
using VitreousClean._1.Application;
using VitreousClean._1.Infrastructure;
using VitreousClean._1.Worker.Orders;

var host = CreateBuilder(args).Build();

await host.Services.EnsureDatabaseCreatedAsync();

// Sample data, so the first run has something to do. Delete it along with SampleOrders.cs.
if (host.Services.GetRequiredService<IHostEnvironment>().IsDevelopment())
    await SampleOrders.Place(host.Services);

await host.RunAsync();

public partial class Program
{
    /// <summary>The app's services. The tests build their host from here too.</summary>
    public static HostApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Generated at build time by Vitreous.SourceGeneration: registers the handlers of every
        // layer and a dispatcher that calls them without reflection. Handlers are scoped, like a
        // DbContext, so the worker opens a scope per unit of work.
        builder.Services.AddVitreousHandlers();

        // Application first: its behaviors (logging, validation) run outside Infrastructure's (caching).
        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);

        builder.Services.AddOptions<ShippingOptions>()
            .BindConfiguration("Shipping")
            .Validate(static o => o.Interval > TimeSpan.Zero, "Shipping:Interval must be greater than zero.")
            .ValidateOnStart();

        builder.Services.AddHostedService<ShippingWorker>();

        return builder;
    }
}
