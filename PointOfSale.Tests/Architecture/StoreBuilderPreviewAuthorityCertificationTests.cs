namespace PointOfSale.Tests.Architecture;

public sealed class StoreBuilderPreviewAuthorityCertificationTests
{
    [Fact]
    public void BuilderPreviewFrontend_MustSaveDraftThenAdmitBackendPreviewDtoOnly()
    {
        var builder = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx");
        var previewModal = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/components/builder/PreviewModal.jsx");
        var service = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/services/storeConfigService.js");

        Assert.Contains("const result = await persistPageLayout(sections)", builder, StringComparison.Ordinal);
        Assert.Contains("if (!result.saved)", builder, StringComparison.Ordinal);
        Assert.Contains("storeConfigService.getPreviewConfig({ signal: controller.signal })", builder, StringComparison.Ordinal);
        Assert.Contains("deriveCurrentVendorScopedDto(", builder, StringComparison.Ordinal);
        Assert.Contains("Current backend storefront preview authority is unavailable.", builder, StringComparison.Ordinal);
        Assert.Contains("Draft preview could not be loaded. No live content was shown.", builder, StringComparison.Ordinal);

        Assert.Contains("Authenticated draft preview that renders backend preview data locally.", previewModal, StringComparison.Ordinal);
        Assert.Contains("This preview uses authenticated draft data only and will never fall back to live content.", previewModal, StringComparison.Ordinal);
        Assert.Contains("Live content will not be substituted automatically.", previewModal, StringComparison.Ordinal);
        Assert.Contains("setConfig(previousConfigRef.current)", previewModal, StringComparison.Ordinal);

        Assert.Contains("async getPreviewConfig(options = {})", service, StringComparison.Ordinal);
        Assert.Contains("api.get('/storefront/config/preview', withNoStoreHeaders({", service, StringComparison.Ordinal);
        Assert.Contains("params: { preview: true }", service, StringComparison.Ordinal);
    }

    [Fact]
    public void BuilderPreviewBackend_MustExposeAuthenticatedTenantBoundDraftOnlyEndpoint()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminStoreConfigController.cs");
        var adminService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/AdminStoreConfigService.cs");
        var previewService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontPreviewService.cs");
        var mapper = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontDraftConfigMapper.cs");
        var dto = CertificationSource.ReadRepoFile("PointOfSale.API/DTOs/Storefront/StorefrontStoreConfigDtos.cs");

        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"/api/storefront/config/preview\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]", controller, StringComparison.Ordinal);
        Assert.Contains("ValidateTenantHeader(traceId)", controller, StringComparison.Ordinal);
        Assert.Contains("GetStoreConfigPreviewAsync(", controller, StringComparison.Ordinal);

        Assert.Contains("_previewService.GetConfigPreviewAsync(tenantId, cancellationToken)", adminService, StringComparison.Ordinal);
        Assert.Contains("GetConfigPreview (DRAFT)", previewService, StringComparison.Ordinal);
        Assert.Contains(".FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken)", previewService, StringComparison.Ordinal);
        Assert.Contains("StorefrontDraftConfigMapper.Map(", previewService, StringComparison.Ordinal);
        Assert.Contains("useDraftContent: true", mapper, StringComparison.Ordinal);
        Assert.Contains("var selected = useDraftContent ? draft : live;", mapper, StringComparison.Ordinal);
        Assert.Contains("public Guid TenantId { get; set; }", dto, StringComparison.Ordinal);
        Assert.Contains("TenantId = config.TenantId", mapper, StringComparison.Ordinal);
    }

    [Fact]
    public void BuilderPreviewArtifacts_MustRecordSfAdm28Truth()
    {
        Assert.Contains(
            "PASS SF-ADM2.8 Builder Preview Authority Certification",
            CertificationSource.ReadRepoFile("docs/architecture/store-builder-preview-authority-certification-findings.md"),
            StringComparison.Ordinal);

        foreach (var path in new[]
                 {
                     ".ai-context/PROJECT_CONTEXT.md",
                     ".ai-context/ARCHITECTURE_TRUTH.md",
                     ".ai-context/KNOWN_RISKS.md",
                     ".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md",
                     ".ai-context/CODEX_RULES.md"
                 })
        {
            Assert.Contains("PASS SF-ADM2.8", CertificationSource.ReadRepoFile(path), StringComparison.Ordinal);
        }
    }
}
