using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using WasteBatteriesSubmitBackend.PomSubmission.Mapping;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.Tests.Common;
using WasteBatteriesSubmitBackend.Tests.Integration.Utils;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.Tests.Integration.PomSubmission;

public partial class PomSubmissionEndpointsTests
{
    [Fact]
    public async Task Create_WhenCredentialsAreMissing_ReturnsUnauthorized()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var countBefore = await SubmissionCount(cancellationToken);
        using var client = fixture.CreateAnonymousClient();

        // Act
        var response = await client.PostAsync("/pom-submissions", Json(CreateBody), cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNothingStored(countBefore, cancellationToken);
    }

    [Fact]
    public async Task Create_WhenTokenIsRejected_ReturnsUnauthorized()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var countBefore = await SubmissionCount(cancellationToken);
        using var client = fixture.CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-test-token");

        // Act
        var response = await client.PostAsync("/pom-submissions", Json(CreateBody), cancellationToken);

        // Assert
        // RequireAuthorization only checks that a principal exists. A token this API
        // does not accept is challenged with 401. There is no role policy that returns 403.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNothingStored(countBefore, cancellationToken);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"tonnageEntries":{"activity":"Placed"}}""")]
    [InlineData("""{"tonnageEntries":[{"activity":"Placed","tonnes":12.5}]}""")]
    [InlineData("""{"tonnageEntries":[{"classification":{"batteryCategory":"Portable","chemistry":"Alkaline"},"tonnes":"1.000"}]}""")]
    [InlineData("""{"tonnageEntries":[{"classification":{"chemistry":"Alkaline","subCategory":"General use"},"tonnes":"1.000"}]}""")]
    [InlineData("""{"tonnageEntries":[{"classification":{"chemistry":"Alkaline","cellType":"Primary"},"tonnes":"1.000"}]}""")]
    [InlineData("""{"tonnageEntries":[{"classification":{"chemistry":"Alkaline","equipmentState":"In equipment"},"tonnes":"1.000"}]}""")]
    public async Task Create_WhenJsonDoesNotMatchRequestShape_ReturnsBadRequest(string body)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var countBefore = await SubmissionCount(cancellationToken);
        using var client = fixture.CreateClient();

        // Act
        var response = await client.PostAsync("/pom-submissions", Json(body), cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNothingStored(countBefore, cancellationToken);
    }

    [Theory]
    [InlineData($$"""{"memberName":"{{SamplePomSubmission.MemberName}}","tonnageEntries":[]}""")]
    [InlineData($$"""{"memberName":"{{SamplePomSubmission.MemberName}}"}""")]
    [InlineData("""{"memberName":" ","tonnageEntries":[{"tonnes":"1.000"}]}""")]
    public async Task Create_WhenMemberNameOrTonnageIsMissing_ReturnsBadRequest(string body)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var countBefore = await SubmissionCount(cancellationToken);
        using var client = fixture.CreateClient();

        // Act
        var response = await client.PostAsync("/pom-submissions", Json(body), cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNothingStored(countBefore, cancellationToken);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    public async Task Create_WhenRequiredYearIsNullOrBlank_ReturnsValidationErrors(string year)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var countBefore = await SubmissionCount(cancellationToken);
        using var client = fixture.CreateClient();
        var classification = year == "null"
            ? "null"
            : """{ "chemistry": "" }""";
        var body = $$"""
            {
              "schemeOrgId": {{year}},
              "memberOrgId": {{year}},
              "memberName": {{year}},
              "memberBprn": {{year}},
              "schemeMembershipRef": {{year}},
              "compliancePeriodYear": {{year}},
              "compliancePeriodQuarter": {{year}},
              "dataBasis": {{year}},
              "tonnageEntries": [
                {
                  "classification": {{classification}},
                  "tonnes": {{year}}
                }
              ]
            }
            """;

        // Act
        var response = await client.PostAsync("/pom-submissions", Json(body), cancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("'Member Name' must not be empty.", problem.Errors["MemberName"]);
        Assert.Contains("compliancePeriodYear must be a calendar year.", problem.Errors["CompliancePeriodYear"]);
        Assert.Contains("'Tonnes' must not be empty.", problem.Errors["TonnageEntries[0].Tonnes"]);
        await AssertNothingStored(countBefore, cancellationToken);
    }

    [Fact]
    public async Task Create_WhenRequestHasManyFailures_ReturnsEveryValidationError()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var countBefore = await SubmissionCount(cancellationToken);
        using var client = fixture.CreateClient();
        var body = $$"""
            {
              "schemeOrgId": "not-an-id",
              "memberOrgId": "{{SamplePomSubmission.MemberOrgId}}",
              "memberName": "",
              "memberBprn": "BPRN-XX-2026-1",
              "schemeMembershipRef": "",
              "compliancePeriodYear": 0,
              "compliancePeriodQuarter": "",
              "dataBasis": "",
              "tonnageEntries": [
                {
                  "classification": { "chemistry": "" },
                  "tonnes": "12.5"
                }
              ]
            }
            """;

        // Act
        var response = await client.PostAsync("/pom-submissions", Json(body), cancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("SchemeOrgId", problem.Errors.Keys);
        Assert.Contains("MemberName", problem.Errors.Keys);
        Assert.Contains("MemberBprn", problem.Errors.Keys);
        Assert.Contains("SchemeMembershipRef", problem.Errors.Keys);
        Assert.Contains("CompliancePeriodYear", problem.Errors.Keys);
        Assert.Contains("CompliancePeriodQuarter", problem.Errors.Keys);
        Assert.Contains("DataBasis", problem.Errors.Keys);
        Assert.Contains(problem.Errors.Keys, key => key.Contains("Tonnes"));
        Assert.Contains(problem.Errors.Keys, key => key.Contains("Chemistry"));
        await AssertNothingStored(countBefore, cancellationToken);
    }

    [Fact]
    public async Task Create_WhenCurrentSubmissionAlreadyExists_ReturnsBadRequest()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();
        var body = CreateBody.Replace(
            PomSubmissionApiFixture.MemberOrgId,
            "674a1b2c3d4e5f6789012348",
            StringComparison.Ordinal);

        // Act
        var first = await client.PostAsync("/pom-submissions", Json(body), cancellationToken);
        var second = await client.PostAsync("/pom-submissions", Json(body), cancellationToken);
        var problem = await second.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);
        var stored = await fixture.Submissions.Find(
            Builders<BsonDocument>.Filter.Eq("memberOrgId", ObjectId.Parse("674a1b2c3d4e5f6789012348")))
            .ToListAsync(cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("Submission", problem.Errors.Keys);
        Assert.Single(stored);
    }

