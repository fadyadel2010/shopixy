using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace PointOfSale.API.Services.CommerceHub.Finance;

public interface ICommerceHubVendorPayoutMethodsService
{
    Task<IReadOnlyList<CommerceHubVendorPayoutMethodDto>> GetAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default);

    Task<CommerceHubVendorPayoutMethodDto> CreateAsync(
        Guid vendorId,
        CommerceHubCreateVendorPayoutMethodRequestDto request,
        Guid? actorId,
        CancellationToken cancellationToken = default);
}

public sealed class CommerceHubVendorPayoutMethodsService : ICommerceHubVendorPayoutMethodsService
{
    private readonly ApplicationDbContext _dbContext;

    public CommerceHubVendorPayoutMethodsService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<CommerceHubVendorPayoutMethodDto>> GetAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default)
    {
        ValidateVendorId(vendorId);

        return await _dbContext.CommerceHubVendorPayoutMethods
            .AsNoTracking()
            .Where(method => method.VendorId == vendorId && !method.IsDeleted)
            .OrderByDescending(method => method.IsDefault)
            .ThenByDescending(method => method.CreatedAt)
            .Select(MapDtoExpression())
            .ToListAsync(cancellationToken);
    }

    public async Task<CommerceHubVendorPayoutMethodDto> CreateAsync(
        Guid vendorId,
        CommerceHubCreateVendorPayoutMethodRequestDto request,
        Guid? actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateVendorId(vendorId);
        ValidateMethodType(request.MethodType);

        var destinationValue = NormalizeDestinationValue(request.DestinationValue);
        var vendorExists = await _dbContext.Tenants
            .AsNoTracking()
            .AnyAsync(tenant => tenant.Id == vendorId && tenant.DeletedAtUtc == null, cancellationToken);

        if (!vendorExists)
        {
            throw new CommerceHubVendorFinancialControlException(
                "VENDOR_NOT_FOUND",
                "Vendor was not found.",
                StatusCodes.Status404NotFound);
        }

        var existing = await _dbContext.CommerceHubVendorPayoutMethods
            .FirstOrDefaultAsync(method =>
                method.VendorId == vendorId &&
                !method.IsDeleted &&
                method.MethodType == request.MethodType &&
                method.DestinationValue == destinationValue,
                cancellationToken);

        if (existing != null)
        {
            if (request.IsDefault && !existing.IsDefault)
            {
                var existingVendorMethods = await _dbContext.CommerceHubVendorPayoutMethods
                    .Where(method => method.VendorId == vendorId && !method.IsDeleted)
                    .ToListAsync(cancellationToken);

                foreach (var method in existingVendorMethods.Where(method => method.IsDefault))
                {
                    method.IsDefault = false;
                }

                existing.IsDefault = true;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return MapDto(existing);
        }

        var methods = await _dbContext.CommerceHubVendorPayoutMethods
            .Where(method => method.VendorId == vendorId && !method.IsDeleted)
            .ToListAsync(cancellationToken);

        var shouldBeDefault = request.IsDefault || methods.Count == 0;
        if (shouldBeDefault)
        {
            foreach (var method in methods.Where(method => method.IsDefault))
            {
                method.IsDefault = false;
            }
        }

        var payoutMethod = new CommerceHubVendorPayoutMethod
        {
            Id = Guid.NewGuid(),
            VendorId = vendorId,
            MethodType = request.MethodType,
            DestinationValue = destinationValue,
            IsDefault = shouldBeDefault,
            IsVerified = false,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = actorId
        };

        _dbContext.CommerceHubVendorPayoutMethods.Add(payoutMethod);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapDto(payoutMethod);
    }

    private static System.Linq.Expressions.Expression<Func<CommerceHubVendorPayoutMethod, CommerceHubVendorPayoutMethodDto>> MapDtoExpression()
    {
        return method => new CommerceHubVendorPayoutMethodDto
        {
            Id = method.Id,
            VendorId = method.VendorId,
            MethodType = method.MethodType.ToString(),
            DestinationValue = method.DestinationValue,
            IsDefault = method.IsDefault,
            IsVerified = method.IsVerified,
            CreatedAtUtc = method.CreatedAt
        };
    }

    private static CommerceHubVendorPayoutMethodDto MapDto(CommerceHubVendorPayoutMethod method) =>
        new()
        {
            Id = method.Id,
            VendorId = method.VendorId,
            MethodType = method.MethodType.ToString(),
            DestinationValue = method.DestinationValue,
            IsDefault = method.IsDefault,
            IsVerified = method.IsVerified,
            CreatedAtUtc = method.CreatedAt
        };

    private static void ValidateVendorId(Guid vendorId)
    {
        if (vendorId == Guid.Empty)
        {
            throw new CommerceHubVendorFinancialControlException(
                "VENDOR_REQUIRED",
                "VendorId is required.",
                StatusCodes.Status400BadRequest);
        }
    }

    private static void ValidateMethodType(CommerceHubPayoutMethod methodType)
    {
        if (methodType is CommerceHubPayoutMethod.BankTransfer or CommerceHubPayoutMethod.VodafoneCash or CommerceHubPayoutMethod.InstaPay)
        {
            return;
        }

        throw new CommerceHubVendorFinancialControlException(
            "PAYOUT_METHOD_TYPE_INVALID",
            "Payout method type must be VodafoneCash, InstaPay, or BankTransfer.",
            StatusCodes.Status400BadRequest);
    }

    private static string NormalizeDestinationValue(string? destinationValue)
    {
        if (string.IsNullOrWhiteSpace(destinationValue))
        {
            throw new CommerceHubVendorFinancialControlException(
                "DESTINATION_VALUE_REQUIRED",
                "Destination value is required.",
                StatusCodes.Status400BadRequest);
        }

        var normalized = destinationValue.Trim();
        if (normalized.Length > 200)
        {
            throw new CommerceHubVendorFinancialControlException(
                "DESTINATION_VALUE_INVALID",
                "Destination value must not exceed 200 characters.",
                StatusCodes.Status400BadRequest);
        }

        return normalized;
    }
}
