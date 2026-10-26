using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;
using WasteBatteriesSubmitBackend.PomSubmission.Validators;
using WasteBatteriesSubmitBackend.Tests.Unit.Utils.Validation;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Validators;

public class PomSubmissionRequestValidatorTests
{
    // Required fields. Null and blank are separate: a missing value and a blank value
    // both fail the required rule, and neither also reports a format error.
    private const string SchemeOrgIdRequired = "'Scheme Org Id' must not be empty.";
    private const string MemberOrgIdRequired = "'Member Org Id' must not be empty.";
    private const string MemberNameRequired = "'Member Name' must not be empty.";
    private const string MemberBprnRequired = "'Member Bprn' must not be empty.";
    private const string SchemeMembershipRefRequired = "'Scheme Membership Ref' must not be empty.";
    private const string CompliancePeriodQuarterRequired = "'Compliance Period Quarter' must not be empty.";
    private const string DataBasisRequired = "'Data Basis' must not be empty.";
    private const string TonnageEntriesRequired = "'Tonnage Entries' must not be empty.";
    private const string ClassificationRequired = "'Classification' must not be empty.";
    private const string BatteryCategoryRequired = "'Classification Battery Category' must not be empty.";
    private const string ChemistryRequired = "'Classification Chemistry' must not be empty.";
    private const string TonnesRequired = "'Tonnes' must not be empty.";

    // Format. These run only when the required rule has passed.
    private const string SchemeOrgIdObjectId = "schemeOrgId must be an ObjectId.";
    private const string MemberOrgIdObjectId = "memberOrgId must be an ObjectId.";
    private const string MemberBprnFormat = "memberBprn must match BPRN-{EA|NRW|SEPA|NIEA}-{year}-{6 digits}.";
    private const string MemberBprnYear = "memberBprn must be the registration for compliancePeriodYear.";
    private const string CompliancePeriodYearRange = "compliancePeriodYear must be a calendar year.";
    private const string TonnesFormat = "tonnes must be a string decimal with exactly 3 digits after the decimal point.";
    private const string TonnageEntryRequired = "A tonnage entry must not be empty.";

    // Duplicate classification.
    private const string ClassificationRepeated = "Each classification can only be reported once in a submission.";

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
    public async Task ValidateAsync_WhenSchemeOrgIdIsNull_ReturnsOnlyTheRequiredError()
    {
        await AssertRequired("SchemeOrgId", request => request with { SchemeOrgId = null! }, SchemeOrgIdRequired);
    }

    [Fact]
    public async Task ValidateAsync_WhenMemberOrgIdIsNull_ReturnsOnlyTheRequiredError()
    {
        await AssertRequired("MemberOrgId", request => request with { MemberOrgId = null! }, MemberOrgIdRequired);
    }

    [Fact]
    public async Task ValidateAsync_WhenMemberNameIsNull_ReturnsOnlyTheRequiredError()
    {
        await AssertRequired("MemberName", request => request with { MemberName = null! }, MemberNameRequired);
    }

    [Fact]
    public async Task ValidateAsync_WhenMemberBprnIsNull_ReturnsOnlyTheRequiredError()
    {
        await AssertRequired("MemberBprn", request => request with { MemberBprn = null! }, MemberBprnRequired);
    }

    [Fact]
    public async Task ValidateAsync_WhenSchemeMembershipRefIsNull_ReturnsOnlyTheRequiredError()
    {
        await AssertRequired(
            "SchemeMembershipRef",
            request => request with { SchemeMembershipRef = null! },
            SchemeMembershipRefRequired);
    }

    [Fact]
    public async Task ValidateAsync_WhenCompliancePeriodQuarterIsNull_ReturnsOnlyTheRequiredError()
    {
        await AssertRequired(
            "CompliancePeriodQuarter",
            request => request with { CompliancePeriodQuarter = null! },
            CompliancePeriodQuarterRequired);
    }

    [Fact]
    public async Task ValidateAsync_WhenDataBasisIsNull_ReturnsOnlyTheRequiredError()
    {
        await AssertRequired("DataBasis", request => request with { DataBasis = null! }, DataBasisRequired);
    }

    [Fact]
    public async Task ValidateAsync_WhenTonnageEntriesAreNull_ReturnsOnlyTheRequiredError()
    {
        await AssertRequired(
            "TonnageEntries",
            request => request with { TonnageEntries = null! },
            TonnageEntriesRequired);
    }

    [Fact]
    public async Task ValidateAsync_WhenClassificationIsNull_ReturnsOnlyTheRequiredError()
    {
        await AssertRequired(
            "TonnageEntries[0].Classification",
            request => WithEntry(request.TonnageEntries[0] with { Classification = null! }),
            ClassificationRequired);
    }

    [Fact]
    public async Task ValidateAsync_WhenBatteryCategoryIsNull_ReturnsOnlyTheRequiredError()
    {
        await AssertRequired(
            "TonnageEntries[0].Classification.BatteryCategory",
            request => WithClassification(request, classification => classification with { BatteryCategory = null! }),
            BatteryCategoryRequired);
    }

