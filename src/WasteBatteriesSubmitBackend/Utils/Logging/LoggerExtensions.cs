namespace WasteBatteriesSubmitBackend.Utils.Logging;

internal static partial class LoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "Unhandled exception")]
    public static partial void UnhandledException(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Ensuring index is created if it does not exist for collection {CollectionName} in DB {DatabaseName}")]
    public static partial void EnsuringIndexes(
        this ILogger logger,
        string collectionName,
        string databaseName);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "Request validation failed: {ValidationErrors}")]
    public static partial void RequestValidationFailed(
        this ILogger logger,
        string validationErrors);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Information,
        Message = "Created POM submission {SubmissionId} for scheme {SchemeOrgId} member {MemberOrgId}")]
    public static partial void CreatedPomSubmission(
        this ILogger logger,
        string submissionId,
        string schemeOrgId,
        string memberOrgId);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Information,
        Message = "Amended POM submission {SubmissionId} superseding {SupersedesSubmissionId} for scheme {SchemeOrgId} member {MemberOrgId}")]
    public static partial void AmendedPomSubmission(
        this ILogger logger,
        string submissionId,
        string supersedesSubmissionId,
        string schemeOrgId,
        string memberOrgId);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Information,
        Message = "Upserted batch of {SubmissionCount} POM submissions ({CreatedCount} created, {AmendedCount} amended)")]
    public static partial void UpsertedPomSubmissionBatch(
        this ILogger logger,
        int submissionCount,
        int createdCount,
        int amendedCount);

    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Error,
        Message = "Failed to save POM submissions and outbox events")]
    public static partial void FailedToSavePomSubmissions(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Outbox relay is off")]
    public static partial void OutboxRelayOff(this ILogger logger);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Error,
        Message = "Outbox relay pass failed")]
    public static partial void OutboxRelayPassFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Publishing outbox event {EventId} {CorrelationId}")]
    public static partial void PublishingOutboxEvent(
        this ILogger logger,
        string eventId,
        string correlationId);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Information,
        Message = "Published outbox event {EventId} {CorrelationId}")]
    public static partial void PublishedOutboxEvent(
        this ILogger logger,
        string eventId,
        string correlationId);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Error,
        Message = "Failed to publish outbox event {EventId} {CorrelationId}")]
    public static partial void FailedToPublishOutboxEvent(
        this ILogger logger,
        Exception exception,
        string eventId,
        string correlationId);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Warning,
        Message = "Outbox event {EventId} was published but the claim was no longer held {CorrelationId}")]
    public static partial void OutboxClaimLostAfterPublish(
        this ILogger logger,
        string eventId,
        string correlationId);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Warning,
        Message = "Outbox event {EventId} failed to publish and the claim was no longer held {CorrelationId}")]
    public static partial void OutboxClaimLostAfterFailure(
        this ILogger logger,
        string eventId,
        string correlationId);

    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Information,
        Message = "Searching for {Name}, found {Result}")]
    public static partial void SearchingForExample(
        this ILogger logger,
        string name,
        object? result);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Searching for {ExampleId}, found {Result}")]
    public static partial void SearchingForExampleData(
        this ILogger logger,
        string exampleId,
        object? result);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Debug,
        Message = "Creating proxy http client")]
    public static partial void CreatingProxyHttpClient(
        this ILogger logger);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Warning,
        Message = "HTTP_PROXY is NOT set, proxy client will be disabled")]
    public static partial void ProxyNotConfigured(
        this ILogger logger);
}
