using System.Text.Json;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using WasteBatteriesSubmitBackend.Config;
using WasteBatteriesSubmitBackend.PomSubmission.Mapping;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.Utils.Mongo;
using WasteBatteriesSubmitBackend.Utils.Time;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Mapping;

public class PomSubmissionMapperTests
{
    private static readonly DateTime CreatedAt = new(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ToNewSubmission_files_the_submission_as_submitted()
    {
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

        var submission = Mapper().ToNewSubmission(request!);

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
    public void ToNewSubmission_rejects_a_submission_with_no_tonnage_entries()
    {
        var request = new PomSubmissionRequest
        {
            SchemeOrgId = ObjectId.GenerateNewId().ToString(),
            MemberOrgId = ObjectId.GenerateNewId().ToString(),
            MemberName = "Northwind Batteries Ltd",
            TonnageEntries = []
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            Mapper().ToNewSubmission(request));

        Assert.Equal("tonnageEntries", exception.ParamName);
    }

    [Fact]
    public void ToNewSubmission_round_trips_through_bson_as_submitted()
    {
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
        });

        var document = submission.ToBsonDocument();
        var stored = BsonSerializer.Deserialize<PomSubmissionModel>(document);

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
    public void ToNewSubmission_uses_the_configured_initial_values()
    {
        var mapper = new PomSubmissionMapper(new FixedTime(CreatedAt), Options.Create(new PomSubmissionConfig
        {
            InitialStatus = "Filed",
            InitialVersion = 4,
            InitialActivity = "Held"
        }));

        var submission = mapper.ToNewSubmission(new PomSubmissionRequest
        {
            TonnageEntries = [new TonnageEntryBase { Tonnes = "1.000" }]
        });

        Assert.Equal("Filed", submission.Status);
        Assert.Equal(4, submission.Version);
        Assert.Equal(PomSubmissionModel.CurrentSchemaVersion, submission.SchemaVersion);
        Assert.Equal("Held", submission.TonnageEntries[0].Activity);
        Assert.Equal(CreatedAt, submission.SubmittedOnUtc);
        Assert.Equal(submission.CreatedAtUtc, submission.SubmittedOnUtc);
    }

    [Fact]
    public void Request_json_rejects_a_property_that_is_not_on_the_model()
    {
        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PomSubmissionRequest>(
            """{"memberName":"Northwind Batteries Ltd","regulatorNote":"ignore me"}""",
            JsonSerializerOptions.Web));

        Assert.Contains("regulatorNote", exception.Message);
    }

    private static PomSubmissionMapper Mapper() =>
        new(new FixedTime(CreatedAt), Options.Create(new PomSubmissionConfig
        {
            InitialStatus = "Submitted",
            InitialVersion = 1,
            InitialActivity = "Placed"
        }));

    private sealed class FixedTime(DateTime utcNow) : ITimeProvider
    {
        public DateTime UtcNow => utcNow;
    }
}
