using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;
using WasteBatteriesSubmitBackend.PomSubmission.Validators;
using WasteBatteriesSubmitBackend.Tests.Common;
using WasteBatteriesSubmitBackend.Tests.Unit.Utils.Validation;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Validators;

public class PomSubmissionFilterRequestValidatorTests
{
    private readonly PomSubmissionFilterRequestValidator _validator = new();

    [Fact]
    public async Task ValidateAsync_WhenNoBusinessFilterIsSet_ReturnsThatError()
    {
        await ValidationAssert.AssertOnly(
            _validator,
            new PomSubmissionFilterRequest
            {
                ReturnCurrentOnly = true,
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
            "scheme" => new PomSubmissionFilterRequest { SchemeOrgId = SamplePomSubmission.SchemeOrgId },
            "member" => new PomSubmissionFilterRequest { MemberOrgId = "674a1b2c3d4e5f6789012347" },
            "year" => new PomSubmissionFilterRequest { CompliancePeriodYears = [SamplePomSubmission.CompliancePeriodYear] },
            "quarter" => new PomSubmissionFilterRequest { CompliancePeriodQuarters = [SamplePomSubmission.CompliancePeriodQuarter] },
            _ => throw new ArgumentOutOfRangeException(nameof(filter))
        };

        var result = await _validator.ValidateAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WhenEveryFilterIsWellFormed_IsValid()
    {
        var result = await _validator.ValidateAsync(
            new PomSubmissionFilterRequest
            {
                SchemeOrgId = SamplePomSubmission.SchemeOrgId,
                MemberOrgId = "674a1b2c3d4e5f6789012347",
                CompliancePeriodYears = [2024, SamplePomSubmission.CompliancePeriodYear],
                CompliancePeriodQuarters = ["Quarter 1", SamplePomSubmission.CompliancePeriodQuarter],
                ReturnCurrentOnly = true,
                PageNumber = 1,
                PageSize = 25
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WhenTheSuppliedValuesAreInvalid_ReturnsEveryError()
    {
        var result = await _validator.ValidateAsync(
            new PomSubmissionFilterRequest
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
    public async Task ValidateAsync_WhenPageNumberIsSetWithoutPageSize_IsValid()
    {
        var result = await _validator.ValidateAsync(
            new PomSubmissionFilterRequest
            {
                SchemeOrgId = SamplePomSubmission.SchemeOrgId,
                PageNumber = 2
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }
}
