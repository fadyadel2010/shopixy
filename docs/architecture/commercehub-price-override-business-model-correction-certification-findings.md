# POS-ADMIN-CERTIFICATION-01 PASS 13.5B CommerceHub Price Override Business Model Correction

## Decision

CommerceHub is a merchant-controlled marketplace. Tenant merchants own CommerceHub price override decisions. Marketplace governance owns visibility and marketplace integrity, not pricing decisions. Overrides remain disabled and no runtime pricing activation occurs.

## 1. Business Model Corrections

PASS 13.5 incorrectly assigned override authority to marketplace governance and introduced approval-centric assumptions. PASS 13.4, PASS 13.5, PASS 13.6, architecture truth, hardening guidance, Codex rules, known risks, and regression freezes now record the merchant-owned model.

## 2. Pricing Ownership Findings

CommerceHub price authority owner: tenant merchant.

CommerceHub visibility authority owner: marketplace governance.

Merchant pricing remains subordinate to visibility authority only. A suppressed listing is not publicly visible, so its price is irrelevant while suppression remains active.

## 3. Governance Boundary Findings

Marketplace governance may hide listings, suppress listings, suspend vendors, suspend tenants, emergency-disable override runtime, audit override activity, preserve marketplace integrity, and perform non-destructive rollback.

Marketplace governance may not approve prices, reject prices, create prices, or edit merchant prices. Audit is oversight and must never become approval.

## 4. Permission Model Corrections

Future merchant permission:

- `CommerceHubPriceOverrideManage`

Future marketplace permissions:

- `CommerceHubPriceOverrideAudit`
- `CommerceHubPriceOverrideEmergencyDisable`

`CommerceHubPriceOverrideApprove` is not part of the intended model.

## 5. Workflow Corrections

Marketplace approval workflow is prohibited. The future merchant-owned lifecycle is optional `Draft`, then `Saved`, `Updated`, or `Removed`. Stale merchant mutation replay must fail closed by mutation version.

## 6. Rollout Findings

The PASS 13.5 rollout design survives the ownership correction: default-off global kill switch, default-empty tenant allowlist, optional cohorts, staged rollout, immediate fallback to `ProductVariant.EffectiveOnlinePrice`, and non-destructive rollback remain valid.

## 7. Audit Findings

Marketplace governance may audit immutable override activity, rollout state, parity evidence, emergency disable, and rollback evidence. Audit does not confer pricing decision authority.

## 8. Replay And Recovery Findings

Ownership correction does not change replay, recovery, reindex, projection, search, or resolver behavior. Current SQL authority still wins. Removed, disabled, stale, or unproven overrides must not resurrect through derivative state.

## 9. Runtime Hardening Performed

No pricing runtime was activated. `CommerceHubPriceAuthority.OverridesEnabled` remains `false`; listing writes remain rejected, cleared, and SQL-constrained to null; derivative override state remains cleared.

## 10. Regression Freeze Added

`CommerceHubPriceOverrideBusinessModelCertificationTests` freezes merchant pricing ownership, governance visibility ownership, allowed governance controls, prohibited pricing governance actions, corrected permissions, no approval workflow, preserved rollout controls, SQL-first recovery, and disabled runtime locks.

## 11. Architecture Updates

`.ai-context/PROJECT_CONTEXT.md`, `.ai-context/ARCHITECTURE_TRUTH.md`, `.ai-context/KNOWN_RISKS.md`, `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`, `.ai-context/CODEX_RULES.md`, and PASS 13.4 through PASS 13.6 findings now record the corrected ownership model.

## 12. Remaining Debt

Overrides remain inactive. Merchant mutation API/UI, active persistence semantics, immutable audit events, tenant-safe runtime flags, migration preflight, parity monitoring, dashboards, alerting, support tooling, rollout drills, rollback drills, and active-state cart/checkout/projection/search/replay/recovery certification remain future work.

## Explicit Answers

1. Who owns CommerceHub pricing? Tenant merchants.
2. Who owns CommerceHub visibility? Marketplace governance.
3. Can marketplace governance approve prices? No.
4. Can marketplace governance reject prices? No.
5. Can marketplace governance edit prices? No.
6. Can marketplace governance suppress listings? Yes.
7. Is approval workflow required? No. It is prohibited.
8. What permissions remain necessary? `CommerceHubPriceOverrideManage`, `CommerceHubPriceOverrideAudit`, and `CommerceHubPriceOverrideEmergencyDisable`.
9. Is the platform aligned with the intended CommerceHub business model? Yes. Runtime overrides remain disabled pending future activation implementation and certification.
