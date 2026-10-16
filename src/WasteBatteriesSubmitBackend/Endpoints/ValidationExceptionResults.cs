using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace WasteBatteriesSubmitBackend.Endpoints;

internal static class ValidationExceptionResults
{
    public static ValidationProblem ToProblem(ValidationException exception)
    {
        var errors = exception.Errors
            .GroupBy(error => error.PropertyName)
            .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray());

        return TypedResults.ValidationProblem(errors);
    }
}
