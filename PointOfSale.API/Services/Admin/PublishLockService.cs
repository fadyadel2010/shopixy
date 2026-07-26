using Microsoft.Extensions.Logging;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.API.Services.Admin;

/// <summary>
/// Service for managing distributed publish locks.
/// Phase: Storefront Admin Hardening - P3
/// 
/// Prevents concurrent publish operations for the same tenant.
/// </summary>
public interface IPublishLockService
{
    /// <summary>
    /// Attempts to acquire a publish lock for a tenant.
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="operationType">Type of publish operation (theme, cms, navigation)</param>
    /// <returns>Lock result with success status and lock token</returns>
    Task<PublishLockResult> TryAcquireAsync(Guid tenantId, string operationType);

    /// <summary>
    /// Releases a previously acquired publish lock.
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="lockToken">Token returned from TryAcquireAsync</param>
    Task ReleaseAsync(Guid tenantId, string lockToken);

    /// <summary>
    /// Checks if a publish operation is currently in progress for a tenant.
    /// </summary>
    Task<bool> IsPublishingAsync(Guid tenantId);
}

/// <summary>
/// Result of a lock acquisition attempt.
/// </summary>
public class PublishLockResult
{
    public bool Acquired { get; set; }
    public string? LockToken { get; set; }
    public string? Message { get; set; }

    public static PublishLockResult Success(string lockToken) => new()
    {
        Acquired = true,
        LockToken = lockToken
    };

    public static PublishLockResult Failed(string message) => new()
    {
        Acquired = false,
        Message = message
    };
}

/// <summary>
/// Redis-based publish lock service implementation.
/// </summary>
public class PublishLockService : IPublishLockService
{
    private readonly IDistributedLockService _lockService;
    private readonly ILogger<PublishLockService> _logger;

    private const string LockKeyPrefix = "storefront:publish:";
    private static readonly TimeSpan LockTtl = TimeSpan.FromSeconds(30);

    public PublishLockService(
        IDistributedLockService lockService,
        ILogger<PublishLockService> logger)
    {
        _lockService = lockService;
        _logger = logger;
    }

    public async Task<PublishLockResult> TryAcquireAsync(Guid tenantId, string operationType)
    {
        var lockKey = $"{LockKeyPrefix}{tenantId}";
        var lockToken = Guid.NewGuid().ToString("N");

        _logger.LogDebug(
            "Attempting to acquire publish lock | TenantId: {TenantId} | Operation: {Operation}",
            tenantId, operationType);

        var acquireResult = await _lockService.TryAcquireLockAsync(lockKey, lockToken, LockTtl);

        if (acquireResult.IsInfrastructureFailure)
        {
            _logger.LogError(
                "Publish lock infrastructure unavailable | TenantId: {TenantId} | Operation: {Operation}",
                tenantId,
                operationType);

            return PublishLockResult.Failed(
                "Publishing is temporarily unavailable because the lock infrastructure could not be reached.");
        }

        if (!acquireResult.IsAcquired)
        {
            _logger.LogWarning(
                "Publish lock denied - already in progress | TenantId: {TenantId} | Operation: {Operation}",
                tenantId, operationType);

            return PublishLockResult.Failed(
                "Publishing already in progress. Please wait for the current operation to complete.");
        }

        _logger.LogInformation(
            "Publish lock acquired | TenantId: {TenantId} | Operation: {Operation}",
            tenantId, operationType);

        return PublishLockResult.Success(lockToken);
    }

    public async Task ReleaseAsync(Guid tenantId, string lockToken)
    {
        var lockKey = $"{LockKeyPrefix}{tenantId}";

        var released = await _lockService.ReleaseLockAsync(lockKey, lockToken);

        if (released)
        {
            _logger.LogInformation(
                "Publish lock released | TenantId: {TenantId}",
                tenantId);
        }
        else
        {
            _logger.LogWarning(
                "Publish lock release failed (expired or not owner) | TenantId: {TenantId}",
                tenantId);
        }
    }

    public async Task<bool> IsPublishingAsync(Guid tenantId)
    {
        var lockKey = $"{LockKeyPrefix}{tenantId}";
        return await _lockService.IsLockedAsync(lockKey);
    }
}
