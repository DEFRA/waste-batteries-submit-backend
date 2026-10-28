using WasteBatteriesSubmitBackend.Utils.Time;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Utils.Time;

public sealed class FixedTime(DateTime utcNow) : ITimeProvider
{
    public DateTime UtcNow { get; } = utcNow;
}