    [Fact]
    public async Task Create_WhenRequiredFieldIsMissing_ReturnsBadRequest()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var countBefore = await SubmissionCount(cancellationToken);
        using var client = fixture.CreateClient();
        var body = CreateBody.Replace(
            $"\"memberName\": \"{SamplePomSubmission.MemberName}\",",
            string.Empty,
            StringComparison.Ordinal);

        // Act
        var response = await client.PostAsync("/pom-submissions", Json(body), cancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("MemberName", problem.Errors.Keys);
        await AssertNothingStored(countBefore, cancellationToken);
    }

    [Fact]
    public async Task Create_WhenPropertyIsNotOnTheModel_ReturnsBadRequest()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var countBefore = await SubmissionCount(cancellationToken);
        using var client = fixture.CreateClient();
        var body = CreateBody.Replace(
            $"\"dataBasis\": \"{SamplePomSubmission.DataBasis}\",",
            $"\"dataBasis\": \"{SamplePomSubmission.DataBasis}\", \"regulatorNote\": \"ignore me\",",
            StringComparison.Ordinal);

        // Act
        var response = await client.PostAsync("/pom-submissions", Json(body), cancellationToken);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Bad request", problem.RootElement.GetProperty("title").GetString());
        Assert.Contains("JSON", problem.RootElement.GetProperty("detail").GetString());
        Assert.False(problem.RootElement.TryGetProperty("errors", out _));
        await AssertNothingStored(countBefore, cancellationToken);
    }

    [Fact]
    public async Task Create_WhenSubmissionIsValid_SavesItAndLogsIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();

        // Act
        var response = await client.PostAsync("/pom-submissions", Json(CreateBody), cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var responseDocument = JsonDocument.Parse(responseJson);
        var created = responseDocument.RootElement;
        var id = created.GetProperty("id").GetString();
        var storedDocuments = await fixture.Submissions.Find(
            Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(id!)))
            .ToListAsync(cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/pom-submissions/{id}", response.Headers.Location?.OriginalString);
        Assert.Equal(fixture.Defaults.InitialStatus, created.GetProperty("status").GetString());
        Assert.Equal(SamplePomSubmission.MemberName, created.GetProperty("memberName").GetString());
        Assert.Equal(PomSubmissionApiFixture.CreatedAt, created.GetProperty("submittedOnUtc").GetDateTime().ToUniversalTime());
        Assert.Equal(
            created.GetProperty("createdAtUtc").GetDateTime().ToUniversalTime(),
            created.GetProperty("submittedOnUtc").GetDateTime().ToUniversalTime());
        Assert.Equal(fixture.Defaults.InitialActivity, created.GetProperty("tonnageEntries")[0].GetProperty("activity").GetString());
        Assert.True(created.GetProperty("isCurrent").GetBoolean());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("supersedesSubmissionId").ValueKind);
        Assert.Equal(JsonValueKind.Null, created.GetProperty("updatedAtUtc").ValueKind);
        Assert.Equal(PomSubmissionModel.CurrentSchemaVersion, created.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(fixture.Defaults.InitialVersion, created.GetProperty("version").GetInt32());
        Assert.Equal(SamplePomSubmission.Tonnes, created.GetProperty("tonnageEntries")[0].GetProperty("tonnes").GetString());
        var classification = created.GetProperty("tonnageEntries")[0].GetProperty("classification");
        Assert.Equal(fixture.Defaults.InitialBatteryCategory, classification.GetProperty("batteryCategory").GetString());
        Assert.Equal(SamplePomSubmission.Chemistry, classification.GetProperty("chemistry").GetString());
        Assert.False(classification.TryGetProperty("subCategory", out _));
        Assert.False(classification.TryGetProperty("cellType", out _));
        Assert.False(classification.TryGetProperty("equipmentState", out _));
        Assert.Equal(PomSubmissionApiFixture.CreatedAt, created.GetProperty("createdAtUtc").GetDateTime().ToUniversalTime());
        Assert.Equal(PomSubmissionApiFixture.SchemeOrgId, created.GetProperty("schemeOrgId").GetString());
        Assert.Equal(PomSubmissionApiFixture.MemberOrgId, created.GetProperty("memberOrgId").GetString());

        var stored = Assert.Single(storedDocuments);
        AssertStoredShape(stored, id!);

        var log = Assert.Single(fixture.Logger.Entries, entry => entry.Message.Contains(id!, StringComparison.Ordinal));
        Assert.Equal(LogLevel.Information, log.Level);
        Assert.Equal(4, log.EventId.Id);
        Assert.Equal(
            $"Created POM submission {id} for scheme {PomSubmissionApiFixture.SchemeOrgId} member {PomSubmissionApiFixture.MemberOrgId}",
            log.Message);

        var snapshot = stored.DeepClone().AsBsonDocument;
        snapshot["_id"] = ObjectId.Parse("000000000000000000000001");
        snapshot["tonnageEntries"][0].AsBsonDocument["id"] = ObjectId.Parse("000000000000000000000002");
        var json = snapshot.ToJson(new JsonWriterSettings
        {
            OutputMode = JsonOutputMode.CanonicalExtendedJson,
            Indent = true,
            NewLineChars = "\n"
        });

        Snapshot.Match(json);
    }

    [Fact]
    public async Task Create_WhenTheCurrentSubmissionIndexRejectsTheInsert_ThrowsDuplicateKeyOnSaveException()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string memberOrgId = "674a1b2c3d4e5f67890124ff";
        var repository = fixture.Services.GetRequiredService<IPomSubmissionRepository>();
        var mapper = fixture.Services.GetRequiredService<IPomSubmissionMapper>();
        var request = SamplePomSubmission.Request() with { MemberOrgId = memberOrgId };
        var first = mapper.ToNewSubmission(request, PomSubmissionApiFixture.CreatedAt);
        var second = mapper.ToNewSubmission(request, PomSubmissionApiFixture.CreatedAt);

        try
        {
            // Act
            await repository.CreateAsync(first, cancellationToken);
            Task Act() => repository.CreateAsync(second, cancellationToken);

            // Assert
            await Assert.ThrowsAsync<DuplicateKeyOnSaveException>(Act);
        }
        finally
        {
            await fixture.Submissions.DeleteManyAsync(
                Builders<BsonDocument>.Filter.Eq("memberOrgId", ObjectId.Parse(memberOrgId)),
                cancellationToken);
        }
    }

    [Fact]
    public async Task Create_WhenTheSameVersionAlreadyExistsAndIsNotCurrent_ThrowsDuplicateKeyOnSaveException()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string memberOrgId = "674a1b2c3d4e5f6789012501";
        var repository = fixture.Services.GetRequiredService<IPomSubmissionRepository>();
        var mapper = fixture.Services.GetRequiredService<IPomSubmissionMapper>();
        var request = SamplePomSubmission.Request() with { MemberOrgId = memberOrgId };
        var first = mapper.ToNewSubmission(request, PomSubmissionApiFixture.CreatedAt);

        try
        {
            await repository.CreateAsync(first, cancellationToken);
            var nextVersion = mapper.ToNewSubmission(
                request,
                PomSubmissionApiFixture.CreatedAt,
                version: fixture.Defaults.InitialVersion + 1);

            // Act
            await ClearCurrent(first.Id, cancellationToken);
            await repository.CreateAsync(nextVersion, cancellationToken);
            await ClearCurrent(nextVersion.Id, cancellationToken);
            var repeatedVersion = mapper.ToNewSubmission(request, PomSubmissionApiFixture.CreatedAt);
            Task Act() => repository.CreateAsync(repeatedVersion, cancellationToken);

            // Assert
            await Assert.ThrowsAsync<DuplicateKeyOnSaveException>(Act);
            Assert.Equal(
                2,
                await fixture.Submissions.CountDocumentsAsync(
                    Builders<BsonDocument>.Filter.Eq("memberOrgId", ObjectId.Parse(memberOrgId)),
                    cancellationToken: cancellationToken));
        }
        finally
        {
            await fixture.Submissions.DeleteManyAsync(
                Builders<BsonDocument>.Filter.Eq("memberOrgId", ObjectId.Parse(memberOrgId)),
                cancellationToken);
        }
    }

    private async Task ClearCurrent(string id, CancellationToken cancellationToken)
    {
        var cleared = await fixture.Submissions.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(id)),
            Builders<BsonDocument>.Update.Set("isCurrent", false),
            cancellationToken: cancellationToken);
        Assert.Equal(1, cleared.ModifiedCount);
    }
}
