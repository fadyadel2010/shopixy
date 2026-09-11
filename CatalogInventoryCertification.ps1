param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$project = "PointOfSale.Tests/PointOfSale.Tests.csproj"

function Invoke-CertificationSuite {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$Filter
    )

    Write-Host "Running Catalog/Inventory gate: $Name"
    & dotnet test $project --no-build --filter $Filter --logger "console;verbosity=minimal"
    if ($LASTEXITCODE -ne 0) {
        throw "Catalog/Inventory gate failed: $Name"
    }
}

if (-not $SkipBuild) {
    & dotnet build PointOfSale.sln --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "Catalog/Inventory solution build failed."
    }
}

Invoke-CertificationSuite "Catalog and inventory authority" (
    "FullyQualifiedName~C2InventoryReliabilitySourceTests|" +
    "FullyQualifiedName~InventoryReservationLifecycleIntegrationTests|" +
    "FullyQualifiedName~InventoryServiceTests|" +
    "FullyQualifiedName~InventoryConsistencyServiceTests|" +
    "FullyQualifiedName~ProductVariantTests|" +
    "FullyQualifiedName~ProductResponseMapperTests|" +
    "FullyQualifiedName~ProductVariantMergeSplitAuthorityTests|" +
    "FullyQualifiedName~WarehouseResolutionServiceTests|" +
    "FullyQualifiedName~WarehouseAuthorityArchitectureCertificationTests|" +
    "FullyQualifiedName~WarehouseOperationalReadinessCertificationTests"
)

Invoke-CertificationSuite "Projection, search, and cache" (
    "FullyQualifiedName~ProductSqlAuthorityProjectionWriterTests|" +
    "FullyQualifiedName~CatalogDependencyProjectionPropagationTests|" +
    "FullyQualifiedName~MongoProjectionConvergenceCertificationTests|" +
    "FullyQualifiedName~ElasticsearchFreshnessSearchConvergenceCertificationTests|" +
    "FullyQualifiedName~EventPropagationProjectionFreshnessCertificationTests|" +
    "FullyQualifiedName~StorefrontSearchReliabilityTests|" +
    "FullyQualifiedName~CommerceHubSearchVisibilityCertificationTests|" +
    "FullyQualifiedName~CommerceHubSearchDiscoveryAuthorityCertificationTests|" +
    "FullyQualifiedName~AffiliateAuthorityFirstPagingTests|" +
    "FullyQualifiedName~ProductProjectionGovernanceTests|" +
    "FullyQualifiedName~CacheCoreAndCleanupArchitectureTests&" +
    "FullyQualifiedName!~HttpCachePolicyIsRegisteredOnceAndControllersCannotOverrideIt"
)

Invoke-CertificationSuite "Catalog to order and tenant isolation" (
    "FullyQualifiedName~OrderCheckoutFieldSnapshotTests|" +
    "FullyQualifiedName~PricingAuthorityConsolidationCertificationTests|" +
    "FullyQualifiedName~AffiliateStorefrontPricingSnapshotTests|" +
    "FullyQualifiedName~CommerceHubFinancialWorkflowCommissionSnapshotTests|" +
    "FullyQualifiedName~StorefrontOrderLineageAuthorityCertificationTests|" +
    "FullyQualifiedName~CategoriesControllerTenantScopeTests|" +
    "FullyQualifiedName~TenantLineageAuthorityTests|" +
    "FullyQualifiedName~StorefrontTenantIsolationTests|" +
    "FullyQualifiedName~CommerceHubVendorVisibilityAuthorityCertificationTests"
)

Invoke-CertificationSuite "Inventory and variant reliability" (
    "FullyQualifiedName~ProductVariantStockMutationSupportIntegrationTests|" +
    "FullyQualifiedName~ProductInventorySqlAuthorityFallbackTests|" +
    "FullyQualifiedName~ProductVariantLifecycleAuthorityTests|" +
    "FullyQualifiedName~ProductVariantCloneImportAuthorityTests|" +
    "FullyQualifiedName~MarketplaceVariantInventoryCertificationTests|" +
    "FullyQualifiedName~CommerceHubCheckoutReservationCertificationTests|" +
    "FullyQualifiedName~CommerceHubCartInventoryCertificationTests|" +
    "FullyQualifiedName~CheckoutConcurrencyHardeningSourceTests"
)

$previousRealPostgresRequirement = $env:REQUIRE_REAL_POSTGRES_TESTS
try {
    $env:REQUIRE_REAL_POSTGRES_TESTS = "1"
    Invoke-CertificationSuite "Real PostgreSQL reservation concurrency" (
        "FullyQualifiedName~InventoryReservationConcurrencyIntegrationTests"
    )
}
finally {
    $env:REQUIRE_REAL_POSTGRES_TESTS = $previousRealPostgresRequirement
}

Write-Host "Catalog/Inventory certification gates passed."
