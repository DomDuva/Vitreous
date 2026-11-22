using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Vitreous.Abstractions;

namespace Vitreous.Internal;

/// <summary>
/// Reflection-based <see cref="IPublisher"/> used when the source generator is not present.
/// It publishes a notification as its <em>runtime</em> type, the way <see cref="ReflectionSender"/>
/// sends a message, and the source-generated <c>VitreousDispatcher</c> replaces it.
/// </summary>
/// <remarks>
/// <see cref="SequentialPublisher"/> and <see cref="ParallelPublisher"/> resolve handlers by the
/// type argument, so a notification held as <see cref="INotification"/> or as a base type would
/// miss the handlers of its concrete type. This publisher picks the type to publish as:
/// the runtime type if it has handlers, else its nearest base class that has handlers, else the
/// type argument. Reflection is used once per type to build the publishing delegate, which is cached.
/// </remarks>
[RequiresUnreferencedCode(VitreousServiceCollectionExtensions.ReflectionMessage)]
[RequiresDynamicCode(VitreousServiceCollectionExtensions.ReflectionMessage)]
internal sealed class ReflectionPublisher : IPublisher
{
    private static readonly MethodInfo _publishAs =
        typeof(ReflectionPublisher).GetMethod(nameof(PublishAs), BindingFlags.NonPublic | BindingFlags.Static)!;

    // Runtime notification type → that type and its base classes that are notifications, nearest
    // first, each paired with its closed INotificationHandler<>. Doesn't depend on the container.
    private static readonly ConcurrentDictionary<Type, (Type Notification, Type Handler)[]> s_chains = new();

    // Notification type → a delegate that publishes a notification as that type.
    private static readonly ConcurrentDictionary<Type, Func<IPublisher, INotification, CancellationToken, ValueTask>> s_invokers = new();

    private readonly IServiceProvider _services;
    private readonly IServiceProviderIsService? _isService;
    private readonly bool _parallelPublish;

    public ReflectionPublisher(IServiceProvider services, VitreousOptions options)
    {
        _services        = services;
        _isService       = services.GetService<IServiceProviderIsService>();
        _parallelPublish = options.NotificationPublishStrategy == NotificationPublishStrategy.Parallel;
    }

    public ValueTask Publish<TNotification>(TNotification notification, CancellationToken ct = default)
        where TNotification : INotification
    {
        if (notification is null)
            throw new ArgumentNullException(nameof(notification));

        IPublisher publisher = _parallelPublish
            ? _services.GetRequiredService<ParallelPublisher>()
            : _services.GetRequiredService<SequentialPublisher>();

        // Value types can't be derived from, so they are always published as the type argument
        // (and GetType() would box them).
        if (!typeof(TNotification).IsValueType
            && notification.GetType() != typeof(TNotification)
            && FindHandledType(notification.GetType()) is { } target
            && target != typeof(TNotification))
        {
            var invoker = s_invokers.GetOrAdd(target, static type =>
                _publishAs.MakeGenericMethod(type).CreateDelegate<Func<IPublisher, INotification, CancellationToken, ValueTask>>());
            return invoker(publisher, notification, ct);
        }

        return publisher.Publish(notification, ct);
    }

    private Type? FindHandledType(Type runtimeType)
    {
        // Without IServiceProviderIsService the handlers can't be checked up front, so publish as
        // the runtime type.
        if (_isService is null)
            return runtimeType;

        foreach (var (notification, handler) in s_chains.GetOrAdd(runtimeType, BuildChain))
        {
            if (_isService.IsService(handler))
                return notification;
        }

        return null;
    }

    private static (Type Notification, Type Handler)[] BuildChain(Type runtimeType)
    {
        var chain = new List<(Type, Type)>();

        // A base class that isn't a notification can't close INotificationHandler<>, and neither
        // can its own bases.
        for (var type = runtimeType; type is not null && typeof(INotification).IsAssignableFrom(type); type = type.BaseType)
            chain.Add((type, typeof(INotificationHandler<>).MakeGenericType(type)));

        return chain.ToArray();
    }

    private static ValueTask PublishAs<TNotification>(IPublisher publisher, INotification notification, CancellationToken ct)
        where TNotification : INotification =>
        publisher.Publish((TNotification)notification, ct);
}
