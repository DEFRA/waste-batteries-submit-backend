using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WasteBatteriesSubmitBackend.Tests.Integration.PomSubmission;

public partial class PomSubmissionEndpointsTests
{
    [Fact]
    public async Task Get_WhenTheClientAcceptsGzip_ReturnsAGzipBody()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = GzipClient();
        const string schemeOrgId = "674a1b2c3d4e5f6789012901";
        var created = await CreateSubmission(
            client,
            schemeOrgId,
            "674a1b2c3d4e5f6789012911",
            2026,
            "Quarter 2",
            cancellationToken);

        // Act
        var byId = await client.GetAsync($"/pom-submissions/{created.Id}", cancellationToken);
        var list = await client.GetAsync($"/pom-submissions?schemeOrgId={schemeOrgId}", cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, byId.StatusCode);
        Assert.Equal("gzip", Assert.Single(byId.Content.Headers.ContentEncoding));
        Assert.Contains(created.Id, await Gunzip(byId, cancellationToken));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal("gzip", Assert.Single(list.Content.Headers.ContentEncoding));
        var listJson = await Gunzip(list, cancellationToken);
        Assert.Contains(created.Id, listJson);
        using var page = JsonDocument.Parse(listJson);
        Assert.Equal(fixture.Defaults.MaxGzipPageSize, page.RootElement.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task List_WhenTheClientAcceptsGzip_UsesTheLargerPageSize()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = GzipClient();
        const string schemeOrgId = "674a1b2c3d4e5f6789012931";
        var betweenTheLimits = fixture.Defaults.MaxPageSize + 50;

        // Act
        using var omitted = await ReadGzipPage(client, cancellationToken, ("schemeOrgId", schemeOrgId));
        using var between = await ReadGzipPage(
            client,
            cancellationToken,
            ("schemeOrgId", schemeOrgId),
            ("pageSize", betweenTheLimits.ToString()));
        using var above = await ReadGzipPage(
            client,
            cancellationToken,
            ("schemeOrgId", schemeOrgId),
            ("pageSize", (fixture.Defaults.MaxGzipPageSize + 50).ToString()));

        // Assert
        AssertPage(omitted.RootElement, 1, fixture.Defaults.MaxGzipPageSize, 0, 0, false, false);
        AssertPage(between.RootElement, 1, betweenTheLimits, 0, 0, false, false);
        AssertPage(above.RootElement, 1, fixture.Defaults.MaxGzipPageSize, 0, 0, false, false);
    }

    [Fact]
    public async Task Create_WhenTheClientAcceptsGzip_ReturnsPlainJson()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = GzipClient();
        var body = CreateBody
            .Replace(PomSubmissionApiFixture.SchemeOrgId, "674a1b2c3d4e5f6789012902", StringComparison.Ordinal)
            .Replace(PomSubmissionApiFixture.MemberOrgId, "674a1b2c3d4e5f6789012921", StringComparison.Ordinal);

        // Act
        var response = await client.PostAsync("/pom-submissions", Json(body), cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Empty(response.Content.Headers.ContentEncoding);
        Assert.StartsWith("{", json, StringComparison.Ordinal);
    }

    private HttpClient GzipClient()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        return client;
    }

    private static async Task<JsonDocument> ReadGzipPage(
        HttpClient client,
        CancellationToken cancellationToken,
        params (string Key, string Value)[] query)
    {
        var response = await client.GetAsync(SubmissionsUrl(query), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("gzip", Assert.Single(response.Content.Headers.ContentEncoding));
        return JsonDocument.Parse(await Gunzip(response, cancellationToken));
    }

    private static async Task<string> Gunzip(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var compressed = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        using var gzip = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
