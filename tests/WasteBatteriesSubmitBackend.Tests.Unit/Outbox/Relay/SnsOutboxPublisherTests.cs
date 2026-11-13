using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using NSubstitute;
using WasteBatteriesSubmitBackend.Outbox;
using WasteBatteriesSubmitBackend.Outbox.Relay;
using WasteBatteriesSubmitBackend.PomSubmission.Outbox;
using WasteBatteriesSubmitBackend.Tests.Common;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Outbox.Relay;

public class SnsOutboxPublisherTests
{
    private const string TopicArn = "arn:aws:sns:eu-west-2:000000000000:waste_batteries_submit_events";

    [Fact]
    public async Task PublishAsync_WhenTheDocumentIsUnsent_SetsTheTopicAndTheTwoAttributes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var simpleNotificationService = Substitute.For<IAmazonSimpleNotificationService>();
        PublishRequest? captured = null;
        simpleNotificationService
            .PublishAsync(Arg.Do<PublishRequest>(request => captured = request), Arg.Any<CancellationToken>())
            .Returns(new PublishResponse());
        var publisher = new SnsOutboxPublisher(
            simpleNotificationService,
            Options.Create(new OutboxRelayOptions { TopicArn = TopicArn }));
        var document = Document();

        await publisher.PublishAsync(document, cancellationToken);

        Assert.NotNull(captured);
        Assert.Equal(TopicArn, captured.TopicArn);
        Assert.Equal(2, captured.MessageAttributes.Count);
        Assert.Equal(document["_id"].AsString, captured.MessageAttributes["eventId"].StringValue);
        Assert.Equal("String", captured.MessageAttributes["eventId"].DataType);
        Assert.Equal(document["eventType"].AsString, captured.MessageAttributes["eventType"].StringValue);
        Assert.Equal("String", captured.MessageAttributes["eventType"].DataType);
        Assert.Contains("\"eventId\":", captured.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sentAtUtc", captured.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("claimedBy", captured.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishAsync_WhenSnsRejectsTheMessage_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var simpleNotificationService = Substitute.For<IAmazonSimpleNotificationService>();
        simpleNotificationService
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<PublishResponse>>(_ => throw new AmazonServiceException("sns down"));
        var publisher = new SnsOutboxPublisher(
            simpleNotificationService,
            Options.Create(new OutboxRelayOptions { TopicArn = TopicArn }));

        var exception = await Assert.ThrowsAsync<AmazonServiceException>(() =>
            publisher.PublishAsync(Document(), cancellationToken));

        Assert.Equal("sns down", exception.Message);
    }

    private static BsonDocument Document() => new()
    {
        { "_id", "3f1a6c0e-4b2d-4e7a-9c11-8d5e2a7b6c40" },
        { "eventType", PomSubmissionOutboxEvent.PomSubmissionCreatedType },
        { "schemaVersion", 1 },
        { "subjectId", $"{SamplePomSubmission.SchemeOrgId}:2026" },
        { "version", OutboxEvents.CommitVersion(SamplePomSubmission.CreatedAt) },
        { "partitionKey", SamplePomSubmission.SchemeOrgId },
        { "occurredAtUtc", SamplePomSubmission.CreatedAt },
        { "actorId", "actor-1" },
        { "correlationId", "correlation-1" },
        { "sent", false },
        { "claimedBy", "relay-1" }
    };
}
