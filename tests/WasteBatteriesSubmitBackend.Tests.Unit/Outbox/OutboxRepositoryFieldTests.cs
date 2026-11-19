using WasteBatteriesSubmitBackend.Outbox.Repositories;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Outbox;

public class OutboxRepositoryFieldTests
{
    [Fact]
    public void Field_WhenThePropertyIsCamelCased_UsesTheStoredElementName()
    {
        Assert.Equal("claimedUntilUtc", OutboxRepository.Field(outboxEvent => outboxEvent.ClaimedUntilUtc));
        Assert.Equal("sent", OutboxRepository.Field(outboxEvent => outboxEvent.Sent));
        Assert.Equal("nextAttemptAtUtc", OutboxRepository.Field(outboxEvent => outboxEvent.NextAttemptAtUtc));
    }

    [Fact]
    public void Field_WhenThePropertyIsTheDocumentId_UsesTheBsonId()
    {
        Assert.Equal("_id", OutboxRepository.Field(outboxEvent => outboxEvent.Id));
    }
}
