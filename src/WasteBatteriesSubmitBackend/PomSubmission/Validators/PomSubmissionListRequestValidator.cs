using FluentValidation;
using MongoDB.Bson;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;

namespace WasteBatteriesSubmitBackend.PomSubmission.Validators;

// Shape of a collection read. At least one business filter is required. isCurrent does not count.
public sealed class PomSubmissionListRequestValidator : AbstractValidator<PomSubmissionListRequest>
{
    public PomSubmissionListRequestValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Continue;
        RuleLevelCascadeMode = CascadeMode.Continue;

        RuleFor(request => request.SchemeOrgId)
            .Must(BeAnObjectId)
            .When(request => request.SchemeOrgId is not null)
            .WithMessage(PomSubmissionValidationMessages.SchemeOrgIdObjectId);

        RuleFor(request => request.MemberOrgId)
            .Must(BeAnObjectId)
            .When(request => request.MemberOrgId is not null)
            .WithMessage(PomSubmissionValidationMessages.MemberOrgIdObjectId);

        RuleForEach(request => request.CompliancePeriodYears)
            .InclusiveBetween(CalendarYear.Minimum, CalendarYear.Maximum)
            .WithMessage(PomSubmissionValidationMessages.CompliancePeriodYearsRange);

        RuleForEach(request => request.CompliancePeriodQuarters)
            .Must(quarter => !string.IsNullOrWhiteSpace(quarter))
            .WithMessage(PomSubmissionValidationMessages.Required);

        RuleFor(request => request.PageNumber)
            .GreaterThanOrEqualTo(1)
            .When(request => request.PageNumber is not null)
            .WithMessage(PomSubmissionValidationMessages.PageNumberMinimum);

        RuleFor(request => request.PageSize)
            .GreaterThanOrEqualTo(1)
            .When(request => request.PageSize is not null)
            .WithMessage(PomSubmissionValidationMessages.PageSizeMinimum);

        RuleFor(request => request.PageNumber)
            .Must(_ => false)
            .When(request => request.PageNumber is not null && request.PageSize is null)
            .WithMessage(PomSubmissionValidationMessages.PageSizeRequired);

        RuleFor(request => request)
            .Must(HasBusinessFilter)
            .WithName("Filters")
            .WithMessage(PomSubmissionValidationMessages.FilterRequired);
    }

    private static bool HasBusinessFilter(PomSubmissionListRequest request) =>
        request.SchemeOrgId is not null
        || request.MemberOrgId is not null
        || request.CompliancePeriodYears is { Length: > 0 }
        || request.CompliancePeriodQuarters is { Length: > 0 };

    private static bool BeAnObjectId(string? value) => ObjectId.TryParse(value, out _);
}
