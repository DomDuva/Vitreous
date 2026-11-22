namespace Vitreous.FluentValidationTests;

/// <summary>
/// Sends messages through a real container: the generated dispatcher (with the behaviors the
/// generator closes over each message) and the reflection-based sender.
/// </summary>
public sealed class DispatchTests
{
    public enum Dispatcher { Generated, Reflection }

    public static TheoryData<Dispatcher> Dispatchers => new() { Dispatcher.Generated, Dispatcher.Reflection };

    private static (ISender Sender, HandlerLog Log) Create(Dispatcher dispatcher)
    {
        var services = new ServiceCollection();
        services.AddSingleton<HandlerLog>();

        if (dispatcher == Dispatcher.Generated)
            services.AddVitreousHandlers();
        else
            services.AddVitreousHandlers(typeof(DispatchTests).Assembly);

        services.AddVitreousValidation();
        services.AddValidatorsFromAssemblyContaining<DispatchTests>(includeInternalTypes: true); // scoped

        var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var scope = root.CreateScope().ServiceProvider;
        return (scope.GetRequiredService<ISender>(), scope.GetRequiredService<HandlerLog>());
    }

    [Theory, MemberData(nameof(Dispatchers))]
    public async Task Send_InvalidCommand_ReturnsValidationErrorAndSkipsTheHandler(Dispatcher dispatcher)
    {
        var (sender, log) = Create(dispatcher);

        var result = await sender.Send(new CreateOrder("", 0));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VALIDATION");
        ValidationBehaviorTests.Failures(result.Error).Should().BeEquivalentTo(new Dictionary<string, string[]>
        {
            ["Customer"] = ["Customer is required."],
            ["Quantity"] = ["Quantity must be greater than zero."],
        });
        log.Calls.Should().BeEmpty();
    }

    [Theory, MemberData(nameof(Dispatchers))]
    public async Task Send_ValidCommand_RunsTheHandler(Dispatcher dispatcher)
    {
        var (sender, log) = Create(dispatcher);

        var result = await sender.Send(new CreateOrder("ada", 1));

        result.IsSuccess.Should().BeTrue();
        log.Calls.Should().Equal(nameof(CreateOrder));
    }

    [Theory, MemberData(nameof(Dispatchers))]
    public async Task Send_VoidCommand_IsValidated(Dispatcher dispatcher)
    {
        var (sender, log) = Create(dispatcher);

        var invalid = await sender.Send(new DeleteOrder(Guid.Empty));
        var valid   = await sender.Send(new DeleteOrder(Guid.NewGuid()));

        invalid.IsFailure.Should().BeTrue();
        ValidationBehaviorTests.Failures(invalid.Error)["Id"].Should().Equal("Id is required.");
        valid.IsSuccess.Should().BeTrue();
        log.Calls.Should().Equal(nameof(DeleteOrder));
    }

    [Theory, MemberData(nameof(Dispatchers))]
    public async Task Send_Query_IsValidated(Dispatcher dispatcher)
    {
        var (sender, log) = Create(dispatcher);

        var invalid = await sender.Send(new GetOrder(Guid.Empty));
        var valid   = await sender.Send(new GetOrder(Guid.NewGuid()));

        invalid.Error.Code.Should().Be("VALIDATION");
        valid.IsSuccess.Should().BeTrue();
        log.Calls.Should().Equal(nameof(GetOrder));
    }

    [Theory, MemberData(nameof(Dispatchers))]
    public async Task Send_StructCommand_IsValidated(Dispatcher dispatcher)
    {
        var (sender, log) = Create(dispatcher);

        var invalid = await sender.Send(new Add(-1, 2));
        var valid   = await sender.Send(new Add(1, 2));

        invalid.Error.Code.Should().Be("VALIDATION");
        valid.Value.Should().Be(3);
        log.Calls.Should().Equal(nameof(Add));
    }

    [Theory, MemberData(nameof(Dispatchers))]
    public async Task Send_MessageWithoutValidator_PassesThrough(Dispatcher dispatcher)
    {
        var (sender, log) = Create(dispatcher);

        var result = await sender.Send(new Ping());

        result.Value.Should().Be("pong");
        log.Calls.Should().Equal(nameof(Ping));
    }

    [Theory, MemberData(nameof(Dispatchers))]
    public async Task CreateStream_InvalidQuery_FailsBeforeTheStreamOpens(Dispatcher dispatcher)
    {
        var (sender, log) = Create(dispatcher);

        var invalid = await sender.CreateStream(new CountTo(0));
        var valid   = await sender.CollectStream(new CountTo(3));

        invalid.IsFailure.Should().BeTrue();
        ValidationBehaviorTests.Failures(invalid.Error)["N"].Should().Equal("N must be between 1 and 10.");
        valid.Value.Should().Equal(1, 2, 3);
        log.Calls.Should().Equal(nameof(CountTo));
    }

    [Fact]
    public void AddVitreousValidation_CalledTwice_RegistersEachBehaviorOnce()
    {
        var services = new ServiceCollection();

        services.AddVitreousValidation();
        services.AddVitreousValidation();

        services.Count(d => d.ImplementationType == typeof(ValidationBehavior<,>)).Should().Be(1);
        services.Count(d => d.ImplementationType == typeof(StreamValidationBehavior<,>)).Should().Be(1);
    }

    [Fact]
    public void AddVitreousValidation_AfterTheGeneratedHandlers_RegistersClosedBehaviors()
    {
        var services = new ServiceCollection();

        services.AddVitreousHandlers();
        services.AddVitreousValidation();

        // The generator closed the behaviors over each message, so nothing is left for DI to
        // close at runtime (which fails under Native AOT for struct messages and value-type results).
        services.Should().Contain(d => d.ServiceType == typeof(IPipelineBehavior<Add, int>)
                                    && d.ImplementationType == typeof(ValidationBehavior<Add, int>));
        services.Should().Contain(d => d.ServiceType == typeof(IStreamPipelineBehavior<CountTo, int>)
                                    && d.ImplementationType == typeof(StreamValidationBehavior<CountTo, int>));
        services.Should().NotContain(d => d.ImplementationType == typeof(ValidationBehavior<,>));
    }
}
