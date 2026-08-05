namespace PointOfSale.Tests.Architecture;

public sealed class StoreBuilderLayoutAuthorityCertificationTests
{
    [Fact]
    public void BackendPageLayoutMutation_MustCanonicalizeLayoutSettingsBeforePersistence()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/Admin/AdminStoreConfigService.cs");
        var builder = ReadRepoFile("PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx");
        var store = ReadRepoFile("PointOfSale.Web/src/modules/storefront/store/builderStore.js");

        Assert.Contains("ReservedBuilderLayoutAuthorityKeys", service, StringComparison.Ordinal);
        Assert.Contains("NormalizeSectionSettings(sectionObject, sectionType)", service, StringComparison.Ordinal);
        Assert.Contains("StorefrontSectionContracts.TryValidateSettingsStrict", service, StringComparison.Ordinal);
        Assert.Contains("ReservedBuilderLayoutAuthorityKeys.Contains(setting.Key)", service, StringComparison.Ordinal);
        Assert.DoesNotContain("NormalizeSectionSettingValue", service, StringComparison.Ordinal);
        Assert.Contains("pageContent[\"draft\"] = NormalizeBuilderPageComposition(pageConfig, pageContent)", service, StringComparison.Ordinal);
        Assert.Contains("templateContent[\"draft\"] = NormalizeBuilderPageComposition(templateConfig, templateContent, authoritativeTemplateId: templateId)", service, StringComparison.Ordinal);
        Assert.Contains("Property(x => x.RowVersion)", service, StringComparison.Ordinal);
        Assert.Contains("OriginalValue = expectedRowVersion.Value", service, StringComparison.Ordinal);

        Assert.Contains("const rowVersion = requireWorkspaceAdmission()", builder, StringComparison.Ordinal);
        Assert.Contains("storeConfigService.updatePageLayout(", builder, StringComparison.Ordinal);
        Assert.Contains("admitWorkspaceConfig(response)", builder, StringComparison.Ordinal);
        Assert.Contains("admitSections(", builder, StringComparison.Ordinal);
        Assert.Contains("admitSections: (sections, options = {}) =>", store, StringComparison.Ordinal);
    }

    [Fact]
    public void LayoutAuthorityDocumentation_MustBeRegisteredInAuthorityContext()
    {
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");
        var findings = ReadRepoFile("docs/architecture/store-builder-layout-authority-certification-findings.md");

        Assert.Contains("PASS SF-ADM2.4 Builder Layout Authority Certification", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS SF-ADM2.4 Builder Layout Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS SF-ADM2.4 builder layout runtime certifiable", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS SF-ADM2.4 Builder Layout Authority Hardening", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS SF-ADM2.4 Builder Layout Authority Freeze", codexRules, StringComparison.Ordinal);
        Assert.Contains("Layout Authority begins at backend page-layout mutation validation", findings, StringComparison.Ordinal);
        Assert.Contains("Is Builder Layout production-certifiable? Yes.", findings, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
