using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;
using WasteBatteriesSubmitBackend.PomSubmission.Models;

namespace WasteBatteriesSubmitBackend.Tests.Integration.PomSubmission;

public class PomSubmissionEndpointsTests(PomSubmissionApiFixture fixture) : IClassFixture<PomSubmissionApiFixture>
{
    private const string CreateBody = """
        {
          "schemeOrgId": "674a1b2c3d4e5f6789012345",
          "memberOrgId": "674a1b2c3d4e5f6789012346",
          "memberName": "Northwind Batteries Ltd",
          "memberBprn": "BPRN-EA-2026-000123",
          "schemeMembershipRef": "SCH-00417",
          "compliancePeriodYear": 2026,
          "compliancePeriodQuarter": "Quarter 2",
          "dataBasis": "Actual",
          "tonnageEntries": [
            {
              "classification": {
                "batteryCategory": "Portable",
                "chemistry": "Alkaline",
                "subCategory": "General use",
                "cellType": "Primary",
                "equipmentState": "In equipment"
              },
              "tonnes": "12.500"
            }
          ]
        }
        """;

    [Fact]
    public async Task Get_WhenSubmissionIsUnknown_ReturnsNotFound()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();

        // Act
        var response = await client.GetAsync("/pom-submissions/674a1b2c3d4e5f6789012345", cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhenCredentialsAreMissing_ReturnsUnauthorized()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateAnonymousClient();

        // Act
        var response = await client.PostAsync("/pom-submissions", Json(CreateBody), cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNothingStored(cancellationToken);
    }

    [Fact]
    public async Task Create_WhenTokenIsRejected_ReturnsUnauthorized()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-test-token");

        // Act
        var response = await client.PostAsync("/pom-submissions", Json(CreateBody), cancellationToken);

        // Assert
        // RequireAuthorization only checks that a principal exists. A token this API
        // does not accept is challenged with 401. There is no role policy that returns 403.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNothingStored(cancellationToken);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"tonnageEntries":{"activity":"Placed"}}""")]
    [InlineData("""{"tonnageEntries":[{"activity":"Placed","tonnes":12.5}]}""")]
    public async Task Create_WhenJsonDoesNotMatchRequestShape_ReturnsBadRequest(string body)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();

        // Act
        var response = await client.PostAsync("/pom-submissions", Json(body), cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNothingStored(cancellationToken);
    }

    [Theory]
    [InlineData("""{"memberName":"Northwind Batteries Ltd","tonnageEntries":[]}""")]
    [InlineData("""{"memberName":"Northwind Batteries Ltd"}""")]
    [InlineData("""{"memberName":" ","tonnageEntries":[{"tonnes":"1.000"}]}""")]
    public async Task Create_WhenMemberNameOrTonnageIsMissing_ReturnsBadRequest(string body)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();

        // Act
        var response = await client.PostAsync("/pom-submissions", Json(body), cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNothingStored(cancellationToken);
    }

    [Fact]
    public async Task Create_WhenRequestHasManyFailures_ReturnsEveryValidationError()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();
        var body = """
            {
              "schemeOrgId": "not-an-id",
              "memberOrgId": "674a1b2c3d4e5f6789012346",
              "memberName": "",
              "memberBprn": "BPRN-XX-2026-1",
              "schemeMembershipRef": "",
              "compliancePeriodYear": 0,
              "compliancePeriodQuarter": "",
              "dataBasis": "",
              "tonnageEntries": [
                {
                  "classification": { "batteryCategory": "", "chemistry": "" },
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
        Assert.Contains(problem.Errors.Keys, key => key.Contains("BatteryCategory"));
        Assert.Contains(problem.Errors.Keys, key => key.Contains("Chemistry"));
        await AssertNothingStored(cancellationToken);
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
    public async Task Create_WhenOptionalPropertyIsOmitted_StoresItAsNull()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();

        // Act
        var missing = await client.PostAsync("/pom-submissions", Json(OptionalBody("674a1b2c3d4e5f6789012349", includeNulls: false)), cancellationToken);
        var withNull = await client.PostAsync("/pom-submissions", Json(OptionalBody("674a1b2c3d4e5f678901234a", includeNulls: true)), cancellationToken);
        var omitted = await FindByMember("674a1b2c3d4e5f6789012349", cancellationToken);
        var explicitNull = await FindByMember("674a1b2c3d4e5f678901234a", cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Created, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Created, withNull.StatusCode);
        Assert.Equal(Canonical(omitted), Canonical(explicitNull));
        Assert.Equal(BsonNull.Value, omitted["tonnageEntries"][0]["classification"]["subCategory"]);
        Assert.Equal(BsonNull.Value, omitted["tonnageEntries"][0]["classification"]["cellType"]);
        Assert.Equal(BsonNull.Value, omitted["tonnageEntries"][0]["classification"]["equipmentState"]);
    }

    [Fact]
    public async Task Create_WhenRequiredFieldIsMissing_ReturnsBadRequest()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();
        var body = CreateBody.Replace("\"memberName\": \"Northwind Batteries Ltd\",", string.Empty, StringComparison.Ordinal);

        // Act
        var response = await client.PostAsync("/pom-submissions", Json(body), cancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("MemberName", problem.Errors.Keys);
        await AssertNothingStored(cancellationToken);
    }

    [Fact]
    public async Task Create_WhenPropertyIsNotOnTheModel_ReturnsBadRequest()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();
        var body = CreateBody.Replace(
            "\"dataBasis\": \"Actual\",",
            "\"dataBasis\": \"Actual\", \"regulatorNote\": \"ignore me\",",
            StringComparison.Ordinal);

        // Act
        var response = await client.PostAsync("/pom-submissions", Json(body), cancellationToken);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Bad request", problem.RootElement.GetProperty("title").GetString());
        Assert.Contains("JSON", problem.RootElement.GetProperty("detail").GetString());
        Assert.False(problem.RootElement.TryGetProperty("errors", out _));
        await AssertNothingStored(cancellationToken);
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
        Assert.Equal("Northwind Batteries Ltd", created.GetProperty("memberName").GetString());
        Assert.Equal(PomSubmissionApiFixture.CreatedAt, created.GetProperty("submittedOnUtc").GetDateTime().ToUniversalTime());
        Assert.Equal(
            created.GetProperty("createdAtUtc").GetDateTime().ToUniversalTime(),
            created.GetProperty("submittedOnUtc").GetDateTime().ToUniversalTime());
        Assert.Equal(fixture.Defaults.InitialActivity, created.GetProperty("tonnageEntries")[0].GetProperty("activity").GetString());
        Assert.True(created.GetProperty("isCurrent").GetBoolean());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("supersedesSubmissionId").ValueKind);
        Assert.Equal(JsonValueKind.Null, created.GetProperty("updatedAtUtc").ValueKind);
        Assert.False(created.TryGetProperty("schemaVersion", out _));
        Assert.False(created.TryGetProperty("version", out _));
        Assert.Equal("12.500", created.GetProperty("tonnageEntries")[0].GetProperty("tonnes").GetString());
        Assert.Equal(PomSubmissionApiFixture.CreatedAt, created.GetProperty("createdAtUtc").GetDateTime().ToUniversalTime());
        Assert.Equal(PomSubmissionApiFixture.SchemeOrgId, created.GetProperty("schemeOrgId").GetString());
        Assert.Equal(PomSubmissionApiFixture.MemberOrgId, created.GetProperty("memberOrgId").GetString());

        var stored = Assert.Single(storedDocuments);
        AssertStoredShape(stored, id!);

        var log = Assert.Single(fixture.Logger.Entries, entry => entry.Message.Contains(id!, StringComparison.Ordinal));
        Assert.Equal(LogLevel.Information, log.Level);
        Assert.Equal(7, log.EventId.Id);
        Assert.Equal(
            $"Created POM submission {id} for scheme {PomSubmissionApiFixture.SchemeOrgId} member {PomSubmissionApiFixture.MemberOrgId}",
            log.Message);

        var snapshot = stored.DeepClone().AsBsonDocument;
        snapshot["_id"] = ObjectId.Parse("000000000000000000000001");
        snapshot["tonnageEntries"][0].AsBsonDocument["id"] = ObjectId.Parse("000000000000000000000002");
        var json = snapshot.ToJson(new JsonWriterSettings
        {
            OutputMode = JsonOutputMode.CanonicalExtendedJson,
            Indent = true
        });

        await Verify(json, SnapshotSettings.Create());
    }

    [Fact]
    public async Task List_WhenApplicationHasStarted_DefinesExpectedIndexes()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var indexes = await fixture.Submissions.Indexes.List(cancellationToken).ToListAsync(cancellationToken);

        // Assert
        Assert.DoesNotContain(indexes, index => index.Contains("expireAfterSeconds"));

        var current = Index(indexes, "schemeOrgId_1_memberOrgId_1_compliancePeriodYear_1_compliancePeriodQuarter_1");
        Assert.True(current["unique"].AsBoolean);
        Assert.True(current["partialFilterExpression"]["isCurrent"].AsBoolean);

        Index(indexes, "schemeOrgId_1_compliancePeriodYear_1_compliancePeriodQuarter_1");

        var memberHistory = Index(indexes, "memberOrgId_1_compliancePeriodYear_1");
        Assert.True(memberHistory["partialFilterExpression"]["isCurrent"].AsBoolean);
        Assert.False(memberHistory.Contains("unique"));

        Index(indexes, "memberBprn_1");
        Index(indexes, "tonnageEntries.classification.chemistry_1_tonnageEntries.classification.batteryCategory_1");

        var supersedes = Index(indexes, "supersedesSubmissionId_1");
        Assert.True(supersedes["sparse"].AsBoolean);
    }

    private void AssertStoredShape(BsonDocument stored, string id)
    {
        Assert.Equal(BsonType.ObjectId, stored["_id"].BsonType);
        Assert.Equal(id, stored["_id"].AsObjectId.ToString());
        Assert.Equal(BsonType.ObjectId, stored["schemeOrgId"].BsonType);
        Assert.Equal(PomSubmissionApiFixture.SchemeOrgId, stored["schemeOrgId"].AsObjectId.ToString());
        Assert.Equal(BsonType.ObjectId, stored["memberOrgId"].BsonType);
        Assert.Equal(PomSubmissionApiFixture.MemberOrgId, stored["memberOrgId"].AsObjectId.ToString());
        Assert.Equal("Northwind Batteries Ltd", stored["memberName"].AsString);
        Assert.Equal("BPRN-EA-2026-000123", stored["memberBprn"].AsString);
        Assert.Equal("SCH-00417", stored["schemeMembershipRef"].AsString);
        Assert.Equal(2026, stored["compliancePeriodYear"].AsInt32);
        Assert.Equal("Quarter 2", stored["compliancePeriodQuarter"].AsString);
        Assert.Equal("Actual", stored["dataBasis"].AsString);
        Assert.Equal(fixture.Defaults.InitialStatus, stored["status"].AsString);
        Assert.Equal(PomSubmissionApiFixture.CreatedAt, stored["submittedOnUtc"].ToUniversalTime());
        Assert.Equal(stored["createdAtUtc"].ToUniversalTime(), stored["submittedOnUtc"].ToUniversalTime());
        Assert.True(stored["isCurrent"].AsBoolean);
        Assert.Equal(BsonNull.Value, stored["supersedesSubmissionId"]);
        Assert.Equal(BsonNull.Value, stored["updatedAtUtc"]);
        Assert.Equal(PomSubmissionApiFixture.CreatedAt, stored["createdAtUtc"].ToUniversalTime());
        Assert.Equal(PomSubmissionModel.CurrentSchemaVersion, stored["schemaVersion"].AsInt32);
        Assert.Equal(fixture.Defaults.InitialVersion, stored["version"].AsInt32);
        Assert.False(stored.Contains("declaration"));
        Assert.False(stored.Contains("_t"));

        var entry = Assert.Single(stored["tonnageEntries"].AsBsonArray).AsBsonDocument;
        Assert.True(entry.Contains("id"));
        Assert.False(entry.Contains("_id"));
        Assert.Equal(BsonType.ObjectId, entry["id"].BsonType);
        Assert.Equal(BsonType.Decimal128, entry["tonnes"].BsonType);
        Assert.Equal("12.500", entry["tonnes"].AsDecimal128.ToString());
        Assert.Equal(fixture.Defaults.InitialActivity, entry["activity"].AsString);
        Assert.Equal("Portable", entry["classification"]["batteryCategory"].AsString);
        Assert.Equal("Alkaline", entry["classification"]["chemistry"].AsString);
    }

    private static BsonDocument Index(List<BsonDocument> indexes, string name)
    {
        var index = Assert.Single(indexes, candidate => candidate["name"].AsString == name);
        return index;
    }

    private async Task AssertNothingStored(CancellationToken cancellationToken)
    {
        var count = await fixture.Submissions.CountDocumentsAsync(
            Builders<BsonDocument>.Filter.Empty,
            cancellationToken: cancellationToken);
        var created = fixture.Logger.Entries.Count(entry => entry.EventId.Id == 7);
        Assert.Equal(created, count);
    }

    private async Task<BsonDocument> FindByMember(string memberOrgId, CancellationToken cancellationToken)
    {
        var documents = await fixture.Submissions.Find(
            Builders<BsonDocument>.Filter.Eq("memberOrgId", ObjectId.Parse(memberOrgId)))
            .ToListAsync(cancellationToken);
        return Assert.Single(documents);
    }

    private static string Canonical(BsonDocument document)
    {
        var copy = document.DeepClone().AsBsonDocument;
        copy["_id"] = ObjectId.Empty;
        copy["memberOrgId"] = ObjectId.Empty;
        copy["tonnageEntries"][0].AsBsonDocument["id"] = ObjectId.Empty;
        return copy.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.CanonicalExtendedJson });
    }

    private static string OptionalBody(string memberOrgId, bool includeNulls)
    {
        var classification = includeNulls
            ? """
              "batteryCategory": "Portable",
              "chemistry": "Alkaline",
              "subCategory": null,
              "cellType": null,
              "equipmentState": null
              """
            : """
              "batteryCategory": "Portable",
              "chemistry": "Alkaline"
              """;
        return $$"""
            {
              "schemeOrgId": "674a1b2c3d4e5f6789012345",
              "memberOrgId": "{{memberOrgId}}",
              "memberName": "Northwind Batteries Ltd",
              "memberBprn": "BPRN-EA-2026-000123",
              "schemeMembershipRef": "SCH-00417",
              "compliancePeriodYear": 2026,
              "compliancePeriodQuarter": "Quarter 2",
              "dataBasis": "Actual",
              "tonnageEntries": [
                {
                  "classification": {
                    {{classification}}
                  },
                  "tonnes": "1.000"
                }
              ]
            }
            """;
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");
}
