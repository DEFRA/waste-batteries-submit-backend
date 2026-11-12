using MongoDB.Bson;

namespace WasteBatteriesSubmitBackend.Outbox;

// Builds the events for one save. Ids and the timestamp are fixed here, before the transaction,
// because the driver can run the transaction callback more than once and must write the same documents.
public static class OutboxEvents
{
    public static IReadOnlyList<OutboxEvent> ForSchemeYears(
        IEnumerable<(string SchemeOrgId, int CompliancePeriodYear, bool Amended)> saved,
        DateTime occurredAtUtc,
        string correlationId,
        string actorId)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        var version = CommitVersion(occurredAtUtc);

        var order = new List<(string SchemeId, int Year)>();
        var groups = new Dictionary<(string SchemeId, int Year), (bool Created, bool Amended)>();
        foreach (var (schemeOrgId, year, amended) in saved)
        {
            // ObjectId text is canonical, so a different hex case is the same scheme.
            var schemeId = ObjectId.Parse(schemeOrgId).ToString();
            var key = (schemeId, year);
            if (!groups.TryGetValue(key, out var flags))
            {
                order.Add(key);
                flags = default;
            }

            groups[key] = (flags.Created || !amended, flags.Amended || amended);
        }

        var events = new List<OutboxEvent>(order.Count);
        foreach (var key in order)
        {
            var (Created, Amended) = groups[key];
            events.Add(new OutboxEvent
            {
                Id = Guid.NewGuid().ToString("D"),
                EventType = EventType(Created, Amended),
                SchemaVersion = OutboxEvent.CurrentSchemaVersion,
                SubjectId = SubjectId(key.SchemeId, key.Year),
                Version = version,
                PartitionKey = key.SchemeId,
                OccurredAtUtc = occurredAtUtc,
                ActorId = actorId,
                CorrelationId = correlationId,
                Sent = false,
                SchemeOrgId = key.SchemeId,
                CompliancePeriodYear = key.Year
            });
        }

        return events;
    }

    // What changed. One event covers every submission in that scheme and year.
    public static string SubjectId(string schemeOrgId, int compliancePeriodYear) =>
        $"{schemeOrgId}:{compliancePeriodYear}";

    // Milliseconds do not fit in a 32-bit int. Unix seconds would collide inside one second.
    public static long CommitVersion(DateTime occurredAtUtc)
    {
        if (occurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The event time must be UTC.", nameof(occurredAtUtc));
        }

        return new DateTimeOffset(occurredAtUtc).ToUnixTimeMilliseconds();
    }

    private static string EventType(bool created, bool amended)
    {
        if (created && amended)
        {
            return OutboxEvent.PomSubmissionSavedType;
        }

        if (amended)
        {
            return OutboxEvent.PomSubmissionAmendedType;
        }

        return OutboxEvent.PomSubmissionCreatedType;
    }
}
