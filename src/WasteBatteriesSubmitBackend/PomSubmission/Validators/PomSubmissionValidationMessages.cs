namespace WasteBatteriesSubmitBackend.PomSubmission.Validators;

// Posted POM submission validation text. {PropertyName} is filled in by FluentValidation.
public static class PomSubmissionValidationMessages
{
    public const string Required = "'{PropertyName}' must not be empty.";

    public const string SchemeOrgIdObjectId = "schemeOrgId must be an ObjectId.";

    public const string MemberOrgIdObjectId = "memberOrgId must be an ObjectId.";

    public const string MemberBprnFormat = "memberBprn must match BPRN-{EA|NRW|SEPA|NIEA}-{year}-{6 digits}.";

    public const string MemberBprnYear = "memberBprn must be the registration for compliancePeriodYear.";

    public const string CompliancePeriodYearRange = "compliancePeriodYear must be a calendar year.";

    public const string TonnesFormat = "tonnes must be a string decimal with exactly 3 digits after the decimal point.";

    public const string TonnageEntryRequired = "A tonnage entry must not be empty.";

    public const string ClassificationRepeated = "Each classification can only be reported once in a submission.";

    public const string CurrentSubmissionExists =
        "A current submission already exists for this scheme, member, year and quarter.";
}
