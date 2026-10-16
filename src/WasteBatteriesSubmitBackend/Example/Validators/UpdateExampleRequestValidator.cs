using FluentValidation;
using WasteBatteriesSubmitBackend.Example.Models;

namespace WasteBatteriesSubmitBackend.Example.Validators;

public sealed class UpdateExampleRequestValidator : AbstractValidator<UpdateExampleRequest>
{
    public UpdateExampleRequestValidator()
    {
        RuleFor(x => x.Value).NotEmpty();
        RuleFor(x => x.Counter).GreaterThanOrEqualTo(0);
    }
}
