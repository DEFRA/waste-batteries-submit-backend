using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.Tests.Common;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.Tests.Integration.PomSubmission;

// The plain list stores a full page and fails when that response is not under 10 MiB.
// The gzip list stores two of the distinct submissions the unit test gzips, and fails when the
// endpoint JSON is not that JSON. The unit test then fails when a full gzip page is not under 10 MiB.
// Recalculate both settings when PomSubmissionModel changes.
public partial class PomSubmissionEndpointsTests
{
    private const int PayloadTonnageEntries = 80;

    private const int PayloadMemberNameLength = 160;

    private const int PayloadChemistryLength = 23;

    private const int PayloadTonnesLength = 9;

    private const int CdpPayloadLimitBytes = 10 * 1024 * 1024;

    private const string PlainSchemeOrgId = "674a1b2c3d4e5f6789012c11";

    private const string GzipSchemeOrgId = MeasuredPomSubmission.SchemeOrgId;

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

    [Fact]
    public async Task List_WhenGzipIsAccepted_ReturnsTheMeasuredSubmissionJson()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const int count = 2;
        await InsertMeasuredPage(GzipSchemeOrgId, count, distinct: true, cancellationToken);
        using var client = PayloadClient(acceptsGzip: true);
        var items = MeasuredPomSubmission.Page(
            GzipSchemeOrgId,
            count,
            fixture.Defaults.InitialActivity,
            fixture.Defaults.InitialStatus,
            fixture.Defaults.InitialVersion,
            distinct: true);

        try
        {
            // Act
            var response = await client.GetAsync($"/pom-submissions?schemeOrgId={GzipSchemeOrgId}", cancellationToken);
            var compressed = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("gzip", Assert.Single(response.Content.Headers.ContentEncoding));
            Assert.Equal(
                MeasuredPomSubmission.PageJson(items, count, pageNumber: 1, fixture.Defaults.MaxGzipPageSize),
                Encoding.UTF8.GetString(Gunzip(compressed)));
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
                    ? MeasuredPomSubmission.Distinct(
                        schemeOrgId,
                        index,
                        fixture.Defaults.InitialActivity,
                        fixture.Defaults.InitialStatus,
                        fixture.Defaults.InitialVersion)
                    : MeasuredPomSubmission.Full(
                        schemeOrgId,
                        index,
                        fixture.Defaults.InitialActivity,
                        fixture.Defaults.InitialStatus,
                        fixture.Defaults.InitialVersion);
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
