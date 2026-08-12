using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using System.Net;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;

namespace PointOfSale.API.Services;

public interface IR2StorageService
{
    Task<R2UploadUrlResult> CreateUploadUrlAsync(
        string contentType,
        string path,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default);

    Task<string> CreateDownloadUrlAsync(
        string objectKey,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default);

    Task<R2ObjectMetadata> GetObjectMetadataAsync(
        string objectKey,
        CancellationToken cancellationToken = default);

    Task<byte[]> GetObjectBytesAsync(
        string objectKey,
        int maxBytes,
        CancellationToken cancellationToken = default);

    Task<byte[]> GetObjectBytesAsync(
        string objectKey,
        int maxBytes,
        string? ifMatchETag,
        CancellationToken cancellationToken = default);

    Task DeleteObjectAsync(
        string objectKey,
        CancellationToken cancellationToken = default);

    Task<R2ObjectMetadata> CopyObjectAsync(
        string sourceObjectKey,
        string destinationObjectKey,
        string contentType,
        string? sourceIfMatchETag,
        CancellationToken cancellationToken = default);

    Task<R2DownloadObjectResult> DownloadObjectAsync(
        string objectKey,
        string? ifMatchETag,
        CancellationToken cancellationToken = default);

    Task<R2UploadResult> UploadFileAsync(
        Stream file,
        string contentType,
        string path,
        CancellationToken cancellationToken = default);
}

public sealed record R2UploadResult(string? Url, string FileName, string ObjectKey);
public sealed record R2UploadUrlResult(
    string UploadUrl,
    string FileName,
    string ObjectKey,
    string HttpMethod,
    string ContentType);
public sealed record R2ObjectMetadata(string ContentType, long ContentLength, string? ChecksumSha256, string? ETag);
public sealed record R2DownloadObjectResult(Stream Content, string ContentType, long ContentLength, string? ETag);

