# POS-ADMIN-CERTIFICATION-01 PASS 13.7 CommerceHub Price Override Implementation

## Decision

Merchant-controlled CommerceHub price overrides are implemented and runtime-capable behind a default-off rollout policy. Tenant merchants own pricing. Marketplace governance still owns visibility and may suppress listings without approving, rejecting, creating, or editing merchant prices.

## 1. Persistence Activation Findings

`CommerceHubListings.CommerceHubPrice` now persists positive merchant-owned overrides. The prior null-only SQL constraint is replaced by `CK_CommerceHubListings_CommerceHubPrice_Positive`. Actor, timestamp, reason, and mutation version remain SQL-authoritative.

## 2. Resolver Activation Findings

`CommerceHubPriceAuthority` is the single runtime resolver. For enabled allowlisted tenants it returns `CommerceHubListing.CommerceHubPrice ?? ProductVariant.EffectiveOnlinePrice`. When the kill switch, tenant allowlist, or optional cohort gate rejects activation, it falls back without deleting the stored override.

## 3. Mutation API Findings

The existing tenant-scoped CommerceHub publish/update workflow now saves, updates, and removes overrides. It validates trusted tenant scope, active tenant/vendor governance, active product and variant authority, live category lineage, positive values, authenticated merchant actor, and stale mutation version.

## 4. UI Findings

The existing CommerceHub settings screen is activated without redesign. Its existing override input saves a positive override and clearing the field removes it. The screen now round-trips the mutation version.

## 5. Audit Event Findings

Immutable authoritative outbox events now include `CommerceHubPriceOverrideSavedEvent`, `CommerceHubPriceOverrideUpdatedEvent`, and `CommerceHubPriceOverrideRemovedEvent`. They carry actor, tenant, listing, previous value, new value, timestamp, correlation id, mutation version, and reason.

## 6. Feature Flag And Rollback Findings

`CommerceHub:PriceOverrides` provides a default-off global kill switch, default-empty tenant allowlist, and optional cohorts. Configuration reload updates the runtime policy. Rollback is non-destructive: disabling the global switch immediately returns variant effective online price while preserving records.

## 7. Cart, Checkout, Projection, Search, Replay And Recovery Findings

Cart and checkout already use `ICommerceHubPriceAuthority`, preserving `PRICE_CHANGED`, expected unit price validation, serializable reservations, inventory sanitation, and idempotency. Mongo projection replay and SQL search overlay now recompose accepted override metadata and price from current SQL authority. Reindex, startup rebuild, recovery, search hydration, and auto-healing inherit the SQL overlay.

## 8. Monitoring Findings

The resolver exposes override and fallback counters through `PointOfSale.CommerceHub.PriceOverride`. Mongo and Elasticsearch documents continue carrying `PriceSource` and `OverridePresent`, enabling parity drift checks.

## 9. Regression Freeze Added

`CommerceHubPriceOverrideImplementationCertificationTests` freezes resolver activation, kill-switch rollback, persistence mutation versioning, stale replay rejection, immutable events, runtime path adoption, and migration behavior.

## 10. Remaining Debt

Production operations still need deployment-specific dashboards and alert rules for the emitted counters and existing Mongo/Elasticsearch parity metadata. The activation migration must be applied before enabling any tenant.
