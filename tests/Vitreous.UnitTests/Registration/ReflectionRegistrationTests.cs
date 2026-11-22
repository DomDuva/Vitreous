using System.Runtime.CompilerServices;

namespace Vitreous.UnitTests.Registration;

/// <summary>
/// Covers the reflection-based registration path (<c>AddVitreousHandlers(params Assembly[])</c>)
/// and the <see cref="ReflectionSender"/> that backs it when the source generator is absent.
/// </summary>
public sealed class ReflectionRegistrationTests
{
    // ── Fixture message / handler types (discovered by scanning this assembly) ──

    public sealed record ReflCommand(int Value) : ICommand<int>;
    public sealed record ReflVoidCommand : ICommand;
    public sealed record ReflQuery(string Text) : IQuery<string>;
    public sealed record ReflStreamQuery(int Count) : IStreamQuery<int>;
    public sealed record ReflNotification : INotification;

    public sealed class ReflCommandHandler : ICommandHandler<ReflCommand, int>
    {
        public ValueTask<Result<int>> Handle(ReflCommand command, CancellationToken ct = default)
            => new(Result<int>.Ok(command.Value * 2));
    }

    public sealed class ReflVoidCommandHandler : ICommandHandler<ReflVoidCommand>
    {
        public static int Calls;
        public ValueTask<Result> Handle(ReflVoidCommand command, CancellationToken ct = default)
        {
            Calls++;
            return new(Result.Success);
        }
    }

    public sealed class ReflQueryHandler : IQueryHandler<ReflQuery, string>
    {
        public ValueTask<Result<string>> Handle(ReflQuery query, CancellationToken ct = default)
            => new(Result<string>.Ok(query.Text.ToUpperInvariant()));
    }

    public sealed class ReflStreamQueryHandler : StreamQueryHandler<ReflStreamQuery, int>
    {
        protected override async IAsyncEnumerable<int> Stream(
            ReflStreamQuery query, [EnumeratorCancellation] CancellationToken ct)
        {
            for (var i = 1; i <= query.Count; i++)
            {
                await Task.Yield();
                yield return i;
            }
        }
    }

    public sealed class ReflNotificationHandlerA : INotificationHandler<ReflNotification>
    {
        public ValueTask Handle(ReflNotification n, CancellationToken ct = default)
        {
            NotificationLog.Add("A");
            return default;
        }
    }

    public sealed class ReflNotificationHandlerB : INotificationHandler<ReflNotification>
    {
        public ValueTask Handle(ReflNotification n, CancellationToken ct = default)
        {
            NotificationLog.Add("B");
            return default;
        }
    }

    public static readonly List<string> NotificationLog = new();

    /// <summary>A message that is both a command and a query with the same result type.</summary>
    public sealed record ReflDualMessage : ICommand<int>, IQuery<int>;

    public sealed class ReflDualCommandHandler : ICommandHandler<ReflDualMessage, int>
    {
        public ValueTask<Result<int>> Handle(ReflDualMessage command, CancellationToken ct = default)
            => new(1);
    }

    public sealed class ReflDualQueryHandler : IQueryHandler<ReflDualMessage, int>
    {
        public ValueTask<Result<int>> Handle(ReflDualMessage query, CancellationToken ct = default)
            => new(2);
    }

    public sealed class ScopedDependency;

    public sealed record ReflNeedsScopedQuery : IQuery<string>;

    public sealed class ReflNeedsScopedQueryHandler(ScopedDependency dependency) : IQueryHandler<ReflNeedsScopedQuery, string>
    {
        public ValueTask<Result<string>> Handle(ReflNeedsScopedQuery query, CancellationToken ct = default)
            => new(dependency.GetHashCode().ToString());
    }

    /// <summary>Open-generic behavior that records the concrete message type it saw.</summary>
    public sealed class RecordingBehavior<TMessage, TResult> : IPipelineBehavior<TMessage, TResult>
    {
        public static readonly List<string> Seen = new();

        public ValueTask<Result<TResult>> Handle(
            TMessage message,
            MessageHandlerDelegate<TMessage, TResult> next,
            CancellationToken ct = default)
        {
            Seen.Add(typeof(TMessage).Name);
            return next(message, ct);
        }
    }

    /// <summary>Behavior closed over one specific message type.</summary>
    public sealed class ReflQueryOnlyBehavior : IPipelineBehavior<ReflQuery, string>
    {
        public static int Calls;

