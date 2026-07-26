using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.CampaignCost;

/// <summary>
/// Implementation of ICampaignCostService.
/// 
/// PHASE 8B: Campaign Cost & ROI (MVP)
/// 
/// STORAGE:
/// - PostgreSQL via EF Core
/// - Tenant-isolated via query filters
/// 
/// UNIQUE KEY HANDLING:
/// - CreateOrUpdate uses upsert logic
/// - Match on (TenantId, Source, Medium, Campaign, PeriodStartUtc)
/// </summary>
public class CampaignCostService : ICampaignCostService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<CampaignCostService> _logger;

    public CampaignCostService(
        ApplicationDbContext dbContext,
        ILogger<CampaignCostService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<Core.Analytics.Attribution.CampaignCost> CreateOrUpdateAsync(
        Guid tenantId,
        CreateCampaignCostRequest request,
        CancellationToken cancellationToken = default)
    {
        // Normalize UTM values
        var source = request.Source.Trim().ToLowerInvariant();
        var medium = request.Medium.Trim().ToLowerInvariant();
        var campaign = request.Campaign.Trim().ToLowerInvariant();

        // Check for existing entry
        var existing = await _dbContext.CampaignCosts
            .FirstOrDefaultAsync(c =>
                c.TenantId == tenantId &&
                c.Source == source &&
                c.Medium == medium &&
                c.Campaign == campaign &&
                c.PeriodStartUtc == request.PeriodStartUtc,
                cancellationToken);

        if (existing != null)
        {
            // Update existing
            existing.PeriodEndUtc = request.PeriodEndUtc;
            existing.CostAmount = request.CostAmount;
            existing.Currency = request.Currency;
            existing.Notes = request.Notes;
            existing.UpdatedAtUtc = DateTime.UtcNow;

            _logger.LogInformation(
                "[CampaignCost] Updated cost for {Source}/{Medium}/{Campaign} period {Start}: {Amount} {Currency}",
                source, medium, campaign, request.PeriodStartUtc, request.CostAmount, request.Currency);

            await _dbContext.SaveChangesAsync(cancellationToken);
            return existing;
        }

        // Create new
        var cost = new Core.Analytics.Attribution.CampaignCost
        {
            CampaignCostId = Guid.NewGuid(),
            TenantId = tenantId,
            Source = source,
            Medium = medium,
            Campaign = campaign,
            PeriodStartUtc = request.PeriodStartUtc,
            PeriodEndUtc = request.PeriodEndUtc,
            CostAmount = request.CostAmount,
            Currency = request.Currency,
            Notes = request.Notes,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            IsDeleted = false
        };

        _dbContext.CampaignCosts.Add(cost);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[CampaignCost] Created cost for {Source}/{Medium}/{Campaign} period {Start}: {Amount} {Currency}",
            source, medium, campaign, request.PeriodStartUtc, request.CostAmount, request.Currency);

        return cost;
    }

    public async Task<List<Core.Analytics.Attribution.CampaignCost>> GetByPeriodAsync(
        Guid tenantId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.CampaignCosts
            .Where(c =>
                c.TenantId == tenantId &&
                c.PeriodStartUtc <= to &&
                c.PeriodEndUtc >= from)
            .OrderByDescending(c => c.PeriodStartUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<Core.Analytics.Attribution.CampaignCost?> GetByIdAsync(
        Guid tenantId,
        Guid campaignCostId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.CampaignCosts
            .FirstOrDefaultAsync(c =>
                c.TenantId == tenantId &&
                c.CampaignCostId == campaignCostId,
                cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        Guid tenantId,
        Guid campaignCostId,
        CancellationToken cancellationToken = default)
    {
        var cost = await _dbContext.CampaignCosts
            .FirstOrDefaultAsync(c =>
                c.TenantId == tenantId &&
                c.CampaignCostId == campaignCostId,
                cancellationToken);

        if (cost == null)
        {
            return false;
        }

        // Soft delete
        cost.IsDeleted = true;
        cost.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[CampaignCost] Deleted cost {Id} for {Source}/{Medium}/{Campaign}",
            campaignCostId, cost.Source, cost.Medium, cost.Campaign);

        return true;
    }

    public async Task<List<Core.Analytics.Attribution.CampaignCost>> GetByCampaignAsync(
        Guid tenantId,
        string? source,
        string? medium,
        string? campaign,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.CampaignCosts
            .Where(c =>
                c.TenantId == tenantId &&
                c.PeriodStartUtc <= to &&
                c.PeriodEndUtc >= from);

        if (!string.IsNullOrWhiteSpace(source))
        {
            var normalizedSource = source.ToLowerInvariant().Trim();
            query = query.Where(c => c.Source == normalizedSource);
        }

        if (!string.IsNullOrWhiteSpace(medium))
        {
            var normalizedMedium = medium.ToLowerInvariant().Trim();
            query = query.Where(c => c.Medium == normalizedMedium);
        }

        if (!string.IsNullOrWhiteSpace(campaign))
        {
            var normalizedCampaign = campaign.ToLowerInvariant().Trim();
            query = query.Where(c => c.Campaign == normalizedCampaign);
        }

        return await query.ToListAsync(cancellationToken);
    }
}
