using Microsoft.Extensions.DependencyInjection;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Fulfillment;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Fulfillment;
using PointOfSale.Infrastructure.Jobs;
using PointOfSale.Infrastructure.Payments;
using PointOfSale.Infrastructure.Treasury;

namespace PointOfSale.Infrastructure.Extensions;

/// <summary>
/// Extension methods for registering accounting services.
/// </summary>
public static class AccountingServiceExtensions
{
    /// <summary>
    /// Registers the core ledger engine services (Phase 1 + Phase 2 + Phase 3 + Phase 3.5 + Phase TLW + Phase COD).
    /// </summary>
    public static IServiceCollection AddCoreLedgerEngine(this IServiceCollection services)
    {
        services.AddOptions<AccountingOutboxOptions>();

        // Phase 1: Core Ledger Engine
        services.AddScoped<IFinancialPostingService, FinancialPostingService>();
        
        // Phase 2: Chart of Accounts & Treasury
        services.AddScoped<ITenantAccountSeeder, TenantAccountSeeder>();
        services.AddScoped<ITreasuryTransferService, TreasuryTransferService>();
        services.AddScoped<ICashboxLedgerAccountService, CashboxLedgerAccountService>();
        services.AddScoped<ITreasuryInitialCapitalizationService, TreasuryInitialCapitalizationService>();
        
        // Phase 3: Commerce-to-Ledger Integration
        services.AddScoped<IAccountResolver, AccountResolver>();
        services.AddScoped<ICommerceAccountingService, CommerceAccountingService>();
        
        // Phase 3.5: Production Hardening (Outbox Pattern)
        services.AddScoped<IAccountingOutboxService, AccountingOutboxService>();
        services.AddScoped<IAccountingOutboxProcessor, AccountingOutboxProcessor>();
        services.AddScoped<IAccountingTransactionBoundary, AccountingTransactionBoundary>();
        services.AddSingleton<IAccountingMetrics, AccountingMetrics>();
        services.AddScoped<AccountingOutboxCleanupJob>();
        
        // Phase 4: Financial Reporting Engine
        services.AddScoped<IFinancialReportService, FinancialReportService>();
        
        // Phase TLW: Treasury Ledger Read Wiring
        services.AddScoped<ITreasuryReadService, TreasuryReadService>();
        
        // Phase TOJ: Treasury Operations Journaling
        services.AddScoped<ITreasuryOperationService, TreasuryOperationService>();
        
        // Phase COD: Payment Lifecycle Architecture
        services.AddScoped<IPaymentLifecycleService, PaymentLifecycleService>();
        
        // Phase OFL: Order Fulfillment Lifecycle
        services.AddScoped<IOrderFulfillmentService, OrderFulfillmentService>();
        
        return services;
    }
    
    /// <summary>
    /// Adds the accounting outbox background worker.
    /// Call this in Program.cs: builder.Services.AddAccountingOutboxWorker();
    /// </summary>
    public static IServiceCollection AddAccountingOutboxWorker(this IServiceCollection services)
    {
        services.AddHostedService<AccountingOutboxWorker>();
        
        // PHASE COA: Startup seeder ensures all existing tenants have Chart of Accounts
        services.AddHostedService<ChartOfAccountsStartupSeeder>();
        
        return services;
    }
}
