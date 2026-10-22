using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Validators;
using WasteBatteriesSubmitBackend.Tests.Unit.Utils.Validation;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Validators;

public class PomSubmissionRequestValidatorTests
{
    private const string SchemeOrgIdRequired = "'Scheme Org Id' must not be empty.";
    private const string SchemeOrgIdObjectId = "schemeOrgId must be an ObjectId.";
    private const string MemberOrgIdRequired = "'Member Org Id' must not be empty.";
    private const string MemberOrgIdObjectId = "memberOrgId must be an ObjectId.";
    private const string MemberNameRequired = "'Member Name' must not be empty.";
    private const string MemberBprnRequired = "'Member Bprn' must not be empty.";
    private const string MemberBprnFormat = "memberBprn must match BPRN-{EA|NRW|SEPA|NIEA}-{year}-{6 digits}.";
    private const string MemberBprnYear = "memberBprn must be the registration for compliancePeriodYear.";
    private const string SchemeMembershipRefRequired = "'Scheme Membership Ref' must not be empty.";
    private const string CompliancePeriodYearRange = "compliancePeriodYear must be a calendar year.";
    private const string CompliancePeriodQuarterRequired = "'Compliance Period Quarter' must not be empty.";
    private const string DataBasisRequired = "'Data Basis' must not be empty.";
    private const string TonnageEntriesRequired = "'Tonnage Entries' must not be empty.";
    private const string ClassificationRepeated = "Each classification can only be reported once in a submission.";
    private const string TonnesRequired = "'Tonnes' must not be empty.";
    private const string TonnesFormat = "tonnes must be a string decimal with exactly 3 digits after the decimal point.";
    private const string ClassificationRequired = "'Classification' must not be empty.";
    private const string BatteryCategoryRequired = "'Classification Battery Category' must not be empty.";
    private const string ChemistryRequired = "'Classification Chemistry' must not be empty.";

