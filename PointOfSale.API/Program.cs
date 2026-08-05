using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using System.IdentityModel.Tokens.Jwt;
using MassTransit;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PointOfSale.API.Consumers;
using PointOfSale.API.Infrastructure.Mvc;
using PointOfSale.API.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Infrastructure.RateLimiting;
using PointOfSale.API.Models;
using PointOfSale.API.Security;
using PointOfSale.API.SuperAdmin.Infrastructure;
using PointOfSale.API.SuperAdmin.Models;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.SuperAdmin.Models;
using PointOfSale.Infrastructure.Consumers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.EventBus;
using PointOfSale.Infrastructure.MongoDB;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Notifications;
using PointOfSale.Infrastructure.Redis;
using PointOfSale.Infrastructure.Elasticsearch;
using PointOfSale.Infrastructure.Configuration;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Extensions;
using PointOfSale.Infrastructure.Payments.Runtime;
using PointOfSale.API.Services.Payments.Stripe;
using PointOfSale.API.Services.Payments.Paymob;
using PointOfSale.API.Services.Payments.Fawry;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.API.Services.Payments.Cod;
using PointOfSale.API.Services.Payments.Intents;
using PointOfSale.API.Services.ProductAuthoring;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly;
using Polly.Extensions.Http;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddJsonFile(
        $"appsettings.{builder.Environment.EnvironmentName}.local.json",
        optional: true,
        reloadOnChange: builder.Environment.IsDevelopment());

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true);
}

builder.Configuration.AddEnvironmentVariables();

var defaultConnectionString = PostgresConnectionStringPolicy.Normalize(
    builder.Configuration.GetConnectionString("DefaultConnection"),
    builder.Environment.IsDevelopment());

var isAuthoritativeDeterministicTestHost =
    string.Equals(Environment.GetEnvironmentVariable("REQUIRE_REAL_POSTGRES_TESTS"), "1", StringComparison.Ordinal);

if (!string.IsNullOrWhiteSpace(defaultConnectionString))
{
    builder.Configuration["ConnectionStrings:DefaultConnection"] = defaultConnectionString;
}

PointOfSale.API.Configuration.SensitiveConfigurationValidator.ValidateOrThrow(builder.Configuration);

builder.Logging.ClearProviders();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});
builder.Logging.AddDebug();
builder.Logging.AddEventSourceLogger();

// Configure forwarded headers for proper client IP resolution behind proxies/load balancers
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    
    // Clear default networks/proxies and configure based on your infrastructure
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    
    // Add your load balancer/reverse proxy IPs here
    // Example: options.KnownProxies.Add(IPAddress.Parse("10.0.0.100"));
    // For development/testing with localhost proxies:
    if (builder.Environment.IsDevelopment())
    {
        options.KnownProxies.Add(System.Net.IPAddress.Parse("127.0.0.1"));
        options.KnownProxies.Add(System.Net.IPAddress.Parse("::1"));
    }
    
    // In production, configure known proxy IPs from configuration
    var knownProxiesConfig = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>();
    if (knownProxiesConfig != null)
    {
        foreach (var proxyIp in knownProxiesConfig)
        {
            if (System.Net.IPAddress.TryParse(proxyIp, out var ipAddress))
            {
                options.KnownProxies.Add(ipAddress);
            }
        }
    }
    
    // Limit to 1 proxy hop for security (adjust based on your infrastructure)
    options.ForwardLimit = 1;
});

// Add services to the container
builder.Services.AddScoped<ApiRequestNormalizationFilter>();
builder.Services.AddScoped<ApiErrorResponseNormalizationFilter>();
builder.Services.AddScoped<PreventEntityExposureFilter>();
builder.Services.AddScoped<WebhookContentTypeValidationFilter>();

builder.Services.AddControllers(options =>
    {
        options.Filters.AddService<ApiRequestNormalizationFilter>();
        options.Filters.AddService<PreventEntityExposureFilter>();
        options.Filters.AddService<ApiErrorResponseNormalizationFilter>();
    })
    .ConfigureApplicationPartManager(manager =>
    {
        if (!builder.Environment.IsDevelopment())
        {
            manager.FeatureProviders.Add(new DevelopmentOnlyControllerFeatureProvider());
        }

        // SECURITY: Strip [TestingOnlyController] endpoints from every environment except Test/Testing/Development.
        // This ensures test auth endpoints never exist in Production or Staging.
        // The TestAuthController itself has a defence-in-depth environment check
        // that returns 404 unless ASPNETCORE_ENVIRONMENT is explicitly a test harness environment.
        var isTestingEnvironment = builder.Environment.IsEnvironment("Test")
                                   || builder.Environment.IsEnvironment("Testing")
                                   || builder.Environment.IsDevelopment();
        manager.FeatureProviders.Add(new TestingOnlyControllerFeatureProvider(isTestingEnvironment));
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var correlationId = PointOfSale.API.Middleware.RequestContextMetadata.GetCorrelationId(context.HttpContext);
        var validationErrors = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value!.Errors
                    .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? "Invalid value." : error.ErrorMessage)
                    .ToArray());

        return new BadRequestObjectResult(new ApiErrorResponse("VALIDATION_ERROR", "The request payload is invalid.", correlationId)
        {
            Details = validationErrors
        });
    };
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.Configure<PointOfSale.API.Configuration.CommerceHubPaymobOptions>(
    builder.Configuration.GetSection("CommerceHub:Payments:Paymob"));
builder.Services.Configure<PointOfSale.API.Configuration.CommerceHubPaymentsOptions>(
    builder.Configuration.GetSection("CommerceHub:Payments"));
builder.Services.AddPaymobFoundation(builder.Configuration);
builder.Services.AddPaymobProviderAdapter();
builder.Services.AddStripeFoundation(builder.Configuration);
builder.Services.AddStripeCanonicalProvider();
builder.Services.AddFawryFoundation(builder.Configuration);
builder.Services.AddManualPaymentFoundation(builder.Configuration);
builder.Services.AddCodReferenceWorkflowFoundation(builder.Configuration);
builder.Services.AddCodCollectionEvidenceFoundation();
builder.Services.AddCodRuntimeAdmissionFoundation();
builder.Services.AddCodRuntimeAdmissionApplicationContract();
builder.Services.AddCodRuntimeAdmissionBridge();
builder.Services.AddCodAdmissionLifecycle();
builder.Services.AddCodWorkflowOrchestrator();
builder.Services.AddPaymentIntentInfrastructure();
builder.Services.AddFawryApplicationContract();
builder.Services.AddFawrySensitiveArtifactProtection();
builder.Services.Configure<PointOfSale.API.Configuration.CommerceHubEmergencyStopOptions>(
    builder.Configuration.GetSection(PointOfSale.API.Configuration.CommerceHubEmergencyStopOptions.SectionName));
builder.Services.Configure<PointOfSale.Core.Pricing.CommerceHubPriceOverrideRolloutOptions>(
    builder.Configuration.GetSection(PointOfSale.Core.Pricing.CommerceHubPriceOverrideRolloutOptions.SectionName));
PointOfSale.Core.Pricing.CommerceHubPriceOverrideRuntimePolicy.Configure(
    builder.Configuration
        .GetSection(PointOfSale.Core.Pricing.CommerceHubPriceOverrideRolloutOptions.SectionName)
        .Get<PointOfSale.Core.Pricing.CommerceHubPriceOverrideRolloutOptions>());
_ = Microsoft.Extensions.Primitives.ChangeToken.OnChange(
    ((IConfiguration)builder.Configuration).GetReloadToken,
    () => PointOfSale.Core.Pricing.CommerceHubPriceOverrideRuntimePolicy.Configure(
        builder.Configuration
            .GetSection(PointOfSale.Core.Pricing.CommerceHubPriceOverrideRolloutOptions.SectionName)
            .Get<PointOfSale.Core.Pricing.CommerceHubPriceOverrideRolloutOptions>()));
builder.Services.Configure<PointOfSale.API.Configuration.CommerceHubVendorDebtOptions>(
    builder.Configuration.GetSection("CommerceHub:Finance:VendorDebt"));
builder.Services.Configure<PointOfSale.API.Configuration.MarketplaceCmsPageCacheOptions>(
    builder.Configuration.GetSection(PointOfSale.API.Configuration.MarketplaceCmsPageCacheOptions.SectionName));
