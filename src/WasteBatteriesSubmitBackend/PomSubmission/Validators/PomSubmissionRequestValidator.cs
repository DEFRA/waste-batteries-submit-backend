using System.Globalization;
using FluentValidation;
using MongoDB.Bson;
using WasteBatteriesSubmitBackend.PomSubmission.Models;

namespace WasteBatteriesSubmitBackend.PomSubmission.Validators;

// Shape of a posted submission. Whether a current submission already exists is a create-only
// decision and lives in the service, which reads that submission once.
public sealed class PomSubmissionRequestValidator : AbstractValidator<PomSubmissionRequest>
{
    public PomSubmissionRequestValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Continue;
        // Stop after the first failure on a field. A missing value must not also report a format error.
        RuleLevelCascadeMode = CascadeMode.Stop;

        Required(RuleFor(request => request.SchemeOrgId))
            .Must(BeAnObjectId)
            .WithMessage(PomSubmissionValidationMessages.SchemeOrgIdObjectId);

        Required(RuleFor(request => request.MemberOrgId))
            .Must(BeAnObjectId)
            .WithMessage(PomSubmissionValidationMessages.MemberOrgIdObjectId);

        Required(RuleFor(request => request.MemberName));

        Required(RuleFor(request => request.MemberBprn))
            .Matches(PomSubmissionPatterns.BprnPattern())
            .WithMessage(PomSubmissionValidationMessages.MemberBprnFormat);

        RuleFor(request => request)
            .Must(YearInBprnMatchesCompliancePeriod)
            .When(request => PomSubmissionPatterns.BprnPattern().IsMatch(request.MemberBprn ?? string.Empty)
                && CalendarYear.Contains(request.CompliancePeriodYear))
            .WithName("MemberBprn")
            .WithMessage(PomSubmissionValidationMessages.MemberBprnYear);

        Required(RuleFor(request => request.SchemeMembershipRef));

        RuleFor(request => request.CompliancePeriodYear)
            .InclusiveBetween(CalendarYear.Minimum, CalendarYear.Maximum)
            .WithMessage(PomSubmissionValidationMessages.CompliancePeriodYearRange);

        Required(RuleFor(request => request.CompliancePeriodQuarter));

        Required(RuleFor(request => request.DataBasis));

        RuleFor(request => request.TonnageEntries)
            .NotEmpty()
            .WithMessage(PomSubmissionValidationMessages.Required);

        RuleFor(request => request.TonnageEntries)
            .Must(ClassificationsAreUnique)
            .When(request => request.TonnageEntries is { Count: > 0 })
            .WithMessage(PomSubmissionValidationMessages.ClassificationRepeated);

        // A null element has no fields to check. Where skips it here so the child rules do not throw.
        RuleForEach(request => request.TonnageEntries)
            .Where(entry => entry is null)
            .Must(_ => false)
            .WithMessage(PomSubmissionValidationMessages.TonnageEntryRequired);

        // Continue across entries. Rule-level Stop would otherwise report only the first failing entry.
        RuleForEach(request => request.TonnageEntries)
            .Where(entry => entry is not null)
            .Cascade(CascadeMode.Continue)
            .ChildRules(entry =>
            {
                entry.ClassLevelCascadeMode = CascadeMode.Continue;
                entry.RuleLevelCascadeMode = CascadeMode.Stop;

                Required(entry.RuleFor(item => item.Tonnes))
                    .Matches(PomSubmissionPatterns.TonnesPattern())
                    .WithMessage(PomSubmissionValidationMessages.TonnesFormat);

                entry.RuleFor(item => item.Classification)
                    .NotNull()
                    .WithMessage(PomSubmissionValidationMessages.Required)
                    .DependentRules(() =>
                    {
                        Required(entry.RuleFor(item => item.Classification.BatteryCategory));
                        Required(entry.RuleFor(item => item.Classification.Chemistry));
                    });
            });
    }

    private static IRuleBuilderOptions<T, string> Required<T>(IRuleBuilder<T, string> rule) =>
        rule.Must(value => !string.IsNullOrWhiteSpace(value))
            .WithMessage(PomSubmissionValidationMessages.Required);

    private static bool BeAnObjectId(string? value) => ObjectId.TryParse(value, out _);

    private static bool YearInBprnMatchesCompliancePeriod(PomSubmissionRequest request)
    {
        var match = PomSubmissionPatterns.BprnPattern().Match(request.MemberBprn ?? string.Empty);
        return match.Success
            && int.Parse(match.Groups["year"].Value, NumberStyles.None, CultureInfo.InvariantCulture) == request.CompliancePeriodYear;
    }

    private static bool ClassificationsAreUnique(IList<TonnageEntryBase> entries)
    {
        var seen = new HashSet<string>();
        foreach (var entry in entries)
        {
            if (entry?.Classification is null)
            {
                continue;
            }

            var classification = entry.Classification;
            var key = string.Join(
                '\u001f',
                classification.BatteryCategory ?? string.Empty,
                classification.Chemistry ?? string.Empty,
                classification.SubCategory ?? string.Empty,
                classification.CellType ?? string.Empty,
                classification.EquipmentState ?? string.Empty);
            if (!seen.Add(key))
            {
                return false;
            }
        }

        return true;
    }
}
