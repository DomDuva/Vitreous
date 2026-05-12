using Vitreous.Abstractions;

namespace Vitreous.Internal;

/// <summary>
/// Reflection-based <see cref="ISender"/> used when the source generator is not present.
/// The source generator will emit a concrete <c>VitreousDispatcher</c> that replaces this.
/// </summary>
internal sealed class ReflectionSender : ISender
{
    private readonly IServiceProvider _services;

    public ReflectionSender(IServiceProvider services) => _services = services;

    public ValueTask<Result<TResult>> Send<TResult>(
        ICommand<TResult> command,
        CancellationToken ct = default)
    {
        var handlerType = typeof(ICommandHandler<,>)
            .MakeGenericType(command.GetType(), typeof(TResult));

        var handler = _services.GetService(handlerType)
            ?? throw new InvalidOperationException(
                $"No handler registered for command '{command.GetType().Name}'. " +
                "Ensure Vitreous.SourceGeneration is referenced and AddVitreous() is called.");

        // Resolve behaviors
        var behaviors = GetBehaviors<ICommand<TResult>, TResult>(_services);

        // Build terminal delegate via reflection (cold path only)
        var handleMethod = handlerType.GetMethod(nameof(ICommandHandler<ICommand<TResult>, TResult>.Handle))!;
        MessageHandlerDelegate<ICommand<TResult>, TResult> terminal =
            (msg, token) => (ValueTask<Result<TResult>>)handleMethod.Invoke(handler, [msg, token])!;

        return PipelineExecutor.Execute(command, terminal, behaviors, ct);
    }

    public ValueTask<Result> Send(ICommand command, CancellationToken ct = default)
    {
        var handlerType = typeof(ICommandHandler<>).MakeGenericType(command.GetType());

        var handler = _services.GetService(handlerType)
            ?? throw new InvalidOperationException(
                $"No handler registered for command '{command.GetType().Name}'.");

        var handleMethod = handlerType.GetMethod("Handle")!;
        CommandHandlerDelegate<ICommand> terminal =
            (cmd, token) => (ValueTask<Result>)handleMethod.Invoke(handler, [cmd, token])!;

        // Void commands participate in the standard IPipelineBehavior<TCommand, Unit> pipeline.
        var behaviors = GetVoidBehaviors(_services);
        return PipelineExecutor.ExecuteVoid(command, terminal, behaviors, ct);
    }

    public ValueTask<Result<TResult>> Send<TResult>(
        IQuery<TResult> query,
        CancellationToken ct = default)
    {
        var handlerType = typeof(IQueryHandler<,>)
            .MakeGenericType(query.GetType(), typeof(TResult));

        var handler = _services.GetService(handlerType)
            ?? throw new InvalidOperationException(
                $"No handler registered for query '{query.GetType().Name}'.");

        var handleMethod = handlerType.GetMethod("Handle")!;
        MessageHandlerDelegate<IQuery<TResult>, TResult> terminal =
            (msg, token) => (ValueTask<Result<TResult>>)handleMethod.Invoke(handler, [msg, token])!;

        var behaviors = GetBehaviors<IQuery<TResult>, TResult>(_services);
        return PipelineExecutor.Execute(query, terminal, behaviors, ct);
    }

    public ValueTask<Result<IAsyncEnumerable<TResult>>> CreateStream<TResult>(
        IStreamQuery<TResult> query,
        CancellationToken ct = default)
    {
        var handlerType = typeof(IStreamQueryHandler<,>)
            .MakeGenericType(query.GetType(), typeof(TResult));

        var handler = _services.GetService(handlerType)
            ?? throw new InvalidOperationException(
                $"No handler registered for stream query '{query.GetType().Name}'. " +
                "Implement 'IStreamQueryHandler<TQuery, TResult>' and ensure AddVitreous() is called.");

        var handleMethod = handlerType.GetMethod("Handle")!;
        StreamQueryHandlerDelegate<IStreamQuery<TResult>, TResult> terminal =
            (msg, token) => (ValueTask<Result<IAsyncEnumerable<TResult>>>)handleMethod.Invoke(handler, [msg, token])!;

        var behaviors = GetStreamBehaviors<IStreamQuery<TResult>, TResult>(_services);
        return PipelineExecutor.ExecuteStream(query, terminal, behaviors, ct);
    }

    // -------------------------------------------------------------------------
    // Behavior resolution helpers
    // -------------------------------------------------------------------------

    private static IEnumerable<IPipelineBehavior<TMessage, TResult>> GetBehaviors<TMessage, TResult>(
        IServiceProvider services) =>
        services.GetServices<IPipelineBehavior<TMessage, TResult>>();

    private static IEnumerable<IPipelineBehavior<ICommand, Unit>> GetVoidBehaviors(
        IServiceProvider services) =>
        services.GetServices<IPipelineBehavior<ICommand, Unit>>();

    private static IEnumerable<IStreamPipelineBehavior<TMessage, TResult>> GetStreamBehaviors<TMessage, TResult>(
        IServiceProvider services)
        where TMessage : IStreamQuery<TResult> =>
        services.GetServices<IStreamPipelineBehavior<TMessage, TResult>>();
}
