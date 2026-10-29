using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.Tests.Common;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.Tests.Integration.PomSubmission;

// The checks to ensure that the payload does not exceed the expected limit is done in the PomSubmissionPageBudgetTests unit tests, since it was slow todo here
public partial class PomSubmissionEndpointsTests
{
    private const string GzipSchemeOrgId = MeasuredPomSubmission.SchemeOrgId;

    [Fact]
    public async Task List_WhenGzipIsAccepted_ReturnsTheMeasuredSubmissionJson()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const int count = 2;
        var items = MeasuredPomSubmission.Page(
            GzipSchemeOrgId,
            count,
            fixture.Defaults.InitialActivity,
            fixture.Defaults.InitialStatus,
            fixture.Defaults.InitialVersion);
        await MeasuredSubmissions().InsertManyAsync(items, cancellationToken: cancellationToken);
        using var client = PayloadClient();

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
            await MeasuredSubmissions().DeleteManyAsync(
                Builders<PomSubmissionModel>.Filter.Eq(submission => submission.SchemeOrgId, GzipSchemeOrgId),
                CancellationToken.None);
        }
    }

    private HttpClient PayloadClient()
    {
        var client = fixture.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.AcceptEncoding.Clear();
        client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        return client;
    }

    private IMongoCollection<PomSubmissionModel> MeasuredSubmissions() =>
        fixture.Services
            .GetRequiredService<IMongoDbClientFactory>()
            .GetCollection<PomSubmissionModel>(PomSubmissionRepository.CollectionName);

    private static byte[] Gunzip(byte[] compressed)
    {
        using var gzip = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }
}
