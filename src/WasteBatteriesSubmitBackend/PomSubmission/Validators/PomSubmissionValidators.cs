using FluentValidation;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;

namespace WasteBatteriesSubmitBackend.PomSubmission.Validators;

// Both request validators are one dependency of the submission service.
public sealed class PomSubmissionValidators(
    IValidator<PomSubmissionRequest> request,
    IValidator<PomSubmissionListRequest> list)
{
    public IValidator<PomSubmissionRequest> Request { get; } = request;

    public IValidator<PomSubmissionListRequest> List { get; } = list;
}
