using Vitreous;

namespace VitreousClean._1.Api.Common;

/// <summary>
/// Turns a <see cref="Result"/> into an HTTP response. Handlers know nothing about HTTP: they
/// return an <see cref="Error"/>, and its <see cref="Error.Code"/> picks the status code here.
/// </summary>
public static class ResultHttpExtensions
{
    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult> onSuccess) =>
        result.Match(onSuccess, ToProblem);

    public static IResult ToHttpResult(this Result result, Func<IResult> onSuccess) =>
        result.Match(onSuccess, ToProblem);

    public static IResult ToProblem(this Error error)
    {
        // Validation errors carry their messages per property, which ValidationProblem returns as "errors".
        if (error.Extensions?.GetValueOrDefault("failures") is IReadOnlyDictionary<string, string[]> failures)
            return Results.ValidationProblem(failures.ToDictionary(), detail: error.Message);

        return Results.Problem(
            title:      error.Code,
            detail:     error.Message,
            statusCode: error.Code switch
            {
                "VALIDATION"   => StatusCodes.Status400BadRequest,
                "NOT_FOUND"    => StatusCodes.Status404NotFound,
                "UNAUTHORIZED" => StatusCodes.Status401Unauthorized,
                "CONFLICT"     => StatusCodes.Status409Conflict,
                _              => StatusCodes.Status500InternalServerError,
            });
    }
}
