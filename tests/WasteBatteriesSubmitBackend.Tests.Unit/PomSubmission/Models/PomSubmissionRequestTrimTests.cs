using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;
using WasteBatteriesSubmitBackend.Tests.Common;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Models;

public class PomSubmissionRequestTrimTests
{
    [Fact]
    public void Trimmed_WhenStringsHaveSurroundingWhitespace_RemovesIt()
    {
        // Arrange
        var request = new PomSubmissionRequest
        {
            SchemeOrgId = $"  {SamplePomSubmission.SchemeOrgId}  ",
            MemberOrgId = $"\t{SamplePomSubmission.MemberOrgId}\t",
            MemberName = $"  {SamplePomSubmission.MemberName}  ",
            MemberBprn = $"  {SamplePomSubmission.MemberBprn}  ",
            SchemeMembershipRef = $"  {SamplePomSubmission.SchemeMembershipRef}  ",
            CompliancePeriodYear = SamplePomSubmission.CompliancePeriodYear,
            CompliancePeriodQuarter = $"  {SamplePomSubmission.CompliancePeriodQuarter}  ",
            DataBasis = $"  {SamplePomSubmission.DataBasis}  ",
            TonnageEntries =
            [
                new TonnageEntryBase
                {
                    Classification = new BatteryClassification
                    {
                        BatteryCategory = "Industrial",
                        Chemistry = $"  {SamplePomSubmission.Chemistry}  "
                    },
                    Tonnes = $"  {SamplePomSubmission.Tonnes}  "
                }
            ]
        };

        // Act
        var trimmed = request.Trimmed();

        // Assert
        Assert.Equal(SamplePomSubmission.SchemeOrgId, trimmed.SchemeOrgId);
        Assert.Equal(SamplePomSubmission.MemberOrgId, trimmed.MemberOrgId);
        Assert.Equal(SamplePomSubmission.MemberName, trimmed.MemberName);
        Assert.Equal(SamplePomSubmission.MemberBprn, trimmed.MemberBprn);
        Assert.Equal(SamplePomSubmission.SchemeMembershipRef, trimmed.SchemeMembershipRef);
        Assert.Equal(SamplePomSubmission.CompliancePeriodYear, trimmed.CompliancePeriodYear);
        Assert.Equal(SamplePomSubmission.CompliancePeriodQuarter, trimmed.CompliancePeriodQuarter);
        Assert.Equal(SamplePomSubmission.DataBasis, trimmed.DataBasis);
        var entry = Assert.Single(trimmed.TonnageEntries);
        Assert.Equal(SamplePomSubmission.Tonnes, entry.Tonnes);
        Assert.Equal(BatteryClassification.Portable, entry.Classification.BatteryCategory);
        Assert.Equal(SamplePomSubmission.Chemistry, entry.Classification.Chemistry);
    }

    [Fact]
    public void Trimmed_WhenOptionalTextIsMissing_LeavesItNull()
    {
        // Arrange
        var request = new PomSubmissionRequest
        {
            MemberName = null!,
            TonnageEntries =
            [
                new TonnageEntryBase
                {
                    Tonnes = null!,
                    Classification = new BatteryClassification
                    {
                        BatteryCategory = null!,
                        Chemistry = null!
                    }
                },
                null!,
                new TonnageEntryBase { Classification = null! }
            ]
        };

        // Act
        var trimmed = request.Trimmed();

        // Assert
        Assert.Equal(string.Empty, trimmed.MemberName);
        Assert.Equal(string.Empty, trimmed.TonnageEntries[0].Tonnes);
        Assert.Equal("Portable", trimmed.TonnageEntries[0].Classification.BatteryCategory);
        Assert.Equal(string.Empty, trimmed.TonnageEntries[0].Classification.Chemistry);
        Assert.Null(trimmed.TonnageEntries[1]);
        Assert.Null(trimmed.TonnageEntries[2].Classification);
    }

    [Fact]
    public void Trimmed_WhenTonnageEntriesAreNull_LeavesThemNull()
    {
        // Arrange
        var request = new PomSubmissionRequest { TonnageEntries = null! };

        // Act
        var trimmed = request.Trimmed();

        // Assert
        Assert.Null(trimmed.TonnageEntries);
    }
}
