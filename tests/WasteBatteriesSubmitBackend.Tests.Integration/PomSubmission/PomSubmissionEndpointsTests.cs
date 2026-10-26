using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;
using System.Net;
using System.Text;
using System.Text.Json;
using WasteBatteriesSubmitBackend.PomSubmission.Models;

namespace WasteBatteriesSubmitBackend.Tests.Integration.PomSubmission;

public partial class PomSubmissionEndpointsTests(PomSubmissionApiFixture fixture) : IClassFixture<PomSubmissionApiFixture>
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
        var created = fixture.Logger.Entries.Count(entry => entry.EventId.Id == 4);
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

    private static string OptionalBody(string memberOrgId, string form)
    {
        var classification = form switch
        {
            "omitted" => """
              "batteryCategory": "Portable",
              "chemistry": "Alkaline"
              """,
            "null" => """
              "batteryCategory": "Portable",
              "chemistry": "Alkaline",
              "subCategory": null,
              "cellType": null,
              "equipmentState": null
              """,
            "empty" => """
              "batteryCategory": "Portable",
              "chemistry": "Alkaline",
              "subCategory": "",
              "cellType": "",
              "equipmentState": ""
              """,
            "whitespace" => """
              "batteryCategory": "Portable",
              "chemistry": "Alkaline",
              "subCategory": "   ",
              "cellType": " \t ",
              "equipmentState": "  "
              """,
            _ => throw new ArgumentOutOfRangeException(nameof(form), form, "Unknown optional text form.")
        };
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

    private async Task<CreatedSubmission> CreateSubmission(
        HttpClient client,
        string schemeOrgId,
        string memberOrgId,
        int year,
        string quarter,
        CancellationToken cancellationToken)
    {
        var body = CreateBody
            .Replace(PomSubmissionApiFixture.SchemeOrgId, schemeOrgId, StringComparison.Ordinal)
            .Replace(PomSubmissionApiFixture.MemberOrgId, memberOrgId, StringComparison.Ordinal)
            .Replace("BPRN-EA-2026-", $"BPRN-EA-{year}-", StringComparison.Ordinal)
            .Replace("\"compliancePeriodYear\": 2026", $"\"compliancePeriodYear\": {year}", StringComparison.Ordinal)
            .Replace("\"compliancePeriodQuarter\": \"Quarter 2\"", $"\"compliancePeriodQuarter\": \"{quarter}\"", StringComparison.Ordinal);
        var response = await client.PostAsync("/pom-submissions", Json(body), cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(json);
        return new CreatedSubmission(document.RootElement.GetProperty("id").GetString()!, json);
    }

    private async Task SetCurrent(string id, bool isCurrent, CancellationToken cancellationToken)
    {
        await fixture.Submissions.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(id)),
            Builders<BsonDocument>.Update.Set("isCurrent", isCurrent),
            cancellationToken: cancellationToken);
    }

    private static async Task<JsonDocument> ReadPage(
        HttpClient client,
        CancellationToken cancellationToken,
        params (string Key, string Value)[] query)
    {
        var response = await client.GetAsync(SubmissionsUrl(query), cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(json);
    }

    private static string SubmissionsUrl(params (string Key, string Value)[] query)
    {
        if (query.Length == 0)
        {
            return "/pom-submissions";
        }

        var text = string.Join(
            "&",
            query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return $"/pom-submissions?{text}";
    }

    private static void AssertPage(
        JsonElement page,
        int pageNumber,
        int pageSize,
        int totalPages,
        int totalCount,
        bool hasPrevious,
        bool hasNext,
        params string[] ids)
    {
        Assert.Equal(pageNumber, page.GetProperty("pageNumber").GetInt32());
        Assert.Equal(pageSize, page.GetProperty("pageSize").GetInt32());
        Assert.Equal(totalPages, page.GetProperty("totalPages").GetInt32());
        Assert.Equal(totalCount, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(hasPrevious, page.GetProperty("hasPreviousPage").GetBoolean());
        Assert.Equal(hasNext, page.GetProperty("hasNextPage").GetBoolean());
        var actual = page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()).ToArray();
        Assert.Equal(ids, actual);
    }

    private static JsonElement Item(JsonElement page, string id) =>
        page.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);

    private sealed record CreatedSubmission(string Id, string Json);

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");
}
