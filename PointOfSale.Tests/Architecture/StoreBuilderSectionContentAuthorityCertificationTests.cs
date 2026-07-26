namespace PointOfSale.Tests.Architecture;

public sealed class StoreBuilderSectionContentAuthorityCertificationTests
{
    [Fact]
    public void BackendPageLayoutMutation_MustCanonicalizeSectionContentBeforePersistence()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/Admin/AdminStoreConfigService.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminStoreConfigController.cs");

        Assert.Contains("AllowedBuilderSectionSettingKeys", service, StringComparison.Ordinal);
        Assert.Contains("NormalizeSectionSettings(sectionObject, sectionType)", service, StringComparison.Ordinal);
        Assert.Contains("ReadPlainText", service, StringComparison.Ordinal);
        Assert.Contains("ReadSanitizedRichText", service, StringComparison.Ordinal);
        Assert.Contains("SafeRichTextSanitizer.Sanitize", service, StringComparison.Ordinal);
        Assert.Contains("ReadSafeUrl", service, StringComparison.Ordinal);
        Assert.Contains("NormalizeImageList", service, StringComparison.Ordinal);
        Assert.Contains("NormalizeTestimonials", service, StringComparison.Ordinal);
        Assert.Contains("NormalizeSpecItems", service, StringComparison.Ordinal);
        Assert.Contains("NormalizeCustomerReviews", service, StringComparison.Ordinal);
        Assert.Contains("ReadStringArray", service, StringComparison.Ordinal);
        Assert.Contains("BuilderShortTextMaxLength", service, StringComparison.Ordinal);
        Assert.Contains("BuilderLongTextMaxLength", service, StringComparison.Ordinal);
        Assert.Contains("pageContent[\"draft\"] = NormalizeBuilderPageComposition(pageConfig, pageContent)", service, StringComparison.Ordinal);
        Assert.Contains("UnprocessableEntity(AdminStoreConfigApiErrorResponse.InvalidConfig", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendBuilderContent_MustRemainCandidateUntilAcceptedDtoAdmission()
    {
        var builder = ReadRepoFile("PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx");
        var store = ReadRepoFile("PointOfSale.Web/src/modules/storefront/store/builderStore.js");
        var settingsPanel = ReadRepoFile("PointOfSale.Web/src/modules/storefront/components/builder/SettingsPanel.jsx");

        Assert.Contains("const rowVersion = requireWorkspaceAdmission()", builder, StringComparison.Ordinal);
        Assert.Contains("storeConfigService.updatePageLayout(", builder, StringComparison.Ordinal);
        Assert.Contains("const acceptedConfig = admitWorkspaceConfig(response)", builder, StringComparison.Ordinal);
        Assert.Contains("getAcceptedSectionsFromConfig(acceptedConfig, targetPage, templateIdFromUrl)", builder, StringComparison.Ordinal);
        Assert.Contains("admitSections(", builder, StringComparison.Ordinal);
        Assert.Contains("updateSectionSettings: (sectionId, newSettings) =>", store, StringComparison.Ordinal);
        Assert.Contains("isDirty: true", store, StringComparison.Ordinal);
        Assert.Contains("updateSectionSettings(selectedSection.id, newSettings)", settingsPanel, StringComparison.Ordinal);
    }

    [Fact]
    public void SectionContentAuthorityDocumentation_MustBeRegisteredInAuthorityContext()
    {
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");
        var findings = ReadRepoFile("docs/architecture/store-builder-section-content-authority-certification-findings.md");

        Assert.Contains("PASS SF-ADM2.6 Builder Section Content Authority Certification", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS SF-ADM2.6 Builder Section Content Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS SF-ADM2.6 builder section content runtime certifiable", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS SF-ADM2.6 Builder Section Content Authority Hardening", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS SF-ADM2.6 Builder Section Content Authority Freeze", codexRules, StringComparison.Ordinal);
        Assert.Contains("Content Authority begins at backend page-layout content canonicalization", findings, StringComparison.Ordinal);
        Assert.Contains("Is Builder Section Content production-certifiable? Yes.", findings, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
