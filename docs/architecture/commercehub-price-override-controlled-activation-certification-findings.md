# POS-ADMIN-CERTIFICATION-01 PASS 13.6 CommerceHub Price Override Controlled Activation Certification

## Decision

CommerceHub price override activation is not allowed. The platform is not activation-ready, so no partial activation was performed.

## 1. Activation Readiness Findings

`CommerceHubPriceAuthority` is centralized across reviewed runtime pricing paths, but controlled activation prerequisites are incomplete. The resolver still has `OverridesEnabled = false`; listing writes reject non-null override input; `ApplicationDbContext` clears attempted override persistence; PostgreSQL requires `CommerceHubPrice IS NULL`; projection and search paths clear derivative override state.

## 2. Governance Findings

No certified override-specific merchant manage permission, marketplace audit/emergency-disable permission, merchant mutation workflow, or immutable change event exists for create, modify, remove, or audit operations. Existing marketplace governance remains authoritative for visibility and can suppress listings, but it may not approve, reject, create, or edit merchant prices. Override metadata cannot create visibility.

## 3. Activation Model Findings

The intended single chain remains:

`FinalCommerceHubPrice = CommerceHubListing.CommerceHubPrice ?? ProductVariant.EffectiveOnlinePrice`

That chain is not active. Current runtime remains:

`FinalCommerceHubPrice = ProductVariant.EffectiveOnlinePrice`

## 4. Feature Flag Findings

No tenant-safe, globally disable-able, rollback-safe runtime feature flag exists for CommerceHub price overrides. The compile-time disabled constant is a safety lock, not an activation control.

## 5. Cart Findings

Cart pricing and sanitation use `ICommerceHubPriceAuthority`. Existing `PRICE_CHANGED` behavior is preserved. Override-enabled, override-removed, override-changed, and stale-cart rollout tests cannot be certified until a controlled flag and mutation workflow exist.

## 6. Checkout Findings

Checkout recomputes SQL-backed price through `ICommerceHubPriceAuthority`, compares `ExpectedUnitPrice`, preserves `PRICE_CHANGED`, and retains serializable reservation and idempotency behavior. Override-update checkout cases remain blocked from activation testing because no controlled override mutation path exists.

Adjacent verification initially found seven existing `CommerceHubCheckoutFinancialControlTests` scenarios failing closed as `VARIANT_NOT_FOUND` before reaching their intended assertions. PASS 13.5 repaired that fixture drift by seeding published online products. The suite is green and must remain green before any activation pass can certify checkout parity.

## 7. Search And Projection Findings

MongoDB, Elasticsearch, SQL overlays, reindex, and recovery remain derivative. Current paths clear `CommerceHubPriceOverride`, emit `OverridePresent = false`, and converge to SQL variant pricing. No active override parity monitor exists.

## 8. Replay And Recovery Findings

Replay, recovery, auto-healing, startup rebuild, and reindex converge to current SQL authority without resurrecting stale overrides because SQL override storage remains constrained null and derivative override fields are cleared.

## 9. Auditability Findings

Nullable attribution columns exist for who, when, and reason, but no immutable override audit event trail or certified operator workflow exists. Attribution storage alone is insufficient for activation.

## 10. Rollback Findings

The current disabled state is safe. A future runtime rollback model is not yet certified because there is no tenant-safe activation flag. Existing records cannot be preserved during a flag rollback yet because active override records are intentionally impossible to persist.

## 11. Runtime Hardening Performed

No runtime behavior was changed. The required hardening is the explicit activation stop and regression freeze.

## 12. Regression Freeze Added

`CommerceHubPriceOverrideActivationCertificationTests` freezes the failed activation gate: disabled resolver, rejected writes, SQL null constraint, EF clearing, derivative clearing, current cart/checkout resolver use, and PASS 13.6 documentation.

## 13. Remaining Debt

Activation still requires override-specific merchant manage permission, marketplace audit/emergency-disable permissions, merchant mutation workflow, immutable audit events, tenant-safe global rollout flag, mutation API and UI, migration/preflight for relaxing the null-only constraint, preservation of the green CommerceHub checkout financial-control baseline, cart and checkout rollout tests, projection/search parity scanner, alerting, operator dashboard, support tooling, and rollback runbook.

## Explicit Answers

1. Was override activated? No.
2. Under what controls? It remains disabled by resolver constant, entity validation, EF save guard, SQL null constraint, and derivative clearing.
3. Can it be rolled back safely? The current disabled state is safe; future active rollback is not yet certified.
4. Can governance still suppress listings? Yes.
5. Can replay/recovery resurrect stale prices? Not while the current null constraint and derivative clearing remain enforced.
6. Does checkout honor override correctly? Checkout honors the centralized resolver, but override behavior is intentionally inactive.
7. Does search honor override correctly? Search honors the centralized disabled-state resolver and SQL overlay; active override parity is not certified.
8. What risks remain? Accidental partial activation, missing merchant mutation authorization and immutable audit trail, missing rollout flag, missing parity monitoring, and untested active rollback.
