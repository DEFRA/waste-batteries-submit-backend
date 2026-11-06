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
    public async Task UpsertBatch_WhenSubmissionIsValid_SavesItAndLogsIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var memberOrgId = ObjectId.GenerateNewId().ToString();
        using var client = fixture.CreateClient();
        var body = BatchBody(Padded(BodyForMember(memberOrgId)));
        const string batchLog = "Upserted batch of 1 POM submissions (1 created, 0 amended)";
        var batchLogsBefore = BatchLogCount(batchLog);

        // Act
        var response = await client.PostAsync("/pom-submissions/upsert-batch", Json(body), cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var results = ReadResults(responseJson);
        var ids = ReadIds(results);
        var storedDocuments = await MemberSubmissions(memberOrgId, cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = Assert.Single(results);
        var id = Assert.Single(ids);
        AssertCoreResult(saved, requestIndex: 0, id, memberOrgId, "Created");
        var stored = Assert.Single(storedDocuments);
        Assert.Equal(id, stored["_id"].AsObjectId.ToString());
        Assert.Equal(memberOrgId, stored["memberOrgId"].AsObjectId.ToString());
        Assert.Equal(SamplePomSubmission.MemberName, stored["memberName"].AsString);
        Assert.Equal(SamplePomSubmission.Tonnes, stored["tonnageEntries"][0]["tonnes"].AsDecimal128.ToString());
        Assert.Equal(SamplePomSubmission.CompliancePeriodQuarter, stored["compliancePeriodQuarter"].AsString);
        Assert.True(stored["isCurrent"].AsBoolean);
        Assert.Equal(fixture.Defaults.InitialVersion, stored["version"].AsInt32);
        Assert.Equal(BsonNull.Value, stored["supersedesSubmissionId"]);
        Assert.Equal(PomSubmissionApiFixture.CreatedAt, stored["createdAtUtc"].ToUniversalTime());

        var log = AssertOneNewBatchLog(batchLog, batchLogsBefore);
        Assert.Equal(LogLevel.Information, log.Level);
        Assert.Equal(6, log.EventId.Id);
        Snapshot.Match(PinCreated(stored));
    }

    [Fact]
    public async Task UpsertBatch_WhenNoCurrentRowsExist_CreatesEachAndReturnsIdsInRequestOrder()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstMemberOrgId = ObjectId.GenerateNewId().ToString();
        var secondMemberOrgId = ObjectId.GenerateNewId().ToString();
        using var client = fixture.CreateClient();
        var body = BatchBody(
            BodyForMember(firstMemberOrgId),
            BodyForMember(secondMemberOrgId, "3.000"));
        const string batchLog = "Upserted batch of 2 POM submissions (2 created, 0 amended)";
        var batchLogsBefore = BatchLogCount(batchLog);

        // Act
        var response = await client.PostAsync("/pom-submissions/upsert-batch", Json(body), cancellationToken);
        var results = ReadResults(await response.Content.ReadAsStringAsync(cancellationToken));
        var ids = ReadIds(results);
        var first = await MemberSubmissions(firstMemberOrgId, cancellationToken);
        var second = await MemberSubmissions(secondMemberOrgId, cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, results.Length);
        AssertCoreResult(results[0], requestIndex: 0, ids[0], firstMemberOrgId, "Created");
        AssertCoreResult(results[1], requestIndex: 1, ids[1], secondMemberOrgId, "Created");
        var firstStored = Assert.Single(first);
        var secondStored = Assert.Single(second);
        Assert.Equal(ids[0], firstStored["_id"].AsObjectId.ToString());
        Assert.Equal(ids[1], secondStored["_id"].AsObjectId.ToString());
        Assert.Equal(SamplePomSubmission.Tonnes, firstStored["tonnageEntries"][0]["tonnes"].AsDecimal128.ToString());
        Assert.Equal("3.000", secondStored["tonnageEntries"][0]["tonnes"].AsDecimal128.ToString());
        Assert.Equal(fixture.Defaults.InitialVersion, firstStored["version"].AsInt32);
        Assert.Equal(fixture.Defaults.InitialVersion, secondStored["version"].AsInt32);
        Assert.True(firstStored["isCurrent"].AsBoolean);
        Assert.True(secondStored["isCurrent"].AsBoolean);
        Assert.Equal(batchLog, AssertOneNewBatchLog(batchLog, batchLogsBefore).Message);
    }

    [Fact]
    public async Task UpsertBatch_WhenCurrentSubmissionExists_ReplacesItAndKeepsThePreviousRow()
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
        const string batchLog = "Upserted batch of 1 POM submissions (0 created, 1 amended)";
        var batchLogsBefore = BatchLogCount(batchLog);

        // Act
        var response = await client.PostAsync(
            "/pom-submissions/upsert-batch",
            Json(BatchBody(BodyForMember(memberOrgId, "3.000"))),
            cancellationToken);
        var results = ReadResults(await response.Content.ReadAsStringAsync(cancellationToken));
        var ids = ReadIds(results);
        var stored = await MemberSubmissions(memberOrgId, cancellationToken);
        var currentResponse = await client.GetAsync($"/pom-submissions/{ids.Single()}", cancellationToken);
        using var currentDocument = JsonDocument.Parse(await currentResponse.Content.ReadAsStringAsync(cancellationToken));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var amendedResult = Assert.Single(results);
        var amendedId = Assert.Single(ids);
        AssertCoreResult(amendedResult, requestIndex: 0, amendedId, memberOrgId, "Amended");
        Assert.NotEqual(created.Id, amendedId);
        Assert.Equal(2, stored.Count);
        var previous = Assert.Single(stored, document => document["_id"].AsObjectId.ToString() == created.Id);
        var replacement = Assert.Single(stored, document => document["_id"].AsObjectId.ToString() == amendedId);
        Assert.False(previous["isCurrent"].AsBoolean);
        Assert.Equal(PomSubmissionApiFixture.CreatedAt, previous["updatedAtUtc"].ToUniversalTime());
        Assert.Equal(fixture.Defaults.InitialVersion, previous["version"].AsInt32);
        Assert.Equal(SamplePomSubmission.Tonnes, previous["tonnageEntries"][0]["tonnes"].AsDecimal128.ToString());
        Assert.True(replacement["isCurrent"].AsBoolean);
        Assert.Equal(BsonNull.Value, replacement["updatedAtUtc"]);
        Assert.Equal(created.Id, replacement["supersedesSubmissionId"].AsObjectId.ToString());
        Assert.Equal(fixture.Defaults.InitialVersion + 1, replacement["version"].AsInt32);
        Assert.Equal("3.000", replacement["tonnageEntries"][0]["tonnes"].AsDecimal128.ToString());
        Assert.Equal(HttpStatusCode.OK, currentResponse.StatusCode);
        Assert.Equal(created.Id, currentDocument.RootElement.GetProperty("supersedesSubmissionId").GetString());
        Assert.Equal(fixture.Defaults.InitialVersion + 1, currentDocument.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(batchLog, AssertOneNewBatchLog(batchLog, batchLogsBefore).Message);
    }

    [Fact]
    public async Task UpsertBatch_WhenTheBatchMixesNewAndExisting_CreatesAndAmendsTogether()
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
        const string batchLog = "Upserted batch of 2 POM submissions (1 created, 1 amended)";
        var batchLogsBefore = BatchLogCount(batchLog);

        // Act
        var response = await client.PostAsync(
            "/pom-submissions/upsert-batch",
            Json(BatchBody(BodyForMember(otherMemberOrgId, "4.000"), BodyForMember(memberOrgId, "3.000"))),
            cancellationToken);
        var results = ReadResults(await response.Content.ReadAsStringAsync(cancellationToken));
        var ids = ReadIds(results);
        var amended = await MemberSubmissions(memberOrgId, cancellationToken);
        var createdNow = await MemberSubmissions(otherMemberOrgId, cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, results.Length);
        AssertCoreResult(results[0], requestIndex: 0, ids[0], otherMemberOrgId, "Created");
        AssertCoreResult(results[1], requestIndex: 1, ids[1], memberOrgId, "Amended");
        var other = Assert.Single(createdNow);
        Assert.Equal(ids[0], other["_id"].AsObjectId.ToString());
        Assert.Equal(fixture.Defaults.InitialVersion, other["version"].AsInt32);
        Assert.Equal(BsonNull.Value, other["supersedesSubmissionId"]);
        Assert.Equal("4.000", other["tonnageEntries"][0]["tonnes"].AsDecimal128.ToString());
        Assert.True(other["isCurrent"].AsBoolean);
        var replacement = Assert.Single(amended, document => document["_id"].AsObjectId.ToString() == ids[1]);
        var previous = Assert.Single(amended, document => document["_id"].AsObjectId.ToString() == created.Id);
        Assert.False(previous["isCurrent"].AsBoolean);
        Assert.True(replacement["isCurrent"].AsBoolean);
        Assert.Equal(created.Id, replacement["supersedesSubmissionId"].AsObjectId.ToString());
        Assert.Equal(fixture.Defaults.InitialVersion + 1, replacement["version"].AsInt32);
        Assert.Equal("3.000", replacement["tonnageEntries"][0]["tonnes"].AsDecimal128.ToString());
        Assert.Equal(other["createdAtUtc"].ToUniversalTime(), replacement["createdAtUtc"].ToUniversalTime());
        Assert.Equal(batchLog, AssertOneNewBatchLog(batchLog, batchLogsBefore).Message);
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

    private static string PinCreated(BsonDocument stored)
    {
        var snapshot = stored.DeepClone().AsBsonDocument;
        snapshot["_id"] = ObjectId.Parse("000000000000000000000001");
        snapshot["memberOrgId"] = ObjectId.Parse(PomSubmissionApiFixture.MemberOrgId);
        snapshot["tonnageEntries"][0].AsBsonDocument["id"] = ObjectId.Parse("000000000000000000000002");
        return snapshot.ToJson(new JsonWriterSettings
        {
            OutputMode = JsonOutputMode.CanonicalExtendedJson,
            Indent = true,
            NewLineChars = "\n"
        });
    }
}
