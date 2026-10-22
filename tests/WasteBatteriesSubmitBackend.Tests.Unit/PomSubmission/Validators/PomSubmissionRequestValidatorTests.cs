using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Validators;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Validators;

public class PomSubmissionRequestValidatorTests
{
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
            ("SchemeOrgId", "schemeOrgId must be an ObjectId."),
            ("MemberOrgId", "'Member Org Id' must not be empty."),
            ("MemberName", "'Member Name' must not be empty."),
            ("MemberBprn", "memberBprn must match BPRN-{EA|NRW|SEPA|NIEA}-{year}-{6 digits}."),
            ("SchemeMembershipRef", "'Scheme Membership Ref' must not be empty."),
            ("CompliancePeriodYear", "compliancePeriodYear must be a calendar year."),
            ("CompliancePeriodQuarter", "'Compliance Period Quarter' must not be empty."),
            ("DataBasis", "'Data Basis' must not be empty."),
            ("TonnageEntries", "Each classification can only be reported once in a submission."),
            ("TonnageEntries[0].Tonnes", "tonnes must be a string decimal with exactly 3 digits after the decimal point."),
            ("TonnageEntries[0].Classification.BatteryCategory", "'Classification Battery Category' must not be empty."),
            ("TonnageEntries[0].Classification.Chemistry", "'Classification Chemistry' must not be empty."),
            ("TonnageEntries[1].Tonnes", "tonnes must be a string decimal with exactly 3 digits after the decimal point."),
            ("TonnageEntries[1].Classification.BatteryCategory", "'Classification Battery Category' must not be empty."),
            ("TonnageEntries[1].Classification.Chemistry", "'Classification Chemistry' must not be empty.")
        ];
        Assert.Equal(
            expected.OrderBy(error => error.PropertyName, StringComparer.Ordinal)
                .ThenBy(error => error.ErrorMessage, StringComparer.Ordinal),
            Messages(result));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenSchemeOrgIdIsMissing_ReturnsOnlyTheRequiredError(string? value)
    {
        await AssertOnly(
            ValidRequest() with { SchemeOrgId = value! },
            "SchemeOrgId",
            "'Scheme Org Id' must not be empty.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenMemberOrgIdIsMissing_ReturnsOnlyTheRequiredError(string? value)
    {
        await AssertOnly(
            ValidRequest() with { MemberOrgId = value! },
            "MemberOrgId",
            "'Member Org Id' must not be empty.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenMemberNameIsMissing_ReturnsOnlyTheRequiredError(string? value)
    {
        await AssertOnly(
            ValidRequest() with { MemberName = value! },
            "MemberName",
            "'Member Name' must not be empty.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenMemberBprnIsMissing_ReturnsOnlyTheRequiredError(string? value)
    {
        await AssertOnly(
            ValidRequest() with { MemberBprn = value! },
            "MemberBprn",
            "'Member Bprn' must not be empty.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenSchemeMembershipRefIsMissing_ReturnsOnlyTheRequiredError(string? value)
    {
        await AssertOnly(
            ValidRequest() with { SchemeMembershipRef = value! },
            "SchemeMembershipRef",
            "'Scheme Membership Ref' must not be empty.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenCompliancePeriodQuarterIsMissing_ReturnsOnlyTheRequiredError(string? value)
    {
        await AssertOnly(
            ValidRequest() with { CompliancePeriodQuarter = value! },
            "CompliancePeriodQuarter",
            "'Compliance Period Quarter' must not be empty.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenDataBasisIsMissing_ReturnsOnlyTheRequiredError(string? value)
    {
        await AssertOnly(
            ValidRequest() with { DataBasis = value! },
            "DataBasis",
            "'Data Basis' must not be empty.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenTonnesIsMissing_ReturnsOnlyTheRequiredError(string? value)
    {
        var request = WithEntry(ValidRequest().TonnageEntries[0] with { Tonnes = value! });

        await AssertOnly(request, "TonnageEntries[0].Tonnes", "'Tonnes' must not be empty.");
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

        await AssertOnly(
            request,
            "TonnageEntries[0].Classification.BatteryCategory",
            "'Classification Battery Category' must not be empty.");
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

        await AssertOnly(
            request,
            "TonnageEntries[0].Classification.Chemistry",
            "'Classification Chemistry' must not be empty.");
    }

    [Fact]
    public async Task ValidateAsync_WhenSchemeOrgIdIsNotAnObjectId_ReturnsOnlyTheObjectIdError()
    {
        await AssertOnly(
            ValidRequest() with { SchemeOrgId = "not-an-id" },
            "SchemeOrgId",
            "schemeOrgId must be an ObjectId.");
    }

    [Fact]
    public async Task ValidateAsync_WhenMemberOrgIdIsNotAnObjectId_ReturnsOnlyTheObjectIdError()
    {
        await AssertOnly(
            ValidRequest() with { MemberOrgId = "not-an-id" },
            "MemberOrgId",
            "memberOrgId must be an ObjectId.");
    }

    [Fact]
    public async Task ValidateAsync_WhenMemberBprnFormatIsWrong_ReturnsOnlyTheFormatError()
    {
        await AssertOnly(
            ValidRequest() with { MemberBprn = "not-a-bprn" },
            "MemberBprn",
            "memberBprn must match BPRN-{EA|NRW|SEPA|NIEA}-{year}-{6 digits}.");
    }

    [Fact]
    public async Task ValidateAsync_WhenBprnYearDiffers_ReturnsOnlyTheYearError()
    {
        await AssertOnly(
            ValidRequest() with { MemberBprn = "BPRN-NRW-2025-000123" },
            "MemberBprn",
            "memberBprn must be the registration for compliancePeriodYear.");
    }

    [Theory]
    [InlineData(999)]
    [InlineData(10000)]
    public async Task ValidateAsync_WhenCompliancePeriodYearIsOutOfRange_ReturnsOnlyTheYearError(int year)
    {
        await AssertOnly(
            ValidRequest() with { CompliancePeriodYear = year },
            "CompliancePeriodYear",
            "compliancePeriodYear must be a calendar year.");
    }

    [Fact]
    public async Task ValidateAsync_WhenTonnesFormatIsWrong_ReturnsOnlyTheFormatError()
    {
        var request = WithEntry(ValidRequest().TonnageEntries[0] with { Tonnes = "12.5" });

        await AssertOnly(
            request,
            "TonnageEntries[0].Tonnes",
            "tonnes must be a string decimal with exactly 3 digits after the decimal point.");
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

        await AssertOnly(request, "TonnageEntries", "'Tonnage Entries' must not be empty.");
    }

    [Fact]
    public async Task ValidateAsync_WhenClassificationIsNull_ReturnsOnlyTheRequiredError()
    {
        var request = WithEntry(ValidRequest().TonnageEntries[0] with { Classification = null! });

        await AssertOnly(request, "TonnageEntries[0].Classification", "'Classification' must not be empty.");
    }

    [Fact]
    public async Task ValidateAsync_WhenClassificationIsRepeated_ReturnsOnlyTheUniquenessError()
    {
        var request = ValidRequest();
        request = request with { TonnageEntries = [request.TonnageEntries[0], request.TonnageEntries[0]] };

        await AssertOnly(
            request,
            "TonnageEntries",
            "Each classification can only be reported once in a submission.");
    }

    private static async Task AssertOnly(PomSubmissionRequest request, string propertyName, string message)
    {
        var result = await Validator().ValidateAsync(request, TestContext.Current.CancellationToken);
        var error = Assert.Single(result.Errors);
        Assert.Equal(propertyName, error.PropertyName);
        Assert.Equal(message, error.ErrorMessage);
    }

    private static List<(string PropertyName, string ErrorMessage)> Messages(FluentValidation.Results.ValidationResult result) =>
        result.Errors
            .Select(error => (error.PropertyName, error.ErrorMessage))
            .OrderBy(error => error.PropertyName, StringComparer.Ordinal)
            .ThenBy(error => error.ErrorMessage, StringComparer.Ordinal)
            .ToList();

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