builder.Services.AddOptions<PointOfSale.API.Configuration.OperationalAlertThresholdsOptions>()
    .Bind(builder.Configuration.GetSection(PointOfSale.API.Configuration.OperationalAlertThresholdsOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Checkout session security options with startup validation
builder.Services.AddOptions<PointOfSale.API.Configuration.CheckoutSessionSecurityOptions>()
    .Bind(builder.Configuration.GetSection(PointOfSale.API.Configuration.CheckoutSessionSecurityOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<AccountingOutboxOptions>()
    .Bind(builder.Configuration.GetSection(AccountingOutboxOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Storefront pricing options with startup validation
builder.Services.AddOptions<PointOfSale.API.Services.Pricing.StorefrontPricingOptions>()
    .Bind(builder.Configuration.GetSection(PointOfSale.API.Services.Pricing.StorefrontPricingOptions.SectionName))
    .PostConfigure(options =>
    {
        options.NormalizeTaxRates();
    })
    .ValidateDataAnnotations()
    .Validate(
        options => options.ValidateCountryTaxRates(out _),
        "Invalid country tax rates configuration")
    .ValidateOnStart();

builder.Services.Configure<PointOfSale.API.Configuration.R2Options>(options =>
{
    options.AccountId = (builder.Configuration["R2_ACCOUNT_ID"] ?? builder.Configuration["R2:AccountId"] ?? string.Empty).Trim();
    options.AccessKeyId = (builder.Configuration["R2_ACCESS_KEY_ID"] ?? builder.Configuration["R2:AccessKeyId"] ?? string.Empty).Trim();
    options.SecretAccessKey = (builder.Configuration["R2_SECRET_ACCESS_KEY"] ?? builder.Configuration["R2:SecretAccessKey"] ?? string.Empty).Trim();
    options.Bucket = (builder.Configuration["R2_BUCKET"] ?? builder.Configuration["R2:Bucket"] ?? string.Empty).Trim();
    options.ServiceUrl = (builder.Configuration["R2_SERVICE_URL"] ?? builder.Configuration["R2:ServiceUrl"] ?? string.Empty).Trim();
    options.PublicUrl = (builder.Configuration["R2_PUBLIC_URL"] ?? builder.Configuration["R2:PublicUrl"] ?? string.Empty).Trim();
});

// Configure Swagger with JWT support
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Point of Sale API",
        Version = "v1",
        Description = "POS System API with E-commerce integration support",
        Contact = new OpenApiContact
        {
            Name = "API Support",
            Email = "support@example.com"
        }
    });
    
    // Add JWT authentication to Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
    
    // Add operation filter for Idempotency-Key header documentation
    c.OperationFilter<PointOfSale.API.Infrastructure.Swagger.IdempotencyKeyOperationFilter>();
});

// Configure Database with Security Interceptors
// SECURITY: TenantSaveChangesInterceptor blocks saving ITenantOwned entities without valid TenantId
// SECURITY: PaymentStatusSaveChangesInterceptor blocks direct PaymentStatus changes (ledger-first enforcement)
// SECURITY: OrderStatusSaveChangesInterceptor blocks direct Order.Status changes (lifecycle authority enforcement)
// SECURITY: FulfillmentStatusSaveChangesInterceptor blocks direct FulfillmentStatus changes (lifecycle enforcement)
// SECURITY: MarketplaceInvariantInterceptor blocks invalid marketplace/service boundary mutations
// SECURITY: PrivilegedRefreshTokenRevocationInterceptor clears refresh authority when SQL authority changes
builder.Services.AddScoped<TenantSaveChangesInterceptor>();
builder.Services.AddScoped<PaymentStatusSaveChangesInterceptor>();
builder.Services.AddScoped<OrderStatusSaveChangesInterceptor>();
builder.Services.AddScoped<FulfillmentStatusSaveChangesInterceptor>();
builder.Services.AddScoped<MarketplaceInvariantInterceptor>();
builder.Services.AddScoped<PrivilegedRefreshTokenRevocationInterceptor>();
builder.Services.AddScoped<PointOfSale.Infrastructure.Observability.SlowQueryLoggingInterceptor>();
builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
{
    options.UseNpgsql(defaultConnectionString);
    options.AddInterceptors(
        sp.GetRequiredService<PointOfSale.Infrastructure.Observability.SlowQueryLoggingInterceptor>(),
        sp.GetRequiredService<TenantSaveChangesInterceptor>(),
        sp.GetRequiredService<PaymentStatusSaveChangesInterceptor>(),
        sp.GetRequiredService<OrderStatusSaveChangesInterceptor>(),
        sp.GetRequiredService<FulfillmentStatusSaveChangesInterceptor>(),
        sp.GetRequiredService<MarketplaceInvariantInterceptor>(),
        sp.GetRequiredService<PrivilegedRefreshTokenRevocationInterceptor>());
});

// Production hardening: Order lifecycle writes must now stay inside the explicit
// authorized boundaries used by checkout, payment, admin, and shipment workflows.
// NOTE: OrderStatusSaveChangesInterceptor enforcement is enabled by default (fail-closed).
// It can only be disabled via explicit configuration in Development/Test environments.


// Add password hashers
builder.Services.AddScoped<Microsoft.AspNetCore.Identity.IPasswordHasher<User>, Microsoft.AspNetCore.Identity.PasswordHasher<User>>();
builder.Services.AddScoped<Microsoft.AspNetCore.Identity.IPasswordHasher<SuperAdminUser>, Microsoft.AspNetCore.Identity.PasswordHasher<SuperAdminUser>>();
// AF-10: Separate password hasher for affiliate identity (decoupled from POS User)
builder.Services.AddScoped<Microsoft.AspNetCore.Identity.IPasswordHasher<PointOfSale.Core.Entities.Affiliate.AffiliateUser>, Microsoft.AspNetCore.Identity.PasswordHasher<PointOfSale.Core.Entities.Affiliate.AffiliateUser>>();

// Add custom services
// IMemoryCache — used by StorefrontUrlResolver for tenant URL caching
builder.Services.AddMemoryCache();
// AF-16D: Storefront URL resolver — single source of truth for promotion link URLs
builder.Services.AddSingleton<PointOfSale.API.Services.StorefrontUrlResolver>();

// AF-16E: Reindex service for backfilling StorefrontBaseUrl in affiliate products
builder.Services.AddScoped<PointOfSale.Infrastructure.Scripts.AF16E_ReindexAffiliateProductsWithStorefrontUrl>();

builder.Services.AddScoped<PointOfSale.API.Services.IBarcodeService, PointOfSale.API.Services.BarcodeService>();
builder.Services.AddScoped<PointOfSale.API.Services.IImageService, PointOfSale.API.Services.ImageService>();
builder.Services.AddScoped<PointOfSale.API.Services.IInventoryService, PointOfSale.API.Services.InventoryService>();
builder.Services.AddScoped<PointOfSale.API.Services.ILedgerService, PointOfSale.API.Services.LedgerService>();
builder.Services.AddScoped<PointOfSale.API.Services.IPartyBalanceMaintenanceService, PointOfSale.API.Services.LedgerService>();
builder.Services.AddScoped<PointOfSale.API.Services.ISaleFinancialIntegrityService, PointOfSale.API.Services.SaleFinancialIntegrityService>();
builder.Services.AddScoped<PointOfSale.API.Services.Pos.IPosCartRuntimeService, PointOfSale.API.Services.Pos.PosCartRuntimeService>();
builder.Services.AddScoped<PointOfSale.API.Services.IPurchaseFinancialIntegrityService, PointOfSale.API.Services.PurchaseFinancialIntegrityService>();
builder.Services.AddScoped<PointOfSale.API.Services.IFinancialMutationIdempotencyService, PointOfSale.API.Services.FinancialMutationIdempotencyService>();
builder.Services.AddScoped<PointOfSale.API.Services.IFinancialIntegrityService, PointOfSale.API.Services.FinancialIntegrityService>();
builder.Services.AddScoped<PointOfSale.API.Services.IProductMediaService, PointOfSale.API.Services.ProductMediaService>();
builder.Services.AddScoped<PointOfSale.API.Services.IR2StorageService, PointOfSale.API.Services.R2StorageService>();
builder.Services.AddScoped<PointOfSale.Core.Abstractions.IChatAttachmentReferenceVerifier, PointOfSale.API.Services.ChatAttachmentReferenceVerifier>();
builder.Services.AddScoped<PointOfSale.API.Services.IFileValidationService, PointOfSale.API.Services.FileValidationService>();
// NOTE: FinancialAccountService was removed - Visa accounts are now created per-tenant in TenantRegistrationService
builder.Services.AddScoped<PointOfSale.API.Services.IValidationService, PointOfSale.API.Services.ValidationService>();
builder.Services.AddScoped<PointOfSale.API.Services.Admin.IGeographicAdministrationService, PointOfSale.API.Services.Admin.GeographicAdministrationService>();
builder.Services.AddProductAuthoringInfrastructure();

// =============================================================================
// SERVICES MARKETPLACE (Phase P2 - Marketplace Module)
// Marketplace services for service discovery, catalog management,
// request lifecycle, chat, reviews, and trust scoring.
// =============================================================================
builder.Services.AddScoped<PointOfSale.Core.Interfaces.IMarketplaceSearchService, PointOfSale.Infrastructure.Services.MarketplaceSearchService>();
builder.Services.AddScoped<PointOfSale.Core.Interfaces.IMarketplaceProfileService, PointOfSale.Infrastructure.Services.MarketplaceProfileService>();
builder.Services.AddScoped<PointOfSale.Core.Interfaces.IServiceCatalogService, PointOfSale.Infrastructure.Services.ServiceCatalogService>();
builder.Services.AddScoped<PointOfSale.Core.Interfaces.IServiceRequestService, PointOfSale.Infrastructure.Services.ServiceRequestService>();
builder.Services.AddScoped<PointOfSale.Core.Interfaces.IServiceReviewService, PointOfSale.Infrastructure.Services.ServiceReviewService>();
builder.Services.AddScoped<PointOfSale.Core.Interfaces.IChatService, PointOfSale.Infrastructure.Services.ChatService>();
builder.Services.AddScoped<PointOfSale.Core.Interfaces.IProviderTrustService, PointOfSale.Infrastructure.Services.ProviderTrustService>();
builder.Services.AddScoped<PointOfSale.Core.Interfaces.ITenantService, PointOfSale.Infrastructure.Services.TenantService>();
builder.Services.AddSignalR();

// =============================================================================
// CORE LEDGER ENGINE (Phase 1-4 - Accounting System)
// Registers all accounting services including:
// - IFinancialPostingService (Phase 1)
// - ITenantAccountSeeder, ITreasuryTransferService (Phase 2)
// - IAccountResolver, ICommerceAccountingService (Phase 3)
// - Outbox Pattern services (Phase 3.5)
// - IFinancialReportService (Phase 4)
// =============================================================================
builder.Services.AddCoreLedgerEngine();
builder.Services.AddAccountingOutboxWorker(); // Background outbox processor + COA startup seeder
builder.Services.AddPaymentCancellationOutbox(builder.Configuration); // Durable payment cancellation operations
builder.Services.AddProviderDeliveryInfrastructure(builder.Configuration); // Durable provider delivery outbox + worker
builder.Services.AddMarketplaceEventBackbone(builder.Configuration);
builder.Services.AddStockSyncOutbox(builder.Configuration);

// =============================================================================
// PRODUCT VARIANT SERVICE (Phase 11.1 - Shopify-Grade Variants)
// Auto-creates default variants for new products.
// Every product MUST have exactly one default variant.
// =============================================================================
builder.Services.AddScoped<PointOfSale.API.Services.IProductVariantService, PointOfSale.API.Services.ProductVariantService>();

// =============================================================================
// STOREFRONT READ SERVICES (E-commerce)
// Read-only services for e-commerce storefront APIs.
// Uses Redis (cache) → MongoDB (read store) flow.
// =============================================================================
builder.Services.AddScoped<PointOfSale.API.Services.Storefront.IStorefrontProductService, PointOfSale.API.Services.Storefront.StorefrontProductService>();
builder.Services.AddScoped<PointOfSale.API.Services.Storefront.IProductSearchService, PointOfSale.API.Services.Storefront.ProductSearchService>();
builder.Services.AddScoped<PointOfSale.API.Services.Storefront.IStorefrontCategoryService, PointOfSale.API.Services.Storefront.StorefrontCategoryService>();
builder.Services.AddScoped<PointOfSale.API.Services.Storefront.IStorefrontHomeService, PointOfSale.API.Services.Storefront.StorefrontHomeService>();
builder.Services.AddScoped<PointOfSale.API.Services.Storefront.IStorefrontPublicationStatusService, PointOfSale.API.Services.Storefront.StorefrontPublicationStatusService>();
builder.Services.AddScoped<PointOfSale.API.Services.Storefront.IStorefrontOrderProjectionRepairService, PointOfSale.API.Services.Storefront.StorefrontOrderProjectionRepairService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICommerceHubOrderProjectionRepairService, PointOfSale.API.Services.CommerceHub.CommerceHubOrderProjectionRepairService>();
builder.Services.AddScoped<PointOfSale.API.Services.Storefront.ISeoService, PointOfSale.API.Services.Storefront.SeoService>();
builder.Services.AddScoped<PointOfSale.API.Services.Homepage.IHomepageSectionSchemaRegistry, PointOfSale.API.Services.Homepage.HomepageSectionSchemaRegistry>();
builder.Services.AddScoped<PointOfSale.API.Services.Homepage.IHomepageSectionReadService, PointOfSale.API.Services.Homepage.HomepageSectionReadService>();
builder.Services.AddScoped<PointOfSale.API.Services.Homepage.IHomepageTemplateSeeder, PointOfSale.API.Services.Homepage.HomepageTemplateSeeder>();
builder.Services.AddScoped<PointOfSale.API.Services.Cms.ICmsPageContentValidator, PointOfSale.API.Services.Cms.CmsPageContentValidator>();
builder.Services.AddScoped<PointOfSale.API.Services.Cms.ICmsPageReadService, PointOfSale.API.Services.Cms.CmsPageReadService>();
builder.Services.AddScoped<PointOfSale.API.Services.Cms.ICmsPageService, PointOfSale.API.Services.Cms.CmsPageService>();
builder.Services.AddScoped<PointOfSale.API.Services.Cms.ICmsPagePreviewTokenService, PointOfSale.API.Services.Cms.CmsPagePreviewTokenService>();
builder.Services.AddScoped<PointOfSale.API.Services.Storefront.IStorefrontFooterReadService, PointOfSale.API.Services.Storefront.StorefrontFooterReadService>();
builder.Services.AddScoped<PointOfSale.API.Services.MarketplaceCms.IMarketplaceCmsPageContentValidator, PointOfSale.API.Services.MarketplaceCms.MarketplaceCmsPageContentValidator>();
builder.Services.AddScoped<PointOfSale.API.Services.MarketplaceCms.IMarketplaceCmsPageCacheService, PointOfSale.API.Services.MarketplaceCms.MarketplaceCmsPageCacheService>();
builder.Services.AddScoped<PointOfSale.API.Services.MarketplaceCms.IMarketplaceCmsPageReadService, PointOfSale.API.Services.MarketplaceCms.MarketplaceCmsPageReadService>();
builder.Services.AddScoped<PointOfSale.API.Services.MarketplaceCms.IMarketplaceCmsPageService, PointOfSale.API.Services.MarketplaceCms.MarketplaceCmsPageService>();
builder.Services.AddScoped<PointOfSale.API.Services.MarketplaceCms.IMarketplaceCmsPagePreviewTokenService, PointOfSale.API.Services.MarketplaceCms.MarketplaceCmsPagePreviewTokenService>();
builder.Services.AddScoped<PointOfSale.API.Services.MarketplaceFooter.IMarketplaceFooterReadService, PointOfSale.API.Services.MarketplaceFooter.MarketplaceFooterReadService>();
builder.Services.AddScoped<PointOfSale.API.Services.MarketplaceFooter.IMarketplaceFooterCacheService, PointOfSale.API.Services.MarketplaceFooter.MarketplaceFooterCacheService>();
builder.Services.AddScoped<PointOfSale.API.Services.MarketplaceTranslations.IMarketplaceTranslationCacheService, PointOfSale.API.Services.MarketplaceTranslations.MarketplaceTranslationCacheService>();
builder.Services.AddScoped<PointOfSale.API.Services.MarketplaceTranslations.IMarketplaceCategoryTranslationService, PointOfSale.API.Services.MarketplaceTranslations.MarketplaceCategoryTranslationService>();
builder.Services.AddScoped<PointOfSale.API.Services.MarketplaceTranslations.IMarketplaceProductLocalizationService, PointOfSale.API.Services.MarketplaceTranslations.MarketplaceProductLocalizationService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICommerceHubQueryService, PointOfSale.API.Services.CommerceHub.CommerceHubQueryService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICommerceHubGovernanceCacheInvalidationService, PointOfSale.API.Services.CommerceHub.CommerceHubGovernanceCacheInvalidationService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICommerceHubPublicVendorVisibilityService, PointOfSale.API.Services.CommerceHub.CommerceHubPublicVendorVisibilityService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICommerceHubSearchIntelligenceService, PointOfSale.API.Services.CommerceHub.CommerceHubSearchIntelligenceService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICommerceHubSearchIntentDetector, PointOfSale.API.Services.CommerceHub.CommerceHubSearchIntentDetector>();
builder.Services.AddSingleton<PointOfSale.Core.Pricing.ICommerceHubPriceAuthority, PointOfSale.Core.Pricing.CommerceHubPriceAuthority>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICommerceHubShippingService, PointOfSale.API.Services.CommerceHub.CommerceHubShippingService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICommerceHubCartService, PointOfSale.API.Services.CommerceHub.CommerceHubCartService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICommissionService, PointOfSale.API.Services.CommerceHub.CommissionService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.IPromoService, PointOfSale.API.Services.CommerceHub.PromoService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICommerceHubCheckoutService, PointOfSale.API.Services.CommerceHub.CommerceHubCheckoutService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.IInventoryConsistencyService, PointOfSale.API.Services.CommerceHub.InventoryConsistencyService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICommerceHubFinancialWorkflowService, PointOfSale.API.Services.CommerceHub.CommerceHubFinancialWorkflowService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.FinancialLedger.ICommerceHubFinancialLedgerService, PointOfSale.API.Services.CommerceHub.FinancialLedger.CommerceHubFinancialLedgerService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICommerceHubPaymentRecoveryAuthorityService, PointOfSale.API.Services.CommerceHub.CommerceHubPaymentRecoveryAuthorityService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICommerceHubPaymentService, PointOfSale.API.Services.CommerceHub.CommerceHubPaymentService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.IPaymentRuntime, PointOfSale.API.Services.CommerceHub.CommerceHubPaymentRuntime>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICommerceHubPaymentOrchestrationService, PointOfSale.API.Services.CommerceHub.CommerceHubPaymentOrchestrationService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICommerceHubEarningsService, PointOfSale.API.Services.CommerceHub.CommerceHubEarningsService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICommerceHubPayoutService, PointOfSale.API.Services.CommerceHub.CommerceHubPayoutService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICommerceHubRefundGatewayService, PointOfSale.API.Services.CommerceHub.CommerceHubRefundGatewayService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.CommerceHubRefundService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICommerceHubRefundService>(sp =>
    sp.GetRequiredService<PointOfSale.API.Services.CommerceHub.CommerceHubRefundService>());
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.IRefundCompletionValidator, PointOfSale.API.Services.CommerceHub.RefundCompletionValidator>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.Wallet.ICommerceHubWalletService, PointOfSale.API.Services.CommerceHub.Wallet.CommerceHubWalletService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICommerceHubVendorVisibilityService, PointOfSale.API.Services.CommerceHub.CommerceHubVendorVisibilityService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.Finance.ICommerceHubVendorDebtService, PointOfSale.API.Services.CommerceHub.Finance.CommerceHubVendorDebtService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.Finance.ICommerceHubFinancialSafetyGuardService, PointOfSale.API.Services.CommerceHub.Finance.CommerceHubFinancialSafetyGuardService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.Finance.ICommerceHubFinancialReadinessGate, PointOfSale.API.Services.CommerceHub.Finance.CommerceHubFinancialReadinessGate>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.Finance.ICommerceHubFinancialDashboardService, PointOfSale.API.Services.CommerceHub.Finance.CommerceHubFinancialDashboardService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.Finance.ICommerceHubVendorFinancialControlService, PointOfSale.API.Services.CommerceHub.Finance.CommerceHubVendorFinancialControlService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.Finance.ICommerceHubVendorPaymentsService, PointOfSale.API.Services.CommerceHub.Finance.CommerceHubVendorPaymentsService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.Finance.ICommerceHubVendorPayoutReadService, PointOfSale.API.Services.CommerceHub.Finance.CommerceHubVendorPayoutReadService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.Finance.ICommerceHubVendorPayoutMethodsService, PointOfSale.API.Services.CommerceHub.Finance.CommerceHubVendorPayoutMethodsService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.IRefundExecutionService, PointOfSale.API.Services.CommerceHub.RefundExecutionService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.IRefundExecutionRecoveryService, PointOfSale.API.Services.CommerceHub.RefundExecutionService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.IReturnManagementService, PointOfSale.API.Services.CommerceHub.ReturnManagementService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICommerceHubFinanceReadService, PointOfSale.API.Services.CommerceHub.CommerceHubFinanceReadService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICommerceHubFinanceReconciliationReadService, PointOfSale.API.Services.CommerceHub.CommerceHubFinanceReconciliationReadService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICommerceHubDailyFinancialAggregateService, PointOfSale.API.Services.CommerceHub.CommerceHubDailyFinancialAggregateService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.IFinancialReconciliationService, PointOfSale.API.Services.CommerceHub.FinancialReconciliationService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.IFinancialReconciliationAlertHook, PointOfSale.API.Services.CommerceHub.NoOpFinancialReconciliationAlertHook>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.CommerceHubFinanceReconciliationJob>();
builder.Services.AddHostedService<PointOfSale.API.Services.CommerceHub.CommerceHubUnresolvedPaymentReconciliationHostedService>();
builder.Services.AddHostedService<PointOfSale.API.Services.CommerceHub.CommerceHubPayoutReconciliationHostedService>();
builder.Services.AddHostedService<PointOfSale.API.Services.CommerceHub.CommerceHubRefundRecoveryHostedService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.CommerceHubInventoryReconciliationJob>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICommerceHubProjectionReadinessService, PointOfSale.API.Services.CommerceHub.CommerceHubProjectionReadinessService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICommerceHubOrderManagementService, PointOfSale.API.Services.CommerceHub.CommerceHubOrderManagementService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICommerceHubFinancialAuditService, PointOfSale.API.Services.CommerceHub.CommerceHubFinancialAuditService>();
builder.Services.AddSingleton<PointOfSale.API.Services.CommerceHub.ICommerceHubOrderAccessService, PointOfSale.API.Services.CommerceHub.CommerceHubOrderAccessService>();
builder.Services.AddScoped<PointOfSale.Infrastructure.Identity.CanonicalIdentityReconciliationService>();
builder.Services.AddHostedService<PointOfSale.Infrastructure.Identity.CanonicalIdentityReconciliationHostedService>();
builder.Services.AddHostedService<PointOfSale.API.Services.CommerceHub.CommerceHubProjectionReconciliationService>();
builder.Services.AddHostedService<PointOfSale.API.Services.Storefront.StorefrontOrderProjectionReconciliationService>();
builder.Services.AddHostedService<PointOfSale.API.Services.CommerceHub.CommerceHubDailyFinancialAggregateRefreshService>();
builder.Services.AddHostedService<PointOfSale.API.Services.CommerceHub.CommerceHubPaymentCompensationOutboxWorker>();
builder.Services.AddScoped<PointOfSale.Infrastructure.Operations.ISystemSchemaVersionService, PointOfSale.Infrastructure.Operations.SystemSchemaVersionService>();
builder.Services.AddScoped<PointOfSale.API.CommerceHub.Services.ICommerceHubSearchQueryParser, PointOfSale.API.CommerceHub.Services.CommerceHubSearchQueryParser>();
builder.Services.AddScoped<PointOfSale.API.CommerceHub.Services.ICommerceHubAttributeService, PointOfSale.API.CommerceHub.Services.CommerceHubAttributeService>();
builder.Services.AddScoped<PointOfSale.API.CommerceHub.Handlers.IPublishToCommerceHubHandler, PointOfSale.API.CommerceHub.Handlers.PublishToCommerceHubHandler>();
builder.Services.AddScoped<PointOfSale.API.CommerceHub.Handlers.IUpdateCommerceHubListingHandler, PointOfSale.API.CommerceHub.Handlers.UpdateCommerceHubListingHandler>();
builder.Services.AddScoped<PointOfSale.API.CommerceHub.Handlers.IUnpublishFromCommerceHubHandler, PointOfSale.API.CommerceHub.Handlers.UnpublishFromCommerceHubHandler>();
builder.Services.AddScoped<PointOfSale.API.Development.CommerceHub.IDevelopmentCommerceHubSeedService, PointOfSale.API.Development.CommerceHub.DevelopmentCommerceHubSeedService>();
builder.Services.AddScoped<PointOfSale.Infrastructure.Jobs.CommerceHubReindexService>();
builder.Services.AddOptions<PointOfSale.Infrastructure.Jobs.CommerceHubReindexOptions>()
    .Bind(builder.Configuration.GetSection(PointOfSale.Infrastructure.Jobs.CommerceHubReindexOptions.SectionName))
    .Validate(options => options.ParallelBatchSize >= 1 && options.ParallelBatchSize <= 32,
        "CommerceHubReindex:ParallelBatchSize must be between 1 and 32.")
    .Validate(options => options.SearchSyncStatusBatchSize >= 1 && options.SearchSyncStatusBatchSize <= 1000,
        "CommerceHubReindex:SearchSyncStatusBatchSize must be between 1 and 1000.")
    .Validate(options => options.InterBatchDelayMilliseconds >= 0 && options.InterBatchDelayMilliseconds <= 60000,
        "CommerceHubReindex:InterBatchDelayMilliseconds must be between 0 and 60000.")
    .ValidateOnStart();
builder.Services.AddScoped<PointOfSale.Infrastructure.Jobs.ICommerceHubReindexService>(sp =>
    sp.GetRequiredService<PointOfSale.Infrastructure.Jobs.CommerceHubReindexService>());

// =============================================================================
// PROMOTION RANKING SERVICE (Shopify-Grade Promotion-Aware Search)
// Provides cached promotion ranking configuration for Elasticsearch queries.
// Redis cache with 5-minute TTL, falls back to database, then defaults.
// =============================================================================
builder.Services.AddScoped<PointOfSale.API.Services.Storefront.IPromotionRankingService, PointOfSale.API.Services.Storefront.PromotionRankingService>();

// =============================================================================
// CART SERVICE (Redis-First, Multi-Tenant)
// Cart is stored in Redis only (ephemeral, TTL-based).
// MongoDB is used for product validation only.
// =============================================================================
builder.Services.AddScoped<PointOfSale.API.Services.Cart.ICartService, PointOfSale.API.Services.Cart.CartService>();
builder.Services.AddScoped<PointOfSale.API.Services.Pricing.ITaxCalculationService, PointOfSale.API.Services.Pricing.TaxCalculationService>();
builder.Services.AddScoped<PointOfSale.API.Services.Pricing.IShippingCalculationService, PointOfSale.API.Services.Pricing.ShippingCalculationService>();
builder.Services.AddScoped<PointOfSale.API.Services.Pricing.IStorefrontPricingService, PointOfSale.API.Services.Pricing.StorefrontPricingService>();

// =============================================================================
// CHECKOUT SERVICE (Cart → Order)
// Checkout converts cart to order with:
// - Redis: Distributed lock, Idempotency, Cart snapshot
// - PostgreSQL: Source of Truth for Orders
// - MongoDB: Read-only validation
// - Payment: Intent only (no execution)
// =============================================================================
builder.Services.AddOptions<PointOfSale.API.Services.Checkout.CheckoutExecutionLockOptions>()
    .Bind(builder.Configuration.GetSection("CheckoutExecutionLock"))
    .Validate(o => o.LockTtl > TimeSpan.Zero, 
        "LockTtl must be positive")
    .Validate(o => o.HeartbeatInterval > TimeSpan.Zero, 
        "HeartbeatInterval must be positive")
    .Validate(o => o.HeartbeatInterval < o.LockTtl, 
        "HeartbeatInterval must be less than LockTtl")
    .ValidateOnStart();
builder.Services.AddSingleton<PointOfSale.API.Services.Checkout.CheckoutLockTelemetry>();
builder.Services.AddScoped<PointOfSale.API.Services.Checkout.ICheckoutExecutionLockManager, PointOfSale.API.Services.Checkout.CheckoutExecutionLockManager>();
builder.Services.AddScoped<PointOfSale.API.Services.Checkout.ICheckoutRequestIdempotencyCoordinator, PointOfSale.API.Services.Checkout.CheckoutRequestIdempotencyCoordinator>();
builder.Services.AddScoped<PointOfSale.API.Services.Checkout.ICheckoutService, PointOfSale.API.Services.Checkout.CheckoutService>();

// =============================================================================
// PHASE 11.4.1: PRODUCT VARIANT STOCK REPOSITORY
// Variant-based inventory reservation for checkout.
// Uses ProductVariantStocks (NOT WarehouseStocks).
// Stock is per-Variant (SKU), not per-Product.
// =============================================================================
builder.Services.AddScoped<PointOfSale.Infrastructure.Data.Repositories.IProductVariantStockRepository, PointOfSale.Infrastructure.Data.Repositories.ProductVariantStockRepository>();

// =============================================================================
// PAYMENT SERVICE (Payment Intents + Webhooks + COD)
// Payments are processed via:
// - Redis: Idempotency for intents and webhooks
// - PostgreSQL: Source of Truth for PaymentIntents and Orders
// - Webhooks: Source of truth for online payment status
// - COD: Immediate confirmation, no gateway
// =============================================================================
builder.Services.AddScoped<PointOfSale.API.Services.Payments.IPaymentService, PointOfSale.API.Services.Payments.PaymentService>();
builder.Services.AddScoped<PointOfSale.API.Services.Payments.IPaymentRequestAuthorizationService, PointOfSale.API.Services.Payments.PaymentRequestAuthorizationService>();
builder.Services.AddScoped<PointOfSale.API.Services.Payments.IPaymentWebhookTenantResolver, PointOfSale.API.Services.Payments.PaymentWebhookTenantResolver>();
builder.Services.AddScoped<PointOfSale.API.Services.Payments.IPaymentWebhookRequestPipeline, PointOfSale.API.Services.Payments.PaymentWebhookRequestPipeline>();
builder.Services.AddScoped<PointOfSale.API.Services.Payments.StorefrontUnresolvedPaymentReconciliationJob>();
builder.Services.AddHostedService<PointOfSale.API.Services.Payments.StorefrontUnresolvedPaymentReconciliationHostedService>();
builder.Services.AddPaymentRuntimeFoundation();

// Manual Wallet Cleanup Job (P3.9)
builder.Services.AddScoped<PointOfSale.API.Jobs.ManualWalletProofCleanupJob>();
builder.Services.AddScoped<PointOfSale.API.Jobs.RefundEvidenceCleanupJob>();
builder.Services.AddScoped<PointOfSale.Infrastructure.Jobs.CommerceHubCartCleanupJob>();
builder.Services.AddScoped<PointOfSale.Infrastructure.Jobs.OrderRequestIdempotencyCleanupJob>();
builder.Services.AddScoped<PointOfSale.Infrastructure.Jobs.InventoryReservationCleanupJob>();

// =============================================================================
// PHASE 11.4.2.A: PAYMENT ALLOCATION SERVICE
// Allocates payments across OrderItems (variants) for SKU-level financial tracking.
// MONEY IS TRACKED ON ORDER ITEMS (VARIANTS), NOT ON ORDERS.
// =============================================================================
builder.Services.AddScoped<PointOfSale.API.Services.Payments.IPaymentAllocationService, PointOfSale.API.Services.Payments.PaymentAllocationService>();

// =============================================================================
// STRIPE PAYMENT SERVICE (Phase 10.4.D4 - Stripe Integration)
// Stripe integration using Payment Intents API.
// - Tenant-aware (each tenant configures their own Stripe keys)
// - Secrets encrypted using Data Protection API
// - Webhooks ONLY for final payment status
// - Idempotent operations
// =============================================================================
builder.Services.AddScoped<IStripeRuntimeCredentialAccessor, StripeRuntimeCredentialAccessor>();
builder.Services.AddScoped<IStripeTenantConfigurationResolver, StripeTenantConfigurationResolver>();
builder.Services.AddScoped<IStripePaymentIntentClient, TenantScopedStripePaymentIntentClient>();
builder.Services.AddScoped<IStripePaymentIntentAdapter, StripePaymentIntentAdapter>();
builder.Services.AddScoped<IStripeRuntimeBridge, StripeRuntimeBridge>();
builder.Services.AddScoped<IPaymobTenantConfigurationResolver, PaymobTenantConfigurationResolver>();
builder.Services.AddScoped<IPaymobRuntimeBridge, PaymobRuntimeBridge>();
builder.Services.AddScoped<IPaymobWebhookEvidenceAdapter, PaymobWebhookEvidenceAdapter>();
builder.Services.AddScoped<IStripeRuntimeApplicationService, StripeRuntimeApplicationService>();
builder.Services.AddScoped<IPaymobRuntimeApplicationService, PaymobRuntimeApplicationService>();
builder.Services.AddScoped<IStripeWebhookRuntimeIntegrationService, StripeWebhookRuntimeIntegrationService>();
builder.Services.AddScoped<IPaymobWebhookRuntimeIntegrationService, PaymobWebhookRuntimeIntegrationService>();
builder.Services.AddScoped<PointOfSale.API.Services.Payments.Stripe.IStripePaymentService, PointOfSale.API.Services.Payments.Stripe.StripePaymentService>();

// Payment Intent Client Secret Protector (for secure storage of sensitive credentials)
builder.Services.AddSingleton<PointOfSale.API.Services.Payments.IPaymentIntentClientSecretProtector, PointOfSale.API.Services.Payments.PaymentIntentClientSecretProtector>();

// =============================================================================
// BACKGROUND TASK QUEUE (RedirectUrl Recovery)
// Channel-based queue for processing RedirectUrl recovery tasks asynchronously.
// - Bounded channel with configurable capacity (default: 1000, min: 1, max: 100,000)
// - Wait/Blocking behavior: waits for space instead of dropping writes (critical for payment recovery)
// - Hosted service processes queue with graceful shutdown support
// =============================================================================
builder.Services.AddSingleton<PointOfSale.API.Services.BackgroundTasks.IBackgroundTaskQueue<PointOfSale.API.Services.BackgroundTasks.RedirectUrlRecoveryWorkItem>>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<PointOfSale.API.Services.BackgroundTasks.BackgroundTaskQueue<PointOfSale.API.Services.BackgroundTasks.RedirectUrlRecoveryWorkItem>>>();
    var configuration = sp.GetRequiredService<IConfiguration>();
    
    // Read capacity from configuration with validation
    var configuredCapacity = configuration.GetValue<int?>("RedirectUrlRecoveryQueue:Capacity");
    const int defaultCapacity = 1000;
    const int minCapacity = 1;
    const int maxCapacity = 100_000;
    
    var capacity = configuredCapacity.HasValue
        ? Math.Clamp(configuredCapacity.Value, minCapacity, maxCapacity)
        : defaultCapacity;
    
    return new PointOfSale.API.Services.BackgroundTasks.BackgroundTaskQueue<PointOfSale.API.Services.BackgroundTasks.RedirectUrlRecoveryWorkItem>(capacity, logger);
});
builder.Services.AddHostedService<PointOfSale.API.Services.BackgroundTasks.RedirectUrlRecoveryHostedService>();
builder.Services.AddScoped<PointOfSale.API.Services.Checkout.CheckoutPaymentIntentRecoveryService>();
builder.Services.AddHostedService<PointOfSale.API.Services.Checkout.CheckoutPaymentIntentRecoveryHostedService>();

// =============================================================================
// PAYMOB PAYMENT SERVICE (Phase 10.4.D5 - Paymob Integration)
// Paymob integration using Intention API.
// - Tenant-aware (each tenant configures their own Paymob keys)
// - Secrets encrypted using Data Protection API
// - Webhooks with HMAC verification for payment status
// - Idempotent operations
// - iFrame-based payment flow
// =============================================================================
builder.Services.AddScoped<PointOfSale.API.Services.Payments.Paymob.IPaymobPaymentService, PointOfSale.API.Services.Payments.Paymob.PaymobPaymentService>();
builder.Services.AddScoped<PointOfSale.API.Services.Payments.ProviderContracts.IRefundGateway, PointOfSale.API.Services.Payments.ProviderContracts.RefundGateway>();
builder.Services.AddScoped<PointOfSale.API.Services.Payments.ProviderContracts.IProviderRefundGateway, PointOfSale.API.Services.Payments.Stripe.StripeCommerceHubRefundGateway>();
builder.Services.AddScoped<PointOfSale.API.Services.Payments.ProviderContracts.IProviderRefundGateway, PointOfSale.API.Services.Payments.Paymob.PaymobCommerceHubRefundGateway>();
builder.Services.AddScoped<PointOfSale.API.Services.Payments.ProviderContracts.IProviderRefundGateway, PointOfSale.API.Services.Payments.Cod.CodCommerceHubRefundGateway>();

// =============================================================================
// MONGODB PROJECTION HEALTH COORDINATOR
// Singleton service to track MongoDB projection health across all requests.
// Prevents per-request health state loss and coordinates recovery attempts.
// =============================================================================
builder.Services.AddSingleton<PointOfSale.Infrastructure.MongoDB.Services.IMongoProjectionHealthCoordinator, PointOfSale.Infrastructure.MongoDB.Services.MongoProjectionHealthCoordinator>();

// =============================================================================
// STOREFRONT SERVICES (Read-Only APIs)
// These services provide read-only access to POS data for e-commerce storefront.
// - Shipping Methods: Read from MongoDB (projected via events), cached in Redis
// - ARCHITECTURE CLOSURE: NO SQL ACCESS - 100% CQRS purity
// =============================================================================
builder.Services.AddScoped<PointOfSale.Infrastructure.MongoDB.Services.IShippingMethodReadRepository, PointOfSale.Infrastructure.MongoDB.Services.ShippingMethodReadRepository>();
builder.Services.AddScoped<PointOfSale.Infrastructure.MongoDB.Services.IPaymentMethodReadRepository, PointOfSale.Infrastructure.MongoDB.Services.PaymentMethodReadRepository>();

// =============================================================================
// ORDER READ REPOSITORY (Phase 10.5.A - Order Lifecycle State Machine)
// MongoDB-only reads for Storefront orders.
// NO SQL reads allowed for orders in Storefront.
// =============================================================================
builder.Services.AddScoped<PointOfSale.Infrastructure.MongoDB.Services.IOrderReadRepository, PointOfSale.Infrastructure.MongoDB.Services.OrderReadRepository>();
builder.Services.AddScoped<PointOfSale.Infrastructure.MongoDB.Services.ICommerceHubOrderReadRepository, PointOfSale.Infrastructure.MongoDB.Services.CommerceHubOrderReadRepository>();

builder.Services.AddScoped<PointOfSale.API.Services.Storefront.IShippingMethodService, PointOfSale.API.Services.Storefront.ShippingMethodService>();

// =============================================================================
// SHIPMENT SERVICE (Lifecycle Management)
// Shipments are created after checkout and track fulfillment lifecycle:
// - Pending → Packed → Shipped → Delivered
// - Event-driven with status transition validation
// =============================================================================
builder.Services.AddScoped<PointOfSale.API.Services.Shipping.IShipmentService, PointOfSale.API.Services.Shipping.ShipmentService>();
builder.Services.AddHostedService<PointOfSale.API.Services.Shipping.ShipmentAuthorityRecoveryHostedService>();

// =============================================================================
// CARRIER-AGNOSTIC SHIPPING MODULE
// 
// Full carrier abstraction layer:
// - IShippingCarrierAdapter: Interface for all carrier integrations
// - ShippingCarrierRegistry: Discovers and manages adapters
// - ShippingQuoteService: Returns quotes from all enabled carriers
// - CarrierShipmentService: Creates/manages shipments with carriers
// - TrackingSyncJob: Background job for tracking status sync
// 
// ARCHITECTURAL RULES:
// - Order ≠ Shipment ≠ Carrier (fully decoupled)
// - Adapters NEVER write to DB (service orchestrates)
// - All carrier-specific data in Shipment.CarrierMetadata
// - Frontend sees normalized quotes/status only
// =============================================================================

// Carrier Adapter Registry (discovers all registered adapters)
builder.Services.AddSingleton<PointOfSale.Infrastructure.Shipping.IShippingCarrierRegistry, PointOfSale.Infrastructure.Shipping.ShippingCarrierRegistry>();

// Carrier Credential Encryption Service (P2 Security Hardening)
// Encrypts/decrypts API credentials using Data Protection API
builder.Services.AddSingleton<PointOfSale.Infrastructure.Shipping.ICarrierCredentialEncryptionService, PointOfSale.Infrastructure.Shipping.CarrierCredentialEncryptionService>();

// Shipping Quote Service (returns quotes from all enabled carriers)
builder.Services.AddScoped<PointOfSale.Infrastructure.Shipping.IShippingQuoteService, PointOfSale.Infrastructure.Shipping.ShippingQuoteService>();

