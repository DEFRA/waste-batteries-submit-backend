using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using WasteBatteriesSubmitBackend.Config;
using WasteBatteriesSubmitBackend.Tests.Common;
using WasteBatteriesSubmitBackend.Tests.Integration.Utils;

namespace WasteBatteriesSubmitBackend.Tests.Integration.PomSubmission;

public partial class PomSubmissionEndpointsTests
{
    [Fact]
    public async Task Get_WhenSubmissionIsUnknown_ReturnsNotFound()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();

        // Act
        var response = await client.GetAsync($"/pom-submissions/{SamplePomSubmission.SchemeOrgId}", cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_WhenCredentialsAreMissing_ReturnsUnauthorized()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateAnonymousClient();

        // Act
        var response = await client.GetAsync($"/pom-submissions/{SamplePomSubmission.SchemeOrgId}", cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_WhenTokenIsRejected_ReturnsUnauthorized()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-test-token");

        // Act
        var response = await client.GetAsync($"/pom-submissions/{SamplePomSubmission.SchemeOrgId}", cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_WhenServiceDevTokenIsPresented_IsNotChallenged()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateAnonymousClient();
        var token = fixture.Services.GetRequiredService<IOptions<ServiceAuthConfig>>().Value.DevToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync($"/pom-submissions/{SamplePomSubmission.SchemeOrgId}", cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("not-an-id")]
    [InlineData("674a1b2c3d4e5f678901234")]
    public async Task Get_WhenIdIsNotAnObjectId_ReturnsValidationError(string id)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();

        // Act
        var response = await client.GetAsync($"/pom-submissions/{id}", cancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("id must be an ObjectId.", problem.Errors["Id"]);
    }

    [Fact]
    public async Task Get_WhenSubmissionExists_ReturnsTheStoredSubmission()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();
        var body = CreateBody.Replace(
            PomSubmissionApiFixture.MemberOrgId,
            "674a1b2c3d4e5f6789012347",
            StringComparison.Ordinal);

        // Act
        var created = await client.PostAsync("/pom-submissions", Json(body), cancellationToken);
        var createdJson = await created.Content.ReadAsStringAsync(cancellationToken);
        var response = await client.GetAsync(created.Headers.Location, cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(createdJson, responseJson);
        Snapshot.Match(PinApiJson(responseJson, (ReadId(responseJson), 1)));
    }

    [Fact]
    public async Task Get_WhenSubmissionIsNotCurrent_ReturnsIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();
        const string memberOrgId = "674a1b2c3d4e5f678901234d";
        var body = CreateBody.Replace(
            PomSubmissionApiFixture.MemberOrgId,
            memberOrgId,
            StringComparison.Ordinal);
        var created = await client.PostAsync("/pom-submissions", Json(body), cancellationToken);
        using var createdDocument = JsonDocument.Parse(await created.Content.ReadAsStringAsync(cancellationToken));
        var id = createdDocument.RootElement.GetProperty("id").GetString()!;
        await fixture.Submissions.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(id)),
            Builders<BsonDocument>.Update.Set("isCurrent", false),
            cancellationToken: cancellationToken);

        // Act
        var response = await client.GetAsync($"/pom-submissions/{id}", cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Snapshot.Match(PinApiJson(json, (id, 1)));
    }
}
