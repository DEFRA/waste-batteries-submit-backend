using System.Diagnostics.CodeAnalysis;

namespace WasteBatteriesSubmitBackend.Utils.Auditing;

[ExcludeFromCodeCoverage]
public static class AuditLoggingExtension
{
    private static readonly Dictionary<string, object> s_auditLogLevel = new()
    {
        [AuditLogger.AuditPropertyName] = true
    };

    [SuppressMessage(
        "Usage",
        "CA2254:Template should be a static expression",
        Justification = "Callers pass a constant message template.")]
    public static void Audit(this Microsoft.Extensions.Logging.ILogger logger,
        string message,
        params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(logger);
        using (logger.BeginScope(s_auditLogLevel))
        {
            logger.LogInformation(message, args);
        }
    }

    [SuppressMessage(
        "Usage",
        "CA2254:Template should be a static expression",
        Justification = "Callers pass a constant message template.")]
    public static void Audit(this Microsoft.Extensions.Logging.ILogger logger,
        Exception exception,
        string message,
        params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(logger);
        using (logger.BeginScope(s_auditLogLevel))
        {
            logger.LogInformation(exception, message, args);
        }
    }
}