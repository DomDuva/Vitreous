namespace Vitreous.DevUI;

/// <summary>
/// Whether the developer UI is active in this application. Registered by
/// <c>AddVitreousDevUI()</c> and switched on by <c>UseVitreousDevUI()</c> or
/// <c>MapVitreousDevUI()</c> when <see cref="VitreousDevUIOptions.Enabled"/> allows it.
/// </summary>
/// <remarks>
/// The tracing behaviors check <see cref="IsActive"/> on every dispatch and pass straight
/// through while it is <see langword="false"/>, so registering the DevUI services costs
/// nothing when the UI is not mounted (for example in Production).
/// </remarks>
public sealed class DevUIState
{
    internal readonly object Gate = new();

    /// <summary><see langword="true"/> once the developer UI has been mounted and enabled.</summary>
    public bool IsActive { get; internal set; }

    internal bool Wired { get; set; }
}
