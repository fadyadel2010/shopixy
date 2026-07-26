# PASS M1-B CommerceHub Vendor Eligibility Authority Certification

## Eligibility Findings

Vendor eligibility presentation is derivative from the current backend POS session vendor scope, backend governance capability composition, current participation/finance/payment DTO evidence, and protected endpoint authorization. Frontend state, storage, route state, history, caches, and mutation responses are not accepted as eligibility truth.

## Participation Findings

Participation appears only through current backend-issued CommerceHub navigation capabilities and current protected endpoint reads. A vendor without backend approval/governance capability cannot retain catalog, order, finance, or payment presentation through sidebar state, direct routes, cached rows, or mutation responses.

## Suspension Findings

Suspension and restricted-order presentation derive from backend finance debt/governance evidence such as `isSuspendedFromNewOrders`; malformed suspension evidence fails closed. Catalog, payment, and payout-method controls now additionally require accepted current session eligibility before presentation or execution.

## Debt Findings

Debt state remains backend-owned. Payment submission requires both `commercehub.payments.submit` from the current backend session and current payment-dashboard `canSubmitPayment=true`; either missing source fails closed.

## Capability-Gating Findings

Catalog publish/update/unpublish/bulk actions require accepted `canManageCatalog`. Debt-payment submission requires accepted `canSubmitPayment`. Payout-method changes require current finance capability. Sidebar and direct routes continue to use backend-issued navigation capabilities.

## Bootstrap Findings

Bootstrap still re-enters `/api/auth/session`, validates the current POS vendor session DTO, purges vendor-scoped query authority on loss, and withholds route presentation during lifecycle revalidation.

## Cache Findings

CommerceHub finance, earnings, payout-method, payments, and settings reads remain current tenant/user scoped and pass through current vendor identity validation. Cache contents cannot restore action eligibility because action controls re-check current backend session capabilities and DTO evidence.

## Transition Findings

Actor, tenant, role, login, logout, and backend authority-loss transitions purge POS query/runtime cache state. Eligibility presentation carries current backend tenant/user identity and cannot cross scopes.

## Failure Findings

401/403 protected CommerceHub vendor failures dispatch scoped vendor authority loss and force backend session revalidation. Malformed capability arrays, duplicate capabilities, malformed DTO booleans, failed reads, and missing session scope fail closed.

## Adversarial Findings

Eligibility replay, governance replay, debt replay, suspension replay, cache replay, storage replay, history replay, mutation replay, actor replay, and tenant replay cannot create accepted vendor eligibility presentation on inspected paths.

## Runtime Hardening Performed

- Added `deriveCommerceHubVendorEligibilityPresentation` to compose frontend eligibility presentation only from a validated current backend POS vendor session and optional current backend DTO evidence.
- Routed CommerceHub catalog publish/unpublish/bulk action presentation and handlers through accepted catalog eligibility.
- Routed CommerceHub debt-payment submission through both backend session submit capability and current dashboard `canSubmitPayment` evidence.
- Routed payout-method creation through current finance eligibility.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/auth/posVendorSessionAuthority.js`
- `PointOfSale.Web/src/pages/CommerceHubSettings.jsx`
- `PointOfSale.Web/src/pages/CommerceHubPayments.jsx`
- `PointOfSale.Web/src/pages/CommerceHubEarnings.jsx`

## Regression Freezes Added

- `PointOfSale.Web/src/auth/posVendorSessionAuthority.test.ts`
- `PointOfSale.Tests/Architecture/CommerceHubFrontendAuthorityCertificationTests.cs`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Explicit Answers

- Who owns vendor eligibility authority? Backend vendor, governance, participation, debt, suspension, blocking, capability, and tenant authorities own eligibility truth.
- Can frontend fabricate eligibility? No.
- Can participation appear without approval? No.
- Can suspended vendors operate? No inspected eligible action remains frontend-enabled from stale state; backend endpoint authorization remains decisive.
- Can blocked vendors operate? No.
- Can debt-blocked vendors continue selling? No; order/catalog/payment capabilities are backend-composed and debt-payment submission requires current backend evidence.
- Can stale eligibility survive refresh? No.
- Can cache restore eligibility? No.
- Can tenant leakage occur? No inspected runtime path accepts cross-tenant eligibility evidence.
- Were runtime changes required? Yes.
- Is Vendor Eligibility Authority production-certifiable? Yes, with residual deployed-browser chaos evidence retained for multi-tab revocation, debt/suspension transitions, malformed gateway DTOs, BFCache, offline/reconnect, and delayed cross-tab signals.
