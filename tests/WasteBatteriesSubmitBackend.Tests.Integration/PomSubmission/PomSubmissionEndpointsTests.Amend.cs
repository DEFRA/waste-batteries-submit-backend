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
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.Tests.Common;
using WasteBatteriesSubmitBackend.Tests.Integration.Utils;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.Tests.Integration.PomSubmission;

public partial class PomSubmissionEndpointsTests
{
    [Fact]
    public async Task Amend_WhenCredentialsAreMissing_ReturnsUnauthorized()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var countBefore = await SubmissionCount(cancellationToken);
        using var client = fixture.CreateAnonymousClient();

        // Act
        var response = await client.PatchAsync("/pom-submissions", Json(CreateBody), cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNothingStored(countBefore, cancellationToken);
    }

    [Fact]
    public async Task Amend_WhenTokenIsRejected_ReturnsUnauthorized()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var countBefore = await SubmissionCount(cancellationToken);
        using var client = fixture.CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-test-token");

        // Act
        var response = await client.PatchAsync("/pom-submissions", Json(CreateBody), cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNothingStored(countBefore, cancellationToken);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"tonnageEntries":{"activity":"Placed"}}""")]
    [InlineData("""{"tonnageEntries":[{"classification":{"batteryCategory":"Portable","chemistry":"Alkaline"},"tonnes":"1.000"}]}""")]
    public async Task Amend_WhenJsonDoesNotMatchRequestShape_ReturnsBadRequest(string body)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var countBefore = await SubmissionCount(cancellationToken);
        using var client = fixture.CreateClient();

        // Act
        var response = await client.PatchAsync("/pom-submissions", Json(body), cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNothingStored(countBefore, cancellationToken);
    }

    [Fact]
    public async Task Amend_WhenPropertyIsNotOnTheModel_ReturnsBadRequest()
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
        var response = await client.PatchAsync("/pom-submissions", Json(body), cancellationToken);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Bad request", problem.RootElement.GetProperty("title").GetString());
        Assert.Contains("JSON", problem.RootElement.GetProperty("detail").GetString());
        Assert.False(problem.RootElement.TryGetProperty("errors", out _));
        await AssertNothingStored(countBefore, cancellationToken);
    }

    [Fact]
    public async Task Amend_WhenRequestHasManyFailures_ReturnsEveryValidationError()
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
        var response = await client.PatchAsync("/pom-submissions", Json(body), cancellationToken);
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
        Assert.DoesNotContain("Submission", problem.Errors.Keys);
        await AssertNothingStored(countBefore, cancellationToken);
    }

    [Fact]
    public async Task Amend_WhenNoCurrentSubmissionExists_ReturnsBadRequest()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var memberOrgId = ObjectId.GenerateNewId().ToString();
        var countBefore = await SubmissionCount(cancellationToken);
        using var client = fixture.CreateClient();

        // Act
        var response = await client.PatchAsync("/pom-submissions", Json(BodyForMember(memberOrgId)), cancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(
            "A current submission does not exist for this scheme, member, year and quarter.",
            problem.Errors["Submission"]);
        await AssertNothingStored(countBefore, cancellationToken);
    }

    [Fact]
    public async Task Amend_WhenModelIsInvalidAndNoCurrentSubmissionExists_ReturnsBothErrors()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var memberOrgId = ObjectId.GenerateNewId().ToString();
        var countBefore = await SubmissionCount(cancellationToken);
        using var client = fixture.CreateClient();
        var body = BodyForMember(memberOrgId).Replace(
            $"\"memberName\": \"{SamplePomSubmission.MemberName}\",",
            "\"memberName\": \"\",",
            StringComparison.Ordinal);

        // Act
        var response = await client.PatchAsync("/pom-submissions", Json(body), cancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("MemberName", problem.Errors.Keys);
        Assert.Contains(
            "A current submission does not exist for this scheme, member, year and quarter.",
            problem.Errors["Submission"]);
        await AssertNothingStored(countBefore, cancellationToken);
    }

    [Fact]
    public async Task Amend_WhenModelIsInvalidAndCurrentSubmissionExists_LeavesThatSubmissionCurrent()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var memberOrgId = ObjectId.GenerateNewId().ToString();
        using var client = fixture.CreateClient();
        var created = await CreateSubmission(
            client,
            PomSubmissionApiFixture.SchemeOrgId,
            memberOrgId,
            SamplePomSubmission.CompliancePeriodYear,
            SamplePomSubmission.CompliancePeriodQuarter,
            cancellationToken);
        var body = BodyForMember(memberOrgId).Replace(
            $"\"memberName\": \"{SamplePomSubmission.MemberName}\",",
            "\"memberName\": \"\",",
            StringComparison.Ordinal);

        // Act
        var response = await client.PatchAsync("/pom-submissions", Json(body), cancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);
        var stored = await MemberSubmissions(memberOrgId, cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("MemberName", problem.Errors.Keys);
        Assert.DoesNotContain("Submission", problem.Errors.Keys);
        var only = Assert.Single(stored);
        Assert.Equal(created.Id, only["_id"].AsObjectId.ToString());
        Assert.True(only["isCurrent"].AsBoolean);
        Assert.Equal(BsonNull.Value, only["updatedAtUtc"]);
    }

    [Fact]
    public async Task Amend_WhenCurrentSubmissionExists_ReplacesItAndKeepsThePreviousRow()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var memberOrgId = ObjectId.GenerateNewId().ToString();
        using var client = fixture.CreateClient();
        var body = BodyForMember(memberOrgId);
        var created = await CreateSubmission(
            client,
            PomSubmissionApiFixture.SchemeOrgId,
            memberOrgId,
            SamplePomSubmission.CompliancePeriodYear,
            SamplePomSubmission.CompliancePeriodQuarter,
            cancellationToken);
        var amendedBody = body.Replace(
            $"\"tonnes\": \"{SamplePomSubmission.Tonnes}\"",
            "\"tonnes\": \"3.000\"",
            StringComparison.Ordinal);

        // Act
        var response = await client.PatchAsync("/pom-submissions", Json(amendedBody), cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var responseDocument = JsonDocument.Parse(responseJson);
        var amended = responseDocument.RootElement;
        var amendedId = amended.GetProperty("id").GetString();
        var stored = await MemberSubmissions(memberOrgId, cancellationToken);
        var previousResponse = await client.GetAsync($"/pom-submissions/{created.Id}", cancellationToken);
        using var currentPage = await ReadPage(client, cancellationToken, ("memberOrgId", memberOrgId));
        using var previousPage = await ReadPage(
            client,
            cancellationToken,
            ("memberOrgId", memberOrgId),
            ("isCurrent", "false"));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(created.Id, amendedId);
        Assert.Equal(created.Id, amended.GetProperty("supersedesSubmissionId").GetString());
        Assert.True(amended.GetProperty("isCurrent").GetBoolean());
        Assert.Equal(JsonValueKind.Null, amended.GetProperty("updatedAtUtc").ValueKind);
        Assert.Equal(fixture.Defaults.InitialVersion + 1, amended.GetProperty("version").GetInt32());
        Assert.Equal("3.000", amended.GetProperty("tonnageEntries")[0].GetProperty("tonnes").GetString());
        Assert.Equal(PomSubmissionApiFixture.CreatedAt, amended.GetProperty("createdAtUtc").GetDateTime().ToUniversalTime());

        Assert.Equal(2, stored.Count);
        var previous = Assert.Single(stored, document => document["_id"].AsObjectId.ToString() == created.Id);
        var replacement = Assert.Single(stored, document => document["_id"].AsObjectId.ToString() == amendedId);
        Assert.False(previous["isCurrent"].AsBoolean);
        Assert.Equal(PomSubmissionApiFixture.CreatedAt, previous["updatedAtUtc"].ToUniversalTime());
        Assert.Equal(BsonNull.Value, previous["supersedesSubmissionId"]);
        Assert.Equal(fixture.Defaults.InitialVersion, previous["version"].AsInt32);
        Assert.Equal(SamplePomSubmission.Tonnes, previous["tonnageEntries"][0]["tonnes"].AsDecimal128.ToString());
        Assert.True(replacement["isCurrent"].AsBoolean);
        Assert.Equal(BsonNull.Value, replacement["updatedAtUtc"]);
        Assert.Equal(created.Id, replacement["supersedesSubmissionId"].AsObjectId.ToString());
        Assert.Equal(fixture.Defaults.InitialVersion + 1, replacement["version"].AsInt32);
        Assert.Equal("3.000", replacement["tonnageEntries"][0]["tonnes"].AsDecimal128.ToString());

        Assert.Equal(HttpStatusCode.OK, previousResponse.StatusCode);
        using var previousDocument = JsonDocument.Parse(await previousResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.False(previousDocument.RootElement.GetProperty("isCurrent").GetBoolean());

        AssertPage(
            currentPage.RootElement,
            1,
            fixture.Defaults.MaxPageSize,
            1,
            1,
            false,
            false,
            amendedId!);
        AssertPage(
            previousPage.RootElement,
            1,
            fixture.Defaults.MaxPageSize,
            1,
            1,
            false,
            false,
            created.Id);

        var log = Assert.Single(fixture.Logger.Entries, entry => entry.Message.Contains(amendedId!, StringComparison.Ordinal));
        Assert.Equal(LogLevel.Information, log.Level);
        Assert.Equal(5, log.EventId.Id);
        Assert.Equal(
            $"Amended POM submission {amendedId} superseding {created.Id} for scheme {PomSubmissionApiFixture.SchemeOrgId} member {memberOrgId}",
            log.Message);

        Snapshot.Match(StoredPair(previous, replacement));
    }

    [Fact]
    public async Task Amend_WhenAmendedAgain_SupersedesTheCurrentReplacement()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var memberOrgId = ObjectId.GenerateNewId().ToString();
        using var client = fixture.CreateClient();
        var created = await CreateSubmission(
            client,
            PomSubmissionApiFixture.SchemeOrgId,
            memberOrgId,
            SamplePomSubmission.CompliancePeriodYear,
            SamplePomSubmission.CompliancePeriodQuarter,
            cancellationToken);
        var firstBody = BodyForMember(memberOrgId, "2.000");
        var secondBody = BodyForMember(memberOrgId, "3.000");

        // Act
        var firstResponse = await client.PatchAsync("/pom-submissions", Json(firstBody), cancellationToken);
        var firstJson = await firstResponse.Content.ReadAsStringAsync(cancellationToken);
        using var firstDocument = JsonDocument.Parse(firstJson);
        var firstId = firstDocument.RootElement.GetProperty("id").GetString();
        var secondResponse = await client.PatchAsync("/pom-submissions", Json(secondBody), cancellationToken);
        using var secondDocument = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync(cancellationToken));
        var secondId = secondDocument.RootElement.GetProperty("id").GetString();
        var stored = await MemberSubmissions(memberOrgId, cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(3, stored.Count);
        var original = Assert.Single(stored, document => document["_id"].AsObjectId.ToString() == created.Id);
        var first = Assert.Single(stored, document => document["_id"].AsObjectId.ToString() == firstId);
        var second = Assert.Single(stored, document => document["_id"].AsObjectId.ToString() == secondId);
        Assert.False(original["isCurrent"].AsBoolean);
        Assert.Equal(BsonNull.Value, original["supersedesSubmissionId"]);
        Assert.Equal(fixture.Defaults.InitialVersion, original["version"].AsInt32);
        Assert.False(first["isCurrent"].AsBoolean);
        Assert.Equal(created.Id, first["supersedesSubmissionId"].AsObjectId.ToString());
        Assert.Equal(fixture.Defaults.InitialVersion + 1, first["version"].AsInt32);
        Assert.Equal(PomSubmissionApiFixture.CreatedAt, first["updatedAtUtc"].ToUniversalTime());
        Assert.True(second["isCurrent"].AsBoolean);
        Assert.Equal(firstId, second["supersedesSubmissionId"].AsObjectId.ToString());
        Assert.Equal(fixture.Defaults.InitialVersion + 2, second["version"].AsInt32);
        Assert.Equal(BsonNull.Value, second["updatedAtUtc"]);
        Assert.Equal("3.000", second["tonnageEntries"][0]["tonnes"].AsDecimal128.ToString());
        Assert.Equal(fixture.Defaults.InitialVersion + 1, firstDocument.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(fixture.Defaults.InitialVersion + 2, secondDocument.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Amend_WhenTheReplacementInsertConflicts_RollsBackTheCurrentRow()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var memberOrgId = ObjectId.GenerateNewId().ToString();
        var repository = fixture.Services.GetRequiredService<IPomSubmissionRepository>();
        var unitOfWork = fixture.Services.GetRequiredService<IUnitOfWork>();
        var mapper = fixture.Services.GetRequiredService<IPomSubmissionMapper>();
        var request = SamplePomSubmission.Request() with { MemberOrgId = memberOrgId };
        var current = mapper.ToNewSubmission(request, PomSubmissionApiFixture.CreatedAt);
        var updatedAt = PomSubmissionApiFixture.CreatedAt.AddMinutes(5);
        var amendment = mapper.ToNewSubmission(request, updatedAt, supersedesSubmissionId: current.Id);
        var conflicting = mapper.ToNewSubmission(request, updatedAt, supersedesSubmissionId: current.Id);

        try
        {
            await repository.CreateAsync(current, cancellationToken);

            // Act
            Task Act() => unitOfWork.ExecuteAsync(
                async (transaction, transactionCancellationToken) =>
                {
                    var superseded = await repository.SupersedeCurrentAsync(
                        transaction.Session,
                        current.Id,
                        updatedAt,
                        transactionCancellationToken);
                    Assert.True(superseded);
                    await repository.CreateAsync(transaction.Session, amendment, transactionCancellationToken);
                    await repository.CreateAsync(transaction.Session, conflicting, transactionCancellationToken);
                    return amendment;
                },
                cancellationToken);

            // Assert
            await Assert.ThrowsAsync<DuplicateKeyOnSaveException>(Act);
            var stored = await MemberSubmissions(memberOrgId, cancellationToken);
            var only = Assert.Single(stored);
            Assert.Equal(current.Id, only["_id"].AsObjectId.ToString());
            Assert.True(only["isCurrent"].AsBoolean);
            Assert.Equal(BsonNull.Value, only["updatedAtUtc"]);
            Assert.Equal(BsonNull.Value, only["supersedesSubmissionId"]);
        }
        finally
        {
            await fixture.Submissions.DeleteManyAsync(MemberFilter(memberOrgId), cancellationToken);
        }
    }

    [Fact]
    public async Task SupersedeCurrent_WhenTheRowIsNoLongerCurrent_DoesNotWriteAgain()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var memberOrgId = ObjectId.GenerateNewId().ToString();
        var repository = fixture.Services.GetRequiredService<IPomSubmissionRepository>();
        var unitOfWork = fixture.Services.GetRequiredService<IUnitOfWork>();
        var mapper = fixture.Services.GetRequiredService<IPomSubmissionMapper>();
        var request = SamplePomSubmission.Request() with { MemberOrgId = memberOrgId };
        var current = mapper.ToNewSubmission(request, PomSubmissionApiFixture.CreatedAt);
        var firstUpdatedAt = PomSubmissionApiFixture.CreatedAt.AddMinutes(5);
        var secondUpdatedAt = PomSubmissionApiFixture.CreatedAt.AddMinutes(10);

        try
        {
            await repository.CreateAsync(current, cancellationToken);

            // Act
            var first = await unitOfWork.ExecuteAsync(
                (transaction, transactionCancellationToken) =>
                    repository.SupersedeCurrentAsync(
                        transaction.Session,
                        current.Id,
                        firstUpdatedAt,
                        transactionCancellationToken),
                cancellationToken);
            var second = await unitOfWork.ExecuteAsync(
                (transaction, transactionCancellationToken) =>
                    repository.SupersedeCurrentAsync(
                        transaction.Session,
                        current.Id,
                        secondUpdatedAt,
                        transactionCancellationToken),
                cancellationToken);
            var stored = await MemberSubmissions(memberOrgId, cancellationToken);

            // Assert
            Assert.True(first);
            Assert.False(second);
            var only = Assert.Single(stored);
            Assert.False(only["isCurrent"].AsBoolean);
            Assert.Equal(firstUpdatedAt, only["updatedAtUtc"].ToUniversalTime());
        }
        finally
        {
            await fixture.Submissions.DeleteManyAsync(MemberFilter(memberOrgId), cancellationToken);
        }
    }

    // The previous row is first, then the replacement. Generated ids are pinned so the file stays stable,
    // and the replacement still points at the previous row.
    private static string StoredPair(BsonDocument previous, BsonDocument replacement)
    {
        var previousId = ObjectId.Parse("000000000000000000000001");
        var replacementId = ObjectId.Parse("000000000000000000000002");
        var memberOrgId = ObjectId.Parse("000000000000000000000003");
        var pinnedPrevious = Pin(previous, previousId, memberOrgId, "000000000000000000000004");
        var pinnedReplacement = Pin(replacement, replacementId, memberOrgId, "000000000000000000000005");
        pinnedReplacement["supersedesSubmissionId"] = previousId;

        return new BsonArray { pinnedPrevious, pinnedReplacement }.ToJson(new JsonWriterSettings
        {
            OutputMode = JsonOutputMode.CanonicalExtendedJson,
            Indent = true,
            NewLineChars = "\n"
        });
    }

    private static BsonDocument Pin(BsonDocument document, ObjectId id, ObjectId memberOrgId, string tonnageEntryId)
    {
        var pinned = document.DeepClone().AsBsonDocument;
        pinned["_id"] = id;
        pinned["memberOrgId"] = memberOrgId;
        pinned["tonnageEntries"][0].AsBsonDocument["id"] = ObjectId.Parse(tonnageEntryId);
        return pinned;
    }

    private static string BodyForMember(string memberOrgId, string? tonnes = null)
    {
        var body = CreateBody.Replace(PomSubmissionApiFixture.MemberOrgId, memberOrgId, StringComparison.Ordinal);
        if (tonnes is null)
        {
            return body;
        }

        return body.Replace(
            $"\"tonnes\": \"{SamplePomSubmission.Tonnes}\"",
            $"\"tonnes\": \"{tonnes}\"",
            StringComparison.Ordinal);
    }

    private static FilterDefinition<BsonDocument> MemberFilter(string memberOrgId) =>
        Builders<BsonDocument>.Filter.Eq("memberOrgId", ObjectId.Parse(memberOrgId));

    private Task<List<BsonDocument>> MemberSubmissions(string memberOrgId, CancellationToken cancellationToken) =>
        fixture.Submissions.Find(MemberFilter(memberOrgId)).ToListAsync(cancellationToken);
}
