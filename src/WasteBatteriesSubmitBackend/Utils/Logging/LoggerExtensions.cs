namespace WasteBatteriesSubmitBackend.Utils.Logging;

internal static partial class LoggerExtensions
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Unhandled exception")]
    public static partial void UnhandledException(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 8, Level = LogLevel.Information, Message = "Request validation failed: {ValidationErrors}")]
    public static partial void RequestValidationFailed(this ILogger logger, string validationErrors);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Ensuring index is created if it does not exist for collection {CollectionName} in DB {DatabaseName}")]
    public static partial void EnsuringIndexes(this ILogger logger, string collectionName, string databaseName);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Searching for {Name}, found {Result}")]
    public static partial void SearchingForExample(this ILogger logger, string name, object? result);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Searching for {ExampleId}, found {Result}")]
    public static partial void SearchingForExampleData(this ILogger logger, string exampleId, object? result);

    [LoggerMessage(EventId = 5, Level = LogLevel.Debug, Message = "Creating proxy http client")]
    public static partial void CreatingProxyHttpClient(this ILogger logger);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "HTTP_PROXY is NOT set, proxy client will be disabled")]
    public static partial void ProxyNotConfigured(this ILogger logger);

    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Information,
        Message = "Created POM submission {SubmissionId} for scheme {SchemeOrgId} member {MemberOrgId}")]
    public static partial void CreatedPomSubmission(
        this ILogger logger,
        string submissionId,
        string schemeOrgId,
        string memberOrgId);
}
