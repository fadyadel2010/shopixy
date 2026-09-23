using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PointOfSale.API.DTOs;
using PointOfSale.API.DTOs.Users;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Security;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.Services.Users;

public sealed class PosUserManagementService : IPosUserManagementService
{
    private static readonly JsonSerializerOptions AuditJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlySet<UserRole> OwnerManageable =
        new HashSet<UserRole> { UserRole.Admin, UserRole.Manager, UserRole.Cashier, UserRole.Warehouse };
    private static readonly IReadOnlySet<UserRole> AdminManageable =
        new HashSet<UserRole> { UserRole.Manager, UserRole.Cashier, UserRole.Warehouse };

    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<PosUserManagementService> _logger;
    private readonly PosUserManagementOptions _options;

    public PosUserManagementService(
        ApplicationDbContext db,
        ITenantContext tenantContext,
        IPasswordHasher<User> passwordHasher,
        IHttpContextAccessor httpContextAccessor,
        IOptions<PosUserManagementOptions> options,
        ILogger<PosUserManagementService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _passwordHasher = passwordHasher;
        _httpContextAccessor = httpContextAccessor;
        _options = options.Value;
        _logger = logger;
    }

    // =====================================================================
    // Reads
    // =====================================================================

    public async Task<PosUserListResultDto> ListAsync(
        Guid actorUserId, PosUserListQuery query, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        await RequireViewerAsync(actorUserId, tenantId, cancellationToken);

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 20 : query.PageSize, 1, 100);

        UserRole? roleFilter = null;
        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            if (!Enum.TryParse<UserRole>(query.Role.Trim(), ignoreCase: true, out var parsed) ||
                !Enum.IsDefined(parsed))
            {
                throw PosUserManagementException.Validation("Unknown role filter.");
            }

