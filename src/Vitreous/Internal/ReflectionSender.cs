using System.Collections.Concurrent;
using System.Reflection;
using Vitreous.Abstractions;

namespace Vitreous.Internal;

/// <summary>
/// Reflection-based <see cref="ISender"/> used when the source generator is not present.
/// The source generator will emit a concrete <c>VitreousDispatcher</c> that replaces this.
/// </summary>
/// <remarks>
/// Reflection is used only once per message type to build a strongly-typed dispatch
/// delegate, which is then cached. After the first call for a given message type the
/// dispatch path is identical to the source-generated one: handlers and pipeline behaviors
/// are resolved by the <em>concrete</em> message type, so behaviors constrained on
/// <c>TMessage</c> and behaviors registered for a specific message type both work.
/// </remarks>
internal sealed class ReflectionSender : ISender
{
    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Static;

    private static readonly MethodInfo _executeCommand =
        typeof(ReflectionSender).GetMethod(nameof(ExecuteCommand), Flags)!;
    private static readonly MethodInfo _executeVoidCommand =
        typeof(ReflectionSender).GetMethod(nameof(ExecuteVoidCommand), Flags)!;
    private static readonly MethodInfo _executeQuery =
        typeof(ReflectionSender).GetMethod(nameof(ExecuteQuery), Flags)!;
    private static readonly MethodInfo _executeStream =
        typeof(ReflectionSender).GetMethod(nameof(ExecuteStream), Flags)!;

    private readonly IServiceProvider _services;

    private enum DispatchKind { Command, VoidCommand, Query, Stream }

    // Keyed by (concrete message type, result type, dispatch kind). The result type is part of
    // the key so a message that implements several ICommand<T>/IQuery<T> interfaces still works,
    // and the kind keeps a type that is both ICommand<T> and IQuery<T> from reusing the wrong
    // (differently typed) invoker.
    private readonly ConcurrentDictionary<(Type Message, Type Result, DispatchKind Kind), Delegate> _invokers = new();

    public ReflectionSender(IServiceProvider services) => _services = services;

    public ValueTask<Result<TResult>> Send<TResult>(
        ICommand<TResult> command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var invoker = (Func<IServiceProvider, ICommand<TResult>, CancellationToken, ValueTask<Result<TResult>>>)
            _invokers.GetOrAdd((command.GetType(), typeof(TResult), DispatchKind.Command), static key =>
                _executeCommand
                    .MakeGenericMethod(key.Message, key.Result)
                    .CreateDelegate<Func<IServiceProvider, ICommand<TResult>, CancellationToken, ValueTask<Result<TResult>>>>());

        return DispatchTelemetry.IsEnabled
            ? DispatchTelemetry.SendCommand(invoker, _services, command, ct)
            : invoker(_services, command, ct);
    }

    public ValueTask<Result> Send(ICommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var invoker = (Func<IServiceProvider, ICommand, CancellationToken, ValueTask<Result>>)
            _invokers.GetOrAdd((command.GetType(), typeof(Unit), DispatchKind.VoidCommand), static key =>
                _executeVoidCommand
                    .MakeGenericMethod(key.Message)
                    .CreateDelegate<Func<IServiceProvider, ICommand, CancellationToken, ValueTask<Result>>>());

        return DispatchTelemetry.IsEnabled
            ? DispatchTelemetry.SendCommand(invoker, _services, command, ct)
            : invoker(_services, command, ct);
    }

    public ValueTask<Result<TResult>> Send<TResult>(
        IQuery<TResult> query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var invoker = (Func<IServiceProvider, IQuery<TResult>, CancellationToken, ValueTask<Result<TResult>>>)
            _invokers.GetOrAdd((query.GetType(), typeof(TResult), DispatchKind.Query), static key =>
                _executeQuery
                    .MakeGenericMethod(key.Message, key.Result)
                    .CreateDelegate<Func<IServiceProvider, IQuery<TResult>, CancellationToken, ValueTask<Result<TResult>>>>());

        return DispatchTelemetry.IsEnabled
            ? DispatchTelemetry.SendQuery(invoker, _services, query, ct)
            : invoker(_services, query, ct);
    }

    public ValueTask<Result<IAsyncEnumerable<TResult>>> CreateStream<TResult>(
        IStreamQuery<TResult> query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var invoker = (Func<IServiceProvider, IStreamQuery<TResult>, CancellationToken, ValueTask<Result<IAsyncEnumerable<TResult>>>>)
            _invokers.GetOrAdd((query.GetType(), typeof(TResult), DispatchKind.Stream), static key =>
                _executeStream
                    .MakeGenericMethod(key.Message, typeof(TResult))
                    .CreateDelegate<Func<IServiceProvider, IStreamQuery<TResult>, CancellationToken, ValueTask<Result<IAsyncEnumerable<TResult>>>>>());

        return DispatchTelemetry.IsEnabled
            ? DispatchTelemetry.CreateStream(invoker, _services, query, ct)
            : invoker(_services, query, ct);
    }

