using Vitreous;

namespace VitreousConsole._1.Common;

/// <summary>What the process returns to the shell.</summary>
public static class ExitCodes
{
    public const int Success   = 0;
    public const int Failure   = 1;
    public const int Usage     = 2;   // the arguments are wrong: an unknown verb or invalid input
    public const int Cancelled = 130; // 128 + SIGINT, what shells expect after Ctrl+C
}

/// <summary>
/// Turns a <see cref="Result"/> into an exit code. Handlers know nothing about the terminal: they
/// return an <see cref="Error"/>, and its <see cref="Error.Code"/> picks the exit code here.
/// </summary>
public static class ResultCliExtensions
{
    public static int ToExitCode<T>(this Result<T> result, CliOutput output, Action<T> onSuccess)
    {
        if (result.IsFailure)
            return result.Error.ToExitCode(output);

        onSuccess(result.Value);
        return ExitCodes.Success;
    }

    public static int ToExitCode(this Result result, CliOutput output) =>
        result.IsSuccess ? ExitCodes.Success : result.Error.ToExitCode(output);

    public static int ToExitCode(this Error error, CliOutput output)
    {
        // Validation errors carry their messages per property: print each one.
        if (error.Extensions?.GetValueOrDefault("failures") is IReadOnlyDictionary<string, string[]> failures)
        {
            foreach (var message in failures.Values.SelectMany(static m => m))
                output.Error.WriteLine($"error: {message}");
        }
        else
        {
            output.Error.WriteLine($"error: {error.Message}");
        }

        return error.Code == "VALIDATION" ? ExitCodes.Usage : ExitCodes.Failure;
    }
}
