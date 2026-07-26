namespace PointOfSale.API.Configuration;

public class R2Options
{
    public string AccountId { get; set; } = string.Empty;
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;
    public string Bucket { get; set; } = string.Empty;
    public string ServiceUrl { get; set; } = string.Empty;
    public string PublicUrl { get; set; } = string.Empty;

    public string ResolvedServiceUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ServiceUrl))
            {
                return ServiceUrl.Trim().TrimEnd('/');
            }

            if (!string.IsNullOrWhiteSpace(AccountId))
            {
                return $"https://{AccountId.Trim()}.r2.cloudflarestorage.com";
            }

            if (Uri.TryCreate(PublicUrl?.Trim(), UriKind.Absolute, out var publicUri) &&
                publicUri.Host.EndsWith(".r2.cloudflarestorage.com", StringComparison.OrdinalIgnoreCase))
            {
                return $"{publicUri.Scheme}://{publicUri.Host}";
            }

            return string.Empty;
        }
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccessKeyId) &&
        !string.IsNullOrWhiteSpace(SecretAccessKey) &&
        !string.IsNullOrWhiteSpace(Bucket) &&
        !string.IsNullOrWhiteSpace(ResolvedServiceUrl);
}