// Carrier Shipment Service (creates/manages shipments with carriers)
builder.Services.AddScoped<PointOfSale.Infrastructure.Shipping.ICarrierShipmentService, PointOfSale.Infrastructure.Shipping.CarrierShipmentService>();
builder.Services.AddScoped<PointOfSale.Core.Interfaces.ShippingAdministration.ICarrierAccountAdministrationRuntime, PointOfSale.Infrastructure.Shipping.Administration.CarrierAccountAdministrationRuntime>();
builder.Services.AddScoped<PointOfSale.API.Services.Admin.IShippingAdministrationApiService, PointOfSale.API.Services.Admin.ShippingAdministrationApiService>();
builder.Services.AddScoped<PointOfSale.API.Services.Shipping.IShippingWebhookTenantResolver, PointOfSale.API.Services.Shipping.ShippingWebhookTenantResolver>();
builder.Services.AddScoped<PointOfSale.API.Services.Shipping.IShippingWebhookRequestPipeline, PointOfSale.API.Services.Shipping.ShippingWebhookRequestPipeline>();

// Tracking Sync Job (background job for tracking status sync)
// IMPORTANT: Schedule this job with Hangfire after app.Build() - see TrackingSyncJobRegistration
builder.Services.AddScoped<PointOfSale.Infrastructure.Shipping.BackgroundJobs.ITrackingSyncJob, PointOfSale.Infrastructure.Shipping.BackgroundJobs.TrackingSyncJob>();

// =============================================================================
// CARRIER ADAPTERS (Real Integrations)
// 
// Each adapter is registered as IShippingCarrierAdapter.
// The registry auto-discovers all registered adapters.
// 
// To add a new carrier:
// 1. Create adapter implementing IShippingCarrierAdapter
// 2. Register here with AddSingleton
// 3. Registry automatically discovers it
// =============================================================================

// MANUAL ADAPTER - FIRST-CLASS CARRIER (not a fallback!)
// Used when merchant handles fulfillment themselves. No external API calls.
// CRITICAL: This MUST be registered for "manual" to be a valid carrier code.
builder.Services.AddSingleton<PointOfSale.Infrastructure.Shipping.IShippingCarrierAdapter, PointOfSale.Infrastructure.Shipping.Adapters.ManualCarrierDefinition>();

// Bosta Adapter (Egypt market - production ready)
builder.Services.AddSingleton<PointOfSale.Infrastructure.Shipping.IShippingCarrierAdapter, PointOfSale.Infrastructure.Shipping.Adapters.BostaAdapter>();

// Aramex Adapter (MENA region - production ready)
builder.Services.AddSingleton<PointOfSale.Infrastructure.Shipping.IShippingCarrierAdapter, PointOfSale.Infrastructure.Shipping.Adapters.AramexAdapter>();

// Mylerz Adapter (Egypt last-mile delivery - fails closed until official contract is frozen)
builder.Services.AddSingleton<PointOfSale.Infrastructure.Shipping.IShippingCarrierAdapter, PointOfSale.Infrastructure.Shipping.Adapters.MylerzAdapter>();

// DHL Adapter (International express - production ready)
builder.Services.AddSingleton<PointOfSale.Infrastructure.Shipping.IShippingCarrierAdapter, PointOfSale.Infrastructure.Shipping.Adapters.DhlAdapter>();

// FedEx Adapter (runtime foundation only - registered but all operations fail closed)
builder.Services.AddSingleton<PointOfSale.Infrastructure.Shipping.Adapters.FedExOptions>();
builder.Services.AddSingleton<PointOfSale.Infrastructure.Shipping.Adapters.IFedExOAuthClient, PointOfSale.Infrastructure.Shipping.Adapters.FedExOAuthClient>();
builder.Services.AddSingleton<PointOfSale.Infrastructure.Shipping.Adapters.IFedExTransportClient, PointOfSale.Infrastructure.Shipping.Adapters.FedExTransportClient>();
builder.Services.AddSingleton<PointOfSale.Infrastructure.Shipping.IShippingCarrierAdapter, PointOfSale.Infrastructure.Shipping.Adapters.FedExAdapter>();



// =============================================================================
// ADMIN ORDER SERVICE (Phase 6.1 - Admin Order Management)
// Admin-only APIs for operations teams to:
// - View orders and order details
// - Manually update order status
// - Cancel orders safely with side effects
// PostgreSQL is the ONLY source of truth. MongoDB is NOT used.
// =============================================================================
builder.Services.AddScoped<PointOfSale.API.Services.Admin.IAdminOrderService, PointOfSale.API.Services.Admin.AdminOrderService>();
builder.Services.AddScoped<PointOfSale.API.Services.PhoneRisk.IPhoneRiskProfileService, PointOfSale.API.Services.PhoneRisk.PhoneRiskProfileService>();
builder.Services.AddScoped<PointOfSale.API.Services.PhoneRisk.IOrderVelocityService, PointOfSale.API.Services.PhoneRisk.OrderVelocityService>();

// =============================================================================
// ORDER STATUS SYNC SERVICE (Phase 2 - Event-Driven Order Status Sync)
// Syncs OrderStatus automatically based on Shipment and Payment events.
// Orders REACT to events, they do NOT control them.
// One-way sync only (events → orders).
// =============================================================================
builder.Services.AddScoped<PointOfSale.Core.Interfaces.IOrderStatusSyncService, PointOfSale.API.Services.OrderStatusSyncService>();

// =============================================================================
// ADMIN SHIPMENT SERVICE (Phase 6.2 - Admin Shipment Operations)
// Admin-only APIs for operations teams to:
// - View shipments and shipment details
// - Update shipment status (with strict lifecycle rules)
// - Update tracking information
// - Cancel shipments safely
// PostgreSQL is the ONLY source of truth. Tenant isolation via Shipment.TenantId
// =============================================================================
builder.Services.AddScoped<PointOfSale.API.Services.Admin.IAdminShipmentService, PointOfSale.API.Services.Admin.AdminShipmentService>();

// =============================================================================
// ADMIN INVENTORY SERVICE (Phase 6.3 - Admin Inventory Operations)
// Admin-only APIs for operations teams to:
// - View inventory and stock levels
// - Adjust stock (with atomic transactions and idempotency)
// - View stock movement history (audit log)
// PostgreSQL is the ONLY source of truth. Tenant isolation via Warehouse/Product.TenantId
// Idempotency via Redis using X-Request-Id header
// =============================================================================
builder.Services.AddScoped<PointOfSale.API.Services.Admin.IAdminInventoryService, PointOfSale.API.Services.Admin.AdminInventoryService>();

// =============================================================================
// ADMIN STORE CONFIG SERVICE (Phase 7.1 - Theme & Store Config System)
// Admin-only APIs for store configuration management:
// - Get/Update store appearance settings (colors, fonts, layout)
// - Get/Update behavior flags (guest checkout, search, etc.)
// PostgreSQL is the ONLY source of truth. Redis cache for storefront reads.
// =============================================================================
builder.Services.AddScoped<PointOfSale.API.Services.Admin.IAdminStoreConfigService, PointOfSale.API.Services.Admin.AdminStoreConfigService>();
builder.Services.AddScoped<PointOfSale.API.Services.Admin.IAdminThemeActivationService, PointOfSale.API.Services.Admin.AdminThemeActivationService>();

// =============================================================================
// ADMIN CMS SERVICE (Phase 9.11 - Static CMS Pages & Navigation Engine)
// Admin-only APIs for CMS page and navigation management:
// - Create/Update/Delete CMS pages (contact, about, privacy, terms, custom)
// - Header/Footer navigation management
// - Draft / Live separation enforced
// PostgreSQL is the ONLY source of truth. MongoDB projection via events.
// =============================================================================
builder.Services.AddScoped<PointOfSale.API.Services.Admin.NavigationAdmissionService>();
builder.Services.AddScoped<PointOfSale.API.Services.Admin.IAdminCmsService, PointOfSale.API.Services.Admin.AdminCmsService>();
builder.Services.AddScoped<PointOfSale.API.Services.Admin.ICmsSeedService, PointOfSale.API.Services.Admin.CmsSeedService>();
builder.Services.AddScoped<PointOfSale.API.Services.Admin.FooterAuthoringValidator>();
builder.Services.AddScoped<PointOfSale.API.Services.Admin.FooterAuthoringMapper>();
builder.Services.AddScoped<PointOfSale.API.Services.Admin.IFooterAuthoringService, PointOfSale.API.Services.Admin.FooterAuthoringService>();

// =============================================================================
// STOREFRONT ADMIN HARDENING - P3 SERVICES
// =============================================================================

// Preview Service - Reads draft content directly from PostgreSQL
// Used by storefront to render draft content when ?preview=true
builder.Services.AddScoped<PointOfSale.API.Services.Storefront.IStorefrontPreviewService, PointOfSale.API.Services.Storefront.StorefrontPreviewService>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddScoped<PointOfSale.API.Services.Storefront.IStorefrontThemePreviewTokenService, PointOfSale.API.Services.Storefront.StorefrontThemePreviewTokenService>();
builder.Services.AddScoped<PointOfSale.API.Services.Storefront.IStorefrontThemePreviewService, PointOfSale.API.Services.Storefront.StorefrontThemePreviewService>();

// Audit Service - Records all publish/rollback operations
// Provides compliance trail for support and debugging
builder.Services.AddScoped<PointOfSale.API.Services.Admin.IStoreConfigAuditService, PointOfSale.API.Services.Admin.StoreConfigAuditService>();

// Distributed Lock Service - Redis-based distributed locking
// Prevents race conditions in concurrent operations
builder.Services.AddScoped<PointOfSale.Infrastructure.Redis.Services.IDistributedLockService, PointOfSale.Infrastructure.Redis.Services.DistributedLockService>();

// Publish Lock Service - Prevents concurrent publish operations per tenant
// Returns 423 Locked when publish is already in progress
builder.Services.AddScoped<PointOfSale.API.Services.Admin.IPublishLockService, PointOfSale.API.Services.Admin.PublishLockService>();

// =============================================================================
// ADMIN ONLINE CUSTOMER SESSION SERVICE (Online Session Management)
// Admin-only APIs for security teams to:
// - View all sessions for an online customer
// - Revoke single session (immediate JWT blacklist)
// - Revoke all sessions (force logout everywhere)
// - Investigate security incidents (token reuse, suspicious activity)
// PostgreSQL is source of truth. MongoDB for read-optimized admin view.
// Redis is cache only for immediate blacklist lookups.
// =============================================================================
builder.Services.AddScoped<PointOfSale.API.Services.Admin.IOnlineCustomerSessionAdminService, PointOfSale.API.Services.Admin.OnlineCustomerSessionAdminService>();

// =============================================================================
// PLATFORM REGISTRATION SERVICE (Phase 8.0 - Tenant Registration)
// Public API for new merchant registration:
// - Atomic tenant bootstrap (all-or-nothing transaction)
// - Subdomain validation and availability check
// - Automatic JWT token generation for immediate login
// =============================================================================
builder.Services.AddScoped<PointOfSale.API.Services.Platform.ITenantRegistrationService, PointOfSale.API.Services.Platform.TenantRegistrationService>();

// =============================================================================
// PLATFORM SECURITY SERVICES (Phase 8.3 - Hardening & Abuse Protection)
// Protects public registration endpoints:
// - Rate limiting (Redis-backed, per IP per endpoint)
// - Idempotency (X-Request-Id header support)
// - Abuse detection (lightweight heuristics)
// - Audit logging (structured, privacy-preserving)
// =============================================================================
builder.Services.Configure<PointOfSale.API.Configuration.PlatformSecuritySettings>(
    builder.Configuration.GetSection(PointOfSale.API.Configuration.PlatformSecuritySettings.SectionName));
builder.Services.AddScoped<PointOfSale.API.Services.Platform.IRateLimitService, PointOfSale.API.Services.Platform.RateLimitService>();
builder.Services.AddScoped<PointOfSale.API.Services.Platform.IIdempotencyService, PointOfSale.API.Services.Platform.IdempotencyService>();
builder.Services.AddScoped<PointOfSale.API.Services.Platform.IPlatformAuditLogger, PointOfSale.API.Services.Platform.PlatformAuditLogger>();
builder.Services.AddScoped<PointOfSale.API.Services.Platform.IAbuseDetectionService, PointOfSale.API.Services.Platform.AbuseDetectionService>();

// =============================================================================
// EMAIL VERIFICATION SERVICES (Phase 8.4 - Email Verification & Account Trust)
// Provides:
// - Token generation and validation
// - Email sending (logging mock for dev, SMTP for prod)
// - Rate-limited resend functionality
// =============================================================================
builder.Services.Configure<PointOfSale.API.Configuration.EmailVerificationSettings>(
    builder.Configuration.GetSection(PointOfSale.API.Configuration.EmailVerificationSettings.SectionName));
builder.Services.Configure<PointOfSale.API.Services.Auth.SmtpSettings>(
    builder.Configuration.GetSection(PointOfSale.API.Services.Auth.SmtpSettings.SectionName));
builder.Services.AddScoped<PointOfSale.API.Services.Auth.IEmailVerificationService, PointOfSale.API.Services.Auth.EmailVerificationService>();
builder.Services.AddScoped<PointOfSale.API.SuperAdmin.Services.ISuperAdminAuthService, PointOfSale.API.SuperAdmin.Services.SuperAdminAuthService>();
builder.Services.AddScoped<PointOfSale.API.SuperAdmin.Services.ISuperAdminManagementService, PointOfSale.API.SuperAdmin.Services.SuperAdminManagementService>();
builder.Services.AddScoped<PointOfSale.API.SuperAdmin.Services.ISuperAdminAuditService, PointOfSale.API.SuperAdmin.Services.SuperAdminAuditService>();
builder.Services.AddScoped<PointOfSale.API.SuperAdmin.Infrastructure.ISuperAdminPlatformAuthority, PointOfSale.API.SuperAdmin.Infrastructure.SuperAdminPlatformAuthority>();
builder.Services.AddScoped<PointOfSale.API.SuperAdmin.Services.ISuperAdminFinanceOverviewService, PointOfSale.API.SuperAdmin.Services.SuperAdminFinanceOverviewService>();
builder.Services.AddScoped<PointOfSale.API.SuperAdmin.Services.ISuperAdminFinancialDashboardService, PointOfSale.API.SuperAdmin.Services.SuperAdminFinancialDashboardService>();
builder.Services.AddScoped<PointOfSale.API.SuperAdmin.Services.ISuperAdminVendorFinanceService, PointOfSale.API.SuperAdmin.Services.SuperAdminVendorFinanceService>();
builder.Services.AddScoped<PointOfSale.API.SuperAdmin.Services.ISuperAdminPayoutsService, PointOfSale.API.SuperAdmin.Services.SuperAdminPayoutsService>();
builder.Services.AddScoped<PointOfSale.API.SuperAdmin.Services.ISuperAdminPayoutOrchestrationService, PointOfSale.API.SuperAdmin.Services.SuperAdminPayoutOrchestrationService>();
builder.Services.AddScoped<PointOfSale.API.SuperAdmin.Services.ISuperAdminCommissionManagementService, PointOfSale.API.SuperAdmin.Services.SuperAdminCommissionManagementService>();
builder.Services.AddScoped<PointOfSale.API.SuperAdmin.Services.ISuperAdminPromoCodeManagementService, PointOfSale.API.SuperAdmin.Services.SuperAdminPromoCodeManagementService>();
builder.Services.AddScoped<PointOfSale.API.SuperAdmin.Services.ISuperAdminHomepageSectionManagementService, PointOfSale.API.SuperAdmin.Services.SuperAdminHomepageSectionManagementService>();
builder.Services.AddScoped<PointOfSale.API.SuperAdmin.Services.ISuperAdminOrdersService, PointOfSale.API.SuperAdmin.Services.SuperAdminOrdersService>();
builder.Services.AddScoped<PointOfSale.API.SuperAdmin.Services.ISuperAdminRefundsService, PointOfSale.API.SuperAdmin.Services.SuperAdminRefundsService>();
builder.Services.AddScoped<PointOfSale.API.Services.Admin.IPaymentProviderAdministrationService, PointOfSale.API.Services.Admin.PaymentProviderAdministrationService>();
builder.Services.AddScoped<PointOfSale.API.Services.Admin.IPaymentProviderDashboardService, PointOfSale.API.Services.Admin.PaymentProviderDashboardService>();
builder.Services.AddScoped<PointOfSale.API.Services.Admin.IPaymentProviderActionService, PointOfSale.API.Services.Admin.PaymentProviderActionService>();
builder.Services.AddScoped<PointOfSale.API.Services.Admin.IPaymentProviderSecretService, PointOfSale.API.Services.Admin.PaymentProviderSecretService>();
builder.Services.AddScoped<PointOfSale.API.Services.Admin.IPaymentProviderConnectivityTester, PointOfSale.API.Services.Admin.PaymobConnectivityTester>();
builder.Services.AddScoped<PointOfSale.API.Services.Governance.IUserGovernanceService, PointOfSale.API.Services.Governance.UserGovernanceService>();
builder.Services.AddSingleton<PointOfSale.API.SuperAdmin.Services.ISuperAdminLoginRateLimiter, PointOfSale.API.SuperAdmin.Services.SuperAdminLoginRateLimiter>();
builder.Services.AddScoped<PointOfSale.API.SuperAdmin.Infrastructure.SuperAdminModelValidationFilter>();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, PointOfSale.API.SuperAdmin.Infrastructure.SuperAdminAuthorizationMiddlewareResultHandler>();
builder.Services.AddHostedService<PointOfSale.API.SuperAdmin.Services.SuperAdminRootSeeder>();
builder.Services.AddHostedService<PointOfSale.API.Services.MarketplaceCms.MarketplaceCmsPageSeeder>();

// Use SMTP if enabled, otherwise use logging sender (for dev)
var smtpEnabled = builder.Configuration.GetValue<bool>("Smtp:Enabled");
if (smtpEnabled)
{
    builder.Services.AddScoped<PointOfSale.API.Services.Auth.IEmailSender, PointOfSale.API.Services.Auth.SmtpEmailSender>();
}
else
{
    builder.Services.AddScoped<PointOfSale.API.Services.Auth.IEmailSender, PointOfSale.API.Services.Auth.LoggingEmailSender>();
}

// =============================================================================
// ONLINE CUSTOMER SERVICES (Phase 10.3.1 - Storefront Identity)
// Completely separate from POS Customers - different authentication system.
// MongoDB repository for read-only Storefront access.
// Redis caching for performance.
// =============================================================================
builder.Services.AddScoped<PointOfSale.Infrastructure.MongoDB.Services.IOnlineCustomerReadRepository, PointOfSale.Infrastructure.MongoDB.Services.OnlineCustomerReadRepository>();
builder.Services.AddScoped<PointOfSale.API.Services.Storefront.IStorefrontOnlineCustomerService, PointOfSale.API.Services.Storefront.StorefrontOnlineCustomerService>();

// =============================================================================
// COMMERCEHUB CUSTOMER AUTHENTICATION
// Global marketplace customer identity, separate from POS customers and
// tenant-scoped storefront online customers.
// =============================================================================
builder.Services.Configure<PointOfSale.API.Models.CustomerJwtSettings>(
    builder.Configuration.GetSection(PointOfSale.API.Models.CustomerJwtSettings.SectionName));
builder.Services.Configure<PointOfSale.API.Services.CommerceHub.CommerceHubCustomerIdentityOptions>(
    builder.Configuration.GetSection(PointOfSale.API.Services.CommerceHub.CommerceHubCustomerIdentityOptions.SectionName));
builder.Services.Configure<PointOfSale.API.Configuration.RefundSettings>(
    builder.Configuration.GetSection("CommerceHub:RefundSettings"));
builder.Services.Configure<PointOfSale.API.Configuration.CommerceHubPayoutSettings>(
    builder.Configuration.GetSection("CommerceHub:Payouts"));
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICommerceHubCustomerAuthService, PointOfSale.API.Services.CommerceHub.CommerceHubCustomerAuthService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICustomerTokenRevocationService, PointOfSale.API.Services.CommerceHub.CustomerTokenRevocationService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICommerceHubCustomerOrderService, PointOfSale.API.Services.CommerceHub.CommerceHubCustomerOrderService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.IRefundEvidenceService, PointOfSale.API.Services.CommerceHub.RefundEvidenceService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.IRefundPayoutSecurityService, PointOfSale.API.Services.CommerceHub.RefundPayoutSecurityService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICustomerPayoutDetailsService, PointOfSale.API.Services.CommerceHub.CustomerPayoutDetailsService>();
builder.Services.AddScoped<PointOfSale.API.Services.CommerceHub.ICustomerRefundService, PointOfSale.API.Services.CommerceHub.CustomerRefundService>();
builder.Services.AddScoped<PointOfSale.API.Services.MarketplaceReviews.IMarketplaceProductReviewService, PointOfSale.API.Services.MarketplaceReviews.MarketplaceProductReviewService>();
builder.Services.AddScoped<PointOfSale.Infrastructure.Services.MarketplaceReviews.IMarketplaceProductReviewModerationService, PointOfSale.Infrastructure.Services.MarketplaceReviews.MarketplaceProductReviewModerationService>();
builder.Services.AddScoped<PointOfSale.Infrastructure.Services.MarketplaceReviews.IMarketplaceProductReviewProjectionService, PointOfSale.Infrastructure.Services.MarketplaceReviews.MarketplaceProductReviewProjectionService>();
builder.Services.AddScoped<PointOfSale.API.SuperAdmin.Services.ISuperAdminProductReviewService, PointOfSale.API.SuperAdmin.Services.SuperAdminProductReviewService>();
builder.Services.AddScoped<Microsoft.AspNetCore.Identity.IPasswordHasher<PointOfSale.Core.Entities.CommerceHubCustomer>, Microsoft.AspNetCore.Identity.PasswordHasher<PointOfSale.Core.Entities.CommerceHubCustomer>>();

// =============================================================================
// STOREFRONT AUTHENTICATION (Phase 10.3.2 - Login / Register)
// COMPLETELY SEPARATE from POS authentication.
// Uses OnlineCustomer entity, separate JWT issuer/audience/secret.
// =============================================================================
builder.Services.Configure<PointOfSale.API.Models.StorefrontJwtSettings>(
    builder.Configuration.GetSection(PointOfSale.API.Models.StorefrontJwtSettings.SectionName));
builder.Services.AddScoped<PointOfSale.API.Services.Storefront.IStorefrontAuthService, PointOfSale.API.Services.Storefront.StorefrontAuthService>();
builder.Services.AddScoped<Microsoft.AspNetCore.Identity.IPasswordHasher<PointOfSale.Core.Entities.OnlineCustomer>, Microsoft.AspNetCore.Identity.PasswordHasher<PointOfSale.Core.Entities.OnlineCustomer>>();

// =============================================================================
// STOREFRONT EMAIL VERIFICATION (Phase 10.3.2.5)
// Tenant-aware, rate-limited email verification for Storefront customers.
// =============================================================================
builder.Services.Configure<PointOfSale.API.Services.Storefront.StorefrontEmailVerificationSettings>(
    builder.Configuration.GetSection(PointOfSale.API.Services.Storefront.StorefrontEmailVerificationSettings.SectionName));
builder.Services.AddScoped<PointOfSale.API.Services.Storefront.IStorefrontEmailVerificationService, PointOfSale.API.Services.Storefront.StorefrontEmailVerificationService>();

// =============================================================================
// STOREFRONT REFRESH TOKENS (Phase 10.3.4)
// PostgreSQL-backed refresh token rotation with reuse detection.
// =============================================================================
builder.Services.AddScoped<PointOfSale.API.Services.Storefront.IStorefrontRefreshTokenService, PointOfSale.API.Services.Storefront.StorefrontRefreshTokenService>();

// =============================================================================
// STOREFRONT AUTHENTICATION HARDENING (Phase 10.3.x - SECURITY CRITICAL)
// 
// SECURITY FEATURES:
// - JWT Blacklist: Revokes access tokens immediately on logout/deactivation
// - Rate Limiter: Prevents brute force attacks on login/refresh
// - Cookie Helper: HttpOnly cookies for XSS protection
// 
// SECURITY BOUNDARY: These services are ONLY for Storefront, NOT POS.
// =============================================================================
builder.Services.AddScoped<PointOfSale.API.Services.Storefront.IStorefrontJwtBlacklist, PointOfSale.API.Services.Storefront.StorefrontJwtBlacklist>();
builder.Services.AddSingleton<PointOfSale.API.Services.Storefront.IStorefrontAuthRateLimiter, PointOfSale.API.Services.Storefront.StorefrontAuthRateLimiter>();
builder.Services.Configure<PointOfSale.API.Services.Storefront.StorefrontCaptchaOptions>(
    builder.Configuration.GetSection(PointOfSale.API.Services.Storefront.StorefrontCaptchaOptions.SectionName));