        public ValueTask<Result<string>> Handle(
            ReflQuery message,
            MessageHandlerDelegate<ReflQuery, string> next,
            CancellationToken ct = default)
        {
            Calls++;
            return next(message, ct);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a provider with scope validation on and resolves from a scope, the way a request
    /// would. <see cref="ScopedProvider.Root"/> exposes the root provider.
    /// </summary>
    private static ScopedProvider Build(Action<IServiceCollection>? extra = null, Action<VitreousOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopedDependency>();
        services.AddVitreousHandlers(configure, typeof(ReflectionRegistrationTests).Assembly);
        extra?.Invoke(services);
        return new ScopedProvider(services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }));
    }

    private sealed class ScopedProvider(ServiceProvider root) : IServiceProvider, IDisposable
    {
        private readonly IServiceScope _scope = root.CreateScope();

        public ServiceProvider Root { get; } = root;

        public object? GetService(Type serviceType) => _scope.ServiceProvider.GetService(serviceType);

        public void Dispose()
        {
            _scope.Dispose();
            Root.Dispose();
        }
    }

    // ── Guard rails ───────────────────────────────────────────────────────────

    [Fact]
    public void AddVitreousHandlers_NoAssemblies_ThrowsHelpfulException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddVitreousHandlers();

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Vitreous.SourceGeneration*")
           .WithMessage("*typeof(Program).Assembly*");
    }

    [Fact]
    public void AddVitreousHandlers_Assembly_RegistersReflectionSenderAndPublisher()
    {
        using var sp = Build();

        sp.GetRequiredService<ISender>().GetType().Name.Should().Be("ReflectionSender");
        sp.GetRequiredService<IPublisher>().GetType().Name.Should().Be("ReflectionPublisher");
    }

    // ── Dispatch through ReflectionSender ─────────────────────────────────────

    [Fact]
    public async Task Send_Command_DispatchesToHandler()
    {
        using var sp = Build();
        var sender = sp.GetRequiredService<ISender>();

        var result = await sender.Send(new ReflCommand(21));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public async Task Send_VoidCommand_DispatchesToHandler()
    {
        using var sp = Build();
        var sender = sp.GetRequiredService<ISender>();
        var before = ReflVoidCommandHandler.Calls;

        var result = await sender.Send(new ReflVoidCommand());

        result.IsSuccess.Should().BeTrue();
        ReflVoidCommandHandler.Calls.Should().Be(before + 1);
    }

    [Fact]
    public async Task Send_Query_DispatchesToHandler()
    {
        using var sp = Build();
        var sender = sp.GetRequiredService<ISender>();

        var result = await sender.Send(new ReflQuery("abc"));

        result.Value.Should().Be("ABC");
    }

    [Fact]
    public async Task CreateStream_DispatchesToHandler()
    {
        using var sp = Build();
        var sender = sp.GetRequiredService<ISender>();

        var result = await sender.CollectStream(new ReflStreamQuery(3));

        result.Value.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Send_RepeatedCalls_ReuseCachedInvoker()
    {
        using var sp = Build();
        var sender = sp.GetRequiredService<ISender>();

        for (var i = 0; i < 50; i++)
            (await sender.Send(new ReflCommand(i))).Value.Should().Be(i * 2);
    }

    [Fact]
    public async Task Send_UnregisteredMessage_ThrowsWithGuidance()
    {
        var services = new ServiceCollection();
        services.AddVitreous();
        using var sp = services.BuildServiceProvider();
        var sender = sp.GetRequiredService<ISender>();

        var act = async () => await sender.Send(new ReflCommand(1));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ReflCommand*")
            .WithMessage("*ICommandHandler<ReflCommand, Int32>*")
            .WithMessage("*Vitreous.SourceGeneration*");
    }

    // ── Behaviors are resolved by the CONCRETE message type ──────────────────

    [Fact]
    public async Task OpenGenericBehavior_SeesConcreteMessageType()
    {
        using var sp = Build(s =>
            s.AddSingleton(typeof(IPipelineBehavior<,>), typeof(RecordingBehavior<,>)));
        var sender = sp.GetRequiredService<ISender>();
        RecordingBehavior<ReflQuery, string>.Seen.Clear();

        await sender.Send(new ReflQuery("x"));

        RecordingBehavior<ReflQuery, string>.Seen.Should().Equal(nameof(ReflQuery));
    }

    [Fact]
    public async Task ClosedBehavior_ForSpecificMessage_IsInvoked()
    {
        using var sp = Build(s =>
            s.AddSingleton<IPipelineBehavior<ReflQuery, string>, ReflQueryOnlyBehavior>());
        var sender = sp.GetRequiredService<ISender>();
        var before = ReflQueryOnlyBehavior.Calls;

        await sender.Send(new ReflQuery("x"));
        await sender.Send(new ReflCommand(1)); // different message — behavior must not run

        ReflQueryOnlyBehavior.Calls.Should().Be(before + 1);
    }

    // ── Notifications ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Publish_AllScannedNotificationHandlersAreInvoked()
    {
        using var sp = Build();
        var publisher = sp.GetRequiredService<IPublisher>();
        NotificationLog.Clear();

        await publisher.Publish(new ReflNotification());

        NotificationLog.Should().BeEquivalentTo(["A", "B"]);
    }

    // ── Lifetimes ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ScopedHandlerLifetime_SenderIsScoped_AndResolvesInsideScope()
    {
        using var sp = Build(configure: o => o.DefaultHandlerLifetime = ServiceLifetime.Scoped);

        // Singleton sender + scoped handlers would throw under ValidateScopes; a scoped
        // sender resolves cleanly from a scope.
        using var scope = sp.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        (await sender.Send(new ReflCommand(2))).Value.Should().Be(4);

        var act = () => sp.Root.GetRequiredService<ISender>();
        act.Should().Throw<InvalidOperationException>("ISender must be scoped when handlers are scoped");
    }

    [Fact]
    public void DefaultHandlerLifetime_IsScoped()
    {
        new VitreousOptions().DefaultHandlerLifetime.Should().Be(ServiceLifetime.Scoped);

        using var sp = Build();
        var act = () => sp.Root.GetRequiredService<ISender>();
        act.Should().Throw<InvalidOperationException>("the default lifetime is scoped");
    }

    [Fact]
    public async Task TransientHandlerLifetime_SenderIsScoped_SoScopedDependenciesResolve()
    {
        // A singleton sender would resolve the transient handler from the root provider,
        // capturing the scoped dependency (or throwing under ValidateScopes).
        using var sp = Build(configure: o => o.DefaultHandlerLifetime = ServiceLifetime.Transient);

        var result = await sp.GetRequiredService<ISender>().Send(new ReflNeedsScopedQuery());

        result.IsSuccess.Should().BeTrue();
        var act = () => sp.Root.GetRequiredService<ISender>();
        act.Should().Throw<InvalidOperationException>("ISender must be scoped when handlers are transient");
    }

    [Fact]
    public void SingletonHandlerLifetime_SenderIsSingleton()
    {
        using var sp = Build(configure: o => o.DefaultHandlerLifetime = ServiceLifetime.Singleton);

        sp.Root.GetRequiredService<ISender>().Should().BeSameAs(sp.Root.GetRequiredService<ISender>());
    }

    // ── Options ───────────────────────────────────────────────────────────────

    [Fact]
    public void AddVitreous_CalledTwice_RegistersOptionsOnce()
    {
        var services = new ServiceCollection();
        services.AddVitreous();
        services.AddVitreous();

        services.Count(d => d.ServiceType == typeof(VitreousOptions)).Should().Be(1);
    }

    [Fact]
    public void AddVitreousGenerated_RegistersEverythingButTheReflectionSenderAndPublisher()
    {
        var reflection = new ServiceCollection().AddVitreous(new VitreousOptions());
        var generated = new ServiceCollection().AddVitreousGenerated(new VitreousOptions());

        generated.Should().NotContain(static d => d.ServiceType == typeof(ISender));
        generated.Should().NotContain(static d => d.ImplementationType != null && d.ImplementationType.Name == "ReflectionPublisher");
        generated.Should().ContainSingle(static d => d.ServiceType == typeof(IPublisher))
            .Which.ImplementationType.Should().Be<SequentialPublisher>();
        generated.Select(static d => (d.ServiceType, d.ImplementationType, d.Lifetime))
            .Should().BeEquivalentTo(reflection
                .Where(static d => d.ServiceType != typeof(ISender) && d.ServiceType != typeof(IPublisher))
                .Select(static d => (d.ServiceType, d.ImplementationType, d.Lifetime))
                .Append((typeof(IPublisher), typeof(SequentialPublisher), ServiceLifetime.Scoped)));
    }

    // ── Invoker cache ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Send_MessageThatIsBothCommandAndQuery_DispatchesEachKindToItsHandler()
    {
        using var sp = Build();
        var sender = sp.GetRequiredService<ISender>();
        var message = new ReflDualMessage();

        // The first call caches an invoker; the second must not reuse it for the other kind.
        (await sender.Send((ICommand<int>)message)).Value.Should().Be(1);
        (await sender.Send((IQuery<int>)message)).Value.Should().Be(2);
    }

    [Fact]
    public void TransientHandlerLifetime_ProducesNewHandlerPerResolve()
    {
        using var sp = Build(configure: o => o.DefaultHandlerLifetime = ServiceLifetime.Transient);

        var a = sp.GetRequiredService<ICommandHandler<ReflCommand, int>>();
        var b = sp.GetRequiredService<ICommandHandler<ReflCommand, int>>();

        a.Should().NotBeSameAs(b);
    }
}