    [Fact]
    public async Task ValidateAsync_WhenChemistryIsNull_ReturnsOnlyTheRequiredError()
    {
        await AssertRequired(
            "TonnageEntries[0].Classification.Chemistry",
            request => WithClassification(request, classification => classification with { Chemistry = null! }),
            ChemistryRequired);
    }

    [Fact]
    public async Task ValidateAsync_WhenTonnesIsNull_ReturnsOnlyTheRequiredError()
    {
        await AssertRequired(
            "TonnageEntries[0].Tonnes",
            request => WithEntry(request.TonnageEntries[0] with { Tonnes = null! }),
            TonnesRequired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenSchemeOrgIdIsBlank_ReturnsOnlyTheRequiredError(string value)
    {
        await AssertRequired("SchemeOrgId", request => request with { SchemeOrgId = value }, SchemeOrgIdRequired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenMemberOrgIdIsBlank_ReturnsOnlyTheRequiredError(string value)
    {
        await AssertRequired("MemberOrgId", request => request with { MemberOrgId = value }, MemberOrgIdRequired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenMemberNameIsBlank_ReturnsOnlyTheRequiredError(string value)
    {
        await AssertRequired("MemberName", request => request with { MemberName = value }, MemberNameRequired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenMemberBprnIsBlank_ReturnsOnlyTheRequiredError(string value)
    {
        await AssertRequired("MemberBprn", request => request with { MemberBprn = value }, MemberBprnRequired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenSchemeMembershipRefIsBlank_ReturnsOnlyTheRequiredError(string value)
    {
        await AssertRequired(
            "SchemeMembershipRef",
            request => request with { SchemeMembershipRef = value },
            SchemeMembershipRefRequired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenCompliancePeriodQuarterIsBlank_ReturnsOnlyTheRequiredError(string value)
    {
        await AssertRequired(
            "CompliancePeriodQuarter",
            request => request with { CompliancePeriodQuarter = value },
            CompliancePeriodQuarterRequired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenDataBasisIsBlank_ReturnsOnlyTheRequiredError(string value)
    {
        await AssertRequired("DataBasis", request => request with { DataBasis = value }, DataBasisRequired);
    }

    [Fact]
    public async Task ValidateAsync_WhenTonnageEntriesAreEmpty_ReturnsOnlyTheRequiredError()
    {
        await AssertRequired(
            "TonnageEntries",
            request => request with { TonnageEntries = [] },
            TonnageEntriesRequired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenBatteryCategoryIsBlank_ReturnsOnlyTheRequiredError(string value)
    {
        await AssertRequired(
            "TonnageEntries[0].Classification.BatteryCategory",
            request => WithClassification(request, classification => classification with { BatteryCategory = value }),
            BatteryCategoryRequired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenChemistryIsBlank_ReturnsOnlyTheRequiredError(string value)
    {
        await AssertRequired(
            "TonnageEntries[0].Classification.Chemistry",
            request => WithClassification(request, classification => classification with { Chemistry = value }),
            ChemistryRequired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenTonnesIsBlank_ReturnsOnlyTheRequiredError(string value)
    {
        await AssertRequired(
            "TonnageEntries[0].Tonnes",
            request => WithEntry(request.TonnageEntries[0] with { Tonnes = value }),
            TonnesRequired);
    }

    [Fact]
    public async Task ValidateAsync_WhenSchemeOrgIdIsNotAnObjectId_ReturnsOnlyTheObjectIdError()
    {
        await AssertRequired(
            "SchemeOrgId",
            request => request with { SchemeOrgId = "not-an-id" },
            SchemeOrgIdObjectId);
    }

    [Fact]
    public async Task ValidateAsync_WhenMemberOrgIdIsNotAnObjectId_ReturnsOnlyTheObjectIdError()
    {
        await AssertRequired(
            "MemberOrgId",
            request => request with { MemberOrgId = "not-an-id" },
            MemberOrgIdObjectId);
    }

    [Theory]
    [InlineData("not-a-bprn")]
    [InlineData("BPRN-XX-2026-000123")]
    [InlineData("BPRN-EA-2026-12345")]
    [InlineData("BPRN-EA-2026-0001234")]
    [InlineData("bprn-ea-2026-000123")]
    public async Task ValidateAsync_WhenMemberBprnFormatIsWrong_ReturnsOnlyTheFormatError(string memberBprn)
    {
        await AssertRequired(
            "MemberBprn",
            request => request with { MemberBprn = memberBprn },
            MemberBprnFormat);
    }

    [Theory]
    [InlineData("EA")]
    [InlineData("NRW")]
    [InlineData("SEPA")]
    [InlineData("NIEA")]
    public async Task ValidateAsync_WhenBprnRegulatorIsKnown_ReturnsValid(string regulator)
    {
        var request = ValidRequest() with { MemberBprn = $"BPRN-{regulator}-2026-000123" };

        var result = await Validator().ValidateAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(CalendarYear.Minimum)]
    [InlineData(CalendarYear.Maximum)]
    public async Task ValidateAsync_WhenCompliancePeriodYearIsAtTheBoundary_ReturnsValid(int year)
    {
        var request = ValidRequest() with
        {
            CompliancePeriodYear = year,
            MemberBprn = $"BPRN-EA-{year}-000123"
        };

        var result = await Validator().ValidateAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WhenBprnYearDiffers_ReturnsOnlyTheYearError()
    {
        await AssertRequired(
            "MemberBprn",
            request => request with { MemberBprn = "BPRN-NRW-2025-000123" },
            MemberBprnYear);
    }

    [Theory]
    [InlineData(CalendarYear.Minimum - 1)]
    [InlineData(CalendarYear.Maximum + 1)]
    public async Task ValidateAsync_WhenCompliancePeriodYearIsOutOfRange_ReturnsOnlyTheYearError(int year)
    {
        await AssertRequired(
            "CompliancePeriodYear",
            request => request with { CompliancePeriodYear = year },
            CompliancePeriodYearRange);
    }

    [Theory]
    [InlineData("12.5")]
    [InlineData("12.5000")]
    [InlineData("-1.000")]
    public async Task ValidateAsync_WhenTonnesFormatIsWrong_ReturnsOnlyTheFormatError(string tonnes)
    {
        await AssertRequired(
            "TonnageEntries[0].Tonnes",
            request => WithEntry(request.TonnageEntries[0] with { Tonnes = tonnes }),
            TonnesFormat);
    }

    [Fact]
    public async Task ValidateAsync_WhenTonnageEntryIsNull_ReturnsOnlyTheRequiredError()
    {
        await AssertRequired(
            "TonnageEntries[0]",
            request => request with { TonnageEntries = [null!] },
            TonnageEntryRequired);
    }

    [Fact]
    public async Task ValidateAsync_WhenOneEntryIsNullAndAnotherIsInvalid_ReturnsBothErrors()
    {
        // Arrange
        var request = ValidRequest() with
        {
            TonnageEntries =
            [
                null!,
                new TonnageEntryBase
                {
                    Classification = new BatteryClassification
                    {
                        BatteryCategory = "Portable",
                        Chemistry = "Alkaline"
                    },
                    Tonnes = "12.5"
                }
            ]
        };

        // Act
        var result = await Validator().ValidateAsync(request, TestContext.Current.CancellationToken);

        // Assert
        (string PropertyName, string ErrorMessage)[] expected =
        [
            ("TonnageEntries[0]", TonnageEntryRequired),
            ("TonnageEntries[1].Tonnes", TonnesFormat)
        ];
        Assert.Equal(
            expected.OrderBy(error => error.PropertyName, StringComparer.Ordinal)
                .ThenBy(error => error.ErrorMessage, StringComparer.Ordinal),
            ValidationAssert.Messages(result));
    }

    [Fact]
    public async Task ValidateAsync_WhenOnlyCellTypeDiffers_ReturnsValid()
    {
        // Arrange
        var entry = ValidRequest().TonnageEntries[0];
        var request = ValidRequest() with
        {
            TonnageEntries =
            [
                entry with { Classification = entry.Classification with { CellType = "Primary" } },
                entry with
                {
                    Tonnes = "1.000",
                    Classification = entry.Classification with { CellType = "Secondary" }
                }
            ]
        };

        // Act
        var result = await Validator().ValidateAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WhenOptionalDimensionIsNullOrEmpty_ReturnsOnlyTheUniquenessError()
    {
        // Arrange
        var entry = ValidRequest().TonnageEntries[0];
        var request = ValidRequest() with
        {
            TonnageEntries =
            [
                entry with { Classification = entry.Classification with { SubCategory = null } },
                entry with
                {
                    Tonnes = "1.000",
                    Classification = entry.Classification with { SubCategory = string.Empty }
                }
            ]
        };

        // Act
        await ValidationAssert.AssertOnly(Validator(), request, "TonnageEntries", ClassificationRepeated);
    }

    [Fact]
    public async Task ValidateAsync_WhenClassificationIsRepeated_ReturnsOnlyTheUniquenessError()
    {
        await AssertRequired(
            "TonnageEntries",
            request => request with { TonnageEntries = [request.TonnageEntries[0], request.TonnageEntries[0]] },
            ClassificationRepeated);
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

    private static async Task AssertRequired(
        string propertyName,
        Func<PomSubmissionRequest, PomSubmissionRequest> mutate,
        string message)
    {
        await ValidationAssert.AssertOnly(Validator(), mutate(ValidRequest()), propertyName, message);
    }

    private static PomSubmissionRequest WithEntry(TonnageEntryBase entry) =>
        ValidRequest() with { TonnageEntries = [entry] };

    private static PomSubmissionRequest WithClassification(
        PomSubmissionRequest request,
        Func<BatteryClassification, BatteryClassification> mutate)
    {
        var entry = request.TonnageEntries[0];
        return WithEntry(entry with { Classification = mutate(entry.Classification) });
    }

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
