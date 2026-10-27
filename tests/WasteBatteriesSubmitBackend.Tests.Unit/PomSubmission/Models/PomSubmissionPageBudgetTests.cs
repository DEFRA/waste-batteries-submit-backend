using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using WasteBatteriesSubmitBackend.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Models;

// MaxPageSize is 750. MaxGzipPageSize is 17500. Both are rounded down from the pages that fit in 10 MiB.
// Recalculate them when PomSubmissionModel changes.
public class PomSubmissionPageBudgetTests
{
    private const int TonnageEntries = 80;

    private const int LargestFittingPage = 772;

    private const int RoundedPageSize = 750;

    private const int LargestGzipPage = 17696;

    private const int RoundedGzipPageSize = 17500;

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

        // Act
        var atConfigured = GzipBytes(configured);
        var atLargest = GzipBytes(LargestGzipPage);
        var atOneMore = GzipBytes(LargestGzipPage + 1);

        // Assert
        Assert.Equal(RoundedGzipPageSize, configured);
        Assert.True(atConfigured < CdpPayloadLimitBytes, $"Gzip of {configured} is {atConfigured} bytes.");
        Assert.True(atLargest < CdpPayloadLimitBytes, $"Gzip of {LargestGzipPage} is {atLargest} bytes.");
        Assert.True(atOneMore >= CdpPayloadLimitBytes, $"Gzip of {LargestGzipPage + 1} is {atOneMore} bytes.");
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

    private static int ConfiguredMaxPageSize([CallerFilePath] string sourceFile = "")
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
        using var document = JsonDocument.Parse(
            File.ReadAllText(appSettings),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        return document.RootElement.GetProperty("PomSubmission").GetProperty("MaxPageSize").GetInt32();
    }

    private static int ConfiguredMaxGzipPageSize([CallerFilePath] string sourceFile = "")
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
        using var document = JsonDocument.Parse(
            File.ReadAllText(appSettings),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        return document.RootElement.GetProperty("PomSubmission").GetProperty("MaxGzipPageSize").GetInt32();
    }

    private static int GzipBytes(int count)
    {
        var items = Enumerable.Range(0, count).Select(DistinctSubmission).ToArray();
        var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new PaginatedList<PomSubmissionModel>(items, count, 1, count),
            Options));
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(json);
        }

        return (int)output.Length;
    }

    // Each submission differs. Identical copies compress far more than a real list.
    private static PomSubmissionModel DistinctSubmission(int index)
    {
        const string chemistry = "Nickel-metal hydride";
        var header = new PomSubmissionRequest
        {
            SchemeOrgId = "674a1b2c3d4e5f6789012345",
            MemberOrgId = $"674a1b2c3d4e5f67{index:x8}",
            MemberName = $"{index:D6} " + new string((char)('A' + (index % 26)), 153),
            MemberBprn = $"BPRN-NIEA-2026-{(index % 1000000):D6}",
            SchemeMembershipRef = $"SCH-{index:D36}",
            CompliancePeriodYear = 2020 + (index % 7),
            CompliancePeriodQuarter = $"Quarter {(index % 4) + 1}",
            DataBasis = index % 2 == 0 ? "Estimated" : "Actual",
            TonnageEntries = Enumerable.Range(1, TonnageEntries).Select(entry => new TonnageEntryBase
            {
                Classification = new BatteryClassification
                {
                    Chemistry = $"{chemistry} {entry:00}"
                },
                Tonnes = $"{(index * 17 + entry) % 100000:D5}.{(entry * 13) % 1000:D3}"
            }).ToList()
        };

        return new PomSubmissionModel(
            header,
            $"674a1b2c3d4e5f66{index:x8}",
            CreatedAt.AddMinutes(index),
            header.TonnageEntries.Select((entry, entryIndex) =>
                entry.ToStoredEntry($"{index:x8}{entryIndex:x8}674a1b2c", "Placed")).ToList(),
            "Submitted",
            1 + (index % 5));
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
