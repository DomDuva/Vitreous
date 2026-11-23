#if (UseValidation)
using FluentValidation;
#endif
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vitreous;
#if (UseValidation)
using Vitreous.FluentValidation;
#endif
using VitreousClean._1.Application.Common;
#if (UseValidation)
using VitreousClean._1.Application.Orders;
#endif

namespace VitreousClean._1.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers what the use cases need, whatever the host. The handlers are not in here: the host
    /// calls <c>AddVitreousHandlers()</c>, which the source generator writes there.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Pipeline behaviors wrap every command and query. They run in registration order, outermost first.
        services.AddVitreousBehavior(typeof(LoggingBehavior<,>));
#if (UseValidation)

        // Runs each message's validators before its handler. Invalid messages return Error.Validation.
        services.AddValidatorsFromAssemblyContaining<PlaceOrderValidator>();
        services.AddVitreousValidation();
#endif

        // The clock is a service, so a test can replace it.
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
