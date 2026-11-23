#if (UseValidation)
using FluentValidation;
#endif
using Vitreous;
#if (caching == "memory")
using Vitreous.Caching;
#elif (caching == "redis")
using Vitreous.Caching.Redis;
#endif
#if (UseValidation)
using Vitreous.FluentValidation;
#endif
using VitreousWorker._1.Common;
using VitreousWorker._1.Features.Reminders;

var host = CreateBuilder(args).Build();

// Sample data, so the first run has something to do. Delete it along with the Reminders feature.
var reminders = host.Services.GetRequiredService<ReminderStore>();
var now       = DateTimeOffset.UtcNow;
reminders.Add("ada", "Review the pull request", now);
reminders.Add("grace", "Update the changelog", now.AddSeconds(5));
reminders.Add("ada", "Publish the release", now.AddSeconds(10));

await host.RunAsync();

public partial class Program
{
    /// <summary>The app's services. The tests build their host from here too.</summary>
    public static HostApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Generated at build time by Vitreous.SourceGeneration: registers every handler in this
        // project and a dispatcher that calls them without reflection. Handlers are scoped, like a
        // DbContext, so the worker opens a scope per unit of work.
        builder.Services.AddVitreousHandlers();

        // Pipeline behaviors wrap every command and query. They run in registration order, outermost first.
        builder.Services.AddVitreousBehavior(typeof(LoggingBehavior<,>));
#if (UseValidation)

        // Runs each message's validators before its handler. Invalid messages return Error.Validation.
        builder.Services.AddValidatorsFromAssemblyContaining<Program>();
        builder.Services.AddVitreousValidation();
#endif
#if (caching == "memory")

        // Caches the result of every ICacheableQuery. The in-memory IDistributedCache suits one instance;
        // switch to Vitreous.Caching.Redis when you run several.
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddVitreousCaching();
#elif (caching == "redis")

        // Caches the result of every ICacheableQuery in Redis (compose.yaml runs one locally). If Redis is
        // down, queries still run: cache errors count as misses.
        builder.Services.AddVitreousRedisCaching(o => o.ConnectionString = builder.Configuration.GetConnectionString("Redis"));
#endif

        builder.Services.AddOptions<ReminderOptions>()
            .BindConfiguration("Reminders")
            .Validate(static o => o.Interval > TimeSpan.Zero, "Reminders:Interval must be greater than zero.")
            .ValidateOnStart();

        // The clock is a service, so the tests can replace it and move time forward themselves.
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ReminderStore>();
        builder.Services.AddSingleton<RecipientDirectory>();
        builder.Services.AddHostedService<ReminderWorker>();

        return builder;
    }
}
