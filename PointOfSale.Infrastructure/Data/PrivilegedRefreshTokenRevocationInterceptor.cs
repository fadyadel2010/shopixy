using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.SuperAdmin.Models;

namespace PointOfSale.Infrastructure.Data;

/// <summary>
/// Revokes persisted privileged refresh authority when the SQL authority that
/// grants it changes. Access tokens are already revalidated at request time;
/// this interceptor ensures refresh credentials die at the same boundary.
/// </summary>
public sealed class PrivilegedRefreshTokenRevocationInterceptor : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ILogger<PrivilegedRefreshTokenRevocationInterceptor> _logger;

    public PrivilegedRefreshTokenRevocationInterceptor(
        ILogger<PrivilegedRefreshTokenRevocationInterceptor> logger)
    {
        _logger = logger;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        RevokeStaleRefreshAuthority(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        RevokeStaleRefreshAuthority(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void RevokeStaleRefreshAuthority(DbContext? context)
    {
        if (context is not ApplicationDbContext dbContext)
        {
            return;
        }

        var revokedUserIds = new HashSet<Guid>();
        var revokedSuperAdminIds = new HashSet<Guid>();

        foreach (var entry in dbContext.ChangeTracker.Entries<User>()
                     .Where(entry => entry.State == EntityState.Modified))
        {
            if (IsUserAuthorityReduced(entry))
            {
                RevokePosUser(entry.Entity, "POS_USER_AUTHORITY_CHANGED", revokedUserIds, dbContext);
            }
        }

        foreach (var tenantId in TenantAuthorityReductionIds(dbContext))
        {
            var users = dbContext.Users
                .IgnoreQueryFilters()
                .Where(user =>
                    user.TenantId == tenantId &&
                    !user.IsDeleted &&
                    user.RefreshToken != null)
                .ToList();

            foreach (var user in users)
            {
                RevokePosUser(user, "TENANT_AUTHORITY_REDUCED", revokedUserIds, dbContext);
            }
        }

        foreach (var profile in GovernanceAuthorityReductionProfiles(dbContext))
        {
            switch (profile.Role)
            {
                case UserGovernanceRole.Vendor:
                {
                    var users = dbContext.Users
                        .IgnoreQueryFilters()
                        .Where(user =>
                            user.TenantId == profile.SubjectId &&
                            !user.IsDeleted &&
                            user.RefreshToken != null)
                        .ToList();

                    foreach (var user in users)
                    {
                        RevokePosUser(user, "VENDOR_GOVERNANCE_RESTRICTED", revokedUserIds, dbContext);
                    }

                    break;
                }
                case UserGovernanceRole.Admin:
                {
                    var superAdmin = dbContext.SuperAdmins
                        .FirstOrDefault(admin => admin.Id == profile.SubjectId);

                    if (superAdmin is not null && !revokedSuperAdminIds.Contains(superAdmin.Id))
                    {
                        superAdmin.ClearRefreshToken();
                        revokedSuperAdminIds.Add(superAdmin.Id);
                        AddAuditLog(
                            dbContext,
                            tenantId: null,
                            userId: superAdmin.Id,
                            action: "PRIVILEGED_REFRESH_REVOKED",
                            entityType: "SuperAdmin",
                            entityId: superAdmin.Id.ToString("D"),
                            description: "Super Admin refresh authority revoked because governance authority was restricted.",
                            reason: "SUPER_ADMIN_GOVERNANCE_RESTRICTED");
                    }

                    break;
                }
            }
        }

        if (revokedUserIds.Count > 0 || revokedSuperAdminIds.Count > 0)
        {
            _logger.LogWarning(
                "Privileged refresh authority revoked during SQL authority change | PosUsers: {PosUserCount} | SuperAdmins: {SuperAdminCount}",
                revokedUserIds.Count,
                revokedSuperAdminIds.Count);
        }
    }

    private static bool IsUserAuthorityReduced(EntityEntry<User> entry)
    {
        return entry.Property(nameof(User.Role)).IsModified ||
               IsBooleanChangedToFalse(entry, nameof(User.IsActive)) ||
               IsBooleanChangedToTrue(entry, nameof(User.IsDeleted));
    }

    private static IEnumerable<Guid> TenantAuthorityReductionIds(ApplicationDbContext dbContext)
    {
        foreach (var entry in dbContext.ChangeTracker.Entries<Tenant>()
                     .Where(entry => entry.State == EntityState.Modified))
        {
            var inactive = IsBooleanChangedToFalse(entry, nameof(Tenant.IsActive));
            var suspendedOrDeleted = entry.Property(nameof(Tenant.Status)).IsModified &&
                                     entry.Entity.Status is TenantStatus.Suspended or TenantStatus.Deleted;

            if (inactive || suspendedOrDeleted)
            {
                yield return entry.Entity.Id;
            }
        }
    }

    private static IEnumerable<UserGovernanceProfile> GovernanceAuthorityReductionProfiles(ApplicationDbContext dbContext)
    {
        foreach (var entry in dbContext.ChangeTracker.Entries<UserGovernanceProfile>()
                     .Where(entry => entry.State == EntityState.Modified))
        {
            if (entry.Property(nameof(UserGovernanceProfile.Status)).IsModified &&
                entry.Entity.Status is UserGovernanceStatus.Suspended or UserGovernanceStatus.Banned)
            {
                yield return entry.Entity;
            }
        }
    }

    private static bool IsBooleanChangedToFalse<TEntity>(EntityEntry<TEntity> entry, string propertyName)
        where TEntity : class
    {
        var property = entry.Property(propertyName);
        return property.IsModified &&
               property.OriginalValue is true &&
               property.CurrentValue is false;
    }

    private static bool IsBooleanChangedToTrue<TEntity>(EntityEntry<TEntity> entry, string propertyName)
        where TEntity : class
    {
        var property = entry.Property(propertyName);
        return property.IsModified &&
               property.OriginalValue is false &&
               property.CurrentValue is true;
    }

    private static void RevokePosUser(
        User user,
        string reason,
        ISet<Guid> revokedUserIds,
        ApplicationDbContext dbContext)
    {
        if (revokedUserIds.Contains(user.Id))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(user.RefreshToken) && user.RefreshTokenExpiryTime is null)
        {
            return;
        }

        user.RefreshToken = null;
        user.RefreshTokenExpiryTime = null;
        revokedUserIds.Add(user.Id);

        AddAuditLog(
            dbContext,
            user.TenantId,
            user.Id,
            "PRIVILEGED_REFRESH_REVOKED",
            "User",
            user.Id.ToString("D"),
            "POS refresh authority revoked because current privileged SQL authority changed.",
            reason);
    }

    private static void AddAuditLog(
        ApplicationDbContext dbContext,
        Guid? tenantId,
        Guid? userId,
        string action,
        string entityType,
        string entityId,
        string description,
        string reason)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            UserId = userId,
            TimestampUtc = DateTime.UtcNow,
            ActionType = action,
            EntityType = entityType,
            EntityId = entityId,
            Description = description,
            AfterDataJson = JsonSerializer.Serialize(new
            {
                reason,
                evidence = "refresh_authority_revoked",
                timestampUtc = DateTime.UtcNow
            }, JsonOptions),
            Source = "Security",
            SuccessFlag = true
        });
    }
}
