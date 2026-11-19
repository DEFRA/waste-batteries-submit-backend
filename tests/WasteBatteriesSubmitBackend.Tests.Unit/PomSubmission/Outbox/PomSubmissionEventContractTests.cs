using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Json.Schema;
using Microsoft.Extensions.Options;
using NSubstitute;
using WasteBatteriesSubmitBackend.Outbox;
using WasteBatteriesSubmitBackend.Outbox.Relay;
using WasteBatteriesSubmitBackend.PomSubmission.Outbox;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Outbox;

// The published contract is contracts/pom-submission-events/pom-submission-events.json.
// Each example is the JSON the relay publishes for that event type, and that JSON has to satisfy the schema.
public class PomSubmissionEventContractTests
{
    private const string TopicArn = "arn:aws:sns:eu-west-2:000000000000:waste_batteries_submit_pom_submission_events";

    private const string TopicName = "waste_batteries_submit_pom_submission_events";

    [Fact]
    public void Contract_ListsTheThreeSubmissionEventTypes()
    {
        var contract = Contract();
        var names = contract["components"]!["messages"]!.AsObject()
            .Select(message => message.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
        [
            PomSubmissionOutboxEvent.PomSubmissionAmendedType,
            PomSubmissionOutboxEvent.PomSubmissionCreatedType,
            PomSubmissionOutboxEvent.PomSubmissionSavedType
        ],
        names);
        Assert.Equal(TopicName, contract["channels"]!["pomSubmissionEvents"]!["address"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(PomSubmissionOutboxEvent.PomSubmissionCreatedType)]
    [InlineData(PomSubmissionOutboxEvent.PomSubmissionAmendedType)]
    [InlineData(PomSubmissionOutboxEvent.PomSubmissionSavedType)]
    public async Task PublishedMessage_MatchesTheContractExampleAndSchema(string eventType)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        MongoConventions.Register();
        var contract = Contract();
        var message = contract["components"]!["messages"]![eventType]!.AsObject();
        var example = Assert.Single(message["examples"]!.AsArray());
        var payload = example!["payload"]!.AsObject();
        var occurredAt = DateTime.Parse(
            payload["occurredAt"]!.GetValue<string>(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        var outboxEvent = new PomSubmissionOutboxEvent
        {
            Id = payload["eventId"]!.GetValue<string>(),
            EventType = payload["eventType"]!.GetValue<string>(),
            SchemaVersion = payload["schemaVersion"]!.GetValue<int>(),
            SubjectId = payload["subjectId"]!.GetValue<string>(),
            Version = payload["version"]!.GetValue<long>(),
            PartitionKey = payload["partitionKey"]!.GetValue<string>(),
            OccurredAtUtc = occurredAt,
            ActorId = payload["actorId"]!.GetValue<string>(),
            CorrelationId = payload["correlationId"]!.GetValue<string>(),
            Sent = false,
            SchemeOrgId = payload["schemeOrgId"]!.GetValue<string>(),
            CompliancePeriodYear = payload["compliancePeriodYear"]!.GetValue<int>()
        };
        Assert.Equal(eventType, outboxEvent.EventType);
        Assert.Equal(PomSubmissionOutboxEvents.CurrentSchemaVersion, outboxEvent.SchemaVersion);
        Assert.Equal(OutboxEvents.CommitVersion(occurredAt), outboxEvent.Version);

        var document = OutboxDocuments.ToBsonDocument(outboxEvent);
        var simpleNotificationService = Substitute.For<IAmazonSimpleNotificationService>();
        PublishRequest? captured = null;
        simpleNotificationService
            .PublishAsync(Arg.Do<PublishRequest>(request => captured = request), Arg.Any<CancellationToken>())
            .Returns(new PublishResponse());
        var publisher = new SnsOutboxPublisher(
            simpleNotificationService,
            Options.Create(new OutboxRelayOptions { TopicArn = TopicArn }));

        await publisher.PublishAsync(document, cancellationToken);

        Assert.NotNull(captured);
        Assert.Equal(TopicArn, captured.TopicArn);
        var published = JsonNode.Parse(captured.Message)!.AsObject();
        Assert.True(JsonNode.DeepEquals(payload, published), captured.Message);
        AssertValid(Schema(contract, message["payload"]!), published);

        var withMember = JsonNode.Parse(captured.Message)!.AsObject();
        withMember["memberOrgId"] = payload["schemeOrgId"]!.GetValue<string>();
        AssertInvalid(Schema(contract, message["payload"]!), withMember);

        var attributes = new JsonObject
        {
            ["eventId"] = captured.MessageAttributes["eventId"].StringValue,
            ["eventType"] = captured.MessageAttributes["eventType"].StringValue
        };
        Assert.Equal(payload["eventId"]!.GetValue<string>(), attributes["eventId"]!.GetValue<string>());
        Assert.Equal(eventType, attributes["eventType"]!.GetValue<string>());
        Assert.Equal(2, captured.MessageAttributes.Count);
        AssertValid(Schema(contract, message["headers"]!), attributes);
        Assert.True(JsonNode.DeepEquals(example["headers"], attributes));
    }

    private static JsonObject Contract()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WasteBatteriesSubmitBackend.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not find the repository root.");
        }

        var path = Path.Combine(directory.FullName, "contracts", "pom-submission-events", "pom-submission-events.json");
        return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }

    // AsyncAPI keeps the schemas under components. JSON Schema resolves refs inside one document, so the
    // component schemas are copied into $defs and the component refs are rewritten to match.
    private static JsonSchema Schema(JsonObject contract, JsonNode schemaNode)
    {
        var bundled = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$defs"] = Rewrite(contract["components"]!["schemas"]!),
            ["allOf"] = new JsonArray(Rewrite(schemaNode))
        };

        return JsonSchema.FromText(bundled.ToJsonString());
    }

    private static JsonNode Rewrite(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                var copy = new JsonObject();
                foreach (var property in obj)
                {
                    if (property.Value is null)
                    {
                        copy[property.Key] = null;
                        continue;
                    }

                    if (property.Key == "$ref")
                    {
                        var reference = property.Value.GetValue<string>();
                        const string prefix = "#/components/schemas/";
                        copy["$ref"] = reference.StartsWith(prefix, StringComparison.Ordinal)
                            ? "#/$defs/" + reference[prefix.Length..]
                            : reference;
                        continue;
                    }

                    copy[property.Key] = Rewrite(property.Value);
                }

                return copy;
            case JsonArray array:
                var items = new JsonArray();
                foreach (var item in array)
                {
                    items.Add(item is null ? null : Rewrite(item));
                }

                return items;
            default:
                return node.DeepClone();
        }
    }

    private static void AssertValid(JsonSchema schema, JsonNode instance)
    {
        var result = schema.Evaluate(JsonSerializer.SerializeToElement(instance));
        Assert.True(result.IsValid, Describe(result));
    }

    private static void AssertInvalid(JsonSchema schema, JsonNode instance)
    {
        var result = schema.Evaluate(JsonSerializer.SerializeToElement(instance));
        Assert.False(result.IsValid);
    }

    private static string Describe(EvaluationResults result)
    {
        return string.Join(
            Environment.NewLine,
            (result.Details ?? [])
                .Where(detail => detail.Errors is { Count: > 0 })
                .SelectMany(detail => detail.Errors!.Select(error => $"{detail.InstanceLocation}: {error.Value}")));
    }
}
