using System.Buffers;
using System.Text;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.API.Security;

public static class WebhookRequestLimits
{
    public const long PaymentProviderMaxBytes = 64 * 1024;
    public const long ShippingProviderMaxBytes = 32 * 1024;
}

public sealed class WebhookRequestRejectedException : Exception
{
    public int StatusCode { get; }
    public string ErrorCode { get; }

    public WebhookRequestRejectedException(int statusCode, string errorCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public ApiErrorResponse ToApiError(string traceId) =>
        new(ErrorCode, Message, traceId);
}

public static class WebhookRequestBodyReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static async Task<string> ReadJsonBodyAsync(
        HttpContext httpContext,
        string provider,
        long maxBytes,
        string webhookKind,
        CancellationToken cancellationToken = default)
    {
        var request = httpContext.Request;
        if (!IsApplicationJson(request.ContentType))
        {
            RecordPayloadRejected(httpContext, provider, "invalid_content_type", webhookKind);
            throw new WebhookRequestRejectedException(
                StatusCodes.Status415UnsupportedMediaType,
                "WEBHOOK_UNSUPPORTED_CONTENT_TYPE",
                "Webhook content type must be application/json.");
        }

        if (request.ContentLength > maxBytes)
        {
            RecordSizeLimitExceeded(httpContext, provider, webhookKind);
            RecordPayloadRejected(httpContext, provider, "payload_too_large", webhookKind);
            throw PayloadTooLarge();
        }

        var buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            await using var memory = new MemoryStream(
                request.ContentLength is > 0 and <= int.MaxValue
                    ? (int)request.ContentLength.Value
                    : 0);

            long totalBytes = 0;
            while (true)
            {
                var bytesRead = await request.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                totalBytes += bytesRead;
                if (totalBytes > maxBytes)
                {
                    RecordSizeLimitExceeded(httpContext, provider, webhookKind);
                    RecordPayloadRejected(httpContext, provider, "payload_too_large", webhookKind);
                    throw PayloadTooLarge();
                }

                await memory.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            }

            if (totalBytes == 0)
            {
                RecordPayloadRejected(httpContext, provider, "empty_payload", webhookKind);
                throw new WebhookRequestRejectedException(
                    StatusCodes.Status400BadRequest,
                    "WEBHOOK_EMPTY_PAYLOAD",
                    "Webhook payload is required.");
            }

            var bodyBytes = memory.ToArray();
            request.Body = new MemoryStream(bodyBytes);
            var payload = StrictUtf8.GetString(bodyBytes);

            if (string.IsNullOrWhiteSpace(payload))
            {
                RecordPayloadRejected(httpContext, provider, "empty_payload", webhookKind);
                throw new WebhookRequestRejectedException(
                    StatusCodes.Status400BadRequest,
                    "WEBHOOK_EMPTY_PAYLOAD",
                    "Webhook payload is required.");
            }

            return payload;
        }
        catch (DecoderFallbackException)
        {
            RecordPayloadRejected(httpContext, provider, "invalid_utf8", webhookKind);
            throw new WebhookRequestRejectedException(
                StatusCodes.Status400BadRequest,
                "WEBHOOK_INVALID_PAYLOAD",
                "Webhook payload must be valid UTF-8 JSON.");
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            RecordSizeLimitExceeded(httpContext, provider, webhookKind);
            RecordPayloadRejected(httpContext, provider, "payload_too_large", webhookKind);
            throw PayloadTooLarge();
        }
        catch (IOException)
        {
            RecordSizeLimitExceeded(httpContext, provider, webhookKind);
            RecordPayloadRejected(httpContext, provider, "payload_too_large", webhookKind);
            throw PayloadTooLarge();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static void RecordInvalidSignature(
        HttpContext httpContext,
        string provider,
        string webhookKind,
        string reason = "invalid_signature")
    {
        ResolveTelemetry(httpContext)
            ?.RecordWebhookInvalidSignature(provider, reason, webhookKind);
    }

    public static void RecordPayloadRejected(
        HttpContext httpContext,
        string provider,
        string reason,
        string webhookKind)
    {
        ResolveTelemetry(httpContext)
            ?.RecordWebhookPayloadRejected(provider, reason, webhookKind);
    }

    public static void RecordSizeLimitExceeded(
        HttpContext httpContext,
        string provider,
        string webhookKind)
    {
        ResolveTelemetry(httpContext)
            ?.RecordWebhookSizeLimitExceeded(provider, webhookKind);
    }

    private static CommerceReliabilityTelemetry? ResolveTelemetry(HttpContext httpContext) =>
        httpContext.RequestServices?.GetService<CommerceReliabilityTelemetry>();

    private static bool IsApplicationJson(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        var mediaType = contentType.Split(';', 2)[0].Trim();
        return string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase);
    }

    private static WebhookRequestRejectedException PayloadTooLarge() =>
        new(
            StatusCodes.Status413PayloadTooLarge,
            "WEBHOOK_PAYLOAD_TOO_LARGE",
            "Webhook payload exceeds the configured size limit.");
}
