using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Admin;

public sealed class PaymentProviderDashboardService : IPaymentProviderDashboardService
{
    private static readonly ISet<string> DashboardAuditActions = new HashSet<string>(StringComparer.Ordinal)
    {
        "PAYMENT_PROVIDER_CONFIGURATION_VIEWED",
        "PAYMENT_PROVIDER_CONFIGURATION_SAVED",
        "PAYMENT_PROVIDER_CONNECTION_TESTED",
        "PAYMENT_PROVIDER_SECRET_METADATA_VIEWED",
        "PAYMENT_PROVIDER_SECRET_ROTATED",
        "PAYMENT_PROVIDER_DASHBOARD_VIEWED",
        "PAYMENT_PROVIDERS_VIEWED"
    };

    private readonly IPaymentProviderAdministrationService _administrationService;
    private readonly ApplicationDbContext _dbContext;

    public PaymentProviderDashboardService(
        IPaymentProviderAdministrationService administrationService,
        ApplicationDbContext dbContext)
    {
        _administrationService = administrationService;
        _dbContext = dbContext;
    }

    public async Task<PaymentProviderDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var auditRows = await _dbContext.AuditLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(audit => audit.TenantId == null &&
                            audit.Source == "SuperAdminAPI" &&
                            DashboardAuditActions.Contains(audit.ActionType))
            .OrderByDescending(audit => audit.TimestampUtc)
            .ThenByDescending(audit => audit.LogId)
            .Take(25)
            .Select(audit => new
            {
                audit.LogId,
                audit.EntityId,
                audit.ReferenceNumber,
                audit.ActionType,
                audit.Description,
                audit.TimestampUtc,
                audit.UserNameSnapshot,
                audit.SuccessFlag
            })
            .ToListAsync(cancellationToken);
        var recentActivity = auditRows
            .Select(audit => new PaymentProviderAdministrationActivityDto
            {
                ActivityId = audit.LogId.ToString(),
                ProviderCode = ResolveProviderCode(audit.EntityId, audit.ReferenceNumber),
                Action = audit.ActionType,
                DisplayName = ResolveActivityDisplayName(audit.ActionType),
                Description = audit.Description,
                OccurredAtUtc = audit.TimestampUtc,
                Actor = audit.UserNameSnapshot,
                Success = audit.SuccessFlag,
                Reference = audit.ReferenceNumber
            })
            .ToList();

        return _administrationService.GetDashboard(recentActivity);
    }

    private static string ResolveActivityDisplayName(string action) =>
        action switch
        {
            "PAYMENT_PROVIDER_CONFIGURATION_VIEWED" => "Configuration Viewed",
            "PAYMENT_PROVIDER_CONFIGURATION_SAVED" => "Configuration Updated",
            "PAYMENT_PROVIDER_CONNECTION_TESTED" => "Connection Tested",
            "PAYMENT_PROVIDER_SECRET_METADATA_VIEWED" => "Secret Metadata Viewed",
            "PAYMENT_PROVIDER_SECRET_ROTATED" => "Secret Rotated",
            "PAYMENT_PROVIDER_DASHBOARD_VIEWED" => "Dashboard Viewed",
            "PAYMENT_PROVIDERS_VIEWED" => "Providers Viewed",
            _ => "Administrative Activity"
        };

    private static string ResolveProviderCode(string entityId, string? reference)
    {
        var candidate = !string.IsNullOrWhiteSpace(reference) &&
                        !reference.Contains('-', StringComparison.Ordinal)
            ? reference
            : entityId;

        if (string.Equals(candidate, "payment-providers", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(candidate)
            ? string.Empty
            : candidate.Trim().ToUpperInvariant();
    }
}
