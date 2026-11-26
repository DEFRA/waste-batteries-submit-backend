using System.Security.Claims;
using System.Text.Json;

namespace WasteBatteriesSubmitBackend.Authentication;

// An STS web identity token names the caller in sub and in the principal tag.
// sub is arn:aws:iam::<account>:role/<service>. The tag Service is the service name.
internal static class ServiceCaller
{
    internal const string StsClaim = "https://sts.amazonaws.com/";

    internal static bool IsAllowed(ClaimsIdentity identity, string allowedService)
    {
        if (string.IsNullOrWhiteSpace(allowedService))
        {
            return false;
        }

        foreach (var claim in identity.Claims)
        {
            if (claim.Type == "sub" && SubjectMatches(claim.Value, allowedService))
            {
                return true;
            }

            if (claim.Type == StsClaim && StsMatches(claim.Value, allowedService))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SubjectMatches(string subject, string allowedService) =>
        string.Equals(subject, allowedService, StringComparison.Ordinal)
        || subject.EndsWith($":role/{allowedService}", StringComparison.Ordinal);

    private static bool StsMatches(string json, string allowedService)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (document.RootElement.TryGetProperty("principal_tags", out var tags)
                && tags.ValueKind == JsonValueKind.Object
                && tags.TryGetProperty("Service", out var service)
                && service.ValueKind == JsonValueKind.String
                && string.Equals(service.GetString(), allowedService, StringComparison.Ordinal))
            {
                return true;
            }

            return document.RootElement.TryGetProperty("principal_id", out var principalId)
                && principalId.ValueKind == JsonValueKind.String
                && SubjectMatches(principalId.GetString() ?? string.Empty, allowedService);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
