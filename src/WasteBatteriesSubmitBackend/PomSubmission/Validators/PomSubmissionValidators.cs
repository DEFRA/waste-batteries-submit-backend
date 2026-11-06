using FluentValidation;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;

namespace WasteBatteriesSubmitBackend.PomSubmission.Validators;

// The submission body and the collection filter each have a validator.
public sealed class PomSubmissionValidators(
    IValidator<PomSubmissionRequest> request,
    IValidator<PomSubmissionFilterRequest> filter)
{
    public IValidator<PomSubmissionRequest> Request { get; } = request;

    public IValidator<PomSubmissionFilterRequest> Filter { get; } = filter;
}
