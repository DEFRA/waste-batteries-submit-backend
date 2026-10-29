using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MongoDB.Bson;
using WasteBatteriesSubmitBackend.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;

namespace WasteBatteriesSubmitBackend.Tests.Common;

// The long submission the page-size limits were measured against. Distinct submissions differ, because identical
// copies compress far more than a real list. The list endpoint returns this JSON, newest first.
public static class MeasuredPomSubmission
{
    public const int TonnageEntryCount = 80;

    // The scheme id the gzip page measurement and the endpoint comparison both use.
    public const string SchemeOrgId = "674a1b2c3d4e5f6789012c12";

    public static readonly DateTime CreatedAt = new(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    public static PomSubmissionModel[] Page(
        string schemeOrgId,
        int count,
        string activity,
        string status,
        int version)
    {
        var items = new PomSubmissionModel[count];
        for (var index = 0; index < count; index++)
        {
            items[index] = Distinct(schemeOrgId, index, activity, status, version);
        }

        Array.Sort(items, CompareNewestFirst);
        return items;
    }

    public static string PageJson(IReadOnlyList<PomSubmissionModel> items, int totalCount, int pageNumber, int pageSize)
    {
        using var stream = new MemoryStream();
        WritePage(stream, items, totalCount, pageNumber, pageSize);
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    public static int GzipPage(IReadOnlyList<PomSubmissionModel> items, int totalCount, int pageNumber, int pageSize)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            WritePage(gzip, items, totalCount, pageNumber, pageSize);
        }

        return (int)output.Length;
    }

    // Each submission differs. Identical copies compress far more than a real list.
    public static PomSubmissionModel Distinct(string schemeOrgId, int index, string activity, string status, int version)
    {
        const string chemistry = "Nickel-metal hydride";
        var header = new PomSubmissionRequest
        {
            SchemeOrgId = schemeOrgId,
            MemberOrgId = $"674a1b2c3d4e5f68{index:x8}",
            MemberName = $"{index:D6} " + new string((char)('A' + (index % 26)), 153),
            MemberBprn = $"BPRN-NIEA-2026-{(index % 1000000):D6}",
            SchemeMembershipRef = $"SCH-{index:D36}",
            CompliancePeriodYear = 2020 + (index % 7),
            CompliancePeriodQuarter = $"Quarter {(index % 4) + 1}",
            DataBasis = index % 2 == 0 ? "Estimated" : "Actual",
            TonnageEntries = [.. Enumerable.Range(1, TonnageEntryCount).Select(entry => new TonnageEntryBase
            {
                Classification = new BatteryClassification
                {
                    Chemistry = $"{chemistry} {entry:00}"
                },
                Tonnes = StoredTonnes($"{10000 + ((index * 17 + entry) % 90000):D5}.{(entry * 13) % 1000:D3}")
            })]
        };

        return Stored(header, index, CreatedAt.AddMinutes(index), activity, status, version);
    }

    private static void WritePage(
        Stream stream,
        IReadOnlyList<PomSubmissionModel> items,
        int totalCount,
        int pageNumber,
        int pageSize) =>
        JsonSerializer.Serialize(
            stream,
            new PaginatedList<PomSubmissionModel>(items, totalCount, pageNumber, pageSize),
            s_jsonOptions);

    private static int CompareNewestFirst(PomSubmissionModel left, PomSubmissionModel right)
    {
        var byTime = right.CreatedAtUtc.CompareTo(left.CreatedAtUtc);
        return byTime != 0 ? byTime : string.CompareOrdinal(right.Id, left.Id);
    }

    // The API writes tonnes back with Decimal128.ToString() after they have been stored.
    private static string StoredTonnes(string tonnes) => Decimal128.Parse(tonnes).ToString();

    private static PomSubmissionModel Stored(
        PomSubmissionRequest header,
        int index,
        DateTime createdAt,
        string activity,
        string status,
        int version) =>
        new(
            header,
            $"674a1b2c3d4e5f69{index:x8}",
            createdAt,
            [.. header.TonnageEntries.Select((entry, entryIndex) =>
                entry.ToStoredEntry($"{index:x8}{entryIndex:x8}674a1b2c", activity))],
            status,
            version);
}
