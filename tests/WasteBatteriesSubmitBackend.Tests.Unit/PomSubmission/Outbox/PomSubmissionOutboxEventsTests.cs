using MongoDB.Bson;
using WasteBatteriesSubmitBackend.Outbox;
using WasteBatteriesSubmitBackend.PomSubmission.Outbox;
using WasteBatteriesSubmitBackend.Tests.Common;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Outbox;

public class PomSubmissionOutboxEventsTests
{
    private const string ActorId = "actor-1";

    private static readonly DateTime s_occurredAt = SamplePomSubmission.CreatedAt;

    [Fact]
    public void GetOutboxEventsBySchemeYears_WhenTheSameSchemeAndYearRepeat_WritesOneEventInFirstSeenOrder()
    {
        const string otherSchemeOrgId = "674a1b2c3d4e5f6789012349";
        var saved = new (string SchemeOrgId, int CompliancePeriodYear, bool Amended)[]
        {
            (SamplePomSubmission.SchemeOrgId.ToUpperInvariant(), SamplePomSubmission.CompliancePeriodYear, false),
            (SamplePomSubmission.SchemeOrgId, SamplePomSubmission.CompliancePeriodYear, false),
            (SamplePomSubmission.SchemeOrgId, 2025, false),
            (otherSchemeOrgId, SamplePomSubmission.CompliancePeriodYear, false),
            (otherSchemeOrgId.ToUpperInvariant(), SamplePomSubmission.CompliancePeriodYear, false)
        };

        var events = PomSubmissionOutboxEvents.GetOutboxEventsBySchemeYears(saved, s_occurredAt, "correlation-1", ActorId);

        Assert.Equal(3, events.Count);
        AssertEvent(events[0], SamplePomSubmission.SchemeOrgId, SamplePomSubmission.CompliancePeriodYear, PomSubmissionOutboxEvent.PomSubmissionCreatedType);
        AssertEvent(events[1], SamplePomSubmission.SchemeOrgId, 2025, PomSubmissionOutboxEvent.PomSubmissionCreatedType);
        AssertEvent(events[2], otherSchemeOrgId, SamplePomSubmission.CompliancePeriodYear, PomSubmissionOutboxEvent.PomSubmissionCreatedType);
        Assert.Equal(3, events.Select(outboxEvent => outboxEvent.Id).Distinct().Count());
    }

    [Fact]
    public void GetOutboxEventsBySchemeYears_WhenASchemeYearIsOnlyAmended_UsesTheAmendedType()
    {
        var saved = new (string, int, bool)[]
        {
            (SamplePomSubmission.SchemeOrgId, SamplePomSubmission.CompliancePeriodYear, true)
        };

        var events = PomSubmissionOutboxEvents.GetOutboxEventsBySchemeYears(saved, s_occurredAt, "correlation-1", ActorId);

        var outboxEvent = Assert.IsType<PomSubmissionOutboxEvent>(Assert.Single(events));
        Assert.Equal(PomSubmissionOutboxEvent.PomSubmissionAmendedType, outboxEvent.EventType);
    }

    [Fact]
    public void GetOutboxEventsBySchemeYears_WhenASchemeYearIsCreatedAndAmended_UsesTheSavedType()
    {
        var saved = new (string, int, bool)[]
        {
            (SamplePomSubmission.SchemeOrgId, SamplePomSubmission.CompliancePeriodYear, false),
            (SamplePomSubmission.SchemeOrgId, SamplePomSubmission.CompliancePeriodYear, true),
            (SamplePomSubmission.SchemeOrgId, 2025, true)
        };

        var events = PomSubmissionOutboxEvents.GetOutboxEventsBySchemeYears(saved, s_occurredAt, "correlation-1", ActorId);

        Assert.Equal(2, events.Count);
        Assert.Equal(PomSubmissionOutboxEvent.PomSubmissionSavedType, events[0].EventType);
        Assert.Equal(PomSubmissionOutboxEvent.PomSubmissionAmendedType, events[1].EventType);
        Assert.Equal(
            SubjectId(SamplePomSubmission.SchemeOrgId, SamplePomSubmission.CompliancePeriodYear),
            events[0].SubjectId);
        Assert.Equal(SubjectId(SamplePomSubmission.SchemeOrgId, 2025), events[1].SubjectId);
    }

    [Fact]
    public void GetOutboxEventsBySchemeYears_WhenNothingWasSaved_ReturnsNoEvents()
    {
        var events = PomSubmissionOutboxEvents.GetOutboxEventsBySchemeYears([], s_occurredAt, "correlation-1", ActorId);

        Assert.Empty(events);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetOutboxEventsBySchemeYears_WhenTheCorrelationIdIsMissing_Throws(string? correlationId)
    {
        var saved = new (string, int, bool)[] { (SamplePomSubmission.SchemeOrgId, SamplePomSubmission.CompliancePeriodYear, false) };

        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            PomSubmissionOutboxEvents.GetOutboxEventsBySchemeYears(saved, s_occurredAt, correlationId!, ActorId));

        Assert.Equal("correlationId", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetOutboxEventsBySchemeYears_WhenTheActorIdIsMissing_Throws(string? actorId)
    {
        var saved = new (string, int, bool)[] { (SamplePomSubmission.SchemeOrgId, SamplePomSubmission.CompliancePeriodYear, false) };

        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            PomSubmissionOutboxEvents.GetOutboxEventsBySchemeYears(saved, s_occurredAt, "correlation-1", actorId!));

        Assert.Equal("actorId", exception.ParamName);
    }

    // The scheme id and year, joined. Hex case is already canonical on the event.
    private static string SubjectId(string schemeOrgId, int year) => $"{schemeOrgId}:{year}";

    private static void AssertEvent(OutboxEvent outboxEvent, string schemeOrgId, int year, string eventType)
    {
        var submissionEvent = Assert.IsType<PomSubmissionOutboxEvent>(outboxEvent);
        Assert.True(Guid.TryParse(submissionEvent.Id, out _));
        Assert.False(ObjectId.TryParse(submissionEvent.Id, out _));
        Assert.Equal(eventType, submissionEvent.EventType);
        Assert.Equal(PomSubmissionOutboxEvents.CurrentSchemaVersion, submissionEvent.SchemaVersion);
        Assert.Equal(SubjectId(schemeOrgId, year), submissionEvent.SubjectId);
        Assert.Equal(OutboxEvents.CommitVersion(s_occurredAt), submissionEvent.Version);
        Assert.Equal(schemeOrgId, submissionEvent.PartitionKey);
        Assert.Equal(schemeOrgId, submissionEvent.SchemeOrgId);
        Assert.Equal(year, submissionEvent.CompliancePeriodYear);
        Assert.Equal(s_occurredAt, submissionEvent.OccurredAtUtc);
        Assert.Equal(ActorId, submissionEvent.ActorId);
        Assert.Equal("correlation-1", submissionEvent.CorrelationId);
        Assert.False(submissionEvent.Sent);
        Assert.Null(submissionEvent.SentAtUtc);
        Assert.Null(submissionEvent.ExpiresAtUtc);
        Assert.Empty(submissionEvent.AdditionalFields);
    }
}
