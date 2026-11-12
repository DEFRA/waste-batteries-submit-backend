using WasteBatteriesSubmitBackend.Tests.Common;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Utils.Time;

public class SteppingTimeTests
{
    private static readonly DateTime s_start = new(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void UtcNow_WhenReadTwice_MovesForwardOneMillisecond()
    {
        var time = new SteppingTime(s_start);

        var first = time.UtcNow;
        var second = time.UtcNow;

        Assert.Equal(DateTimeKind.Utc, first.Kind);
        Assert.Equal(s_start, first);
        Assert.Equal(s_start.AddMilliseconds(1), second);
    }

    [Fact]
    public void Reset_WhenCalled_StartsAgainAtTheOriginalInstant()
    {
        var time = new SteppingTime(s_start);
        _ = time.UtcNow;
        _ = time.UtcNow;

        time.Reset();

        Assert.Equal(s_start, time.UtcNow);
    }
}
