namespace PointOfSale.Infrastructure.Analytics.Delivery;

public sealed class ProviderDeliveryRetryEngine : IProviderDeliveryRetryEngine
{
    public ProviderDeliveryRetryDecision Classify(
        ProviderDeliverySendResult result,
        int nextAttemptNumber,
        ProviderDeliveryOptions options,
        DateTime nowUtc)
    {
        if (result.Success)
        {
            return new ProviderDeliveryRetryDecision(false, false, true, "success", null, "delivered");
        }

        var statusCode = result.StatusCode;
        var failureClass = ClassifyFailure(statusCode, result.ErrorMessage);
        var replayEligible = !string.Equals(failureClass, "payload_rejected", StringComparison.Ordinal);
        var retryable = IsRetryable(statusCode, failureClass);

        if (!retryable || nextAttemptNumber >= options.MaxAttempts)
        {
            return new ProviderDeliveryRetryDecision(
                false,
                true,
                replayEligible,
                failureClass,
                null,
                nextAttemptNumber >= options.MaxAttempts ? "max_attempts_exceeded" : "terminal_failure");
        }

        var delay = result.RetryAfter ?? ComputeBackoff(nextAttemptNumber, options);
        var maxDelay = TimeSpan.FromMinutes(options.MaxRetryDelayMinutes);
        if (delay > maxDelay)
        {
            delay = maxDelay;
        }

        return new ProviderDeliveryRetryDecision(
            true,
            false,
            true,
            failureClass,
            nowUtc.Add(delay),
            result.RetryAfter.HasValue ? "retry_after" : "exponential_backoff");
    }

    private static bool IsRetryable(int? statusCode, string failureClass)
    {
        if (statusCode is null)
        {
            return true;
        }

        return statusCode == 408 ||
               statusCode == 409 ||
               statusCode == 425 ||
               statusCode == 429 ||
               statusCode >= 500 ||
               string.Equals(failureClass, "network_error", StringComparison.Ordinal);
    }

    private static string ClassifyFailure(int? statusCode, string? errorMessage)
    {
        if (statusCode is null)
        {
            return "network_error";
        }

        if (statusCode is 401 or 403)
        {
            return "credential_rejected";
        }

        if (statusCode == 429)
        {
            return "rate_limited";
        }

        if (statusCode == 408 || statusCode >= 500)
        {
            return "provider_transient";
        }

        if (statusCode >= 400)
        {
            return "payload_rejected";
        }

        return string.IsNullOrWhiteSpace(errorMessage) ? "unknown" : "provider_failure";
    }

    private static TimeSpan ComputeBackoff(int nextAttemptNumber, ProviderDeliveryOptions options)
    {
        var exponent = Math.Min(nextAttemptNumber - 1, 10);
        var baseDelaySeconds = options.BaseRetryDelaySeconds * Math.Pow(2, exponent);
        var jitterRatio = options.JitterPercent / 100d;
        var jitter = baseDelaySeconds * jitterRatio * Random.Shared.NextDouble();
        return TimeSpan.FromSeconds(baseDelaySeconds + jitter);
    }
}