builder.Services.AddSingleton<PointOfSale.API.Services.Storefront.IStorefrontCaptchaGuard, PointOfSale.API.Services.Storefront.StorefrontCaptchaGuard>();
// SECURITY HARDENING (Audit C-2): Rate limiter for guest order tracking (10 req/min per IP)
builder.Services.AddSingleton<PointOfSale.API.Services.Storefront.IGuestOrderTrackingRateLimiter, PointOfSale.API.Services.Storefront.GuestOrderTrackingRateLimiter>();
builder.Services.Configure<PointOfSale.API.Services.Storefront.StorefrontCookieSettings>(
    builder.Configuration.GetSection(PointOfSale.API.Services.Storefront.StorefrontCookieSettings.SectionName));
builder.Services.AddSingleton<PointOfSale.API.Services.Storefront.IStorefrontCookieHelper, PointOfSale.API.Services.Storefront.StorefrontCookieHelper>();

// =============================================================================
// STOREFRONT SOCIAL LOGIN (Phase 10.3.3)
// 
// Google & Facebook OAuth authentication for Storefront customers.
// POS manages provider settings (enable/disable, client credentials).
// Provider tokens are verified once then discarded - NEVER stored.
// 
// SECURITY BOUNDARY: Social login is ONLY for Storefront, NOT POS.
// =============================================================================
static IAsyncPolicy<HttpResponseMessage> CreateExternalHttpRetryPolicy(HttpRequestMessage request)
{
    // Retry idempotent methods (GET, HEAD, OPTIONS)
    if (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head || request.Method == HttpMethod.Options)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(response => response.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                retryCount: 2,
                sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(250 * attempt));
    }

    // For non-idempotent methods (POST, PUT, PATCH, DELETE), only retry if Idempotency-Key header is present
    // This allows payment services (Paymob, CommerceHub) to safely retry with idempotency protection
    if (request.Headers.Contains("Idempotency-Key"))
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(response => response.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                retryCount: 2,
                sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(250 * attempt));
    }

    // No retry for non-idempotent methods without idempotency protection
    return Policy.NoOpAsync<HttpResponseMessage>();
}

static IHttpClientBuilder ConfigureExternalHttpClient(
    IHttpClientBuilder clientBuilder,
    TimeSpan timeout,
    bool enableRetry = true)
{
    clientBuilder.ConfigureHttpClient(client => client.Timeout = timeout);

    if (enableRetry)
    {
        clientBuilder.AddPolicyHandler(request => CreateExternalHttpRetryPolicy(request));
    }

    return clientBuilder;
}

ConfigureExternalHttpClient(builder.Services.AddHttpClient(string.Empty), TimeSpan.FromSeconds(15)); // For OAuth token verification and shared external calls
ConfigureExternalHttpClient(builder.Services.AddHttpClient("Paymob"), TimeSpan.FromSeconds(20));
ConfigureExternalHttpClient(builder.Services.AddHttpClient("Bosta"), TimeSpan.FromSeconds(15));
ConfigureExternalHttpClient(builder.Services.AddHttpClient("Aramex"), TimeSpan.FromSeconds(15));
ConfigureExternalHttpClient(builder.Services.AddHttpClient("DHL"), TimeSpan.FromSeconds(15));
ConfigureExternalHttpClient(builder.Services.AddHttpClient("Mylerz"), TimeSpan.FromSeconds(15));
ConfigureExternalHttpClient(builder.Services.AddHttpClient("FedExOAuth"), TimeSpan.FromSeconds(15));
ConfigureExternalHttpClient(builder.Services.AddHttpClient("FedEx"), TimeSpan.FromSeconds(15));
builder.Services.AddScoped<PointOfSale.API.Services.Storefront.IStorefrontSocialLoginService, PointOfSale.API.Services.Storefront.StorefrontSocialLoginService>();

// =============================================================================
// STOREFRONT AUTH PROVIDER ADMIN SERVICE (Phase P2 - Provider Management)
// 
// POS admin interface for managing Storefront authentication providers.
// - Enable/disable Google and Facebook login
// - Manage OAuth client credentials
// - Rotate secrets securely
// 
// SECURITY: Uses Data Protection API for secret encryption at rest.
// Keys are persisted to file system to survive app restarts.
// =============================================================================
var configuredDataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
var dataProtectionKeysPath = string.IsNullOrWhiteSpace(configuredDataProtectionKeysPath)
    ? builder.Environment.IsEnvironment("Test")
        ? Path.Combine(builder.Environment.ContentRootPath, "..", "temp", "test-environment", "data-protection-keys")
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PointOfSale",
            "DataProtection-Keys")
    : Path.IsPathRooted(configuredDataProtectionKeysPath)
        ? configuredDataProtectionKeysPath
        : Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, configuredDataProtectionKeysPath));
var dataProtectionApplicationName = builder.Configuration["DataProtection:ApplicationName"];

Directory.CreateDirectory(dataProtectionKeysPath);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName(string.IsNullOrWhiteSpace(dataProtectionApplicationName)
        ? "PointOfSale"
        : dataProtectionApplicationName);
builder.Services.AddScoped<PointOfSale.API.Services.Admin.IStorefrontAuthProviderAdminService, PointOfSale.API.Services.Admin.StorefrontAuthProviderAdminService>();

// =============================================================================
// STOREFRONT PASSWORD RESET (Phase 10.3.5)
// 
// Secure password reset with:
// - Token hashing (SHA256) - raw token NEVER stored
// - One-time use tokens with 30-min expiration
// - No user enumeration (same response for all cases)
// - All sessions revoked on successful reset
// 
// SECURITY BOUNDARY: Password reset is ONLY for Storefront, NOT POS.
// =============================================================================
builder.Services.AddScoped<PointOfSale.API.Services.Storefront.IStorefrontPasswordResetService, PointOfSale.API.Services.Storefront.StorefrontPasswordResetService>();

// =============================================================================
// STOREFRONT WISHLIST SERVICE (Phase 10.3.6)
// 
// Customer wishlist management with CQRS pattern:
// - PostgreSQL: Source of truth (write model)
// - MongoDB: Read model (projected via events)
// - Redis: Cache layer for fast reads
// - Event-driven: Domain events for projection and cache invalidation
// 
// SECURITY BOUNDARY: Wishlist is ONLY for authenticated Storefront customers.
// =============================================================================
builder.Services.AddScoped<PointOfSale.API.Services.Storefront.IStorefrontWishlistService, PointOfSale.API.Services.Storefront.StorefrontWishlistService>();

// =============================================================================
// STOREFRONT PHONE VERIFICATION SERVICE (WhatsApp OTP)
// 
// Phone verification via WhatsApp OTP:
// - Request OTP: Generates 6-digit code, sends via WhatsApp
// - Verify OTP: Validates code, marks phone as verified
// - Redis: Temp storage for OTP with 5-minute TTL
// - Rate limiting: Max 3 attempts per phone per 15 minutes
// 
// SECURITY: OTP code is never stored in PostgreSQL (only Redis with TTL)
// =============================================================================
builder.Services.AddScoped<PointOfSale.API.Services.Storefront.ICheckoutPhoneOtpStore, PointOfSale.API.Services.Storefront.CheckoutPhoneOtpStore>();
builder.Services.AddScoped<PointOfSale.API.Services.Storefront.IStorefrontPhoneVerificationService, PointOfSale.API.Services.Storefront.StorefrontPhoneVerificationService>();

// =============================================================================
// STOREFRONT COUPON SERVICE (Phase C2 - Cart + Coupon Integration)
// 
// CQRS COMPLIANT: Coupon validation uses MongoDB read models.
// - MongoDB: Read-only source for Storefront
// - PostgreSQL: Write-only for coupons
// - Redis: Cart with applied coupon code
// - Server-side only: Frontend never calculates discounts
// 
// SECURITY BOUNDARY: Coupons are tenant-scoped, cross-tenant rejection enforced.
// =============================================================================
builder.Services.AddScoped<PointOfSale.Infrastructure.MongoDB.Services.ICouponReadRepository, PointOfSale.Infrastructure.MongoDB.Services.CouponReadRepository>();
builder.Services.AddScoped<PointOfSale.Core.Interfaces.ICouponValidationService, PointOfSale.Infrastructure.Discounts.CouponValidationService>();
builder.Services.AddScoped<PointOfSale.API.Services.Storefront.IStorefrontCouponService, PointOfSale.API.Services.Storefront.StorefrontCouponService>();
builder.Services.AddScoped<PointOfSale.API.Services.ReadModels.IDiscountReadModelRepairService, PointOfSale.API.Services.ReadModels.DiscountReadModelRepairService>();

// =============================================================================
// LEGACY COUPON ADAPTER
//
// The compatibility adapter remains available for older call sites and tests,
// but all live coupon authority now routes through ICouponValidationService.
// =============================================================================
builder.Services.AddScoped<PointOfSale.API.Services.Coupons.ICouponUsageService, PointOfSale.API.Services.Coupons.CouponUsageService>();

// =============================================================================
// PROMOTION ENGINE (PHASE: Promotions Engine - Unified Discounts)
// 
// Single engine for ALL discount calculations:
// - Offers (auto-applied promotions without codes)
// - Coupons (code-based promotions)
// Server-side enforcement ONLY - no frontend trust.
// Highest priority wins when multiple promotions apply.
// =============================================================================
builder.Services.AddScoped<PointOfSale.API.Services.Promotions.IPromotionEngine, PointOfSale.API.Services.Promotions.PromotionEngine>();

// =============================================================================
// ANALYTICS EVENT SERVICES (PHASE 1: Canonical Analytics Event Foundation)
// 
// POS-owned analytics system that:
// - Collects all storefront user behavior
// - Is tenant-isolated and idempotent
// - Fully decoupled from Google Analytics & Meta
// - Can later dispatch events to any analytics provider
//
// Components:
// - Validator: Server-side validation and enrichment
// - Hot Store (Redis): Realtime counters, recent events, idempotency
// - Cold Store (MongoDB): Historical append-only storage
// =============================================================================
builder.Services.AddScoped<PointOfSale.API.Services.Analytics.IAnalyticsEventValidator, PointOfSale.API.Services.Analytics.AnalyticsEventValidator>();
builder.Services.AddSingleton<PointOfSale.API.Services.Analytics.IAnalyticsPayloadGuard, PointOfSale.API.Services.Analytics.AnalyticsPayloadGuard>();
builder.Services.AddSingleton<PointOfSale.API.Services.Analytics.IAnalyticsIngestionRateLimiter, PointOfSale.API.Services.Analytics.AnalyticsIngestionRateLimiter>();
builder.Services.AddScoped<PointOfSale.API.Services.Analytics.IAnalyticsTenantCurrencyResolver, PointOfSale.API.Services.Analytics.AnalyticsTenantCurrencyResolver>();
builder.Services.AddScoped<PointOfSale.Infrastructure.Analytics.IStorefrontAnalyticsAuthorityReader, PointOfSale.Infrastructure.Analytics.StorefrontAnalyticsAuthorityReader>();
builder.Services.AddScoped<PointOfSale.Infrastructure.Redis.Services.IAnalyticsHotStoreService, PointOfSale.Infrastructure.Redis.Services.AnalyticsHotStoreService>();
builder.Services.AddScoped<PointOfSale.Infrastructure.MongoDB.Services.IAnalyticsColdStoreService, PointOfSale.Infrastructure.MongoDB.Services.AnalyticsColdStoreService>();

// =============================================================================
// PHASE 3: ANALYTICS DISPATCHERS
// 
// Fan-out analytics events to external providers without coupling:
// - Google Analytics 4 (Measurement Protocol - server-side only)
// - Meta Conversions API (CAPI - server-side only)
// - Admin Realtime Dashboard (internal)
//
// ARCHITECTURAL RULES:
// - Dispatchers are stateless and never mutate events
// - Dispatch failures NEVER block event ingestion
// - No gtag.js or Meta Pixel on storefront
// - All provider secrets encrypted at rest
// =============================================================================

// Dispatcher Pipeline (orchestrates all dispatchers)
builder.Services.AddScoped<PointOfSale.Infrastructure.Analytics.Dispatchers.AnalyticsDispatcherPipeline>();

// Individual Dispatchers (registered via IEnumerable<IAnalyticsDispatcher>)
builder.Services.AddScoped<PointOfSale.Infrastructure.Analytics.Dispatchers.IAnalyticsDispatcher, PointOfSale.Infrastructure.Analytics.Dispatchers.AdminRealtimeDispatcher>();
builder.Services.AddScoped<PointOfSale.Infrastructure.Analytics.Dispatchers.IAnalyticsDispatcher, PointOfSale.Infrastructure.Analytics.Dispatchers.GoogleAnalyticsDispatcher>();
builder.Services.AddScoped<PointOfSale.Infrastructure.Analytics.Dispatchers.IAnalyticsDispatcher, PointOfSale.Infrastructure.Analytics.Dispatchers.MetaConversionsApiDispatcher>();
builder.Services.AddScoped<PointOfSale.Infrastructure.Analytics.Dispatchers.IAnalyticsDispatcher, PointOfSale.Infrastructure.Analytics.Dispatchers.TikTokAnalyticsDispatcher>();

// Settings Service (manages tenant analytics configuration)
builder.Services.AddScoped<PointOfSale.Infrastructure.Analytics.Dispatchers.IAnalyticsSettingsService, PointOfSale.Infrastructure.Analytics.Dispatchers.AnalyticsSettingsService>();

// Realtime Dashboard Service (reads from Redis hot store)
builder.Services.AddScoped<PointOfSale.API.Services.Analytics.IAnalyticsRealtimeService, PointOfSale.API.Services.Analytics.AnalyticsRealtimeService>();
builder.Services.AddScoped<PointOfSale.API.Services.Analytics.IAnalyticsSessionJourneyService, PointOfSale.API.Services.Analytics.AnalyticsSessionJourneyService>();
builder.Services.AddScoped<PointOfSale.API.Services.Analytics.IStorefrontAnalyticsDashboardService, PointOfSale.API.Services.Analytics.StorefrontAnalyticsDashboardService>();
builder.Services.AddScoped<PointOfSale.API.Services.Analytics.IStorefrontLiveMonitoringService, PointOfSale.API.Services.Analytics.StorefrontLiveMonitoringService>();

// Phase M1: Pixel Conversion Value Override
builder.Services.AddScoped<PointOfSale.Core.Analytics.IPixelValueResolver, PointOfSale.Infrastructure.Analytics.PixelValueResolver>();
builder.Services.AddScoped<PointOfSale.Core.Analytics.IPixelTrackingConfigService, PointOfSale.Infrastructure.Analytics.PixelTrackingConfigService>();
builder.Services.AddScoped<PointOfSale.API.Services.Analytics.IPixelOverrideStatsService, PointOfSale.API.Services.Analytics.PixelOverrideStatsService>();

// Phase M2: Per-Product Pixel Override
builder.Services.AddScoped<PointOfSale.Core.Interfaces.IProductPixelOverrideService, PointOfSale.Infrastructure.Analytics.ProductPixelOverrideService>();

// =============================================================================
// WHATSAPP MESSAGING (Phase 13)
// 
// - Optional, opt-in only
// - Tenant-isolated
// =============================================================================
builder.Services.AddScoped<PointOfSale.Core.Interfaces.WhatsApp.IWhatsAppAccountService, PointOfSale.Infrastructure.WhatsApp.Services.WhatsAppAccountService>();
builder.Services.AddScoped<PointOfSale.Core.Interfaces.WhatsApp.IWhatsAppFeatureGateService, PointOfSale.Infrastructure.WhatsApp.Services.WhatsAppFeatureGateService>();

// WhatsApp Infrastructure Services
builder.Services.Configure<PointOfSale.Infrastructure.WhatsApp.WhatsAppRateLimitSettings>(builder.Configuration.GetSection("WhatsAppRateLimits"));

builder.Services.AddSingleton<PointOfSale.Infrastructure.WhatsApp.Interfaces.IWhatsAppSessionManager, PointOfSale.Infrastructure.WhatsApp.Services.WhatsAppSessionManager>();
// Playwright Engine must be Singleton to maintain browser instance
builder.Services.AddSingleton<PointOfSale.Infrastructure.WhatsApp.Interfaces.IWhatsAppWebEngine, PointOfSale.Infrastructure.WhatsApp.Engine.PlaywrightWhatsAppEngine>();
builder.Services.AddSingleton<PointOfSale.Infrastructure.WhatsApp.Interfaces.IWhatsAppRateLimiter, PointOfSale.Infrastructure.WhatsApp.Services.WhatsAppRateLimiter>();

// =============================================================================
// WhatsApp Transport Layer - Strangler Fig Pattern for Baileys Migration
// =============================================================================
// Both Playwright and Baileys transports coexist during migration.
// Dispatcher routes based on tenant's UseBaileys feature gate.
// This enables gradual rollout with instant rollback capability.
// 
// Playwright Transport: Legacy (300-600 MB RAM per session)
// Baileys Transport: New (20-40 MB RAM per session)
// =============================================================================

// Register Playwright Transport (legacy)
builder.Services.AddKeyedScoped<PointOfSale.Infrastructure.WhatsApp.Interfaces.IWhatsAppTransport, PointOfSale.Infrastructure.WhatsApp.Transport.PlaywrightTransport>("playwright");

// Register Baileys Transport (new)
builder.Services.AddHttpClient<PointOfSale.Infrastructure.WhatsApp.Transport.BaileysWhatsAppTransport>();
builder.Services.AddKeyedScoped<PointOfSale.Infrastructure.WhatsApp.Interfaces.IWhatsAppTransport, PointOfSale.Infrastructure.WhatsApp.Transport.BaileysWhatsAppTransport>("baileys");

builder.Services.AddScoped<PointOfSale.Infrastructure.WhatsApp.Interfaces.IWhatsAppMessageLogService, PointOfSale.Infrastructure.WhatsApp.Services.WhatsAppMessageLogService>();

// Phone Number Normalizer (E.164 format for WhatsApp)
// Uses libphonenumber for industry-standard phone parsing
builder.Services.AddScoped<PointOfSale.Core.Interfaces.IPhoneNumberNormalizer, PointOfSale.Infrastructure.Services.PhoneNumberNormalizer>();

builder.Services.AddScoped<PointOfSale.Infrastructure.WhatsApp.Interfaces.IWhatsAppMessageDispatcher, PointOfSale.Infrastructure.WhatsApp.Services.WhatsAppMessageDispatcher>();
builder.Services.AddScoped<PointOfSale.Infrastructure.WhatsApp.Services.WhatsAppDispatchAuthority>();

// WhatsApp Template Resolver
builder.Services.AddScoped<PointOfSale.Core.Interfaces.WhatsApp.IWhatsAppTemplateResolver, PointOfSale.Infrastructure.WhatsApp.Services.WhatsAppTemplateResolver>();

// WhatsApp OTP Service
builder.Services.AddScoped<PointOfSale.Core.Interfaces.WhatsApp.IWhatsAppOtpService, PointOfSale.Infrastructure.WhatsApp.Services.WhatsAppOtpService>();

// WhatsApp Payment Notification Service (Manual Wallet Approval)
builder.Services.AddScoped<PointOfSale.Core.Interfaces.WhatsApp.IWhatsAppPaymentNotificationService, PointOfSale.Infrastructure.WhatsApp.Services.WhatsAppPaymentNotificationService>();

// WhatsApp Operational Safety (requires IDistributedCache)
builder.Services.AddScoped<PointOfSale.Infrastructure.WhatsApp.Interfaces.IWhatsAppCircuitBreaker, PointOfSale.Infrastructure.WhatsApp.Services.WhatsAppCircuitBreaker>();
builder.Services.AddScoped<PointOfSale.Infrastructure.WhatsApp.Interfaces.IWhatsAppKillSwitch, PointOfSale.Infrastructure.WhatsApp.Services.WhatsAppKillSwitch>();

// WhatsApp Background Jobs (Hangfire)
builder.Services.AddScoped<PointOfSale.Infrastructure.WhatsApp.Jobs.WhatsAppSessionHeartbeatJob>();
builder.Services.AddScoped<PointOfSale.Infrastructure.WhatsApp.Jobs.WhatsAppMessageRetryJob>();
builder.Services.AddScoped<PointOfSale.Infrastructure.WhatsApp.Jobs.WhatsAppSessionCleanupJob>();

// =============================================================================
// SMART ABANDONED CART RECOVERY (Phase: Abandoned Cart Hardening)
// 
// Tenant automation settings and eligibility services for abandoned cart recovery:
// - ITenantAutomationSettingsService: Manages tenant-level automation configuration
// - IAbandonedCartEligibilityService: Checks if tenant can send recovery messages
//
// ARCHITECTURAL RULES:
// - Feature-gated: Disabled by default, admin must enable
// - Depends on WhatsApp integration being configured and connected
// - All settings auditable in PostgreSQL
// =============================================================================
builder.Services.AddScoped<PointOfSale.Core.Interfaces.ITenantAutomationSettingsService, PointOfSale.Infrastructure.Services.TenantAutomationSettingsService>();
builder.Services.AddScoped<PointOfSale.Core.Interfaces.IAbandonedCartEligibilityService, PointOfSale.Infrastructure.Services.AbandonedCartEligibilityService>();
builder.Services.AddScoped<PointOfSale.Core.Interfaces.IAbandonedCartRecoveryTelemetryService, PointOfSale.Infrastructure.Services.AbandonedCartRecoveryTelemetryService>();

// Abandoned Cart Service: Core cart tracking and state management
builder.Services.AddScoped<PointOfSale.Core.Interfaces.IAbandonedCartService, PointOfSale.Infrastructure.Services.AbandonedCartService>();

// Geographic runtime foundation: SQL-backed published catalog admission authority.
builder.Services.AddScoped<PointOfSale.Core.Interfaces.Geographic.IGeographicRepository, PointOfSale.Infrastructure.Geographic.GeographicRepository>();
builder.Services.AddScoped<PointOfSale.Core.Interfaces.Geographic.IGeographicCatalogService, PointOfSale.Infrastructure.Geographic.GeographicCatalogService>();
builder.Services.AddScoped<PointOfSale.Core.Interfaces.Geographic.IGeographicResolver, PointOfSale.Infrastructure.Geographic.GeographicResolver>();
builder.Services.AddScoped<PointOfSale.Core.Interfaces.Geographic.IAddressAdmissionService, PointOfSale.Infrastructure.Geographic.GeographicResolver>();
builder.Services.AddScoped<PointOfSale.Core.Interfaces.Geographic.ICarrierGeographicMappingService, PointOfSale.Infrastructure.Geographic.CarrierGeographicMappingService>();
builder.Services.AddScoped<PointOfSale.Core.Interfaces.Customs.ICustomsEvidenceAdmissionService, PointOfSale.Infrastructure.Customs.CustomsEvidenceAdmissionService>();
builder.Services.AddScoped<PointOfSale.Core.Interfaces.Customs.ICustomsAdministrationWorkflowService, PointOfSale.Infrastructure.Customs.CustomsAdministrationWorkflowService>();
builder.Services.AddScoped<PointOfSale.Core.Interfaces.Geographic.IGeographicPublicationService, PointOfSale.Infrastructure.Geographic.GeographicPublicationService>();
builder.Services.AddScoped<PointOfSale.Core.Interfaces.Geographic.IGeographicSynchronizationService, PointOfSale.Infrastructure.Geographic.GeographicSynchronizationService>();

// Abandoned Cart Scanner Job: Background job that finds and triggers recovery for abandoned carts
builder.Services.AddScoped<PointOfSale.Infrastructure.Jobs.AbandonedCartScannerJob>();
builder.Services.AddScoped<PointOfSale.Infrastructure.Jobs.AbandonedCartCleanupJob>();

// =============================================================================
// ABANDONED CHECKOUT SESSION SERVICE (Phase: Lost Orders)
// Tracks partially completed checkout attempts for recovery.
// NOT related to AbandonedCartService — different entity, different lifecycle.
// =============================================================================
builder.Services.AddScoped<PointOfSale.Core.Interfaces.ICheckoutSessionService, PointOfSale.Infrastructure.Services.CheckoutSessionService>();
builder.Services.AddScoped<PointOfSale.Infrastructure.Jobs.AbandonedCheckoutScannerJob>();

// P2-13: ProcessedTrustEvents cleanup job (scheduled via Hangfire)
builder.Services.AddScoped<PointOfSale.Infrastructure.Jobs.ProcessedTrustEventsCleanupJob>();

// Recovery Orchestrator: Calls n8n webhook to trigger recovery workflow
builder.Services.AddScoped<PointOfSale.Core.Interfaces.IRecoveryOrchestrator, PointOfSale.Infrastructure.Services.N8nRecoveryOrchestrator>();

// WhatsApp Rate Limiting: Prevents spam and handles opt-out
builder.Services.AddScoped<PointOfSale.Core.Interfaces.IWhatsAppRateLimitService, PointOfSale.Infrastructure.Services.WhatsAppRateLimitService>();

// HttpClient for n8n webhook calls
ConfigureExternalHttpClient(builder.Services.AddHttpClient("N8nRecovery"), TimeSpan.FromSeconds(10), enableRetry: false);


// =============================================================================
// PHASE 4: REALTIME BUSINESS ALERTS
// 
// Detect business-critical situations and notify admins automatically:
// - Zero sales alerts
// - Payment failure spikes
// - High cart abandonment
// - Traffic anomalies
//
// ARCHITECTURAL RULES:
// - All metrics from Redis counters (NO MongoDB reads during evaluation)
// - Background job runs every 60 seconds
// - Alerts are deterministic & reproducible
// - Multi-tenant isolated
// =============================================================================

