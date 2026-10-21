using WasteBatteriesSubmitBackend.Utils.Time;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Utils.Time;

public class TimeProviderTests
{
    [Fact]
    public void AsUtc_labels_an_unspecified_instant_without_shifting_it()
    {
        ITimeProvider time = new FixedTime();
        var unspecified = new DateTime(2026, 7, 31, 23, 0, 0, DateTimeKind.Unspecified);

        var labelled = time.AsUtc(unspecified);

        Assert.NotNull(labelled);
        Assert.Equal(DateTimeKind.Utc, labelled.Value.Kind);
        Assert.Equal(unspecified.Ticks, labelled.Value.Ticks);
    }

    [Fact]
    public void AsUtc_keeps_a_utc_instant_and_returns_null_for_null()
    {
        ITimeProvider time = new FixedTime();
        var utc = new DateTime(2026, 7, 31, 23, 0, 0, DateTimeKind.Utc);

        Assert.Equal(utc, time.AsUtc(utc));
        Assert.Null(time.AsUtc(null));
    }

    [Fact]
    public void SystemTimeProvider_returns_utc_now()
    {
        var before = DateTime.UtcNow;

        var now = new SystemTimeProvider().UtcNow;

        Assert.Equal(DateTimeKind.Utc, now.Kind);
        Assert.InRange(now, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
    }

    private sealed class FixedTime : ITimeProvider
    {
        public DateTime UtcNow => new(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);
    }
}
