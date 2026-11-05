using System.Text.Json;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;

namespace WasteBatteriesSubmitBackend.Tests.Common;

// The valid create body. Posted JSON omits batteryCategory. The mapper sets it from configuration.
// Request() reads this JSON, so the model and the body stay the same document.
public static class SamplePomSubmission
{
    public const string SchemeOrgId = "674a1b2c3d4e5f6789012345";

    public const string MemberOrgId = "674a1b2c3d4e5f6789012346";

    public const string MemberName = "Northwind Batteries Ltd";

    public const string MemberBprn = "BPRN-EA-2026-000123";

    public const string SchemeMembershipRef = "SCH-00417";

    public const int CompliancePeriodYear = 2026;

    public const string CompliancePeriodQuarter = "Quarter 2";

    public const string DataBasis = "Actual";

    public const string Chemistry = "Alkaline";

    public const string Tonnes = "12.500";

    public static readonly DateTime CreatedAt = new(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);

    // Keep this layout. Endpoint tests replace these property texts inside the body.
    public static string Json { get; } = $$"""
        {
          "schemeOrgId": "{{SchemeOrgId}}",
          "memberOrgId": "{{MemberOrgId}}",
          "memberName": "{{MemberName}}",
          "memberBprn": "{{MemberBprn}}",
          "schemeMembershipRef": "{{SchemeMembershipRef}}",
          "compliancePeriodYear": {{CompliancePeriodYear}},
          "compliancePeriodQuarter": "{{CompliancePeriodQuarter}}",
          "dataBasis": "{{DataBasis}}",
          "tonnageEntries": [
            {
              "classification": {
                "chemistry": "{{Chemistry}}"
              },
              "tonnes": "{{Tonnes}}"
            }
          ]
        }
        """;

    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    public static PomSubmissionRequest Request() =>
        JsonSerializer.Deserialize<PomSubmissionRequest>(Json, s_json)
        ?? throw new InvalidOperationException("Sample POM submission JSON did not deserialise.");
}
