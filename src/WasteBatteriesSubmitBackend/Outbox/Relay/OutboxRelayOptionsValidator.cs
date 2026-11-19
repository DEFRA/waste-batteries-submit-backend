using Microsoft.Extensions.Options;

namespace WasteBatteriesSubmitBackend.Outbox.Relay;

// Checked when the host starts. A relay that is on with a placeholder topic would retry every document forever.
internal sealed class OutboxRelayOptionsValidator(IConfiguration configuration, string sectionName)
    : IValidateOptions<OutboxRelayOptions>
{
    public ValidateOptionsResult Validate(string? name, OutboxRelayOptions options)
    {
        if (!string.Equals(name, sectionName, StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Skip;
        }

        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();
        RequireAtLeast(errors, options.PollIntervalSeconds, 1, Setting("PollIntervalSeconds"));
        RequireAtLeast(errors, options.BatchSize, 1, Setting("BatchSize"));
        RequireAtLeast(errors, options.RetentionDays, 1, Setting("RetentionDays"));
        RequireAtLeast(errors, options.RetryInitialDelaySeconds, 1, Setting("RetryInitialDelaySeconds"));
        RequireAtLeast(errors, options.RetryMaxDelaySeconds, 1, Setting("RetryMaxDelaySeconds"));
        if (options.RetryMaxDelaySeconds < options.RetryInitialDelaySeconds)
        {
            errors.Add($"{Setting("RetryMaxDelaySeconds")} must be at least {Setting("RetryInitialDelaySeconds")}.");
        }

        if (double.IsNaN(options.RetryMultiplier) || double.IsInfinity(options.RetryMultiplier) || options.RetryMultiplier < 1)
        {
            errors.Add($"{Setting("RetryMultiplier")} must be at least 1.");
        }

        if (options.LeaseSeconds <= OutboxRelayDefaults.PublishTimeoutSeconds)
        {
            errors.Add(
                $"{Setting("LeaseSeconds")} must be greater than {OutboxRelayDefaults.PublishTimeoutSeconds} so the lease outlasts the publish timeout.");
        }

        if (options.Enabled)
        {
            AddEnabledErrors(errors, options);
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private void AddEnabledErrors(List<string> errors, OutboxRelayOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.TopicArn)
            || !options.TopicArn.StartsWith("arn:aws:sns:", StringComparison.Ordinal))
        {
            errors.Add($"{Setting("TopicArn")} must be an SNS topic ARN when the relay is on.");
        }

        if (options.EventTypes is null
            || options.EventTypes.Length == 0
            || options.EventTypes.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add($"{Setting("EventTypes")} must list the event types this relay publishes.");
        }

        if (!string.IsNullOrWhiteSpace(options.ServiceUrl) && !IsHttpUrl(options.ServiceUrl))
        {
            errors.Add($"{Setting("ServiceUrl")} must be an absolute http or https URL when it is set.");
        }
    }

    // A key set on the relay names that section. Otherwise it names the shared defaults.
    private string Setting(string key)
    {
        if (Has(sectionName, key))
        {
            return $"{sectionName}:{key}";
        }

        if (Has(OutboxRelayDefaults.SectionName, key) || !IsRelaySetting(key))
        {
            return $"{OutboxRelayDefaults.SectionName}:{key}";
        }

        return $"{sectionName}:{key}";
    }

    private bool Has(string section, string key)
    {
        var bound = configuration.GetSection(section);
        return bound[key] is not null || bound.GetSection(key).Exists();
    }

    private static bool IsRelaySetting(string key) =>
        key is "Enabled" or "TopicArn" or "ServiceUrl" or "EventTypes";

    private static void RequireAtLeast(List<string> errors, int value, int minimum, string name)
    {
        if (value < minimum)
        {
            errors.Add($"{name} must be at least {minimum}.");
        }
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