// Metric Reader (reads from Redis counters)
builder.Services.AddScoped<PointOfSale.Infrastructure.Analytics.Alerts.IAnalyticsMetricReader, PointOfSale.Infrastructure.Analytics.Alerts.AnalyticsMetricReader>();

// Alert Store (Redis for active alerts, MongoDB for history)
builder.Services.AddScoped<PointOfSale.Infrastructure.Analytics.Alerts.IAnalyticsAlertStore, PointOfSale.Infrastructure.Analytics.Alerts.AnalyticsAlertStore>();

// Alert Rule Service (PostgreSQL)
builder.Services.AddScoped<PointOfSale.Infrastructure.Analytics.Alerts.IAnalyticsAlertRuleService, PointOfSale.Infrastructure.Analytics.Alerts.AnalyticsAlertRuleService>();

// Background Evaluator Service (runs every 60 seconds)
builder.Services.AddHostedService<PointOfSale.Infrastructure.Analytics.Alerts.AnalyticsAlertEvaluatorService>();

// =============================================================================
// PHASE 4B: REALTIME CONVERSION FUNNELS
// 
// Track where users drop off in the buying journey:
// - Purchase Funnel: PageView → ProductView → AddToCart → Checkout → Purchase
// - Discovery Funnel: PageView → ProductImpression → ProductView → AddToCart
// - Checkout Funnel: BeginCheckout → PurchaseCompleted
//
// ARCHITECTURAL RULES:
// - Computed from Redis counters ONLY (no MongoDB)
// - Server-side computation only (no frontend logic)
// - Deterministic and reproducible
// =============================================================================

// Funnel Service (computes funnels from Redis counters)
builder.Services.AddScoped<PointOfSale.Infrastructure.Analytics.Funnels.IAnalyticsFunnelService, PointOfSale.Infrastructure.Analytics.Funnels.AnalyticsFunnelService>();

// =============================================================================
// PHASE 7: ATTRIBUTION ENGINE (Marketing ROI)
// 
// Deterministic attribution tracking for marketing ROI:
// - Session-based attribution from UTM parameters
// - First-touch and last-touch attribution models
// - Order-level attribution linking orders to sources
// - Revenue by source/campaign analysis
//
// ARCHITECTURAL RULES:
// - POS is the single source of truth (NOT GA/Meta)
// - No cookies used (SessionId is POS-owned)
// - No frontend attribution logic (server-side only)
// - Attribution is immutable after order completion
// - Provider-independent (works without GA/Meta enabled)
// =============================================================================

// Attribution Context Store (Redis hot + MongoDB cold)
builder.Services.AddScoped<PointOfSale.Infrastructure.Attribution.IAttributionContextStore, PointOfSale.Infrastructure.Attribution.AttributionContextStore>();

// Order Attribution Store (MongoDB primary + Redis cache)
builder.Services.AddScoped<PointOfSale.Infrastructure.Attribution.IOrderAttributionStore, PointOfSale.Infrastructure.Attribution.OrderAttributionStore>();

// Attribution Context Resolver (extracts UTM params from PageView events)
builder.Services.AddScoped<PointOfSale.Infrastructure.Attribution.IAttributionContextResolver, PointOfSale.Infrastructure.Attribution.AttributionContextResolver>();

// Order Attribution Resolver (links orders to marketing sources)
builder.Services.AddScoped<PointOfSale.Infrastructure.Attribution.IOrderAttributionResolver, PointOfSale.Infrastructure.Attribution.OrderAttributionResolver>();

// =============================================================================
// PHASE 8B: CAMPAIGN COST & ROI (Marketing ROI Engine)
// 
// Admin-declared marketing costs for deterministic ROI calculations:
// - Campaign Cost Service: CRUD operations for admin-declared costs
// - Campaign ROI Service: Server-side ROI calculations
//
// ARCHITECTURAL RULES:
// - POS is the single source of truth for costs (NOT ad platforms)
// - NO auto-fetch from Facebook/Google Ads APIs
// - All ROI calculations done server-side (no frontend logic)
// - Currency must match tenant currency
// - Costs are tenant-isolated and immutable per period
// =============================================================================

// Campaign Cost Service (PostgreSQL storage, CRUD operations)
builder.Services.AddScoped<PointOfSale.Infrastructure.CampaignCost.ICampaignCostService, PointOfSale.Infrastructure.CampaignCost.CampaignCostService>();

// Campaign ROI Service (joins attribution with costs, calculates ROI)
builder.Services.AddScoped<PointOfSale.Infrastructure.CampaignCost.ICampaignRoiService, PointOfSale.Infrastructure.CampaignCost.CampaignRoiService>();

// =============================================================================
// AI COMMERCE ASSISTANT (Phase 1 - Router-Ready, Storefront-Safe, Order-Aware)
// 
// Embedded AI assistant for e-commerce storefront:
// - Answers customer questions about products, prices, shipping, and policies
// - Helps customers build order drafts (intent → draft, NOT execution)
// - NEVER invents data outside provided context
// - NEVER executes actions directly
// - Model-agnostic (works with OpenRouter, OpenAI, Azure, etc.)
//
// ARCHITECTURAL RULES:
// - AI operates ONLY on CommerceContext (cart, products, shipping, store config)
// - All responses are validated against the context
// - Fallback to safe response if AI fails
// - Tenant isolation mandatory
// =============================================================================
builder.Services.Configure<PointOfSale.API.Services.AI.Models.CommerceAIOptions>(
    builder.Configuration.GetSection(PointOfSale.API.Services.AI.Models.CommerceAIOptions.SectionName));
builder.Services.AddSingleton<PointOfSale.API.Services.AI.IAIPromptManager, PointOfSale.API.Services.AI.AIPromptManager>();
builder.Services.AddScoped<PointOfSale.API.Services.AI.IAIResponseValidator, PointOfSale.API.Services.AI.AIResponseValidator>();
builder.Services.AddScoped<PointOfSale.API.Services.AI.ICommerceContextBuilder, PointOfSale.API.Services.AI.CommerceContextBuilder>();
builder.Services.AddHttpClient<PointOfSale.API.Services.AI.IAIRouterService, PointOfSale.API.Services.AI.AIRouterService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});

// =============================================================================
// AI COMMERCE ASSISTANT (Phase 2 - Model Router, Fallback & Safety Layer)
// 
// Multi-model routing with automatic fallback:
// - Routes to models by priority (DeepSeek R1 → GPT-OSS → Gemma)
// - Automatic fallback when models fail or timeout
// - Rate limiting per tenant (Redis-based)
// - Response consistency regardless of model
// - Customer experience remains identical
//
// PHASE 2 RULES:
// - Router decides which model to use
// - Model identity NEVER exposed to customer
// - Same input → same behavior (deterministic)
// - Fail SAFE, not smart
// =============================================================================
builder.Services.AddSingleton<PointOfSale.API.Services.AI.Router.ISafeResponseBuilder, PointOfSale.API.Services.AI.Router.SafeResponseBuilder>();
builder.Services.AddScoped<PointOfSale.API.Services.AI.Router.IAIRateLimiter, PointOfSale.API.Services.AI.Router.AIRateLimiter>();

// Model adapters (all use the same HttpClient factory)
builder.Services.AddHttpClient<PointOfSale.API.Services.AI.Router.Adapters.DeepSeekAdapter>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(35);
});
builder.Services.AddHttpClient<PointOfSale.API.Services.AI.Router.Adapters.GptOssAdapter>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient<PointOfSale.API.Services.AI.Router.Adapters.GemmaAdapter>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});

// Register adapters as IModelAdapter collection
builder.Services.AddScoped<PointOfSale.API.Services.AI.Router.IModelAdapter, PointOfSale.API.Services.AI.Router.Adapters.DeepSeekAdapter>();
builder.Services.AddScoped<PointOfSale.API.Services.AI.Router.IModelAdapter, PointOfSale.API.Services.AI.Router.Adapters.GptOssAdapter>();
builder.Services.AddScoped<PointOfSale.API.Services.AI.Router.IModelAdapter, PointOfSale.API.Services.AI.Router.Adapters.GemmaAdapter>();

// Model router
builder.Services.AddScoped<PointOfSale.API.Services.AI.Router.IModelRouter, PointOfSale.API.Services.AI.Router.ModelRouter>();

// =============================================================================
// AI COMMERCE ASSISTANT (Phase 3 - Event-Aware, Order-Context Intelligence)
// 
// Post-purchase support:
// - Order status inquiries ("Where is my order?")
// - Shipment tracking explanations
// - Event-aware responses (timeline events)
//
// PHASE 3 RULES:
// - AI can only READ and EXPLAIN order/shipment data
// - AI cannot MODIFY order status or contact carriers
// - AI is a narrator of events, not an actor
// =============================================================================
builder.Services.AddScoped<PointOfSale.API.Services.AI.OrderTracking.IOrderTrackingRepository, PointOfSale.API.Services.AI.OrderTracking.StubOrderTrackingRepository>();
builder.Services.AddScoped<PointOfSale.API.Services.AI.OrderTracking.IOrderContextProvider, PointOfSale.API.Services.AI.OrderTracking.OrderContextProvider>();

// =============================================================================
// AI COMMERCE ASSISTANT (Phase 4 - Audit & Explanation Intelligence)
// 
// Admin-facing explanations:
// - Explain WHY events happened
// - Identify WHO/WHAT triggered events
// - Translate system events to human reasoning
//
// PHASE 4 RULES:
// - AI can only EXPLAIN, never suggest actions
// - No blame (users, carriers, or system)
// ===========================================
builder.Services.AddScoped<PointOfSale.API.Services.AI.ICommerceAiInsightsService, PointOfSale.API.Services.AI.CommerceAiInsightsService>();
// - Neutral, factual tone
// - No technical jargon or internal class names
// =============================================================================
builder.Services.AddScoped<PointOfSale.API.Services.AI.Admin.IAdminAuditService, PointOfSale.API.Services.AI.Admin.AdminAuditService>();
builder.Services.AddHttpClient<PointOfSale.API.Services.AI.Admin.IAdminHelpAssistantService, PointOfSale.API.Services.AI.Admin.AdminHelpAssistantService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddHttpClient<PointOfSale.API.Services.AI.Admin.IAdminProductGeneratorService, PointOfSale.API.Services.AI.Admin.AdminProductGeneratorService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddHttpClient<PointOfSale.API.Services.AI.Marketplace.IAiTranslationService, PointOfSale.API.Services.AI.Marketplace.AiTranslationService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(45);
});

// =============================================================================
// AI COMMERCE ASSISTANT (Phase 5 - Historical Consistency & Confidence Layer)
// 
// Historical data handling:
// - Determine data confidence based on order age
// - Handle legacy orders gracefully
// - Explain data limitations as temporal, not failures
//
// PHASE 5 RULES:
// - Legacy ≠ Error
// - Confidence > Completeness
// - Never blame the system for missing historical data
// - Gently explain when features weren't available
// =============================================================================
builder.Services.AddScoped<PointOfSale.API.Services.AI.Historical.IConfidenceResolver, PointOfSale.API.Services.AI.Historical.ConfidenceResolver>();

// =============================================================================
// MONGODB READ STORE (E-commerce)
// MongoDB is used ONLY as a read store for e-commerce product data.
// PostgreSQL remains the source of truth.
// =============================================================================
builder.Services.AddMongoDb(builder.Configuration);
builder.Services.AddHostedService<MongoDbIndexInitializer>();
builder.Services.AddHostedService<CommerceHubMongoDbIndexInitializer>();

// =============================================================================
// REDIS CACHE LAYER
// Redis is used ONLY as a cache layer for read performance optimization.
// Cache failures are graceful - system continues without cache.
// All cache keys are tenant-scoped for multi-tenant isolation.
// =============================================================================
builder.Services.AddRedisCache(builder.Configuration);

// Redis Hardening: Cache warmup on startup (non-blocking, best-effort)
builder.Services.AddHostedService<PointOfSale.API.Services.CacheWarmup.CacheWarmupService>();

// =============================================================================
// ELASTICSEARCH SEARCH INDEX
// Elasticsearch is used ONLY as a search index for product search & filtering.
// PostgreSQL remains the source of truth. Elasticsearch is kept in sync via events.
// Index failures are graceful - system continues without search.
// All documents are tenant-scoped for multi-tenant isolation.
// =============================================================================
builder.Services.AddElasticsearch(builder.Configuration);

// =============================================================================
// AFFILIATE SYSTEM (Phase AF-1 / AF-2 / AF-2.5 / AF-12)
// Commission attribution and tracking for affiliate marketing.
// AffiliateService: Write-side domain service (idempotent, tenant-isolated).
// AffiliateTelemetry: System.Diagnostics.Metrics instrumentation (OpenTelemetry-compatible).
// AffiliateModeMetricsService: AF-12 realtime metrics for Affiliate Mode operations.
// Y1 Audit Fix: AffiliateJwtSettings — cryptographically separate from POS/Storefront.
// =============================================================================
builder.Services.Configure<PointOfSale.API.Models.AffiliateJwtSettings>(
    builder.Configuration.GetSection(PointOfSale.API.Models.AffiliateJwtSettings.SectionName));
builder.Services.AddSingleton<PointOfSale.Infrastructure.Instrumentation.AffiliateTelemetry>();
builder.Services.AddScoped<PointOfSale.Infrastructure.Services.Affiliate.AffiliateService>();
builder.Services.AddScoped<PointOfSale.Core.Interfaces.Affiliate.IAffiliateService>(sp =>
    sp.GetRequiredService<PointOfSale.Infrastructure.Services.Affiliate.AffiliateService>());
builder.Services.AddScoped<PointOfSale.API.Services.Affiliate.IAffiliateModeMetricsService, PointOfSale.API.Services.Affiliate.AffiliateModeMetricsService>();
builder.Services.AddHostedService<PointOfSale.Infrastructure.Services.Affiliate.AffiliateAttributionReconciliationHostedService>();

// AF-23: Affiliate Readiness Gate Service
builder.Services.AddScoped<PointOfSale.Core.Interfaces.Affiliate.IAffiliateReadinessService, PointOfSale.Infrastructure.Services.Affiliate.AffiliateReadinessService>();

// AF-34: Ranking Score Service (click/conversion signal tracking + daily recalculation)
builder.Services.AddScoped<PointOfSale.Infrastructure.Services.Affiliate.AffiliateRankingService>();

// AF-38: Affiliate Payout Service (Hybrid Withdrawal Engine)
builder.Services.AddScoped<PointOfSale.Core.Interfaces.Affiliate.IAffiliatePayoutService, PointOfSale.Infrastructure.Services.Affiliate.AffiliatePayoutService>();
builder.Services.AddScoped<PointOfSale.Core.Interfaces.Affiliate.IAffiliatePayoutEvidenceService, PointOfSale.Infrastructure.Services.Affiliate.AffiliatePayoutEvidenceService>();

// AF-38H: Unified Affiliate Payout Orchestrator
// Single authoritative payout execution pipeline — both Legacy Settlement and
// Withdrawal Engine MUST flow through this orchestrator.
builder.Services.AddOptions<PointOfSale.Infrastructure.Services.Affiliate.AffiliateSettlementOptions>()
    .Bind(builder.Configuration.GetSection("AffiliateSettlement"))
    .ValidateDataAnnotations()
    .Validate(o => o.AccountingStaleMinutes > 0, "AccountingStaleMinutes must be greater than 0")
    .Validate(o => o.IntegrationStaleMinutes > 0, "IntegrationStaleMinutes must be greater than 0")
    .Validate(
        o => !builder.Environment.IsProduction() || PointOfSale.Infrastructure.Services.Affiliate.AffiliateSettlementOptionsProductionGuard.IsProductionSafe(o),
        "AFFILIATE_SETTLEMENT_PRODUCTION_GUARDRAIL_VIOLATION: real-money production requires verified payout evidence, verified manual delivery evidence, and all affiliate settlement break-glass overrides disabled.")
    .ValidateOnStart();
builder.Services.AddScoped<PointOfSale.Core.Interfaces.Affiliate.IAffiliateSettlementOrchestrator, PointOfSale.Infrastructure.Services.Affiliate.AffiliateSettlementOrchestrator>();

