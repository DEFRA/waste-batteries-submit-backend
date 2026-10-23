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
