using MongoDB.Bson;
using WasteBatteriesSubmitBackend.Outbox;

namespace WasteBatteriesSubmitBackend.PomSubmission.Outbox;

// One event per scheme and compliance year. Ids and the timestamp are fixed by the caller, before the transaction.
public static class PomSubmissionOutboxEvents
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
        var version = OutboxEvents.CommitVersion(occurredAtUtc);

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
            events.Add(new PomSubmissionOutboxEvent
            {
                Id = OutboxEvents.NewId(),
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

    private static string EventType(bool created, bool amended) =>
        created && amended
            ? PomSubmissionOutboxEvent.PomSubmissionSavedType
            : amended
                ? PomSubmissionOutboxEvent.PomSubmissionAmendedType
                : PomSubmissionOutboxEvent.PomSubmissionCreatedType;
}
