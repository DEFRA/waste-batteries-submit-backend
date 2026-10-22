using System.Text.RegularExpressions;
using FluentValidation;
using MongoDB.Bson;
using WasteBatteriesSubmitBackend.PomSubmission.Models;

namespace WasteBatteriesSubmitBackend.PomSubmission.Validators;

// Shape of a posted submission. Whether a current submission already exists is a create-only
// decision and lives in the service, which reads that submission once.
public sealed partial class PomSubmissionRequestValidator : AbstractValidator<PomSubmissionRequest>
{
    public PomSubmissionRequestValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Continue;
        // Stop after the first failure on a field. A missing value must not also report a format error.
        RuleLevelCascadeMode = CascadeMode.Stop;

        Required(RuleFor(request => request.SchemeOrgId))
            .Must(BeAnObjectId)
            .WithMessage("schemeOrgId must be an ObjectId.");

        Required(RuleFor(request => request.MemberOrgId))
            .Must(BeAnObjectId)
            .WithMessage("memberOrgId must be an ObjectId.");

        Required(RuleFor(request => request.MemberName));

        Required(RuleFor(request => request.MemberBprn))
            .Matches(BprnPattern())
            .WithMessage("memberBprn must match BPRN-{EA|NRW|SEPA|NIEA}-{year}-{6 digits}.");

        RuleFor(request => request)
            .Must(YearInBprnMatchesCompliancePeriod)
            .When(request => BprnPattern().IsMatch(request.MemberBprn ?? string.Empty)
                && request.CompliancePeriodYear is >= 1000 and <= 9999)
            .WithName("MemberBprn")
            .WithMessage("memberBprn must be the registration for compliancePeriodYear.");

        Required(RuleFor(request => request.SchemeMembershipRef));

        RuleFor(request => request.CompliancePeriodYear)
            .InclusiveBetween(1000, 9999)
            .WithMessage("compliancePeriodYear must be a calendar year.");

        Required(RuleFor(request => request.CompliancePeriodQuarter));

        Required(RuleFor(request => request.DataBasis));

        RuleFor(request => request.TonnageEntries).NotEmpty();

        RuleFor(request => request.TonnageEntries)
            .Must(ClassificationsAreUnique)
            .When(request => request.TonnageEntries is { Count: > 0 })
            .WithMessage("Each classification can only be reported once in a submission.");

        // Continue across entries. Rule-level Stop would otherwise report only the first failing entry.
        RuleForEach(request => request.TonnageEntries).Cascade(CascadeMode.Continue).ChildRules(entry =>
        {
            entry.ClassLevelCascadeMode = CascadeMode.Continue;
            entry.RuleLevelCascadeMode = CascadeMode.Stop;

            Required(entry.RuleFor(item => item.Tonnes))
                .Matches(@"^\d+\.\d{3}$")
                .WithMessage("tonnes must be a string decimal with exactly 3 digits after the decimal point.");

            entry.RuleFor(item => item.Classification)
                .NotNull()
                .DependentRules(() =>
                {
                    Required(entry.RuleFor(item => item.Classification.BatteryCategory));
                    Required(entry.RuleFor(item => item.Classification.Chemistry));
                });
        });
    }

    private static IRuleBuilderOptions<T, string> Required<T>(IRuleBuilder<T, string> rule) =>
        rule.Must(value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("'{PropertyName}' must not be empty.");

    private static bool BeAnObjectId(string? value) => ObjectId.TryParse(value, out _);

    private static bool YearInBprnMatchesCompliancePeriod(PomSubmissionRequest request)
    {
        var match = BprnPattern().Match(request.MemberBprn ?? string.Empty);
        return match.Success && int.Parse(match.Groups["year"].Value) == request.CompliancePeriodYear;
    }

    private static bool ClassificationsAreUnique(IList<TonnageEntryBase>? entries)
    {
        if (entries is null)
        {
            return true;
        }

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
                classification.BatteryCategory,
                classification.Chemistry,
                classification.SubCategory,
                classification.CellType,
                classification.EquipmentState);
            if (!seen.Add(key))
            {
                return false;
            }
        }

        return true;
    }

    [GeneratedRegex("^BPRN-(EA|NRW|SEPA|NIEA)-(?<year>\\d{4})-\\d{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex BprnPattern();
}
