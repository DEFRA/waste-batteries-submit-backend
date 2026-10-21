using NSubstitute;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.PomSubmission.Validators;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Validators;

public class PomSubmissionRequestValidatorTests
{
    [Fact]
    public async Task Validate_accepts_a_complete_submission_when_none_is_current()
    {
        var result = await Validator().ValidateAsync(ValidRequest(), TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Validate_returns_every_failure_together()
    {
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

        var result = await Validator().ValidateAsync(request, TestContext.Current.CancellationToken);

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
    public async Task Validate_rejects_a_bprn_for_a_different_year()
    {
        var result = await Validator().ValidateAsync(
            ValidRequest() with { MemberBprn = "BPRN-NRW-2025-000123" },
            TestContext.Current.CancellationToken);

        Assert.Contains(result.Errors, error => error.PropertyName == "MemberBprn");
    }

    [Fact]
    public async Task Validate_rejects_a_current_submission_that_already_exists()
    {
        var repository = Substitute.For<IPomSubmissionRepository>();
        repository
            .FindCurrentAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new PomSubmissionModel { Id = "674a1b2c3d4e5f6789012345", CreatedAtUtc = DateTime.UnixEpoch });

        var result = await Validator(repository).ValidateAsync(ValidRequest(), TestContext.Current.CancellationToken);

        Assert.Contains(result.Errors, error => error.PropertyName == "Submission");
    }

    private static PomSubmissionRequestValidator Validator(IPomSubmissionRepository? repository = null) =>
        new(repository ?? Substitute.For<IPomSubmissionRepository>());

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
