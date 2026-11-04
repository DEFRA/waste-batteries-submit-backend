using FluentValidation;
using FluentValidation.Results;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Utils.Validation;

public static class ValidationAssert
{
    public static async Task AssertOnly<T>(
        IValidator<T> validator,
        T instance,
        string propertyName,
        string message)
    {
        var result = await validator.ValidateAsync(instance, TestContext.Current.CancellationToken);
        var error = Assert.Single(result.Errors);
        Assert.Equal(propertyName, error.PropertyName);
        Assert.Equal(message, error.ErrorMessage);
    }

    public static List<(string PropertyName, string ErrorMessage)> Messages(ValidationResult result) =>
        [.. result.Errors
            .Select(error => (error.PropertyName, error.ErrorMessage))
            .OrderBy(error => error.PropertyName, StringComparer.Ordinal)
            .ThenBy(error => error.ErrorMessage, StringComparer.Ordinal)];
}
