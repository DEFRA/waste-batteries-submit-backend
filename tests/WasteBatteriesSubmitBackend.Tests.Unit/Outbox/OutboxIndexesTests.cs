using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using WasteBatteriesSubmitBackend.Outbox;
using WasteBatteriesSubmitBackend.Outbox.Repositories;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Outbox;

public class OutboxIndexesTests
{
    [Fact]
    public void Create_WhenCalled_DefinesTheRelayAndRetentionIndexes()
    {
        MongoConventions.Register();
        var indexKeys = Builders<OutboxEvent>.IndexKeys;

        var indexes = OutboxIndexes.Create(indexKeys);
        var args = RenderArgs();
        var sentByTime = indexes[0];
        var expiresAt = indexes[1];
        var subjectVersion = indexes[2];

        Assert.Equal(3, indexes.Count);
        Assert.Equal("sent_occurred_at", sentByTime.Options.Name);
        Assert.Equal(
            "{ \"sent\" : 1, \"occurredAtUtc\" : 1 }",
            sentByTime.Keys.Render(args).ToString());
        Assert.Null(sentByTime.Options.PartialFilterExpression);
        Assert.NotEqual(true, sentByTime.Options.Unique);

        Assert.Equal("expires_at", expiresAt.Options.Name);
        Assert.Equal("{ \"expiresAtUtc\" : 1 }", expiresAt.Keys.Render(args).ToString());
        Assert.Equal(TimeSpan.Zero, expiresAt.Options.ExpireAfter);
        Assert.Null(expiresAt.Options.PartialFilterExpression);
        Assert.NotEqual(true, expiresAt.Options.Unique);

        Assert.Equal("subject_version", subjectVersion.Options.Name);
        Assert.Equal(
            "{ \"subjectId\" : 1, \"version\" : 1 }",
            subjectVersion.Keys.Render(args).ToString());
        Assert.True(subjectVersion.Options.Unique);
        Assert.Null(subjectVersion.Options.PartialFilterExpression);
    }

    private static RenderArgs<OutboxEvent> RenderArgs()
    {
        var registry = BsonSerializer.SerializerRegistry;
        return new RenderArgs<OutboxEvent>(
            registry.GetSerializer<OutboxEvent>(),
            registry,
            default,
            false,
            false,
            false,
            null);
    }
}
