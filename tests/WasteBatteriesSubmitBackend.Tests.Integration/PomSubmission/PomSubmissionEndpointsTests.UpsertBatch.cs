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
using System.Text.Json.Nodes;
using WasteBatteriesSubmitBackend.PomSubmission.Mapping;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.PomSubmission.Services;
using WasteBatteriesSubmitBackend.Tests.Common;
using WasteBatteriesSubmitBackend.Tests.Integration.Utils;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.Tests.Integration.PomSubmission;

public partial class PomSubmissionEndpointsTests
{
    [Fact]
    public async Task UpsertBatch_WhenCredentialsAreMissing_ReturnsUnauthorized()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var countBefore = await SubmissionCount(cancellationToken);
        using var client = fixture.CreateAnonymousClient();

        // Act
        var response = await client.PostAsync("/pom-submissions/upsert-batch", Json(BatchBody(CreateBody)), cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNothingStored(countBefore, cancellationToken);
    }

    [Fact]
    public async Task UpsertBatch_WhenTokenIsRejected_ReturnsUnauthorized()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var countBefore = await SubmissionCount(cancellationToken);
        using var client = fixture.CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-test-token");

        // Act
        var response = await client.PostAsync("/pom-submissions/upsert-batch", Json(BatchBody(CreateBody)), cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNothingStored(countBefore, cancellationToken);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"schemeOrgId":"674a1b2c3d4e5f6789012345"}""")]
    [InlineData("""[{"tonnageEntries":{"activity":"Placed"}}]""")]
    [InlineData("""[{"tonnageEntries":[{"classification":{"batteryCategory":"Portable","chemistry":"Alkaline"},"tonnes":"1.000"}]}]""")]
    public async Task UpsertBatch_WhenJsonDoesNotMatchRequestShape_ReturnsBadRequest(string body)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var countBefore = await SubmissionCount(cancellationToken);
        using var client = fixture.CreateClient();

        // Act
        var response = await client.PostAsync("/pom-submissions/upsert-batch", Json(body), cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNothingStored(countBefore, cancellationToken);
    }

    [Fact]
    public async Task UpsertBatch_WhenPropertyIsNotOnTheModel_ReturnsBadRequest()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var countBefore = await SubmissionCount(cancellationToken);
        using var client = fixture.CreateClient();
        var body = BatchBody(CreateBody.Replace(
            $"\"dataBasis\": \"{SamplePomSubmission.DataBasis}\",",
            $"\"dataBasis\": \"{SamplePomSubmission.DataBasis}\", \"regulatorNote\": \"ignore me\",",
            StringComparison.Ordinal));

        // Act
        var response = await client.PostAsync("/pom-submissions/upsert-batch", Json(body), cancellationToken);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Bad request", problem.RootElement.GetProperty("title").GetString());
        Assert.Contains("JSON", problem.RootElement.GetProperty("detail").GetString());
        Assert.False(problem.RootElement.TryGetProperty("errors", out _));
        await AssertNothingStored(countBefore, cancellationToken);
    }

    [Fact]
    public async Task UpsertBatch_WhenTheBatchIsEmpty_ReturnsValidationErrors()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var countBefore = await SubmissionCount(cancellationToken);
        using var client = fixture.CreateClient();

        // Act
        var response = await client.PostAsync("/pom-submissions/upsert-batch", Json("[]"), cancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("At least one submission is required.", problem.Errors["Submissions"]);
        await AssertNothingStored(countBefore, cancellationToken);
    }

    [Fact]
    public async Task UpsertBatch_WhenTheBodyIsNull_ReturnsBadRequest()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var countBefore = await SubmissionCount(cancellationToken);
        using var client = fixture.CreateClient();

        // Act
        var response = await client.PostAsync("/pom-submissions/upsert-batch", Json("null"), cancellationToken);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Bad request", problem.RootElement.GetProperty("title").GetString());
        Assert.Contains("Required parameter", problem.RootElement.GetProperty("detail").GetString());
        Assert.False(problem.RootElement.TryGetProperty("errors", out _));
        await AssertNothingStored(countBefore, cancellationToken);
    }

    [Fact]
    public async Task UpsertBatch_WhenASubmissionIsEmpty_ReturnsAnIndexedError()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var countBefore = await SubmissionCount(cancellationToken);
        using var client = fixture.CreateClient();
        var body = BatchBody(BodyForMember(ObjectId.GenerateNewId().ToString()), "null");

        // Act
        var response = await client.PostAsync("/pom-submissions/upsert-batch", Json(body), cancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("A submission must not be empty.", problem.Errors["[1].Submission"]);
        await AssertNothingStored(countBefore, cancellationToken);
    }

    [Fact]
    public async Task UpsertBatch_WhenSeveralItemsAreInvalid_ReturnsEveryIndexedErrorAndStoresNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var countBefore = await SubmissionCount(cancellationToken);
        using var client = fixture.CreateClient();
        var valid = BodyForMember(ObjectId.GenerateNewId().ToString());
        var missingName = BodyForMember(ObjectId.GenerateNewId().ToString()).Replace(
            $"\"memberName\": \"{SamplePomSubmission.MemberName}\",",
            "\"memberName\": \"\",",
            StringComparison.Ordinal);
        var badTonnes = BodyForMember(ObjectId.GenerateNewId().ToString()).Replace(
            $"\"tonnes\": \"{SamplePomSubmission.Tonnes}\"",
            "\"tonnes\": \"12.5\"",
            StringComparison.Ordinal);

        // Act
        var response = await client.PostAsync(
            "/pom-submissions/upsert-batch",
            Json(BatchBody(valid, missingName, badTonnes)),
            cancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("[1].MemberName", problem.Errors.Keys);
        Assert.Contains("[2].TonnageEntries[0].Tonnes", problem.Errors.Keys);
        Assert.DoesNotContain(problem.Errors.Keys, key => key.StartsWith("[0].", StringComparison.Ordinal));
        await AssertNothingStored(countBefore, cancellationToken);
    }

    [Fact]
    public async Task UpsertBatch_WhenALaterSubmissionIsInvalid_LeavesTheCurrentRowUnchanged()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var memberOrgId = ObjectId.GenerateNewId().ToString();
        var otherMemberOrgId = ObjectId.GenerateNewId().ToString();
        using var client = fixture.CreateClient();
        var created = await CreateSubmission(
            client,
            PomSubmissionApiFixture.SchemeOrgId,
            memberOrgId,
            SamplePomSubmission.CompliancePeriodYear,
            SamplePomSubmission.CompliancePeriodQuarter,
            cancellationToken);
        var invalid = BodyForMember(memberOrgId).Replace(
            $"\"memberName\": \"{SamplePomSubmission.MemberName}\",",
            "\"memberName\": \"\",",
            StringComparison.Ordinal);

        // Act
        var response = await client.PostAsync(
            "/pom-submissions/upsert-batch",
            Json(BatchBody(invalid, BodyForMember(otherMemberOrgId))),
            cancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);
        var stored = await MemberSubmissions(memberOrgId, cancellationToken);
        var other = await MemberSubmissions(otherMemberOrgId, cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("[0].MemberName", problem.Errors.Keys);
        var only = Assert.Single(stored);
        Assert.Equal(created.Id, only["_id"].AsObjectId.ToString());
        Assert.True(only["isCurrent"].AsBoolean);
        Assert.Equal(BsonNull.Value, only["updatedAtUtc"]);
        Assert.Empty(other);
    }

    [Fact]
    public async Task UpsertBatch_WhenTheSameKeyIsRepeated_ReturnsAnIndexedError()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var memberOrgId = ObjectId.GenerateNewId().ToString();
        var countBefore = await SubmissionCount(cancellationToken);
        using var client = fixture.CreateClient();
        var body = BatchBody(BodyForMember(memberOrgId), BodyForMember(memberOrgId.ToUpperInvariant(), "3.000"));

        // Act
        var response = await client.PostAsync("/pom-submissions/upsert-batch", Json(body), cancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(
            "This submission repeats [0] for the same scheme, member, year and quarter.",
            problem.Errors["[1].Submission"]);
        await AssertNothingStored(countBefore, cancellationToken);
    }

    [Fact]
    public async Task UpsertBatch_WhenNoCurrentRowsExist_CreatesEachInRequestOrder()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstMemberOrgId = ObjectId.GenerateNewId().ToString();
        var secondMemberOrgId = ObjectId.GenerateNewId().ToString();
        using var client = fixture.CreateClient();
        // Surrounding spaces are trimmed before save. The stored snapshot shows the trimmed fields.
        var body = BatchBody(
            Padded(BodyForMember(firstMemberOrgId)),
            BodyForMember(secondMemberOrgId, "3.000"));
        const string batchLog = "Upserted batch of 2 POM submissions (2 created, 0 amended)";
        var batchLogsBefore = BatchLogCount(batchLog);

        // Act
        var response = await client.PostAsync("/pom-submissions/upsert-batch", Json(body), cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var stored = await MembersInVersionOrder(cancellationToken, firstMemberOrgId, secondMemberOrgId);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertBatchLogged(batchLog, batchLogsBefore);
        var pins = PinsFor(stored);
        Snapshot.Match(PinResponse(responseJson, pins), "Response");
        Snapshot.Match(PinStored(stored, pins), "Stored");
    }

    [Fact]
    public async Task UpsertBatch_WhenTheBatchCreatesAndAmends_LeavesOtherHistory()
    {
        // Arrange
        // Amended Member already has a superseded row and a current row, and this batch amends it again.
        // Untouched History has the same kind of history and is not posted, so both of its rows stay as they were.
        var cancellationToken = TestContext.Current.CancellationToken;
        var createdMemberOrgId = ObjectId.GenerateNewId().ToString();
        var amendedMemberOrgId = ObjectId.GenerateNewId().ToString();
        var untouchedMemberOrgId = ObjectId.GenerateNewId().ToString();
        using var client = fixture.CreateClient();
        await CreateNamed(client, amendedMemberOrgId, "Amended Member Ltd", cancellationToken);
        using (var amendedEarlier = await client.PatchAsync(
            "/pom-submissions",
            Json(NamedBody(amendedMemberOrgId, "Amended Member Ltd", "2.000")),
            cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, amendedEarlier.StatusCode);
        }

        await CreateNamed(client, untouchedMemberOrgId, "Untouched History Ltd", cancellationToken);
        using (var untouchedEarlier = await client.PatchAsync(
            "/pom-submissions",
            Json(NamedBody(untouchedMemberOrgId, "Untouched History Ltd", "5.000")),
            cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, untouchedEarlier.StatusCode);
        }

        var body = BatchBody(
            NamedBody(createdMemberOrgId, "Created Member Ltd", "4.000"),
            NamedBody(amendedMemberOrgId, "Amended Member Ltd", "3.000"));
        const string batchLog = "Upserted batch of 2 POM submissions (1 created, 1 amended)";
        var batchLogsBefore = BatchLogCount(batchLog);

        // Act
        var response = await client.PostAsync("/pom-submissions/upsert-batch", Json(body), cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        // Created row, then the amended chain oldest first, then the member this batch did not post.
        var stored = await MembersInVersionOrder(
            cancellationToken,
            createdMemberOrgId,
            amendedMemberOrgId,
            untouchedMemberOrgId);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertBatchLogged(batchLog, batchLogsBefore);
        var pins = PinsFor(stored);
        Snapshot.Match(PinResponse(responseJson, pins), "Response");
        Snapshot.Match(PinStored(stored, pins), "Stored");
    }

    [Fact]
    public async Task UpsertBatch_WhenThePostedIdUsesAnotherHexCase_AmendsTheCurrentRow()
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

        // Act
        var response = await client.PostAsync(
            "/pom-submissions/upsert-batch",
            Json(BatchBody(BodyForMember(memberOrgId.ToUpperInvariant(), "3.000"))),
            cancellationToken);
        var results = ReadResults(await response.Content.ReadAsStringAsync(cancellationToken));
        var ids = ReadIds(results);
        var stored = await MemberSubmissions(memberOrgId, cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var amendedResult = Assert.Single(results);
        var amendedId = Assert.Single(ids);
        AssertCoreResult(amendedResult, requestIndex: 0, amendedId, memberOrgId, "Amended");
        Assert.NotEqual(created.Id, amendedId);
        Assert.Equal(2, stored.Count);
        Assert.Single(stored, document => document["isCurrent"].AsBoolean && document["_id"].AsObjectId.ToString() == amendedId);
        Assert.Equal(memberOrgId, stored[0]["memberOrgId"].AsObjectId.ToString());
    }

    [Fact]
    public async Task FindCurrent_WhenKeysMatch_ReturnsOnlyTheCurrentRows()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var memberOrgId = ObjectId.GenerateNewId().ToString();
        var otherMemberOrgId = ObjectId.GenerateNewId().ToString();
        var missingMemberOrgId = ObjectId.GenerateNewId().ToString();
        using var client = fixture.CreateClient();
        var created = await CreateSubmission(
            client,
            PomSubmissionApiFixture.SchemeOrgId,
            memberOrgId,
            SamplePomSubmission.CompliancePeriodYear,
            SamplePomSubmission.CompliancePeriodQuarter,
            cancellationToken);
        var previous = await CreateSubmission(
            client,
            PomSubmissionApiFixture.SchemeOrgId,
            otherMemberOrgId,
            SamplePomSubmission.CompliancePeriodYear,
            SamplePomSubmission.CompliancePeriodQuarter,
            cancellationToken);
        var amended = await client.PatchAsync(
            "/pom-submissions",
            Json(BodyForMember(otherMemberOrgId, "3.000")),
            cancellationToken);
        var amendedId = ReadId(await amended.Content.ReadAsStringAsync(cancellationToken));
        var repository = fixture.Services.GetRequiredService<IPomSubmissionRepository>();

        // Act
        var found = await repository.FindCurrentAsync(
            [
                KeyFor(memberOrgId),
                KeyFor(otherMemberOrgId),
                KeyFor(missingMemberOrgId)
            ],
            cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, amended.StatusCode);
        Assert.Equal(2, found.Count);
        Assert.Contains(found, submission => submission.Id == created.Id);
        Assert.Contains(found, submission => submission.Id == amendedId);
        Assert.DoesNotContain(found, submission => submission.Id == previous.Id);
    }

    [Fact]
    public async Task FindCurrent_WhenNoKeyIsSupplied_ReturnsNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = fixture.Services.GetRequiredService<IPomSubmissionRepository>();

        // Act
        var found = await repository.FindCurrentAsync([], cancellationToken);

        // Assert
        Assert.Empty(found);
    }

    [Fact]
    public async Task SupersedeCurrents_WhenARowIsNoLongerCurrent_LeavesThatRowUnchanged()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var memberOrgId = ObjectId.GenerateNewId().ToString();
        var otherMemberOrgId = ObjectId.GenerateNewId().ToString();
        var repository = fixture.Services.GetRequiredService<IPomSubmissionRepository>();
        var unitOfWork = fixture.Services.GetRequiredService<IUnitOfWork>();
        var mapper = fixture.Services.GetRequiredService<IPomSubmissionMapper>();
        var current = mapper.ToNewSubmission(
            SamplePomSubmission.Request() with { MemberOrgId = memberOrgId },
            PomSubmissionApiFixture.CreatedAt);
        var already = mapper.ToNewSubmission(
            SamplePomSubmission.Request() with { MemberOrgId = otherMemberOrgId },
            PomSubmissionApiFixture.CreatedAt);
        var updatedAt = PomSubmissionApiFixture.CreatedAt.AddMinutes(5);

        try
        {
            await repository.CreateAsync(current, cancellationToken);
            await repository.CreateAsync(already, cancellationToken);
            await SetCurrent(already.Id, isCurrent: false, cancellationToken);

            // Act
            var matched = await unitOfWork.ExecuteAsync(
                (transaction, transactionCancellationToken) =>
                    repository.SupersedeCurrentsAsync(
                        transaction.Session,
                        [current.Id, already.Id],
                        updatedAt,
                        transactionCancellationToken),
                cancellationToken);
            var currentStored = await MemberSubmissions(memberOrgId, cancellationToken);
            var alreadyStored = await MemberSubmissions(otherMemberOrgId, cancellationToken);

            // Assert
            Assert.Equal(1, matched);
            var superseded = Assert.Single(currentStored);
            Assert.False(superseded["isCurrent"].AsBoolean);
            Assert.Equal(updatedAt, superseded["updatedAtUtc"].ToUniversalTime());
            var untouched = Assert.Single(alreadyStored);
            Assert.False(untouched["isCurrent"].AsBoolean);
            Assert.Equal(BsonNull.Value, untouched["updatedAtUtc"]);
        }
        finally
        {
            await fixture.Submissions.DeleteManyAsync(MemberFilter(memberOrgId), cancellationToken);
            await fixture.Submissions.DeleteManyAsync(MemberFilter(otherMemberOrgId), cancellationToken);
        }
    }

    [Fact]
    public async Task CreateMany_WhenOneInsertConflicts_RollsBackTheBatch()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var memberOrgId = ObjectId.GenerateNewId().ToString();
        var otherMemberOrgId = ObjectId.GenerateNewId().ToString();
        var repository = fixture.Services.GetRequiredService<IPomSubmissionRepository>();
        var unitOfWork = fixture.Services.GetRequiredService<IUnitOfWork>();
        var mapper = fixture.Services.GetRequiredService<IPomSubmissionMapper>();
        var request = SamplePomSubmission.Request() with { MemberOrgId = memberOrgId };
        var current = mapper.ToNewSubmission(request, PomSubmissionApiFixture.CreatedAt);
        var updatedAt = PomSubmissionApiFixture.CreatedAt.AddMinutes(5);
        var amendment = mapper.ToNewSubmission(request, updatedAt, supersedesSubmissionId: current.Id);
        var conflicting = mapper.ToNewSubmission(request, updatedAt, supersedesSubmissionId: current.Id);
        var other = mapper.ToNewSubmission(
            SamplePomSubmission.Request() with { MemberOrgId = otherMemberOrgId },
            updatedAt);

        try
        {
            await repository.CreateAsync(current, cancellationToken);

            // Act
            Task Act() => unitOfWork.ExecuteAsync(
                async (transaction, transactionCancellationToken) =>
                {
                    var matched = await repository.SupersedeCurrentsAsync(
                        transaction.Session,
                        [current.Id],
                        updatedAt,
                        transactionCancellationToken);
                    Assert.Equal(1, matched);
                    await repository.CreateManyAsync(
                        transaction.Session,
                        [amendment, conflicting, other],
                        transactionCancellationToken);
                    return 0;
                },
                cancellationToken);

            // Assert
            await Assert.ThrowsAsync<DuplicateKeyOnSaveException>(Act);
            var stored = await MemberSubmissions(memberOrgId, cancellationToken);
            var only = Assert.Single(stored);
            Assert.Equal(current.Id, only["_id"].AsObjectId.ToString());
            Assert.True(only["isCurrent"].AsBoolean);
            Assert.Equal(BsonNull.Value, only["updatedAtUtc"]);
            Assert.Empty(await MemberSubmissions(otherMemberOrgId, cancellationToken));
        }
        finally
        {
            await fixture.Submissions.DeleteManyAsync(MemberFilter(memberOrgId), cancellationToken);
            await fixture.Submissions.DeleteManyAsync(MemberFilter(otherMemberOrgId), cancellationToken);
        }
    }

    // The fixture logger is shared by every test in this class. Another test can already
    // have written the same batch summary, so count matches from this call only.
    private int BatchLogCount(string message) =>
        fixture.Logger.Entries.Count(entry => entry.Message == message);

    private CapturingLogger<PomSubmissionService>.LogEntry AssertOneNewBatchLog(string message, int countBefore)
    {
        var matches = fixture.Logger.Entries.Where(entry => entry.Message == message).ToList();
        Assert.Equal(countBefore + 1, matches.Count);
        return matches[^1];
    }

    private void AssertBatchLogged(string message, int countBefore)
    {
        var log = AssertOneNewBatchLog(message, countBefore);
        Assert.Equal(LogLevel.Information, log.Level);
        Assert.Equal(6, log.EventId.Id);
    }

    private static string BatchBody(params string[] submissions) =>
        "[" + string.Join(",", submissions) + "]";

    private static string Padded(string body) =>
        body
            .Replace(
                $"\"memberName\": \"{SamplePomSubmission.MemberName}\"",
                $"\"memberName\": \"  {SamplePomSubmission.MemberName}  \"",
                StringComparison.Ordinal)
            .Replace(
                $"\"compliancePeriodQuarter\": \"{SamplePomSubmission.CompliancePeriodQuarter}\"",
                $"\"compliancePeriodQuarter\": \"  {SamplePomSubmission.CompliancePeriodQuarter}  \"",
                StringComparison.Ordinal)
            .Replace(
                $"\"tonnes\": \"{SamplePomSubmission.Tonnes}\"",
                $"\"tonnes\": \"  {SamplePomSubmission.Tonnes}  \"",
                StringComparison.Ordinal);

    private static JsonElement[] ReadResults(string json)
    {
        using var document = JsonDocument.Parse(json);
        return [.. document.RootElement.EnumerateArray().Select(item => item.Clone())];
    }

    private static string[] ReadIds(JsonElement[] results) =>
        [.. results.Select(item => item.GetProperty("id").GetString()!)];

    private static void AssertCoreResult(
        JsonElement item,
        int requestIndex,
        string id,
        string memberOrgId,
        string action)
    {
        Assert.Equal(requestIndex, item.GetProperty("requestIndex").GetInt32());
        Assert.Equal(id, item.GetProperty("id").GetString());
        Assert.Equal(PomSubmissionApiFixture.SchemeOrgId, item.GetProperty("schemeOrgId").GetString());
        Assert.Equal(memberOrgId, item.GetProperty("memberOrgId").GetString());
        Assert.Equal(SamplePomSubmission.CompliancePeriodYear, item.GetProperty("compliancePeriodYear").GetInt32());
        Assert.Equal(SamplePomSubmission.CompliancePeriodQuarter, item.GetProperty("compliancePeriodQuarter").GetString());
        Assert.Equal(action, item.GetProperty("action").GetString());
        Assert.False(item.TryGetProperty("tonnageEntries", out _));
        Assert.Equal(7, item.EnumerateObject().Count());
    }

    private static string ReadId(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("id").GetString()!;
    }

    private static PomSubmissionKey KeyFor(string memberOrgId) =>
        new(
            PomSubmissionApiFixture.SchemeOrgId,
            memberOrgId,
            SamplePomSubmission.CompliancePeriodYear,
            SamplePomSubmission.CompliancePeriodQuarter);

    private static async Task CreateNamed(
        HttpClient client,
        string memberOrgId,
        string memberName,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync(
            "/pom-submissions",
            Json(NamedBody(memberOrgId, memberName)),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static string NamedBody(string memberOrgId, string memberName, string? tonnes = null) =>
        BodyForMember(memberOrgId, tonnes).Replace(
            $"\"memberName\": \"{SamplePomSubmission.MemberName}\"",
            $"\"memberName\": \"{memberName}\"",
            StringComparison.Ordinal);

    private async Task<List<BsonDocument>> MembersInVersionOrder(
        CancellationToken cancellationToken,
        params string[] memberOrgIds)
    {
        var documents = new List<BsonDocument>();
        foreach (var memberOrgId in memberOrgIds)
        {
            var rows = await MemberSubmissions(memberOrgId, cancellationToken);
            documents.AddRange(rows.OrderBy(document => document["version"].AsInt32));
        }

        return documents;
    }

    // Generated ids change every run. The same pin is used in the response and the stored
    // document, and supersedesSubmissionId is rewritten to the pinned id of the row it replaced.
    private static BatchPins PinsFor(IReadOnlyList<BsonDocument> stored)
    {
        var submissionIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var memberIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var nextSubmission = 1;
        var nextMember = 0x101;
        foreach (var document in stored)
        {
            var id = document["_id"].AsObjectId.ToString();
            if (!submissionIds.ContainsKey(id))
            {
                submissionIds[id] = PinHex(nextSubmission++);
            }

            var memberOrgId = document["memberOrgId"].AsObjectId.ToString();
            if (!memberIds.ContainsKey(memberOrgId))
            {
                memberIds[memberOrgId] = PinHex(nextMember++);
            }
        }

        return new BatchPins(submissionIds, memberIds);
    }

    private static string PinResponse(string json, BatchPins pins)
    {
        var results = JsonNode.Parse(json)!.AsArray();
        foreach (var result in results)
        {
            var item = result!.AsObject();
            item["id"] = pins.SubmissionIds[item["id"]!.GetValue<string>()];
            item["memberOrgId"] = pins.MemberIds[item["memberOrgId"]!.GetValue<string>()];
        }

        return results.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string PinStored(IReadOnlyList<BsonDocument> stored, BatchPins pins)
    {
        var pinned = new BsonArray();
        var tonnageId = 0x201;
        foreach (var document in stored)
        {
            var copy = document.DeepClone().AsBsonDocument;
            copy["_id"] = ObjectId.Parse(pins.SubmissionIds[copy["_id"].AsObjectId.ToString()]);
            copy["memberOrgId"] = ObjectId.Parse(pins.MemberIds[copy["memberOrgId"].AsObjectId.ToString()]);
            if (copy["supersedesSubmissionId"].BsonType == BsonType.ObjectId)
            {
                copy["supersedesSubmissionId"] = ObjectId.Parse(
                    pins.SubmissionIds[copy["supersedesSubmissionId"].AsObjectId.ToString()]);
            }

            foreach (var entry in copy["tonnageEntries"].AsBsonArray)
            {
                entry.AsBsonDocument["id"] = ObjectId.Parse(PinHex(tonnageId++));
            }

            pinned.Add(copy);
        }

        return pinned.ToJson(new JsonWriterSettings
        {
            OutputMode = JsonOutputMode.CanonicalExtendedJson,
            Indent = true,
            NewLineChars = "\n"
        });
    }

    private static string PinHex(int value) => value.ToString("x24");

    private sealed record BatchPins(
        Dictionary<string, string> SubmissionIds,
        Dictionary<string, string> MemberIds);
}
