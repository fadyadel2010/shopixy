# POS-ADMIN-CERTIFICATION-01 PASS 13.5 CommerceHub Price Override Governance, Feature Flag & Rollout

## Decision

CommerceHub price overrides remain disabled. This pass freezes the required future operational model only. It does not activate overrides, relax SQL null constraints, add API/UI mutations, or change customer-visible pricing.

## 1. Governance Findings

Tenant merchants own CommerceHub price override decisions. A tenant POS `Owner` or explicitly delegated pricing operator may create, update, or remove an override for its own tenant only. Marketplace governance owns visibility, suppression, vendor suspension, tenant suspension, emergency disable, audit, and rollback authority. Marketplace governance may not approve, reject, create, or edit merchant prices.

No actor may activate override runtime independently of the rollout controls.

## 2. Permission Model Findings

Future activation must introduce explicit least-privilege permissions:

- `CommerceHubPriceOverrideManage`: create, update, and remove overrides within trusted merchant tenant scope.
- `CommerceHubPriceOverrideAudit`: read immutable override history, rollout state, parity status, and rollback evidence.
- `CommerceHubPriceOverrideEmergencyDisable`: globally disable or tenant-disable runtime use without deleting records.

Existing broad POS roles or Super Admin identity alone are insufficient substitutes.

## 3. Merchant Workflow Findings

Marketplace approval is prohibited. The future merchant-owned workflow is optional `Draft`, then `Saved`, `Updated`, or `Removed`. Persisted records require merchant actor, timestamps, reason, tenant, listing, prior value, proposed value, mutation version, and immutable event evidence. Stale mutation-version replay must fail closed.

## 4. Feature Flag Findings

The future runtime flag must be globally disable-able, tenant-safe, rollout-safe, and emergency-safe:

- a global emergency kill switch defaults off and wins over every other setting;
- a tenant allowlist defaults empty;
- an optional percentage or cohort rollout applies only inside the allowlist;
- resolver use requires a current-version merchant override plus all flags;
- flag disable falls back immediately to `ProductVariant.EffectiveOnlinePrice` while preserving override records.

`CommerceHubPriceAuthority.OverridesEnabled = false` remains the current safety lock, not the future rollout implementation.

## 5. Rollout Findings

Future activation must be incremental:

- `Stage 0`: resolver-only disabled state and monitoring.
- `Stage 1`: internal test tenant.
- `Stage 2`: limited allowlisted tenants with parity alerts and rollback drills.
- `Stage 3`: broader cohorts only after checkout, cart, projection, search, replay, and recovery evidence remains green.
- `Stage 4`: general availability with the emergency kill switch retained.

No big-bang deployment is acceptable.

## 6. Rollback Findings

Rollback is non-destructive: flip the global kill switch off, stop new mutations, preserve merchant override and audit rows, verify resolver fallback to `ProductVariant.EffectiveOnlinePrice`, reproject MongoDB and Elasticsearch from SQL authority, reconcile parity metrics, and record an immutable rollback event. SQL records must not be deleted to roll back runtime use.

## 7. Audit & Event Findings

Attribution columns from PASS 13.4 are useful but insufficient. Future activation requires immutable events:

- `CommerceHubPriceOverrideSaved`
- `CommerceHubPriceOverrideUpdated`
- `CommerceHubPriceOverrideRemoved`
- `CommerceHubPriceOverrideRolloutChanged`
- `CommerceHubPriceOverrideEmergencyDisabled`
- `CommerceHubPriceOverrideRollbackCompleted`

Each event must include actor, tenant, listing, correlation id, mutation version, reason, previous/proposed value, timestamp, and rollout scope where applicable.

## 8. Monitoring Findings

Minimum future visibility: active override count by tenant, stale mutation attempts, override-vs-base drift, SQL-vs-Mongo-vs-Elasticsearch parity, resolver fallback counts, flag changes, rollback events, projection failures, checkout `PRICE_CHANGED` rate, and override-related checkout failures. Alerts must cover unexpected activation, parity drift, stale replay, and kill-switch failure.

## 9. Checkout Activation Blocker Findings

PASS 13.6 reported seven `CommerceHubCheckoutFinancialControlTests` failures returning `VARIANT_NOT_FOUND`. Investigation found fixture drift: test products were active but remained default `Draft` and `IsOnlineVisible = false` after checkout adopted `WherePubliclyVisibleLineage`. The fixtures now seed published online products so the tests reach their intended financial-control assertions.

Checkout failures must be fixed before activation: Yes. The repaired baseline must remain green, and future active-override checkout parity scenarios are still required before activation.

## 10. Chaos Findings

The frozen design fails closed:

- unauthorized creation lacks `CommerceHubPriceOverrideManage`;
- marketplace price approval, rejection, creation, or editing is not a supported authority path;
- stale merchant mutation replay fails mutation-version validation;
- rollback during checkout disables override use and recomputes SQL base price;
- feature-flag disable during rollout falls back immediately without deleting records;
- replay, recovery, and reindex cannot resurrect removed or disabled overrides.

## 11. Runtime Hardening Performed

No production pricing runtime was changed. The checkout financial-control test fixtures were aligned with current public SQL lineage so the activation blocker is accurately testable.

## 12. Regression Freeze Added

`CommerceHubPriceOverrideGovernanceCertificationTests` freezes the disabled runtime locks and this operational model. Existing PASS 13.3, 13.4, and 13.6 freezes remain authoritative.

## 13. Architecture Updates

The `.ai-context` architecture records now freeze PASS 13.5 merchant pricing ownership, governance visibility ownership, permissions, merchant workflow, feature flags, staged rollout, non-destructive rollback, immutable event inventory, monitoring, chaos behavior, and checkout blocker ownership.

## 14. Remaining Debt

Activation remains blocked until the designed merchant permission, workflow persistence, immutable events, tenant-safe runtime flags, mutation API/UI, SQL constraint relaxation migration with preflight, active-state parity scanner, dashboards, alerts, support tooling, rollout drills, rollback drills, and active override cart/checkout/projection/search/replay/recovery tests are implemented and certified.

## Explicit Answers

1. Who owns override authority? Tenant merchants.
2. Who approves override authority? Nobody. Marketplace governance does not approve pricing.
3. Is approval mandatory? No. Approval workflow is prohibited.
4. What feature flag model should exist? Global emergency kill switch plus tenant allowlist and optional cohort rollout, all default off.
5. What rollout model should exist? `Stage 0` disabled monitoring, then internal tenant, limited allowlist, broader cohorts, and general availability.
6. What rollback model should exist? Disable runtime use immediately, preserve records, fall back to `ProductVariant.EffectiveOnlinePrice`, reproject, reconcile, and audit.
7. Must checkout failures be fixed before activation? Yes.
8. What still blocks activation? The unimplemented permissions, workflow, immutable events, runtime flags, mutations, migration, parity monitoring, tooling, drills, and active-state certification.
