using PointOfSale.API.SuperAdmin.DTOs;

namespace PointOfSale.API.SuperAdmin.Models;

public sealed class SuperAdminAuthResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? Message { get; init; }
    public SuperAdminLoginResponse? Response { get; init; }
}