            roleFilter = parsed;
        }

        var statusFilter = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status.Trim();
        if (statusFilter != null && !PosUserStatuses.All.Contains(statusFilter))
        {
            throw PosUserManagementException.Validation("Unknown status filter.");
        }

        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim().ToLowerInvariant();

        var baseQuery = _db.Users.AsNoTracking();
        var includeRemoved = string.Equals(statusFilter, PosUserStatuses.Removed, StringComparison.OrdinalIgnoreCase);
        if (includeRemoved)
        {
            baseQuery = baseQuery.IgnoreQueryFilters()
                .Where(u => u.TenantId == tenantId && u.IsDeleted);
        }
        else
        {
            baseQuery = baseQuery.Where(u => u.TenantId == tenantId);
        }

        if (roleFilter.HasValue)
        {
            baseQuery = baseQuery.Where(u => u.Role == roleFilter.Value);
        }

        if (search != null)
        {
            baseQuery = baseQuery.Where(u =>
                u.Username.ToLower().Contains(search) ||
                u.Email.ToLower().Contains(search) ||
                u.FirstName.ToLower().Contains(search) ||
                u.LastName.ToLower().Contains(search) ||
                (u.Phone != null && u.Phone.ToLower().Contains(search)));
        }

        if (query.InvitedFrom.HasValue)
        {
            var from = query.InvitedFrom.Value.ToUniversalTime();
            baseQuery = baseQuery.Where(u => u.InvitedAtUtc >= from);
        }

        if (query.InvitedTo.HasValue)
        {
            var to = query.InvitedTo.Value.ToUniversalTime();
            baseQuery = baseQuery.Where(u => u.InvitedAtUtc <= to);
        }

        if (query.LastLoginFrom.HasValue)
        {
            var from = query.LastLoginFrom.Value.ToUniversalTime();
            baseQuery = baseQuery.Where(u => u.LastLoginAt >= from);
        }

        if (query.LastLoginTo.HasValue)
        {
            var to = query.LastLoginTo.Value.ToUniversalTime();
            baseQuery = baseQuery.Where(u => u.LastLoginAt <= to);
        }

        // Tenant staff scale is small; statuses Pending vs InvitationExpired
        // need the valid-token set, so candidates are materialized once.
        var candidates = await baseQuery.ToListAsync(cancellationToken);
        var validInvitationUserIds = await GetUserIdsWithValidTokensAsync(
            tenantId, candidates.Select(u => u.Id).ToList(),
            PosUserSecurityTokenPurpose.Invitation, cancellationToken);

        var withStatus = candidates
            .Select(u => (User: u, Status: DeriveStatus(u, validInvitationUserIds)))
            .ToList();

        if (statusFilter != null && !includeRemoved)
        {
            withStatus = withStatus
                .Where(x => string.Equals(x.Status, statusFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var descending = string.Equals(query.SortDir?.Trim(), "desc", StringComparison.OrdinalIgnoreCase);
        withStatus = (query.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "email" => descending
                ? withStatus.OrderByDescending(x => x.User.Email).ToList()
                : withStatus.OrderBy(x => x.User.Email).ToList(),
            "role" => descending
                ? withStatus.OrderByDescending(x => x.User.Role).ThenBy(x => x.User.Username).ToList()
                : withStatus.OrderBy(x => x.User.Role).ThenBy(x => x.User.Username).ToList(),
            "status" => descending
                ? withStatus.OrderByDescending(x => x.Status).ThenBy(x => x.User.Username).ToList()
                : withStatus.OrderBy(x => x.Status).ThenBy(x => x.User.Username).ToList(),
            "lastlogin" or "lastloginat" => descending
                ? withStatus.OrderByDescending(x => x.User.LastLoginAt).ToList()
                : withStatus.OrderBy(x => x.User.LastLoginAt).ToList(),
            "invitedat" or "invited" => descending
                ? withStatus.OrderByDescending(x => x.User.InvitedAtUtc).ToList()
                : withStatus.OrderBy(x => x.User.InvitedAtUtc).ToList(),
            _ => descending
                ? withStatus.OrderByDescending(x => x.User.Username).ToList()
                : withStatus.OrderBy(x => x.User.Username).ToList(),
        };

        var totalCount = withStatus.Count;
        var items = withStatus
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => ToListItem(x.User, x.Status))
            .ToList();

        var summary = await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId)
            .GroupBy(u => 1)
            .Select(g => new
            {
                Active = g.Count(u => u.IsActive),
                Pending = g.Count(u => !u.IsActive && u.InvitedAtUtc != null && u.InvitationAcceptedAtUtc == null),
                Disabled = g.Count(u => !u.IsActive && (u.InvitedAtUtc == null || u.InvitationAcceptedAtUtc != null)),
                OwnerAdmin = g.Count(u => u.Role == UserRole.Owner || u.Role == UserRole.Admin),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return new PosUserListResultDto
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize),
            ActiveCount = summary?.Active ?? 0,
            PendingCount = summary?.Pending ?? 0,
            DisabledCount = summary?.Disabled ?? 0,
            OwnerAdminCount = summary?.OwnerAdmin ?? 0,
        };
    }

    public async Task<PosUserDetailDto> GetAsync(
        Guid actorUserId, Guid userId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        await RequireViewerAsync(actorUserId, tenantId, cancellationToken);

        var user = await FindTenantUserAsync(userId, tenantId, cancellationToken)
            ?? throw PosUserManagementException.NotFound();

        var valid = await GetUserIdsWithValidTokensAsync(
            tenantId, new List<Guid> { user.Id },
            PosUserSecurityTokenPurpose.Invitation, cancellationToken);

        return ToDetail(user, DeriveStatus(user, valid));
    }

    public async Task<IReadOnlyList<PosRoleDto>> GetRolesAsync(
        Guid actorUserId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var actor = await RequireViewerAsync(actorUserId, tenantId, cancellationToken);
        var assignable = ManageableRoles(actor.Role);

        return Enum.GetValues<UserRole>()
            .Select(role => new PosRoleDto
            {
                Name = role.ToString(),
                Permissions = PosRolePermissionCatalog.GetPermissions(role)
                    .OrderBy(p => p, StringComparer.Ordinal)
                    .ToArray(),
                AssignableByActor = assignable.Contains(role),
            })
            .ToList();
    }

    public async Task<PagedResult<AuditLogItem>> GetAuditAsync(
        Guid actorUserId, Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        await RequireViewerAsync(actorUserId, tenantId, cancellationToken);

        _ = await FindTenantUserAsync(userId, tenantId, cancellationToken)
            ?? throw PosUserManagementException.NotFound();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 100);
        var entityId = userId.ToString("D");

        var userManagementActions = PosUserManagementAuditActions.All.ToArray();
        var query = _db.AuditLogs.AsNoTracking()
            .Where(a => a.TenantId == tenantId &&
                        a.EntityType == "User" &&
                        a.Source == "UsersManagement" &&
                        userManagementActions.Contains(a.ActionType) &&
                        (a.EntityId == entityId ||
                         (a.ActionType == PosUserManagementAuditActions.OwnershipTransferred &&
                          a.UserId == userId)));

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(a => a.TimestampUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditLogItem>
        {
            Items = rows.Select(a => new AuditLogItem
            {
                TenantId = a.TenantId,
                TimestampUtc = a.TimestampUtc,
                UserId = a.UserId,
                UserNameSnapshot = a.UserNameSnapshot,
                ActionType = a.ActionType,
                EntityType = a.EntityType,
                EntityId = a.EntityId,
                Description = a.Description,
                BeforeDataJson = a.BeforeDataJson,
                AfterDataJson = a.AfterDataJson,
                IpAddress = a.IpAddress,
                UserAgent = a.UserAgent,
                ReferenceNumber = a.ReferenceNumber,
                Source = a.Source,
                SuccessFlag = a.SuccessFlag,
            }).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize),
        };
    }

    // =====================================================================
    // Invitation lifecycle
    // =====================================================================

    public async Task<(PosUserDetailDto User, string RawInvitationToken)> InviteAsync(
        Guid actorUserId, InvitePosUserRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var actor = await RequireActorAsync(actorUserId, tenantId, cancellationToken);

        var role = ParseRole(request.Role);
        if (role == UserRole.Owner)
        {
            throw PosUserManagementException.OwnerInvariant(
                "Owner can only be assigned through ownership transfer.");
        }

        RequireAssignable(actor.Role, role);

        var username = (request.Username ?? string.Empty).Trim();
        var email = (request.Email ?? string.Empty).Trim();
        var firstName = (request.FirstName ?? string.Empty).Trim();
        var lastName = (request.LastName ?? string.Empty).Trim();
        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();

        if (username.Length == 0 || username.Length > PosIdentityNormalizer.MaxUsernameLength)
        {
            throw PosUserManagementException.Validation("Username is required (max 100 characters).");
        }

        if (firstName.Length == 0 || lastName.Length == 0)
        {
            throw PosUserManagementException.Validation("First name and last name are required.");
        }

        string normalizedUsername;
        string normalizedEmail;
        try
        {
            normalizedUsername = PosIdentityNormalizer.NormalizeUsername(username);
            normalizedEmail = PosIdentityNormalizer.NormalizeEmail(email);
        }
        catch (ArgumentException)
        {
            throw PosUserManagementException.Validation("Username or email is invalid.");
        }

        await RequireUniqueIdentityAsync(null, normalizedUsername, normalizedEmail, cancellationToken);

        var now = DateTime.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Username = username,
            Email = email,
            NormalizedUsername = normalizedUsername,
            NormalizedEmail = normalizedEmail,
            FirstName = firstName,
            LastName = lastName,
            Phone = phone,
            Role = role,
            IsActive = false,
            EmailVerificationStatus = EmailVerificationStatus.Unverified,
            InvitedAtUtc = now,
            InvitedByUserId = actor.Id,
            CreatedAt = now,
        };
        // Unknowable random password: the account cannot authenticate until
        // invitation acceptance sets a real password.
        user.PasswordHash = _passwordHasher.HashPassword(
            user, Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)));

        var (rawToken, token) = CreateToken(
            tenantId, user.Id, PosUserSecurityTokenPurpose.Invitation,
            now.AddHours(NormalizeInvitationExpiryHours()), actor.Id, now);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            _db.Users.Add(user);
            _db.PosUserSecurityTokens.Add(token);
            AddAudit(
                tenantId, actor.Id, actor.Username,
                PosUserManagementAuditActions.Invited, user.Id,
                $"Invited {username} as {role}.",
                null,
                new
                {
                    usernameFingerprint = IdentityFingerprint(normalizedUsername),
                    emailFingerprint = IdentityFingerprint(normalizedEmail),
                    role = role.ToString(),
                    expiresAtUtc = token.ExpiresAtUtc
                });
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw PosUserManagementException.IdentityExists();
        }

        _logger.LogInformation(
            "POS user invited | TenantId: {TenantId} | TargetUserId: {UserId} | Role: {Role} | Actor: {Actor}",
            tenantId, user.Id, role, actor.Id);

        var valid = new HashSet<Guid> { user.Id };
        return (ToDetail(user, DeriveStatus(user, valid)), rawToken);
    }

    public async Task<(PosUserDetailDto User, string RawInvitationToken)> ResendInvitationAsync(
        Guid actorUserId, Guid userId, string? ifMatch, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var actor = await RequireActorAsync(actorUserId, tenantId, cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await LockTenantUsersAsync(tenantId, [userId], cancellationToken);
        var target = await RequireManageableTargetAsync(actor, userId, tenantId, "resend invitations for", cancellationToken);
        RequireVersion(target, ifMatch);

        if (!IsPendingInvitation(target))
        {
            throw PosUserManagementException.Validation("Only pending invitations can be resent.");
        }

        var now = DateTime.UtcNow;
        var revoked = await RevokeOutstandingTokensAsync(
            tenantId, target.Id, PosUserSecurityTokenPurpose.Invitation, now, cancellationToken);
        var (rawToken, token) = CreateToken(
            tenantId, target.Id, PosUserSecurityTokenPurpose.Invitation,
            now.AddHours(NormalizeInvitationExpiryHours()), actor.Id, now);

        try
        {
            TouchUser(target, now);
            _db.PosUserSecurityTokens.Add(token);
            AddAudit(
                tenantId, actor.Id, actor.Username,
                PosUserManagementAuditActions.InvitationResent, target.Id,
                $"Resent invitation to {target.Username}.",
                null,
                new { revokedTokens = revoked, expiresAtUtc = token.ExpiresAtUtc });
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw PosUserManagementException.VersionConflict();
        }

        var valid = new HashSet<Guid> { target.Id };
        return (ToDetail(target, DeriveStatus(target, valid)), rawToken);
    }

    public async Task RevokeInvitationAsync(
        Guid actorUserId, Guid userId, string? reason, string? ifMatch, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var actor = await RequireActorAsync(actorUserId, tenantId, cancellationToken);
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await LockTenantUsersAsync(tenantId, [userId], cancellationToken);
        var target = await RequireManageableTargetAsync(actor, userId, tenantId, "revoke invitations for", cancellationToken);
        RequireVersion(target, ifMatch);

        if (!IsPendingInvitation(target))
        {
            throw PosUserManagementException.Validation("Only pending invitations can be revoked.");
        }

        var now = DateTime.UtcNow;
        var revoked = await RevokeOutstandingTokensAsync(
            tenantId, target.Id, PosUserSecurityTokenPurpose.Invitation, now, cancellationToken);
        if (revoked == 0)
        {
            throw PosUserManagementException.Validation("There is no pending invitation to revoke.");
        }

        try
        {
            TouchUser(target, now);
            AddAudit(
                tenantId, actor.Id, actor.Username,
                PosUserManagementAuditActions.InvitationRevoked, target.Id,
                $"Revoked invitation for {target.Username}.",
                null,
                new { revokedTokens = revoked, reason = TrimReason(reason) });
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw PosUserManagementException.VersionConflict();
        }
    }

    public async Task AcceptInvitationAsync(
        string rawToken, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            throw PosUserManagementException.InvalidToken();
        }

        if (!PosPasswordPolicy.IsValid(password, out _))
        {
            throw PosUserManagementException.Validation(
                $"Password must be {PosPasswordPolicy.MinLength}-{PosPasswordPolicy.MaxLength} characters.");
        }

        var now = DateTime.UtcNow;
        var candidate = await FindCeremonyTokenCandidateAsync(
            rawToken, PosUserSecurityTokenPurpose.Invitation, cancellationToken);
        if (candidate == null)
        {
            throw PosUserManagementException.InvalidToken();
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await LockTenantUsersAsync(candidate.Value.TenantId, [candidate.Value.UserId], cancellationToken);
        var token = await FindCeremonyTokenAsync(
            rawToken, PosUserSecurityTokenPurpose.Invitation, cancellationToken);
        var user = token?.User;
        if (token == null || user == null ||
            user.IsDeleted || user.TenantId != token.TenantId ||
            user.IsActive || user.InvitationAcceptedAtUtc.HasValue ||
            !await TenantLineageAuthority.IsActiveTenantAsync(_db, token.TenantId, cancellationToken))
        {
            throw PosUserManagementException.InvalidToken();
        }

        user.PasswordHash = _passwordHasher.HashPassword(user, password);
        user.EmailVerificationStatus = EmailVerificationStatus.Verified;
        user.EmailVerifiedAtUtc = now;
        user.InvitationAcceptedAtUtc = now;
        user.IsActive = true;

        var outstandingInvitations = await _db.PosUserSecurityTokens.IgnoreQueryFilters()
            .Where(t => t.UserId == user.Id &&
                        t.Purpose == PosUserSecurityTokenPurpose.Invitation &&
                        t.ConsumedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var outstanding in outstandingInvitations)
        {
            outstanding.ConsumedAtUtc = now;
        }

        AddAudit(
            user.TenantId, user.Id, user.Username,
            PosUserManagementAuditActions.InvitationAccepted, user.Id,
            $"Invitation accepted by {user.Username}.",
            null,
            new { acceptedAtUtc = now });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // No POS session is created: the user signs in through normal login.
        _logger.LogInformation(
            "POS invitation accepted | TenantId: {TenantId} | UserId: {UserId}",
            user.TenantId, user.Id);
    }

    // =====================================================================
    // Profile / role / status
    // =====================================================================

    public async Task<PosUserDetailDto> UpdateProfileAsync(
        Guid actorUserId, Guid userId, UpdatePosUserProfileRequest request, string? ifMatch,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var actor = await RequireActorAsync(actorUserId, tenantId, cancellationToken);
        var target = await RequireManageableTargetAsync(actor, userId, tenantId, "edit the profile of", cancellationToken);
        RequireVersion(target, ifMatch);

        var firstName = (request.FirstName ?? string.Empty).Trim();
        var lastName = (request.LastName ?? string.Empty).Trim();
        var email = (request.Email ?? string.Empty).Trim();
        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();

        if (firstName.Length == 0 || lastName.Length == 0)
        {
            throw PosUserManagementException.Validation("First name and last name are required.");
        }

        string normalizedEmail;
        try
        {
            normalizedEmail = PosIdentityNormalizer.NormalizeEmail(email);
        }
        catch (ArgumentException)
        {
            throw PosUserManagementException.Validation("Email is invalid.");
        }

        var emailChanged = !string.Equals(target.NormalizedEmail, normalizedEmail, StringComparison.Ordinal);
        if (emailChanged)
        {
            await RequireUniqueIdentityAsync(target.Id, target.NormalizedUsername, normalizedEmail, cancellationToken);
        }

        var before = new { target.FirstName, target.LastName, target.Phone, target.Email };

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            target.FirstName = firstName;
            target.LastName = lastName;
            target.Phone = phone;
            target.Email = email;
            target.NormalizedEmail = normalizedEmail;
            if (emailChanged)
            {
                target.EmailVerificationStatus = EmailVerificationStatus.Unverified;
                target.EmailVerifiedAtUtc = null;
                await RevokeOutstandingTokensAsync(
                    tenantId, target.Id, PosUserSecurityTokenPurpose.Invitation,
                    DateTime.UtcNow, cancellationToken);
                await RevokeOutstandingTokensAsync(
                    tenantId, target.Id, PosUserSecurityTokenPurpose.PasswordReset,
                    DateTime.UtcNow, cancellationToken);
            }

            AddAudit(
                tenantId, actor.Id, actor.Username,
                PosUserManagementAuditActions.ProfileChanged, target.Id,
                $"Profile changed for {target.Username}.",
                before,
                new { firstName, lastName, phone, email });
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw PosUserManagementException.IdentityExists();
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw PosUserManagementException.VersionConflict();
        }

        return await GetAsync(actorUserId, target.Id, cancellationToken);
    }

    public async Task<PosUserDetailDto> ChangeRoleAsync(
        Guid actorUserId, Guid userId, ChangePosUserRoleRequest request, string? ifMatch,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var actor = await RequireActorAsync(actorUserId, tenantId, cancellationToken);
        var target = await RequireManageableTargetAsync(actor, userId, tenantId, "change the role of", cancellationToken);
        RequireVersion(target, ifMatch);

        var newRole = ParseRole(request.Role);
        if (newRole == UserRole.Owner)
        {
            throw PosUserManagementException.OwnerInvariant(
                "Owner can only be assigned through ownership transfer.");
        }

        RequireAssignable(actor.Role, newRole);

        if (target.Role == newRole)
        {
            return await GetAsync(actorUserId, target.Id, cancellationToken);
        }

        var beforeRole = target.Role;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            target.Role = newRole;
            // Role change revokes the target's current POS session at the same
            // boundary (the interceptor also enforces this on Role change).
            target.RefreshToken = null;
            target.RefreshTokenExpiryTime = null;

            AddAudit(
                tenantId, actor.Id, actor.Username,
                PosUserManagementAuditActions.RoleChanged, target.Id,
                $"Role changed for {target.Username} from {beforeRole} to {newRole}.",
                new { role = beforeRole.ToString() },
                new { role = newRole.ToString() });
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw PosUserManagementException.VersionConflict();
        }

        _logger.LogInformation(
            "POS user role changed | TenantId: {TenantId} | Target: {UserId} | {Before} -> {After} | Actor: {Actor}",
            tenantId, target.Id, beforeRole, newRole, actor.Id);

        return await GetAsync(actorUserId, target.Id, cancellationToken);
    }

    public async Task<PosUserDetailDto> ChangeStatusAsync(
        Guid actorUserId, Guid userId, ChangePosUserStatusRequest request, string? ifMatch,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var actor = await RequireActorAsync(actorUserId, tenantId, cancellationToken);
        var target = await RequireManageableTargetAsync(actor, userId, tenantId, "change the status of", cancellationToken);
        RequireVersion(target, ifMatch);

        if (!request.IsActive)
        {
            var reason = TrimReason(request.Reason);
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw PosUserManagementException.Validation("A reason is required to disable a user.");
            }

            if (target.Role == UserRole.Owner)
            {
                throw PosUserManagementException.OwnerInvariant(
                    "Owner cannot be disabled through status management. Use ownership transfer.");
            }

            if (!target.IsActive)
            {
                return await GetAsync(actorUserId, target.Id, cancellationToken);
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                target.IsActive = false;
                target.DisabledAtUtc = DateTime.UtcNow;
                target.DisabledByUserId = actor.Id;
                target.DisableReason = reason;
                target.RefreshToken = null;
                target.RefreshTokenExpiryTime = null;

                AddAudit(
                    tenantId, actor.Id, actor.Username,
                    PosUserManagementAuditActions.Disabled, target.Id,
                    $"Disabled {target.Username}. Reason: {reason}",
                    new { isActive = true },
                    new { isActive = false, reason });
                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw PosUserManagementException.VersionConflict();
            }

            return await GetAsync(actorUserId, target.Id, cancellationToken);
        }

        if (IsPendingInvitation(target))
        {
            throw PosUserManagementException.Validation(
                "Pending invitations cannot be enabled. Resend the invitation instead.");
        }

        if (target.IsActive)
        {
            return await GetAsync(actorUserId, target.Id, cancellationToken);
        }

        await using (var transaction = await _db.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                target.IsActive = true;
                target.DisabledAtUtc = null;
                target.DisabledByUserId = null;
                target.DisableReason = null;

                AddAudit(
                    tenantId, actor.Id, actor.Username,
                    PosUserManagementAuditActions.Enabled, target.Id,
                    $"Enabled {target.Username}.",
                    new { isActive = false },
                    new { isActive = true });
                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw PosUserManagementException.VersionConflict();
            }
        }

        // No session is issued: the user signs in normally.
        return await GetAsync(actorUserId, target.Id, cancellationToken);
    }

    // =====================================================================
    // Password reset / session revocation
    // =====================================================================

    public async Task<string> IssuePasswordResetAsync(
        Guid actorUserId, Guid userId, string? ifMatch, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var actor = await RequireActorAsync(actorUserId, tenantId, cancellationToken);
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await LockTenantUsersAsync(tenantId, [userId], cancellationToken);
        var target = await RequireManageableTargetAsync(actor, userId, tenantId, "reset the password of", cancellationToken);
        RequireVersion(target, ifMatch);

        if (IsPendingInvitation(target))
        {
            throw PosUserManagementException.Validation(
                "Pending invitations cannot use password reset. Resend the invitation instead.");
        }

        var now = DateTime.UtcNow;
        var revoked = await RevokeOutstandingTokensAsync(
            tenantId, target.Id, PosUserSecurityTokenPurpose.PasswordReset, now, cancellationToken);
        var (rawToken, token) = CreateToken(
            tenantId, target.Id, PosUserSecurityTokenPurpose.PasswordReset,
            now.AddMinutes(NormalizePasswordResetExpiryMinutes()), actor.Id, now);

        try
        {
            TouchUser(target, now);
            _db.PosUserSecurityTokens.Add(token);
            AddAudit(
                tenantId, actor.Id, actor.Username,
                PosUserManagementAuditActions.PasswordResetIssued, target.Id,
                $"Password reset issued for {target.Username}.",
                null,
                new { revokedTokens = revoked, expiresAtUtc = token.ExpiresAtUtc });
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw PosUserManagementException.VersionConflict();
        }

        return rawToken;
    }

    public async Task CompletePasswordResetAsync(
        string rawToken, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            throw PosUserManagementException.InvalidToken();
        }

        if (!PosPasswordPolicy.IsValid(password, out _))
        {
            throw PosUserManagementException.Validation(
                $"Password must be {PosPasswordPolicy.MinLength}-{PosPasswordPolicy.MaxLength} characters.");
        }

        var now = DateTime.UtcNow;
        var candidate = await FindCeremonyTokenCandidateAsync(
            rawToken, PosUserSecurityTokenPurpose.PasswordReset, cancellationToken);
        if (candidate == null)
        {
            throw PosUserManagementException.InvalidToken();
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await LockTenantUsersAsync(candidate.Value.TenantId, [candidate.Value.UserId], cancellationToken);
        var token = await FindCeremonyTokenAsync(
            rawToken, PosUserSecurityTokenPurpose.PasswordReset, cancellationToken);
        var user = token?.User;
        if (token == null || user == null ||
            user.IsDeleted || user.TenantId != token.TenantId ||
            !await TenantLineageAuthority.IsActiveTenantAsync(_db, token.TenantId, cancellationToken))
        {
            throw PosUserManagementException.InvalidToken();
        }

        user.PasswordHash = _passwordHasher.HashPassword(user, password);
        user.RefreshToken = null;
        user.RefreshTokenExpiryTime = null;

        token.ConsumedAtUtc = now;
        var otherOutstanding = await _db.PosUserSecurityTokens.IgnoreQueryFilters()
            .Where(t => t.Id != token.Id &&
                        t.UserId == user.Id &&
                        t.Purpose == PosUserSecurityTokenPurpose.PasswordReset &&
                        t.ConsumedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var other in otherOutstanding)
        {
            other.RevokedAtUtc = now;
        }

        AddAudit(
            user.TenantId, user.Id, user.Username,
            PosUserManagementAuditActions.PasswordResetCompleted, user.Id,
            $"Password reset completed for {user.Username}.",
            null,
            new { sessionRevoked = true });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "POS password reset completed | TenantId: {TenantId} | UserId: {UserId}",
            user.TenantId, user.Id);
    }

    public async Task RevokeSessionAsync(
        Guid actorUserId, Guid userId, string? reason, string? ifMatch, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var actor = await RequireActorAsync(actorUserId, tenantId, cancellationToken);
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await LockTenantUsersAsync(tenantId, [userId], cancellationToken);
        var target = await RequireManageableTargetAsync(actor, userId, tenantId, "revoke the session of", cancellationToken);
        RequireVersion(target, ifMatch);

        try
        {
            target.RefreshToken = null;
            target.RefreshTokenExpiryTime = null;
            TouchUser(target, DateTime.UtcNow);

            AddAudit(
                tenantId, actor.Id, actor.Username,
                PosUserManagementAuditActions.SessionRevoked, target.Id,
                $"Current POS session revoked for {target.Username}.",
                null,
                new { reason = TrimReason(reason) });
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw PosUserManagementException.VersionConflict();
        }
    }

    // =====================================================================
    // Ownership transfer: one atomic transaction, never two role calls.
    // =====================================================================

    public async Task TransferOwnershipAsync(
        Guid actorUserId, Guid targetUserId, string? actorIfMatch, string? targetVersion,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await LockTenantUsersAsync(tenantId, [actorUserId, targetUserId], cancellationToken);

        try
        {
            var actor = await _db.Users
                .FirstOrDefaultAsync(u => u.Id == actorUserId && u.TenantId == tenantId, cancellationToken);
            if (actor == null || actor.IsDeleted || !actor.IsActive || actor.Role != UserRole.Owner)
            {
                throw new PosUserManagementException(
                    "POS_PERMISSION_FORBIDDEN", StatusCodes.Status403Forbidden, "Only the current Owner can transfer ownership.");
            }

            var target = await _db.Users
                .FirstOrDefaultAsync(u => u.Id == targetUserId && u.TenantId == tenantId, cancellationToken)
                ?? throw PosUserManagementException.NotFound();

            RequireVersion(actor, actorIfMatch);
            RequireVersion(target, targetVersion);

            if (target.Id == actor.Id)
            {
                throw PosUserManagementException.Validation("Ownership cannot be transferred to yourself.");
            }

            if (target.IsDeleted || !target.IsActive || IsPendingInvitation(target))
            {
                throw PosUserManagementException.Validation(
                    "Ownership can only transfer to an active eligible user.");
            }

            if (target.Role == UserRole.Owner)
            {
                throw PosUserManagementException.OwnerInvariant("The target is already an Owner.");
            }

            var targetBeforeRole = target.Role;

            actor.Role = UserRole.Admin;
            actor.RefreshToken = null;
            actor.RefreshTokenExpiryTime = null;
            target.RefreshToken = null;
            target.RefreshTokenExpiryTime = null;

            // Demote first and flush inside the transaction: the partial
            // sole-Owner index caps owners at one, so the swap must pass
            // through zero owners (index-safe), never two.
            await _db.SaveChangesAsync(cancellationToken);

            target.Role = UserRole.Owner;

            AddAudit(
                tenantId, actor.Id, actor.Username,
                PosUserManagementAuditActions.OwnershipTransferred, target.Id,
                $"Ownership transferred from {actor.Username} to {target.Username}.",
                new
                {
                    oldOwnerId = actor.Id,
                    newOwnerId = target.Id,
                    oldOwnerRole = UserRole.Owner.ToString(),
                    newOwnerRole = targetBeforeRole.ToString()
                },
                new
                {
                    oldOwnerId = actor.Id,
                    newOwnerId = target.Id,
                    oldOwnerRole = UserRole.Admin.ToString(),
                    newOwnerRole = UserRole.Owner.ToString()
                });
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw PosUserManagementException.OwnerInvariant(
                "Ownership transfer would violate the single-Owner invariant.");
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw PosUserManagementException.VersionConflict();
        }

        _logger.LogInformation(
            "POS ownership transferred | TenantId: {TenantId} | OldOwner: {Old} | NewOwner: {New}",
            tenantId, actorUserId, targetUserId);
    }

    // =====================================================================
    // Authority helpers
    // =====================================================================

    private async Task<User> RequireActorAsync(
        Guid actorUserId, Guid tenantId, CancellationToken cancellationToken)
    {
        var actor = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == actorUserId && u.TenantId == tenantId, cancellationToken);
        if (actor == null || actor.IsDeleted || !actor.IsActive ||
            (actor.Role != UserRole.Owner && actor.Role != UserRole.Admin))
        {
            throw new PosUserManagementException(
                "POS_PERMISSION_FORBIDDEN", StatusCodes.Status403Forbidden, "Access denied.");
        }

        return actor;
    }

    private async Task<User> RequireViewerAsync(
        Guid actorUserId, Guid tenantId, CancellationToken cancellationToken)
    {
        // Read admission mirrors the users.view grant (Owner/Admin). Backend
        // permission policies remain the authoritative boundary.
        return await RequireActorAsync(actorUserId, tenantId, cancellationToken);
    }

    private async Task<User> RequireManageableTargetAsync(
        User actor, Guid targetUserId, Guid tenantId, string operation, CancellationToken cancellationToken)
    {
        // Tenant-filtered lookup: foreign-tenant IDs resolve to 404 with no
        // existence leak, per the safe-lookup contract.
        var target = await FindTenantUserAsync(targetUserId, tenantId, cancellationToken)
            ?? throw PosUserManagementException.NotFound();

        if (target.Id == actor.Id)
        {
            throw PosUserManagementException.Validation(
                $"You cannot {operation} your own account through this API.");
        }

        if (target.Role == UserRole.Owner)
        {
            throw PosUserManagementException.OwnerInvariant(
                $"The Owner cannot be changed through this operation. Use ownership transfer.");
        }

        if (!ManageableRoles(actor.Role).Contains(target.Role))
        {
            throw PosUserManagementException.TargetForbidden(
                $"Your role cannot manage {target.Role} users.");
        }

        return target;
    }

    private Task<User?> FindTenantUserAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId, cancellationToken);

    private static IReadOnlySet<UserRole> ManageableRoles(UserRole actorRole) => actorRole switch
    {
        UserRole.Owner => OwnerManageable,
        UserRole.Admin => AdminManageable,
        _ => new HashSet<UserRole>(),
    };

    private static void RequireAssignable(UserRole actorRole, UserRole role)
    {
        if (!ManageableRoles(actorRole).Contains(role))
        {
            throw PosUserManagementException.TargetForbidden(
                $"Your role cannot assign the {role} role.");
        }
    }

    private static UserRole ParseRole(string? role)
    {
        if (!Enum.TryParse<UserRole>(role?.Trim(), ignoreCase: true, out var parsed) ||
            !Enum.IsDefined(parsed))
        {
            throw PosUserManagementException.Validation("Unknown role.");
        }

        return parsed;
    }

    private static void RequireVersion(User target, string? ifMatch)
    {
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            throw PosUserManagementException.Validation("If-Match header with the current version is required.");
        }

        var candidate = ifMatch.Trim();
        if (candidate.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate[2..].Trim();
        }

        candidate = candidate.Trim('"');
        if (!uint.TryParse(candidate, NumberStyles.None, CultureInfo.InvariantCulture, out var version) ||
            version != target.RowVersion)
        {
            throw PosUserManagementException.VersionConflict();
        }
    }

    private async Task RequireUniqueIdentityAsync(
        Guid? excludeUserId, string normalizedUsername, string normalizedEmail,
        CancellationToken cancellationToken)
    {
        // Global uniqueness must bypass the tenant query filter.
        var collision = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.Id != excludeUserId &&
                        (u.NormalizedUsername == normalizedUsername || u.NormalizedEmail == normalizedEmail))
            .Select(u => u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (collision != Guid.Empty)
        {
            throw PosUserManagementException.IdentityExists();
        }
    }

    private async Task<HashSet<Guid>> GetUserIdsWithValidTokensAsync(
        Guid tenantId, List<Guid> userIds, PosUserSecurityTokenPurpose purpose,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var now = DateTime.UtcNow;
        var ids = await _db.PosUserSecurityTokens.AsNoTracking()
            .Where(t => t.TenantId == tenantId &&
                        t.Purpose == purpose &&
                        t.ConsumedAtUtc == null &&
                        t.RevokedAtUtc == null &&
                        t.ExpiresAtUtc > now &&
                        userIds.Contains(t.UserId))
            .Select(t => t.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return ids.ToHashSet();
    }

    private async Task<int> RevokeOutstandingTokensAsync(
        Guid tenantId, Guid userId, PosUserSecurityTokenPurpose purpose, DateTime now,
        CancellationToken cancellationToken)
    {
        // Tracked updates (not bulk ExecuteUpdate): token rows must stay
        // consistent with the change tracker because the same scoped context
        // performs security reads after lifecycle writes.
        var outstanding = await _db.PosUserSecurityTokens
            .Where(t => t.TenantId == tenantId &&
                        t.UserId == userId &&
                        t.Purpose == purpose &&
                        t.ConsumedAtUtc == null &&
                        t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in outstanding)
        {
            token.RevokedAtUtc = now;
        }

        return outstanding.Count;
    }

    private async Task<PosUserSecurityToken?> FindCeremonyTokenAsync(
        string rawToken, PosUserSecurityTokenPurpose purpose, CancellationToken cancellationToken)
    {
        string hash;
        try
        {
            hash = HashToken(rawToken);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var token = await _db.PosUserSecurityTokens.IgnoreQueryFilters()
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == purpose, cancellationToken);

        if (token == null || token.IsConsumed || token.IsRevoked)
        {
            return null;
        }

        if (token.IsExpired(DateTime.UtcNow))
        {
            throw PosUserManagementException.TokenExpired();
        }

        return token;
    }

    private async Task<(Guid TenantId, Guid UserId)?> FindCeremonyTokenCandidateAsync(
        string rawToken, PosUserSecurityTokenPurpose purpose, CancellationToken cancellationToken)
    {
        string hash;
        try
        {
            hash = HashToken(rawToken);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var candidate = await _db.PosUserSecurityTokens.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(token => token.TokenHash == hash && token.Purpose == purpose)
            .Select(token => new { token.TenantId, token.UserId })
            .FirstOrDefaultAsync(cancellationToken);

        return candidate == null ? null : (candidate.TenantId, candidate.UserId);
    }

    private (string RawToken, PosUserSecurityToken Token) CreateToken(
        Guid tenantId, Guid userId, PosUserSecurityTokenPurpose purpose,
        DateTime expiresAtUtc, Guid? createdByUserId, DateTime now)
    {
        var rawToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
        var token = new PosUserSecurityToken
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            Purpose = purpose,
            TokenHash = HashToken(rawToken),
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = now,
            CreatedByUserId = createdByUserId,
            CreatedAt = now,
        };

        return (rawToken, token);
    }

    internal static string HashToken(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            throw new ArgumentException("Token is required.", nameof(rawToken));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
    }

    private int NormalizeInvitationExpiryHours() =>
        _options.InvitationExpiryHours <= 0 ? 24 : _options.InvitationExpiryHours;

    private int NormalizePasswordResetExpiryMinutes() =>
        _options.PasswordResetExpiryMinutes <= 0 ? 30 : _options.PasswordResetExpiryMinutes;

    private bool IsNpgsql() =>
        string.Equals(
            _db.Database.ProviderName,
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            StringComparison.Ordinal);

    private async Task LockTenantUsersAsync(
        Guid tenantId, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        if (!IsNpgsql())
        {
            return;
        }

        var orderedIds = userIds.Distinct().OrderBy(id => id).ToArray();
        await _db.Database.ExecuteSqlAsync(
            $"SELECT 1 FROM \"Users\" WHERE \"TenantId\" = {tenantId} AND \"Id\" = ANY ({orderedIds}) ORDER BY \"Id\" FOR UPDATE",
            cancellationToken);
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        var text = exception.InnerException?.Message ?? exception.Message;
        return text.Contains("23505", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TrimReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        var trimmed = reason.Trim();
        return trimmed.Length > 500 ? trimmed[..500] : trimmed;
    }

    private static bool IsPendingInvitation(User user) =>
        user.InvitedAtUtc.HasValue && !user.InvitationAcceptedAtUtc.HasValue;

    private static void TouchUser(User user, DateTime now) => user.UpdatedAt = now;

    private static string IdentityFingerprint(string normalizedIdentity) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedIdentity)));

    private static string DeriveStatus(User user, HashSet<Guid> validInvitationUserIds)
    {
        if (user.IsDeleted)
        {
            return PosUserStatuses.Removed;
        }

        if (user.IsActive)
        {
            return PosUserStatuses.Active;
        }

        if (!user.IsActive && IsPendingInvitation(user))
        {
            return validInvitationUserIds.Contains(user.Id)
                ? PosUserStatuses.Pending
                : PosUserStatuses.InvitationExpired;
        }

        return PosUserStatuses.Disabled;
    }

    private static PosUserListItemDto ToListItem(User user, string status) => new()
    {
        Id = user.Id,
        Username = user.Username,
        Email = user.Email,
        FirstName = user.FirstName,
        LastName = user.LastName,
        Phone = user.Phone,
        Role = user.Role.ToString(),
        Status = status,
        EmailVerified = user.EmailVerificationStatus == EmailVerificationStatus.Verified,
        LastLoginAt = user.LastLoginAt,
        InvitedAtUtc = user.InvitedAtUtc,
        InvitedByUserId = user.InvitedByUserId,
        Version = user.RowVersion.ToString(CultureInfo.InvariantCulture),
    };

    private static PosUserDetailDto ToDetail(User user, string status)
    {
        var item = ToListItem(user, status);
        return new PosUserDetailDto
        {
            Id = item.Id,
            Username = item.Username,
            Email = item.Email,
            FirstName = item.FirstName,
            LastName = item.LastName,
            Phone = item.Phone,
            Role = item.Role,
            Status = item.Status,
            EmailVerified = item.EmailVerified,
            LastLoginAt = item.LastLoginAt,
            InvitedAtUtc = item.InvitedAtUtc,
            InvitedByUserId = item.InvitedByUserId,
            Version = item.Version,
            InvitationAcceptedAtUtc = user.InvitationAcceptedAtUtc,
            DisabledAtUtc = user.DisabledAtUtc,
            DisabledByUserId = user.DisabledByUserId,
            DisableReason = user.DisableReason,
            CreatedAt = user.CreatedAt,
        };
    }

    private void AddAudit(
        Guid tenantId, Guid actorUserId, string actorUsername, string action, Guid targetUserId,
        string description, object? before, object? after)
    {
        var http = _httpContextAccessor.HttpContext;
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            UserId = actorUserId,
            TimestampUtc = DateTime.UtcNow,
            UserNameSnapshot = actorUsername.Length > 150 ? actorUsername[..150] : actorUsername,
            ActionType = action,
            EntityType = "User",
            EntityId = targetUserId.ToString("D"),
            Description = description,
            BeforeDataJson = before == null ? null : JsonSerializer.Serialize(before, AuditJsonOptions),
            AfterDataJson = after == null ? null : JsonSerializer.Serialize(after, AuditJsonOptions),
            IpAddress = http?.Connection.RemoteIpAddress?.ToString(),
            UserAgent = http?.Request.Headers.UserAgent.ToString(),
            ReferenceNumber = http?.TraceIdentifier,
            Source = "UsersManagement",
            SuccessFlag = true,
        });
    }
}
