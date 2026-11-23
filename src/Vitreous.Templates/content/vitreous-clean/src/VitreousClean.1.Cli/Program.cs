using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vitreous;
using Vitreous.Abstractions;
using VitreousClean._1.Application;
using VitreousClean._1.Cli.Common;
using VitreousClean._1.Cli.Orders;
using VitreousClean._1.Infrastructure;

// Ctrl+C cancels the token that every handler receives, so a command can stop cleanly.
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

using var host = CreateBuilder().Build();

await host.Services.EnsureDatabaseCreatedAsync();

// Handlers are scoped, like a DbContext, and so is ISender. A web app gets a scope per request;
// a console app creates its own: one run of the app is one unit of work.
await using var scope = host.Services.CreateAsyncScope();

var sender = scope.ServiceProvider.GetRequiredService<ISender>();
var output = scope.ServiceProvider.GetRequiredService<CliOutput>();

try
{
    return await OrderCli.Run(args, sender, output, cancellation.Token);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    return ExitCodes.Cancelled;
}

public partial class Program
{
    /// <summary>The app's services. The tests build their host from here too.</summary>
    public static HostApplicationBuilder CreateBuilder()
    {
        // The arguments are this app's own (see OrderCli). Passed to the host, they would be read as
        // configuration. The content root is where appsettings.json is copied, so the app can run
        // from any directory.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
        });

        // Logs go to stderr: stdout carries only results, so it can be piped.
        builder.Logging.AddConsole(static o => o.LogToStandardErrorThreshold = LogLevel.Trace);

        // Generated at build time by Vitreous.SourceGeneration: registers the handlers of every
        // layer and a dispatcher that calls them without reflection.
        builder.Services.AddVitreousHandlers();

        // Application first: its behaviors (logging, validation) run outside Infrastructure's (caching).
        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);

        builder.Services.AddSingleton(new CliOutput(Console.Out, Console.Error));

        // Unless it is configured, the database is in the user's data folder: the same one from any directory.
        builder.Configuration["ConnectionStrings:Database"] ??= $"Data Source={DefaultDatabasePath()}";

        return builder;
    }

    private static string DefaultDatabasePath()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VitreousClean.1");
        Directory.CreateDirectory(folder);

        return Path.Combine(folder, "VitreousClean.1.db");
    }
}
