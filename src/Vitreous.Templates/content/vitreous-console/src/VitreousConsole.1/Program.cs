#if (UseValidation)
using FluentValidation;
#endif
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vitreous;
using Vitreous.Abstractions;
#if (UseValidation)
using Vitreous.FluentValidation;
#endif
using VitreousConsole._1.Common;
using VitreousConsole._1.Features.Todos;

// Ctrl+C cancels the token that every handler receives, so a command can stop cleanly.
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

using var host = CreateBuilder().Build();

// Handlers are scoped, like a DbContext, and so is ISender. A web app gets a scope per request;
// a console app creates its own: one run of the app is one unit of work.
await using var scope = host.Services.CreateAsyncScope();

var sender = scope.ServiceProvider.GetRequiredService<ISender>();
var output = scope.ServiceProvider.GetRequiredService<CliOutput>();

try
{
    return await TodoCli.Run(args, sender, output, cancellation.Token);
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
        // The arguments are this app's own (see TodoCli). Passed to the host, they would be read as
        // configuration. The content root is where appsettings.json is copied, so the app can run
        // from any directory.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
        });

        // Logs go to stderr: stdout carries only results, so it can be piped.
        builder.Logging.AddConsole(static o => o.LogToStandardErrorThreshold = LogLevel.Trace);

        // Generated at build time by Vitreous.SourceGeneration: registers every handler in this
        // project and a dispatcher that calls them without reflection.
        builder.Services.AddVitreousHandlers();

        // Pipeline behaviors wrap every command and query. They run in registration order, outermost first.
        builder.Services.AddVitreousBehavior(typeof(LoggingBehavior<,>));
#if (UseValidation)

        // Runs each message's validators before its handler. Invalid messages return Error.Validation.
        builder.Services.AddValidatorsFromAssemblyContaining<Program>();
        builder.Services.AddVitreousValidation();
#endif

        builder.Services.AddSingleton(new CliOutput(Console.Out, Console.Error));
        builder.Services.AddSingleton(new TodoStore(builder.Configuration["TodosFile"] ?? TodoStore.DefaultPath));

        return builder;
    }
}
