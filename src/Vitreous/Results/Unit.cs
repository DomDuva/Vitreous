namespace Vitreous;

/// <summary>
/// Represents the absence of a meaningful value.
/// <para>
/// Used as the <c>TResult</c> type parameter for void commands so they participate in the same
/// <see cref="Abstractions.IPipelineBehavior{TMessage,TResult}"/> pipeline as commands that
/// return a value. Behaviors written against <c>IPipelineBehavior&lt;TMessage, TResult&gt;</c>
/// automatically apply to void commands when registered as open generics — no separate
/// <c>ICommandPipelineBehavior</c> implementation is needed.
/// </para>
/// </summary>
public readonly struct Unit
{
    /// <summary>The single value of this type.</summary>
    public static readonly Unit Value = default;

    /// <inheritdoc/>
    public override string ToString() => "()";
}