    [Fact]
    public async Task ValidateAsync_WhenSubmissionIsComplete_ReturnsValid()
    {
        // Arrange
        var request = ValidRequest();

        // Act
        var result = await Validator().ValidateAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WhenRequestHasManyFailures_ReturnsEveryFailure()
    {
        // Arrange
        var request = ValidRequest() with
        {
            SchemeOrgId = "not-an-id",
            MemberOrgId = "",
            MemberName = "",
            MemberBprn = "not-a-bprn",
            SchemeMembershipRef = "   ",
            CompliancePeriodYear = 0,
            CompliancePeriodQuarter = "",
            DataBasis = "",
            TonnageEntries =
            [
                InvalidEntry(),
                InvalidEntry()
            ]
        };

        // Act
        var result = await Validator().ValidateAsync(request, TestContext.Current.CancellationToken);

        // Assert
        (string PropertyName, string ErrorMessage)[] expected =
        [
            ("SchemeOrgId", SchemeOrgIdObjectId),
            ("MemberOrgId", MemberOrgIdRequired),
            ("MemberName", MemberNameRequired),
            ("MemberBprn", MemberBprnFormat),
            ("SchemeMembershipRef", SchemeMembershipRefRequired),
            ("CompliancePeriodYear", CompliancePeriodYearRange),
            ("CompliancePeriodQuarter", CompliancePeriodQuarterRequired),
            ("DataBasis", DataBasisRequired),
            ("TonnageEntries", ClassificationRepeated),
            ("TonnageEntries[0].Tonnes", TonnesFormat),
            ("TonnageEntries[0].Classification.BatteryCategory", BatteryCategoryRequired),
            ("TonnageEntries[0].Classification.Chemistry", ChemistryRequired),
            ("TonnageEntries[1].Tonnes", TonnesFormat),
            ("TonnageEntries[1].Classification.BatteryCategory", BatteryCategoryRequired),
            ("TonnageEntries[1].Classification.Chemistry", ChemistryRequired)
        ];
        Assert.Equal(
            expected.OrderBy(error => error.PropertyName, StringComparer.Ordinal)
                .ThenBy(error => error.ErrorMessage, StringComparer.Ordinal),
            ValidationAssert.Messages(result));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenSchemeOrgIdIsMissing_ReturnsOnlyTheRequiredError(string? value)
    {
        await ValidationAssert.AssertOnly(
            Validator(),
            ValidRequest() with { SchemeOrgId = value! },
            "SchemeOrgId",
            SchemeOrgIdRequired);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenMemberOrgIdIsMissing_ReturnsOnlyTheRequiredError(string? value)
    {
        await ValidationAssert.AssertOnly(
            Validator(),
            ValidRequest() with { MemberOrgId = value! },
            "MemberOrgId",
            MemberOrgIdRequired);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenMemberNameIsMissing_ReturnsOnlyTheRequiredError(string? value)
    {
        await ValidationAssert.AssertOnly(
            Validator(),
            ValidRequest() with { MemberName = value! },
            "MemberName",
            MemberNameRequired);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenMemberBprnIsMissing_ReturnsOnlyTheRequiredError(string? value)
    {
        await ValidationAssert.AssertOnly(
            Validator(),
            ValidRequest() with { MemberBprn = value! },
            "MemberBprn",
            MemberBprnRequired);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenSchemeMembershipRefIsMissing_ReturnsOnlyTheRequiredError(string? value)
    {
        await ValidationAssert.AssertOnly(
            Validator(),
            ValidRequest() with { SchemeMembershipRef = value! },
            "SchemeMembershipRef",
            SchemeMembershipRefRequired);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenCompliancePeriodQuarterIsMissing_ReturnsOnlyTheRequiredError(string? value)
    {
        await ValidationAssert.AssertOnly(
            Validator(),
            ValidRequest() with { CompliancePeriodQuarter = value! },
            "CompliancePeriodQuarter",
            CompliancePeriodQuarterRequired);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenDataBasisIsMissing_ReturnsOnlyTheRequiredError(string? value)
    {
        await ValidationAssert.AssertOnly(
            Validator(),
            ValidRequest() with { DataBasis = value! },
            "DataBasis",
            DataBasisRequired);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenTonnesIsMissing_ReturnsOnlyTheRequiredError(string? value)
    {
        var request = WithEntry(ValidRequest().TonnageEntries[0] with { Tonnes = value! });

        await ValidationAssert.AssertOnly(
            Validator(),
            request,
            "TonnageEntries[0].Tonnes",
            TonnesRequired);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenBatteryCategoryIsMissing_ReturnsOnlyTheRequiredError(string? value)
    {
        var entry = ValidRequest().TonnageEntries[0];
        var request = WithEntry(entry with
        {
            Classification = entry.Classification with { BatteryCategory = value! }
        });

        await ValidationAssert.AssertOnly(
            Validator(),
            request,
            "TonnageEntries[0].Classification.BatteryCategory",
            BatteryCategoryRequired);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenChemistryIsMissing_ReturnsOnlyTheRequiredError(string? value)
    {
        var entry = ValidRequest().TonnageEntries[0];
        var request = WithEntry(entry with
        {
            Classification = entry.Classification with { Chemistry = value! }
        });

        await ValidationAssert.AssertOnly(
            Validator(),
            request,
            "TonnageEntries[0].Classification.Chemistry",
            ChemistryRequired);
    }

    [Fact]
    public async Task ValidateAsync_WhenSchemeOrgIdIsNotAnObjectId_ReturnsOnlyTheObjectIdError()
    {
        await ValidationAssert.AssertOnly(
            Validator(),
            ValidRequest() with { SchemeOrgId = "not-an-id" },
            "SchemeOrgId",
            SchemeOrgIdObjectId);
    }

    [Fact]
    public async Task ValidateAsync_WhenMemberOrgIdIsNotAnObjectId_ReturnsOnlyTheObjectIdError()
    {
        await ValidationAssert.AssertOnly(
            Validator(),
            ValidRequest() with { MemberOrgId = "not-an-id" },
            "MemberOrgId",
            MemberOrgIdObjectId);
    }

    [Fact]
    public async Task ValidateAsync_WhenMemberBprnFormatIsWrong_ReturnsOnlyTheFormatError()
    {
        await ValidationAssert.AssertOnly(
            Validator(),
            ValidRequest() with { MemberBprn = "not-a-bprn" },
            "MemberBprn",
            MemberBprnFormat);
    }

    [Fact]
    public async Task ValidateAsync_WhenBprnYearDiffers_ReturnsOnlyTheYearError()
    {
        await ValidationAssert.AssertOnly(
            Validator(),
            ValidRequest() with { MemberBprn = "BPRN-NRW-2025-000123" },
            "MemberBprn",
            MemberBprnYear);
    }

    [Theory]
    [InlineData(999)]
    [InlineData(10000)]
    public async Task ValidateAsync_WhenCompliancePeriodYearIsOutOfRange_ReturnsOnlyTheYearError(int year)
    {
        await ValidationAssert.AssertOnly(
            Validator(),
            ValidRequest() with { CompliancePeriodYear = year },
            "CompliancePeriodYear",
            CompliancePeriodYearRange);
    }

    [Fact]
    public async Task ValidateAsync_WhenTonnesFormatIsWrong_ReturnsOnlyTheFormatError()
    {
        var request = WithEntry(ValidRequest().TonnageEntries[0] with { Tonnes = "12.5" });

        await ValidationAssert.AssertOnly(
            Validator(),
            request,
            "TonnageEntries[0].Tonnes",
            TonnesFormat);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ValidateAsync_WhenTonnageEntriesAreMissing_ReturnsOnlyTheRequiredError(string? marker)
    {
        var request = ValidRequest() with
        {
            TonnageEntries = marker is null ? null! : []
        };

        await ValidationAssert.AssertOnly(
            Validator(),
            request,
            "TonnageEntries",
            TonnageEntriesRequired);
    }

    [Fact]
    public async Task ValidateAsync_WhenClassificationIsNull_ReturnsOnlyTheRequiredError()
    {
        var request = WithEntry(ValidRequest().TonnageEntries[0] with { Classification = null! });

        await ValidationAssert.AssertOnly(
            Validator(),
            request,
            "TonnageEntries[0].Classification",
            ClassificationRequired);
    }

    [Fact]
    public async Task ValidateAsync_WhenClassificationIsRepeated_ReturnsOnlyTheUniquenessError()
    {
        var request = ValidRequest();
        request = request with { TonnageEntries = [request.TonnageEntries[0], request.TonnageEntries[0]] };

        await ValidationAssert.AssertOnly(
            Validator(),
            request,
            "TonnageEntries",
            ClassificationRepeated);
    }

    private static PomSubmissionRequest WithEntry(TonnageEntryBase entry) =>
        ValidRequest() with { TonnageEntries = [entry] };

    private static PomSubmissionRequestValidator Validator() => new();

    private static TonnageEntryBase InvalidEntry() => new()
    {
        Classification = new BatteryClassification
        {
            BatteryCategory = "",
            Chemistry = ""
        },
        Tonnes = "12.5"
    };

    private static PomSubmissionRequest ValidRequest() => new()
    {
        SchemeOrgId = "674a1b2c3d4e5f6789012345",
        MemberOrgId = "674a1b2c3d4e5f6789012346",
        MemberName = "Northwind Batteries Ltd",
        MemberBprn = "BPRN-EA-2026-000123",
        SchemeMembershipRef = "SCH-00417",
        CompliancePeriodYear = 2026,
        CompliancePeriodQuarter = "Quarter 2",
        DataBasis = "Actual",
        TonnageEntries =
        [
            new TonnageEntryBase
            {
                Classification = new BatteryClassification
                {
                    BatteryCategory = "Portable",
                    Chemistry = "Alkaline",
                    SubCategory = null,
                    CellType = null,
                    EquipmentState = null
                },
                Tonnes = "12.500"
            }
        ]
    };
}
