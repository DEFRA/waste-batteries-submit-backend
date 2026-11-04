using WasteBatteriesSubmitBackend.Utils.Time;

namespace WasteBatteriesSubmitBackend.Tests.Common;

public sealed class FixedTime(DateTime utcNow) : ITimeProvider
{
    public DateTime UtcNow { get; } = utcNow;
}
