namespace WasteBatteriesSubmitBackend.Utils.Time;

public interface ITimeProvider
{
    DateTime UtcNow { get; }

    // Labels an instant as UTC. Unspecified clock times are not shifted:
    // this model does not convert between timezones.
    DateTime? AsUtc(DateTime? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.Value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
    }
}

public sealed class SystemTimeProvider : ITimeProvider
{
    // BSON Date keeps milliseconds. The value written on create is the value a later read returns.
    public DateTime UtcNow
    {
        get
        {
            var now = DateTime.UtcNow;
            return new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);
        }
    }
}
