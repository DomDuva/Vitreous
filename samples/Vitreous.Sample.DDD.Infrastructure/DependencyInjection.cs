using Microsoft.Extensions.DependencyInjection;
using Vitreous.Sample.DDD.Domain.Orders;
using Vitreous.Sample.DDD.Infrastructure.Orders.Persistence;

namespace Vitreous.Sample.DDD.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the infrastructure implementations of the domain's ports. Its notification
    /// handlers need no registration: the source generator in the host finds them.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<InMemoryOrderStore>();
        services.AddScoped<IOrderRepository, InMemoryOrderRepository>();
        return services;
    }
}
