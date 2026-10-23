using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using System.Text.Json;
using WasteBatteriesSubmitBackend.Config;
using WasteBatteriesSubmitBackend.PomSubmission.Mapping;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Mapping;

public class PomSubmissionMapperTests
{
    private static readonly DateTime CreatedAt = new(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ToNewSubmission_WhenRequestIsComplete_MapsSubmittedSubmission()
    {
        // Arrange
        var request = JsonSerializer.Deserialize<PomSubmissionRequest>(
            """
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
            """,
            JsonSerializerOptions.Web);

        // Act
        var submission = Mapper().ToNewSubmission(request!, CreatedAt);

        // Assert
        Assert.Equal("674a1b2c3d4e5f6789012345", submission.SchemeOrgId);
        Assert.Equal("674a1b2c3d4e5f6789012346", submission.MemberOrgId);
        Assert.Equal("Northwind Batteries Ltd", submission.MemberName);
        Assert.Equal("BPRN-EA-2026-000123", submission.MemberBprn);
        Assert.Equal("SCH-00417", submission.SchemeMembershipRef);
        Assert.Equal(2026, submission.CompliancePeriodYear);
        Assert.Equal("Quarter 2", submission.CompliancePeriodQuarter);
        Assert.Equal("Actual", submission.DataBasis);
        Assert.Equal("Submitted", submission.Status);
        Assert.Equal(CreatedAt, submission.SubmittedOnUtc);
        Assert.Equal(submission.CreatedAtUtc, submission.SubmittedOnUtc);
        Assert.True(submission.IsCurrent);
        Assert.Null(submission.SupersedesSubmissionId);
        Assert.Null(submission.UpdatedAtUtc);
        Assert.Equal(CreatedAt, submission.CreatedAtUtc);
        Assert.Equal(PomSubmissionModel.CurrentSchemaVersion, submission.SchemaVersion);
        Assert.Equal(1, submission.Version);
        Assert.True(ObjectId.TryParse(submission.Id, out _));

        var entry = Assert.Single(submission.TonnageEntries);
        Assert.True(ObjectId.TryParse(entry.Id, out _));
        Assert.NotEqual(submission.Id, entry.Id);
        Assert.Equal("Portable", entry.Classification.BatteryCategory);
        Assert.Equal("Alkaline", entry.Classification.Chemistry);
        Assert.Equal("General use", entry.Classification.SubCategory);
        Assert.Equal("Primary", entry.Classification.CellType);
        Assert.Equal("In equipment", entry.Classification.EquipmentState);
        Assert.Equal("Placed", entry.Activity);
        Assert.Equal("12.500", entry.Tonnes);
    }

    [Fact]
    public void ToNewSubmission_WhenTonnageEntriesAreEmpty_ThrowsArgumentException()
    {
        // Arrange
        var request = new PomSubmissionRequest
        {
            SchemeOrgId = ObjectId.GenerateNewId().ToString(),
            MemberOrgId = ObjectId.GenerateNewId().ToString(),
            MemberName = "Northwind Batteries Ltd",
            TonnageEntries = []
        };

        // Act
        var act = () => Mapper().ToNewSubmission(request, CreatedAt);

        // Assert
        var exception = Assert.Throws<ArgumentException>(act);
        Assert.Equal("tonnageEntries", exception.ParamName);
    }

    [Fact]
    public void ToNewSubmission_WhenSubmissionIsMapped_RoundTripsThroughBson()
    {
        // Arrange
        MongoConventions.Register();
        var submission = Mapper().ToNewSubmission(new PomSubmissionRequest
        {
            SchemeOrgId = "674a1b2c3d4e5f6789012345",
            MemberOrgId = "674a1b2c3d4e5f6789012346",
            MemberName = "Northwind Batteries Ltd",
            DataBasis = "Actual",
            TonnageEntries =
            [
                new TonnageEntryBase
                {
                    Tonnes = "12.500"
                }
            ]
        }, CreatedAt);

        // Act
        var document = submission.ToBsonDocument();
        var stored = BsonSerializer.Deserialize<PomSubmissionModel>(document);

        // Assert
        Assert.Equal("Submitted", document["status"].AsString);
        Assert.Equal(1, document["version"].AsInt32);
        Assert.Equal(CreatedAt, document["submittedOnUtc"].ToUniversalTime());
        Assert.Equal(document["createdAtUtc"].ToUniversalTime(), document["submittedOnUtc"].ToUniversalTime());
        Assert.Equal("Northwind Batteries Ltd", document["memberName"].AsString);
        Assert.Equal("Placed", document["tonnageEntries"][0]["activity"].AsString);
        Assert.Equal(BsonType.Decimal128, document["tonnageEntries"][0]["tonnes"].BsonType);
        Assert.False(document["tonnageEntries"][0].AsBsonDocument.Contains("_id"));
        Assert.False(document.Contains("declaration"));
        Assert.Equal(submission.Id, stored.Id);
        Assert.Equal("Submitted", stored.Status);
        Assert.Equal(CreatedAt, stored.SubmittedOnUtc);
        Assert.Equal(stored.CreatedAtUtc, stored.SubmittedOnUtc);
        Assert.Equal("12.500", stored.TonnageEntries[0].Tonnes);
    }

    [Fact]
    public void ToNewSubmission_WhenInitialValuesAreConfigured_UsesThoseValues()
    {
        // Arrange
        var mapper = new PomSubmissionMapper(Options.Create(new PomSubmissionConfig
        {
            InitialStatus = "Filed",
            InitialVersion = 4,
            InitialActivity = "Held"
        }));
        var request = new PomSubmissionRequest
        {
            TonnageEntries = [new TonnageEntryBase { Tonnes = "1.000" }]
        };

        // Act
        var submission = mapper.ToNewSubmission(request, CreatedAt);

        // Assert
        Assert.Equal("Filed", submission.Status);
        Assert.Equal(4, submission.Version);
        Assert.Equal(PomSubmissionModel.CurrentSchemaVersion, submission.SchemaVersion);
        Assert.Equal("Held", submission.TonnageEntries[0].Activity);
        Assert.Equal(CreatedAt, submission.CreatedAtUtc);
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
            InitialActivity = "Placed"
        }));
        var request = new PomSubmissionRequest
        {
            TonnageEntries = [new TonnageEntryBase { Tonnes = "1.000" }]
        };

        // Act
        var submission = mapper.ToNewSubmission(request, CreatedAt);

        // Assert
        Assert.Equal("sUbMiTtEd", submission.Status);
        Assert.Equal(CreatedAt, submission.SubmittedOnUtc);
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
        var submission = Mapper().ToNewSubmission(request, CreatedAt, version: 3);

        // Assert
        Assert.Equal(3, submission.Version);
        Assert.Null(submission.SupersedesSubmissionId);
        Assert.Equal(CreatedAt, submission.CreatedAtUtc);
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
            CreatedAt,
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
            InitialActivity = "Placed"
        }));
}
