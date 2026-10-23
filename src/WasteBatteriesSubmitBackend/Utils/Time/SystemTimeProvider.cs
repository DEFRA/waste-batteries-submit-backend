namespace WasteBatteriesSubmitBackend.Utils.Time;

public sealed class SystemTimeProvider : ITimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
