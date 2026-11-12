namespace WasteBatteriesSubmitBackend.Outbox;

// Shared rules for building an event. A feature decides which events a save writes.
public static class OutboxEvents
{
    // Chosen before the transaction. The driver can run the callback more than once and must write the same id.
    public static string NewId() => Guid.NewGuid().ToString("D");

    // Milliseconds do not fit in a 32-bit int. Unix seconds would collide inside one second.
    public static long CommitVersion(DateTime occurredAtUtc)
    {
        if (occurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The event time must be UTC.", nameof(occurredAtUtc));
        }

        return new DateTimeOffset(occurredAtUtc).ToUnixTimeMilliseconds();
    }
}
