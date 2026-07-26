namespace PointOfSale.Tests.Architecture;

public sealed class StoreBuilderSectionCompositionAuthorityCertificationTests
{
    [Fact]
    public void BackendPageLayoutMutation_MustCanonicalizeSectionCompositionBeforePersistence()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/Admin/AdminStoreConfigService.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminStoreConfigController.cs");

        Assert.Contains("NormalizeBuilderPageComposition(pageConfig, pageContent)", service, StringComparison.Ordinal);
        Assert.Contains("NormalizeBuilderPageComposition(templateConfig, templateContent, authoritativeTemplateId: templateId)", service, StringComparison.Ordinal);
        Assert.Contains("AllowedBuilderSectionTypes", service, StringComparison.Ordinal);
        Assert.Contains("ReadRequiredSectionType", service, StringComparison.Ordinal);
        Assert.Contains("ExtractAcceptedSectionIds(currentContent)", service, StringComparison.Ordinal);
        Assert.Contains("IsReusableAcceptedSectionId", service, StringComparison.Ordinal);
        Assert.Contains("GenerateBackendSectionId", service, StringComparison.Ordinal);
        Assert.Contains("[\"order\"] = normalizedSections.Count", service, StringComparison.Ordinal);
        Assert.Contains("UnprocessableEntity(AdminStoreConfigApiErrorResponse.InvalidConfig", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendBuilderStore_MustHydrateAcceptedSectionsFromBackendDtoOnly()
    {
        var builder = ReadRepoFile("PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx");
        var store = ReadRepoFile("PointOfSale.Web/src/modules/storefront/store/builderStore.js");

        Assert.Contains("admitSections: (sections, options = {}) =>", store, StringComparison.Ordinal);
        Assert.Contains("getAcceptedSectionsFromConfig", builder, StringComparison.Ordinal);
        Assert.Contains("admitSections(acceptedSections)", builder, StringComparison.Ordinal);
        Assert.Contains("getAcceptedSectionsFromConfig(acceptedConfig, targetPage, templateIdFromUrl)", builder, StringComparison.Ordinal);
        Assert.Contains("resetHistory: false", builder, StringComparison.Ordinal);
        Assert.Contains("const acceptedConfig = admitWorkspaceConfig(response)", builder, StringComparison.Ordinal);
        Assert.Contains("markClean()", builder, StringComparison.Ordinal);
    }

    [Fact]
    public void SectionCompositionDocumentation_MustBeRegisteredInAuthorityContext()
    {
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");
        var findings = ReadRepoFile("docs/architecture/store-builder-section-composition-authority-certification-findings.md");

        Assert.Contains("PASS SF-ADM2.3 Builder Section Composition Authority Certification", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS SF-ADM2.3 Builder Section Composition Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS SF-ADM2.3 builder section composition runtime certifiable", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS SF-ADM2.3 Builder Section Composition Authority Hardening", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS SF-ADM2.3 Builder Section Composition Authority Freeze", codexRules, StringComparison.Ordinal);
        Assert.Contains("Section Authority begins at backend page-layout mutation canonicalization", findings, StringComparison.Ordinal);
        Assert.Contains("Is Builder Section Composition production-certifiable? Yes.", findings, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
