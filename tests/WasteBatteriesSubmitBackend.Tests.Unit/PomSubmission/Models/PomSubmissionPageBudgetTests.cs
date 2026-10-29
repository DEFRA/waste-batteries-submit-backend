using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using WasteBatteriesSubmitBackend.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;
using WasteBatteriesSubmitBackend.Tests.Common;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Models;

public class PomSubmissionPageBudgetTests
{
    private const int TonnageEntries = 80;

    private const int LargestFittingPage = 772;

    // RoundedPageSize and RoundedGzipPageSize here should match the values in the app settings (MaxPageSize and MaxGzipPageSize) - if the values are out of sync then the tests fail
    // This is the number of standard json submission models that should be returned to ensure that it is within the payload limit
    private const int RoundedPageSize = 750;

    // This is the number of submission models when using gzip, that should be returned to ensure that it is within the payload limit
    private const int RoundedGzipPageSize = 17000;

    // Room above the configured page sizes. If the model gets smaller, these larger pages can fit and the limits can move up.
    private const int ExtraStandardResponses = 50;
    private const int ExtraGzipResponses = 1000;

    //Assume that it is 10MB as the limit for the payloads
    private const int CdpPayloadLimitBytes = 10 * 1024 * 1024;

    private static readonly DateTime s_createdAt = new(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);

    private static readonly JsonSerializerOptions s_options = new(JsonSerializerDefaults.Web);

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
            ConfiguredPomSubmissionSetting("InitialVersion"));

        // Act
        var gzipBytes = MeasuredPomSubmission.GzipPage(items, configured, pageNumber: 1, pageSize: configured);

        // Assert
        Assert.Equal(RoundedGzipPageSize, configured);
        Assert.True(
            gzipBytes < CdpPayloadLimitBytes,
            $"Gzip of {configured} submissions is {gzipBytes} bytes, which is over the {CdpPayloadLimitBytes} byte limit.");
    }

    [Fact]
    public void MaxPageSize_PlusExtraStandardResponses_ExceedsTheUncompressedCdpPayloadLimit()
    {
        // Arrange
        var submission = FullSubmission();
        var overLimitCount = ConfiguredMaxPageSize() + ExtraStandardResponses;

        // Act
        var bytes = PageBytes(submission, overLimitCount);

        // Assert
        Assert.True(
            bytes >= CdpPayloadLimitBytes,
            $"A page of {overLimitCount} submissions is {bytes} bytes, which is under the {CdpPayloadLimitBytes} byte limit. Raise MaxPageSize.");
    }

    [Fact]
    public void MaxGzipPageSize_PlusExtraGzipResponses_ExceedsTheCdpPayloadLimit()
    {
        // Arrange
        var overLimitCount = ConfiguredMaxGzipPageSize() + ExtraGzipResponses;
        var items = MeasuredPomSubmission.Page(
            MeasuredPomSubmission.SchemeOrgId,
            overLimitCount,
            ConfiguredPomSubmissionText("InitialActivity"),
            ConfiguredPomSubmissionText("InitialStatus"),
            ConfiguredPomSubmissionSetting("InitialVersion"));

        // Act
        var gzipBytes = MeasuredPomSubmission.GzipPage(items, overLimitCount, pageNumber: 1, pageSize: overLimitCount);

        // Assert
        Assert.True(
            gzipBytes >= CdpPayloadLimitBytes,
            $"Gzip of {overLimitCount} submissions is {gzipBytes} bytes, which is under the {CdpPayloadLimitBytes} byte limit. Raise MaxGzipPageSize.");
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
        var json = JsonSerializer.Serialize(new PaginatedList<PomSubmissionModel>(items, count, 1, count), s_options);
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
            TonnageEntries = [.. Enumerable.Range(1, TonnageEntries).Select(index => new TonnageEntryBase
            {
                Classification = new BatteryClassification
                {
                    Chemistry = $"{chemistry} {index:00}"
                },
                Tonnes = "99999.999"
            })]
        };

        return new PomSubmissionModel(
            header,
            "674a1b2c3d4e5f6789012347",
            s_createdAt,
            [.. header.TonnageEntries.Select((entry, index) => entry.ToStoredEntry($"674a1b2c3d4e5f678901{index + 1000:x4}", "Placed"))],
            "Submitted",
            1);
    }
}
