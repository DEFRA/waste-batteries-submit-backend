using System.Text.Json;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Validators;
using WasteBatteriesSubmitBackend.Tests.Unit.Utils.Validation;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Models;

public class PomSubmissionRequestJsonTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    public async Task Deserialize_WhenYearIsNullOrBlank_ReportsFieldValidationErrors(string year)
    {
        // Arrange
        var json = $$"""
            {
              "schemeOrgId": {{year}},
              "memberOrgId": {{year}},
              "memberName": {{year}},
              "memberBprn": {{year}},
              "schemeMembershipRef": {{year}},
              "compliancePeriodYear": {{year}},
              "compliancePeriodQuarter": {{year}},
              "dataBasis": {{year}},
              "tonnageEntries": [
                {
                  "classification": {{(year == "null" ? "null" : """{ "batteryCategory": "", "chemistry": "" }""")}},
                  "tonnes": {{year}}
                }
              ]
            }
            """;

        // Act
        var request = JsonSerializer.Deserialize<PomSubmissionRequest>(json, JsonSerializerOptions.Web);
        Assert.NotNull(request);
        var result = await new PomSubmissionRequestValidator().ValidateAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, request.CompliancePeriodYear);
        Assert.Contains(result.Errors, error => error.PropertyName == "MemberName"
            && error.ErrorMessage == "'Member Name' must not be empty.");
        Assert.Contains(result.Errors, error => error.PropertyName == "CompliancePeriodYear"
            && error.ErrorMessage == "compliancePeriodYear must be a calendar year.");
        Assert.Contains(result.Errors, error => error.PropertyName == "TonnageEntries[0].Tonnes"
            && error.ErrorMessage == "'Tonnes' must not be empty.");
    }

    [Fact]
    public void Deserialize_WhenYearIsANumber_KeepsIt()
    {
        // Arrange
        const string json = """{ "compliancePeriodYear": 2026 }""";

        // Act
        var request = JsonSerializer.Deserialize<PomSubmissionRequest>(json, JsonSerializerOptions.Web);

        // Assert
        Assert.NotNull(request);
        Assert.Equal(2026, request.CompliancePeriodYear);
    }

    [Fact]
    public async Task Deserialize_WhenNoPropertiesArePosted_ReportsEveryRequiredField()
    {
        // Arrange
        const string json = "{}";

        // Act
        var request = JsonSerializer.Deserialize<PomSubmissionRequest>(json, JsonSerializerOptions.Web);
        Assert.NotNull(request);
        var result = await new PomSubmissionRequestValidator().ValidateAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(string.Empty, request.SchemeOrgId);
        Assert.Equal(0, request.CompliancePeriodYear);
        Assert.Empty(request.TonnageEntries);
        (string PropertyName, string ErrorMessage)[] expected =
        [
            ("SchemeOrgId", "'Scheme Org Id' must not be empty."),
            ("MemberOrgId", "'Member Org Id' must not be empty."),
            ("MemberName", "'Member Name' must not be empty."),
            ("MemberBprn", "'Member Bprn' must not be empty."),
            ("SchemeMembershipRef", "'Scheme Membership Ref' must not be empty."),
            ("CompliancePeriodYear", "compliancePeriodYear must be a calendar year."),
            ("CompliancePeriodQuarter", "'Compliance Period Quarter' must not be empty."),
            ("DataBasis", "'Data Basis' must not be empty."),
            ("TonnageEntries", "'Tonnage Entries' must not be empty.")
        ];
        Assert.Equal(
            expected.OrderBy(error => error.PropertyName, StringComparer.Ordinal)
                .ThenBy(error => error.ErrorMessage, StringComparer.Ordinal),
            ValidationAssert.Messages(result));
    }
}