    // -------------------------------------------------------------------------
    // Strongly-typed dispatch (instantiated once per message type via reflection)
    // -------------------------------------------------------------------------

    private static ValueTask<Result<TResult>> ExecuteCommand<TCommand, TResult>(
        IServiceProvider services, ICommand<TResult> command, CancellationToken ct)
        where TCommand : ICommand<TResult>
    {
        var handler = services.GetService<ICommandHandler<TCommand, TResult>>()
            ?? throw NoHandler("command", typeof(TCommand), $"ICommandHandler<{typeof(TCommand).Name}, {typeof(TResult).Name}>");

        var behaviors = services.GetServices<IPipelineBehavior<TCommand, TResult>>();
        // No behaviors: call the handler directly, without allocating a delegate for it.
        if (behaviors is ICollection<IPipelineBehavior<TCommand, TResult>> { Count: 0 })
            return handler.Handle((TCommand)command, ct);
        MessageHandlerDelegate<TCommand, TResult> terminal = handler.Handle;
        return PipelineExecutor.Execute((TCommand)command, terminal, behaviors, ct);
    }

    private static ValueTask<Result> ExecuteVoidCommand<TCommand>(
        IServiceProvider services, ICommand command, CancellationToken ct)
        where TCommand : ICommand
    {
        var handler = services.GetService<ICommandHandler<TCommand>>()
            ?? throw NoHandler("command", typeof(TCommand), $"ICommandHandler<{typeof(TCommand).Name}>");

        var behaviors = services.GetServices<IPipelineBehavior<TCommand, Unit>>();
        // No behaviors: call the handler directly, without allocating a delegate for it.
        if (behaviors is ICollection<IPipelineBehavior<TCommand, Unit>> { Count: 0 })
            return handler.Handle((TCommand)command, ct);
        CommandHandlerDelegate<TCommand> terminal = handler.Handle;
        return PipelineExecutor.ExecuteVoid((TCommand)command, terminal, behaviors, ct);
    }

    private static ValueTask<Result<TResult>> ExecuteQuery<TQuery, TResult>(
        IServiceProvider services, IQuery<TResult> query, CancellationToken ct)
        where TQuery : IQuery<TResult>
    {
        var handler = services.GetService<IQueryHandler<TQuery, TResult>>()
            ?? throw NoHandler("query", typeof(TQuery), $"IQueryHandler<{typeof(TQuery).Name}, {typeof(TResult).Name}>");

        var behaviors = services.GetServices<IPipelineBehavior<TQuery, TResult>>();
        // No behaviors: call the handler directly, without allocating a delegate for it.
        if (behaviors is ICollection<IPipelineBehavior<TQuery, TResult>> { Count: 0 })
            return handler.Handle((TQuery)query, ct);
        MessageHandlerDelegate<TQuery, TResult> terminal = handler.Handle;
        return PipelineExecutor.Execute((TQuery)query, terminal, behaviors, ct);
    }

    private static ValueTask<Result<IAsyncEnumerable<TResult>>> ExecuteStream<TQuery, TResult>(
        IServiceProvider services, IStreamQuery<TResult> query, CancellationToken ct)
        where TQuery : IStreamQuery<TResult>
    {
        var handler = services.GetService<IStreamQueryHandler<TQuery, TResult>>()
            ?? throw NoHandler("stream query", typeof(TQuery), $"IStreamQueryHandler<{typeof(TQuery).Name}, {typeof(TResult).Name}>");

        var behaviors = services.GetServices<IStreamPipelineBehavior<TQuery, TResult>>();
        // No behaviors: call the handler directly, without allocating a delegate for it.
        if (behaviors is ICollection<IStreamPipelineBehavior<TQuery, TResult>> { Count: 0 })
            return handler.Handle((TQuery)query, ct);
        StreamQueryHandlerDelegate<TQuery, TResult> terminal = handler.Handle;
        return PipelineExecutor.ExecuteStream((TQuery)query, terminal, behaviors, ct);
    }

    private static InvalidOperationException NoHandler(string kind, Type messageType, string expectedHandler) =>
        new(
            $"No handler registered for {kind} '{messageType.Name}'. " +
            $"Implement '{expectedHandler}' and make sure it is registered: either install " +
            "'Vitreous.SourceGeneration' on the entry-point project and call AddVitreousHandlers(), " +
            $"or pass the handler's assembly explicitly: AddVitreousHandlers(typeof({messageType.Name}).Assembly).");
}
