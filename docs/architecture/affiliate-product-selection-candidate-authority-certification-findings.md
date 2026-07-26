# PASS AF-PA4.2 Affiliate Product Selection Candidate Authority Certification

Date: 2026-06-26

## Scope

This pass certifies Affiliate Product Selection candidate lifecycle and selection authority only. The implemented runtime does not expose a separate selected-products table; affiliate product selection is represented by backend-owned `AffiliatePromotionLink` rows that bind `AffiliateUserId`, `TenantId` (store), and `ProductId`. Promotion link generation, attribution, commissions, payout, and order attribution behavior were not reviewed beyond confirming they are not frontend selection authority.

## Selection Findings

- Frontend discovery cards, drawers, buttons, modal state, copy/share state, URL filters, browser history, and temporary selected product IDs collect selection intent only.
- A product is selected only after `POST /api/affiliate/promotion-links` accepts current backend authority and persists or reactivates the backend relationship row.
- Runtime hardening added explicit Selection Admission before creation/reactivation and before list/detail presentation.
- Failed, timed-out, offline, stale, or malformed frontend selection intent cannot create accepted selection presentation.

## Relationship Findings

- Backend SQL owns relationship identity through `AffiliatePromotionLink`.
- Relationship identity is `(AffiliateUserId, TenantId, ProductId)`.
- `UQ_AffiliatePromotionLinks_User_Store_Product` keeps duplicate active relationships database-impossible for the same affiliate, store, and product.
- Re-selecting an existing inactive relationship re-enters current backend selection admission before `existingLink.Activate()`.

## Candidate Findings

- Frontend candidates are product card clicks, drawer open state, modal state, pending link generation, and local copy/share feedback.
- Candidate state is not committed locally and is not persisted in local storage.
- The selected-products presentation boundary is backend response admission from `/affiliate/promotion-links`; local product DTOs cannot become selected-product identity.

## Lifecycle Findings

- Refresh, login, logout, another tab, browser back/history, and BFCache re-enter affiliate session/profile authority and then `/affiliate/promotion-links` for current selection presentation.
- If POS Admin disables affiliate on Product A, changes its affiliate category to inactive, archives it, hides it, deletes it, removes price, or removes stock without backorder allowance, Product A no longer passes selection admission and disappears from selected-products presentation.
- Removed/deactivated links do not remain visibly selected after the next backend read; DELETE is backend-owned and followed by a frontend refetch.

## Cache Findings

- React state, drawer/modal state, copied state, browser history, browser cache, and local/session storage cannot fabricate selected products.
- `LinksTab` reads current backend links and copy/display uses backend-issued `promotionUrl` only.
- Runtime hardening makes backend list/detail mapping return `null` for stale or ineligible relationships, so cache/replay cannot restore stale selected-product presentation.

## Failure Findings

- `400`, `404`, `409`-like duplicate races, `422`, `500`, timeout, and offline selection failures do not become accepted selection because frontend only presents a backend-returned promotion link.
- `401`/`403` remain subordinate to `AffiliateOnly` session authority and cannot be repaired by frontend state.
- Failed removal does not remove backend authority locally; `LinksTab` refetches after successful DELETE and silently leaves backend truth decisive on failure.

## Adversarial Findings

- Replay selection and duplicate click hit the backend idempotent existing-link path plus database uniqueness.
- Replay remove is scoped by current authenticated `AffiliateUserId` and link id.
- Modified DTOs can propose `TenantId` and `ProductId` only; backend validates product store ownership, current product lifecycle, enabled policy, active affiliate category, price, stock, and current affiliate identity before acceptance.
- Modified selected list, React state, or cached link rows cannot create backend `AffiliatePromotionLink` rows or pass read admission after eligibility loss.

## Selection Admission Review

Pipeline:

Eligible Product

Affiliate Selects

Backend Validation

Relationship Created or Reactivated

Current Selection Read

Selection Admission

My Products / My Links

Selection Admission occurs in `AffiliatePromotionLinksController.IsProductCurrentlyAffiliateSelectableAsync` and is applied by:

- `Create` before creating or reactivating an `AffiliatePromotionLink`.
- `MapToResponseAsync(..., requireCurrentSelectionAdmission: true, ...)` before list/detail responses include a selected product.

Admission requires current backend SQL product existence, non-deleted product, active product, `PublishStatus.Published`, storefront visibility, enabled non-deleted affiliate policy, active non-deleted affiliate category, positive affiliate price, and available affiliate inventory or backorder allowance.

## Runtime Hardening Performed

- Added current backend selection admission to `AffiliatePromotionLinksController.Create`.
- Added current backend selection admission to existing-link reactivation.
- Added current backend selection admission to promotion-link list and detail response mapping.
- Added price and inventory admission helpers mirroring the certified affiliate eligibility lifecycle predicate.
- Stale/ineligible links are withheld from selected-products presentation instead of being returned as accepted selection.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/AffiliatePromotionLinksController.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/AffiliateProductSelectionCandidateAuthorityCertificationTests.cs`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Explicit Certification Questions

- Who owns Affiliate Product Selection? Backend SQL and `AffiliatePromotionLinksController` selection admission own it.
- Can frontend fabricate selected products? No. Frontend can submit intent only; selected products appear only from backend-admitted relationship rows.
- Can duplicate selections occur? No for the same affiliate, store, and product; uniqueness plus idempotent reactivation prevents duplicates.
- Can removed products survive eligibility loss? No in selected-products presentation after hardening; stale relationships fail current selection admission.
- Can stale selections survive logout? No. Logout clears affiliate session authority; login re-enters backend profile/session and selection reads.
- Can cache become selection authority? No. Cache/state/history can only trigger display/refetch; backend read admission decides.
- Where does Selection Admission occur? `AffiliatePromotionLinksController.IsProductCurrentlyAffiliateSelectableAsync`, called before create/reactivate and list/detail response admission.
- Were runtime changes required? Yes.
- Is Affiliate Product Selection production-certifiable? Yes, for the inspected runtime scope after AF-PA4.2 hardening.

## Certification Result

Affiliate Product Selection is production-certifiable for the inspected runtime scope. Backend owns affiliate/product relationships, selection identity, persistence, validation, eligibility validation, and relationship lifecycle. Frontend owns only selection intent, loading, presentation, temporary state, animation, and navigation.
