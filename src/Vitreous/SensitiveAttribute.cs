namespace Vitreous;

/// <summary>
/// Marks a property (or a record's positional parameter) as holding sensitive data.
/// Diagnostic tooling such as <c>Vitreous.DevUI</c> masks its value instead of recording it.
/// </summary>
/// <example>
/// <code>
/// public sealed record LoginCommand(string UserName, [Sensitive] string Password) : ICommand&lt;Guid&gt;;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, Inherited = true)]
public sealed class SensitiveAttribute : Attribute
{
}
