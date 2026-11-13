using WasteBatteriesSubmitBackend.Outbox.Relay;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Outbox.Relay;

public class OutboxRetryDelayTests
{
    [Fact]
    public void ForAttempt_WhenTheFirstPublishFails_UsesTheInitialDelay()
    {
        var delay = OutboxRetryDelay.ForAttempt(Options(initial: 1, max: 300, multiplier: 2), attemptCount: 1);

        Assert.Equal(TimeSpan.FromSeconds(1), delay);
    }

    [Fact]
    public void ForAttempt_WhenThePublishFailsAgain_DoublesTheDelay()
    {
        var delay = OutboxRetryDelay.ForAttempt(Options(initial: 1, max: 300, multiplier: 2), attemptCount: 2);

        Assert.Equal(TimeSpan.FromSeconds(2), delay);
    }

    [Fact]
    public void ForAttempt_WhenTheDelayWouldPassTheCap_StopsAtTheCap()
    {
        var delay = OutboxRetryDelay.ForAttempt(Options(initial: 1, max: 300, multiplier: 2), attemptCount: 10);

        Assert.Equal(TimeSpan.FromSeconds(300), delay);
    }

    [Fact]
    public void ForAttempt_WhenTheInitialDelayIsAlreadyAboveTheCap_UsesTheCap()
    {
        var delay = OutboxRetryDelay.ForAttempt(Options(initial: 10, max: 5, multiplier: 2), attemptCount: 1);

        Assert.Equal(TimeSpan.FromSeconds(5), delay);
    }

    [Fact]
    public void ForAttempt_WhenTheAttemptCountIsBelowOne_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            OutboxRetryDelay.ForAttempt(Options(initial: 1, max: 300, multiplier: 2), attemptCount: 0));

        Assert.Equal("attemptCount", exception.ParamName);
    }

    private static OutboxRelayOptions Options(int initial, int max, double multiplier) => new()
    {
        RetryInitialDelaySeconds = initial,
        RetryMaxDelaySeconds = max,
        RetryMultiplier = multiplier
    };
}
