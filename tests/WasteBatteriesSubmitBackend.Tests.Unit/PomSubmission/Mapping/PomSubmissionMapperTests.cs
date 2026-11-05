using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using WasteBatteriesSubmitBackend.Config;
using WasteBatteriesSubmitBackend.PomSubmission.Mapping;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;
using WasteBatteriesSubmitBackend.Tests.Common;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Mapping;

public class PomSubmissionMapperTests
{
    private static readonly DateTime s_createdAt = SamplePomSubmission.CreatedAt;

    [Fact]
    public void ToNewSubmission_WhenRequestIsComplete_MapsSubmittedSubmission()
    {
        // Arrange
        var request = SamplePomSubmission.Request();

        // Act
        var submission = Mapper().ToNewSubmission(request, s_createdAt);

        // Assert
        Assert.Equal(request.SchemeOrgId, submission.SchemeOrgId);
        Assert.Equal(request.MemberOrgId, submission.MemberOrgId);
        Assert.Equal(request.MemberName, submission.MemberName);
        Assert.Equal(request.MemberBprn, submission.MemberBprn);
        Assert.Equal(request.SchemeMembershipRef, submission.SchemeMembershipRef);
        Assert.Equal(request.CompliancePeriodYear, submission.CompliancePeriodYear);
        Assert.Equal(request.CompliancePeriodQuarter, submission.CompliancePeriodQuarter);
        Assert.Equal(request.DataBasis, submission.DataBasis);
        Assert.Equal("Submitted", submission.Status);
        Assert.Equal(s_createdAt, submission.SubmittedOnUtc);
        Assert.Equal(submission.CreatedAtUtc, submission.SubmittedOnUtc);
        Assert.True(submission.IsCurrent);
        Assert.Null(submission.SupersedesSubmissionId);
        Assert.Null(submission.UpdatedAtUtc);
        Assert.Equal(s_createdAt, submission.CreatedAtUtc);
        Assert.Equal(PomSubmissionModel.CurrentSchemaVersion, submission.SchemaVersion);
        Assert.Equal(1, submission.Version);
        Assert.True(ObjectId.TryParse(submission.Id, out _));

        var entry = Assert.Single(submission.TonnageEntries);
        Assert.True(ObjectId.TryParse(entry.Id, out _));
        Assert.NotEqual(submission.Id, entry.Id);
        Assert.Equal("Portable", entry.Classification.BatteryCategory);
        Assert.Equal(request.TonnageEntries[0].Classification.Chemistry, entry.Classification.Chemistry);
        Assert.Equal("Placed", entry.Activity);
        Assert.Equal(request.TonnageEntries[0].Tonnes, entry.Tonnes);
    }

    [Fact]
    public void ToNewSubmission_WhenTonnageEntriesAreEmpty_ThrowsArgumentException()
    {
        // Arrange
        var request = SamplePomSubmission.Request() with { TonnageEntries = [] };

        // Act
        PomSubmissionModel Act() => Mapper().ToNewSubmission(request, s_createdAt);

        // Assert
        var exception = Assert.Throws<ArgumentException>(Act);
        Assert.Equal("tonnageEntries", exception.ParamName);
    }

    [Fact]
    public void ToNewSubmission_WhenSubmissionIsMapped_RoundTripsThroughBson()
    {
        // Arrange
        MongoConventions.Register();
        var request = SamplePomSubmission.Request();
        var submission = Mapper().ToNewSubmission(request, s_createdAt);

        // Act
        var document = submission.ToBsonDocument();
        var stored = BsonSerializer.Deserialize<PomSubmissionModel>(document);

        // Assert
        Assert.Equal("Submitted", document["status"].AsString);
        Assert.Equal(1, document["version"].AsInt32);
        Assert.Equal(s_createdAt, document["submittedOnUtc"].ToUniversalTime());
        Assert.Equal(document["createdAtUtc"].ToUniversalTime(), document["submittedOnUtc"].ToUniversalTime());
        Assert.Equal(request.MemberName, document["memberName"].AsString);
        Assert.Equal("Placed", document["tonnageEntries"][0]["activity"].AsString);
        Assert.Equal(BsonType.Decimal128, document["tonnageEntries"][0]["tonnes"].BsonType);
        Assert.False(document["tonnageEntries"][0].AsBsonDocument.Contains("_id"));
        Assert.False(document.Contains("declaration"));
        Assert.Equal(submission.Id, stored.Id);
        Assert.Equal("Submitted", stored.Status);
        Assert.Equal(s_createdAt, stored.SubmittedOnUtc);
        Assert.Equal(stored.CreatedAtUtc, stored.SubmittedOnUtc);
        Assert.Equal(request.TonnageEntries[0].Tonnes, stored.TonnageEntries[0].Tonnes);
    }

