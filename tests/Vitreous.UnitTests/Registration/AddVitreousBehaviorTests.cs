namespace Vitreous.UnitTests.Registration;

/// <summary>
/// <see cref="VitreousServiceCollectionExtensions.AddVitreousBehavior"/>: open generics without
/// generated behaviors, and the closed registrations that replace them when there are some.
/// </summary>
public sealed class AddVitreousBehaviorTests
{
    public sealed record Ping : ICommand<int>;
    public sealed record Tick : IStreamQuery<int>;

    public sealed class OuterBehavior<TMessage, TResult> : IPipelineBehavior<TMessage, TResult>
    {
        public ValueTask<Result<TResult>> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResult> next, CancellationToken ct = default) =>
            next(message, ct);
    }

    public sealed class InnerBehavior<TMessage, TResult> : IPipelineBehavior<TMessage, TResult>
    {
        public ValueTask<Result<TResult>> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResult> next, CancellationToken ct = default) =>
            next(message, ct);
    }

    public sealed class PlainBehavior<TMessage, TResult> : IPipelineBehavior<TMessage, TResult>
    {
        public ValueTask<Result<TResult>> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResult> next, CancellationToken ct = default) =>
            next(message, ct);
    }

    public sealed class PingBehavior : IPipelineBehavior<Ping, int>
    {
        public ValueTask<Result<int>> Handle(Ping message, MessageHandlerDelegate<Ping, int> next, CancellationToken ct = default) =>
            next(message, ct);
    }

    public sealed class BothBehavior<TMessage, TResult> : IPipelineBehavior<TMessage, TResult>, IStreamPipelineBehavior<TMessage, TResult>
        where TMessage : IStreamQuery<TResult>
    {
        public ValueTask<Result<TResult>> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResult> next, CancellationToken ct = default) =>
            next(message, ct);

        public ValueTask<Result<IAsyncEnumerable<TResult>>> Handle(TMessage message, StreamQueryHandlerDelegate<TMessage, TResult> next, CancellationToken ct = default) =>
            next(message, ct);
    }

    public sealed class SwappedBehavior<TResult, TMessage> : IPipelineBehavior<TMessage, TResult>
    {
        public ValueTask<Result<TResult>> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResult> next, CancellationToken ct = default) =>
            next(message, ct);
    }

    private static GeneratedBehaviors Closing(params Type[] openBehaviors)
    {
        var generated = new GeneratedBehaviors();
        foreach (var open in openBehaviors)
        {
            var closed = open.MakeGenericType(typeof(Ping), typeof(int));
            generated.Add(typeof(IPipelineBehavior<,>), open, lifetime =>
                [new ServiceDescriptor(typeof(IPipelineBehavior<Ping, int>), closed, lifetime)]);
        }
        return generated;
    }

    private static List<(Type Service, Type? Implementation)> Behaviors(IServiceCollection services) =>
        services.Where(static d => d.ServiceType.IsGenericType &&
                                   d.ServiceType.GetGenericTypeDefinition() is var def &&
                                   (def == typeof(IPipelineBehavior<,>) || def == typeof(IStreamPipelineBehavior<,>)))
            .Select(static d => (d.ServiceType, d.ImplementationType))
            .ToList();

    [Fact]
    public void OpenGeneric_WithoutGeneratedBehaviors_IsRegisteredOpen()
    {
        var services = new ServiceCollection().AddVitreousBehavior(typeof(OuterBehavior<,>), ServiceLifetime.Singleton);

        var descriptor = services.Single(static d => d.ServiceType == typeof(IPipelineBehavior<,>));
        descriptor.ImplementationType.Should().Be(typeof(OuterBehavior<,>));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void OpenGeneric_IsRegisteredForEveryBehaviorInterface()
    {
        var services = new ServiceCollection().AddVitreousBehavior(typeof(BothBehavior<,>));

        Behaviors(services).Should().Equal(
            (typeof(IPipelineBehavior<,>), typeof(BothBehavior<,>)),
            (typeof(IStreamPipelineBehavior<,>), typeof(BothBehavior<,>)));
    }

    [Fact]
    public void ClosedBehavior_IsRegisteredForItsMessage()
    {
        var services = new ServiceCollection().AddVitreousBehavior(typeof(PingBehavior));

        Behaviors(services).Should().Equal((typeof(IPipelineBehavior<Ping, int>), typeof(PingBehavior)));
        services.Single(static d => d.ServiceType == typeof(IPipelineBehavior<Ping, int>)).Lifetime.Should().Be(ServiceLifetime.Transient);
    }

    [Fact]
    public void AddingTheSameBehaviorTwice_RegistersItOnce()
    {
        var services = new ServiceCollection()
            .AddVitreousBehavior(typeof(OuterBehavior<,>))
            .AddVitreousBehavior(typeof(OuterBehavior<,>))
            .AddVitreousBehavior(typeof(PingBehavior))
            .AddVitreousBehavior(typeof(PingBehavior));

        Behaviors(services).Should().HaveCount(2);
    }

    [Fact]
    public void ATypeThatIsNotABehavior_Throws()
    {
        var act = () => new ServiceCollection().AddVitreousBehavior(typeof(List<>));

        act.Should().Throw<ArgumentException>().WithMessage("*does not implement IPipelineBehavior<,>*");
    }

    [Fact]
    public void TypeParametersPassedOutOfOrder_Throws()
    {
        var act = () => new ServiceCollection().AddVitreousBehavior(typeof(SwappedBehavior<,>));

        act.Should().Throw<ArgumentException>().WithMessage("*in the order it declares them*");
    }

    [Fact]
    public void WithGeneratedBehaviors_RegistersTheClosedBehaviorsInsteadOfTheOpenGeneric()
    {
        var services = new ServiceCollection()
            .AddVitreousGeneratedBehaviors(Closing(typeof(OuterBehavior<,>)))
            .AddVitreousBehavior(typeof(OuterBehavior<,>), ServiceLifetime.Scoped);

        Behaviors(services).Should().Equal((typeof(IPipelineBehavior<Ping, int>), typeof(OuterBehavior<Ping, int>)));
        services.Single(static d => d.ServiceType == typeof(IPipelineBehavior<Ping, int>)).Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void BehaviorsAddedBeforeTheGeneratedOnes_AreClosedInPlace()
    {
        var services = new ServiceCollection()
            .AddVitreousBehavior(typeof(OuterBehavior<,>))
            .AddSingleton(typeof(IPipelineBehavior<,>), typeof(PlainBehavior<,>))
            .AddVitreousBehavior(typeof(InnerBehavior<,>));

        services.AddVitreousGeneratedBehaviors(Closing(typeof(OuterBehavior<,>), typeof(InnerBehavior<,>), typeof(PlainBehavior<,>)));

        // Order is kept; a behavior registered directly as a service is left alone.
        Behaviors(services).Should().Equal(
            (typeof(IPipelineBehavior<Ping, int>), typeof(OuterBehavior<Ping, int>)),
            (typeof(IPipelineBehavior<,>), typeof(PlainBehavior<,>)),
            (typeof(IPipelineBehavior<Ping, int>), typeof(InnerBehavior<Ping, int>)));
    }

    [Fact]
    public void AnOpenGenericWithoutGeneratedClosers_StaysOpen()
    {
        var services = new ServiceCollection()
            .AddVitreousBehavior(typeof(InnerBehavior<,>));

        services.AddVitreousGeneratedBehaviors(Closing(typeof(OuterBehavior<,>)));

        Behaviors(services).Should().Equal((typeof(IPipelineBehavior<,>), typeof(InnerBehavior<,>)));
    }
}
