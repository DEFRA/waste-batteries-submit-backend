using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using WasteBatteriesSubmitBackend.Endpoints;

namespace WasteBatteriesSubmitBackend.Utils.Logging;

public sealed class ExceptionLoggingHandler(
    ILogger<ExceptionLoggingHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is ValidationException validationException)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.RequestValidationFailed(ValidationErrors(validationException));
            }
            await ValidationExceptionResults.ToProblem(validationException).ExecuteAsync(httpContext);
            return true;
        }

        logger.UnhandledException(exception);

        var (statusCode, title, detail) = exception switch
        {
            BadHttpRequestException badRequest => (badRequest.StatusCode, "Bad request", badRequest.Message),
            _ => (
                StatusCodes.Status500InternalServerError,
                "An error occurred while processing your request.",
                null
            ),
        };

        httpContext.Response.StatusCode = statusCode;

        return await problemDetailsService.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = new ProblemDetails
                {
                    Title = title,
                    Detail = detail,
                    Status = statusCode,
                },
            }
        );
    }

    private static string ValidationErrors(ValidationException exception)
    {
        var errors = exception.Errors
            .Select(error => $"{error.PropertyName}: {error.ErrorMessage}")
            .ToArray();

        return errors.Length == 0 ? exception.Message : string.Join("; ", errors);
    }
}
