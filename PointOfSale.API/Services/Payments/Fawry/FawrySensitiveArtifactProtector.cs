using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;

namespace PointOfSale.API.Services.Payments.Fawry;

public interface IFawrySensitiveArtifactProtector
{
    string ProtectWebhookPayload(string rawPayload);

    string UnprotectWebhookPayload(string protectedPayload);

    string DigestMessageSignature(string signature);
}

public sealed class FawrySensitiveArtifactProtector : IFawrySensitiveArtifactProtector
{
    public const string WebhookPayloadPrefix = "fawry-webhook:v1:";
    public const string SignatureDigestPrefix = "sha256:";

    private const string WebhookPayloadPurpose = "PointOfSale.Fawry.WebhookPayload.v1";
    private const string MessageSignatureProperty = "messageSignature";

    private readonly IDataProtector _webhookPayloadProtector;

    public FawrySensitiveArtifactProtector(IDataProtectionProvider dataProtectionProvider)
    {
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        _webhookPayloadProtector = dataProtectionProvider.CreateProtector(WebhookPayloadPurpose);
    }

    public string ProtectWebhookPayload(string rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            throw new ArgumentException("Fawry webhook payload is required.", nameof(rawPayload));
        }

        var protectedEvidence = RemoveRawMessageSignatures(rawPayload);
        return WebhookPayloadPrefix + _webhookPayloadProtector.Protect(protectedEvidence);
    }

    public string UnprotectWebhookPayload(string protectedPayload)
    {
        if (string.IsNullOrWhiteSpace(protectedPayload) ||
            !protectedPayload.StartsWith(WebhookPayloadPrefix, StringComparison.Ordinal))
        {
            throw new CryptographicException(
                "Fawry webhook payload is not stored in the required protected envelope.");
        }

        var payload = _webhookPayloadProtector.Unprotect(protectedPayload[WebhookPayloadPrefix.Length..]);
        EnsureRawMessageSignatureAbsent(payload);
        return payload;
    }

    public string DigestMessageSignature(string signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            throw new ArgumentException("Fawry message signature is required.", nameof(signature));
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(signature));
        return SignatureDigestPrefix + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string RemoveRawMessageSignatures(string rawPayload)
    {
        JsonNode root;
        try
        {
            root = JsonNode.Parse(rawPayload)
                ?? throw new CryptographicException("Fawry webhook payload contains no recoverable JSON evidence.");
        }
        catch (JsonException exception)
        {
            throw new CryptographicException(
                "Fawry webhook payload cannot be protected without safely classifying embedded signature evidence.",
                exception);
        }

        if (root is not JsonObject)
        {
            throw new CryptographicException(
                "Fawry webhook payload is not a classifiable JSON object.");
        }

        RemoveRawMessageSignatures(root);
        var protectedEvidence = root.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        EnsureRawMessageSignatureAbsent(protectedEvidence);
        return protectedEvidence;
    }

    private static void RemoveRawMessageSignatures(JsonNode node)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToArray())
            {
                if (string.Equals(property.Key, MessageSignatureProperty, StringComparison.OrdinalIgnoreCase))
                {
                    jsonObject.Remove(property.Key);
                    continue;
                }

                if (property.Value is not null)
                {
                    RemoveRawMessageSignatures(property.Value);
                }
            }

            return;
        }

        if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                if (item is not null)
                {
                    RemoveRawMessageSignatures(item);
                }
            }
        }
    }

    private static void EnsureRawMessageSignatureAbsent(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        if (ContainsMessageSignature(document.RootElement))
        {
            throw new CryptographicException(
                "Fawry webhook evidence still contains a raw message-signature field.");
        }
    }

    private static bool ContainsMessageSignature(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, MessageSignatureProperty, StringComparison.OrdinalIgnoreCase) ||
                    ContainsMessageSignature(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (ContainsMessageSignature(item))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
