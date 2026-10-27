using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.Tests.Integration.PomSubmission;

// A full page of the long submission from the README, read back through GET /pom-submissions.
// The plain body uses MaxPageSize. The gzip body uses MaxGzipPageSize. Both must stay under 10 MiB.
// Recalculate both settings when PomSubmissionModel changes. These tests fail until that is done.
public partial class PomSubmissionEndpointsTests
{
    private const int PayloadTonnageEntries = 80;

    private const int PayloadMemberNameLength = 160;

    private const int PayloadChemistryLength = 23;

    private const int PayloadTonnesLength = 9;

    private const int CdpPayloadLimitBytes = 10 * 1024 * 1024;

    private const string PlainSchemeOrgId = "674a1b2c3d4e5f6789012c11";

    private const string GzipSchemeOrgId = "674a1b2c3d4e5f6789012c12";

    [Fact(Timeout = 180_000)]
    public async Task List_WhenGzipIsNotAccepted_ReturnsTheFullPageUnderThePayloadLimit()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var count = fixture.Defaults.MaxPageSize;
        await InsertMeasuredPage(PlainSchemeOrgId, count, distinct: false, cancellationToken);
        using var client = PayloadClient(acceptsGzip: false);

        try
        {
            // Act
            var response = await client.GetAsync($"/pom-submissions?schemeOrgId={PlainSchemeOrgId}", cancellationToken);
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(response.Content.Headers.ContentEncoding);
            AssertUnderPayloadLimit(body.Length, count, "plain JSON");
            AssertFullPage(body, count);
        }
        finally
        {
            await DeleteMeasuredPage(PlainSchemeOrgId, CancellationToken.None);
        }
    }

    [Fact(Timeout = 600_000)]
    public async Task List_WhenGzipIsAccepted_ReturnsTheFullPageUnderThePayloadLimit()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var count = fixture.Defaults.MaxGzipPageSize;
        await InsertMeasuredPage(GzipSchemeOrgId, count, distinct: true, cancellationToken);
        using var client = PayloadClient(acceptsGzip: true);

        try
        {
            // Act
            var response = await client.GetAsync($"/pom-submissions?schemeOrgId={GzipSchemeOrgId}", cancellationToken);
            var compressed = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("gzip", Assert.Single(response.Content.Headers.ContentEncoding));
            AssertUnderPayloadLimit(compressed.Length, count, "gzip");
            AssertFullPage(Gunzip(compressed), count);
        }
        finally
        {
            await DeleteMeasuredPage(GzipSchemeOrgId, CancellationToken.None);
        }
    }

    private HttpClient PayloadClient(bool acceptsGzip)
    {
        var client = fixture.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.AcceptEncoding.Clear();
        if (acceptsGzip)
        {
            client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        }

        return client;
    }

    private async Task InsertMeasuredPage(
        string schemeOrgId,
        int count,
        bool distinct,
        CancellationToken cancellationToken)
    {
        var collection = MeasuredSubmissions();
        const int batchSize = 250;
        for (var start = 0; start < count; start += batchSize)
        {
            var take = Math.Min(batchSize, count - start);
            var batch = new PomSubmissionModel[take];
            for (var offset = 0; offset < take; offset++)
            {
                var index = start + offset;
                batch[offset] = distinct
                    ? DistinctMeasuredSubmission(schemeOrgId, index)
                    : FullMeasuredSubmission(schemeOrgId, index);
            }

            await collection.InsertManyAsync(batch, cancellationToken: cancellationToken);
        }
    }

    private Task DeleteMeasuredPage(string schemeOrgId, CancellationToken cancellationToken) =>
        MeasuredSubmissions().DeleteManyAsync(
            Builders<PomSubmissionModel>.Filter.Eq(submission => submission.SchemeOrgId, schemeOrgId),
            cancellationToken);

    private IMongoCollection<PomSubmissionModel> MeasuredSubmissions() =>
        fixture.Services
            .GetRequiredService<IMongoDbClientFactory>()
            .GetCollection<PomSubmissionModel>(PomSubmissionRepository.CollectionName);

    private static void AssertUnderPayloadLimit(int bytes, int count, string encoding) =>
        Assert.True(
            bytes < CdpPayloadLimitBytes,
            $"The {encoding} list of {count} submissions is {bytes} bytes, which is over the {CdpPayloadLimitBytes} byte limit.");

    private static void AssertFullPage(byte[] json, int count)
    {
        var page = ReadListPayload(json);
        Assert.Equal(count, page.PageSize);
        Assert.Equal(count, page.TotalCount);
        Assert.Equal(count, page.ItemCount);
        Assert.Equal(PayloadTonnageEntries, page.FirstEntryCount);
        Assert.Equal(PayloadMemberNameLength, page.FirstMemberNameLength);
        Assert.Equal(PayloadChemistryLength, page.FirstChemistryLength);
        Assert.Equal(PayloadTonnesLength, page.FirstTonnesLength);
    }

    private static byte[] Gunzip(byte[] compressed)
    {
        using var gzip = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    // Long header text and a long chemistry on every entry. Ids differ so the current-submission index allows the page.
    private PomSubmissionModel FullMeasuredSubmission(string schemeOrgId, int index)
    {
        const string chemistry = "Nickel-metal hydride";
        var header = new PomSubmissionRequest
        {
            SchemeOrgId = schemeOrgId,
            MemberOrgId = $"674a1b2c3d4e5f68{index:x8}",
            MemberName = new string('N', PayloadMemberNameLength),
            MemberBprn = "BPRN-NIEA-2026-000123",
            SchemeMembershipRef = new string('S', 40),
            CompliancePeriodYear = 2026,
            CompliancePeriodQuarter = "Quarter 2",
            DataBasis = "Estimated",
            TonnageEntries = Enumerable.Range(1, PayloadTonnageEntries).Select(entry => new TonnageEntryBase
            {
                Classification = new BatteryClassification
                {
                    Chemistry = $"{chemistry} {entry:00}"
                },
                Tonnes = "99999.999"
            }).ToList()
        };

        return StoredSubmission(header, index, PomSubmissionApiFixture.CreatedAt);
    }

    // Each submission differs. Identical copies compress far more than a real list.
    private PomSubmissionModel DistinctMeasuredSubmission(string schemeOrgId, int index)
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
            TonnageEntries = Enumerable.Range(1, PayloadTonnageEntries).Select(entry => new TonnageEntryBase
            {
                Classification = new BatteryClassification
                {
                    Chemistry = $"{chemistry} {entry:00}"
                },
                Tonnes = $"{10000 + ((index * 17 + entry) % 90000):D5}.{(entry * 13) % 1000:D3}"
            }).ToList()
        };

        return StoredSubmission(header, index, PomSubmissionApiFixture.CreatedAt.AddMinutes(index));
    }

    private PomSubmissionModel StoredSubmission(PomSubmissionRequest header, int index, DateTime createdAt) =>
        new(
            header,
            $"674a1b2c3d4e5f69{index:x8}",
            createdAt,
            header.TonnageEntries.Select((entry, entryIndex) =>
                entry.ToStoredEntry($"{index:x8}{entryIndex:x8}674a1b2c", fixture.Defaults.InitialActivity)).ToList(),
            fixture.Defaults.InitialStatus,
            fixture.Defaults.InitialVersion);

    private static ListPayload ReadListPayload(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json, isFinalBlock: true, state: default);
        var pageSize = -1;
        var totalCount = -1;
        var itemCount = 0;
        var firstEntryCount = -1;
        var firstMemberNameLength = -1;
        var firstChemistryLength = -1;
        var firstTonnesLength = -1;
        var inItems = false;
        var itemDepth = -1;
        var inFirstItem = false;
        var firstItemDepth = -1;
        var inFirstEntries = false;
        var entriesDepth = -1;
        string? property = null;

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    property = reader.GetString();
                    break;
                case JsonTokenType.StartArray when property == "items" && !inItems:
                    inItems = true;
                    itemDepth = reader.CurrentDepth;
                    property = null;
                    break;
                case JsonTokenType.StartObject when inItems && reader.CurrentDepth == itemDepth + 1:
                    itemCount++;
                    if (itemCount == 1)
                    {
                        inFirstItem = true;
                        firstItemDepth = reader.CurrentDepth;
                    }

                    property = null;
                    break;
                case JsonTokenType.EndObject when inFirstItem && reader.CurrentDepth == firstItemDepth:
                    inFirstItem = false;
                    property = null;
                    break;
                case JsonTokenType.EndArray when inItems && reader.CurrentDepth == itemDepth:
                    inItems = false;
                    property = null;
                    break;
                case JsonTokenType.StartArray when inFirstItem && property == "tonnageEntries" && firstEntryCount < 0:
                    inFirstEntries = true;
                    entriesDepth = reader.CurrentDepth;
                    firstEntryCount = 0;
                    property = null;
                    break;
                case JsonTokenType.StartObject when inFirstEntries && reader.CurrentDepth == entriesDepth + 1:
                    firstEntryCount++;
                    property = null;
                    break;
                case JsonTokenType.EndArray when inFirstEntries && reader.CurrentDepth == entriesDepth:
                    inFirstEntries = false;
                    property = null;
                    break;
                case JsonTokenType.String when inFirstItem && !inFirstEntries && property == "memberName" && firstMemberNameLength < 0:
                    firstMemberNameLength = reader.GetString()!.Length;
                    property = null;
                    break;
                case JsonTokenType.String when inFirstEntries && property == "chemistry" && firstChemistryLength < 0:
                    firstChemistryLength = reader.GetString()!.Length;
                    property = null;
                    break;
                case JsonTokenType.String when inFirstEntries && property == "tonnes" && firstTonnesLength < 0:
                    firstTonnesLength = reader.GetString()!.Length;
                    property = null;
                    break;
                case JsonTokenType.Number when !inItems && property == "pageSize":
                    pageSize = reader.GetInt32();
                    property = null;
                    break;
                case JsonTokenType.Number when !inItems && property == "totalCount":
                    totalCount = reader.GetInt32();
                    property = null;
                    break;
                default:
                    if (reader.TokenType is not (JsonTokenType.PropertyName or JsonTokenType.StartObject or JsonTokenType.StartArray))
                    {
                        property = null;
                    }

                    break;
            }
        }

        return new ListPayload(
            pageSize,
            totalCount,
            itemCount,
            firstEntryCount,
            firstMemberNameLength,
            firstChemistryLength,
            firstTonnesLength);
    }

    private sealed record ListPayload(
        int PageSize,
        int TotalCount,
        int ItemCount,
        int FirstEntryCount,
        int FirstMemberNameLength,
        int FirstChemistryLength,
        int FirstTonnesLength);
}
