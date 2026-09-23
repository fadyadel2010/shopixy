using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PointOfSale.API.DTOs.Users;
using PointOfSale.API.Services.Users;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Security;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Security;

/// <summary>
/// Wave 2 lifecycle tests for tenant POS user management.
/// SQLite-backed: enforces real unique constraints, FKs, check constraints,
/// transactions, and query filters. PostgreSQL-only guarantees (xmin bump,
/// partial Owner index races, FOR UPDATE) are additionally covered by
/// PosUserManagementModelTests (model metadata) and the PostgreSQL lane in
/// the implementation report for SOL.
/// </summary>
public sealed class PosUserManagementTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();

    public PosUserManagementTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        // Repository precedent: PostgreSQL functions used in model check
        // constraints need SQLite shims before EnsureCreated.
        _connection.CreateFunction<string?, string?>("btrim", value => value?.Trim());
        _connection.CreateFunction<string?, string?, string?>(
            "btrim",
            (value, characters) => value?.Trim(characters?.ToCharArray() ?? []));
        _connection.CreateFunction<string?, int>("char_length", value => value?.Length ?? 0);
        _connection.CreateFunction<string?, string?, string?, string?, string?>(
            "REGEXP_REPLACE",
            (value, pattern, replacement, flags) =>
            {
                if (value is null || pattern is null)
                {
                    return value;
                }

                var regexOptions = flags?.Contains('i') == true
                    ? System.Text.RegularExpressions.RegexOptions.IgnoreCase
                    : System.Text.RegularExpressions.RegexOptions.None;
                return System.Text.RegularExpressions.Regex.Replace(
                    value, pattern, replacement ?? string.Empty, regexOptions);
            });

        using var setup = ContextFor(_tenantId);
        setup.Database.EnsureCreated();
        setup.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Name = "Tenant A",
            IsActive = true,
            Status = TenantStatus.Active,
            Currency = "EGP",
            CreatedAtUtc = DateTime.UtcNow,
        });
        setup.Tenants.Add(new Tenant
        {
            Id = _otherTenantId,
            Name = "Tenant B",
            IsActive = true,
            Status = TenantStatus.Active,
            Currency = "EGP",
            CreatedAtUtc = DateTime.UtcNow,
        });
        setup.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    // =====================================================================
    // Invitation
    // =====================================================================

    [Fact]
    public async Task Invite_CreatesInactiveUserHashOnlyTokenAndAuditAtomically()
    {
        var (db, service) = Service();
        var owner = SeedUser(db, UserRole.Owner, "owner");

        var (user, rawToken) = await service.InviteAsync(owner.Id, new InvitePosUserRequest
        {
            FirstName = "Cash",
            LastName = "ier",
            Username = "  Cashier.One ",
            Email = "Cashier.One@Example.com ",
            Role = "Cashier",
        }, CancellationToken.None);

        Assert.Equal("Pending", user.Status);
        Assert.False(string.IsNullOrWhiteSpace(rawToken));

        using var verify = ContextFor(_tenantId);
        var row = await verify.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == user.Id);
        Assert.False(row.IsActive);
        Assert.Null(row.RefreshToken);
        Assert.Equal("Cashier.One", row.Username);
        Assert.Equal("CASHIER.ONE", row.NormalizedUsername);
        Assert.Equal("CASHIER.ONE@EXAMPLE.COM", row.NormalizedEmail);
        Assert.Equal(owner.Id, row.InvitedByUserId);
        Assert.NotNull(row.InvitedAtUtc);
        Assert.Equal(EmailVerificationStatus.Unverified, row.EmailVerificationStatus);

        var token = await verify.PosUserSecurityTokens.IgnoreQueryFilters()
            .SingleAsync(t => t.UserId == user.Id);
        Assert.Equal(PosUserSecurityTokenPurpose.Invitation, token.Purpose);
        Assert.DoesNotContain(rawToken, token.TokenHash);
        Assert.True(token.ExpiresAtUtc > DateTime.UtcNow);
        Assert.Null(token.ConsumedAtUtc);

        var audit = await verify.AuditLogs.IgnoreQueryFilters()
            .SingleAsync(a => a.ActionType == PosUserManagementAuditActions.Invited);
        Assert.Equal(_tenantId, audit.TenantId);
        Assert.Equal(owner.Id, audit.UserId);
        Assert.Equal(user.Id.ToString("D"), audit.EntityId);
        Assert.Equal("User", audit.EntityType);
        Assert.DoesNotContain(rawToken, audit.AfterDataJson ?? string.Empty);
        Assert.DoesNotContain("PasswordHash", audit.AfterDataJson ?? string.Empty);
    }

    [Fact]
    public async Task Invite_RejectsOwnerRole()
    {
        var (db, service) = Service();
        var owner = SeedUser(db, UserRole.Owner, "owner");

        var exception = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.InviteAsync(owner.Id, Invite("someadmin", "someadmin@example.com", UserRole.Owner), CancellationToken.None));

        Assert.Equal("OWNER_INVARIANT_VIOLATION", exception.Code);
        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
    }

    [Fact]
    public async Task Invite_RejectsCaseInsensitiveDuplicates()
    {
        var (db, service) = Service();
        var owner = SeedUser(db, UserRole.Owner, "owner");
        await service.InviteAsync(owner.Id, Invite("cashier1", "Cashier1@Example.com"), CancellationToken.None);

        var byUsername = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.InviteAsync(owner.Id, Invite("CASHIER1", "other@example.com"), CancellationToken.None));
        Assert.Equal("IDENTITY_ALREADY_EXISTS", byUsername.Code);

        var byEmail = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.InviteAsync(owner.Id, Invite("other", "  cashier1@example.COM "), CancellationToken.None));
        Assert.Equal("IDENTITY_ALREADY_EXISTS", byEmail.Code);
    }

    [Fact]
    public async Task Invite_EnforcesGlobalUniquenessAcrossTenants()
    {
        var (db, service) = Service();
        var owner = SeedUser(db, UserRole.Owner, "owner");

        using var other = ContextFor(_otherTenantId);
        other.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            TenantId = _otherTenantId,
            Username = "shared-name",
            Email = "shared@example.com",
            PasswordHash = "hash",
            FirstName = "S",
            LastName = "T",
            Role = UserRole.Cashier,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        });
        await other.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.InviteAsync(owner.Id, Invite("Shared-Name", "new@example.com"), CancellationToken.None));
        Assert.Equal("IDENTITY_ALREADY_EXISTS", exception.Code);
    }

    [Fact]
    public async Task Invite_EnforcesActorTargetRank()
    {
        var (db, service) = Service();
        var owner = SeedUser(db, UserRole.Owner, "owner");
        var admin = SeedUser(db, UserRole.Admin, "admin", active: true, accepted: true);

        // Owner may invite Admin.
        var adminInvite = await service.InviteAsync(owner.Id, Invite("admin2", "a2", UserRole.Admin), CancellationToken.None);
        Assert.Equal("Admin", adminInvite.User.Role);

        // Admin may invite Manager but not Admin or Owner.
        var managerInvite = await service.InviteAsync(admin.Id, Invite("mgr", "m", UserRole.Manager), CancellationToken.None);
        Assert.Equal("Manager", managerInvite.User.Role);

        var adminOnAdmin = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.InviteAsync(admin.Id, Invite("admin3", "a3", UserRole.Admin), CancellationToken.None));
        Assert.Equal("TARGET_ROLE_FORBIDDEN", adminOnAdmin.Code);
    }

    [Fact]
    public async Task Invite_DeniesNonPrivilegedActors()
    {
        var (db, service) = Service();
        _ = SeedUser(db, UserRole.Owner, "owner");
        var cashier = SeedUser(db, UserRole.Cashier, "cashier", active: true, accepted: true);
        var manager = SeedUser(db, UserRole.Manager, "manager", active: true, accepted: true);

        foreach (var actor in new[] { cashier, manager })
        {
            var exception = await Assert.ThrowsAsync<PosUserManagementException>(() =>
                service.InviteAsync(actor.Id, Invite("x" + actor.Username, "x" + actor.Email), CancellationToken.None));
            Assert.Equal("POS_PERMISSION_FORBIDDEN", exception.Code);
        }
    }

    [Fact]
    public async Task Invite_RejectsUnknownRole()
    {
        var (db, service) = Service();
        var owner = SeedUser(db, UserRole.Owner, "owner");

        var exception = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.InviteAsync(owner.Id, Invite("u1", "u1@e.com", role: "SuperAdmin"), CancellationToken.None));
        Assert.Equal("VALIDATION_ERROR", exception.Code);
    }

    // =====================================================================
    // Invitation acceptance
    // =====================================================================

    [Fact]
    public async Task AcceptInvitation_ActivatesWithoutCreatingSession()
    {
        var (db, service) = Service();
        var owner = SeedUser(db, UserRole.Owner, "owner");
        var (invited, rawToken) = await service.InviteAsync(
            owner.Id, Invite("newbie", "newbie@example.com"), CancellationToken.None);

        await service.AcceptInvitationAsync(rawToken, "StrongPass1!", CancellationToken.None);

        using var verify = ContextFor(_tenantId);
        var row = await verify.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == invited.Id);
        Assert.True(row.IsActive);
        Assert.NotNull(row.InvitationAcceptedAtUtc);
        Assert.Equal(EmailVerificationStatus.Verified, row.EmailVerificationStatus);
        Assert.NotNull(row.EmailVerifiedAtUtc);
        Assert.Null(row.RefreshToken);
        Assert.Null(row.RefreshTokenExpiryTime);
        Assert.NotEqual("hash", row.PasswordHash);

        var tokens = await verify.PosUserSecurityTokens.IgnoreQueryFilters()
            .Where(t => t.UserId == invited.Id).ToListAsync();
        Assert.All(tokens, t => Assert.NotNull(t.ConsumedAtUtc));

        var audit = await verify.AuditLogs.IgnoreQueryFilters()
            .SingleAsync(a => a.ActionType == PosUserManagementAuditActions.InvitationAccepted);
        Assert.Equal(invited.Id, audit.UserId);
        Assert.DoesNotContain(rawToken, (audit.BeforeDataJson ?? string.Empty) + (audit.AfterDataJson ?? string.Empty));
    }

    [Fact]
    public async Task AcceptInvitation_RejectsExpiredConsumedRevokedAndWrongPurposeTokens()
    {
        var (db, service) = Service();
        var owner = SeedUser(db, UserRole.Owner, "owner");
        var (invited, rawToken) = await service.InviteAsync(
            owner.Id, Invite("newbie", "newbie@example.com"), CancellationToken.None);

        // Wrong purpose: craft a password-reset token and present it as invitation.
        string resetRaw;
        using (var setup = ContextFor(_tenantId))
        {
            var activeId = SeedActive(setup, "active1");
            var activeVersion = (await service.GetAsync(owner.Id, activeId, CancellationToken.None)).Version;
            resetRaw = await service.IssuePasswordResetAsync(owner.Id, activeId, activeVersion, CancellationToken.None);
        }

        var wrongPurpose = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.AcceptInvitationAsync(resetRaw, "StrongPass1!", CancellationToken.None));
        Assert.Equal("VALIDATION_ERROR", wrongPurpose.Code);

        // Consumed: accept once, then again.
        await service.AcceptInvitationAsync(rawToken, "StrongPass1!", CancellationToken.None);
        var consumed = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.AcceptInvitationAsync(rawToken, "StrongPass2!", CancellationToken.None));
        Assert.Equal("VALIDATION_ERROR", consumed.Code);

        // Revoked: invite, revoke, accept.
        var (revoked, revokedRaw) = await service.InviteAsync(
            owner.Id, Invite("rev", "rev@example.com"), CancellationToken.None);
        await service.RevokeInvitationAsync(owner.Id, revoked.Id, "no longer needed", revoked.Version, CancellationToken.None);
        var revokedEx = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.AcceptInvitationAsync(revokedRaw, "StrongPass1!", CancellationToken.None));
        Assert.Equal("VALIDATION_ERROR", revokedEx.Code);

        // Expired: craft an expired-but-check-valid token directly (production
        // code can never persist ExpiresAtUtc <= CreatedAtUtc).
        var expiredRaw = "expired-" + Guid.NewGuid().ToString("N");
        var expiredTarget = SeedUser(db, UserRole.Cashier, "expireduser");
        using (var setup = ContextFor(_tenantId))
        {
            var created = DateTime.UtcNow.AddHours(-2);
            setup.PosUserSecurityTokens.Add(new PosUserSecurityToken
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                UserId = expiredTarget.Id,
                Purpose = PosUserSecurityTokenPurpose.Invitation,
                TokenHash = PosUserManagementService_Hash(expiredRaw),
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1),
                CreatedAtUtc = created,
                CreatedAt = created,
            });
            await setup.SaveChangesAsync();
        }

        var expired = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.AcceptInvitationAsync(expiredRaw, "StrongPass1!", CancellationToken.None));
        Assert.Equal("SECURITY_TOKEN_EXPIRED", expired.Code);
        Assert.Equal(StatusCodes.Status410Gone, expired.StatusCode);
    }

    [Fact]
    public async Task AcceptInvitation_RejectsWeakPassword()
    {
        var (db, service) = Service();
        var owner = SeedUser(db, UserRole.Owner, "owner");
        var (_, rawToken) = await service.InviteAsync(
            owner.Id, Invite("newbie", "newbie@example.com"), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.AcceptInvitationAsync(rawToken, "short", CancellationToken.None));
        Assert.Equal("VALIDATION_ERROR", exception.Code);
    }

    // =====================================================================
    // Resend / revoke invitation
    // =====================================================================

    [Fact]
    public async Task ResendInvitation_RevokesPriorTokenAndIssuesNew()
    {
        var (db, service) = Service();
        var owner = SeedUser(db, UserRole.Owner, "owner");
        var (invited, firstRaw) = await service.InviteAsync(
            owner.Id, Invite("newbie", "newbie@example.com"), CancellationToken.None);

        var (_, secondRaw) = await service.ResendInvitationAsync(owner.Id, invited.Id, invited.Version, CancellationToken.None);
        Assert.NotEqual(firstRaw, secondRaw);

        // First token is dead.
        var stale = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.AcceptInvitationAsync(firstRaw, "StrongPass1!", CancellationToken.None));
        Assert.Equal("VALIDATION_ERROR", stale.Code);

        // Second token works.
        await service.AcceptInvitationAsync(secondRaw, "StrongPass1!", CancellationToken.None);

        using var verify = ContextFor(_tenantId);
        Assert.Equal(1, await verify.AuditLogs.IgnoreQueryFilters()
            .CountAsync(a => a.ActionType == PosUserManagementAuditActions.InvitationResent));
    }

    [Fact]
    public async Task RevokeInvitation_RemovesPendingStateWithoutDeletingUser()
    {
        var (db, service) = Service();
        var owner = SeedUser(db, UserRole.Owner, "owner");
        var (invited, raw) = await service.InviteAsync(
            owner.Id, Invite("newbie", "newbie@example.com"), CancellationToken.None);

        await service.RevokeInvitationAsync(owner.Id, invited.Id, "duplicate", invited.Version, CancellationToken.None);

        using var verify = ContextFor(_tenantId);
        Assert.NotNull(await verify.Users.SingleOrDefaultAsync(u => u.Id == invited.Id));
        var detail = await service.GetAsync(owner.Id, invited.Id, CancellationToken.None);
        Assert.Equal("InvitationExpired", detail.Status);

        var dead = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.AcceptInvitationAsync(raw, "StrongPass1!", CancellationToken.None));
        Assert.Equal("VALIDATION_ERROR", dead.Code);
    }

    // =====================================================================
    // Profile
    // =====================================================================

    [Fact]
    public async Task UpdateProfile_ChangesAllowListedFieldsAndReverifiesEmail()
    {
        var (db, service) = Service();
        var owner = SeedUser(db, UserRole.Owner, "owner");
        var cashier = SeedUser(db, UserRole.Cashier, "cashier", active: true, accepted: true);
        var version = (await service.GetAsync(owner.Id, cashier.Id, CancellationToken.None)).Version;

        var updated = await service.UpdateProfileAsync(owner.Id, cashier.Id, new UpdatePosUserProfileRequest
        {
            FirstName = "New",
            LastName = "Name",
            Phone = "01000000000",
            Email = "changed@example.com",
        }, version, CancellationToken.None);

        Assert.Equal("New", updated.FirstName);
        Assert.Equal("changed@example.com", updated.Email);
        Assert.False(updated.EmailVerified);

        using var verify = ContextFor(_tenantId);
        var audit = await verify.AuditLogs.IgnoreQueryFilters()
            .SingleAsync(a => a.ActionType == PosUserManagementAuditActions.ProfileChanged);
        Assert.Contains("changed@example.com", audit.AfterDataJson);
    }

    [Fact]
    public async Task UpdateProfile_RequiresVersionAndDeniesSelfAndDuplicates()
    {
        var (db, service) = Service();
        var owner = SeedUser(db, UserRole.Owner, "owner");
        var cashier = SeedUser(db, UserRole.Cashier, "cashier", active: true, accepted: true);

        var missing = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.UpdateProfileAsync(owner.Id, cashier.Id, Profile("A", "B", "a@b.com"), null, CancellationToken.None));
        Assert.Equal("VALIDATION_ERROR", missing.Code);

        var stale = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.UpdateProfileAsync(owner.Id, cashier.Id, Profile("A", "B", "a@b.com"), "stale", CancellationToken.None));
        Assert.Equal("USER_VERSION_CONFLICT", stale.Code);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, stale.StatusCode);

        var self = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.UpdateProfileAsync(owner.Id, owner.Id, Profile("A", "B", "owner@example.com"), "0", CancellationToken.None));
        Assert.Equal("VALIDATION_ERROR", self.Code);

        var version = (await service.GetAsync(owner.Id, cashier.Id, CancellationToken.None)).Version;
        var dup = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.UpdateProfileAsync(owner.Id, cashier.Id, Profile("A", "B", owner.Email.ToUpperInvariant()), version, CancellationToken.None));
        Assert.Equal("IDENTITY_ALREADY_EXISTS", dup.Code);
    }

    [Fact]
    public async Task UpdateProfile_EmailChangeRevokesOutstandingSecurityTokens()
    {
        var (db, service) = Service();
        var owner = SeedUser(db, UserRole.Owner, "owner");
        var cashier = SeedUser(db, UserRole.Cashier, "cashier", active: true, accepted: true);
        var version = (await service.GetAsync(owner.Id, cashier.Id, CancellationToken.None)).Version;
        var rawReset = await service.IssuePasswordResetAsync(
            owner.Id, cashier.Id, version, CancellationToken.None);
        var afterIssue = (await service.GetAsync(owner.Id, cashier.Id, CancellationToken.None)).Version;

        await service.UpdateProfileAsync(
            owner.Id,
            cashier.Id,
            Profile("Cash", "Ier", "new-address@example.com"),
            afterIssue,
            CancellationToken.None);

        var rejected = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.CompletePasswordResetAsync(rawReset, "BrandNewPass1!", CancellationToken.None));
        Assert.Equal("VALIDATION_ERROR", rejected.Code);
    }

    // =====================================================================
    // Role
    // =====================================================================

    [Fact]
    public async Task ChangeRole_RevokesSessionAndAuditsTransition()
    {
        var (db, service) = Service();
        var owner = SeedUser(db, UserRole.Owner, "owner");
        var cashier = SeedUser(db, UserRole.Cashier, "cashier", active: true, accepted: true, session: true);
        var version = (await service.GetAsync(owner.Id, cashier.Id, CancellationToken.None)).Version;

        var updated = await service.ChangeRoleAsync(
            owner.Id, cashier.Id, new ChangePosUserRoleRequest { Role = "Manager" }, version, CancellationToken.None);

        Assert.Equal("Manager", updated.Role);

        using var verify = ContextFor(_tenantId);
        var row = await verify.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == cashier.Id);
        Assert.Null(row.RefreshToken);
        var audit = await verify.AuditLogs.IgnoreQueryFilters()
            .SingleAsync(a => a.ActionType == PosUserManagementAuditActions.RoleChanged);
        Assert.Contains("Cashier", audit.BeforeDataJson);
        Assert.Contains("Manager", audit.AfterDataJson);
    }

    [Fact]
    public async Task ChangeRole_DeniesAdminOnAdminOwnerSelfAndOwnerAssignment()
    {
        var (db, service) = Service();
        var owner = SeedUser(db, UserRole.Owner, "owner");
        var admin = SeedUser(db, UserRole.Admin, "admin", active: true, accepted: true);
        var cashier = SeedUser(db, UserRole.Cashier, "cashier", active: true, accepted: true);

        var adminVersion = (await service.GetAsync(owner.Id, admin.Id, CancellationToken.None)).Version;
        var adminOnAdmin = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.ChangeRoleAsync(admin.Id, admin.Id, new ChangePosUserRoleRequest { Role = "Manager" }, adminVersion, CancellationToken.None));
        Assert.Equal("VALIDATION_ERROR", adminOnAdmin.Code); // self first

        var cashierVersion = (await service.GetAsync(owner.Id, cashier.Id, CancellationToken.None)).Version;
        var adminOnCashierToOwner = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.ChangeRoleAsync(admin.Id, cashier.Id, new ChangePosUserRoleRequest { Role = "Owner" }, cashierVersion, CancellationToken.None));
        Assert.Equal("OWNER_INVARIANT_VIOLATION", adminOnCashierToOwner.Code);

        var ownerVersion = (await service.GetAsync(owner.Id, owner.Id, CancellationToken.None)).Version;
        var selfDemote = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.ChangeRoleAsync(owner.Id, owner.Id, new ChangePosUserRoleRequest { Role = "Admin" }, ownerVersion, CancellationToken.None));
        Assert.Equal("VALIDATION_ERROR", selfDemote.Code);

        var ownerToOwner = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.ChangeRoleAsync(owner.Id, cashier.Id, new ChangePosUserRoleRequest { Role = "Owner" }, cashierVersion, CancellationToken.None));
        Assert.Equal("OWNER_INVARIANT_VIOLATION", ownerToOwner.Code);
    }

    // =====================================================================
    // Status
    // =====================================================================

    [Fact]
    public async Task Disable_RequiresReasonRevokesSessionAndBlocksOwnerAndSelf()
    {
        var (db, service) = Service();
        var owner = SeedUser(db, UserRole.Owner, "owner");
        var cashier = SeedUser(db, UserRole.Cashier, "cashier", active: true, accepted: true, session: true);
        var version = (await service.GetAsync(owner.Id, cashier.Id, CancellationToken.None)).Version;

        var noReason = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.ChangeStatusAsync(owner.Id, cashier.Id, new ChangePosUserStatusRequest { IsActive = false }, version, CancellationToken.None));
        Assert.Equal("VALIDATION_ERROR", noReason.Code);

        var disabled = await service.ChangeStatusAsync(owner.Id, cashier.Id,
            new ChangePosUserStatusRequest { IsActive = false, Reason = "Policy violation" }, version, CancellationToken.None);
        Assert.Equal("Disabled", disabled.Status);
        Assert.Equal("Policy violation", disabled.DisableReason);

        using (var verify = ContextFor(_tenantId))
        {
            var row = await verify.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == cashier.Id);
            Assert.False(row.IsActive);
            Assert.Null(row.RefreshToken);
        }

        var ownerVersion = (await service.GetAsync(owner.Id, owner.Id, CancellationToken.None)).Version;
        var disableOwner = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.ChangeStatusAsync(owner.Id, owner.Id, new ChangePosUserStatusRequest { IsActive = false, Reason = "x" }, ownerVersion, CancellationToken.None));
        Assert.Equal("VALIDATION_ERROR", disableOwner.Code); // self first

        var admin = SeedUser(db, UserRole.Admin, "admin2", active: true, accepted: true);
        var ownerVersion2 = (await service.GetAsync(owner.Id, owner.Id, CancellationToken.None)).Version;
        var adminDisablesOwner = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.ChangeStatusAsync(admin.Id, owner.Id, new ChangePosUserStatusRequest { IsActive = false, Reason = "x" }, ownerVersion2, CancellationToken.None));
        Assert.Equal("OWNER_INVARIANT_VIOLATION", adminDisablesOwner.Code);
    }

    [Fact]
    public async Task Enable_ReactivatesWithoutCreatingSession()
    {
        var (db, service) = Service();
        var owner = SeedUser(db, UserRole.Owner, "owner");
        var cashier = SeedUser(db, UserRole.Cashier, "cashier", active: true, accepted: true);
        var v1 = (await service.GetAsync(owner.Id, cashier.Id, CancellationToken.None)).Version;
        await service.ChangeStatusAsync(owner.Id, cashier.Id,
            new ChangePosUserStatusRequest { IsActive = false, Reason = "pause" }, v1, CancellationToken.None);

        var v2 = (await service.GetAsync(owner.Id, cashier.Id, CancellationToken.None)).Version;
        var enabled = await service.ChangeStatusAsync(owner.Id, cashier.Id,
            new ChangePosUserStatusRequest { IsActive = true }, v2, CancellationToken.None);

        Assert.Equal("Active", enabled.Status);

        using var verify = ContextFor(_tenantId);
        var row = await verify.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == cashier.Id);
        Assert.True(row.IsActive);
        Assert.Null(row.RefreshToken);
        Assert.Null(row.DisableReason);
    }

    [Fact]
    public async Task LegacyUsersWithoutInvitationProvenanceRemainLifecycleEligible()
    {
        var (db, service) = Service();
        var owner = SeedUser(db, UserRole.Owner, "owner");
        var legacy = SeedUser(db, UserRole.Cashier, "legacy", active: true, accepted: false);

        var listed = await service.GetAsync(owner.Id, legacy.Id, CancellationToken.None);
        Assert.Equal("Active", listed.Status);

        var disabled = await service.ChangeStatusAsync(
            owner.Id,
            legacy.Id,
            new ChangePosUserStatusRequest { IsActive = false, Reason = "maintenance" },
            listed.Version,
            CancellationToken.None);
        Assert.Equal("Disabled", disabled.Status);

        var enabled = await service.ChangeStatusAsync(
            owner.Id,
            legacy.Id,
            new ChangePosUserStatusRequest { IsActive = true },
            disabled.Version,
            CancellationToken.None);
        Assert.Equal("Active", enabled.Status);

        var resetToken = await service.IssuePasswordResetAsync(
            owner.Id, legacy.Id, enabled.Version, CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(resetToken));
    }

    // =====================================================================
    // Password reset
    // =====================================================================

    [Fact]
    public async Task PasswordReset_CompletesAndRevokesSessionWithoutCreatingOne()
    {
        var (db, service) = Service();
        var owner = SeedUser(db, UserRole.Owner, "owner");
        var cashier = SeedUser(db, UserRole.Cashier, "cashier", active: true, accepted: true, session: true);

        var version = (await service.GetAsync(owner.Id, cashier.Id, CancellationToken.None)).Version;
        var raw = await service.IssuePasswordResetAsync(owner.Id, cashier.Id, version, CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(raw));

        using (var verify = ContextFor(_tenantId))
        {
            var stored = await verify.PosUserSecurityTokens.IgnoreQueryFilters()
                .SingleAsync(t => t.UserId == cashier.Id && t.Purpose == PosUserSecurityTokenPurpose.PasswordReset);
            Assert.DoesNotContain(raw, stored.TokenHash);
        }

        await service.CompletePasswordResetAsync(raw, "BrandNewPass1!", CancellationToken.None);

        using (var verify = ContextFor(_tenantId))
        {
            var row = await verify.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == cashier.Id);
            Assert.Null(row.RefreshToken);
            Assert.Null(row.RefreshTokenExpiryTime);
            var tokens = await verify.PosUserSecurityTokens.IgnoreQueryFilters()
                .Where(t => t.UserId == cashier.Id && t.Purpose == PosUserSecurityTokenPurpose.PasswordReset)
                .ToListAsync();
            Assert.All(tokens, t => Assert.True(t.ConsumedAtUtc != null || t.RevokedAtUtc != null));
            var audit = await verify.AuditLogs.IgnoreQueryFilters()
                .SingleAsync(a => a.ActionType == PosUserManagementAuditActions.PasswordResetCompleted);
            Assert.DoesNotContain(raw, (audit.BeforeDataJson ?? string.Empty) + (audit.AfterDataJson ?? string.Empty));
        }

        var replay = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.CompletePasswordResetAsync(raw, "AnotherPass1!", CancellationToken.None));
        Assert.Equal("VALIDATION_ERROR", replay.Code);
    }

    [Fact]
    public async Task PasswordReset_RejectsPendingUsersAndAdminChosenPasswords()
    {
        var (db, service) = Service();
        var owner = SeedUser(db, UserRole.Owner, "owner");
        var (pending, _) = await service.InviteAsync(owner.Id, Invite("pend", "pend@e.com"), CancellationToken.None);

        // Pending users must use invitation resend, not password reset.
        var pendingEx = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.IssuePasswordResetAsync(owner.Id, pending.Id, pending.Version, CancellationToken.None));
        Assert.Equal("VALIDATION_ERROR", pendingEx.Code);

        // There is no API accepting an admin-supplied password: the issue
        // endpoint takes only the target id (compile-time guarantee), and
        // completion validates the shared policy server-side.
        var cashierId = SeedActive(db, "cashier9");
        var cashierVersion = (await service.GetAsync(owner.Id, cashierId, CancellationToken.None)).Version;
        var raw = await service.IssuePasswordResetAsync(owner.Id, cashierId, cashierVersion, CancellationToken.None);
        var weak = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.CompletePasswordResetAsync(raw, "short", CancellationToken.None));
        Assert.Equal("VALIDATION_ERROR", weak.Code);
    }

    // =====================================================================
    // Session revocation
    // =====================================================================

    [Fact]
    public async Task RevokeSession_ClearsBindingAndAudits()
    {
        var (db, service) = Service();
        var owner = SeedUser(db, UserRole.Owner, "owner");
        var cashier = SeedUser(db, UserRole.Cashier, "cashier", active: true, accepted: true, session: true);

        var version = (await service.GetAsync(owner.Id, cashier.Id, CancellationToken.None)).Version;
        await service.RevokeSessionAsync(owner.Id, cashier.Id, "lost device", version, CancellationToken.None);

        using var verify = ContextFor(_tenantId);
        var row = await verify.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == cashier.Id);
        Assert.Null(row.RefreshToken);
        Assert.NotNull(await verify.AuditLogs.IgnoreQueryFilters()
            .SingleOrDefaultAsync(a => a.ActionType == PosUserManagementAuditActions.SessionRevoked));
    }

    // =====================================================================
    // Ownership transfer
    // =====================================================================

    [Fact]
    public async Task TransferOwnership_SwapsRolesRevokesBothAndWritesOneAudit()
    {
        var (db, service) = Service();
        var owner = SeedUser(db, UserRole.Owner, "owner", session: true);
        var admin = SeedUser(db, UserRole.Admin, "successor", active: true, accepted: true, session: true);

        var ownerVersion = (await service.GetAsync(owner.Id, owner.Id, CancellationToken.None)).Version;
        var adminVersion = (await service.GetAsync(owner.Id, admin.Id, CancellationToken.None)).Version;
        await service.TransferOwnershipAsync(owner.Id, admin.Id, ownerVersion, adminVersion, CancellationToken.None);

        using var verify = ContextFor(_tenantId);
        var oldOwner = await verify.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == owner.Id);
        var newOwner = await verify.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == admin.Id);
        Assert.Equal(UserRole.Admin, oldOwner.Role);
        Assert.Equal(UserRole.Owner, newOwner.Role);
        Assert.Null(oldOwner.RefreshToken);
        Assert.Null(newOwner.RefreshToken);

        var audits = await verify.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.ActionType == PosUserManagementAuditActions.OwnershipTransferred)
            .ToListAsync();
        var audit = Assert.Single(audits);
        Assert.Equal(owner.Id, audit.UserId);
        Assert.Equal(admin.Id.ToString("D"), audit.EntityId);
        Assert.Contains($"\"oldOwnerId\":\"{owner.Id:D}\"", audit.BeforeDataJson);
        Assert.Contains($"\"newOwnerId\":\"{admin.Id:D}\"", audit.BeforeDataJson);
        Assert.Contains("\"newOwnerRole\":\"Admin\"", audit.BeforeDataJson);
        Assert.Contains($"\"oldOwnerId\":\"{owner.Id:D}\"", audit.AfterDataJson);

        var oldOwnerAudit = await service.GetAuditAsync(admin.Id, owner.Id, 1, 20, CancellationToken.None);
        Assert.Contains(oldOwnerAudit.Items, row =>
            row.ActionType == PosUserManagementAuditActions.OwnershipTransferred);

        var owners = await verify.Users.IgnoreQueryFilters()
            .CountAsync(u => u.TenantId == _tenantId && !u.IsDeleted && u.Role == UserRole.Owner);
        Assert.Equal(1, owners);
    }

    [Fact]
    public async Task TransferOwnership_DeniesNonOwnerSelfAndIneligibleTargets()
    {
        var (db, service) = Service();
        var owner = SeedUser(db, UserRole.Owner, "owner");
        var admin = SeedUser(db, UserRole.Admin, "admin", active: true, accepted: true);
        var cashier = SeedUser(db, UserRole.Cashier, "cashier", active: true, accepted: true);

        var nonOwner = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.TransferOwnershipAsync(admin.Id, cashier.Id, "0", "0", CancellationToken.None));
        Assert.Equal("POS_PERMISSION_FORBIDDEN", nonOwner.Code);

        var ownerVersion = (await service.GetAsync(owner.Id, owner.Id, CancellationToken.None)).Version;
        var self = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.TransferOwnershipAsync(
                owner.Id, owner.Id, ownerVersion, ownerVersion,
                CancellationToken.None));
        Assert.Equal("VALIDATION_ERROR", self.Code);

        var (pending, _) = await service.InviteAsync(owner.Id, Invite("pend", "pend@e.com"), CancellationToken.None);
        var pendingTarget = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.TransferOwnershipAsync(
                owner.Id, pending.Id, ownerVersion,
                pending.Version,
                CancellationToken.None));
        Assert.Equal("VALIDATION_ERROR", pendingTarget.Code);

        var foreign = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.TransferOwnershipAsync(
                owner.Id, Guid.NewGuid(), ownerVersion,
                "0",
                CancellationToken.None));
        Assert.Equal("USER_NOT_FOUND", foreign.Code);
    }

    // =====================================================================
    // Tenant isolation / safe lookup
    // =====================================================================

    [Fact]
    public async Task CrossTenantUserId_ReturnsNotFoundWithoutLeak()
    {
        var (db, service) = Service();
        var owner = SeedUser(db, UserRole.Owner, "owner");

        Guid foreignId;
        using (var other = ContextFor(_otherTenantId))
        {
            var foreign = new User
            {
                Id = Guid.NewGuid(),
                TenantId = _otherTenantId,
                Username = "foreign",
                Email = "foreign@example.com",
                PasswordHash = "hash",
                FirstName = "F",
                LastName = "B",
                Role = UserRole.Cashier,
                IsActive = true,
                InvitationAcceptedAtUtc = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            };
            other.Users.Add(foreign);
            await other.SaveChangesAsync();
            foreignId = foreign.Id;
        }

        var get = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.GetAsync(owner.Id, foreignId, CancellationToken.None));
        Assert.Equal("USER_NOT_FOUND", get.Code);

        var version = (await service.GetAsync(owner.Id, owner.Id, CancellationToken.None)).Version;
        var mutate = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.ChangeStatusAsync(owner.Id, foreignId,
                new ChangePosUserStatusRequest { IsActive = false, Reason = "x" }, version, CancellationToken.None));
        Assert.Equal("USER_NOT_FOUND", mutate.Code);
    }

    // =====================================================================
    // Reads
    // =====================================================================

    [Fact]
    public async Task List_SupportsPagingSearchRoleStatusAndSort()
    {
        var (db, service) = Service();
        var owner = SeedUser(db, UserRole.Owner, "owner");
        SeedUser(db, UserRole.Cashier, "alpha", active: true, accepted: true);
        SeedUser(db, UserRole.Manager, "beta", active: true, accepted: true);
        var (pending, _) = await service.InviteAsync(owner.Id, Invite("pending1", "p1@e.com"), CancellationToken.None);

        var all = await service.ListAsync(owner.Id, new PosUserListQuery { PageSize = 100 }, CancellationToken.None);
        Assert.Equal(4, all.TotalCount);
        Assert.Equal(3, all.ActiveCount);
        Assert.Equal(1, all.PendingCount);
        Assert.Equal(0, all.DisabledCount);
        Assert.Equal(1, all.OwnerAdminCount);
        Assert.DoesNotContain(all.Items.Select(i => i.Username), u => u == "secret");
        foreach (var item in all.Items)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Version));
        }

        var search = await service.ListAsync(owner.Id, new PosUserListQuery { Search = "ALPHA" }, CancellationToken.None);
        Assert.Single(search.Items);

        var role = await service.ListAsync(owner.Id, new PosUserListQuery { Role = "Manager" }, CancellationToken.None);
        Assert.Single(role.Items);
        Assert.Equal("Manager", role.Items[0].Role);

        var pendingOnly = await service.ListAsync(owner.Id, new PosUserListQuery { Status = "Pending" }, CancellationToken.None);
        Assert.Single(pendingOnly.Items);
        Assert.Equal(pending.Id, pendingOnly.Items[0].Id);

        var page = await service.ListAsync(owner.Id, new PosUserListQuery { Page = 2, PageSize = 2, SortBy = "username" }, CancellationToken.None);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(2, page.TotalPages);

        var badRole = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.ListAsync(owner.Id, new PosUserListQuery { Role = "Nope" }, CancellationToken.None));
        Assert.Equal("VALIDATION_ERROR", badRole.Code);
    }

    [Fact]
    public async Task GetRoles_ExposesCatalogWithAssignableFlags()
    {
        var (db, service) = Service();
        var owner = SeedUser(db, UserRole.Owner, "owner");
        var admin = SeedUser(db, UserRole.Admin, "admin", active: true, accepted: true);

        var ownerView = await service.GetRolesAsync(owner.Id, CancellationToken.None);
        Assert.Equal(5, ownerView.Count);
        Assert.DoesNotContain(ownerView, r => r.Name == "Owner" && r.AssignableByActor);
        Assert.Contains(ownerView, r => r.Name == "Admin" && r.AssignableByActor);
        Assert.All(ownerView, r => Assert.NotEmpty(r.Permissions));

        var adminView = await service.GetRolesAsync(admin.Id, CancellationToken.None);
        Assert.DoesNotContain(adminView, r => (r.Name == "Owner" || r.Name == "Admin") && r.AssignableByActor);
        Assert.Contains(adminView, r => r.Name == "Manager" && r.AssignableByActor);
    }

    [Fact]
    public async Task GetAudit_ReturnsTenantScopedUserSubset()
    {
        var (db, service) = Service();
        var owner = SeedUser(db, UserRole.Owner, "owner");
        var (invited, _) = await service.InviteAsync(owner.Id, Invite("aud", "aud@e.com"), CancellationToken.None);

        var audit = await service.GetAuditAsync(owner.Id, invited.Id, 1, 20, CancellationToken.None);
        Assert.Equal(1, audit.TotalCount);
        Assert.Equal(PosUserManagementAuditActions.Invited, audit.Items[0].ActionType);
        Assert.Equal(_tenantId, audit.Items[0].TenantId);

        var missing = await Assert.ThrowsAsync<PosUserManagementException>(() =>
            service.GetAuditAsync(owner.Id, Guid.NewGuid(), 1, 20, CancellationToken.None));
        Assert.Equal("USER_NOT_FOUND", missing.Code);
    }

    [Fact]
    public void IdentityNormalizer_RejectsValuesPostgreSqlCannotNormalizeEquivalently()
    {
        Assert.Throws<ArgumentException>(() => PosIdentityNormalizer.NormalizeUsername("\tuser\t"));
        Assert.Throws<ArgumentException>(() => PosIdentityNormalizer.NormalizeUsername("مستخدم"));
        Assert.Throws<ArgumentException>(() => PosIdentityNormalizer.NormalizeEmail("ı@example.com"));
        Assert.Equal("USER", PosIdentityNormalizer.NormalizeUsername(" user "));
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private (ApplicationDbContext Db, PosUserManagementService Service) Service()
    {
        var db = ContextFor(_tenantId);
        var http = new DefaultHttpContext();
        var service = new PosUserManagementService(
            db,
            new StubTenantContext(_tenantId),
            new PasswordHasher<User>(),
            new HttpContextAccessor { HttpContext = http },
            Options.Create(new PosUserManagementOptions()),
            NullLogger<PosUserManagementService>.Instance);
        return (db, service);
    }

    private ApplicationDbContext ContextFor(Guid tenantId)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new PosUserManagementSqliteDbContext(options, tenantContext);
    }

    /// <summary>
    /// Test-only context: identical model except PostgreSQL-dialect CHECK
    /// constraints (regex `~`, `E''` escapes, `::` casts, REGEXP_REPLACE)
    /// which SQLite cannot parse. All portable constraints — including the
    /// Users/PosUserSecurityTokens authority constraints — stay enforced.
    /// Pre-existing repo condition: full-model SQLite EnsureCreated is
    /// broken on the pristine tree by the same PG-dialect constraints
    /// (proven: CommerceHubVendorFinancialControlServiceRelationalTests
    /// fails 5/5 without this work); PostgreSQL proof stays on the SOL lane.
    /// </summary>
    private sealed class PosUserManagementSqliteDbContext(
        DbContextOptions<ApplicationDbContext> options,
        PointOfSale.Core.Interfaces.ITenantContext tenantContext)
        : ApplicationDbContext(options, tenantContext)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var check in entityType.GetCheckConstraints().ToList())
                {
                    if (check.Sql.Contains('~') ||
                        check.Sql.Contains("E'", StringComparison.Ordinal) ||
                        check.Sql.Contains("::", StringComparison.Ordinal) ||
                        check.Sql.Contains("regexp_replace", StringComparison.OrdinalIgnoreCase))
                    {
                        ((Microsoft.EntityFrameworkCore.Metadata.IMutableEntityType)entityType)
                            .RemoveCheckConstraint(check.Name);
                    }
                }
            }
        }
    }

    private User SeedUser(
        ApplicationDbContext db, UserRole role, string prefix,
        bool active = false, bool accepted = false, bool session = false)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Username = $"{prefix}-{Guid.NewGuid():N}",
            Email = $"{prefix}-{Guid.NewGuid():N}@example.com",
            PasswordHash = "hash",
            FirstName = "Seed",
            LastName = "User",
            Role = role,
            IsActive = active || role == UserRole.Owner,
            InvitationAcceptedAtUtc = accepted || role == UserRole.Owner ? DateTime.UtcNow : null,
            RefreshToken = session ? "session-binding" : null,
            RefreshTokenExpiryTime = session ? DateTime.UtcNow.AddDays(1) : null,
            CreatedAt = DateTime.UtcNow,
        };
        db.Users.Add(user);
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return user;
    }

    private Guid SeedActive(ApplicationDbContext db, string prefix)
    {
        using var setup = ContextFor(_tenantId);
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Username = $"{prefix}-{Guid.NewGuid():N}",
            Email = $"{prefix}-{Guid.NewGuid():N}@example.com",
            PasswordHash = "hash",
            FirstName = "Seed",
            LastName = "User",
            Role = UserRole.Cashier,
            IsActive = true,
            InvitationAcceptedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
        setup.Users.Add(user);
        setup.SaveChanges();
        return user.Id;
    }

    private static InvitePosUserRequest Invite(string username, string email, UserRole role = UserRole.Cashier) =>
        new()
        {
            FirstName = "First",
            LastName = "Last",
            Username = username,
            Email = email.Contains('@') ? email : $"{email}@example.com",
            Role = role.ToString(),
        };

    private static InvitePosUserRequest Invite(string username, string email, string role) =>
        new()
        {
            FirstName = "First",
            LastName = "Last",
            Username = username,
            Email = email.Contains('@') ? email : $"{email}@example.com",
            Role = role,
        };

    private static UpdatePosUserProfileRequest Profile(string first, string last, string email) =>
        new() { FirstName = first, LastName = last, Email = email };

    private static string PosUserManagementService_Hash(string raw)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw)));
    }

    private sealed class StubTenantContext(Guid tenantId) : PointOfSale.Core.Interfaces.ITenantContext
    {
        public Guid? TenantId => tenantId;
    }
}
