namespace VitreousClean._1.Cli.Common;

/// <summary>
/// Where the app writes: results to <see cref="Out"/>, errors to <see cref="Error"/>. The tests
/// replace it to read what was written.
/// </summary>
public sealed record CliOutput(TextWriter Out, TextWriter Error);
