namespace Vitreous.Abstractions;

/// <summary>
/// Marker interface for a command that mutates state and returns a value of type <typeparamref name="TResult"/>.
/// Handled by a single <see cref="ICommandHandler{TCommand,TResult}"/>.
/// </summary>
/// <typeparam name="TResult">The type of the value returned on success.</typeparam>
public interface ICommand<TResult> { }

/// <summary>
/// Marker interface for a command that mutates state and does not return a meaningful value.
/// Handled by a single <see cref="ICommandHandler{TCommand}"/>.
/// </summary>
public interface ICommand { }
