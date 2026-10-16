using FluentValidation;
using WasteBatteriesSubmitBackend.ExampleData.Models;

namespace WasteBatteriesSubmitBackend.ExampleData.Validators;

public sealed class CreateExampleDataRequestValidator : AbstractValidator<CreateExampleDataRequest>
{
    public CreateExampleDataRequestValidator()
    {
        RuleFor(x => x.ExampleText).NotEmpty().MaximumLength(255);
    }
}
