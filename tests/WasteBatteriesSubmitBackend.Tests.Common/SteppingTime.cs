using WasteBatteriesSubmitBackend.Utils.Time;

namespace WasteBatteriesSubmitBackend.Tests.Common;

// Each read is one millisecond later. Integration saves of one subject then store distinct outbox versions.
public sealed class SteppingTime(DateTime start) : ITimeProvider
{
    private long _ticks = start.Ticks;

    public DateTime UtcNow
    {
        get
        {
            var ticks = Interlocked.Add(ref _ticks, TimeSpan.TicksPerMillisecond) - TimeSpan.TicksPerMillisecond;
            return new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    public void Reset() => Interlocked.Exchange(ref _ticks, start.Ticks);
}
