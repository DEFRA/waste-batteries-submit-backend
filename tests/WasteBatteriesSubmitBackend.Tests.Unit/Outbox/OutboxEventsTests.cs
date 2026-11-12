using WasteBatteriesSubmitBackend.Outbox;
using WasteBatteriesSubmitBackend.Tests.Common;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Outbox;

public class OutboxEventsTests
{
    [Fact]
    public void NewId_WhenCalledTwice_ReturnsTwoGuids()
    {
        var first = OutboxEvents.NewId();
        var second = OutboxEvents.NewId();

        Assert.True(Guid.TryParse(first, out _));
        Assert.True(Guid.TryParse(second, out _));
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void CommitVersion_WhenTheTimeIsNotUtc_Throws()
    {
        var unspecified = DateTime.SpecifyKind(SamplePomSubmission.CreatedAt, DateTimeKind.Unspecified);

        var exception = Assert.Throws<ArgumentException>(() => OutboxEvents.CommitVersion(unspecified));

        Assert.Equal("occurredAtUtc", exception.ParamName);
    }

    [Fact]
    public void CommitVersion_WhenTheTimeIsUtc_UsesUnixMilliseconds()
    {
        var version = OutboxEvents.CommitVersion(SamplePomSubmission.CreatedAt);

        Assert.Equal(
            new DateTimeOffset(SamplePomSubmission.CreatedAt).ToUnixTimeMilliseconds(),
            version);
    }
}
