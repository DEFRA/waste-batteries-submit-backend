using MongoDB.Bson;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WasteBatteriesSubmitBackend.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;

namespace WasteBatteriesSubmitBackend.Tests.Common;

// The long submission the page-size limits were measured against. Distinct rows differ, because identical
// copies compress far more than a real list. Full is the index-0 row and is repeated for the uncompressed limit.
// The list endpoint returns this JSON in list order: scheme, member, latest year, quarter and version,
// then newest created, then id.
public static class MeasuredPomSubmission
{
    public const int TonnageEntryCount = 80;

    // The scheme id the gzip page measurement and the endpoint comparison both use.
    public const string SchemeOrgId = "674a1b2c3d4e5f6789012c12";

    public static readonly DateTime CreatedAt = SamplePomSubmission.CreatedAt;

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    public static PomSubmissionModel[] Page(
        string schemeOrgId,
        int count,
        string activity,
        string status,
        int version,
        string batteryCategory)
    {
        var items = new PomSubmissionModel[count];
        for (var index = 0; index < count; index++)
        {
            items[index] = Distinct(schemeOrgId, index, activity, status, version, batteryCategory);
        }

        Array.Sort(items, CompareListOrder);
        return items;
    }

    public static string PageJson(IReadOnlyList<PomSubmissionModel> items, int totalCount, int pageNumber, int pageSize)
    {
        using var stream = new MemoryStream();
        WritePage(stream, items, totalCount, pageNumber, pageSize);
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    public static int PageBytes(IReadOnlyList<PomSubmissionModel> items, int totalCount, int pageNumber, int pageSize)
    {
        using var stream = new MemoryStream();
        WritePage(stream, items, totalCount, pageNumber, pageSize);
        return (int)stream.Length;
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

    // Distinct at index 0. Repeating it measures the uncompressed page; the row is the same length as the gzip rows.
    public static PomSubmissionModel Full(string activity, string status, int version, string batteryCategory) =>
        Distinct(SchemeOrgId, 0, activity, status, version, batteryCategory);

    // The posted JSON for the upsert batch limit. Repeating index 0 measures the uncompressed request.
    // Tonnes stay in the form a client sends. The stored page runs them through Decimal128 first.
    public static int BatchRequestBytes(int count)
    {
        var requests = new PomSubmissionRequest[count];
        Array.Fill(requests, Posted(0));
        return JsonSerializer.SerializeToUtf8Bytes(requests, s_jsonOptions).Length;
    }

    public static PomSubmissionRequest Posted(int index)
    {
        const string chemistry = "Nickel-metal hydride";
        return new PomSubmissionRequest
        {
            SchemeOrgId = SchemeOrgId,
            MemberOrgId = $"674a1b2c3d4e5f68{index:x8}",
            MemberName = $"{index:D6} " + new string((char)('A' + (index % 26)), 153),
            MemberBprn = $"BPRN-NIEA-2026-{(index % 1000000):D6}",
            SchemeMembershipRef = $"SCH-{index:D36}",
            CompliancePeriodYear = 2020 + (index % 7),
            CompliancePeriodQuarter = $"Quarter {(index % 4) + 1}",
            DataBasis = index % 2 == 0 ? "Estimated" : "Actual",
            TonnageEntries = [.. Enumerable.Range(1, TonnageEntryCount).Select(entry => new TonnageEntryBase
            {
                Classification = new BatteryClassificationBase
                {
                    Chemistry = $"{chemistry} {entry:00}"
                },
                Tonnes = $"{10000 + ((index * 17 + entry) % 90000):D5}.{(entry * 13) % 1000:D3}"
            })]
        };
    }

    // Each submission differs. Identical copies compress far more than a real list.
    public static PomSubmissionModel Distinct(
        string schemeOrgId,
        int index,
        string activity,
        string status,
        int version,
        string batteryCategory)
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
                Classification = new BatteryClassificationBase
                {
                    Chemistry = $"{chemistry} {entry:00}"
                },
                Tonnes = StoredTonnes($"{10000 + ((index * 17 + entry) % 90000):D5}.{(entry * 13) % 1000:D3}")
            })]
        };

        return Stored(header, index, CreatedAt.AddMinutes(index), activity, status, version, batteryCategory);
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

    // Same order as PomSubmissionRepository.ListOrder.
    private static int CompareListOrder(PomSubmissionModel left, PomSubmissionModel right)
    {
        var byScheme = string.CompareOrdinal(left.SchemeOrgId, right.SchemeOrgId);
        if (byScheme != 0)
        {
            return byScheme;
        }

        var byMember = string.CompareOrdinal(left.MemberOrgId, right.MemberOrgId);
        if (byMember != 0)
        {
            return byMember;
        }

        var byYear = right.CompliancePeriodYear.CompareTo(left.CompliancePeriodYear);
        if (byYear != 0)
        {
            return byYear;
        }

        var byQuarter = string.CompareOrdinal(right.CompliancePeriodQuarter, left.CompliancePeriodQuarter);
        if (byQuarter != 0)
        {
            return byQuarter;
        }

        var byVersion = right.Version.CompareTo(left.Version);
        if (byVersion != 0)
        {
            return byVersion;
        }

        var byTime = right.CreatedAtUtc.CompareTo(left.CreatedAtUtc);
        if (byTime != 0)
        {
            return byTime;
        }

        return string.CompareOrdinal(left.Id, right.Id);
    }

    // The API writes tonnes back with Decimal128.ToString() after they have been stored.
    private static string StoredTonnes(string tonnes) => Decimal128.Parse(tonnes).ToString();

    private static PomSubmissionModel Stored(
        PomSubmissionRequest header,
        int index,
        DateTime createdAt,
        string activity,
        string status,
        int version,
        string batteryCategory) =>
        new(
            header,
            $"674a1b2c3d4e5f69{index:x8}",
            createdAt,
            [.. header.TonnageEntries.Select((entry, entryIndex) =>
                entry.ToStoredEntry($"{index:x8}{entryIndex:x8}674a1b2c", activity, batteryCategory))],
            status,
            version);
}