    [Fact]
    public void ToNewSubmission_WhenInitialValuesAreConfigured_UsesThoseValues()
    {
        // Arrange
        var mapper = new PomSubmissionMapper(Options.Create(new PomSubmissionConfig
        {
            InitialStatus = "Filed",
            InitialVersion = 4,
            InitialActivity = "Held",
            InitialBatteryCategory = "Industrial"
        }));
        var request = new PomSubmissionRequest
        {
            TonnageEntries = [new TonnageEntryBase { Tonnes = "1.000" }]
        };

        // Act
        var submission = mapper.ToNewSubmission(request, s_createdAt);

        // Assert
        Assert.Equal("Filed", submission.Status);
        Assert.Equal(4, submission.Version);
        Assert.Equal(PomSubmissionModel.CurrentSchemaVersion, submission.SchemaVersion);
        Assert.Equal("Held", submission.TonnageEntries[0].Activity);
        Assert.Equal("Industrial", submission.TonnageEntries[0].Classification.BatteryCategory);
        Assert.Equal(s_createdAt, submission.CreatedAtUtc);
        Assert.Null(submission.SubmittedOnUtc);
    }

    [Fact]
    public void ToNewSubmission_WhenStatusIsSubmittedIgnoringCase_SetsSubmittedOnUtc()
    {
        // Arrange
        var mapper = new PomSubmissionMapper(Options.Create(new PomSubmissionConfig
        {
            InitialStatus = "sUbMiTtEd",
            InitialVersion = 1,
            InitialActivity = "Placed",
            InitialBatteryCategory = "Portable"
        }));
        var request = new PomSubmissionRequest
        {
            TonnageEntries = [new TonnageEntryBase { Tonnes = "1.000" }]
        };

        // Act
        var submission = mapper.ToNewSubmission(request, s_createdAt);

        // Assert
        Assert.Equal("sUbMiTtEd", submission.Status);
        Assert.Equal(s_createdAt, submission.SubmittedOnUtc);
        Assert.Equal(submission.CreatedAtUtc, submission.SubmittedOnUtc);
    }

    [Fact]
    public void ToNewSubmission_WhenVersionIsSupplied_UsesSuppliedVersion()
    {
        // Arrange
        var request = new PomSubmissionRequest
        {
            TonnageEntries = [new TonnageEntryBase { Tonnes = "1.000" }]
        };

        // Act
        var submission = Mapper().ToNewSubmission(request, s_createdAt, version: 3);

        // Assert
        Assert.Equal(3, submission.Version);
        Assert.Null(submission.SupersedesSubmissionId);
        Assert.Equal(s_createdAt, submission.CreatedAtUtc);
    }

    [Fact]
    public void ToNewSubmission_WhenSupersedesSubmissionIdIsSupplied_SetsIt()
    {
        // Arrange
        var previousId = ObjectId.GenerateNewId().ToString();
        var request = new PomSubmissionRequest
        {
            TonnageEntries = [new TonnageEntryBase { Tonnes = "1.000" }]
        };

        // Act
        var submission = Mapper().ToNewSubmission(
            request,
            s_createdAt,
            version: 2,
            supersedesSubmissionId: previousId);

        // Assert
        Assert.Equal(2, submission.Version);
        Assert.Equal(previousId, submission.SupersedesSubmissionId);
        Assert.True(submission.IsCurrent);
    }

    private static PomSubmissionMapper Mapper() =>
        new(Options.Create(new PomSubmissionConfig
        {
            InitialStatus = "Submitted",
            InitialVersion = 1,
            InitialActivity = "Placed",
            InitialBatteryCategory = "Portable"
        }));
}
