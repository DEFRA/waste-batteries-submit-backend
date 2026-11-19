using MongoDB.Bson;
using WasteBatteriesSubmitBackend.Outbox;

namespace WasteBatteriesSubmitBackend.PomSubmission.Outbox;

// One event per scheme and compliance year. Ids and the timestamp are fixed by the caller, before the transaction.
public static class PomSubmissionOutboxEvents
{
    // The shape of this event, for this feature. A new field or meaning is a code change, so this is not configuration.
    public const int CurrentSchemaVersion = 1;

    public static IReadOnlyList<OutboxEvent> GetOutboxEventsBySchemeYears(
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
            events.Add(new PomSubmissionOutboxEvent
            {
                Id = OutboxEvents.NewId(),
                EventType = GetEventType(groups[key].Created, groups[key].Amended),
                SchemaVersion = CurrentSchemaVersion,
                SubjectId = GetSubjectIdForSchemeYear(key.SchemeId, key.Year),
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
    private static string GetSubjectIdForSchemeYear(string schemeOrgId, int compliancePeriodYear) =>
        $"{schemeOrgId}:{compliancePeriodYear}";

    private static string GetEventType(bool created, bool amended)
    {
        if (created && amended)
        {
            return PomSubmissionOutboxEvent.PomSubmissionSavedType;
        }

        if (amended)
        {
            return PomSubmissionOutboxEvent.PomSubmissionAmendedType;
        }

        return PomSubmissionOutboxEvent.PomSubmissionCreatedType;
    }
}
