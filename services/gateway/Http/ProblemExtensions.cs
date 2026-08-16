using BidRisk.Contracts.Results;
using Microsoft.AspNetCore.Mvc;

using Error = BidRisk.Contracts.Results.Error;
using HttpResults = Microsoft.AspNetCore.Http.Results;
using IHttpResult = Microsoft.AspNetCore.Http.IResult;

namespace BidRisk.Gateway.Http;

public static class ProblemExtensions
{
    public static IHttpResult ToProblem(this List<Error> errors)
    {
        if (errors.Count == 0)
        {
            return HttpResults.Problem();
        }

        return errors.All(error => error.Type == ErrorType.Validation)
            ? ValidationProblem(errors)
            : Problem(errors[0]);
    }

    public static IHttpResult ToProblem(this Error error)
    {
        return Problem(error);
    }

    private static IHttpResult Problem(Error error)
    {
        var statusCode = error.Type switch
        {
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.Failure => StatusCodes.Status400BadRequest,
            ErrorType.Unexpected => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status500InternalServerError,
        };

        return HttpResults.Problem(
            statusCode: statusCode,
            title: GetTitle(error.Type),
            detail: error.Description,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = error.Code,
            });
    }

    private static string GetTitle(ErrorType type) => type switch
    {
        ErrorType.Conflict => "Conflict",
        ErrorType.Validation => "Validation Error",
        ErrorType.NotFound => "Not Found",
        ErrorType.Unauthorized => "Unauthorized",
        ErrorType.Forbidden => "Forbidden",
        ErrorType.Failure => "Bad Request",
        ErrorType.Unexpected => "Internal Server Error",
        _ => "An error occurred",
    };

    private static IHttpResult ValidationProblem(List<Error> errors)
    {
        var errorsDict = errors
            .GroupBy(e => e.Code)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.Description).ToArray());

        var problemDetails = new ValidationProblemDetails(errorsDict)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
        };

        return HttpResults.Json(
            problemDetails,
            statusCode: StatusCodes.Status400BadRequest,
            contentType: "application/problem+json");
    }
}
