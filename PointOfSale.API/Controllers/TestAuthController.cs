using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PointOfSale.API.Attributes;
using PointOfSale.API.Models;
using PointOfSale.API.Security;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Security;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Controllers;

/// <summary>
/// TESTING/DEVELOPMENT ENVIRONMENT ONLY — Deterministic JWT issuance for reliability test harnesses.
///
/// This controller is stripped from Production by TestingOnlyControllerFeatureProvider.
///
/// SECURITY PROPERTIES:
///   - Never present in Production (filtered at MVC feature level — Production is not Testing/Development).
///   - Requires no existing authentication to call (test harness entry point).
///   - Issues tokens that are accepted by the existing POS JWT Bearer pipeline
///     using the same JwtSettings:Secret / Issuer / Audience as the real auth system.
///   - Tokens carry real, DB-verified tenant context to satisfy TenantResolutionMiddleware.
///   - Tokens are SHORT-LIVED (1 hour) to prevent accidental cross-environment reuse.
///   - If tenantId is provided it must exist and be active in the DB.
///   - If tenantId is omitted the first active tenant is used (useful for single-tenant dev stacks).
///
/// USAGE (TestSprite / harnesses):
///   POST /api/test-auth/token
///   Body: { } OR { "tenantId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" }
///   Response: { "token": "<bearer jwt>", "expiresAt": "<iso8601>", "tenantId": "<used tenant id>" }
///   Use: Authorization: Bearer <token>
/// </summary>
[TestingOnlyController]
[AllowAnonymous]
[AllowAnonymousTenant]
[ApiController]
[Route("api/test-auth")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class TestAuthController : ControllerBase
{
    // -------------------------------------------------------------------------
    // Deterministic test user identity — fixed across all test runs.
    // -------------------------------------------------------------------------
    private const string TestUserId = "00000000-0000-0000-0000-000000000901";
    private const string TestUsername = "test-reliability-agent-01";
    private const string TestUserRole = "Admin";
    private const string SiCon01FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.SalesInvoiceConcurrencyIntegrationTests.SI_CON_01_Concurrent_Sales_On_Contested_Stock_Converge_Safely_And_Are_Replay_Safe";
    private const string SiCon02FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.SalesInvoiceConcurrencyIntegrationTests.SI_CON_02_Replay_During_Active_Transaction_Converges_Safely";
    private const string SiCon03FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.SalesInvoiceConcurrencyIntegrationTests.SI_CON_03_Duplicate_Worker_Delivery_During_Active_Processing_Converges_Safely";
    private const string PiCon01FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.PurchaseInvoiceConcurrencyIntegrationTests.PI_CON_01_Concurrent_Purchase_Creation_Against_Same_Variant_Converges_Safely_And_Is_Replay_Safe";
    private const string PiCon02FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.PurchaseInvoiceConcurrencyIntegrationTests.PI_CON_02_Replay_During_Active_Purchase_Transaction_Converges_Safely";
    private const string PiCon03FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.PurchaseInvoiceConcurrencyIntegrationTests.PI_CON_03_Duplicate_Purchase_Worker_Delivery_During_Active_Processing_Converges_Safely";
    private const string SrCon01FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.SalesReturnDistributedReliabilityIntegrationTests.SR_CON_01_Concurrent_Sales_Return_Against_Same_Invoice_Converges_Safely";
    private const string SrCon02FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.SalesReturnDistributedReliabilityIntegrationTests.SR_CON_02_Replay_During_Active_Sales_Return_Transaction_Converges_Safely";
    private const string SrCon03FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.SalesReturnDistributedReliabilityIntegrationTests.SR_CON_03_Duplicate_Sales_Return_Worker_Delivery_During_Active_Processing_Converges_Safely";
    private const string PrCon01FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.PurchaseReturnDistributedReliabilityIntegrationTests.PR_CON_01_Concurrent_Purchase_Return_Against_Same_Purchase_Invoice_Converges_Safely";
    private const string PrCon02FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.PurchaseReturnDistributedReliabilityIntegrationTests.PR_CON_02_Replay_During_Active_Purchase_Return_Transaction_Converges_Safely";
    private const string PrCon03FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.PurchaseReturnDistributedReliabilityIntegrationTests.PR_CON_03_Duplicate_Purchase_Return_Worker_Delivery_During_Active_Processing_Converges_Safely";
    private const string ProdCon01FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.ProductVariantDistributedReliabilityIntegrationTests.PROD_CON_01_Concurrent_Product_Variant_Creation_With_Same_SKU_Converges_Safely";
    private const string ProdCon02FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.ProductVariantDistributedReliabilityIntegrationTests.PROD_CON_02_Replay_During_Active_Product_Variant_Creation_Converges_Safely";
    private const string ProdCon03FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.ProductVariantDistributedReliabilityIntegrationTests.PROD_CON_03_Duplicate_Product_Sync_Worker_Delivery_During_Active_Processing_Converges_Safely";
    private const string VarCon01FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.ProductVariantDistributedReliabilityIntegrationTests.VAR_CON_01_Concurrent_Same_Attribute_Combination_Creation_Converges_Safely";
    private const string VarCon02FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.ProductVariantDistributedReliabilityIntegrationTests.VAR_CON_02_Replay_During_Active_Variant_Attribute_Mutation_Converges_Safely";
    private const string VarCon03FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.ProductVariantDistributedReliabilityIntegrationTests.VAR_CON_03_Duplicate_Variant_Projection_Delivery_During_Active_Processing_Converges_Safely";
    private const string VarCon04FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.ProductVariantDistributedReliabilityIntegrationTests.VAR_CON_04_Concurrent_SKU_Reassignment_Race_Converges_Safely";
    private const string VarCon05FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.ProductVariantDistributedReliabilityIntegrationTests.VAR_CON_05_Variant_Delete_During_Active_Inventory_Mutation_Converges_Safely";
    private const string VarCon06FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.ProductVariantDistributedReliabilityIntegrationTests.VAR_CON_06_Out_Of_Order_Variant_Projection_Propagation_Rejects_Stale_Replay";
    private const string VarCon07FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.ProductVariantDistributedReliabilityIntegrationTests.VAR_CON_07_Stale_Marketplace_Variant_Replay_Fails_Closed_After_Newer_SQL_Truth_Propagates";
    private const string VarCon08FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.ProductVariantDistributedReliabilityIntegrationTests.VAR_CON_08_Duplicate_Variant_Projection_Delivery_During_Active_Processing_Is_Visibility_Safe";
    private const string VarCon09FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.ProductVariantDistributedReliabilityIntegrationTests.VAR_CON_09_Variant_Media_Synchronization_Corruption_Converges_From_SQL_Truth";
    private const string PriceCon01FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.ProductVariantPriceDistributedReliabilityIntegrationTests.PRICE_CON_01_Concurrent_Sell_Price_Update_Against_Same_Variant_Converges_Safely";
    private const string PriceCon02FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.ProductVariantPriceDistributedReliabilityIntegrationTests.PRICE_CON_02_Replay_During_Active_Price_Mutation_Converges_Safely";
    private const string PriceCon03FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.ProductVariantPriceDistributedReliabilityIntegrationTests.PRICE_CON_03_Duplicate_Pricing_Worker_Delivery_During_Active_Processing_Converges_Safely";
    private const string PriceCon04FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.ProductVariantPriceDistributedReliabilityIntegrationTests.PRICE_CON_04_Out_Of_Order_Marketplace_Price_Propagation_Rejected_By_Version_Aware_Downstream_Consumers";
    private const string PriceCon05FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.ProductVariantPriceDistributedReliabilityIntegrationTests.PRICE_CON_05_Stale_Affiliate_Pricing_Replay_Rejected_After_Newer_Pricing_Truth_Propagates";
    private const string PriceCon06FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.ProductVariantPriceDistributedReliabilityIntegrationTests.PRICE_CON_06_Duplicate_Marketplace_Price_Publish_Converges_To_One_Authoritative_Multi_Channel_Pricing_Truth";
    private const string PriceCon07FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.ProductVariantPriceDistributedReliabilityIntegrationTests.PRICE_CON_07_Partial_Multi_Channel_Pricing_Sync_Corruption_Converges_To_One_Authoritative_Pricing_Truth";
    private const string PriceCon08FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.ProductVariantPriceDistributedReliabilityIntegrationTests.PRICE_CON_08_Concurrent_Promotion_Activation_Against_Same_Variant_Converges_To_One_Deterministic_Overlay";
    private const string PriceCon09FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.ProductVariantPriceDistributedReliabilityIntegrationTests.PRICE_CON_09_Promotion_Expiration_Replay_Overlap_Preserves_Authoritative_Expired_State";
    private const string PriceCon10FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.ProductVariantPriceDistributedReliabilityIntegrationTests.PRICE_CON_10_Duplicate_Promotion_Sync_Worker_Delivery_Converges_Safely";
    private const string PriceCon11FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.SalesInvoiceConcurrencyIntegrationTests.PRICE_CON_11_Concurrent_Cost_Price_Mutation_During_Sales_Activity_Preserves_Historical_COGS_And_Replay_Safety";
    private const string PriceCon12FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.SalesInvoiceConcurrencyIntegrationTests.PRICE_CON_12_Replay_During_Cost_Basis_Recalculation_Converges_Safely";
    private const string PriceCon13FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.SalesInvoiceConcurrencyIntegrationTests.PRICE_CON_13_Duplicate_Financial_Pricing_Projection_Delivery_Converges_Safely";
    private const string StockCon01FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.SalesInvoiceConcurrencyIntegrationTests.STOCK_CON_01_Concurrent_Last_Unit_Stock_Deduction_Converges_Safely";
    private const string StockCon02FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.SalesInvoiceConcurrencyIntegrationTests.STOCK_CON_02_Replay_During_Active_Stock_Mutation_Converges_Safely";
    private const string StockCon03FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.SalesInvoiceConcurrencyIntegrationTests.STOCK_CON_03_Duplicate_Inventory_Worker_Delivery_During_Active_Processing_Converges_Safely";
    private const string StockCon04FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.SalesInvoiceConcurrencyIntegrationTests.STOCK_CON_04_Replay_During_Sales_Return_Stock_Restoration_Converges_Safely";
    private const string StockCon05FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.SalesInvoiceConcurrencyIntegrationTests.STOCK_CON_05_Duplicate_CommerceHub_Return_Delivery_Converges_Safely";
    private const string StockCon06FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.PurchaseReturnDistributedReliabilityIntegrationTests.STOCK_CON_06_Replay_During_Purchase_Return_Stock_Deduction_Converges_Safely";
    private const string StockCon07FocusedTestFilter =
        "FullyQualifiedName=PointOfSale.Tests.Integration.SalesInvoiceConcurrencyIntegrationTests.STOCK_CON_07_Concurrent_Sales_Return_Against_Same_Historical_Sale_Quantity_Converges_Safely";
    private const int DefaultSiCon01Repetitions = 3;
    private const int DefaultSiCon02Repetitions = 1;
    private const int DefaultSiCon03Repetitions = 1;
    private const int DefaultPiCon01Repetitions = 1;
    private const int DefaultPiCon02Repetitions = 1;
    private const int DefaultPiCon03Repetitions = 1;
    private const int DefaultSrCon01Repetitions = 1;
    private const int DefaultSrCon02Repetitions = 1;
    private const int DefaultSrCon03Repetitions = 1;
    private const int DefaultPrCon01Repetitions = 1;
    private const int DefaultPrCon02Repetitions = 1;
    private const int DefaultPrCon03Repetitions = 1;
    private const int DefaultProdCon01Repetitions = 1;
    private const int DefaultProdCon02Repetitions = 1;
    private const int DefaultProdCon03Repetitions = 1;
    private const int DefaultVarCon01Repetitions = 1;
    private const int DefaultVarCon02Repetitions = 1;
    private const int DefaultVarCon03Repetitions = 1;
    private const int DefaultVarCon04Repetitions = 1;
    private const int DefaultVarCon05Repetitions = 1;
    private const int DefaultVarCon06Repetitions = 1;
    private const int DefaultVarCon07Repetitions = 1;
    private const int DefaultVarCon08Repetitions = 1;
    private const int DefaultVarCon09Repetitions = 1;
    private const int DefaultPriceCon01Repetitions = 1;
    private const int DefaultPriceCon02Repetitions = 1;
    private const int DefaultPriceCon03Repetitions = 1;
    private const int DefaultPriceCon04Repetitions = 1;
    private const int DefaultPriceCon05Repetitions = 1;
    private const int DefaultPriceCon06Repetitions = 1;
    private const int DefaultPriceCon07Repetitions = 1;
    private const int DefaultPriceCon08Repetitions = 1;
    private const int DefaultPriceCon09Repetitions = 1;
    private const int DefaultPriceCon10Repetitions = 1;
    private const int DefaultPriceCon11Repetitions = 1;
    private const int DefaultPriceCon12Repetitions = 1;
    private const int DefaultPriceCon13Repetitions = 1;
    private const int DefaultStockCon01Repetitions = 1;
    private const int DefaultStockCon02Repetitions = 1;
    private const int DefaultStockCon03Repetitions = 1;
    private const int DefaultStockCon04Repetitions = 1;
    private const int DefaultStockCon05Repetitions = 1;
    private const int DefaultStockCon06Repetitions = 1;
    private const int DefaultStockCon07Repetitions = 1;
    private static readonly TimeSpan FocusedTestIterationTimeout = TimeSpan.FromMinutes(4);
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> SiCon01Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> SiCon02Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> SiCon03Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> PiCon01Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> PiCon02Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> PiCon03Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> SrCon01Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> SrCon02Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> SrCon03Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> PrCon01Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> PrCon02Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> PrCon03Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> ProdCon01Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> ProdCon02Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> ProdCon03Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> VarCon01Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> VarCon02Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> VarCon03Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> VarCon04Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> VarCon05Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> VarCon06Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> VarCon07Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> VarCon08Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> VarCon09Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> PriceCon01Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> PriceCon02Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> PriceCon03Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> PriceCon04Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> PriceCon05Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> PriceCon06Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> PriceCon07Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> PriceCon08Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> PriceCon09Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> PriceCon10Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> PriceCon11Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> PriceCon12Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> PriceCon13Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> StockCon01Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> StockCon02Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> StockCon03Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> StockCon04Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> StockCon05Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> StockCon06Jobs = new();
    private static readonly ConcurrentDictionary<Guid, SiCon01RunJob> StockCon07Jobs = new();
    private static readonly SemaphoreSlim SiCon01RunGate = new(1, 1);
    private static readonly SemaphoreSlim SiCon02RunGate = new(1, 1);
    private static readonly SemaphoreSlim SiCon03RunGate = new(1, 1);
    private static readonly SemaphoreSlim PiCon01RunGate = new(1, 1);
    private static readonly SemaphoreSlim PiCon02RunGate = new(1, 1);
    private static readonly SemaphoreSlim PiCon03RunGate = new(1, 1);
    private static readonly SemaphoreSlim SrCon01RunGate = new(1, 1);
    private static readonly SemaphoreSlim SrCon02RunGate = new(1, 1);
    private static readonly SemaphoreSlim SrCon03RunGate = new(1, 1);
    private static readonly SemaphoreSlim PrCon01RunGate = new(1, 1);
    private static readonly SemaphoreSlim PrCon02RunGate = new(1, 1);
    private static readonly SemaphoreSlim PrCon03RunGate = new(1, 1);
    private static readonly SemaphoreSlim ProdCon01RunGate = new(1, 1);
    private static readonly SemaphoreSlim ProdCon02RunGate = new(1, 1);
    private static readonly SemaphoreSlim ProdCon03RunGate = new(1, 1);
    private static readonly SemaphoreSlim VarCon01RunGate = new(1, 1);
    private static readonly SemaphoreSlim VarCon02RunGate = new(1, 1);
    private static readonly SemaphoreSlim VarCon03RunGate = new(1, 1);
    private static readonly SemaphoreSlim VarCon04RunGate = new(1, 1);
    private static readonly SemaphoreSlim VarCon05RunGate = new(1, 1);
    private static readonly SemaphoreSlim VarCon06RunGate = new(1, 1);
    private static readonly SemaphoreSlim VarCon07RunGate = new(1, 1);
    private static readonly SemaphoreSlim VarCon08RunGate = new(1, 1);
    private static readonly SemaphoreSlim VarCon09RunGate = new(1, 1);
    private static readonly SemaphoreSlim PriceCon01RunGate = new(1, 1);
    private static readonly SemaphoreSlim PriceCon02RunGate = new(1, 1);
    private static readonly SemaphoreSlim PriceCon03RunGate = new(1, 1);
    private static readonly SemaphoreSlim PriceCon04RunGate = new(1, 1);
    private static readonly SemaphoreSlim PriceCon05RunGate = new(1, 1);
    private static readonly SemaphoreSlim PriceCon06RunGate = new(1, 1);
    private static readonly SemaphoreSlim PriceCon07RunGate = new(1, 1);
    private static readonly SemaphoreSlim PriceCon08RunGate = new(1, 1);
    private static readonly SemaphoreSlim PriceCon09RunGate = new(1, 1);
    private static readonly SemaphoreSlim PriceCon10RunGate = new(1, 1);
    private static readonly SemaphoreSlim PriceCon11RunGate = new(1, 1);
    private static readonly SemaphoreSlim PriceCon12RunGate = new(1, 1);
    private static readonly SemaphoreSlim PriceCon13RunGate = new(1, 1);
    private static readonly SemaphoreSlim StockCon01RunGate = new(1, 1);
    private static readonly SemaphoreSlim StockCon02RunGate = new(1, 1);
    private static readonly SemaphoreSlim StockCon03RunGate = new(1, 1);
    private static readonly SemaphoreSlim StockCon04RunGate = new(1, 1);
    private static readonly SemaphoreSlim StockCon05RunGate = new(1, 1);
    private static readonly SemaphoreSlim StockCon06RunGate = new(1, 1);
    private static readonly SemaphoreSlim StockCon07RunGate = new(1, 1);

    private readonly JwtSettings _jwtSettings;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<TestAuthController> _logger;
    private readonly ApplicationDbContext _dbContext;
    private readonly IAccountingOutboxProcessor _outboxProcessor;

    public TestAuthController(
        IOptions<JwtSettings> jwtSettings,
        IWebHostEnvironment env,
        ILogger<TestAuthController> logger,
        ApplicationDbContext dbContext,
        IAccountingOutboxProcessor outboxProcessor)
    {
        _jwtSettings = jwtSettings.Value;
        _env = env;
        _logger = logger;
        _dbContext = dbContext;
        _outboxProcessor = outboxProcessor;
    }

    private bool IsTestHarnessEnvironment()
    {
        return _env.IsDevelopment()
            || _env.IsEnvironment("Testing")
            || _env.IsEnvironment("Test");
    }

    /// <summary>
    /// Issues a POS JWT bearer token for a DB-verified tenant.
    /// Only available when ASPNETCORE_ENVIRONMENT=Testing or Development.
    /// </summary>
    [HttpPost("token")]
    public async Task<IActionResult> IssueTestToken(
        [FromBody] TestTokenRequest? request = null,
        CancellationToken ct = default)
    {
        // Defence-in-depth: return 404 if somehow reached outside Testing or Development.
        if (!IsTestHarnessEnvironment())
        {
            _logger.LogCritical(
                "TestAuthController.IssueTestToken was reached outside Testing/Development. " +
                "Environment={Environment}. Security misconfiguration.",
                _env.EnvironmentName);
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(_jwtSettings.Secret))
        {
            return Problem(
                detail: "JwtSettings:Secret is not configured. Set it in appsettings.Development.json or user-secrets.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "JWT secret not configured");
        }

        // Resolve tenant from DB — must be real and active so TenantResolutionMiddleware accepts the token.
        var requestedId = request?.TenantId;
        var tenant = requestedId.HasValue && requestedId.Value != Guid.Empty
            ? await _dbContext.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == requestedId.Value && t.IsActive, ct)
            : await _dbContext.Tenants
                .AsNoTracking()
                .OrderBy(t => t.CreatedAtUtc)
                .FirstOrDefaultAsync(t => t.IsActive, ct);

        if (tenant == null)
        {
            var hint = requestedId.HasValue
                ? $"Tenant {requestedId.Value} not found or inactive."
                : "No active tenant exists in the database.";

            _logger.LogWarning("TestAuthController: tenant resolution failed. {Hint}", hint);

            return BadRequest(new
            {
                error = "Tenant not found",
                code = "TENANT_NOT_FOUND",
                hint
            });
        }

        var sessionBinding = RefreshTokenHasher.Hash(RefreshTokenGenerator.Generate());
        var testUserId = Guid.Parse(TestUserId);
        var testUser = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(user => user.Id == testUserId, ct);

        if (testUser == null)
        {
            testUser = new User
            {
                Id = testUserId,
                TenantId = tenant.Id,
                Username = TestUsername,
                Email = "test-reliability-agent@testing.invalid",
                PasswordHash = "testing-only-token-issuer",
                FirstName = "Reliability",
                LastName = "Agent",
                Role = UserRole.Admin,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.Users.Add(testUser);
        }
        else if (testUser.TenantId != tenant.Id || !testUser.IsActive || testUser.IsDeleted)
        {
            var tenantScopedUsername = $"{TestUsername}-{tenant.Id:N}";
            testUser = await _dbContext.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    user => user.TenantId == tenant.Id && user.Username == tenantScopedUsername,
                    ct);

            if (testUser == null)
            {
                testUser = new User
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenant.Id,
                    Username = tenantScopedUsername,
                    Email = $"test-reliability-agent-{tenant.Id:N}@testing.invalid",
                    PasswordHash = "testing-only-token-issuer",
                    FirstName = "Reliability",
                    LastName = "Agent",
                    Role = UserRole.Admin,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                _dbContext.Users.Add(testUser);
            }
            else if (!testUser.IsActive || testUser.IsDeleted)
            {
                return Conflict(new
                {
                    error = "The tenant-scoped test authority user is inactive.",
                    code = "TEST_ACTOR_SCOPE_CONFLICT"
                });
            }
        }

        testUser.RefreshToken = sessionBinding;
        testUser.RefreshTokenExpiryTime = DateTime.UtcNow.AddHours(1);
        await _dbContext.SaveChangesAsync(ct);

        var token = BuildToken(tenant.Id, testUser.Id, sessionBinding);

        _logger.LogInformation(
            "TestAuthController issued test JWT | TenantId={TenantId} TenantName={TenantName} UserId={UserId}",
            tenant.Id,
            tenant.Name,
            testUser.Id);

        return Ok(new TestTokenResponse(
            Token: token.Token,
            ExpiresAt: token.ExpiresAt,
            TenantId: tenant.Id,
            TenantName: tenant.Name,
            UserId: testUser.Id.ToString(),
            Role: TestUserRole));
    }

    /// <summary>
    /// Idempotently ensures that all schema prerequisites for the SI reliability test suite are met.
    /// Specifically, adds the unique index on SalesRequestIdempotency(TenantId, Key, Operation)
    /// if it does not already exist — required for the ON CONFLICT ... DO NOTHING upsert in CreateSale.
    /// </summary>
    [HttpPost("ensure-schema")]
    public async Task<IActionResult> EnsureSchema(CancellationToken ct = default)
    {
        if (!IsTestHarnessEnvironment())
        {
            return NotFound();
        }

        var results = new List<string>();

        // === Step 1: Apply ALL pending EF Core migrations atomically ===
        try
        {
            await _dbContext.Database.MigrateAsync(ct);
            results.Add("EF migrations: ALL PENDING APPLIED (or already up-to-date)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TestAuthController.EnsureSchema: EF MigrateAsync failed");
            results.Add($"EF migrations: FAILED — {ex.Message}");
            // Do NOT return early — try the idempotent column fixes below anyway.
        }

        // === Step 2: Belt-and-suspenders idempotent schema fixes ===
        // These apply only if the EF migration for some reason didn't cover them.

        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE ""Promotions""
                    ADD COLUMN IF NOT EXISTS ""ProjectionMutationVersion"" bigint NOT NULL DEFAULT 0;", ct);
            results.Add("Promotions.ProjectionMutationVersion: OK");
        }
        catch (Exception ex) { results.Add($"Promotions.ProjectionMutationVersion: FAILED - {ex.Message}"); }

        // Unique index on SalesRequestIdempotency (TenantId, Key, Operation)
        // CREATE UNIQUE INDEX IF NOT EXISTS is idempotent and doesn't require a pg_indexes lookup.
        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(@"
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SalesRequestIdempotency_TenantId_Key_Operation""
                    ON ""SalesRequestIdempotency"" (""TenantId"", ""Key"", ""Operation"");", ct);
            results.Add("IX_SalesRequestIdempotency_TenantId_Key_Operation: OK");
        }
        catch (Exception ex)
        {
            results.Add($"IX_SalesRequestIdempotency_TenantId_Key_Operation: FAILED — {ex.Message}");
        }

        // SalesRequestIdempotency.ClaimToken
        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE ""SalesRequestIdempotency""
                    ADD COLUMN IF NOT EXISTS ""ClaimToken"" character varying(64) NULL;", ct);
            results.Add("SalesRequestIdempotency.ClaimToken: OK");
        }
        catch (Exception ex) { results.Add($"SalesRequestIdempotency.ClaimToken: FAILED — {ex.Message}"); }

        // SalesRequestIdempotency.ClaimTimestampUtc
        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE ""SalesRequestIdempotency""
                    ADD COLUMN IF NOT EXISTS ""ClaimTimestampUtc"" timestamp with time zone NULL;", ct);
            results.Add("SalesRequestIdempotency.ClaimTimestampUtc: OK");
        }
        catch (Exception ex) { results.Add($"SalesRequestIdempotency.ClaimTimestampUtc: FAILED — {ex.Message}"); }

        // SalesInvoices.AccountingMutationVersion (from S28 migration)
        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE ""SalesInvoices""
                    ADD COLUMN IF NOT EXISTS ""AccountingMutationVersion"" integer NOT NULL DEFAULT 0;", ct);
            results.Add("SalesInvoices.AccountingMutationVersion: OK");
        }
        catch (Exception ex) { results.Add($"SalesInvoices.AccountingMutationVersion: FAILED — {ex.Message}"); }

        // PurchaseRequestIdempotency unique index
        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(@"
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PurchaseRequestIdempotency_TenantId_Key_Operation""
                    ON ""PurchaseRequestIdempotency"" (""TenantId"", ""Key"", ""Operation"");", ct);
            results.Add("IX_PurchaseRequestIdempotency_TenantId_Key_Operation: OK");
        }
        catch (Exception ex)
        {
            results.Add($"IX_PurchaseRequestIdempotency_TenantId_Key_Operation: FAILED â€” {ex.Message}");
        }

        // PurchaseInvoices.AccountingMutationVersion
        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE ""PurchaseInvoices""
                    ADD COLUMN IF NOT EXISTS ""AccountingMutationVersion"" integer NOT NULL DEFAULT 0;", ct);
            results.Add("PurchaseInvoices.AccountingMutationVersion: OK");
        }
        catch (Exception ex) { results.Add($"PurchaseInvoices.AccountingMutationVersion: FAILED â€” {ex.Message}"); }

        // Purchase invoice sequence
        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(@"
                CREATE SEQUENCE IF NOT EXISTS purchase_invoice_seq
                    START WITH 1000
                    INCREMENT BY 1
                    NO MINVALUE
                    NO MAXVALUE
                    CACHE 1;", ct);
            results.Add("purchase_invoice_seq: OK");
        }
        catch (Exception ex) { results.Add($"purchase_invoice_seq: FAILED â€” {ex.Message}"); }

        // MarketplaceProjectionOutboxMessages table + indexes
        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""MarketplaceProjectionOutboxMessages""
                (
                    ""Id"" uuid NOT NULL,
                    ""EventId"" uuid NOT NULL,
                    ""ConsumerName"" character varying(200) NOT NULL,
                    ""EventType"" character varying(200) NOT NULL,
                    ""ClrType"" character varying(1000) NOT NULL,
                    ""EntityId"" uuid NOT NULL,
                    ""TenantId"" uuid NULL,
                    ""CorrelationId"" character varying(200) NOT NULL,
                    ""EventVersion"" integer NOT NULL,
                    ""OccurredAtUtc"" timestamp with time zone NOT NULL,
                    ""PayloadJson"" text NOT NULL,
                    ""ProcessedAtUtc"" timestamp with time zone NULL,
                    ""ClaimedUntilUtc"" timestamp with time zone NULL,
                    ""AttemptCount"" integer NOT NULL DEFAULT 0,
                    ""LastError"" character varying(2000) NULL,
                    ""NextRetryAtUtc"" timestamp with time zone NULL,
                    ""IsFailed"" boolean NOT NULL DEFAULT false,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""UpdatedAt"" timestamp with time zone NULL,
                    ""CreatedBy"" uuid NULL,
                    ""UpdatedBy"" uuid NULL,
                    ""IsDeleted"" boolean NOT NULL DEFAULT false,
                    CONSTRAINT ""PK_MarketplaceProjectionOutboxMessages"" PRIMARY KEY (""Id"")
                );", ct);
            results.Add("MarketplaceProjectionOutboxMessages table: OK");
        }
        catch (Exception ex) { results.Add($"MarketplaceProjectionOutboxMessages table: FAILED â€” {ex.Message}"); }

        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(@"
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_MarketplaceProjectionOutbox_Idempotency""
                    ON ""MarketplaceProjectionOutboxMessages"" (""EventId"", ""ConsumerName"", ""EventType"")
                    WHERE ""IsDeleted"" = false;", ct);
            results.Add("IX_MarketplaceProjectionOutbox_Idempotency: OK");
        }
        catch (Exception ex) { results.Add($"IX_MarketplaceProjectionOutbox_Idempotency: FAILED â€” {ex.Message}"); }

        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(@"
                CREATE INDEX IF NOT EXISTS ""IX_MarketplaceProjectionOutbox_ProcessedAtUtc""
                    ON ""MarketplaceProjectionOutboxMessages"" (""ProcessedAtUtc"");", ct);
            results.Add("IX_MarketplaceProjectionOutbox_ProcessedAtUtc: OK");
        }
        catch (Exception ex) { results.Add($"IX_MarketplaceProjectionOutbox_ProcessedAtUtc: FAILED â€” {ex.Message}"); }

        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(@"
                CREATE INDEX IF NOT EXISTS ""IX_MarketplaceProjectionOutbox_TenantId_EntityId""
                    ON ""MarketplaceProjectionOutboxMessages"" (""TenantId"", ""EntityId"");", ct);
            results.Add("IX_MarketplaceProjectionOutbox_TenantId_EntityId: OK");
        }
        catch (Exception ex) { results.Add($"IX_MarketplaceProjectionOutbox_TenantId_EntityId: FAILED â€” {ex.Message}"); }

        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE ""MarketplaceProjectionOutboxMessages""
                    ADD COLUMN IF NOT EXISTS ""ClrType"" character varying(1000) NOT NULL DEFAULT '';", ct);
            results.Add("MarketplaceProjectionOutboxMessages.ClrType: OK");
        }
        catch (Exception ex) { results.Add($"MarketplaceProjectionOutboxMessages.ClrType: FAILED - {ex.Message}"); }

        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE ""MarketplaceProjectionOutboxMessages""
                    ADD COLUMN IF NOT EXISTS ""PayloadJson"" text NOT NULL DEFAULT '{}';", ct);
            results.Add("MarketplaceProjectionOutboxMessages.PayloadJson: OK");
        }
        catch (Exception ex) { results.Add($"MarketplaceProjectionOutboxMessages.PayloadJson: FAILED - {ex.Message}"); }

        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE ""MarketplaceProjectionOutboxMessages""
                    ADD COLUMN IF NOT EXISTS ""EventVersion"" integer NOT NULL DEFAULT 1;", ct);
            results.Add("MarketplaceProjectionOutboxMessages.EventVersion: OK");
        }
        catch (Exception ex) { results.Add($"MarketplaceProjectionOutboxMessages.EventVersion: FAILED - {ex.Message}"); }

        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE ""MarketplaceProjectionOutboxMessages""
                    ADD COLUMN IF NOT EXISTS ""ClaimedUntilUtc"" timestamp with time zone NULL;", ct);
            results.Add("MarketplaceProjectionOutboxMessages.ClaimedUntilUtc: OK");
        }
        catch (Exception ex) { results.Add($"MarketplaceProjectionOutboxMessages.ClaimedUntilUtc: FAILED - {ex.Message}"); }

        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(@"
                CREATE INDEX IF NOT EXISTS ""IX_MarketplaceProjectionOutbox_ProcessingStatus""
                    ON ""MarketplaceProjectionOutboxMessages"" (""TenantId"", ""ConsumerName"", ""ProcessedAtUtc"", ""NextRetryAtUtc"", ""ClaimedUntilUtc"")
                    WHERE ""ProcessedAtUtc"" IS NULL AND ""IsFailed"" = false;", ct);
            results.Add("IX_MarketplaceProjectionOutbox_ProcessingStatus: OK");
        }
        catch (Exception ex) { results.Add($"IX_MarketplaceProjectionOutbox_ProcessingStatus: FAILED - {ex.Message}"); }

        // ProductVariants SKU / Barcode identity uniqueness
        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM pg_class index_class
                        INNER JOIN pg_index index_def ON index_def.indexrelid = index_class.oid
                        WHERE index_class.relname = 'IX_ProductVariants_SKU'
                          AND index_def.indisunique = false) THEN
                        DROP INDEX IF EXISTS ""IX_ProductVariants_SKU"";
                    END IF;

                    DROP INDEX IF EXISTS ""IX_ProductVariants_SKU"";
                    CREATE UNIQUE INDEX ""IX_ProductVariants_SKU""
                        ON ""ProductVariants"" (""TenantId"", ""SKU"")
                        WHERE ""IsDeleted"" = false AND ""SemanticLifecycleState"" = 0 AND ""SKU"" IS NOT NULL;

                    IF EXISTS (
                        SELECT 1
                        FROM pg_class index_class
                        INNER JOIN pg_index index_def ON index_def.indexrelid = index_class.oid
                        WHERE index_class.relname = 'IX_ProductVariants_Barcode'
                          AND index_def.indisunique = false) THEN
                        DROP INDEX IF EXISTS ""IX_ProductVariants_Barcode"";
                    END IF;

                    DROP INDEX IF EXISTS ""IX_ProductVariants_Barcode"";
                    CREATE UNIQUE INDEX ""IX_ProductVariants_Barcode""
                        ON ""ProductVariants"" (""TenantId"", ""Barcode"")
                        WHERE ""IsDeleted"" = false AND ""SemanticLifecycleState"" = 0 AND ""Barcode"" IS NOT NULL;
                END
                $$;", ct);
            results.Add("ProductVariants SKU/Barcode unique indexes: OK");
        }
        catch (Exception ex) { results.Add($"ProductVariants SKU/Barcode unique indexes: FAILED Ã¢â‚¬â€ {ex.Message}"); }

        // ProductRequestIdempotency table + indexes
        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""ProductRequestIdempotency""
                (
                    ""Id"" uuid NOT NULL,
                    ""TenantId"" uuid NOT NULL,
                    ""Key"" character varying(200) NOT NULL,
                    ""Operation"" character varying(32) NOT NULL,
                    ""ProductId"" uuid NOT NULL,
                    ""ProductVariantId"" uuid NULL,
                    ""RequestFingerprint"" character varying(64) NOT NULL,
                    ""ResponseStatusCode"" integer NULL,
                    ""ResponseJson"" text NULL,
                    ""CompletedAtUtc"" timestamp with time zone NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""UpdatedAt"" timestamp with time zone NULL,
                    ""CreatedBy"" uuid NULL,
                    ""UpdatedBy"" uuid NULL,
                    ""IsDeleted"" boolean NOT NULL DEFAULT false,
                    CONSTRAINT ""PK_ProductRequestIdempotency"" PRIMARY KEY (""Id"")
                );", ct);
            results.Add("ProductRequestIdempotency table: OK");
        }
        catch (Exception ex)
        {
            results.Add($"ProductRequestIdempotency table: FAILED Ã¢â‚¬â€ {ex.Message}");
        }

        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(@"
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ProductRequestIdempotency_TenantId_Key_Operation""
                    ON ""ProductRequestIdempotency"" (""TenantId"", ""Key"", ""Operation"");", ct);
            results.Add("IX_ProductRequestIdempotency_TenantId_Key_Operation: OK");
        }
        catch (Exception ex)
        {
            results.Add($"IX_ProductRequestIdempotency_TenantId_Key_Operation: FAILED Ã¢â‚¬â€ {ex.Message}");
        }

        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(@"
                CREATE INDEX IF NOT EXISTS ""IX_ProductRequestIdempotency_TenantId_ProductId""
                    ON ""ProductRequestIdempotency"" (""TenantId"", ""ProductId"");", ct);
            results.Add("IX_ProductRequestIdempotency_TenantId_ProductId: OK");
        }
        catch (Exception ex)
        {
            results.Add($"IX_ProductRequestIdempotency_TenantId_ProductId: FAILED Ã¢â‚¬â€ {ex.Message}");
        }

        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(@"
                CREATE INDEX IF NOT EXISTS ""IX_ProductRequestIdempotency_TenantId_ProductVariantId""
                    ON ""ProductRequestIdempotency"" (""TenantId"", ""ProductVariantId"");", ct);
            results.Add("IX_ProductRequestIdempotency_TenantId_ProductVariantId: OK");
        }
        catch (Exception ex)
        {
            results.Add($"IX_ProductRequestIdempotency_TenantId_ProductVariantId: FAILED Ã¢â‚¬â€ {ex.Message}");
        }

        _logger.LogInformation("TestAuthController.EnsureSchema completed: {Results}", string.Join("; ", results));
        return Ok(new { applied = results });
    }

    /// <summary>
    /// Starts the authoritative SI-CON-01 focused integration test runner.
    /// Remote TestSprite execution should call this instead of synthesizing an HTTP approximation.
    /// </summary>
    [HttpPost("si-con-01/run")]
    public IActionResult StartSiCon01Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 10
            ? request.Repetitions.Value
            : DefaultSiCon01Repetitions;

        if (SiCon01Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "An SI-CON-01 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildSiCon01CommandPreview(repetitions)
        };

        SiCon01Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecuteSiCon01RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started SI-CON-01 focused integration test run.
    /// </summary>
    [HttpGet("si-con-01/run/{jobId:guid}")]
    public IActionResult GetSiCon01RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!SiCon01Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"SI-CON-01 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative SI-CON-02 focused integration test runner.
    /// Remote TestSprite execution must call this instead of synthesizing overlapping HTTP requests.
    /// </summary>
    [HttpPost("si-con-02/run")]
    public IActionResult StartSiCon02Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultSiCon02Repetitions;

        if (SiCon02Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "An SI-CON-02 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildSiCon02CommandPreview(repetitions)
        };

        SiCon02Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecuteSiCon02RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started SI-CON-02 focused integration test run.
    /// </summary>
    [HttpGet("si-con-02/run/{jobId:guid}")]
    public IActionResult GetSiCon02RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!SiCon02Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"SI-CON-02 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative SI-CON-03 focused integration test runner.
    /// Remote TestSprite execution must call this instead of approximating duplicate worker overlap with HTTP flows.
    /// </summary>
    [HttpPost("si-con-03/run")]
    public IActionResult StartSiCon03Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultSiCon03Repetitions;

        if (SiCon03Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "An SI-CON-03 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildSiCon03CommandPreview(repetitions)
        };

        SiCon03Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecuteSiCon03RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started SI-CON-03 focused integration test run.
    /// </summary>
    [HttpGet("si-con-03/run/{jobId:guid}")]
    public IActionResult GetSiCon03RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!SiCon03Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"SI-CON-03 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative PI-CON-01 focused integration test runner.
    /// Remote TestSprite execution must call this instead of synthesizing concurrent purchase timing.
    /// </summary>
    [HttpPost("pi-con-01/run")]
    public IActionResult StartPiCon01Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultPiCon01Repetitions;

        if (PiCon01Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A PI-CON-01 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildPiCon01CommandPreview(repetitions)
        };

        PiCon01Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecutePiCon01RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started PI-CON-01 focused integration test run.
    /// </summary>
    [HttpGet("pi-con-01/run/{jobId:guid}")]
    public IActionResult GetPiCon01RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!PiCon01Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"PI-CON-01 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative PI-CON-02 focused integration test runner.
    /// Remote TestSprite execution must call this instead of approximating purchase replay overlap with HTTP flows.
    /// </summary>
    [HttpPost("pi-con-02/run")]
    public IActionResult StartPiCon02Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultPiCon02Repetitions;

        if (PiCon02Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A PI-CON-02 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildPiCon02CommandPreview(repetitions)
        };

        PiCon02Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecutePiCon02RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started PI-CON-02 focused integration test run.
    /// </summary>
    [HttpGet("pi-con-02/run/{jobId:guid}")]
    public IActionResult GetPiCon02RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!PiCon02Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"PI-CON-02 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative PI-CON-03 focused integration test runner.
    /// Remote TestSprite execution must call this instead of approximating duplicate purchase worker overlap with HTTP flows.
    /// </summary>
    [HttpPost("pi-con-03/run")]
    public IActionResult StartPiCon03Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultPiCon03Repetitions;

        if (PiCon03Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A PI-CON-03 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildPiCon03CommandPreview(repetitions)
        };

        PiCon03Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecutePiCon03RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started PI-CON-03 focused integration test run.
    /// </summary>
    [HttpGet("pi-con-03/run/{jobId:guid}")]
    public IActionResult GetPiCon03RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!PiCon03Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"PI-CON-03 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative SR-CON-01 focused integration test runner.
    /// Remote TestSprite execution must call this instead of approximating concurrent returns with HTTP timing.
    /// </summary>
    [HttpPost("sr-con-01/run")]
    public IActionResult StartSrCon01Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultSrCon01Repetitions;

        if (SrCon01Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "An SR-CON-01 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildSrCon01CommandPreview(repetitions)
        };

        SrCon01Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecuteSrCon01RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started SR-CON-01 focused integration test run.
    /// </summary>
    [HttpGet("sr-con-01/run/{jobId:guid}")]
    public IActionResult GetSrCon01RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!SrCon01Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"SR-CON-01 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative SR-CON-02 focused integration test runner.
    /// Remote TestSprite execution must call this instead of approximating replay overlap with HTTP timing.
    /// </summary>
    [HttpPost("sr-con-02/run")]
    public IActionResult StartSrCon02Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultSrCon02Repetitions;

        if (SrCon02Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "An SR-CON-02 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildSrCon02CommandPreview(repetitions)
        };

        SrCon02Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecuteSrCon02RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started SR-CON-02 focused integration test run.
    /// </summary>
    [HttpGet("sr-con-02/run/{jobId:guid}")]
    public IActionResult GetSrCon02RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!SrCon02Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"SR-CON-02 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative SR-CON-03 focused integration test runner.
    /// Remote TestSprite execution must call this instead of approximating duplicate worker overlap with HTTP timing.
    /// </summary>
    [HttpPost("sr-con-03/run")]
    public IActionResult StartSrCon03Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultSrCon03Repetitions;

        if (SrCon03Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "An SR-CON-03 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildSrCon03CommandPreview(repetitions)
        };

        SrCon03Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecuteSrCon03RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started SR-CON-03 focused integration test run.
    /// </summary>
    [HttpGet("sr-con-03/run/{jobId:guid}")]
    public IActionResult GetSrCon03RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!SrCon03Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"SR-CON-03 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative PR-CON-01 focused integration test runner.
    /// Remote TestSprite execution must call this instead of fabricating purchase-return overlap with HTTP timing.
    /// </summary>
    [HttpPost("pr-con-01/run")]
    public IActionResult StartPrCon01Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultPrCon01Repetitions;

        if (PrCon01Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A PR-CON-01 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildPrCon01CommandPreview(repetitions)
        };

        PrCon01Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecutePrCon01RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started PR-CON-01 focused integration test run.
    /// </summary>
    [HttpGet("pr-con-01/run/{jobId:guid}")]
    public IActionResult GetPrCon01RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!PrCon01Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"PR-CON-01 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative PR-CON-02 focused integration test runner.
    /// Remote TestSprite execution must call this instead of fabricating purchase-return replay timing with HTTP.
    /// </summary>
    [HttpPost("pr-con-02/run")]
    public IActionResult StartPrCon02Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultPrCon02Repetitions;

        if (PrCon02Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A PR-CON-02 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildPrCon02CommandPreview(repetitions)
        };

        PrCon02Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecutePrCon02RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started PR-CON-02 focused integration test run.
    /// </summary>
    [HttpGet("pr-con-02/run/{jobId:guid}")]
    public IActionResult GetPrCon02RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!PrCon02Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"PR-CON-02 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative PR-CON-03 focused integration test runner.
    /// Remote TestSprite execution must call this instead of fabricating purchase-return duplicate worker overlap with HTTP timing.
    /// </summary>
    [HttpPost("pr-con-03/run")]
    public IActionResult StartPrCon03Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultPrCon03Repetitions;

        if (PrCon03Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A PR-CON-03 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildPrCon03CommandPreview(repetitions)
        };

        PrCon03Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecutePrCon03RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started PR-CON-03 focused integration test run.
    /// </summary>
    [HttpGet("pr-con-03/run/{jobId:guid}")]
    public IActionResult GetPrCon03RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!PrCon03Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"PR-CON-03 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative PROD-CON-01 focused integration test runner.
    /// Remote TestSprite execution must call this instead of synthesizing concurrent variant creation timing.
    /// </summary>
    [HttpPost("prod-con-01/run")]
    public IActionResult StartProdCon01Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultProdCon01Repetitions;

        if (ProdCon01Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A PROD-CON-01 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildProdCon01CommandPreview(repetitions)
        };

        ProdCon01Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecuteProdCon01RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started PROD-CON-01 focused integration test run.
    /// </summary>
    [HttpGet("prod-con-01/run/{jobId:guid}")]
    public IActionResult GetProdCon01RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!ProdCon01Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"PROD-CON-01 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative PROD-CON-02 focused integration test runner.
    /// Remote TestSprite execution must call this instead of synthesizing active replay overlap timing.
    /// </summary>
    [HttpPost("prod-con-02/run")]
    public IActionResult StartProdCon02Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultProdCon02Repetitions;

        if (ProdCon02Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A PROD-CON-02 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildProdCon02CommandPreview(repetitions)
        };

        ProdCon02Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecuteProdCon02RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started PROD-CON-02 focused integration test run.
    /// </summary>
    [HttpGet("prod-con-02/run/{jobId:guid}")]
    public IActionResult GetProdCon02RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!ProdCon02Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"PROD-CON-02 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative PROD-CON-03 focused integration test runner.
    /// Remote TestSprite execution must call this instead of approximating duplicate product-sync worker overlap with HTTP flows.
    /// </summary>
    [HttpPost("prod-con-03/run")]
    public IActionResult StartProdCon03Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultProdCon03Repetitions;

        if (ProdCon03Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A PROD-CON-03 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildProdCon03CommandPreview(repetitions)
        };

        ProdCon03Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecuteProdCon03RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started PROD-CON-03 focused integration test run.
    /// </summary>
    [HttpGet("prod-con-03/run/{jobId}")]
    public IActionResult GetProdCon03RunStatus(string jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!Guid.TryParse(jobId, out var parsedJobId) ||
            !ProdCon03Jobs.TryGetValue(parsedJobId, out var job))
        {
            return NotFound(new
            {
                message = $"PROD-CON-03 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative VAR-CON-01 focused integration test runner.
    /// Remote TestSprite execution must call this instead of approximating concurrent variant combination creation with HTTP sleeps.
    /// </summary>
    [HttpPost("var-con-01/run")]
    public IActionResult StartVarCon01Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultVarCon01Repetitions;

        if (VarCon01Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A VAR-CON-01 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildVarCon01CommandPreview(repetitions)
        };

        VarCon01Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecuteVarCon01RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started VAR-CON-01 focused integration test run.
    /// </summary>
    [HttpGet("var-con-01/run/{jobId:guid}")]
    public IActionResult GetVarCon01RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!VarCon01Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"VAR-CON-01 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative VAR-CON-02 focused integration test runner.
    /// Remote TestSprite execution must call this instead of approximating replay during variant mutation with HTTP sleeps.
    /// </summary>
    [HttpPost("var-con-02/run")]
    public IActionResult StartVarCon02Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultVarCon02Repetitions;

        if (VarCon02Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A VAR-CON-02 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildVarCon02CommandPreview(repetitions)
        };

        VarCon02Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecuteVarCon02RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started VAR-CON-02 focused integration test run.
    /// </summary>
    [HttpGet("var-con-02/run/{jobId:guid}")]
    public IActionResult GetVarCon02RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!VarCon02Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"VAR-CON-02 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative VAR-CON-03 focused integration test runner.
    /// Remote TestSprite execution must call this instead of approximating duplicate variant projection delivery with HTTP sleeps.
    /// </summary>
    [HttpPost("var-con-03/run")]
    public IActionResult StartVarCon03Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultVarCon03Repetitions;

        if (VarCon03Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A VAR-CON-03 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildVarCon03CommandPreview(repetitions)
        };

        VarCon03Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecuteVarCon03RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started VAR-CON-03 focused integration test run.
    /// </summary>
    [HttpGet("var-con-03/run/{jobId:guid}")]
    public IActionResult GetVarCon03RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!VarCon03Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"VAR-CON-03 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative VAR-CON-04 focused integration test runner.
    /// Remote TestSprite execution must call this instead of approximating concurrent SKU reassignment with HTTP sleeps.
    /// </summary>
    [HttpPost("var-con-04/run")]
    public IActionResult StartVarCon04Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultVarCon04Repetitions;

        if (VarCon04Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A VAR-CON-04 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildVarCon04CommandPreview(repetitions)
        };

        VarCon04Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecuteVarCon04RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started VAR-CON-04 focused integration test run.
    /// </summary>
    [HttpGet("var-con-04/run/{jobId:guid}")]
    public IActionResult GetVarCon04RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!VarCon04Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"VAR-CON-04 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative VAR-CON-05 focused integration test runner.
    /// Remote TestSprite execution must call this instead of approximating delete/inventory overlap with HTTP sleeps.
    /// </summary>
    [HttpPost("var-con-05/run")]
    public IActionResult StartVarCon05Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultVarCon05Repetitions;

        if (VarCon05Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A VAR-CON-05 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildVarCon05CommandPreview(repetitions)
        };

        VarCon05Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecuteVarCon05RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started VAR-CON-05 focused integration test run.
    /// </summary>
    [HttpGet("var-con-05/run/{jobId:guid}")]
    public IActionResult GetVarCon05RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!VarCon05Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"VAR-CON-05 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative VAR-CON-06 focused integration test runner.
    /// Remote TestSprite execution must call this instead of approximating out-of-order projection delivery with sleeps.
    /// </summary>
    [HttpPost("var-con-06/run")]
    public IActionResult StartVarCon06Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultVarCon06Repetitions;

        if (VarCon06Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A VAR-CON-06 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildVarCon06CommandPreview(repetitions)
        };

        VarCon06Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecuteVarCon06RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started VAR-CON-06 focused integration test run.
    /// </summary>
    [HttpGet("var-con-06/run/{jobId:guid}")]
    public IActionResult GetVarCon06RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!VarCon06Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"VAR-CON-06 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative VAR-CON-07 focused integration test runner.
    /// Remote TestSprite execution must call this instead of approximating stale marketplace replay with sleeps.
    /// </summary>
    [HttpPost("var-con-07/run")]
    public IActionResult StartVarCon07Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultVarCon07Repetitions;

        if (VarCon07Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A VAR-CON-07 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildVarCon07CommandPreview(repetitions)
        };

        VarCon07Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecuteVarCon07RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started VAR-CON-07 focused integration test run.
    /// </summary>
    [HttpGet("var-con-07/run/{jobId:guid}")]
    public IActionResult GetVarCon07RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!VarCon07Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"VAR-CON-07 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative VAR-CON-08 focused integration test runner.
    /// Remote TestSprite execution must call this instead of synthesizing duplicate projection delivery timing.
    /// </summary>
    [HttpPost("var-con-08/run")]
    public IActionResult StartVarCon08Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultVarCon08Repetitions;

        if (VarCon08Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A VAR-CON-08 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildVarCon08CommandPreview(repetitions)
        };

        VarCon08Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecuteVarCon08RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started VAR-CON-08 focused integration test run.
    /// </summary>
    [HttpGet("var-con-08/run/{jobId:guid}")]
    public IActionResult GetVarCon08RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!VarCon08Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"VAR-CON-08 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative VAR-CON-09 focused integration test runner.
    /// Remote TestSprite execution must call this instead of synthesizing media replay timing.
    /// </summary>
    [HttpPost("var-con-09/run")]
    public IActionResult StartVarCon09Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultVarCon09Repetitions;

        if (VarCon09Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A VAR-CON-09 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildVarCon09CommandPreview(repetitions)
        };

        VarCon09Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecuteVarCon09RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started VAR-CON-09 focused integration test run.
    /// </summary>
    [HttpGet("var-con-09/run/{jobId:guid}")]
    public IActionResult GetVarCon09RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!VarCon09Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"VAR-CON-09 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative PRICE-CON-01 focused integration test runner.
    /// Remote TestSprite execution must call this instead of synthesizing concurrent price-update timing.
    /// </summary>
    [HttpPost("price-con-01/run")]
    public IActionResult StartPriceCon01Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultPriceCon01Repetitions;

        if (PriceCon01Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A PRICE-CON-01 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildPriceCon01CommandPreview(repetitions)
        };

        PriceCon01Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecutePriceCon01RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started PRICE-CON-01 focused integration test run.
    /// </summary>
    [HttpGet("price-con-01/run/{jobId:guid}")]
    public IActionResult GetPriceCon01RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!PriceCon01Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"PRICE-CON-01 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative PRICE-CON-02 focused integration test runner.
    /// Remote TestSprite execution must call this instead of synthesizing replay-overlap timing.
    /// </summary>
    [HttpPost("price-con-02/run")]
    public IActionResult StartPriceCon02Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultPriceCon02Repetitions;

        if (PriceCon02Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A PRICE-CON-02 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildPriceCon02CommandPreview(repetitions)
        };

        PriceCon02Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecutePriceCon02RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started PRICE-CON-02 focused integration test run.
    /// </summary>
    [HttpGet("price-con-02/run/{jobId:guid}")]
    public IActionResult GetPriceCon02RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!PriceCon02Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"PRICE-CON-02 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative PRICE-CON-03 focused integration test runner.
    /// Remote TestSprite execution must call this instead of synthesizing duplicate worker timing.
    /// </summary>
    [HttpPost("price-con-03/run")]
    public IActionResult StartPriceCon03Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultPriceCon03Repetitions;

        if (PriceCon03Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A PRICE-CON-03 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildPriceCon03CommandPreview(repetitions)
        };

        PriceCon03Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecutePriceCon03RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started PRICE-CON-03 focused integration test run.
    /// </summary>
    [HttpGet("price-con-03/run/{jobId:guid}")]
    public IActionResult GetPriceCon03RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!PriceCon03Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"PRICE-CON-03 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative PRICE-CON-04 focused integration test runner.
    /// Remote TestSprite execution must call this instead of synthesizing out-of-order delivery.
    /// </summary>
    [HttpPost("price-con-04/run")]
    public IActionResult StartPriceCon04Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultPriceCon04Repetitions;

        if (PriceCon04Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A PRICE-CON-04 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildPriceCon04CommandPreview(repetitions)
        };

        PriceCon04Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecutePriceCon04RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started PRICE-CON-04 focused integration test run.
    /// </summary>
    [HttpGet("price-con-04/run/{jobId:guid}")]
    public IActionResult GetPriceCon04RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!PriceCon04Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"PRICE-CON-04 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative PRICE-CON-05 focused integration test runner.
    /// Remote TestSprite execution must call this instead of synthesizing stale affiliate replay.
    /// </summary>
    [HttpPost("price-con-05/run")]
    public IActionResult StartPriceCon05Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultPriceCon05Repetitions;

        if (PriceCon05Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A PRICE-CON-05 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildPriceCon05CommandPreview(repetitions)
        };

        PriceCon05Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecutePriceCon05RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started PRICE-CON-05 focused integration test run.
    /// </summary>
    [HttpGet("price-con-05/run/{jobId:guid}")]
    public IActionResult GetPriceCon05RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!PriceCon05Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"PRICE-CON-05 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative PRICE-CON-06 focused integration test runner.
    /// Remote TestSprite execution must call this instead of synthesizing duplicate marketplace publish timing.
    /// </summary>
    [HttpPost("price-con-06/run")]
    public IActionResult StartPriceCon06Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultPriceCon06Repetitions;

        if (PriceCon06Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A PRICE-CON-06 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildPriceCon06CommandPreview(repetitions)
        };

        PriceCon06Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecutePriceCon06RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started PRICE-CON-06 focused integration test run.
    /// </summary>
    [HttpGet("price-con-06/run/{jobId:guid}")]
    public IActionResult GetPriceCon06RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!PriceCon06Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"PRICE-CON-06 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative PRICE-CON-07 focused integration test runner.
    /// Remote TestSprite execution must call this instead of synthesizing partial-sync corruption or recovery timing.
    /// </summary>
    [HttpPost("price-con-07/run")]
    public IActionResult StartPriceCon07Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultPriceCon07Repetitions;

        if (PriceCon07Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A PRICE-CON-07 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildPriceCon07CommandPreview(repetitions)
        };

        PriceCon07Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecutePriceCon07RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started PRICE-CON-07 focused integration test run.
    /// </summary>
    [HttpGet("price-con-07/run/{jobId:guid}")]
    public IActionResult GetPriceCon07RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!PriceCon07Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"PRICE-CON-07 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative PRICE-CON-08 focused integration test runner.
    /// Remote TestSprite execution must call this instead of synthesizing concurrent promotion overlap flows.
    /// </summary>
    [HttpPost("price-con-08/run")]
    public IActionResult StartPriceCon08Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultPriceCon08Repetitions;

        if (PriceCon08Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A PRICE-CON-08 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildPriceCon08CommandPreview(repetitions)
        };

        PriceCon08Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecutePriceCon08RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started PRICE-CON-08 focused integration test run.
    /// </summary>
    [HttpGet("price-con-08/run/{jobId:guid}")]
    public IActionResult GetPriceCon08RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!PriceCon08Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"PRICE-CON-08 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative PRICE-CON-09 focused integration test runner.
    /// Remote TestSprite execution must call this instead of synthesizing expiration overlap or replay timing flows.
    /// </summary>
    [HttpPost("price-con-09/run")]
    public IActionResult StartPriceCon09Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultPriceCon09Repetitions;

        if (PriceCon09Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A PRICE-CON-09 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildPriceCon09CommandPreview(repetitions)
        };

        PriceCon09Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecutePriceCon09RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started PRICE-CON-09 focused integration test run.
    /// </summary>
    [HttpGet("price-con-09/run/{jobId:guid}")]
    public IActionResult GetPriceCon09RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!PriceCon09Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"PRICE-CON-09 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative PRICE-CON-10 focused integration test runner.
    /// Remote TestSprite execution must call this instead of synthesizing duplicate promotion delivery timing.
    /// </summary>
    [HttpPost("price-con-10/run")]
    public IActionResult StartPriceCon10Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultPriceCon10Repetitions;

        if (PriceCon10Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A PRICE-CON-10 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildPriceCon10CommandPreview(repetitions)
        };

        PriceCon10Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecutePriceCon10RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started PRICE-CON-10 focused integration test run.
    /// </summary>
    [HttpGet("price-con-10/run/{jobId:guid}")]
    public IActionResult GetPriceCon10RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!PriceCon10Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"PRICE-CON-10 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative PRICE-CON-11 focused integration test runner.
    /// Remote TestSprite execution must call this instead of synthesizing cost mutation overlap timing.
    /// </summary>
    [HttpPost("price-con-11/run")]
    public IActionResult StartPriceCon11Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultPriceCon11Repetitions;

        if (PriceCon11Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A PRICE-CON-11 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildPriceCon11CommandPreview(repetitions)
        };

        PriceCon11Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecutePriceCon11RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started PRICE-CON-11 focused integration test run.
    /// </summary>
    [HttpGet("price-con-11/run/{jobId:guid}")]
    public IActionResult GetPriceCon11RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!PriceCon11Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"PRICE-CON-11 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative PRICE-CON-12 focused integration test runner.
    /// Remote TestSprite execution must call this instead of synthesizing cost-basis recalculation replay timing.
    /// </summary>
    [HttpPost("price-con-12/run")]
    public IActionResult StartPriceCon12Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultPriceCon12Repetitions;

        if (PriceCon12Jobs.Values.Any(job =>
                job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running))
        {
            return Conflict(new
            {
                message = "A PRICE-CON-12 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildPriceCon12CommandPreview(repetitions)
        };

        PriceCon12Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecutePriceCon12RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started PRICE-CON-12 focused integration test run.
    /// </summary>
    [HttpGet("price-con-12/run/{jobId:guid}")]
    public IActionResult GetPriceCon12RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!PriceCon12Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"PRICE-CON-12 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative PRICE-CON-13 focused integration test runner.
    /// Remote TestSprite execution must call this instead of synthesizing duplicate projection timing.
    /// </summary>
    [HttpPost("price-con-13/run")]
    public IActionResult StartPriceCon13Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultPriceCon13Repetitions;

        var activeJob = PriceCon13Jobs.Values.FirstOrDefault(job =>
            job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running);
        if (activeJob is not null)
        {
            if (activeJob.Repetitions == repetitions)
                return Ok(BuildSiCon01RunResponse(activeJob));

            return Conflict(new
            {
                message = "A PRICE-CON-13 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildPriceCon13CommandPreview(repetitions)
        };

        PriceCon13Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecutePriceCon13RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started PRICE-CON-13 focused integration test run.
    /// </summary>
    [HttpGet("price-con-13/run/{jobId:guid}")]
    public IActionResult GetPriceCon13RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!PriceCon13Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"PRICE-CON-13 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative STOCK-CON-01 focused integration test runner.
    /// Remote TestSprite execution must call this runner instead of simulating stock overlap with sleeps.
    /// </summary>
    [HttpPost("stock-con-01/run")]
    public IActionResult StartStockCon01Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultStockCon01Repetitions;

        var activeJob = StockCon01Jobs.Values.FirstOrDefault(job =>
            job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running);
        if (activeJob is not null)
        {
            if (activeJob.Repetitions == repetitions)
                return Ok(BuildSiCon01RunResponse(activeJob));

            return Conflict(new
            {
                message = "A STOCK-CON-01 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildStockCon01CommandPreview(repetitions)
        };

        StockCon01Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecuteStockCon01RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started STOCK-CON-01 focused integration test run.
    /// </summary>
    [HttpGet("stock-con-01/run/{jobId:guid}")]
    public IActionResult GetStockCon01RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!StockCon01Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"STOCK-CON-01 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative STOCK-CON-02 focused integration test runner.
    /// Remote TestSprite execution must call this runner instead of simulating replay overlap with sleeps.
    /// </summary>
    [HttpPost("stock-con-02/run")]
    public IActionResult StartStockCon02Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultStockCon02Repetitions;

        var activeJob = StockCon02Jobs.Values.FirstOrDefault(job =>
            job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running);
        if (activeJob is not null)
        {
            if (activeJob.Repetitions == repetitions)
                return Ok(BuildSiCon01RunResponse(activeJob));

            return Conflict(new
            {
                message = "A STOCK-CON-02 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildStockCon02CommandPreview(repetitions)
        };

        StockCon02Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecuteStockCon02RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started STOCK-CON-02 focused integration test run.
    /// </summary>
    [HttpGet("stock-con-02/run/{jobId:guid}")]
    public IActionResult GetStockCon02RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!StockCon02Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"STOCK-CON-02 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative STOCK-CON-03 focused integration test runner.
    /// Remote TestSprite execution must call this runner instead of simulating duplicate inventory workers with sleeps.
    /// </summary>
    [HttpPost("stock-con-03/run")]
    public IActionResult StartStockCon03Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultStockCon03Repetitions;

        var activeJob = StockCon03Jobs.Values.FirstOrDefault(job =>
            job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running);
        if (activeJob is not null)
        {
            if (activeJob.Repetitions == repetitions)
                return Ok(BuildSiCon01RunResponse(activeJob));

            return Conflict(new
            {
                message = "A STOCK-CON-03 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildStockCon03CommandPreview(repetitions)
        };

        StockCon03Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecuteStockCon03RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started STOCK-CON-03 focused integration test run.
    /// </summary>
    [HttpGet("stock-con-03/run/{jobId:guid}")]
    public IActionResult GetStockCon03RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!StockCon03Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"STOCK-CON-03 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative STOCK-CON-04 focused integration test runner.
    /// Remote TestSprite execution must call this runner instead of simulating sales-return replay overlap with sleeps.
    /// </summary>
    [HttpPost("stock-con-04/run")]
    public IActionResult StartStockCon04Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultStockCon04Repetitions;

        var activeJob = StockCon04Jobs.Values.FirstOrDefault(job =>
            job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running);
        if (activeJob is not null)
        {
            if (activeJob.Repetitions == repetitions)
                return Ok(BuildSiCon01RunResponse(activeJob));

            return Conflict(new
            {
                message = "A STOCK-CON-04 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildStockCon04CommandPreview(repetitions)
        };

        StockCon04Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecuteStockCon04RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started STOCK-CON-04 focused integration test run.
    /// </summary>
    [HttpGet("stock-con-04/run/{jobId:guid}")]
    public IActionResult GetStockCon04RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!StockCon04Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"STOCK-CON-04 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative STOCK-CON-05 focused integration test runner.
    /// Remote TestSprite execution must call this runner instead of simulating CommerceHub return webhook timing with sleeps.
    /// </summary>
    [HttpPost("stock-con-05/run")]
    public IActionResult StartStockCon05Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultStockCon05Repetitions;

        var activeJob = StockCon05Jobs.Values.FirstOrDefault(job =>
            job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running);
        if (activeJob is not null)
        {
            if (activeJob.Repetitions == repetitions)
                return Ok(BuildSiCon01RunResponse(activeJob));

            return Conflict(new
            {
                message = "A STOCK-CON-05 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildStockCon05CommandPreview(repetitions)
        };

        StockCon05Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecuteStockCon05RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started STOCK-CON-05 focused integration test run.
    /// </summary>
    [HttpGet("stock-con-05/run/{jobId:guid}")]
    public IActionResult GetStockCon05RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!StockCon05Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"STOCK-CON-05 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative STOCK-CON-06 focused integration test runner.
    /// Remote TestSprite execution must call this runner instead of simulating purchase-return replay timing with sleeps.
    /// </summary>
    [HttpPost("stock-con-06/run")]
    public IActionResult StartStockCon06Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultStockCon06Repetitions;

        var activeJob = StockCon06Jobs.Values.FirstOrDefault(job =>
            job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running);
        if (activeJob is not null)
        {
            if (activeJob.Repetitions == repetitions)
                return Ok(BuildSiCon01RunResponse(activeJob));

            return Conflict(new
            {
                message = "A STOCK-CON-06 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildStockCon06CommandPreview(repetitions)
        };

        StockCon06Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecuteStockCon06RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started STOCK-CON-06 focused integration test run.
    /// </summary>
    [HttpGet("stock-con-06/run/{jobId:guid}")]
    public IActionResult GetStockCon06RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!StockCon06Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"STOCK-CON-06 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Starts the authoritative STOCK-CON-07 focused integration test runner.
    /// Remote TestSprite execution must call this runner instead of simulating concurrent historical sales-return timing with sleeps.
    /// </summary>
    [HttpPost("stock-con-07/run")]
    public IActionResult StartStockCon07Run([FromBody] SiCon01RunRequest? request = null)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var repetitions = request?.Repetitions is > 0 and <= 5
            ? request.Repetitions.Value
            : DefaultStockCon07Repetitions;

        var activeJob = StockCon07Jobs.Values.FirstOrDefault(job =>
            job.Status is SiCon01RunStatus.Pending or SiCon01RunStatus.Running);
        if (activeJob is not null)
        {
            if (activeJob.Repetitions == repetitions)
                return Ok(BuildSiCon01RunResponse(activeJob));

            return Conflict(new
            {
                message = "A STOCK-CON-07 run is already in progress."
            });
        }

        var job = new SiCon01RunJob
        {
            JobId = Guid.NewGuid(),
            Status = SiCon01RunStatus.Pending,
            Repetitions = repetitions,
            StartedAtUtc = DateTime.UtcNow,
            Command = BuildStockCon07CommandPreview(repetitions)
        };

        StockCon07Jobs[job.JobId] = job;
        _ = Task.Run(() => ExecuteStockCon07RunAsync(job));

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Returns status for a previously started STOCK-CON-07 focused integration test run.
    /// </summary>
    [HttpGet("stock-con-07/run/{jobId:guid}")]
    public IActionResult GetStockCon07RunStatus(Guid jobId)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        if (!StockCon07Jobs.TryGetValue(jobId, out var job))
        {
            return NotFound(new
            {
                message = $"STOCK-CON-07 run '{jobId}' was not found."
            });
        }

        return Ok(BuildSiCon01RunResponse(job));
    }

    /// <summary>
    /// Atomically resets all SI reliability test pre-conditions so SI-HP-01, SI-REP-01,
    /// SI-RDS-01, and SI-DMSG-01 can be re-run cleanly by the TestSprite remote agent without
    /// manual intervention or external cleanup scripts.
    ///
    /// Actions (all idempotent):
    ///   1. DELETE stale SalesRequestIdempotency rows for the known SI deterministic keys.
    ///   2. RESET ProductVariantStocks quantity to 10 for the SI test stock row.
    ///   3. VERIFY seed entities (warehouse, product, variant, customer) exist.
    ///
    /// Only available in Testing / Development environments.
    /// </summary>
    [HttpPost("reset-si-fixtures")]
    public async Task<IActionResult> ResetSiFixtures(CancellationToken ct = default)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        // Deterministic SI test fixture IDs (must match SEED_si_test_data.sql)
        const string stockId    = "10000000-0000-0000-0000-000000000099";
        const int    resetQty   = 10;
        var results = new List<string>();

        // Open a raw ADO.NET connection for direct SQL — avoids EF Core parameter-mapping
        // limitations with array types and CancellationToken in ExecuteSqlRawAsync.
        var conn = _dbContext.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        // Step 1: Purge stale idempotency records for the known SI keys, including
        // the deterministic SI-DMSG-01 suffix variants used by the replay harness.
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM ""SalesRequestIdempotency""
                 WHERE ""Key"" IN (
                     'si-hp-01-req-0001',
                     'si-rep-01-req-0001',
                     'si-rds-01-req-0001'
                 )
                    OR ""Key"" LIKE 'si-dmsg-01-req-0001%'";
            var deleted = await cmd.ExecuteNonQueryAsync(ct);
            results.Add($"SalesRequestIdempotency purge: {deleted} row(s) deleted");
        }
        catch (Exception ex)
        {
            results.Add($"SalesRequestIdempotency purge: FAILED — {ex.Message}");
        }

        // Step 2: Reset stock to resetQty using GREATEST (never reduces existing qty)
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                UPDATE ""ProductVariantStocks""
                   SET ""Quantity"" = GREATEST(""Quantity"", {resetQty})
                 WHERE ""Id"" = '{stockId}'::uuid";
            var updated = await cmd.ExecuteNonQueryAsync(ct);
            results.Add(updated > 0
                ? $"ProductVariantStocks reset: quantity >= {resetQty} (rows updated: {updated})"
                : $"ProductVariantStocks reset: WARNING — stock row {stockId} not found. Run SEED_si_test_data.sql first.");
        }
        catch (Exception ex)
        {
            results.Add($"ProductVariantStocks reset: FAILED — {ex.Message}");
        }

        // Step 3: Verify seed entities exist (non-mutating)
        var seedChecks = new[]
        {
            (@"SELECT COUNT(*)::int FROM ""Warehouses""           WHERE ""Id"" = '10000000-0000-0000-0000-000000000001'::uuid", "Warehouse"),
            (@"SELECT COUNT(*)::int FROM ""Products""             WHERE ""Id"" = '10000000-0000-0000-0000-000000000010'::uuid", "Product"),
            (@"SELECT COUNT(*)::int FROM ""ProductVariants""      WHERE ""Id"" = '10000000-0000-0000-0000-000000000011'::uuid", "ProductVariant"),
            (@"SELECT COUNT(*)::int FROM ""Customers""            WHERE ""Id"" = '10000000-0000-0000-0000-000000000020'::uuid", "Customer"),
        };


        foreach (var (sql, entity) in seedChecks)
        {
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                var count = (int)(await cmd.ExecuteScalarAsync(ct))!;
                results.Add(count > 0
                    ? $"{entity} seed: OK"
                    : $"{entity} seed: MISSING — run SEED_si_test_data.sql");
            }
            catch (Exception ex)
            {
                results.Add($"{entity} seed check: FAILED — {ex.Message}");
            }
        }

        _logger.LogInformation("TestAuthController.ResetSiFixtures completed: {Results}", string.Join("; ", results));
        return Ok(new { reset = results });
    }

    /// <summary>
    /// Manually triggers accounting outbox processing — for SI-DMSG-01 replay simulation.
    /// Accessible with normal test-auth JWT (no SuperAdmin required).
    /// </summary>
    [HttpPost("outbox/process")]
    [AllowAnonymous]
    public async Task<IActionResult> TriggerOutboxProcess(CancellationToken ct = default)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var processed = await _outboxProcessor.ProcessPendingMessagesAsync(50, ct);
        return Ok(new { ProcessedCount = processed, Timestamp = DateTime.UtcNow });
    }

    /// <summary>
    /// Returns accounting outbox status counts — for SI-DMSG-01 baseline snapshot.
    /// Accessible with normal test-auth JWT (no SuperAdmin required).
    /// </summary>
    [HttpGet("outbox/status")]
    [AllowAnonymous]
    public async Task<IActionResult> GetOutboxStatus(CancellationToken ct = default)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var pendingCount   = await _dbContext.AccountingOutboxMessages.CountAsync(m => m.ProcessedAtUtc == null && !m.IsFailed, ct);
        var failedCount    = await _dbContext.AccountingOutboxMessages.CountAsync(m => m.IsFailed, ct);
        var processedCount = await _dbContext.AccountingOutboxMessages.CountAsync(m => m.ProcessedAtUtc != null, ct);
        var journalCount   = await _dbContext.JournalEntries.IgnoreQueryFilters().CountAsync(ct);

        return Ok(new
        {
            processedCount,
            pendingCount,
            failedCount,
            journalCount,
            stockSyncCount  = pendingCount + processedCount,
            Timestamp       = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Returns the actual PostgreSQL catalog state for SalesRequestIdempotency: indexes, constraints, columns.
    /// Use this to debug 42P10 (ON CONFLICT mismatch) failures.
    /// </summary>
    [HttpGet("diagnose")]
    public async Task<IActionResult> Diagnose(CancellationToken ct = default)
    {
        if (!IsTestHarnessEnvironment())
            return NotFound();

        var conn = _dbContext.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        var diagnostics = new Dictionary<string, object>();

        // Indexes on SalesRequestIdempotency
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT indexname, indexdef, indisunique::text
                FROM pg_indexes
                JOIN pg_index ON indexrelid = (SELECT oid FROM pg_class WHERE relname = tablename)
                WHERE tablename = 'SalesRequestIdempotency'";
            var indexes = new List<string>();
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
                indexes.Add($"{rdr[0]}: unique={rdr[2]} def={rdr[1]}");
            diagnostics["pg_indexes"] = indexes;
        }

        // Columns
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT column_name, data_type
                FROM information_schema.columns
                WHERE table_name = 'SalesRequestIdempotency'
                ORDER BY ordinal_position";
            var cols = new List<string>();
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
                cols.Add($"{rdr[0]} ({rdr[1]})");
            diagnostics["columns"] = cols;
        }

        // Unique constraints
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT conname, pg_get_constraintdef(oid)
                FROM pg_constraint
                WHERE conrelid = (SELECT oid FROM pg_class WHERE relname = 'SalesRequestIdempotency')
                  AND contype = 'u'";
            var constraints = new List<string>();
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
                constraints.Add($"{rdr[0]}: {rdr[1]}");
            diagnostics["unique_constraints"] = constraints;
        }

        return Ok(diagnostics);
    }

    // -------------------------------------------------------------------------
    // Internal helpers
    // -------------------------------------------------------------------------

    private (string Token, DateTime ExpiresAt) BuildToken(Guid tenantId, Guid userId, string sessionBinding)
    {
        var secretBytes = Encoding.UTF8.GetBytes(_jwtSettings.Secret);
        var key = new SymmetricSecurityKey(secretBytes);
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var issuedAt = DateTime.UtcNow;
        var expiresAt = issuedAt.AddHours(1); // Short-lived test token

        var claims = new[]
        {
            // tenant_id is the primary claim consumed by TenantResolutionMiddleware
            new Claim("tenant_id", tenantId.ToString()),

            // NameIdentifier is the user identity claim expected by the POS JWT pipeline
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),

            // Role claim allows the test token to pass [Authorize(Roles = "...")] guards
            new Claim(ClaimTypes.Role, TestUserRole),
            new Claim(PosSecurityConstants.SessionBindingClaim, sessionBinding),

            // Standard JWT claims for traceability
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new Claim(JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(issuedAt).ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64),

            // Test-environment marker — allows log correlation
            new Claim("env", "Testing"),
        };

        var tokenDescriptor = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            notBefore: issuedAt,
            expires: expiresAt,
            signingCredentials: credentials);

        var handler = new JwtSecurityTokenHandler();
        var tokenString = handler.WriteToken(tokenDescriptor);

        return (tokenString, expiresAt);
    }

    private async Task ExecuteSiCon01RunAsync(SiCon01RunJob job)
    {
        await SiCon01RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for SI-CON-01 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for SI-CON-01 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecuteSiCon01IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused SI-CON-01 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "SI-CON-01 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            SiCon01RunGate.Release();
        }
    }

    private static async Task<SiCon01IterationResult> ExecuteSiCon01IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            SiCon01FocusedTestFilter);
    }

    private async Task ExecuteSiCon02RunAsync(SiCon01RunJob job)
    {
        await SiCon02RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for SI-CON-02 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for SI-CON-02 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecuteSiCon02IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused SI-CON-02 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "SI-CON-02 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            SiCon02RunGate.Release();
        }
    }

    private static async Task<SiCon01IterationResult> ExecuteSiCon02IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            SiCon02FocusedTestFilter);
    }

    private async Task ExecuteSiCon03RunAsync(SiCon01RunJob job)
    {
        await SiCon03RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for SI-CON-03 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for SI-CON-03 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecuteSiCon03IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused SI-CON-03 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "SI-CON-03 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            SiCon03RunGate.Release();
        }
    }

    private static async Task<SiCon01IterationResult> ExecuteSiCon03IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            SiCon03FocusedTestFilter);
    }

    private async Task ExecutePiCon01RunAsync(SiCon01RunJob job)
    {
        await PiCon01RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for PI-CON-01 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for PI-CON-01 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecutePiCon01IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused PI-CON-01 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "PI-CON-01 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            PiCon01RunGate.Release();
        }
    }

    private static async Task<SiCon01IterationResult> ExecutePiCon01IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            PiCon01FocusedTestFilter);
    }

    private async Task ExecutePiCon02RunAsync(SiCon01RunJob job)
    {
        await PiCon02RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for PI-CON-02 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for PI-CON-02 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecutePiCon02IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused PI-CON-02 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "PI-CON-02 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            PiCon02RunGate.Release();
        }
    }

    private static async Task<SiCon01IterationResult> ExecutePiCon02IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            PiCon02FocusedTestFilter);
    }

    private async Task ExecutePiCon03RunAsync(SiCon01RunJob job)
    {
        await PiCon03RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for PI-CON-03 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for PI-CON-03 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecutePiCon03IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused PI-CON-03 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "PI-CON-03 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            PiCon03RunGate.Release();
        }
    }

    private static async Task<SiCon01IterationResult> ExecutePiCon03IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            PiCon03FocusedTestFilter);
    }

    private async Task ExecuteSrCon01RunAsync(SiCon01RunJob job)
    {
        await SrCon01RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for SR-CON-01 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for SR-CON-01 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecuteSrCon01IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused SR-CON-01 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "SR-CON-01 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            SrCon01RunGate.Release();
        }
    }

    private static async Task<SiCon01IterationResult> ExecuteSrCon01IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            SrCon01FocusedTestFilter);
    }

    private async Task ExecuteSrCon02RunAsync(SiCon01RunJob job)
    {
        await SrCon02RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for SR-CON-02 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for SR-CON-02 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecuteSrCon02IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused SR-CON-02 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "SR-CON-02 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            SrCon02RunGate.Release();
        }
    }

    private static async Task<SiCon01IterationResult> ExecuteSrCon02IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            SrCon02FocusedTestFilter);
    }

    private async Task ExecuteSrCon03RunAsync(SiCon01RunJob job)
    {
        await SrCon03RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for SR-CON-03 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for SR-CON-03 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecuteSrCon03IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused SR-CON-03 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "SR-CON-03 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            SrCon03RunGate.Release();
        }
    }

    private static async Task<SiCon01IterationResult> ExecuteSrCon03IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            SrCon03FocusedTestFilter);
    }

    private async Task ExecutePrCon01RunAsync(SiCon01RunJob job)
    {
        await PrCon01RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for PR-CON-01 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for PR-CON-01 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecutePrCon01IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused PR-CON-01 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "PR-CON-01 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            PrCon01RunGate.Release();
        }
    }

    private static async Task<SiCon01IterationResult> ExecutePrCon01IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            PrCon01FocusedTestFilter);
    }

    private async Task ExecutePrCon02RunAsync(SiCon01RunJob job)
    {
        await PrCon02RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for PR-CON-02 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for PR-CON-02 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecutePrCon02IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused PR-CON-02 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "PR-CON-02 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            PrCon02RunGate.Release();
        }
    }

    private static async Task<SiCon01IterationResult> ExecutePrCon02IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            PrCon02FocusedTestFilter);
    }

    private async Task ExecutePrCon03RunAsync(SiCon01RunJob job)
    {
        await PrCon03RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for PR-CON-03 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for PR-CON-03 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecutePrCon03IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused PR-CON-03 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "PR-CON-03 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            PrCon03RunGate.Release();
        }
    }

    private static async Task<SiCon01IterationResult> ExecutePrCon03IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            PrCon03FocusedTestFilter);
    }

    private async Task ExecuteProdCon01RunAsync(SiCon01RunJob job)
    {
        await ProdCon01RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for PROD-CON-01 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for PROD-CON-01 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecuteProdCon01IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused PROD-CON-01 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "PROD-CON-01 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            ProdCon01RunGate.Release();
        }
    }

    private static async Task<SiCon01IterationResult> ExecuteProdCon01IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            ProdCon01FocusedTestFilter);
    }

    private async Task ExecuteProdCon02RunAsync(SiCon01RunJob job)
    {
        await ProdCon02RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for PROD-CON-02 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for PROD-CON-02 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecuteProdCon02IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused PROD-CON-02 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "PROD-CON-02 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            ProdCon02RunGate.Release();
        }
    }

    private async Task ExecuteProdCon03RunAsync(SiCon01RunJob job)
    {
        await ProdCon03RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for PROD-CON-03 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for PROD-CON-03 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecuteProdCon03IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused PROD-CON-03 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "PROD-CON-03 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            ProdCon03RunGate.Release();
        }
    }

    private static async Task<SiCon01IterationResult> ExecuteProdCon02IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            ProdCon02FocusedTestFilter);
    }

    private static async Task<SiCon01IterationResult> ExecuteProdCon03IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            ProdCon03FocusedTestFilter);
    }

    private async Task ExecuteVarCon01RunAsync(SiCon01RunJob job)
    {
        await VarCon01RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for VAR-CON-01 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for VAR-CON-01 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecuteVarCon01IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused VAR-CON-01 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "VAR-CON-01 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            VarCon01RunGate.Release();
        }
    }

    private static async Task<SiCon01IterationResult> ExecuteVarCon01IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            VarCon01FocusedTestFilter);
    }

    private async Task ExecuteVarCon02RunAsync(SiCon01RunJob job)
    {
        await VarCon02RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for VAR-CON-02 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for VAR-CON-02 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecuteVarCon02IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused VAR-CON-02 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "VAR-CON-02 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            VarCon02RunGate.Release();
        }
    }

    private static async Task<SiCon01IterationResult> ExecuteVarCon02IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            VarCon02FocusedTestFilter);
    }

    private async Task ExecuteVarCon03RunAsync(SiCon01RunJob job)
    {
        await VarCon03RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for VAR-CON-03 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for VAR-CON-03 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecuteVarCon03IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused VAR-CON-03 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "VAR-CON-03 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            VarCon03RunGate.Release();
        }
    }

    private static async Task<SiCon01IterationResult> ExecuteVarCon03IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            VarCon03FocusedTestFilter);
    }

    private async Task ExecuteVarCon04RunAsync(SiCon01RunJob job)
    {
        await VarCon04RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for VAR-CON-04 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for VAR-CON-04 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecuteVarCon04IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused VAR-CON-04 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "VAR-CON-04 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            VarCon04RunGate.Release();
        }
    }

    private static async Task<SiCon01IterationResult> ExecuteVarCon04IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            VarCon04FocusedTestFilter);
    }

    private async Task ExecuteVarCon05RunAsync(SiCon01RunJob job)
    {
        await VarCon05RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for VAR-CON-05 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for VAR-CON-05 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecuteVarCon05IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused VAR-CON-05 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "VAR-CON-05 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            VarCon05RunGate.Release();
        }
    }

    private static async Task<SiCon01IterationResult> ExecuteVarCon05IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            VarCon05FocusedTestFilter);
    }

    private async Task ExecuteVarCon06RunAsync(SiCon01RunJob job)
    {
        await VarCon06RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for VAR-CON-06 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for VAR-CON-06 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecuteVarCon06IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused VAR-CON-06 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "VAR-CON-06 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            VarCon06RunGate.Release();
        }
    }

    private static async Task<SiCon01IterationResult> ExecuteVarCon06IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            VarCon06FocusedTestFilter);
    }

    private async Task ExecuteVarCon07RunAsync(SiCon01RunJob job)
    {
        await VarCon07RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for VAR-CON-07 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for VAR-CON-07 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecuteVarCon07IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused VAR-CON-07 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "VAR-CON-07 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            VarCon07RunGate.Release();
        }
    }

    private static async Task<SiCon01IterationResult> ExecuteVarCon07IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            VarCon07FocusedTestFilter);
    }

    private async Task ExecuteVarCon08RunAsync(SiCon01RunJob job)
    {
        await VarCon08RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for VAR-CON-08 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for VAR-CON-08 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecuteVarCon08IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused VAR-CON-08 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "VAR-CON-08 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            VarCon08RunGate.Release();
        }
    }

    private static async Task<SiCon01IterationResult> ExecuteVarCon08IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            VarCon08FocusedTestFilter);
    }

    private async Task ExecuteVarCon09RunAsync(SiCon01RunJob job)
    {
        await VarCon09RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for VAR-CON-09 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for VAR-CON-09 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecuteVarCon09IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused VAR-CON-09 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "VAR-CON-09 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            VarCon09RunGate.Release();
        }
    }

    private static async Task<SiCon01IterationResult> ExecuteVarCon09IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            VarCon09FocusedTestFilter);
    }

    private async Task ExecutePriceCon02RunAsync(SiCon01RunJob job)
    {
        await PriceCon02RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for PRICE-CON-02 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for PRICE-CON-02 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecutePriceCon02IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused PRICE-CON-02 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "PRICE-CON-02 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            PriceCon02RunGate.Release();
        }
    }

    private async Task ExecutePriceCon03RunAsync(SiCon01RunJob job)
    {
        await PriceCon03RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for PRICE-CON-03 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for PRICE-CON-03 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecutePriceCon03IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused PRICE-CON-03 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "PRICE-CON-03 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            PriceCon03RunGate.Release();
        }
    }

    private async Task ExecutePriceCon04RunAsync(SiCon01RunJob job)
    {
        await PriceCon04RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for PRICE-CON-04 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for PRICE-CON-04 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecutePriceCon04IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused PRICE-CON-04 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "PRICE-CON-04 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            PriceCon04RunGate.Release();
        }
    }

    private async Task ExecutePriceCon05RunAsync(SiCon01RunJob job)
    {
        await PriceCon05RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for PRICE-CON-05 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for PRICE-CON-05 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecutePriceCon05IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused PRICE-CON-05 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "PRICE-CON-05 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            PriceCon05RunGate.Release();
        }
    }

    private async Task ExecutePriceCon06RunAsync(SiCon01RunJob job)
    {
        await PriceCon06RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for PRICE-CON-06 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for PRICE-CON-06 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecutePriceCon06IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused PRICE-CON-06 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "PRICE-CON-06 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            PriceCon06RunGate.Release();
        }
    }

    private async Task ExecutePriceCon07RunAsync(SiCon01RunJob job)
    {
        await PriceCon07RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for PRICE-CON-07 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for PRICE-CON-07 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecutePriceCon07IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused PRICE-CON-07 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "PRICE-CON-07 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            PriceCon07RunGate.Release();
        }
    }

    private async Task ExecutePriceCon08RunAsync(SiCon01RunJob job)
    {
        await PriceCon08RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for PRICE-CON-08 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for PRICE-CON-08 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecutePriceCon08IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused PRICE-CON-08 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "PRICE-CON-08 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            PriceCon08RunGate.Release();
        }
    }

    private async Task ExecutePriceCon09RunAsync(SiCon01RunJob job)
    {
        await PriceCon09RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for PRICE-CON-09 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for PRICE-CON-09 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecutePriceCon09IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused PRICE-CON-09 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "PRICE-CON-09 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            PriceCon09RunGate.Release();
        }
    }

    private async Task ExecutePriceCon10RunAsync(SiCon01RunJob job)
    {
        await PriceCon10RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for PRICE-CON-10 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for PRICE-CON-10 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecutePriceCon10IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused PRICE-CON-10 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "PRICE-CON-10 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            PriceCon10RunGate.Release();
        }
    }

    private async Task ExecutePriceCon11RunAsync(SiCon01RunJob job)
    {
        await PriceCon11RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for PRICE-CON-11 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for PRICE-CON-11 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecutePriceCon11IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused PRICE-CON-11 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "PRICE-CON-11 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            PriceCon11RunGate.Release();
        }
    }

    private async Task ExecutePriceCon12RunAsync(SiCon01RunJob job)
    {
        await PriceCon12RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for PRICE-CON-12 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for PRICE-CON-12 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecutePriceCon12IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused PRICE-CON-12 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "PRICE-CON-12 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            PriceCon12RunGate.Release();
        }
    }

    private async Task ExecutePriceCon13RunAsync(SiCon01RunJob job)
    {
        await PriceCon13RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for PRICE-CON-13 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for PRICE-CON-13 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecutePriceCon13IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused PRICE-CON-13 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "PRICE-CON-13 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            PriceCon13RunGate.Release();
        }
    }

    private async Task ExecuteStockCon01RunAsync(SiCon01RunJob job)
    {
        await StockCon01RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for STOCK-CON-01 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for STOCK-CON-01 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecuteStockCon01IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused STOCK-CON-01 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "STOCK-CON-01 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            StockCon01RunGate.Release();
        }
    }

    private async Task ExecuteStockCon02RunAsync(SiCon01RunJob job)
    {
        await StockCon02RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for STOCK-CON-02 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for STOCK-CON-02 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecuteStockCon02IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused STOCK-CON-02 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "STOCK-CON-02 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            StockCon02RunGate.Release();
        }
    }

    private async Task ExecuteStockCon03RunAsync(SiCon01RunJob job)
    {
        await StockCon03RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for STOCK-CON-03 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for STOCK-CON-03 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecuteStockCon03IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused STOCK-CON-03 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "STOCK-CON-03 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            StockCon03RunGate.Release();
        }
    }

    private async Task ExecuteStockCon04RunAsync(SiCon01RunJob job)
    {
        await StockCon04RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for STOCK-CON-04 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for STOCK-CON-04 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecuteStockCon04IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused STOCK-CON-04 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "STOCK-CON-04 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            StockCon04RunGate.Release();
        }
    }

    private async Task ExecuteStockCon05RunAsync(SiCon01RunJob job)
    {
        await StockCon05RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for STOCK-CON-05 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for STOCK-CON-05 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecuteStockCon05IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused STOCK-CON-05 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "STOCK-CON-05 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            StockCon05RunGate.Release();
        }
    }

    private async Task ExecuteStockCon06RunAsync(SiCon01RunJob job)
    {
        await StockCon06RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for STOCK-CON-06 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for STOCK-CON-06 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecuteStockCon06IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused STOCK-CON-06 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "STOCK-CON-06 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            StockCon06RunGate.Release();
        }
    }

    private async Task ExecuteStockCon07RunAsync(SiCon01RunJob job)
    {
        await StockCon07RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for STOCK-CON-07 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for STOCK-CON-07 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecuteStockCon07IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused STOCK-CON-07 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "STOCK-CON-07 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            StockCon07RunGate.Release();
        }
    }

    private async Task ExecutePriceCon01RunAsync(SiCon01RunJob job)
    {
        await PriceCon01RunGate.WaitAsync();

        try
        {
            job.Status = SiCon01RunStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;

            var solutionRoot = Directory.GetParent(_env.ContentRootPath)?.FullName
                ?? throw new InvalidOperationException("Failed to resolve solution root for PRICE-CON-01 runner.");
            var testProjectPath = Path.Combine(solutionRoot, "PointOfSale.Tests", "PointOfSale.Tests.csproj");

            if (!System.IO.File.Exists(testProjectPath))
            {
                throw new FileNotFoundException("PointOfSale.Tests.csproj was not found for PRICE-CON-01 runner.", testProjectPath);
            }

            for (var iteration = 1; iteration <= job.Repetitions; iteration++)
            {
                var iterationResult = await ExecutePriceCon01IterationAsync(solutionRoot, testProjectPath, iteration);

                lock (job.Iterations)
                {
                    job.Iterations.Add(iterationResult);
                }

                if (iterationResult.ExitCode != 0)
                {
                    job.Status = SiCon01RunStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.FailureMessage =
                        $"Focused PRICE-CON-01 run failed on iteration {iteration} with exit code {iterationResult.ExitCode}.";
                    return;
                }
            }

            job.Status = SiCon01RunStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = SiCon01RunStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.FailureMessage = ex.Message;
            _logger.LogError(ex, "PRICE-CON-01 focused test runner failed for job {JobId}", job.JobId);
        }
        finally
        {
            PriceCon01RunGate.Release();
        }
    }

    private static async Task<SiCon01IterationResult> ExecutePriceCon02IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            PriceCon02FocusedTestFilter);
    }

    private static async Task<SiCon01IterationResult> ExecutePriceCon03IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            PriceCon03FocusedTestFilter);
    }

    private static async Task<SiCon01IterationResult> ExecutePriceCon04IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            PriceCon04FocusedTestFilter);
    }

    private static async Task<SiCon01IterationResult> ExecutePriceCon05IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            PriceCon05FocusedTestFilter);
    }

    private static async Task<SiCon01IterationResult> ExecutePriceCon06IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            PriceCon06FocusedTestFilter);
    }

    private static async Task<SiCon01IterationResult> ExecutePriceCon07IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            PriceCon07FocusedTestFilter);
    }

    private static async Task<SiCon01IterationResult> ExecutePriceCon08IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            PriceCon08FocusedTestFilter);
    }

    private static async Task<SiCon01IterationResult> ExecutePriceCon09IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            PriceCon09FocusedTestFilter);
    }

    private static async Task<SiCon01IterationResult> ExecutePriceCon10IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            PriceCon10FocusedTestFilter);
    }

    private static async Task<SiCon01IterationResult> ExecutePriceCon11IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            PriceCon11FocusedTestFilter);
    }

    private static async Task<SiCon01IterationResult> ExecutePriceCon12IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            PriceCon12FocusedTestFilter);
    }

    private static async Task<SiCon01IterationResult> ExecutePriceCon13IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            PriceCon13FocusedTestFilter);
    }

    private static async Task<SiCon01IterationResult> ExecuteStockCon01IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            StockCon01FocusedTestFilter);
    }

    private static async Task<SiCon01IterationResult> ExecuteStockCon02IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            StockCon02FocusedTestFilter);
    }

    private static async Task<SiCon01IterationResult> ExecuteStockCon03IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            StockCon03FocusedTestFilter);
    }

    private static async Task<SiCon01IterationResult> ExecuteStockCon04IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            StockCon04FocusedTestFilter);
    }

    private static async Task<SiCon01IterationResult> ExecuteStockCon05IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            StockCon05FocusedTestFilter);
    }

    private static async Task<SiCon01IterationResult> ExecuteStockCon06IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            StockCon06FocusedTestFilter);
    }

    private static async Task<SiCon01IterationResult> ExecuteStockCon07IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            StockCon07FocusedTestFilter);
    }

    private static async Task<SiCon01IterationResult> ExecutePriceCon01IterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration)
    {
        return await ExecuteFocusedTestIterationAsync(
            solutionRoot,
            testProjectPath,
            iteration,
            PriceCon01FocusedTestFilter);
    }

    private static async Task<SiCon01IterationResult> ExecuteFocusedTestIterationAsync(
        string solutionRoot,
        string testProjectPath,
        int iteration,
        string testFilter)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = solutionRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("test");
        startInfo.ArgumentList.Add(testProjectPath);
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--filter");
        startInfo.ArgumentList.Add(testFilter);
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("minimal");
        startInfo.Environment["REQUIRE_REAL_POSTGRES_TESTS"] = "1";

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var exitTask = process.WaitForExitAsync();
        if (await Task.WhenAny(exitTask, Task.Delay(FocusedTestIterationTimeout)) != exitTask)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            await process.WaitForExitAsync();

            var timedOutStdout = await stdoutTask;
            var timedOutStderr = await stderrTask;
            return new SiCon01IterationResult
            {
                Iteration = iteration,
                ExitCode = -1,
                CompletedAtUtc = DateTime.UtcNow,
                StdoutTail = TakeTail(timedOutStdout, 6000),
                StderrTail = TakeTail(
                    $"Focused test iteration timed out after {FocusedTestIterationTimeout.TotalMinutes:0} minutes.{Environment.NewLine}{timedOutStderr}",
                    4000)
            };
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (stdout.Contains("No test matches the given testcase filter", StringComparison.OrdinalIgnoreCase))
        {
            stderr = string.IsNullOrWhiteSpace(stderr)
                ? "Focused test runner failed closed because the compiled test assembly did not contain the requested testcase filter."
                : $"{stderr}{Environment.NewLine}Focused test runner failed closed because the compiled test assembly did not contain the requested testcase filter.";

            return new SiCon01IterationResult
            {
                Iteration = iteration,
                ExitCode = -2,
                CompletedAtUtc = DateTime.UtcNow,
                StdoutTail = TakeTail(stdout, 6000),
                StderrTail = TakeTail(stderr, 4000)
            };
        }

        return new SiCon01IterationResult
        {
            Iteration = iteration,
            ExitCode = process.ExitCode,
            CompletedAtUtc = DateTime.UtcNow,
            StdoutTail = TakeTail(stdout, 6000),
            StderrTail = TakeTail(stderr, 4000)
        };
    }

    private static object BuildSiCon01RunResponse(SiCon01RunJob job)
    {
        lock (job.Iterations)
        {
            return new SiCon01RunResponse
            {
                JobId = job.JobId,
                Status = job.Status.ToString(),
                Repetitions = job.Repetitions,
                StartedAtUtc = job.StartedAtUtc,
                CompletedAtUtc = job.CompletedAtUtc,
                AllPassed = job.Status == SiCon01RunStatus.Completed &&
                            job.Iterations.Count == job.Repetitions &&
                            job.Iterations.All(item => item.ExitCode == 0),
                Command = job.Command,
                FailureMessage = job.FailureMessage,
                Iterations = job.Iterations.Select(item => new SiCon01IterationResponse
                {
                    Iteration = item.Iteration,
                    ExitCode = item.ExitCode,
                    CompletedAtUtc = item.CompletedAtUtc,
                    StdoutTail = item.StdoutTail,
                    StderrTail = item.StderrTail
                }).ToArray()
            };
        }
    }

    private static string BuildSiCon01CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{SiCon01FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildSiCon02CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{SiCon02FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildSiCon03CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{SiCon03FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildPiCon01CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{PiCon01FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildPiCon02CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{PiCon02FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildPiCon03CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{PiCon03FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildSrCon01CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{SrCon01FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildSrCon02CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{SrCon02FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildSrCon03CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{SrCon03FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildPrCon01CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{PrCon01FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildPrCon02CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{PrCon02FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildPrCon03CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{PrCon03FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildProdCon01CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{ProdCon01FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildProdCon02CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{ProdCon02FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildProdCon03CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{ProdCon03FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildVarCon01CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{VarCon01FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildVarCon02CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{VarCon02FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildVarCon03CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{VarCon03FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildVarCon04CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{VarCon04FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildVarCon05CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{VarCon05FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildVarCon06CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{VarCon06FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildVarCon07CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{VarCon07FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildVarCon08CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{VarCon08FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildVarCon09CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{VarCon09FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildPriceCon01CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{PriceCon01FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildPriceCon02CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{PriceCon02FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildPriceCon03CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{PriceCon03FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildPriceCon04CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{PriceCon04FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildPriceCon05CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{PriceCon05FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildPriceCon06CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{PriceCon06FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildPriceCon07CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{PriceCon07FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildPriceCon08CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{PriceCon08FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildPriceCon09CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{PriceCon09FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildPriceCon10CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{PriceCon10FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildPriceCon11CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{PriceCon11FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildPriceCon12CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{PriceCon12FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildPriceCon13CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{PriceCon13FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildStockCon01CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{StockCon01FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildStockCon02CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{StockCon02FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildStockCon03CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{StockCon03FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildStockCon04CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{StockCon04FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildStockCon05CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{StockCon05FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildStockCon06CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{StockCon06FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string BuildStockCon07CommandPreview(int repetitions)
    {
        return $"Repeat {repetitions}x: dotnet test PointOfSale.Tests\\PointOfSale.Tests.csproj --no-build --filter \"{StockCon07FocusedTestFilter}\" -nologo -v minimal";
    }

    private static string TakeTail(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength
            ? value
            : value[^maxLength..];
    }

    // -------------------------------------------------------------------------
    // Request / Response DTOs
    // -------------------------------------------------------------------------

    public sealed class TestTokenRequest
    {
        /// <summary>
        /// Target tenant. When null/omitted, the first active tenant is used.
        /// </summary>
        public Guid? TenantId { get; set; }
    }

    public sealed class SiCon01RunRequest
    {
        public int? Repetitions { get; set; }
    }

    public sealed class SiCon01RunResponse
    {
        [JsonPropertyName("JobId")]
        public Guid JobId { get; set; }

        [JsonPropertyName("Status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("Repetitions")]
        public int Repetitions { get; set; }

        [JsonPropertyName("StartedAtUtc")]
        public DateTime StartedAtUtc { get; set; }

        [JsonPropertyName("CompletedAtUtc")]
        public DateTime? CompletedAtUtc { get; set; }

        [JsonPropertyName("AllPassed")]
        public bool AllPassed { get; set; }

        [JsonPropertyName("Command")]
        public string Command { get; set; } = string.Empty;

        [JsonPropertyName("FailureMessage")]
        public string? FailureMessage { get; set; }

        [JsonPropertyName("Iterations")]
        public SiCon01IterationResponse[] Iterations { get; set; } = [];
    }

    public sealed class SiCon01IterationResponse
    {
        [JsonPropertyName("Iteration")]
        public int Iteration { get; set; }

        [JsonPropertyName("ExitCode")]
        public int ExitCode { get; set; }

        [JsonPropertyName("CompletedAtUtc")]
        public DateTime CompletedAtUtc { get; set; }

        [JsonPropertyName("StdoutTail")]
        public string StdoutTail { get; set; } = string.Empty;

        [JsonPropertyName("StderrTail")]
        public string StderrTail { get; set; } = string.Empty;
    }

    public sealed record TestTokenResponse(
        string Token,
        DateTime ExpiresAt,
        Guid TenantId,
        string TenantName,
        string UserId,
        string Role);

    private sealed class SiCon01RunJob
    {
        public Guid JobId { get; init; }
        public SiCon01RunStatus Status { get; set; }
        public int Repetitions { get; init; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public string Command { get; init; } = string.Empty;
        public string? FailureMessage { get; set; }
        public List<SiCon01IterationResult> Iterations { get; } = [];
    }

    private sealed class SiCon01IterationResult
    {
        public int Iteration { get; init; }
        public int ExitCode { get; init; }
        public DateTime CompletedAtUtc { get; init; }
        public string StdoutTail { get; init; } = string.Empty;
        public string StderrTail { get; init; } = string.Empty;
    }

    private enum SiCon01RunStatus
    {
        Pending,
        Running,
        Completed,
        Failed
    }
}
