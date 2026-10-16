using FluentValidation;

namespace WasteBatteriesSubmitBackend.ExampleData.Validators;

public sealed record ExampleDataUser(string? UserId);

public sealed class ExampleDataUserValidator : AbstractValidator<ExampleDataUser>
{
    public ExampleDataUserValidator()
    {
        RuleFor(x => x.UserId)
            .Must(userId => !string.IsNullOrWhiteSpace(userId))
            .OverridePropertyName("userId")
            .WithMessage("Authenticated user id must not be empty");
    }
}