// Event Publisher & Consumers (MassTransit)
builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();

    // Register consumers
    // MongoDB sync consumer (read-store projection)
    x.AddConsumer<ProductCreatedEventConsumer>();
    x.AddConsumer<ProductUpdatedEventConsumer>();
    x.AddConsumer<ProductVisibilityLifecycleProjectionConsumer>();
    // Redis cache invalidation consumer
    x.AddConsumer<ProductCacheInvalidationConsumer>();
    x.AddConsumer<PointOfSale.API.Consumers.RedisInvalidationConsumer>();
    // Elasticsearch search index consumers (create, update, delete)
    x.AddConsumer<ProductSearchIndexConsumer>();
    x.AddConsumer<ProductUpdatedSearchIndexConsumer>();
    x.AddConsumer<ProductDeletedSearchIndexConsumer>();
    x.AddConsumer<ProductStockPriceSearchIndexConsumer, ProductStockPriceSearchIndexConsumerDefinition>();
    // Product stock projection consumer (for storefront stock display)
    x.AddConsumer<ProductStockChangedConsumer, ProductStockChangedConsumerDefinition>();
    // Storefront Config Projection Consumer (SQL -> Mongo)
    x.AddConsumer<StorefrontConfigProjectionConsumer, StorefrontConfigProjectionConsumerDefinition>();
    // Shipping Method Projection Consumer (SQL -> Mongo) - CQRS Purity
    x.AddConsumer<ShippingMethodProjectionConsumer, ShippingMethodProjectionConsumerDefinition>();
    x.AddConsumer<StorefrontFooterProjectionConsumer, StorefrontFooterProjectionConsumerDefinition>();

    // =============================================================================
    // CMS PAGE & NAVIGATION PROJECTION CONSUMERS (Phase 9.11)
    // Projects CMS pages and navigation from SQL to MongoDB on publish events.
    // Storefront reads ONLY from MongoDB - never from SQL.
    // =============================================================================
    x.AddConsumer<CmsPageProjectionConsumer, CmsPageProjectionConsumerDefinition>();
    x.AddConsumer<NavigationProjectionConsumer, NavigationProjectionConsumerDefinition>();

    // =============================================================================
    // SHIPMENT NOTIFICATION CONSUMERS (Phase 5.3)
    // Event-driven notifications for shipment lifecycle events.
    // All consumers are idempotent and best-effort (never break flow).
    // =============================================================================
    x.AddConsumer<ShipmentCreatedNotificationConsumer>();
    x.AddConsumer<ShipmentShippedNotificationConsumer>();
    x.AddConsumer<ShipmentDeliveredNotificationConsumer>();
    x.AddConsumer<ShipmentCancelledNotificationConsumer>();
    x.AddConsumer<ShipmentExternalRegistrationRequestedConsumer>();
    x.AddConsumer<OrderShipmentOrchestrationConsumer>();

    // =============================================================================
    // ONLINE CUSTOMER PROJECTION CONSUMER (Phase 10.3.1 - Storefront Identity)
    // Projects online customer data from PostgreSQL to MongoDB.
    // Handles registration, activation, deactivation, email verification, login.
    // =============================================================================
    x.AddConsumer<OnlineCustomerProjectionConsumer, OnlineCustomerProjectionConsumerDefinition>();

    // =============================================================================
    // ONLINE CUSTOMER SESSION PROJECTION CONSUMER (Admin Session Management)
    // Projects refresh token events to MongoDB for admin session view.
    // Handles session creation, revocation, and suspicious activity marking.
    // =============================================================================
    x.AddConsumer<OnlineCustomerSessionProjectionConsumer, OnlineCustomerSessionProjectionConsumerDefinition>();

    // =============================================================================
    // ONLINE CUSTOMER WISHLIST PROJECTION CONSUMER (Phase 10.3.6 - Wishlist)
    // Projects wishlist events from PostgreSQL to MongoDB.
    // Handles add item, remove item, and clear wishlist operations.
    // =============================================================================
    x.AddConsumer<OnlineCustomerWishlistProjectionConsumer, OnlineCustomerWishlistProjectionConsumerDefinition>();

    // =============================================================================
    // PAYMENT METHOD PROJECTION CONSUMER (Phase 10.4.D1 - Payment Methods)
    // Projects payment method events from PostgreSQL to MongoDB.
    // Handles created, updated, enabled, disabled, and deleted events.
    // ❌ NO SECRETS are ever stored in MongoDB.
    // =============================================================================
    x.AddConsumer<PaymentMethodProjectionConsumer, PaymentMethodProjectionConsumerDefinition>();

    // =============================================================================
    // COUPON PROJECTION CONSUMER (CQRS Hardening)
    // Projects coupon events from PostgreSQL to derivative MongoDB visibility documents.
    // Handles created, updated, deleted, and usage changed events.
    // Accepted coupon validation/pricing/reservation/usage remains SQL-backed.
    // =============================================================================
    x.AddConsumer<CouponProjectionConsumer, CouponProjectionConsumerDefinition>();

    // =============================================================================
    // PROMOTION PROJECTION CONSUMER (PHASE: Promotions Engine)
    // Projects promotion (offer) events from PostgreSQL to MongoDB.
    // Handles created, updated, deleted, and usage changed events.
    // Updates product documents with promotion badges for storefront display.
    // =============================================================================
    x.AddConsumer<PromotionProjectionConsumer, PromotionProjectionConsumerDefinition>();

    // =============================================================================
    // SMART OFFER PROJECTION CONSUMER
    // Projects independent popup offers from PostgreSQL into StorefrontConfigDocument.
    // =============================================================================
    x.AddConsumer<SmartOfferProjectionConsumer, SmartOfferProjectionConsumerDefinition>();

    // =============================================================================
    // PRODUCT PROMOTION SEARCH INDEX CONSUMER (Shopify-Grade Promotion-Aware Search)
    // Syncs promotion projection candidates through SQL authority into Elasticsearch for ranking.
    // Triggered by: PromotionCreated, PromotionUpdated, PromotionDeleted, PromotionExpired
    // Ensures ES has latest promotion fields for function_score boosting.
    // =============================================================================
    x.AddConsumer<ProductPromotionSearchIndexConsumer, ProductPromotionSearchIndexConsumerDefinition>();

    // =============================================================================
    // TENANT CURRENCY PROJECTION CONSUMER (Phase 10.4.X - Currency Authority Lockdown)
    // Projects tenant currency changes from PostgreSQL to MongoDB.
    // Handles TenantCurrencyChangedEvent published on tenant create/update.
    // Ensures StorefrontConfig.Currency stays in sync with Tenant.Currency.
    // =============================================================================
    x.AddConsumer<TenantCurrencyProjectionConsumer, TenantCurrencyProjectionConsumerDefinition>();

    // =============================================================================
    // ORDER PROJECTION CONSUMER (Phase 10.5.A - Order Lifecycle State Machine)
    // Projects order lifecycle events from PostgreSQL to MongoDB.
    // Handles OrderCreated, StatusChanged, PaymentChanged, ShipmentChanged, etc.
    // Storefront reads ONLY from MongoDB - never from SQL for orders.
    // 
    // TOPOLOGY FIX: Registered WITHOUT ConsumerDefinition to allow automatic
    // endpoint name generation and binding creation by MassTransit.
    // =============================================================================
    x.AddConsumer<OrderProjectionConsumer, OrderProjectionConsumerDefinition>();

    // =============================================================================
    // PRODUCT VARIANT PROJECTION CONSUMER (Phase 11.3.1 - Shopify-Grade Variants)
    // Projects variant events from PostgreSQL to MongoDB ProductDocument.Variants[].
    // Handles Created, Updated, Deleted, and StockChanged events.
    // Variants are EMBEDDED in ProductDocument - Shopify model for efficient reads.
    // VariantId is the ONLY identity - no attribute-based lookups.
    // =============================================================================
    x.AddConsumer<ProductVariantProjectionConsumer, ProductVariantProjectionConsumerDefinition>();
    x.AddConsumer<CommerceHubProjectionConsumer, CommerceHubProjectionConsumerDefinition>();
    x.AddConsumer<CommerceHubOrderProjectionConsumer, CommerceHubOrderProjectionConsumerDefinition>();

    // =============================================================================
    // PRODUCT MEDIA PROJECTION CONSUMER (Phase 22 - Product Media Model)
    // Projects ProductMediaChangedEvent from PostgreSQL to MongoDB ProductDocument.Media[].
    // Keeps the storefront read model ready for multi-image/video galleries.
    // =============================================================================
    x.AddConsumer<ProductMediaChangedEventConsumer>();

    // =============================================================================
    // MARKETPLACE SERVICE DISCOVERY CONSUMER (Phase P2 - Marketplace)
    // Projects service events (Created, Updated, Activated) to Elasticsearch.
    // Also updates service documents with trust metrics and request stats.
    // =============================================================================
    x.AddConsumer<ServiceDiscoveryProjectionConsumer, ServiceDiscoveryProjectionConsumerDefinition>();

    // =============================================================================
    // MARKETPLACE TRUST CONSUMERS (Phase P2 - Trust & Safety)
    // 1. ProviderTrustConsumer: Aggregates reviews into ProviderTrustAggregate (Write)
    // 2. ProviderTrustProjectionConsumer: Projects trust stats to MongoDB (Read)
    // =============================================================================
    x.AddConsumer<ProviderTrustConsumer, ProviderTrustConsumerDefinition>();
    x.AddConsumer<ProviderTrustProjectionConsumer, ProviderTrustProjectionConsumerDefinition>();

    // =============================================================================
    // ANALYTICS EVENT CONSUMER (PHASE 1: Canonical Analytics Event Foundation)
    // Processes analytics events from the event bus.
    // Writes to both Redis (hot) and MongoDB (cold) storage.
    // Idempotent by EventId - safe to replay.
    // Best-effort delivery - never blocks storefront.
    // =============================================================================
    x.AddConsumer<AnalyticsEventConsumer, AnalyticsEventConsumerDefinition>();
    x.AddConsumer<AuthoritativePurchaseAnalyticsConsumer, AuthoritativePurchaseAnalyticsConsumerDefinition>();

    // =============================================================================
    // ORDER STATUS SYNC CONSUMER (Phase 2 - Event-Driven Order Status Sync)
    // Listens to shipment and payment events to sync OrderStatus automatically.
    // Shipment lifecycle events → Order status updates
    // Payment confirmation → Pending → Confirmed transition
    // Orders REACT to events, they do NOT control them.
    // =============================================================================
    x.AddConsumer<OrderStatusSyncConsumer, OrderStatusSyncConsumerDefinition>();

    // =============================================================================
    // FULFILLMENT DELIVERED EVENT CONSUMER (Phase OFL - Order Fulfillment Lifecycle)
    // Integrates Fulfillment Lifecycle with Payment Lifecycle.
    // FulfillmentDeliveredEvent -> PaymentLifecycleService.MarkOfflineCollectionDeliveredAsync() -> Ledger
    // Ensures COD revenue recognition happens exactly at delivery.
    // =============================================================================
    x.AddConsumer<FulfillmentDeliveredEventConsumer>();
    x.AddConsumer<ShipmentCodCollectedConsumer, ShipmentCodCollectedConsumerDefinition>();

    // =============================================================================
    // AFFILIATE SYSTEM CONSUMERS (Phase AF-2 - Event Pipeline)
    // Payment-capture consumers: Trigger commission recording on payment events
    // Refund commission reversals are written transactionally in PaymentLifecycleService,
    // so the legacy refund bridge consumers are intentionally not registered.
    // All consumers are idempotent — duplicate events are silently ignored.
    // =============================================================================
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Affiliate.OnlinePaymentCapturedAffiliateConsumer>();
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Affiliate.CodDeliveredAffiliateConsumer>();
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Affiliate.WalletPaymentApprovedAffiliateConsumer>();
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Affiliate.OrderCollectionAccountingPostedAffiliateConsumer>();
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Affiliate.AffiliateOrderDeliveredConsumer>();
    
    // AF-18: OrderCompleted consumer — triggers commission recording when order reaches Completed status.
    // CRITICAL: Ensures commission is recorded even if order skips Delivered status.
    // Idempotent: Safe to call after CodDeliveredAffiliateConsumer (unique constraint prevents duplicates).
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Affiliate.OrderCompletedAffiliateConsumer>();
    
    // AF-5 / AF-10B: Projection consumer — updates MongoDB ProductDocument, Elasticsearch,
    // AND public_affiliate_stores read model when affiliate policy changes.
    // CRITICAL: Without this, enabling affiliate on a product has NO effect on discovery.
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Affiliate.AffiliateProductPolicyProjectionConsumer>();

    // AF-22: Product Intelligence projection consumer — builds affiliate_product_intelligence MongoDB collection.
    // Listens to: ProductCreated, ProductUpdated, ProductStockChanged, AffiliateProductPolicyChanged, StoreProfileUpdated.
    // CRITICAL: Without this, affiliate discover page shows "No Products Found".
    // Event-driven, read-only, no SQL joins, tenant-safe.
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Affiliate.AffiliateProductIntelligenceProjectionConsumer>();

    // AF-30A: Category updated consumer — propagates category name/slug changes to MongoDB projections.
    // Listens to: AffiliateCategoryUpdatedEvent (published when admin renames/re-slugs a category).
    // CRITICAL: Without this, category renames in SQL are NOT reflected in MongoDB product intelligence.
    // Uses UpdateMany (idempotent — safe for replay).
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Affiliate.AffiliateCategoryUpdatedConsumer>();

    // AF-11: Attribution consumer — creates AffiliateAttribution when affiliate-referred orders are placed.
    // CRITICAL: Without this, no AffiliateAttribution → no commission recording → empty portal.
    // Must run BEFORE payment-capture consumers to ensure attribution exists when commission is calculated.
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Affiliate.AffiliateOrderAttributionConsumer>();

    // AF-3: Projection consumers — update MongoDB dashboards, order documents, and earnings summaries.
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Affiliate.AffiliateAttributedProjectionConsumer>();
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Affiliate.AffiliateCommissionEarnedProjectionConsumer>();
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Affiliate.AffiliateCommissionReversedProjectionConsumer>();
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Affiliate.AffiliateCommissionApprovedProjectionConsumer>();
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Affiliate.AffiliateCommissionPaidProjectionConsumer>();
    
    // AF-21: Order lifecycle projection consumer — updates affiliate order documents with order/fulfillment status.
    // Listens to OrderCreatedEvent, OrderStatusChangedEvent, OrderCompletedEvent, OrderCancelledEvent,
    // FulfillmentDeliveredEvent, OrderFulfillmentCancelledEvent.
    // CRITICAL: Only updates existing documents (no upsert), enforces tenant isolation.
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Affiliate.OrderLifecycleAffiliateProjectionConsumer>();
    
    // AF-14: Accounting bridge consumers — post journal entries to General Ledger
    // when affiliate commissions are earned. Paid commission accounting is owned by
    // AffiliateSettlementOrchestrator inside the payout transaction.
    // AffiliateCommissionPaidEvent remains projection-only to avoid duplicate GL clearing.
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Affiliate.AffiliateCommissionEarnedAccountingConsumer>();

    // AF-4: Analytics consumers — update click-to-conversion tracking.
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Affiliate.AffiliateLinkClickedConsumer>();
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Affiliate.AffiliateClickConversionConsumer>();

    // AF-24: Daily analytics projection consumers — time-series click/order/earnings tracking.
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Affiliate.AffiliateAnalyticsDailyClickConsumer>();
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Affiliate.AffiliateAnalyticsDailyEarningConsumer>();

    // =============================================================================
    // MARKETPLACE EVENT BACKBONE (Production-grade backend event foundation)
    // Standardized immutable facts emitted through the transactional outbox and
    // consumed via dedicated queues with retry + idempotency protection.
    // =============================================================================
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Marketplace.MarketplaceOrderCreatedConsumer, PointOfSale.Infrastructure.Consumers.Marketplace.MarketplaceOrderCreatedConsumerDefinition>();
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Marketplace.MarketplacePaymentCompletedConsumer, PointOfSale.Infrastructure.Consumers.Marketplace.MarketplacePaymentCompletedConsumerDefinition>();
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Marketplace.MarketplaceOrderDeliveredConsumer, PointOfSale.Infrastructure.Consumers.Marketplace.MarketplaceOrderDeliveredConsumerDefinition>();
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Marketplace.MarketplaceRefundProcessedConsumer, PointOfSale.Infrastructure.Consumers.Marketplace.MarketplaceRefundProcessedConsumerDefinition>();
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Marketplace.MarketplaceProductUpdatedConsumer, PointOfSale.Infrastructure.Consumers.Marketplace.MarketplaceProductUpdatedConsumerDefinition>();
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Marketplace.MarketplaceReviewCreatedConsumer, PointOfSale.Infrastructure.Consumers.Marketplace.MarketplaceReviewCreatedConsumerDefinition>();
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Marketplace.MarketplaceReviewUpdatedConsumer, PointOfSale.Infrastructure.Consumers.Marketplace.MarketplaceReviewUpdatedConsumerDefinition>();
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Marketplace.MarketplaceReviewApprovedConsumer, PointOfSale.Infrastructure.Consumers.Marketplace.MarketplaceReviewApprovedConsumerDefinition>();
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Marketplace.MarketplaceReviewRejectedConsumer, PointOfSale.Infrastructure.Consumers.Marketplace.MarketplaceReviewRejectedConsumerDefinition>();
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Marketplace.MarketplaceReviewDeletedConsumer, PointOfSale.Infrastructure.Consumers.Marketplace.MarketplaceReviewDeletedConsumerDefinition>();
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Marketplace.HomepageProjectionConsumer, PointOfSale.Infrastructure.Consumers.Marketplace.HomepageProjectionConsumerDefinition>();
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.Marketplace.MarketplaceFaultMonitorConsumer, PointOfSale.Infrastructure.Consumers.Marketplace.MarketplaceFaultMonitorConsumerDefinition>();

    x.AddEntityFrameworkOutbox<ApplicationDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
        
        // PHASE 10.5.A: Configure faster outbox delivery for order events
        o.QueryDelay = TimeSpan.FromSeconds(1); // Poll every 1 second instead of default 10s
        o.DuplicateDetectionWindow = TimeSpan.FromMinutes(5);
    });

    // =============================================================================
    // WHATSAPP CONSUMERS (Phase 13)
    // =============================================================================
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.WhatsApp.WhatsAppOrderConfirmationConsumer>();
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.WhatsApp.WhatsAppOtpConsumer>();
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.WhatsApp.WhatsAppSessionMonitorConsumer>();
    
    // WhatsApp Payment Approval Consumers (Manual Wallet)
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.WhatsApp.WhatsAppPaymentApprovalRequestedConsumer>();
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.WhatsApp.WhatsAppPaymentApprovedNotificationConsumer>();
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.WhatsApp.WhatsAppPaymentRejectedNotificationConsumer>();

    // =============================================================================
    // ABANDONED CART RECOVERY CONSUMERS (Smart Cart Recovery Engine)
    // =============================================================================
    // CartActivityConsumer: Tracks cart activity for abandonment detection
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.AbandonedCart.CartActivityConsumer>();
    // RecoveryOrchestrationConsumer: Calls n8n webhook to trigger recovery workflow
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.AbandonedCart.RecoveryOrchestrationConsumer>();
    // OrderRecoveryConsumer: Marks abandoned carts as recovered when orders complete
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.AbandonedCart.OrderRecoveryConsumer, PointOfSale.Infrastructure.Consumers.AbandonedCart.OrderRecoveryConsumerDefinition>();

    // =============================================================================
    // ABANDONED CHECKOUT PROJECTION CONSUMER (Phase: Lost Orders)
    // Projects CheckoutSession lifecycle events to MongoDB for admin dashboard.
    // Handles: Started, Updated, Abandoned, Converted events.
    // =============================================================================
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.CheckoutSessionProjectionConsumer>();
    // Listens for OrderCreatedEvent and marks corresponding checkout session as converted.
    x.AddConsumer<PointOfSale.Infrastructure.Consumers.CheckoutSessionOrderConversionConsumer>();

    // =============================================================================
    // SERVICES MARKETPLACE CONSUMERS (Audit Hardening)
    // ⚠️ REMOVED: Duplicate registrations of ProviderTrustConsumer, 
    // ProviderTrustProjectionConsumer, and ServiceDiscoveryProjectionConsumer.
    // These are already registered above (lines ~1130-1138).
    // Duplicate AddConsumer() calls cause MassTransit to create competing 
    // RabbitMQ endpoints, leading to message round-robin where half the 
    // events silently fail — ROOT CAUSE of services not appearing in ES.
    // =============================================================================

    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbitMqSettings = builder.Configuration.GetSection("RabbitMQ");
        cfg.Host(rabbitMqSettings["Host"] ?? "localhost", rabbitMqSettings["VirtualHost"] ?? "/", h =>
        {
            h.Username(rabbitMqSettings["Username"] ?? "guest");
            h.Password(rabbitMqSettings["Password"] ?? "guest");
        });

        // =============================================================================
        // MESSAGE TOPOLOGY: CheckoutSessionProofEvent (Sensitive Event)
        // This block only sets the entity name for routing purposes.
        // The actual outbox bypass is implemented at the application level:
        // - CheckoutSessionService.cs calls PublishSensitiveAsync() instead of PublishAsync()
        // - MassTransitEventPublisher.PublishSensitiveAsync() uses IBus.Publish() directly
        // - This bypasses the scoped IPublishEndpoint which would trigger outbox interception
        // - Result: Sensitive payment proof data never enters the EF Core outbox tables
        // =============================================================================
        cfg.Message<PointOfSale.Core.Events.CheckoutSessionProofEvent>(m =>
        {
            m.SetEntityName("checkout-session-proof-event");
        });

        // Configure retry policy for transient failures
        cfg.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(30)));

        // PHASE CR-2: Event Bus Delivery Monitoring
        cfg.UsePublishFilter(typeof(PointOfSale.Infrastructure.Instrumentation.MarketplacePublishFilter<>), context);

        // =============================================================================
        // TOPOLOGY FIX: Remove Manual Configuration - Let MassTransit Auto-Wire
        // 
        // PROBLEM: Manual ReceiveEndpoint configuration with custom endpoint names
        // prevents MassTransit from creating exchange-to-queue bindings.
        // 
        // ROOT CAUSE: MassTransit's Bind<T>() doesn't work as expected with RabbitMQ.
        // The bindings are created based on IConsumer<T> interfaces during
        // ConfigureEndpoints(), but ONLY if no manual ReceiveEndpoint exists.
        // 
        // SOLUTION: Remove ALL manual configuration for OrderProjectionConsumer.
        // Let MassTransit auto-generate the endpoint name and create bindings.
        // The endpoint name will be "order-projection-consumer" (kebab-case).
        // =============================================================================
        
        // Configure all consumers automatically (including OrderProjectionConsumer)
        cfg.ConfigureEndpoints(context);
    });
});
builder.Services.AddScoped<IEventPublisher, MassTransitEventPublisher>();
builder.Services.AddScoped<PointOfSale.API.Security.ICheckoutSessionTokenService, PointOfSale.API.Security.CheckoutSessionTokenService>();
builder.Services.AddScoped<PointOfSale.API.Security.ISmartOfferRedeemTokenService, PointOfSale.API.Security.SmartOfferRedeemTokenService>();
builder.Services.AddScoped<PointOfSale.API.Security.IAffiliateAttributionEvidenceTokenService, PointOfSale.API.Security.AffiliateAttributionEvidenceTokenService>();

// =============================================================================
// NOTIFICATION SENDER (Phase 5.3 - Tracking & Notifications)
// Log-based sender for development. In production, swap with real providers.
// Singleton because LogNotificationSender is stateless.
// =============================================================================
builder.Services.AddSingleton<INotificationSender, LogNotificationSender>();

// Audit Services
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<PointOfSale.Core.Interfaces.ICorrelationIdAccessor, PointOfSale.API.Infrastructure.CorrelationIdAccessor>();
builder.Services.AddSingleton<PointOfSale.API.Services.Audit.IAuditQueue, PointOfSale.API.Services.Audit.AuditQueue>();
builder.Services.AddHostedService<PointOfSale.API.Services.Audit.AuditLogBackgroundService>();
builder.Services.AddSingleton<PointOfSale.API.Services.CommerceHub.ICommerceHubSearchAnalyticsQueue, PointOfSale.API.Services.CommerceHub.CommerceHubSearchAnalyticsQueue>();
builder.Services.AddHostedService<PointOfSale.API.Services.CommerceHub.CommerceHubSearchAnalyticsBackgroundService>();
builder.Services.AddHostedService<PointOfSale.API.Services.CommerceHub.RevokedTokenCleanupService>();
builder.Services.AddHostedService<PointOfSale.API.Services.Storefront.StorefrontRevokedTokenCleanupService>();
builder.Services.AddSingleton<PointOfSale.API.Services.CommerceHub.CommerceHubAutoHealingService>();
builder.Services.AddSingleton<PointOfSale.API.Services.CommerceHub.ICommerceHubAutoHealingQueue>(sp =>
    sp.GetRequiredService<PointOfSale.API.Services.CommerceHub.CommerceHubAutoHealingService>());
builder.Services.AddSingleton<PointOfSale.API.Services.CommerceHub.ICommerceHubAutoHealingMetrics>(sp =>
    sp.GetRequiredService<PointOfSale.API.Services.CommerceHub.CommerceHubAutoHealingService>());
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<PointOfSale.API.Services.CommerceHub.CommerceHubAutoHealingService>());

// =============================================================================
// MARKETPLACE OPERATIONAL TELEMETRY (PHASE CR-2)
// Defines all metrics (Meters/Counters/Histograms) for runtime monitoring.
// =============================================================================
builder.Services.AddSingleton<PointOfSale.Infrastructure.Instrumentation.MarketplaceTelemetry>();
builder.Services.AddSingleton<PointOfSale.Core.Abstractions.IMarketplaceTelemetry>(sp => 
    sp.GetRequiredService<PointOfSale.Infrastructure.Instrumentation.MarketplaceTelemetry>());
builder.Services.AddSingleton<PointOfSale.Infrastructure.Instrumentation.AnalyticsReliabilityTelemetry>();
builder.Services.AddSingleton<PointOfSale.Infrastructure.Instrumentation.AbandonedCartRecoveryTelemetry>();
builder.Services.AddSingleton<PointOfSale.Infrastructure.Instrumentation.StorefrontPublishTelemetry>();
builder.Services.AddSingleton<PointOfSale.Infrastructure.Instrumentation.CommerceReliabilityTelemetry>();
builder.Services.AddSingleton<PointOfSale.API.Instrumentation.PaymobOperationalTelemetry>();
builder.Services.AddSingleton<PointOfSale.API.Instrumentation.StripeOperationalTelemetry>();
builder.Services.AddSingleton<PointOfSale.API.Instrumentation.FawryOperationalTelemetry>();
builder.Services.AddSingleton<PointOfSale.API.Instrumentation.SuperAdminPayoutOrchestrationTelemetry>();
builder.Services.AddSingleton<PointOfSale.API.Instrumentation.CommerceHubPaymentOrchestrationTelemetry>();
builder.Services.TryAddSingleton<PointOfSale.API.Instrumentation.ManualPaymentOperationalTelemetry>();
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<PointOfSale.API.Configuration.CommerceHubPaymentsOptions>>().Value;
    // Guard against non-positive TTL by clamping to minimum (same as CommissionService runtime behavior)
    const int MinimumCacheDurationSeconds = 1;
    var cacheDurationSeconds = options.CommissionRulesCacheDurationSeconds > 0 
        ? options.CommissionRulesCacheDurationSeconds 
        : MinimumCacheDurationSeconds;
    var cacheDuration = TimeSpan.FromSeconds(cacheDurationSeconds);
    return new PointOfSale.API.Instrumentation.CommissionServiceTelemetry(cacheDuration);
});

// =============================================================================
// OPENTELEMETRY METRICS EXPORT PIPELINE
// Wires the custom Meter instances into a global MeterProvider with exporters
// so metrics actually reach production observability backends (OTLP/Prometheus).
// =============================================================================
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: "PointOfSale.API",
                    serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0"))
            .AddMeter(PointOfSale.Infrastructure.Instrumentation.MarketplaceTelemetry.MeterName)
            .AddMeter(PointOfSale.Infrastructure.Instrumentation.AffiliateTelemetry.MeterName)
            .AddMeter(PointOfSale.Infrastructure.Instrumentation.AnalyticsReliabilityTelemetry.MeterName)
            .AddMeter(PointOfSale.Infrastructure.Instrumentation.AbandonedCartRecoveryTelemetry.MeterName)
            .AddMeter(PointOfSale.Infrastructure.Instrumentation.StorefrontPublishTelemetry.MeterName)
            .AddMeter(PointOfSale.Infrastructure.Instrumentation.CommerceReliabilityTelemetry.MeterName)
            .AddMeter(PointOfSale.Infrastructure.Instrumentation.InventoryReservationTelemetry.MeterName)
            .AddMeter(PointOfSale.Infrastructure.Instrumentation.OperationalConvergenceTelemetry.MeterName)
            .AddMeter(PointOfSale.Infrastructure.Accounting.AccountingMetrics.MeterName)
            .AddMeter(PointOfSale.Infrastructure.Analytics.Delivery.ProviderDeliveryMetrics.MeterName)
            .AddMeter(PointOfSale.Infrastructure.Redis.Services.RedisPerformanceTelemetry.MeterName)
            .AddMeter(PointOfSale.Infrastructure.Elasticsearch.Services.SearchTelemetry.MeterName)
            .AddMeter(PointOfSale.API.Services.Checkout.CheckoutLockTelemetry.MeterName)
            .AddMeter(PointOfSale.API.Instrumentation.CommissionServiceTelemetry.MeterName)
            .AddMeter(PointOfSale.API.Instrumentation.PaymobOperationalTelemetry.MeterName)
            .AddMeter(PointOfSale.API.Instrumentation.StripeOperationalTelemetry.MeterName)
            .AddMeter(PointOfSale.API.Instrumentation.FawryOperationalTelemetry.MeterName)
            .AddMeter(PointOfSale.API.Instrumentation.SuperAdminPayoutOrchestrationTelemetry.MeterName)
            .AddMeter(PointOfSale.API.Instrumentation.CommerceHubPaymentOrchestrationTelemetry.MeterName)
            .AddMeter(PointOfSale.API.Instrumentation.ManualPaymentOperationalTelemetry.MeterName)
            .AddAspNetCoreInstrumentation()
            .AddPrometheusExporter();
        
        // Optional: Add OTLP exporter if endpoint is configured
        var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            metrics.AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(otlpEndpoint);
            });
        }
    })
    .WithTracing(tracing =>
    {
        tracing
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: "PointOfSale.API",
                    serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0"))
            .AddSource(PointOfSale.Infrastructure.Analytics.Delivery.ProviderDeliveryTelemetry.ActivitySourceName)
            .AddAspNetCoreInstrumentation();

        var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(otlpEndpoint);
            });
        }
    });

builder.Services.AddOptions<PointOfSale.Infrastructure.Observability.InventoryReservationMonitoringOptions>()
    .BindConfiguration(PointOfSale.Infrastructure.Observability.InventoryReservationMonitoringOptions.SectionName)
    .Validate(options =>
    {
        if (options.StaleReservationThresholdMinutes <= 0)
            return false;
        if (options.ActivePaymentGraceMinutes <= 0)
            return false;
        if (options.ProviderReconciliationGraceMinutes <= 0)
            return false;
        if (options.OrphanReservationGraceMinutes <= 0)
            return false;
        if (options.CleanupBatchSize <= 0)
            return false;
        if (options.HighReservedQuantityThreshold <= 0)
            return false;
        return true;
    }, "InventoryReservationMonitoringOptions validation failed: StaleReservationThresholdMinutes, ActivePaymentGraceMinutes, ProviderReconciliationGraceMinutes, OrphanReservationGraceMinutes, CleanupBatchSize, and HighReservedQuantityThreshold must all be positive values.")
    .ValidateOnStart();

// =============================================================================
// PROMOTION EXPIRATION SERVICE (PHASE: Promotions Engine - Auto-Expiration)
// Background service that runs every 5 minutes to:
// - Find promotions that have passed their EndDate
// - Mark them as inactive in PostgreSQL
// - Publish PromotionExpiredEvent to clear badges from MongoDB
// - Invalidate Redis cache so storefront reflects changes
// =============================================================================
builder.Services.AddHostedService<PointOfSale.API.Services.Background.PromotionExpirationService>();
builder.Services.AddScoped<PointOfSale.API.Services.Audit.IAuditService, PointOfSale.API.Services.Audit.AuditService>();

// =============================================================================
// MULTI-TENANCY SERVICES
// TenantContext is scoped to hold the current tenant for each request.
// ITenantContext is the abstraction used by domain services and event publishers.
// =============================================================================
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
builder.Services.AddScoped<IAdminTenantAuthority, AdminTenantAuthority>();
builder.Services.AddScoped<IPosSessionAuthority, PosSessionAuthority>();

// Configure JWT Authentication
// =============================================================================
// DUAL JWT AUTHENTICATION:
// 1. Default "Bearer" scheme for POS users (User entity)
// 2. "SuperAdminJwt" scheme for platform owner access (SuperAdminUser entity)
// 3. "StorefrontJwt" scheme for Storefront customers (OnlineCustomer entity)
// These are COMPLETELY SEPARATE authentication systems.
// =============================================================================
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>()!;
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));

var superAdminJwtSettings = builder.Configuration.GetSection(SuperAdminJwtSettings.SectionName).Get<SuperAdminJwtSettings>()
    ?? new SuperAdminJwtSettings();

