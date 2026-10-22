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
        RuleLevelCascadeMode = CascadeMode.Continue;

        RuleFor(request => request.SchemeOrgId)
            .NotEmpty()
            .Must(BeAnObjectId)
            .WithMessage("schemeOrgId must be an ObjectId.");

        RuleFor(request => request.MemberOrgId)
            .NotEmpty()
            .Must(BeAnObjectId)
            .WithMessage("memberOrgId must be an ObjectId.");

        RuleFor(request => request.MemberName).NotEmpty();

        RuleFor(request => request.MemberBprn)
            .NotEmpty()
            .Matches(BprnPattern())
            .WithMessage("memberBprn must match BPRN-{EA|NRW|SEPA|NIEA}-{year}-{6 digits}.");

        RuleFor(request => request)
            .Must(YearInBprnMatchesCompliancePeriod)
            .When(request => BprnPattern().IsMatch(request.MemberBprn ?? string.Empty))
            .WithName("MemberBprn")
            .WithMessage("memberBprn must be the registration for compliancePeriodYear.");

        RuleFor(request => request.SchemeMembershipRef).NotEmpty();

        RuleFor(request => request.CompliancePeriodYear)
            .InclusiveBetween(1000, 9999)
            .WithMessage("compliancePeriodYear must be a calendar year.");

        RuleFor(request => request.CompliancePeriodQuarter).NotEmpty();

        RuleFor(request => request.DataBasis).NotEmpty();

        RuleFor(request => request.TonnageEntries).NotEmpty();

        RuleFor(request => request.TonnageEntries)
            .Must(ClassificationsAreUnique)
            .When(request => request.TonnageEntries is { Count: > 0 })
            .WithMessage("Each classification can only be reported once in a submission.");

        RuleForEach(request => request.TonnageEntries).ChildRules(entry =>
        {
            entry.ClassLevelCascadeMode = CascadeMode.Continue;
            entry.RuleLevelCascadeMode = CascadeMode.Continue;

            entry.RuleFor(item => item.Tonnes)
                .NotEmpty()
                .Matches(@"^\d+\.\d{3}$")
                .WithMessage("tonnes must be a string decimal with exactly 3 digits after the decimal point.");

            entry.RuleFor(item => item.Classification).NotNull();
            entry.RuleFor(item => item.Classification.BatteryCategory)
                .NotEmpty()
                .When(item => item.Classification is not null);
            entry.RuleFor(item => item.Classification.Chemistry)
                .NotEmpty()
                .When(item => item.Classification is not null);
        });
    }

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
