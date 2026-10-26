using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;
using WasteBatteriesSubmitBackend.PomSubmission.Validators;
using WasteBatteriesSubmitBackend.Tests.Unit.Utils.Validation;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Validators;

public class PomSubmissionListRequestValidatorTests
{
    private readonly PomSubmissionListRequestValidator validator = new();

    [Fact]
    public async Task ValidateAsync_WhenNoBusinessFilterIsSet_ReturnsThatError()
    {
        await ValidationAssert.AssertOnly(
            validator,
            new PomSubmissionListRequest
            {
                IsCurrent = true,
                CompliancePeriodYears = [],
                CompliancePeriodQuarters = []
            },
            "Filters",
            "At least one of schemeOrgId, memberOrgId, compliancePeriodYears or compliancePeriodQuarters is required.");
    }

    [Theory]
    [InlineData("scheme")]
    [InlineData("member")]
    [InlineData("year")]
    [InlineData("quarter")]
    public async Task ValidateAsync_WhenOneBusinessFilterIsSet_IsValid(string filter)
    {
        var request = filter switch
        {
            "scheme" => new PomSubmissionListRequest { SchemeOrgId = "674a1b2c3d4e5f6789012346" },
            "member" => new PomSubmissionListRequest { MemberOrgId = "674a1b2c3d4e5f6789012347" },
            "year" => new PomSubmissionListRequest { CompliancePeriodYears = [2026] },
            "quarter" => new PomSubmissionListRequest { CompliancePeriodQuarters = ["Quarter 2"] },
            _ => throw new ArgumentOutOfRangeException(nameof(filter))
        };

        var result = await validator.ValidateAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WhenEveryFilterIsWellFormed_IsValid()
    {
        var result = await validator.ValidateAsync(
            new PomSubmissionListRequest
            {
                SchemeOrgId = "674a1b2c3d4e5f6789012346",
                MemberOrgId = "674a1b2c3d4e5f6789012347",
                CompliancePeriodYears = [2024, 2026],
                CompliancePeriodQuarters = ["Quarter 1", "Quarter 2"],
                IsCurrent = true,
                PageNumber = 1,
                PageSize = 25
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WhenTheSuppliedValuesAreInvalid_ReturnsEveryError()
    {
        var result = await validator.ValidateAsync(
            new PomSubmissionListRequest
            {
                SchemeOrgId = "not-an-id",
                MemberOrgId = "not-an-id",
                CompliancePeriodYears = [12],
                CompliancePeriodQuarters = [" "],
                PageNumber = 0,
                PageSize = 0
            },
            TestContext.Current.CancellationToken);

        var messages = ValidationAssert.Messages(result);
        Assert.Contains(("SchemeOrgId", "schemeOrgId must be an ObjectId."), messages);
        Assert.Contains(("MemberOrgId", "memberOrgId must be an ObjectId."), messages);
        Assert.Contains(("CompliancePeriodYears[0]", "compliancePeriodYears must be a calendar year."), messages);
        Assert.Contains(("CompliancePeriodQuarters[0]", "'Compliance Period Quarters' must not be empty."), messages);
        Assert.Contains(("PageNumber", "pageNumber must be at least 1."), messages);
        Assert.Contains(("PageSize", "pageSize must be at least 1."), messages);
    }

    [Fact]
    public async Task ValidateAsync_WhenPageNumberIsSetWithoutPageSize_ReturnsThatError()
    {
        await ValidationAssert.AssertOnly(
            validator,
            new PomSubmissionListRequest
            {
                SchemeOrgId = "674a1b2c3d4e5f6789012346",
                PageNumber = 2
            },
            "PageNumber",
            "pageSize is required when pageNumber is set.");
    }
}