// =============================================================================
// C-1 FIX: FAIL FAST if SuperAdmin JWT secret is not explicitly configured.
// The SuperAdmin JWT MUST use a dedicated secret, issuer, and audience.
// Falling back to POS JWT credentials would allow tenant admins who know
// the claim structure to forge SuperAdmin tokens — privilege escalation.
// =============================================================================
if (string.IsNullOrWhiteSpace(superAdminJwtSettings.Secret))
{
    throw new InvalidOperationException(
        "SECURITY CRITICAL: SuperAdminJwt:Secret is not configured. " +
        "The Super Admin authentication system requires a dedicated JWT signing secret " +
        "that is separate from the POS JWT secret. " +
        $"Configure '{SuperAdminJwtSettings.SectionName}:Secret' in appsettings or environment variables.");
}

if (string.IsNullOrWhiteSpace(superAdminJwtSettings.Issuer))
{
    superAdminJwtSettings.Issuer = $"{jwtSettings.Issuer}:super-admin";
}

if (string.IsNullOrWhiteSpace(superAdminJwtSettings.Audience))
{
    superAdminJwtSettings.Audience = $"{jwtSettings.Audience}:super-admin";
}

if (superAdminJwtSettings.AccessTokenExpirationMinutes <= 0)
{
    superAdminJwtSettings.AccessTokenExpirationMinutes = jwtSettings.AccessTokenExpirationMinutes;
}

if (superAdminJwtSettings.RefreshTokenExpirationDays <= 0)
{
    superAdminJwtSettings.RefreshTokenExpirationDays = jwtSettings.RefreshTokenExpirationDays;
}

builder.Services.Configure<SuperAdminJwtSettings>(options =>
{
    options.Secret = superAdminJwtSettings.Secret;
    options.Issuer = superAdminJwtSettings.Issuer;
    options.Audience = superAdminJwtSettings.Audience;
    options.AccessTokenExpirationMinutes = superAdminJwtSettings.AccessTokenExpirationMinutes;
    options.RefreshTokenExpirationDays = superAdminJwtSettings.RefreshTokenExpirationDays;
});

var storefrontJwtSettings = builder.Configuration.GetSection("StorefrontJwt").Get<PointOfSale.API.Models.StorefrontJwtSettings>()
    ?? new PointOfSale.API.Models.StorefrontJwtSettings();
var customerJwtSettings = builder.Configuration.GetSection(PointOfSale.API.Models.CustomerJwtSettings.SectionName)
    .Get<PointOfSale.API.Models.CustomerJwtSettings>()
    ?? new PointOfSale.API.Models.CustomerJwtSettings();

// Y1 Audit Fix: Dedicated affiliate JWT settings — cryptographic boundary isolation
var affiliateJwtSettings = builder.Configuration.GetSection(PointOfSale.API.Models.AffiliateJwtSettings.SectionName)
    .Get<PointOfSale.API.Models.AffiliateJwtSettings>()
    ?? new PointOfSale.API.Models.AffiliateJwtSettings();

// =============================================================================
// PASS A1 HARDENING: FAIL FAST if Affiliate JWT secret is not explicitly configured.
// An empty secret would cause all affiliate token validations to fail at runtime
// with an opaque cryptographic error instead of a clear configuration message.
// Like SuperAdmin, the Affiliate JWT MUST use a dedicated secret separate from
// POS, Storefront, and Customer JWT credentials.
// =============================================================================
if (string.IsNullOrWhiteSpace(affiliateJwtSettings.Secret))
{
    throw new InvalidOperationException(
        "SECURITY CRITICAL: AffiliateJwt:Secret is not configured. " +
        "The Affiliate authentication system requires a dedicated JWT signing secret " +
        "that is separate from POS, Storefront, and Customer JWT secrets. " +
        $"Configure '{PointOfSale.API.Models.AffiliateJwtSettings.SectionName}:Secret' in appsettings or environment variables.");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
// POS JWT Authentication (default)
.AddJwtBearer(options =>
{
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            // Browser sessions use HttpOnly cookies so JavaScript never needs
            // direct access to the bearer token. Keep the legacy cookie name
            // during the migration window so existing sessions fail closed only
            // when they naturally expire.
            if (context.Request.Cookies.TryGetValue(AuthCookieConfiguration.PosAccessTokenCookieName, out var cookieToken))
            {
                context.Token = cookieToken;
            }
            else if (context.Request.Cookies.TryGetValue(AuthCookieConfiguration.LegacyPosAccessTokenCookieName, out var legacyCookieToken))
            {
                context.Token = legacyCookieToken;
            }
            // Priority 2: Authorization header (for REST APIs)
            else if (context.Request.Headers.ContainsKey("Authorization"))
            {
                var authHeader = context.Request.Headers["Authorization"].ToString();
                if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    context.Token = authHeader.Substring("Bearer ".Length).Trim();
                }
            }
            return Task.CompletedTask;
        },
        OnTokenValidated = async context =>
        {
            // PHASE CR-1: Validate tenant claim exists (server-authoritative)
            var tenantClaim = context.Principal?.FindFirst("tenant_id")?.Value;
            if (string.IsNullOrEmpty(tenantClaim) || !Guid.TryParse(tenantClaim, out _))
            {
                context.Fail("Missing or invalid tenant_id claim in JWT");
                return;
            }

            var sessionAuthority = context.HttpContext.RequestServices
                .GetRequiredService<IPosSessionAuthority>();
            var authorityResult = await sessionAuthority.ValidateAccessTokenAsync(
                context.Principal!,
                context.HttpContext.RequestAborted);

            if (!authorityResult.Authorized)
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogWarning(
                    "POS session authority rejected access token | Reason: {Reason} | UserId: {UserId} | TenantId: {TenantId} | Path: {Path}",
                    authorityResult.Reason,
                    authorityResult.UserId,
                    authorityResult.TenantId,
                    context.HttpContext.Request.Path.Value ?? "unknown");
                context.Fail("POS session is no longer current");
            }
        }
    };
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
        ClockSkew = TimeSpan.Zero,
        // CRITICAL: Map role claim type for authorization policies
        RoleClaimType = System.Security.Claims.ClaimTypes.Role,
        NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier
    };
})
// Super Admin JWT Authentication (dedicated scheme, shared signing config, no tenant claim)
// Use [Authorize(Policy = "RequireSuperAdmin")] for platform-owner endpoints
.AddJwtBearer(SuperAdminSecurityConstants.AuthenticationScheme, options =>
{
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            if (context.Request.Cookies.TryGetValue(AuthCookieConfiguration.SuperAdminAccessTokenCookieName, out var cookieToken))
            {
                context.Token = cookieToken;
            }
            else if (context.Request.Headers.ContainsKey("Authorization"))
            {
                var authHeader = context.Request.Headers["Authorization"].ToString();
                if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    context.Token = authHeader.Substring("Bearer ".Length).Trim();
                }
            }

            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            if (!SuperAdminTokenClaimsValidator.HasRequiredClaims(context.Principal))
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogWarning(
                    "SuperAdmin token rejected during validation | RequestId: {RequestId} | Path: {Path} | Method: {Method} | Role: {Role} | AuthType: {AuthType}",
                    context.HttpContext.TraceIdentifier,
                    context.HttpContext.Request.Path.Value ?? "unknown",
                    context.HttpContext.Request.Method,
                    context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
                        ?? context.Principal?.FindFirst("role")?.Value
                        ?? "missing",
                    context.Principal?.FindFirst(SuperAdminSecurityConstants.AuthTypeClaim)?.Value ?? "missing");
                context.Fail("Token is not a Super Admin token");
            }

            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogWarning(
                context.Exception,
                "SuperAdmin authentication failed | RequestId: {RequestId} | Path: {Path} | Method: {Method}",
                context.HttpContext.TraceIdentifier,
                context.HttpContext.Request.Path.Value ?? "unknown",
                context.HttpContext.Request.Method);
            return Task.CompletedTask;
        }
    };

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = superAdminJwtSettings.Issuer,
        ValidAudience = superAdminJwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(superAdminJwtSettings.Secret)),
        ClockSkew = TimeSpan.Zero,
        RoleClaimType = System.Security.Claims.ClaimTypes.Role,
        NameClaimType = System.Security.Claims.ClaimTypes.Email
    };
})
// Storefront JWT Authentication (separate issuer/audience/secret)
// Use [Authorize(AuthenticationSchemes = "StorefrontJwt")] for Storefront endpoints
.AddJwtBearer("StorefrontJwt", options =>
{
    // CRITICAL: Disable inbound claim remapping so standard JWT claim names
    // ("jti", "sub", etc.) are preserved as-is after token validation.
    // Without this, .NET's default mapping silently renames "jti" to its full
    // URI form, making FindFirst(JwtRegisteredClaimNames.Jti) in
    // StorefrontJwtBlacklistMiddleware return null and causing all valid
    // Storefront requests to be rejected with INVALID_TOKEN.
    // Custom claims (e.g., "CustomerId", "TenantId") are not in the mapping
    // table and are unaffected by this setting.
    options.MapInboundClaims = false;
    
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            if (context.Request.Cookies.TryGetValue(AuthCookieConfiguration.StorefrontAccessTokenCookieName, out var cookieToken))
            {
                context.Token = cookieToken;
                return Task.CompletedTask;
            }

            if (!JwtBearerRoutingHelper.TryGetBearerToken(context.Request, out var token))
            {
                return Task.CompletedTask;
            }

            if (!JwtBearerRoutingHelper.TryReadIssuer(token, out var issuer) ||
                !string.Equals(issuer, storefrontJwtSettings.Issuer, StringComparison.Ordinal))
            {
                context.NoResult();
                return Task.CompletedTask;
            }

            context.Token = token;
            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogError(
                "StorefrontJwt Authentication Failed: {Message} | Path: {Path}",
                context.Exception.Message,
                context.Request.Path);
            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogInformation(
                "StorefrontJwt Token Validated. Issuer: {Issuer} | Path: {Path}",
                context.Principal?.FindFirst("iss")?.Value,
                context.Request.Path);
            return Task.CompletedTask;
        },
        OnChallenge = context => 
        {
             var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
             logger.LogWarning("StorefrontJwt OnChallenge. Error: {Error}, ErrorDescription: {ErrorDesc}", context.Error, context.ErrorDescription);
             return Task.CompletedTask;
        }
    };
    
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = storefrontJwtSettings.Issuer,
        ValidAudience = storefrontJwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(storefrontJwtSettings.Secret)),
        ClockSkew = TimeSpan.Zero,
        // Custom claim validation for Storefront
        NameClaimType = "CustomerId"
    };
})
// CommerceHub Customer JWT Authentication
// Use [Authorize(AuthenticationSchemes = "CustomerJwt", Roles = "Customer")] for customer order/account endpoints
.AddJwtBearer(PointOfSale.API.Security.CustomerSecurityConstants.AuthenticationScheme, options =>
{
    options.MapInboundClaims = false;
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            if (context.Request.Cookies.TryGetValue(AuthCookieConfiguration.CommerceHubCustomerAccessTokenCookieName, out var cookieToken))
            {
                context.Token = cookieToken;
                return Task.CompletedTask;
            }

            if (!JwtBearerRoutingHelper.TryGetBearerToken(context.Request, out var token))
            {
                return Task.CompletedTask;
            }

            if (!JwtBearerRoutingHelper.TryReadIssuer(token, out var issuer) ||
                !string.Equals(issuer, customerJwtSettings.Issuer, StringComparison.Ordinal))
            {
                context.NoResult();
                return Task.CompletedTask;
            }

            context.Token = token;
            return Task.CompletedTask;
        },
        OnTokenValidated = async context =>
        {
            var jtiClaim = context.Principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
            var customerIdClaim = context.Principal?.FindFirst("CustomerId")?.Value;

            if (string.IsNullOrWhiteSpace(jtiClaim))
            {
                context.Fail("Missing token jti claim.");
                return;
            }

            if (!Guid.TryParse(customerIdClaim, out var customerId))
            {
                context.Fail("Missing or invalid customer identifier.");
                return;
            }

            var revocationService = context.HttpContext.RequestServices
                .GetRequiredService<PointOfSale.API.Services.CommerceHub.ICustomerTokenRevocationService>();
            var governanceService = context.HttpContext.RequestServices
                .GetRequiredService<PointOfSale.API.Services.Governance.IUserGovernanceService>();

            try
            {
                if (await revocationService.IsTokenRevokedAsync(jtiClaim, context.HttpContext.RequestAborted))
                {
                    context.Fail("Token has been revoked.");
                    return;
                }

                await revocationService.EnsureTokenSessionStillValidAsync(
                    customerId,
                    context.Principal!,
                    context.HttpContext.RequestAborted);

                await governanceService.EnsureCustomerLoginAllowedAsync(customerId, context.HttpContext.RequestAborted);
            }
            catch (PointOfSale.API.Security.CustomerTokenSessionRevokedException ex)
            {
                context.Fail(ex.Message);
            }
            catch (PointOfSale.API.Services.Governance.UserGovernanceException ex)
            {
                context.Fail(ex.Message);
            }
            catch (PointOfSale.API.Security.TokenRevocationCheckUnavailableException)
            {
                context.Fail("Token revocation status could not be verified.");
            }
            catch (Exception)
            {
                context.Fail("Customer access validation could not be completed.");
            }
        },
        OnAuthenticationFailed = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogWarning(
                "CustomerJwt Authentication Failed: {Message} | Path: {Path}",
                context.Exception.Message,
                context.Request.Path);
            return Task.CompletedTask;
        }
    };

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = customerJwtSettings.Issuer,
        ValidAudience = customerJwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(customerJwtSettings.Secret)),
        ClockSkew = TimeSpan.Zero,
        RoleClaimType = System.Security.Claims.ClaimTypes.Role,
        NameClaimType = "CustomerId",
        // SECURITY: Disable inbound claim remapping so standard JWT claim names
        // ("jti", "sub", etc.) are preserved as-is after token validation.
        // Without this, .NET's default mapping silently renames "jti" to its full
        // URI form, making FindFirst(JwtRegisteredClaimNames.Jti) return null and
        // causing the token revocation check to silently pass for revoked tokens.
        // Custom claims (e.g., "CustomerId") are not in the mapping table and are
        // unaffected by this setting.
    };
})
// =============================================================================
// AFFILIATE JWT AUTHENTICATION (AF-AUTH-ISOLATION)
//
// CRITICAL: This scheme reads only the affiliate auth cookie or an explicit
// affiliate Authorization header. It never inspects POS or Super Admin cookies.
//
// ROOT CAUSE FIX: the dedicated scheme guarantees affiliate requests cannot be
// authenticated with an unrelated admin session cookie.
//
// Use [Authorize(AuthenticationSchemes = "AffiliateJwt")] for Affiliate endpoints
// or bind via policy: policy.AuthenticationSchemes.Add("AffiliateJwt")
// =============================================================================
.AddJwtBearer("AffiliateJwt", options =>
{
    options.Events = new JwtBearerEvents
    {
        // HEADER-ONLY TOKEN EXTRACTION — cookies are invisible to this scheme
        OnMessageReceived = context =>
        {
            // ONLY read from Authorization header — NEVER from cookies
            if (context.Request.Cookies.TryGetValue(AuthCookieConfiguration.AffiliateAccessTokenCookieName, out var cookieToken))
            {
                context.Token = cookieToken;
            }
            else if (context.Request.Headers.ContainsKey("Authorization"))
            {
                var authHeader = context.Request.Headers["Authorization"].ToString();
                if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    context.Token = authHeader.Substring("Bearer ".Length).Trim();
                }
            }
            return Task.CompletedTask;
        },
        OnTokenValidated = async context =>
        {
            // AF-19: Global affiliates - NO tenant_id claim required
            // Validate this is actually an affiliate token (has role=Affiliate)
            var roleClaim = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            if (roleClaim != "Affiliate")
            {
                context.Fail("Token is not an affiliate token");
                return;
            }

            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            var affiliateAccountClaim = context.Principal?.FindFirst("affiliate_account_id")?.Value;
            var affiliateUserClaim = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? context.Principal?.FindFirst("sub")?.Value;
            var sessionClaim = context.Principal?.FindFirst("affiliate_session")?.Value;
            var issuedAtClaim = context.Principal?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Iat)?.Value;

            if (!Guid.TryParse(affiliateUserClaim, out var affiliateUserId) ||
                !Guid.TryParse(affiliateAccountClaim, out var affiliateAccountId) ||
                string.IsNullOrWhiteSpace(sessionClaim))
            {
                context.Fail("Affiliate token is missing required identity/session claims");
                return;
            }

            var dbContext = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
            var affiliateAuthority = await dbContext.AffiliateUsers
                .IgnoreQueryFilters()
                .Where(user =>
                    user.Id == affiliateUserId &&
                    user.AffiliateAccountId == affiliateAccountId &&
                    !user.IsDeleted)
                .Select(user => new
                {
                    user.EmailVerified,
                    user.RefreshToken,
                    user.TokensValidAfterUtc,
                    AccountActive = user.AffiliateAccount != null && user.AffiliateAccount.IsActive,
                    AccountDeleted = user.AffiliateAccount != null && user.AffiliateAccount.IsDeleted
                })
                .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

            if (affiliateAuthority == null ||
                !affiliateAuthority.EmailVerified ||
                !affiliateAuthority.AccountActive ||
                affiliateAuthority.AccountDeleted ||
                !MatchesAffiliateSessionBinding(sessionClaim, affiliateAuthority.RefreshToken))
            {
                context.Fail("Affiliate token is not current SQL authority");
                return;
            }

            if (affiliateAuthority.TokensValidAfterUtc.HasValue)
            {
                if (!long.TryParse(issuedAtClaim, out var issuedAtUnixSeconds))
                {
                    context.Fail("Affiliate token is missing issued-at authority");
                    return;
                }

                var issuedAtUtc = DateTimeOffset.FromUnixTimeSeconds(issuedAtUnixSeconds).UtcDateTime;
                if (issuedAtUtc <= affiliateAuthority.TokensValidAfterUtc.Value)
                {
                    context.Fail("Affiliate token predates session cutoff");
                    return;
                }
            }

            logger.LogInformation(
                "AffiliateJwt token validated. AffiliateAccountId: {AccountId} (Global Affiliate)",
                affiliateAccountClaim ?? "MISSING");

            return;
        },
        OnAuthenticationFailed = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogWarning(
                "AffiliateJwt authentication failed: {Error} | Path: {Path}",
                context.Exception.Message, context.Request.Path);
            return Task.CompletedTask;
        },
        OnChallenge = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogWarning(
                "AffiliateJwt OnChallenge. Error: {Error}, Path: {Path}",
                context.Error, context.Request.Path);
            return Task.CompletedTask;
        }
    };

    // Y1 Audit Fix: Dedicated signing key/issuer/audience — cryptographic boundary isolation from POS
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = affiliateJwtSettings.Issuer,
        ValidAudience = affiliateJwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(affiliateJwtSettings.Secret)),
        ClockSkew = TimeSpan.Zero,
        RoleClaimType = System.Security.Claims.ClaimTypes.Role,
        NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier
    };
})
.AddScheme<AuthenticationSchemeOptions, ManualWalletUploadTokenAuthenticationHandler>(
    ManualWalletUploadTokenAuthenticationDefaults.SchemeName,
    _ => { });

// AF-AUTH-ISOLATION: Authorization policies — scheme-bound token scope isolation
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    options.AddPolicy(SuperAdminSecurityConstants.PolicyName, policy =>
    {
        policy.AuthenticationSchemes.Add(SuperAdminSecurityConstants.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireRole(SuperAdminSecurityConstants.Role);
        policy.RequireClaim(
            SuperAdminSecurityConstants.AuthTypeClaim,
            SuperAdminSecurityConstants.AuthTypeValue);
    });

    // Affiliate Portal: MUST authenticate via AffiliateJwt scheme + require role=Affiliate
    // This ensures the AffiliateJwt handler (header-only) is used instead of the default
    // Bearer handler (cookie-first), preventing POS cookie collision.
    options.AddPolicy("AffiliateOnly", policy =>
    {
        policy.AuthenticationSchemes.Add("AffiliateJwt");
        policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, "Affiliate");
    });
    
    // AF-10 DIAGNOSTIC: Log authorization failures
    options.InvokeHandlersAfterFailure = true;
});

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// Configure Hangfire — always register so the server retries internally on transient PostgreSQL blips.
// Previously, a single failed CanInitializeHangfireStorage() probe permanently disabled Hangfire.
// Now the server retries storage connections, and HangfireInitializationHostedService
// handles deferred recurring-job registration with exponential backoff.
var hangfireDashboardPath = builder.Configuration["Hangfire:DashboardPath"] ?? "/hangfire";

if (!isAuthoritativeDeterministicTestHost && !string.IsNullOrWhiteSpace(defaultConnectionString))
{
    builder.Services.AddHangfire(config =>
        config.UsePostgreSqlStorage(c =>
            c.UseNpgsqlConnection(defaultConnectionString)));

    builder.Services.AddHangfireServer();
}

if (!isAuthoritativeDeterministicTestHost)
{
    builder.Services.AddHostedService<PointOfSale.API.Jobs.HangfireInitializationHostedService>();
}

static string GetRateLimitClientIp(HttpContext httpContext)
{
    var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();
    return string.IsNullOrWhiteSpace(clientIp)
        ? $"conn-{httpContext.Connection.Id}"
        : clientIp;
}

static string GetRateLimitTenantPartition(HttpContext httpContext)
{
    var tenantContext = httpContext.RequestServices.GetService<ITenantContext>();
    if (tenantContext?.TenantId is Guid tenantId && tenantId != Guid.Empty)
    {
        return tenantId.ToString("N");
    }

    return "unresolved-tenant";
}

/// <summary>
/// Gets a trusted subject partition for rate limiting based on authenticated claims.
/// Priority: 1) Authenticated customer ID from StorefrontJwt/CustomerJwt, 2) Session cookie, 3) Anonymous partition.
/// SECURITY: Never trust X-Customer-Id or X-Session-Id headers directly as they can be spoofed.
/// Explicitly authenticates against StorefrontJwt and CustomerJwt schemes to avoid collapsing authenticated customers into IP bucket.
/// NOTE: This method reads from HttpContext.Items cache populated by RateLimitSubjectResolutionMiddleware to avoid sync-over-async.
/// </summary>
static string GetTrustedRateLimitSubject(HttpContext httpContext, string clientIp)
{
    // Read from cache populated by RateLimitSubjectResolutionMiddleware
    // This avoids sync-over-async blocking in rate limit partition factories
    if (httpContext.Items.TryGetValue(PointOfSale.API.Middleware.RateLimitSubjectResolutionMiddleware.CacheKey, out var cached) 
        && cached is string cachedSubject)
    {
        return cachedSubject;
    }

    // Fallback to synchronous resolution if middleware didn't run (shouldn't happen in normal flow)
    // This uses the synchronous wrapper which doesn't perform explicit scheme authentication
    return PointOfSale.API.Security.StorefrontRateLimitSession.ResolveTrustedSubject(httpContext, clientIp);
}
static SlidingWindowRateLimiterOptions CreateSlidingWindowOptions(int permitLimit, int segmentsPerWindow = 2) => new()
{
    PermitLimit = permitLimit,
    Window = TimeSpan.FromMinutes(1),
    SegmentsPerWindow = segmentsPerWindow,
    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
    QueueLimit = 0
};

static bool IsHealthEndpoint(PathString path) => path.StartsWithSegments("/health");
static bool IsTestAuthEndpoint(PathString path) => path.StartsWithSegments("/api/test-auth", StringComparison.OrdinalIgnoreCase);

static bool MatchesAffiliateSessionBinding(string presentedBinding, string? currentBinding)
{
    if (string.IsNullOrWhiteSpace(presentedBinding) ||
        string.IsNullOrWhiteSpace(currentBinding))
    {
        return false;
    }

    try
    {
        var presentedBytes = Convert.FromHexString(presentedBinding);
        var currentBytes = Convert.FromHexString(currentBinding);
        return presentedBytes.Length == currentBytes.Length &&
               CryptographicOperations.FixedTimeEquals(presentedBytes, currentBytes);
    }
    catch (FormatException)
    {
        return false;
    }
}

static bool CanInitializeHangfireStorage(string? connectionString, out string? failureReason)
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        failureReason = "DefaultConnection is not configured.";
        return false;
    }

    try
    {
        var connectionStringBuilder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString)
        {
            Timeout = 5,
            CommandTimeout = 5
        };

        using var connection = new Npgsql.NpgsqlConnection(connectionStringBuilder.ConnectionString);
        connection.Open();
        failureReason = null;
        return true;
    }
    catch (Exception ex)
    {
        failureReason = ex.GetBaseException().Message;
        return false;
    }
}

