using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PointOfSale.API.DTOs.Users;

/// <summary>
/// Presentation status derived from IsActive/IsDeleted/invitation state.
/// No second status column exists; IsActive stays the runtime login authority.
/// </summary>
public static class PosUserStatuses
{
    public const string Active = "Active";
    public const string Pending = "Pending";
    public const string InvitationExpired = "InvitationExpired";
    public const string Disabled = "Disabled";
    public const string Removed = "Removed";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Active, Pending, InvitationExpired, Disabled, Removed,
    };
}

public sealed class PosUserListQuery
{
    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;

    [MaxLength(100)]
    public string? Search { get; set; }

    [MaxLength(20)]
    public string? Role { get; set; }

    [MaxLength(20)]
    public string? Status { get; set; }

    public DateTime? InvitedFrom { get; set; }

    public DateTime? InvitedTo { get; set; }

    public DateTime? LastLoginFrom { get; set; }

    public DateTime? LastLoginTo { get; set; }

    [MaxLength(20)]
    public string? SortBy { get; set; }

    [MaxLength(4)]
    public string? SortDir { get; set; }
}

public class PosUserListItemDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool EmailVerified { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime? InvitedAtUtc { get; set; }
    public Guid? InvitedByUserId { get; set; }
    public string Version { get; set; } = string.Empty;
}

public sealed class PosUserDetailDto : PosUserListItemDto
{
    public DateTime? InvitationAcceptedAtUtc { get; set; }
    public DateTime? DisabledAtUtc { get; set; }
    public Guid? DisabledByUserId { get; set; }
    public string? DisableReason { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class PosUserListResultDto
{
    public IReadOnlyList<PosUserListItemDto> Items { get; set; } = Array.Empty<PosUserListItemDto>();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public int ActiveCount { get; set; }
    public int PendingCount { get; set; }
    public int DisabledCount { get; set; }
    public int OwnerAdminCount { get; set; }
}

public sealed class PosRoleDto
{
    public string Name { get; set; } = string.Empty;
    public IReadOnlyList<string> Permissions { get; set; } = Array.Empty<string>();
    public bool AssignableByActor { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class InvitePosUserRequest
{
    [Required, MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Phone { get; set; }

    [Required, MaxLength(20)]
    public string Role { get; set; } = string.Empty;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdatePosUserProfileRequest
{
    [Required, MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Phone { get; set; }

    [Required, MaxLength(200)]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ChangePosUserRoleRequest
{
    [Required, MaxLength(20)]
    public string Role { get; set; } = string.Empty;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ChangePosUserStatusRequest
{
    public bool IsActive { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TransferPosOwnershipRequest
{
    [Required]
    public Guid TargetUserId { get; set; }

    [Required, MaxLength(20)]
    public string TargetVersion { get; set; } = string.Empty;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AcceptPosUserInvitationRequest
{
    [Required, MaxLength(512)]
    public string Token { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string Password { get; set; } = string.Empty;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CompletePosPasswordResetRequest
{
    [Required, MaxLength(512)]
    public string Token { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string Password { get; set; } = string.Empty;
}
