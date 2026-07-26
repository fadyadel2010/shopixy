namespace PointOfSale.Tests.Architecture;

public sealed class AdminSmartOfferRuleEligibilityAuthorityCertificationTests
{
    [Fact]
    public void AdminSmartOfferRuleAdmission_MustRemainBackendOwnedAndRejectUnsupportedEligibilityAuthority()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminSmartOffersController.cs");
        var dto = CertificationSource.ReadRepoFile("PointOfSale.API/DTOs/SmartOffers/SmartOfferDtos.cs");
        var entity = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/SmartOffer.cs");
        var page = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/SmartOffersPage.jsx");
        var editor = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/SmartOfferEditor.jsx");
        var api = CertificationSource.ReadRepoFile("PointOfSale.Web/src/services/adminSmartOffersApi.js");
        var admission = CertificationSource.ReadRepoFile("PointOfSale.Web/src/auth/adminSmartOfferReadAuthority.js");

        Assert.Contains("ValidateRequest(request)", controller, StringComparison.Ordinal);
        Assert.Contains("entity.Validate()", controller, StringComparison.Ordinal);
        Assert.Contains("LoadSmartOfferForLifecycleMutationAsync(tenantId, id, cancellationToken)", controller, StringComparison.Ordinal);
        Assert.Contains("ParseRequiredSmartOfferIfMatch", controller, StringComparison.Ordinal);
        Assert.Contains("currentRowVersion != expectedRowVersion.RowVersion", controller, StringComparison.Ordinal);
        Assert.Contains("_dbContext.SmartOffers.Add(entity)", controller, StringComparison.Ordinal);
        Assert.Contains("await _dbContext.SaveChangesAsync(cancellationToken)", controller, StringComparison.Ordinal);
        Assert.Contains("return Ok(MapToAdminDto(entity, currency, coupon))", controller, StringComparison.Ordinal);

        Assert.Contains("coupon.ApplicableProductIds = null", controller, StringComparison.Ordinal);
        Assert.Contains("coupon.ApplicableCategoryIds = null", controller, StringComparison.Ordinal);
        Assert.Contains("coupon.ExcludedProductIds = null", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("_dbContext.Products", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("_dbContext.Categories", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductIds", dto, StringComparison.Ordinal);
        Assert.DoesNotContain("CategoryIds", dto, StringComparison.Ordinal);
        Assert.DoesNotContain("CustomerIds", dto, StringComparison.Ordinal);
        Assert.DoesNotContain("CustomerGroupIds", dto, StringComparison.Ordinal);
        Assert.DoesNotContain("StoreIds", dto, StringComparison.Ordinal);
        Assert.DoesNotContain("ChannelIds", dto, StringComparison.Ordinal);
        Assert.DoesNotContain("RuleJson", dto, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductIds", entity, StringComparison.Ordinal);
        Assert.DoesNotContain("CategoryIds", entity, StringComparison.Ordinal);
        Assert.DoesNotContain("CustomerIds", entity, StringComparison.Ordinal);
        Assert.DoesNotContain("StoreIds", entity, StringComparison.Ordinal);
        Assert.DoesNotContain("ChannelIds", entity, StringComparison.Ordinal);
        Assert.DoesNotContain("RuleJson", entity, StringComparison.Ordinal);

        Assert.Contains("onSave({", editor, StringComparison.Ordinal);
        Assert.Contains("createAdminSmartOffer(payload, user)", page, StringComparison.Ordinal);
        Assert.Contains("updateAdminSmartOffer(editingOffer, payload, user)", page, StringComparison.Ordinal);
        Assert.Contains("requireAdminSmartOfferReadAuthority(response.data, currentUser)", api, StringComparison.Ordinal);
        Assert.Contains("'If-Match': `\"${rowVersion}\"`", api, StringComparison.Ordinal);
        Assert.Contains("Smart Offer read authority rejected foreign or route-mismatched DTO evidence", admission, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminSmartOfferRuleEligibilityAuthorityDocumentation_MustBeFrozen()
    {
        var findings = CertificationSource.ReadRepoFile("docs/architecture/admin-smart-offer-rule-eligibility-authority-certification-findings.md");
        var projectContext = CertificationSource.ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = CertificationSource.ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = CertificationSource.ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = CertificationSource.ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = CertificationSource.ReadRepoFile(".ai-context/CODEX_RULES.md");

        Assert.Contains("PASS SF-SMO.RULE Admin Smart Offer Rule & Eligibility Authority", findings, StringComparison.Ordinal);
        Assert.Contains("PASS SF-SMO.RULE Admin Smart Offer Rule & Eligibility Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS SF-SMO.RULE Admin Smart Offer Rule & Eligibility Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS SF-SMO.RULE admin smart offer rule eligibility runtime certifiable", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS SF-SMO.RULE Admin Smart Offer Rule & Eligibility Authority Hardening", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS SF-SMO.RULE Admin Smart Offer Rule & Eligibility Authority Freeze", codexRules, StringComparison.Ordinal);
    }
}

