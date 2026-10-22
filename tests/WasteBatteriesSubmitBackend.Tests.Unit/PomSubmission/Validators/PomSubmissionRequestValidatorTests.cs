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
            MemberName = "",
            SchemeMembershipRef = "",
            MemberBprn = "not-a-bprn",
            CompliancePeriodYear = 0,
            SchemeOrgId = "not-an-id",
            TonnageEntries =
            [
                new TonnageEntryBase
                {
                    Classification = new BatteryClassification
                    {
                        BatteryCategory = "",
                        Chemistry = "Alkaline"
                    },
                    Tonnes = "12.5"
                },
                new TonnageEntryBase
                {
                    Classification = new BatteryClassification
                    {
                        BatteryCategory = "",
                        Chemistry = "Alkaline"
                    },
                    Tonnes = "12.5"
                }
            ]
        };

        // Act
        var result = await Validator().ValidateAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(result.Errors, error => error.PropertyName == "MemberName");
        Assert.Contains(result.Errors, error => error.PropertyName == "SchemeMembershipRef");
        Assert.Contains(result.Errors, error => error.PropertyName == "MemberBprn");
        Assert.Contains(result.Errors, error => error.PropertyName == "CompliancePeriodYear");
        Assert.Contains(result.Errors, error => error.PropertyName == "SchemeOrgId");
        Assert.Contains(result.Errors, error => error.PropertyName == "TonnageEntries[0].Tonnes");
        Assert.Contains(result.Errors, error => error.PropertyName == "TonnageEntries[0].Classification.BatteryCategory");
        Assert.Contains(result.Errors, error => error.PropertyName == "TonnageEntries");
        Assert.True(result.Errors.Count >= 8);
    }

    [Fact]
    public async Task ValidateAsync_WhenBprnYearDiffers_ReturnsMemberBprnError()
    {
        // Arrange
        var request = ValidRequest() with { MemberBprn = "BPRN-NRW-2025-000123" };

        // Act
        var result = await Validator().ValidateAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(result.Errors, error => error.PropertyName == "MemberBprn");
    }

    private static PomSubmissionRequestValidator Validator() => new();

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