// =============================================================================
// RATE LIMITING (Phase C5 - Coupon Abuse Protection)
// Sliding window rate limiter to prevent coupon brute-forcing.
// Policy "CouponRateLimit": 10 requests per minute per cart owner (session/customer)
// =============================================================================
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = RateLimitRejectionHandlers.WriteAsync;
    
    // Coupon rate limiting policy
    options.AddPolicy("CouponRateLimit", httpContext =>
    {
        var tenantId = GetRateLimitTenantPartition(httpContext);
        var clientIp = GetRateLimitClientIp(httpContext);
        var subject = GetTrustedRateLimitSubject(httpContext, clientIp);
        var partitionKey = $"coupon:{tenantId}:{subject}";
        
        return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ => CreateSlidingWindowOptions(10));
    });
    
    // Analytics rate limiting policy (Phase 1 - Analytics Event Foundation)
    // More permissive than coupon rate limiting since analytics is high-volume
    options.AddPolicy("analytics", httpContext =>
    {
        var tenantId = GetRateLimitTenantPartition(httpContext);
        var clientIp = GetRateLimitClientIp(httpContext);
        var subject = GetTrustedRateLimitSubject(httpContext, clientIp);
        var partitionKey = $"analytics:{tenantId}:{subject}";
        
        return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ => new System.Threading.RateLimiting.SlidingWindowRateLimiterOptions
        {
            PermitLimit = 100, // 100 events per minute per session
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 4,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 10 // Small queue for burst handling
        });
    });
    
    // Public auth endpoints are brute-force targets, so keep registration, login,
    // and refresh isolated per path, resolved tenant, and caller IP.
    options.AddPolicy("PublicRegisterRateLimit", httpContext =>
    {
        var clientIp = GetRateLimitClientIp(httpContext);
        var tenantPartition = GetRateLimitTenantPartition(httpContext);
        var path = httpContext.Request.Path.Value?.ToLowerInvariant() ?? "unknown";
        var partitionKey = $"public-register:{path}:tenant:{tenantPartition}:ip:{clientIp}";

        return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ => CreateSlidingWindowOptions(3));
    });

    options.AddPolicy("PublicLoginRateLimit", httpContext =>
    {
        var clientIp = GetRateLimitClientIp(httpContext);
        var tenantPartition = GetRateLimitTenantPartition(httpContext);
        var path = httpContext.Request.Path.Value?.ToLowerInvariant() ?? "unknown";
        var partitionKey = $"public-login:{path}:tenant:{tenantPartition}:ip:{clientIp}";

        return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ => CreateSlidingWindowOptions(5));
    });

    options.AddPolicy("PublicRefreshRateLimit", httpContext =>
    {
        var clientIp = GetRateLimitClientIp(httpContext);
        var tenantPartition = GetRateLimitTenantPartition(httpContext);
        var path = httpContext.Request.Path.Value?.ToLowerInvariant() ?? "unknown";
        var partitionKey = $"public-refresh:{path}:tenant:{tenantPartition}:ip:{clientIp}";

        return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ => CreateSlidingWindowOptions(10, segmentsPerWindow: 4));
    });

    options.AddPolicy("PublicReadRateLimit", httpContext =>
    {
        var clientIp = GetRateLimitClientIp(httpContext);
        var tenantPartition = GetRateLimitTenantPartition(httpContext);
        var path = httpContext.Request.Path.Value?.ToLowerInvariant() ?? "unknown";
        var partitionKey = $"public-read:{path}:tenant:{tenantPartition}:ip:{clientIp}";

        return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ => CreateSlidingWindowOptions(120, segmentsPerWindow: 4));
    });

    // Checkout OTP is externally reachable and high-abuse, so partition by the
    // resolved tenant plus caller/session identity.
    options.AddPolicy("CheckoutOtpRequestRateLimit", httpContext =>
    {
        var clientIp = GetRateLimitClientIp(httpContext);
        var tenantPartition = GetRateLimitTenantPartition(httpContext);
        var sessionId = httpContext.Request.Headers["X-Session-Id"].FirstOrDefault();
        var sessionPartition = string.IsNullOrWhiteSpace(sessionId) ? "anonymous" : sessionId.Trim();
        var partitionKey = $"checkout-otp-request:tenant:{tenantPartition}:session:{sessionPartition}:ip:{clientIp}";

        return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ => CreateSlidingWindowOptions(5));
    });

    options.AddPolicy("CheckoutOtpVerifyRateLimit", httpContext =>
    {
        var clientIp = GetRateLimitClientIp(httpContext);
        var tenantPartition = GetRateLimitTenantPartition(httpContext);
        var sessionId = httpContext.Request.Headers["X-Session-Id"].FirstOrDefault();
        var sessionPartition = string.IsNullOrWhiteSpace(sessionId) ? "anonymous" : sessionId.Trim();
        var partitionKey = $"checkout-otp-verify:tenant:{tenantPartition}:session:{sessionPartition}:ip:{clientIp}";

        return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ => CreateSlidingWindowOptions(10, segmentsPerWindow: 4));
    });

    options.AddPolicy("checkout", httpContext =>
    {
        var clientIp = GetRateLimitClientIp(httpContext);
        var tenantPartition = GetRateLimitTenantPartition(httpContext);
        
        // SECURITY FIX: Use trusted authenticated claims instead of untrusted headers
        // X-Customer-Id and X-Session-Id headers can be spoofed by clients to bypass rate limits
        var subjectPartition = GetTrustedRateLimitSubject(httpContext, clientIp);
        var partitionKey = $"checkout:tenant:{tenantPartition}:{subjectPartition}";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });

    // PATCH J: Affiliate Auth Rate Limiting
    // Prevents brute-force login attempts and credential stuffing
    options.AddPolicy("AffiliateAuthRateLimit", httpContext =>
    {
        var clientIp = GetRateLimitClientIp(httpContext);
        var path = httpContext.Request.Path.Value?.ToLowerInvariant() ?? "unknown";
        
        var partitionKey = $"affiliate-auth:{path}:ip:{clientIp}";

        return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ => CreateSlidingWindowOptions(5));
    });
    options.AddPolicy(SuperAdminSecurityConstants.RateLimitPolicyName, httpContext =>
    {
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var partitionKey = $"super-admin-auth:ip:{clientIp}";

        return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ => new System.Threading.RateLimiting.SlidingWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 2,
            QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    options.AddPolicy(SuperAdminSecurityConstants.GovernanceWriteRateLimitPolicyName, httpContext =>
    {
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var partitionKey = $"super-admin-governance-write:ip:{clientIp}";

        return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ => new System.Threading.RateLimiting.SlidingWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 4,
            QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    options.AddPolicy(RateLimitRejectionHandlers.FinancialWritePolicyName, new FinancialWriteRateLimiterPolicy());

    // Manual Wallet Upload Proof Rate Limiting
    // Prevents abuse of anonymous file upload endpoint
    // SECURITY: Uses resolved tenant from TenantResolutionMiddleware, not raw headers
    options.AddPolicy("UploadProofPolicy", httpContext =>
    {
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        
        // Get resolved tenant from TenantContext (set by TenantResolutionMiddleware)
        var tenantContext = httpContext.RequestServices.GetService<ITenantContext>();
        var resolvedTenantId = tenantContext?.TenantId;
        
        string partitionKey;
        if (resolvedTenantId.HasValue && resolvedTenantId.Value != Guid.Empty)
        {
            // Use resolved tenant ID from middleware
            partitionKey = $"upload-proof:{resolvedTenantId.Value}:ip:{clientIp}";
        }
        else
        {
            // Fallback to IP-only partition when no tenant is resolved
            partitionKey = $"upload-proof:ip:{clientIp}";
        }
        
        return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ => new System.Threading.RateLimiting.SlidingWindowRateLimiterOptions
        {
            PermitLimit = 10, // 10 uploads per minute per tenant/IP
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 2,
            QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst,
            QueueLimit = 0 // No queuing — reject immediately
        });
    });

    // Commerce AI Event Tracking Rate Limiting
    // Prevents telemetry spam and cost amplification on anonymous event ingestion
    // SECURITY: Uses resolved tenant from TenantResolutionMiddleware, not raw headers
    options.AddPolicy("CommerceAiEventPolicy", httpContext =>
    {
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        
        // Get resolved tenant from TenantContext (set by TenantResolutionMiddleware)
        var tenantContext = httpContext.RequestServices.GetService<ITenantContext>();
        var resolvedTenantId = tenantContext?.TenantId;
        
        string partitionKey;
        if (resolvedTenantId.HasValue && resolvedTenantId.Value != Guid.Empty)
        {
            // Use resolved tenant ID from middleware
            partitionKey = $"commerce-ai-event:{resolvedTenantId.Value}:ip:{clientIp}";
        }
        else
        {
            // Fallback to IP-only partition when no tenant is resolved
            partitionKey = $"commerce-ai-event:ip:{clientIp}";
        }
        
        return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ => new System.Threading.RateLimiting.SlidingWindowRateLimiterOptions
        {
            PermitLimit = 60, // 60 events per minute per tenant/IP (1 per second average)
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 4,
            QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst,
            QueueLimit = 0 // No queuing — reject immediately
        });
    });

    // Webhook endpoints are externally reachable and provider-controlled.
    // Partition by provider, caller IP, and resolved/claimed tenant so abuse of one
    // provider or tenant does not consume capacity for all webhook traffic.
    options.AddPolicy(WebhookRateLimiterPolicy.PolicyName, new WebhookRateLimiterPolicy());

    // Global fallback for other endpoints (if needed in future)
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        return RateLimitPartition.GetNoLimiter("global");
    });
});

// =============================================================================
// PRODUCTION OBSERVABILITY (Phase 12 - Performance & Scalability Hardening)
// Adds health checks for:
// - Projection drift (PostgreSQL vs MongoDB)
// - Redis latency monitoring
// - Stock reservation integrity
// Also adds event lag tracking for consumer monitoring.
// =============================================================================
builder.Services.AddProductionObservability();
builder.Services.AddHealthChecks()
    .AddCheck<PointOfSale.API.HealthChecks.PostgresDependencyHealthCheck>("postgresql", tags: new[] { "database", "postgresql", "ready", "dependency" })
    .AddCheck<PointOfSale.API.HealthChecks.RedisDependencyHealthCheck>("redis", tags: new[] { "cache", "redis", "ready", "dependency" })
    .AddCheck<PointOfSale.API.HealthChecks.MongoDependencyHealthCheck>("mongodb", tags: new[] { "mongodb", "ready", "dependency" })
    .AddCheck<PointOfSale.API.HealthChecks.ElasticsearchDependencyHealthCheck>("elasticsearch", tags: new[] { "elasticsearch", "search", "ready", "dependency" })
    .AddCheck<PointOfSale.API.HealthChecks.RabbitMqDependencyHealthCheck>("rabbitmq", tags: new[] { "messaging", "rabbitmq", "ready", "dependency" })
    .AddCheck<PointOfSale.API.HealthChecks.R2DependencyHealthCheck>("r2", tags: new[] { "storage", "r2", "ready", "dependency" })
    .AddCheck<PointOfSale.API.HealthChecks.PaymobFoundationHealthCheck>("paymob_foundation", tags: new[] { "payments", "paymob", "configuration", "ready" })
    .AddCheck<PointOfSale.API.HealthChecks.PaymobOperationalReadinessHealthCheck>("paymob_operational_readiness", tags: new[] { "payments", "paymob", "ready", "operational" })
    .AddCheck<PointOfSale.API.HealthChecks.StripeFoundationHealthCheck>("stripe_foundation", tags: new[] { "payments", "stripe", "configuration" })
    .AddCheck<PointOfSale.API.HealthChecks.StripeOperationalReadinessHealthCheck>("stripe_operational_readiness", tags: new[] { "payments", "stripe", "ready", "operational" })
    .AddCheck<PointOfSale.API.HealthChecks.FawryFoundationHealthCheck>("fawry_foundation", tags: new[] { "payments", "fawry", "configuration", "ready" })
    .AddCheck<PointOfSale.API.HealthChecks.FawryOperationalReadinessHealthCheck>("fawry_operational_readiness", tags: new[] { "payments", "fawry", "ready", "operational" })
    .AddCheck<PointOfSale.API.HealthChecks.ManualPaymentFoundationHealthCheck>("manual_payment_foundation", tags: new[] { "payments", "manual-payment", "configuration", "ready" })
    .AddCheck<PointOfSale.API.HealthChecks.ManualPaymentOperationalReadinessHealthCheck>("manual_payment_operational_readiness", tags: new[] { "payments", "manual-payment", "ready", "operational" })
    .AddCheck<PointOfSale.API.HealthChecks.CodReferenceWorkflowFoundationHealthCheck>("cod_reference_workflow_foundation", tags: new[] { "payments", "cod", "configuration", "ready" })
    .AddCheck<PointOfSale.API.HealthChecks.MarketplaceHealthCheck>("Marketplace")
    .AddCheck<PointOfSale.API.HealthChecks.CommerceHubPlatformHealthCheck>("CommerceHubPlatform", tags: new[] { "database", "mongodb", "elasticsearch" })
    .AddCheck<PointOfSale.API.HealthChecks.CommerceHubSearchSyncHealthCheck>("CommerceHubSearchSync", tags: new[] { "mongodb", "elasticsearch" });
builder.Services.AddHostedService<PointOfSale.API.Services.Health.CommerceHubPlatformStartupValidator>();

if (isAuthoritativeDeterministicTestHost)
{
    // Deterministic local runner hosts only need to expose test-auth endpoints that
    // shell out to focused xUnit against real PostgreSQL/Mongo/Redis authority.
    // Stripping hosted services keeps unrelated reconciliation, queue, indexing,
    // cache warmup, and transport startup from blocking the lightweight API surface.
    builder.Services.RemoveAll<IHostedService>();
}

// =============================================================================
// SIGNALR CONFIGURATION (P1-7: Redis Backplane)
// 
// In multi-instance deployments, SignalR needs a backplane to fan out
// messages to all connected servers. Without this, clients connected
// to instance A won't receive messages published from instance B.
// 
// Uses the same Redis connection as the cache layer.
// Falls back to in-process (no backplane) if Redis is not configured.
// =============================================================================
var signalRRedisConnectionString = builder.Configuration.GetSection("Redis")?.GetValue<string>("ConnectionString");
if (!string.IsNullOrWhiteSpace(signalRRedisConnectionString))
{
    builder.Services.AddSignalR()
        .AddStackExchangeRedis(signalRRedisConnectionString, options =>
        {
            options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("PointOfSale");
        });
}
else
{
    // Local development / single-instance: no backplane needed
    builder.Services.AddSignalR();
}

var app = builder.Build();

// Configure forwarded headers FIRST - must run before any middleware that needs client IP
app.UseForwardedHeaders();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    // Enable Swagger for API documentation
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "POS API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseMiddleware<PointOfSale.API.Middleware.CorrelationIdMiddleware>();
app.UseMiddleware<PointOfSale.API.Middleware.StorefrontHttpCachePolicyMiddleware>();
app.UseMiddleware<PointOfSale.API.SuperAdmin.Infrastructure.SuperAdminRequestLoggingMiddleware>();

app.UseWhen(
    context => !IsHealthEndpoint(context.Request.Path),
    branch => branch.UseHttpsRedirection());

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";

        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; object-src 'none'";
        }

        return Task.CompletedTask;
    });

    await next();
});

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/uploads/chat", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next();
});

app.UseStaticFiles();

app.UseRouting();

app.UseCors("AllowFrontend");
app.UseWhen(
    context => !IsTestAuthEndpoint(context.Request.Path) && !IsHealthEndpoint(context.Request.Path),
    branch => branch.UseMiddleware<PointOfSale.API.Middleware.CookieCsrfProtectionMiddleware>());

app.UseAuthentication();

// =============================================================================
// THEME PREVIEW AUTHORITY
// Validates signed theme-preview capability tokens before normal tenant
// resolution. It establishes tenant context only for constrained, read-only
// storefront preview requests and never trusts tenant query/header input.
// =============================================================================
app.UseMiddleware<PointOfSale.API.Middleware.ThemePreviewAuthorityMiddleware>();

// =============================================================================
// TENANT RESOLUTION MIDDLEWARE
// Must run after Authentication and before Authorization.
// Resolves tenant from authenticated claims and verified host mapping, strips any
// inbound X-Tenant-Id header, and only re-populates the header with the verified
// tenant ID for backward compatibility.
// =============================================================================
app.UseWhen(
    context => !IsTestAuthEndpoint(context.Request.Path) && !IsHealthEndpoint(context.Request.Path),
    branch => branch.UseMiddleware<PointOfSale.API.Middleware.TenantResolutionMiddleware>());
app.UseWhen(
    context => !IsTestAuthEndpoint(context.Request.Path) && !IsHealthEndpoint(context.Request.Path),
    branch => branch.UseMiddleware<PointOfSale.API.Middleware.RequestContextLoggingMiddleware>());
app.UseMiddleware<PointOfSale.API.Middleware.GlobalExceptionMiddleware>();
app.UseMiddleware<PointOfSale.API.SuperAdmin.Infrastructure.SuperAdminExceptionHandlingMiddleware>();

// =============================================================================
// STOREFRONT JWT BLACKLIST MIDDLEWARE (Phase 10.3.x - SECURITY CRITICAL)
// 
// Checks Storefront access-token revocation by JWT ID (jti) on every request.
// Uses Redis cache with PostgreSQL fallback and denies access on uncertainty.
// Must run AFTER UseAuthentication() and BEFORE UseAuthorization().
// 
// SECURITY: Rejected tokens return 401 immediately.
// =============================================================================
app.UseWhen(
    context => !IsTestAuthEndpoint(context.Request.Path) && !IsHealthEndpoint(context.Request.Path),
    branch => branch.UseMiddleware<PointOfSale.API.Middleware.StorefrontJwtBlacklistMiddleware>());

app.UseAuthorization();
app.UseMiddleware<PointOfSale.API.SuperAdmin.Infrastructure.SuperAdminPlatformAuthorityMiddleware>();
app.UseMiddleware<PointOfSale.API.SuperAdmin.Infrastructure.SuperAdminOperationalAuditMiddleware>();
app.UseWhen(
    context => !IsTestAuthEndpoint(context.Request.Path) && !IsHealthEndpoint(context.Request.Path),
    branch => branch.UseMiddleware<PointOfSale.API.Middleware.AdminTenantAuthorityMiddleware>());
app.UseWhen(
    context => !IsTestAuthEndpoint(context.Request.Path) && !IsHealthEndpoint(context.Request.Path),
    branch => branch.UseMiddleware<PointOfSale.API.Middleware.StorefrontPublicationGuardMiddleware>());

// =============================================================================
// AFFILIATE REFERRAL TRACKING MIDDLEWARE (AF-8)
// Intercepts ?ref=CODE on storefront requests, publishes click events.
// Must run after UseAuthorization() and TenantResolution.
// Non-blocking: errors are silently swallowed.
// =============================================================================
app.UseWhen(
    context => !IsTestAuthEndpoint(context.Request.Path) && !IsHealthEndpoint(context.Request.Path),
    branch => branch.UseMiddleware<PointOfSale.API.Middleware.AffiliateReferralMiddleware>());

// =============================================================================
// RATE LIMIT SUBJECT RESOLUTION MIDDLEWARE
// Resolves the trusted rate limit subject once per request and caches it in HttpContext.Items.
// This prevents sync-over-async blocking in rate limit partition factories.
// Must run AFTER UseAuthentication() and BEFORE UseRateLimiter().
// =============================================================================
app.UseWhen(
    context => !IsTestAuthEndpoint(context.Request.Path) && !IsHealthEndpoint(context.Request.Path),
    branch => branch.UseMiddleware<PointOfSale.API.Middleware.RateLimitSubjectResolutionMiddleware>());

// =============================================================================
// RATE LIMITING MIDDLEWARE (Phase C5 - Coupon Abuse Protection)
// Must run AFTER UseAuthorization() to access user identity if needed.
// Specific endpoints use [EnableRateLimiting("CouponRateLimit")] attribute.
// =============================================================================
app.UseWhen(
    context => !IsTestAuthEndpoint(context.Request.Path) && !IsHealthEndpoint(context.Request.Path),
    branch =>
    {
        branch.Use(async (context, next) =>
        {
            PointOfSale.API.Security.StorefrontRateLimitSession.EnsureServerIssuedSessionCookie(
                context,
                app.Environment.IsDevelopment());
            await next();
        });

        branch.UseRateLimiter();
    });

app.MapControllers();
app.MapHub<PointOfSale.API.Hubs.ChatHub>("/hubs/chat");

// =============================================================================
// HEALTH CHECK ENDPOINTS (Phase 12 - Production Observability)
// /health - Full health status with all checks
// /health/live - Liveness probe (is the app running?)
// /health/ready - Readiness probe (is the app ready to receive traffic?)
// =============================================================================
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = PointOfSale.API.HealthChecks.HealthCheckJsonResponseWriter.WriteAsync
}).AllowAnonymous();
app.MapHealthChecks("/health/marketplace", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Name == "Marketplace" || check.Name == "CommerceHubPlatform",
    ResponseWriter = PointOfSale.API.HealthChecks.HealthCheckJsonResponseWriter.WriteAsync
}).AllowAnonymous();
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false, // Liveness just checks if app is running
    ResponseWriter = PointOfSale.API.HealthChecks.HealthCheckJsonResponseWriter.WriteAsync
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = PointOfSale.API.HealthChecks.HealthCheckJsonResponseWriter.WriteAsync
}).AllowAnonymous();

// =============================================================================
// PROMETHEUS METRICS ENDPOINT (OpenTelemetry Export)
// Exposes /metrics endpoint for Prometheus scraping.
// SECURITY: Never allow anonymous public scraping. Requests must either:
//   1. Present a valid API key, or
//   2. Originate from a private/internal network address.
// =============================================================================
var prometheusApiKey = builder.Configuration["PROMETHEUS_API_KEY"];
app.MapPrometheusScrapingEndpoint()
    .WithMetadata(new Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute())
    .AddEndpointFilter(async (context, next) =>
{
    var httpContext = context.HttpContext;
    if (PointOfSale.API.Security.MetricsEndpointAccessPolicy.IsAuthorized(httpContext, prometheusApiKey))
    {
        return await next(context);
    }

    var traceId = System.Diagnostics.Activity.Current?.Id ?? httpContext.TraceIdentifier;
    httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
    await httpContext.Response.WriteAsJsonAsync(
        new PointOfSale.API.DTOs.Storefront.ApiErrorResponse(
            "UNAUTHORIZED",
            "Metrics endpoint is not publicly accessible.",
            traceId),
        httpContext.RequestAborted);
    return null;
});

// Hangfire Dashboard — always register; the dashboard middleware gracefully handles
// storage unavailability. Recurring jobs are registered by HangfireInitializationHostedService
// which retries with exponential backoff if PostgreSQL is temporarily down.
if (!isAuthoritativeDeterministicTestHost && !string.IsNullOrWhiteSpace(defaultConnectionString))
{
    app.UseHangfireDashboard(hangfireDashboardPath);
}

// =============================================================================
// SHIPPING TRACKING SYNC JOB (P2 Hardening)
// Syncs shipment tracking status from carriers every 30 minutes.
// Only processes non-terminal shipments with tracking numbers.
// =============================================================================
// Recurring jobs are now registered by HangfireInitializationHostedService with retry logic.
// See: PointOfSale.API/Jobs/HangfireInitializationHostedService.cs

// =============================================================================
// MANUAL WALLET PROOF CLEANUP JOB (P3.9 Hardening)
// Runs every 6 hours to clean up expired proof images for compliance/disk optimization.
// =============================================================================

// =============================================================================
// REFUND EVIDENCE CLEANUP JOB (Refund Evidence Hardening)
// Deletes abandoned temporary refund evidence uploads after their staging lease expires.
// Runs hourly so unused R2 objects do not accumulate.
// =============================================================================

// =============================================================================
// WHATSAPP BACKGROUND JOBS (Phase 13)
// =============================================================================
// Session Heartbeat: Every 5 minutes
// TRANSPORT-AWARE: Routes to Playwright or Baileys based on tenant's UseBaileys flag
// - Playwright tenants: Uses browser-based session management (legacy)
// - Baileys tenants: Uses lightweight HTTP-based session management (new)

// =============================================================================
// ABANDONED CART RECOVERY SCANNER (Smart Cart Recovery Engine)
// Scans for abandoned carts and triggers recovery workflows.
// Runs every 5 minutes to detect carts idle beyond FirstMessageDelayMinutes.
// =============================================================================

// =============================================================================
// ABANDONED CHECKOUT SESSION SCANNER (Phase: Lost Orders)
// Scans for idle checkout sessions and marks them as abandoned.
// Runs every 5 minutes. Threshold: 30 minutes of inactivity.
// =============================================================================

// =============================================================================
// P2-13: PROCESSED TRUST EVENTS CLEANUP (Services Marketplace Hardening)
// Deletes idempotency records older than 30 days to prevent unbounded table growth.
// Runs daily at 3 AM.
// =============================================================================

// =============================================================================
// COMMERCEHUB CART CLEANUP (Cart Persistence Hardening)
// Removes carts idle for 30 days to keep the persisted cart store bounded.
// Runs every 6 hours.
// =============================================================================

// =============================================================================
// MARKETPLACE OPERATIONAL JOBS (Phase CR-2)
// =============================================================================
// Trust Reconciliation: Hourly

app.Run();




