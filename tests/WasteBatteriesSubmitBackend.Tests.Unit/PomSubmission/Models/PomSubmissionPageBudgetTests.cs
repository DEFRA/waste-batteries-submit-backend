using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using WasteBatteriesSubmitBackend.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;
using WasteBatteriesSubmitBackend.Tests.Common;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Models;

// MaxPageSize is 750, rounded down from 772 uncompressed submissions.
// MaxGzipPageSize is 17000, rounded down from the endpoint measurement in the README.
// Recalculate both when PomSubmissionModel changes.
public class PomSubmissionPageBudgetTests
{
    private const int TonnageEntries = 80;

    private const int LargestFittingPage = 772;

    private const int RoundedPageSize = 750;

    private const int RoundedGzipPageSize = 17000;

    private const int CdpPayloadLimitBytes = 10 * 1024 * 1024;

    private static readonly DateTime CreatedAt = new(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void MaxPageSize_StaysUnderTheUncompressedCdpPayloadLimit()
    {
        // Arrange
        var submission = FullSubmission();
        var configured = ConfiguredMaxPageSize();

        // Act
        var fitted = LargestPageUnderLimit(submission);
        var atConfigured = PageBytes(submission, configured);

        // Assert
        Assert.Equal(LargestFittingPage, fitted);
        Assert.Equal(RoundedPageSize, configured);
        Assert.True(
            atConfigured < CdpPayloadLimitBytes,
            $"A page of {configured} submissions is {atConfigured} bytes, which is over the {CdpPayloadLimitBytes} byte limit.");
    }

    [Fact]
    public void MaxGzipPageSize_StaysUnderTheCdpPayloadLimit()
    {
        // Arrange
        var configured = ConfiguredMaxGzipPageSize();
        var items = MeasuredPomSubmission.Page(
            MeasuredPomSubmission.SchemeOrgId,
            configured,
            ConfiguredPomSubmissionText("InitialActivity"),
            ConfiguredPomSubmissionText("InitialStatus"),
            ConfiguredPomSubmissionSetting("InitialVersion"),
            distinct: true);

        // Act
        var gzipBytes = MeasuredPomSubmission.GzipPage(items, configured, pageNumber: 1, pageSize: configured);

        // Assert
        Assert.Equal(RoundedGzipPageSize, configured);
        Assert.True(
            gzipBytes < CdpPayloadLimitBytes,
            $"Gzip of {configured} submissions is {gzipBytes} bytes, which is over the {CdpPayloadLimitBytes} byte limit.");
    }

    private static int LargestPageUnderLimit(PomSubmissionModel submission)
    {
        var one = PageBytes(submission, 1);
        var marginal = PageBytes(submission, 2) - one;
        var count = 1;
        while (one + count * marginal < CdpPayloadLimitBytes)
        {
            count++;
        }

        return count;
    }

    private static int PageBytes(PomSubmissionModel submission, int count)
    {
        var items = Enumerable.Repeat(submission, count).ToArray();
        var json = JsonSerializer.Serialize(new PaginatedList<PomSubmissionModel>(items, count, 1, count), Options);
        return Encoding.UTF8.GetByteCount(json);
    }

    private static int ConfiguredMaxPageSize() => ConfiguredPomSubmissionSetting("MaxPageSize");

    private static int ConfiguredMaxGzipPageSize() => ConfiguredPomSubmissionSetting("MaxGzipPageSize");

    private static int ConfiguredPomSubmissionSetting(string name)
    {
        using var document = OpenAppSettings();
        return document.RootElement.GetProperty("PomSubmission").GetProperty(name).GetInt32();
    }

    private static string ConfiguredPomSubmissionText(string name)
    {
        using var document = OpenAppSettings();
        return document.RootElement.GetProperty("PomSubmission").GetProperty(name).GetString()
            ?? throw new InvalidOperationException($"PomSubmission:{name} is missing from appsettings.json.");
    }

    private static JsonDocument OpenAppSettings([CallerFilePath] string sourceFile = "")
    {
        var appSettings = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(sourceFile)!,
            "..",
            "..",
            "..",
            "..",
            "src",
            "WasteBatteriesSubmitBackend",
            "appsettings.json"));
        return JsonDocument.Parse(
            File.ReadAllText(appSettings),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
    }

    // Long header text and a long chemistry on every entry. The example response is smaller than this.
    private static PomSubmissionModel FullSubmission()
    {
        const string chemistry = "Nickel-metal hydride";
        var header = new PomSubmissionRequest
        {
            SchemeOrgId = "674a1b2c3d4e5f6789012345",
            MemberOrgId = "674a1b2c3d4e5f6789012346",
            MemberName = new string('N', 160),
            MemberBprn = "BPRN-NIEA-2026-000123",
            SchemeMembershipRef = new string('S', 40),
            CompliancePeriodYear = 2026,
            CompliancePeriodQuarter = "Quarter 2",
            DataBasis = "Estimated",
            TonnageEntries = Enumerable.Range(1, TonnageEntries).Select(index => new TonnageEntryBase
            {
                Classification = new BatteryClassification
                {
                    Chemistry = $"{chemistry} {index:00}"
                },
                Tonnes = "99999.999"
            }).ToList()
        };

        return new PomSubmissionModel(
            header,
            "674a1b2c3d4e5f6789012347",
            CreatedAt,
            header.TonnageEntries.Select((entry, index) => entry.ToStoredEntry($"674a1b2c3d4e5f678901{index + 1000:x4}", "Placed")).ToList(),
            "Submitted",
            1);
    }
}
