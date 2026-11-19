namespace WasteBatteriesSubmitBackend.PomSubmission.Outbox;

// The relay section for POM submission events. Another event family adds its own section.
public static class PomSubmissionOutboxRelay
{
    public const string SectionName = "PomSubmissionOutboxRelay";
}
