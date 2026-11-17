using Microsoft.Extensions.Options;

namespace WasteBatteriesSubmitBackend.Outbox.Relay;

// Checked when the host starts. A relay that is on with a placeholder topic would retry every document forever.
internal sealed class OutboxRelayOptionsValidator : IValidateOptions<OutboxRelayOptions>
{
    public ValidateOptionsResult Validate(string? name, OutboxRelayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();
        RequireAtLeast(errors, options.PollIntervalSeconds, 1, "OutboxRelay:PollIntervalSeconds");
        RequireAtLeast(errors, options.BatchSize, 1, "OutboxRelay:BatchSize");
        RequireAtLeast(errors, options.RetentionDays, 1, "OutboxRelay:RetentionDays");
        RequireAtLeast(errors, options.RetryInitialDelaySeconds, 1, "OutboxRelay:RetryInitialDelaySeconds");
        RequireAtLeast(errors, options.RetryMaxDelaySeconds, 1, "OutboxRelay:RetryMaxDelaySeconds");
        if (options.RetryMaxDelaySeconds < options.RetryInitialDelaySeconds)
        {
            errors.Add("OutboxRelay:RetryMaxDelaySeconds must be at least OutboxRelay:RetryInitialDelaySeconds.");
        }

        if (double.IsNaN(options.RetryMultiplier) || double.IsInfinity(options.RetryMultiplier) || options.RetryMultiplier < 1)
        {
            errors.Add("OutboxRelay:RetryMultiplier must be at least 1.");
        }

        if (options.LeaseSeconds <= OutboxRelayOptions.PublishTimeoutSeconds)
        {
            errors.Add(
                $"OutboxRelay:LeaseSeconds must be greater than {OutboxRelayOptions.PublishTimeoutSeconds} so the lease outlasts the publish timeout.");
        }

        if (options.Enabled)
        {
            if (string.IsNullOrWhiteSpace(options.TopicArn)
                || !options.TopicArn.StartsWith("arn:aws:sns:", StringComparison.Ordinal))
            {
                errors.Add("OutboxRelay:TopicArn must be an SNS topic ARN when the relay is on.");
            }

            if (!string.IsNullOrWhiteSpace(options.ServiceUrl) && !IsHttpUrl(options.ServiceUrl))
            {
                errors.Add("OutboxRelay:ServiceUrl must be an absolute http or https URL when it is set.");
            }
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

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