public class R2StorageService : IR2StorageService, IDisposable
{
    private static readonly IReadOnlyDictionary<string, string> ContentTypeExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = ".jpg",
            ["image/png"] = ".png",
            ["image/webp"] = ".webp",
            ["image/gif"] = ".gif",
            ["application/pdf"] = ".pdf",
            ["application/msword"] = ".doc",
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = ".docx",
            ["video/mp4"] = ".mp4"
        };

    private readonly R2Options _options;
    private readonly ILogger<R2StorageService> _logger;
    private IAmazonS3? _s3Client;
    private readonly object _clientLock = new();

    public R2StorageService(
        IOptions<R2Options> options,
        ILogger<R2StorageService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<R2UploadResult> UploadFileAsync(
        Stream file,
        string contentType,
        string path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureConfigured();

            if (!ContentTypeExtensions.TryGetValue(NormalizeContentType(contentType), out var extension))
            {
                throw new ArgumentException($"Unsupported content type '{contentType}'.", nameof(contentType));
            }

            var fileName = $"{Guid.NewGuid():N}{extension}";
            var objectKey = $"{SanitizePath(path)}/{fileName}";

            var request = new PutObjectRequest
            {
                BucketName = _options.Bucket,
                Key = objectKey,
                InputStream = file,
                ContentType = NormalizeContentType(contentType),
                AutoCloseStream = false,
                DisablePayloadSigning = true,
                DisableMD5Stream = true,
                UseChunkEncoding = false
            };

            var s3Client = GetOrCreateClient();
            await s3Client.PutObjectAsync(request, cancellationToken);

            var publicUrl = string.IsNullOrWhiteSpace(_options.PublicUrl)
                ? null
                : BuildPublicUrl(objectKey);
            _logger.LogInformation("Uploaded file to R2 | ContentType: {ContentType}", NormalizeContentType(contentType));

            return new R2UploadResult(publicUrl, fileName, objectKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "R2 upload failed | ContentType: {ContentType}",
                NormalizeContentType(contentType));
            throw;
        }
    }

    public Task<R2UploadUrlResult> CreateUploadUrlAsync(
        string contentType,
        string path,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureConfigured();

            var normalizedContentType = NormalizeContentType(contentType);
            if (!ContentTypeExtensions.TryGetValue(normalizedContentType, out var extension))
            {
                throw new ArgumentException($"Unsupported content type '{contentType}'.", nameof(contentType));
            }

            var fileName = $"{Guid.NewGuid():N}{extension}";
            var objectKey = $"{SanitizePath(path)}/{fileName}";
            var s3Client = GetOrCreateClient();
            var request = new GetPreSignedUrlRequest
            {
                BucketName = _options.Bucket,
                Key = objectKey,
                Verb = HttpVerb.PUT,
                Expires = DateTime.UtcNow.Add(expiresIn),
                ContentType = normalizedContentType,
                Protocol = Protocol.HTTPS
            };

            var uploadUrl = s3Client.GetPreSignedURL(request);
            return Task.FromResult(new R2UploadUrlResult(uploadUrl, fileName, objectKey, "PUT", normalizedContentType));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "R2 presigned upload URL generation failed | ContentType: {ContentType}",
                NormalizeContentType(contentType));
            throw;
        }
    }

    public Task<string> CreateDownloadUrlAsync(
        string objectKey,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureConfigured();

            var s3Client = GetOrCreateClient();
            var request = new GetPreSignedUrlRequest
            {
                BucketName = _options.Bucket,
                Key = objectKey,
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.Add(expiresIn),
                Protocol = Protocol.HTTPS
            };

            return Task.FromResult(s3Client.GetPreSignedURL(request));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "R2 presigned download URL generation failed");
            throw;
        }
    }

    public async Task<R2ObjectMetadata> GetObjectMetadataAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureConfigured();

            var request = new GetObjectMetadataRequest
            {
                BucketName = _options.Bucket,
                Key = objectKey
            };

            var response = await GetOrCreateClient().GetObjectMetadataAsync(request, cancellationToken);
            return new R2ObjectMetadata(
                NormalizeContentType(response.Headers.ContentType),
                response.Headers.ContentLength,
                NormalizeChecksum(response.ChecksumSHA256),
                response.ETag);
        }
        catch (Exception ex)
        when (ex is AmazonS3Exception { StatusCode: HttpStatusCode.NotFound or HttpStatusCode.PreconditionFailed })
        {
            _logger.LogInformation(
                ex,
                "R2 metadata lookup returned expected S3 status | StatusCode: {StatusCode}",
                ((AmazonS3Exception)ex).StatusCode);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "R2 metadata lookup failed");
            throw;
        }
    }

    public async Task<byte[]> GetObjectBytesAsync(
        string objectKey,
        int maxBytes,
        CancellationToken cancellationToken = default)
    {
        return await GetObjectBytesAsync(objectKey, maxBytes, null, cancellationToken);
    }

    public async Task<byte[]> GetObjectBytesAsync(
        string objectKey,
        int maxBytes,
        string? ifMatchETag,
        CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureConfigured();

            if (maxBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxBytes), "The requested byte count must be greater than zero.");
            }

            var request = new GetObjectRequest
            {
                BucketName = _options.Bucket,
                Key = objectKey,
                ByteRange = new ByteRange(0, maxBytes - 1)
            };

            if (!string.IsNullOrEmpty(ifMatchETag))
            {
                request.EtagToMatch = ifMatchETag;
            }

            using var response = await GetOrCreateClient().GetObjectAsync(request, cancellationToken);
            using var memoryStream = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memoryStream, cancellationToken);
            return memoryStream.ToArray();
        }
        catch (Exception ex)
        when (ex is AmazonS3Exception { StatusCode: HttpStatusCode.NotFound or HttpStatusCode.PreconditionFailed })
        {
            _logger.LogInformation(
                ex,
                "R2 object read returned expected S3 status | MaxBytes: {MaxBytes} | StatusCode: {StatusCode}",
                maxBytes,
                ((AmazonS3Exception)ex).StatusCode);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "R2 object read failed | MaxBytes: {MaxBytes}",
                maxBytes);
            throw;
        }
    }

    public async Task DeleteObjectAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureConfigured();

            var request = new DeleteObjectRequest
            {
                BucketName = _options.Bucket,
                Key = objectKey
            };

            await GetOrCreateClient().DeleteObjectAsync(request, cancellationToken);
        }
        catch (Exception ex)
        when (ex is AmazonS3Exception { StatusCode: HttpStatusCode.NotFound or HttpStatusCode.PreconditionFailed })
        {
            _logger.LogInformation(
                ex,
                "R2 object delete returned expected S3 status | StatusCode: {StatusCode}",
                ((AmazonS3Exception)ex).StatusCode);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "R2 object delete failed");
            throw;
        }
    }

    public async Task<R2ObjectMetadata> CopyObjectAsync(
        string sourceObjectKey,
        string destinationObjectKey,
        string contentType,
        string? sourceIfMatchETag,
        CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureConfigured();

            var request = new CopyObjectRequest
            {
                SourceBucket = _options.Bucket,
                SourceKey = sourceObjectKey,
                DestinationBucket = _options.Bucket,
                DestinationKey = destinationObjectKey,
                MetadataDirective = S3MetadataDirective.REPLACE,
                ContentType = NormalizeContentType(contentType),
                ETagToMatch = sourceIfMatchETag
            };

            await GetOrCreateClient().CopyObjectAsync(request, cancellationToken);
            return await GetObjectMetadataAsync(destinationObjectKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "R2 object copy failed | SourceETagRequired: {SourceETagRequired}",
                !string.IsNullOrWhiteSpace(sourceIfMatchETag));
            throw;
        }
    }

    public async Task<R2DownloadObjectResult> DownloadObjectAsync(
        string objectKey,
        string? ifMatchETag,
        CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureConfigured();

            var request = new GetObjectRequest
            {
                BucketName = _options.Bucket,
                Key = objectKey
            };

            if (!string.IsNullOrWhiteSpace(ifMatchETag))
            {
                request.EtagToMatch = ifMatchETag;
            }

            var response = await GetOrCreateClient().GetObjectAsync(request, cancellationToken);
            return new R2DownloadObjectResult(
                response.ResponseStream,
                NormalizeContentType(response.Headers.ContentType),
                response.Headers.ContentLength,
                response.ETag);
        }
        catch (Exception ex)
        when (ex is AmazonS3Exception { StatusCode: HttpStatusCode.NotFound or HttpStatusCode.PreconditionFailed })
        {
            _logger.LogInformation(
                ex,
                "R2 object download returned expected S3 status | StatusCode: {StatusCode}",
                ((AmazonS3Exception)ex).StatusCode);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "R2 object download failed");
            throw;
        }
    }

    private void EnsureConfigured()
    {
        if (_options.IsConfigured)
        {
            return;
        }

        throw new InvalidOperationException(
            "Cloudflare R2 is not configured. Set R2_ACCOUNT_ID or R2_SERVICE_URL, plus R2_ACCESS_KEY_ID, R2_SECRET_ACCESS_KEY, and R2_BUCKET.");
    }

    private IAmazonS3 GetOrCreateClient()
    {
        if (_s3Client != null)
        {
            return _s3Client;
        }

        lock (_clientLock)
        {
            if (_s3Client != null)
            {
                return _s3Client;
            }

            var credentials = new BasicAWSCredentials(
                _options.AccessKeyId.Trim(),
                _options.SecretAccessKey.Trim());

            var config = new AmazonS3Config
            {
                ServiceURL = _options.ResolvedServiceUrl,
                ForcePathStyle = true,
                AuthenticationRegion = "auto"
            };

            _logger.LogInformation("Initializing R2 S3 client");

            _s3Client = new AmazonS3Client(credentials, config);
            return _s3Client;
        }
    }

    public void Dispose()
    {
        lock (_clientLock)
        {
            _s3Client?.Dispose();
            _s3Client = null;
        }
        GC.SuppressFinalize(this);
    }

    private string BuildPublicUrl(string objectKey)
    {
        var baseUrl = _options.PublicUrl.Trim().TrimEnd('/');
        var bucket = _options.Bucket.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("R2 public URL is not configured.");
        }

        if (baseUrl.EndsWith($"/{bucket}", StringComparison.OrdinalIgnoreCase))
        {
            return $"{baseUrl}/{objectKey}";
        }

        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var publicUri) &&
            publicUri.Host.EndsWith(".r2.cloudflarestorage.com", StringComparison.OrdinalIgnoreCase))
        {
            return $"{baseUrl}/{bucket}/{objectKey}";
        }

        return $"{baseUrl}/{objectKey}";
    }

    private static string NormalizeContentType(string contentType)
    {
        return (contentType ?? string.Empty).Split(';', 2)[0].Trim().ToLowerInvariant();
    }

    private static string? NormalizeChecksum(string? checksum)
    {
        if (string.IsNullOrWhiteSpace(checksum))
        {
            return null;
        }

        return checksum.Trim().ToLowerInvariant();
    }

    private static string SanitizePath(string path)
    {
        var sanitizedSegments = path
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(SanitizePathSegment)
            .Where(segment => !string.IsNullOrWhiteSpace(segment));

        return string.Join('/', sanitizedSegments);
    }

    private static string SanitizePathSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Where(ch => !invalidChars.Contains(ch) && ch != '/' && ch != '\\')
            .ToArray());

        return sanitized.Replace("..", string.Empty, StringComparison.Ordinal);
    }
}
