using MongoDB.Bson;
using WasteBatteriesSubmitBackend.Outbox;
using WasteBatteriesSubmitBackend.Tests.Common;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Outbox;

public class OutboxEventsTests
{
    private const string ActorId = "actor-1";

    private static readonly DateTime s_occurredAt = SamplePomSubmission.CreatedAt;

    [Fact]
    public void ForSchemeYears_WhenTheSameSchemeAndYearRepeat_WritesOneEventInFirstSeenOrder()
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

        var events = OutboxEvents.ForSchemeYears(saved, s_occurredAt, "correlation-1", ActorId);

        Assert.Equal(3, events.Count);
        AssertEvent(events[0], SamplePomSubmission.SchemeOrgId, SamplePomSubmission.CompliancePeriodYear, OutboxEvent.PomSubmissionCreatedType);
        AssertEvent(events[1], SamplePomSubmission.SchemeOrgId, 2025, OutboxEvent.PomSubmissionCreatedType);
        AssertEvent(events[2], otherSchemeOrgId, SamplePomSubmission.CompliancePeriodYear, OutboxEvent.PomSubmissionCreatedType);
        Assert.Equal(3, events.Select(outboxEvent => outboxEvent.Id).Distinct().Count());
    }

    [Fact]
    public void ForSchemeYears_WhenASchemeYearIsOnlyAmended_UsesTheAmendedType()
    {
        var saved = new (string, int, bool)[]
        {
            (SamplePomSubmission.SchemeOrgId, SamplePomSubmission.CompliancePeriodYear, true)
        };

        var events = OutboxEvents.ForSchemeYears(saved, s_occurredAt, "correlation-1", ActorId);

        var outboxEvent = Assert.Single(events);
        Assert.Equal(OutboxEvent.PomSubmissionAmendedType, outboxEvent.EventType);
    }

    [Fact]
    public void ForSchemeYears_WhenASchemeYearIsCreatedAndAmended_UsesTheSavedType()
    {
        var saved = new (string, int, bool)[]
        {
            (SamplePomSubmission.SchemeOrgId, SamplePomSubmission.CompliancePeriodYear, false),
            (SamplePomSubmission.SchemeOrgId, SamplePomSubmission.CompliancePeriodYear, true),
            (SamplePomSubmission.SchemeOrgId, 2025, true)
        };

        var events = OutboxEvents.ForSchemeYears(saved, s_occurredAt, "correlation-1", ActorId);

        Assert.Equal(2, events.Count);
        Assert.Equal(OutboxEvent.PomSubmissionSavedType, events[0].EventType);
        Assert.Equal(OutboxEvent.PomSubmissionAmendedType, events[1].EventType);
        Assert.Equal(
            OutboxEvents.SubjectId(SamplePomSubmission.SchemeOrgId, SamplePomSubmission.CompliancePeriodYear),
            events[0].SubjectId);
    }

    [Fact]
    public void ForSchemeYears_WhenNothingWasSaved_ReturnsNoEvents()
    {
        var events = OutboxEvents.ForSchemeYears([], s_occurredAt, "correlation-1", ActorId);

        Assert.Empty(events);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ForSchemeYears_WhenTheCorrelationIdIsMissing_Throws(string? correlationId)
    {
        var saved = new (string, int, bool)[] { (SamplePomSubmission.SchemeOrgId, SamplePomSubmission.CompliancePeriodYear, false) };

        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            OutboxEvents.ForSchemeYears(saved, s_occurredAt, correlationId!, ActorId));

        Assert.Equal("correlationId", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ForSchemeYears_WhenTheActorIdIsMissing_Throws(string? actorId)
    {
        var saved = new (string, int, bool)[] { (SamplePomSubmission.SchemeOrgId, SamplePomSubmission.CompliancePeriodYear, false) };

        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            OutboxEvents.ForSchemeYears(saved, s_occurredAt, "correlation-1", actorId!));

        Assert.Equal("actorId", exception.ParamName);
    }

    [Fact]
    public void CommitVersion_WhenTheTimeIsNotUtc_Throws()
    {
        var unspecified = DateTime.SpecifyKind(s_occurredAt, DateTimeKind.Unspecified);

        var exception = Assert.Throws<ArgumentException>(() => OutboxEvents.CommitVersion(unspecified));

        Assert.Equal("occurredAtUtc", exception.ParamName);
    }

    private static void AssertEvent(OutboxEvent outboxEvent, string schemeOrgId, int year, string eventType)
    {
        Assert.True(Guid.TryParse(outboxEvent.Id, out _));
        Assert.False(ObjectId.TryParse(outboxEvent.Id, out _));
        Assert.Equal(eventType, outboxEvent.EventType);
        Assert.Equal(OutboxEvent.CurrentSchemaVersion, outboxEvent.SchemaVersion);
        Assert.Equal(OutboxEvents.SubjectId(schemeOrgId, year), outboxEvent.SubjectId);
        Assert.Equal(OutboxEvents.CommitVersion(s_occurredAt), outboxEvent.Version);
        Assert.Equal(schemeOrgId, outboxEvent.PartitionKey);
        Assert.Equal(schemeOrgId, outboxEvent.SchemeOrgId);
        Assert.Equal(year, outboxEvent.CompliancePeriodYear);
        Assert.Equal(s_occurredAt, outboxEvent.OccurredAtUtc);
        Assert.Equal(ActorId, outboxEvent.ActorId);
        Assert.Equal("correlation-1", outboxEvent.CorrelationId);
        Assert.False(outboxEvent.Sent);
        Assert.Null(outboxEvent.SentAtUtc);
        Assert.Null(outboxEvent.ExpiresAtUtc);
    }
}
