# SHOPIXY Dynamic Checkout, Shipping, and Warehouse Final Forensic Audit

Date: 2026-08-11

## 1. Final Verdict

Final status:

```text
BLOCKED FOR PRODUCTION CERTIFICATION
SOURCE IMPLEMENTATION SUBSTANTIALLY HARDENED
EXTERNAL VERIFICATION AND BUSINESS DECISIONS REMAIN
```

No known P0 source-code defect remains in the inspected Dynamic Checkout
attachment finalization path or the single-origin Storefront warehouse authority
path after the corrections listed in this report.

Production certification is still blocked by four gates:

1. Real Cloudflare R2 finalization, download, retry, cleanup, and race behavior has
   not been verified because no credential-bearing R2 runtime is available.
2. The current migration
   `20260811150644_SHOPIXY_WarehouseResolutionAuthority` has not been applied to an
   isolated or target PostgreSQL database in this pass. The SQL script and EF model
   are valid statically, but current-schema application evidence is absent.
3. `STOREFRONT_SINGLE_WAREHOUSE_EXACT_ONE_V1` deliberately rejects carts that
   have more than one eligible warehouse. A winner policy remains a business
   decision and was not invented.
4. External carrier readiness still has two persisted authorities:
   `CarrierConfiguration` and `CarrierAccount`/`CarrierWarehouseAssignment`.
   Preview and submit now consistently require the intersection, but the business
   and architecture decision about which authority survives remains open.

The practical result is conditional readiness:

- Manual fulfillment with exactly one eligible warehouse is source-ready, subject
  to migration deployment and ordinary staging verification.
- External carriers are fail-closed and preview/submit consistent, but require both
  current readiness systems until authority unification is decided.
- Tenants with multiple warehouses that can fulfill the same cart cannot complete
  Storefront checkout until a legitimate winner policy is selected.
- Attachment-backed Dynamic Checkout cannot be production-certified until live R2
  verification succeeds.

## 2. Scope and Method

Audited surfaces:

- Dynamic Checkout field definitions, browser/server rules, attachment leases,
  finalization, cleanup, order binding, and idempotency.
- Shipping method administration, read projection, Storefront availability,
  carrier readiness, and preview/submit parity.
- Checkout-session ownership, warehouse resolution, inventory reservation, order
  warehouse stamping, and shipment origin propagation.
- EF model constraints, tenant foreign keys, migration generation, migration SQL,
  transaction boundaries, concurrency handling, logging, frontend admission, and
  production verification evidence.

Method:

- Reconstructed the actual Storefront-to-order-to-shipment call graph from source.
- Reviewed all prior `SHOPIXY_*` architecture reports as historical evidence, not
  as proof that the current source still satisfies their conclusions.
- Replaced source-string-only confidence with focused behavioral tests where
  practical.
- Preserved the pre-existing dirty worktree and performed no reset, clean, commit,
  push, or unrelated revert.
- Did not fabricate R2, carrier, or PostgreSQL runtime evidence.

## 3. Historical Certification Correction

`SHOPIXY_SHIPPING_METHODS_STEP_2F_IMPLEMENTATION_2026-08-11.md` reported `PASS`
and cited migration `20260811142427_SHOPIXY_WarehouseResolutionAuthority` plus a
scratch database application. That report is useful historical evidence, but it
does not certify the final source for three reasons:

- The earlier warehouse tests were primarily architecture/source assertions rather
  than behavioral resolution tests.
- Additional model invariants were added after the original migration was
  generated, so that migration omitted the session/version unique index and two
  status check constraints.
- The migration was regenerated as
  `20260811150644_SHOPIXY_WarehouseResolutionAuthority`; prior scratch-catalog
  evidence for the old migration does not prove that the current migration applies.

This report supersedes the unconditional Step 2F `PASS` conclusion.

## 4. Final Authority Graph

The implemented Storefront authority path is:

```text
CheckoutSession + authoritative cart owner
  -> WarehouseResolutionService
  -> WarehouseResolution.WarehouseId
  -> warehouse-filtered shipping method preview
  -> CheckoutRequest resolution ID/version
  -> serializable final checkout validation + resolution row lock
  -> explicit warehouse inventory reservation
  -> Order.WarehouseId
  -> every Storefront OrderItem.WarehouseId
  -> Shipment.OriginWarehouseId
```

Evidence:

- Preview accepts `CheckoutSessionId` and does not accept client warehouse
  authority: `StorefrontShippingMethodsController.cs:136-199`.
- Resolution submission requires session ID, resolution ID, and positive version:
  `CheckoutDtos.cs:128-141`.
- Tenant/session/cart/owner binding and submitted resolution validation occur in
  `WarehouseResolutionService.cs:156-287`.
- PostgreSQL final validation locks the resolution row with `FOR UPDATE`:
  `WarehouseResolutionService.cs:722-740`.
- Final checkout revalidates inside a serializable transaction:
  `CheckoutService.cs:6366-6413`.
- The order and every order item receive the resolved warehouse:
  `CheckoutService.cs:6451-6462`.
- Reservation receives the explicit warehouse:
  `CheckoutService.cs:6467-6475` and `CheckoutService.cs:5791-5795`.
- Shipment creation copies `Order.WarehouseId` without resolving another origin:
  `ShipmentService.cs:170-176`.

No Storefront request field becomes warehouse authority. IDs returned by the
server are opaque references that are reloaded and tenant/session/cart checked.

## 5. Warehouse Resolution Findings

Implemented algorithm:

```text
STOREFRONT_SINGLE_WAREHOUSE_EXACT_ONE_V1
```

Behavior:

- Zero candidate warehouses: `NO_ELIGIBLE_WAREHOUSE`, HTTP 422.
- Exactly one candidate warehouse: persisted `Resolved` authority with ten-minute
  expiry.
- More than one candidate: `WAREHOUSE_SELECTION_POLICY_REQUIRED`, HTTP 422.
- Empty carts and cart lines without valid variant authority fail closed.
- Non-stock-tracked and backorderable variants do not impose stock requirements,
  matching reservation policy.
- The resolver hashes safe tenant/session/cart/variant/warehouse/stock inputs and
  creates a new version rather than mutating a prior decision.

Implementation evidence:

- Candidate input construction and grouped stock reads:
  `WarehouseResolutionService.cs:462-575`.
- Exact-one/zero/multiple decision:
  `WarehouseResolutionService.cs:362-378`.
- Cart-change and expiry invalidation:
  `WarehouseResolutionService.cs:124-153`.
- Concurrent unique-conflict replay, including deterministic failed outcomes:
  `WarehouseResolutionService.cs:382-415`.
- Strict `Locked -> Ordered` conversion:
  `WarehouseResolutionService.cs:290-327`.

Remaining business gate:

The source contains `Warehouse.IsDefault`, but no reviewed authority establishes
that the default warehouse must win Storefront routing. Nearest warehouse,
customer-city routing, lowest GUID, first stock row, carrier preference, or a
default-warehouse fallback would all invent policy. The implementation therefore
correctly blocks ambiguous carts.

Operational consequence:

Any tenant with two active warehouses that can both fulfill a cart will receive
`WAREHOUSE_SELECTION_POLICY_REQUIRED`. This is safe, explicit, and not general
multi-warehouse production readiness.

## 6. Shipping and Carrier Findings

Manual fulfillment:

- Remains available for any resolved active warehouse.
- Does not require credentials, `CarrierAccount`, `CarrierConfiguration`, or
  `CarrierWarehouseAssignment`.
- Still uses the resolved warehouse for inventory, order, and shipment origin.

External carrier eligibility now requires all existing gates:

- Active projected shipping method.
- Enabled legacy `CarrierConfiguration` for the tenant/carrier.
- Active `CarrierAccount` with `OperationalState == Ready`.
- Enabled, origin-ready `CarrierWarehouseAssignment` for the resolved warehouse.
- Same tenant across configuration, account, assignment, and warehouse.

Preview/submit parity fix:

Before this audit, preview filtered through `CarrierAccount` and assignment, while
final checkout also required `CarrierConfiguration`. A customer could select a
method that checkout would deterministically reject. Preview now intersects ready
account/assignment carrier codes with enabled `CarrierConfiguration` codes in
`ShippingMethodService.cs:214-251`.

This is fail-closed parity, not authority unification. The unresolved decision is
whether the legacy `CarrierConfiguration` or the newer carrier-account model is
the future canonical readiness owner. Until that decision is made, both must be
maintained.

## 7. Attachment and R2 Findings

The original P0 attachment defect was a time-of-check/time-of-use gap:

```text
HEAD staged object -> signature read -> unconditional final copy
```

An object could theoretically change between validation and copy. The hardened
flow is:

```text
HEAD staged object and require ETag
  -> conditional prefix read with If-Match
  -> signature validation
  -> persist deterministic final key in recoverable state
  -> conditional source copy with the same ETag
  -> verify final content type and length
  -> persist final ETag/checksum and Finalized state
```

Evidence:

- Missing ETag fails as `CHECKOUT_ATTACHMENT_INTEGRITY_EVIDENCE_MISSING`:
  `CheckoutAttachmentService.cs:644-650`.
- Signature read uses the staged ETag: `CheckoutAttachmentService.cs:661-665`.
- Deterministic final key is retained before copy:
  `CheckoutAttachmentService.cs:723-735`.
- R2 copy sends `ETagToMatch`: `R2StorageService.cs:354-377`.
- R2 logs no longer include object keys or bucket details.

Recovery hardening:

- Ambiguous copy failure retains `FinalObjectKey`, marks the row failed, and leaves
  cleanup enough evidence to delete a possibly-created object.
- Cleanup claims rows as `Deleting` before object deletion.
- Cleanup deletes staged and final objects when both exist.
- Any claimed-object deletion failure is rethrown after the batch, preventing
  Hangfire from recording false success: `CheckoutAttachmentCleanupJob.cs:83-97`.

Live limitation:

Mock tests cannot prove Cloudflare R2 honors all S3 conditional-copy semantics in
the configured account, or prove real timeout, lost-response, concurrent confirm,
cleanup race, and merchant-download behavior. Live R2 remains a P0 external gate.

## 8. Data Integrity and Migration

Current migration:

```text
20260811150644_SHOPIXY_WarehouseResolutionAuthority
```

The migration creates:

- `WarehouseResolutions` with composite tenant foreign keys to checkout session,
  warehouse, and converted order.
- Composite tenant foreign keys from `CarrierWarehouseAssignment` to
  `CarrierAccount` and `Warehouse`.
- One active `Resolved`/`Locked` resolution per tenant/session.
- Unique resolution version per tenant/session.
- Positive version constraint.
- Warehouse-required constraint for `Resolved`, `Locked`, and `Ordered`.
- Converted-order-required constraint for `Ordered`.
- Failure-code-required constraint for `Failed`.
- Tenant/cart/status, tenant/warehouse/status, expiry/status, converted order, and
  input hash indexes.

The migration includes fail-fast preflight checks for existing missing or
cross-tenant carrier account/warehouse assignments:
`20260811150644_SHOPIXY_WarehouseResolutionAuthority.cs:14-40`.

EF runtime guards prevent mutation of warehouse decision inputs and prevent
deletion/soft deletion of retained resolution authority:
`ApplicationDbContext.cs:4028-4051`.

Verification:

- EF reports no pending model changes.
- An idempotent PostgreSQL script was generated successfully from
  `20260810181420_DOMAIN_Final_ForensicHardening` through the current migration.
- Generated SQL contains the preflight checks, table, check constraints, tenant
  foreign keys, partial active index, and session/version unique index.

Blocked evidence:

- Docker is not installed in the current environment.
- No local PostgreSQL service or `psql` executable was found.
- The current migration was not applied to a disposable database in this pass.
- Target database migration history and data preflight results are unknown.

Rollout risk:

Adding alternate unique keys to existing `Warehouses`, `CheckoutSessions`, and
`CarrierAccounts` can acquire table locks. The composite carrier foreign keys can
fail when legacy orphan/cross-tenant rows exist. The preflight makes data defects
explicit, but staging rehearsal and an operational lock budget are still required.

## 9. Transaction and Idempotency Review

Final checkout authority is revalidated within the existing PostgreSQL
serializable transaction before reservation and order persistence. Resolution is
row-locked, shipping and payment authority rows are reloaded, authoritative
pricing is recalculated, field definitions and attachments are revalidated, stock
is reserved for the explicit warehouse, and resolution is marked ordered before
commit.

The durable request hash includes:

- checkout session ID;
- warehouse resolution ID;
- resolution version;
- shipping method;
- canonical owner;
- payment/coupon/wallet fields;
- custom fields and sorted attachments;
- shipping and billing address fingerprints.

The cart-snapshot hash additionally includes the resolved warehouse ID. A retry
after a successful order can replay through durable idempotency before volatile
cart and active-resolution validation, preserving completed-order behavior.

Residual concurrency evidence gap:

Focused tests verify deterministic service behavior and transition rules, but no
real PostgreSQL race test was run for concurrent preview creation, resolution
expiry, last-stock checkout, carrier disablement, or serializable retry behavior.

## 10. Tenant and Threat Review

Controls found:

- Storefront tenant resolution validates the trusted tenant context/header.
- Authenticated customer tenant claims are checked where present.
- Guest calls require `X-Session-Id`.
- Checkout session ownership is checked against customer or browser session.
- Cart ID, owner type, owner ID, tenant, and checkout session must agree.
- Resolution lookup always includes tenant, ID, version, session, and cart checks.
- Stock, warehouse, carrier account, assignment, configuration, shipping method,
  order, and shipment queries include tenant scope.
- Composite database foreign keys prevent cross-tenant carrier assignments and
  warehouse-resolution references after migration.
- The browser never submits authoritative warehouse ID or storage object keys.

Threat outcomes:

| Threat | Result |
| --- | --- |
| Client submits another warehouse | No warehouse field is accepted; server reloads resolution. |
| Cross-tenant resolution/session ID | Fails tenant/session/cart/owner validation. |
| Stale cart or stock | Hash/candidate validation fails and requires refresh. |
| Carrier assignment removed | Preview filters it; submit revalidates and fails closed. |
| Legacy carrier config disabled | Preview filters it; submit also rejects it. |
| Inventory fallback to another warehouse | Forbidden in Storefront checkout. |
| Shipment selects a new origin | Shipment copies order warehouse only. |
| Staged upload replaced after signature validation | Conditional ETag copy/read prevents accepted-object substitution. |
| Cleanup storage failure hidden from scheduler | Failure is rethrown for retry/alerting. |

## 11. Frontend Review

Storefront behavior:

- Calls server preview with checkout session ID.
- Admits tenant, UUID, amount, currency, resolution status/version, and expiry
  evidence before state mutation.
- Aborts superseded preview requests and uses request sequence fencing.
- Clears resolution state while refreshing or after a failed preview.
- Refreshes shipping preview after cart ID or cart update timestamp changes.
- Refreshes cart, shipping preview, pricing, and dynamic fields immediately before
  final submit.
- Blocks submit when resolution is missing, not `RESOLVED`, expired, or paired
  with a stale shipping selection.
- Sends resolution ID/version but no warehouse ID.

The Storefront and admin production builds pass. Existing Vite warnings about
large chunks, mixed static/dynamic imports, and stale Browserslist data are
performance/maintenance findings, not correctness blockers for this scope.

## 12. Verification Evidence

Commands and outcomes:

| Verification | Result |
| --- | --- |
| API project build | PASS, 0 errors; repository warnings remain on non-incremental builds. |
| Focused backend warehouse/attachment/rule/idempotency/shipping suite | PASS, 79/79. |
| Focused Storefront authority and field-rule Vitest suite | PASS, 39/39. |
| Focused shipping administration Web Vitest suite | PASS, 13/13. |
| Storefront production build | PASS with existing Vite warnings. |
| Web production build | PASS. |
| EF pending model changes | PASS, no pending changes. |
| Current idempotent migration script generation | PASS. |
| Current migration applied to scratch PostgreSQL | BLOCKED in current environment. |
| Live R2 lifecycle and race verification | BLOCKED, credentials/runtime absent. |
| Live external carrier connectivity/shipment registration | NOT PERFORMED. |

The backend suite now contains behavioral coverage for exact-one, zero,
multi-candidate, empty-cart, missing-variant, immutability, cart-change versioning,
expiry versioning, and strict ordered transition behavior. It also covers carrier
preview fail-closed behavior when legacy configuration is absent.

## 13. Remaining Findings

| ID | Severity | Type | Finding | Required action |
| --- | --- | --- | --- | --- |
| FINAL-BLOCK-001 | P0 gate | External verification | Real R2 lifecycle, conditional copy, timeout recovery, merchant download, and cleanup races are unverified. | Run disposable staging R2 smoke/race suite with real credentials and remove all test objects. |
| FINAL-BLOCK-002 | P0 cutover gate | Database verification | Current migration has not been applied to disposable or target PostgreSQL. | Run preflight, apply full chain, inspect constraints/indexes/history, test down only on disposable DB, and capture evidence. |
| FINAL-BIZ-001 | Business decision | Warehouse routing | Multiple eligible warehouses have no legitimate winner policy. | Select and document default, priority, service-area, customer-selection, or another explicit policy before multi-warehouse rollout. |
| FINAL-BIZ-002 | Business/architecture decision | Carrier readiness | `CarrierConfiguration` and `CarrierAccount` remain dual authorities. Runtime safely requires both, but admin ownership and migration policy are duplicated. | Choose canonical authority, define backfill/retirement, and update checkout, shipment, admin, and projection paths together. |
| FINAL-P1-001 | P1 | Concurrency evidence | No real PostgreSQL preview/last-stock/serializable race suite for the final source. | Add deterministic PostgreSQL integration tests after scratch infrastructure is available. |
| FINAL-P1-002 | P1 | Observability | Warehouse resolution has structured logs but no dedicated metrics/alerts for created, invalidated, policy-required, stock-conflict, or carrier-mismatch outcomes. | Add bounded-cardinality counters/histograms and alert thresholds. |
| FINAL-P1-003 | P1 | Performance | Resolution loads all active warehouses and grouped stock evidence, then evaluates candidates in memory; snapshot JSON grows with warehouse/variant count. | Measure realistic tenant sizes and add query/snapshot limits or SQL-side candidate reduction if required. |
| FINAL-P1-004 | P1 | Migration operations | Alternate keys and foreign keys can lock populated tables; orphan preflight can block deployment. | Rehearse with production-like volume, define lock timeout/window, and remediate preflight failures before rollout. |
| FINAL-P2-001 | P2 | Documentation | `ShippingMethodService` comments still describe Mongo/Redis-only access although the service now reads SQL readiness/count authority. | Update architecture comments after canonical carrier authority is decided. |
| FINAL-P2-002 | P2 | Failure taxonomy | `WAREHOUSE_INACTIVE` is defined but current candidate drift normally reports stock/resolution change codes. | Decide whether operationally distinct inactive-warehouse reporting is required and test it. |

## 14. Production Readiness Matrix

| Category | Result |
| --- | --- |
| Server authority | PASS STATIC/FOCUSED TESTS |
| Tenant isolation | PASS STATIC/FOCUSED TESTS; migration application pending |
| Warehouse split-brain prevention | PASS for exact-one policy |
| Multi-warehouse business completeness | BLOCKED / BUSINESS DECISION |
| Inventory/order/shipment propagation | PASS STATIC/FOCUSED TESTS |
| Shipping preview/submit parity | PASS after intersection fix |
| Canonical external carrier authority | BLOCKED / BUSINESS DECISION |
| Checkout idempotency | PASS STATIC/FOCUSED TESTS |
| Attachment TOCTOU protection | PASS STATIC/MOCK TESTS |
| Live object-storage integrity | BLOCKED / EXTERNAL VERIFICATION |
| Migration model consistency | PASS |
| Migration SQL generation | PASS |
| Migration execution/current catalog | BLOCKED / ENVIRONMENT |
| Frontend contract/admission | PASS |
| Production builds | PASS |
| Observability | PARTIAL |
| Performance evidence | PARTIAL |

## 15. Required Next Actions

1. Apply `20260811150644_SHOPIXY_WarehouseResolutionAuthority` to an isolated
   PostgreSQL database using production-like data volume; capture preflight,
   migration history, constraints, indexes, and lock duration.
2. Run real R2 staged upload, conditional finalization, concurrent confirm,
   response-loss retry, merchant download, explicit removal, cleanup retry, and
   cleanup/finalization race scenarios.
3. Decide the warehouse winner policy for multiple valid candidates.
4. Decide whether `CarrierConfiguration` or `CarrierAccount` is canonical, then
   plan deterministic backfill and retirement of the other readiness authority.
5. Add PostgreSQL race tests and bounded-cardinality warehouse-resolution metrics.

Do not start another shipping feature phase before actions 1 through 4 are
resolved. The remaining risk is deployment/runtime authority, not missing UI
surface area.

## 16. Certification Statement

The inspected source now establishes a coherent exact-one Storefront warehouse
authority and propagates it through shipping eligibility, reservation, order,
order items, and shipment origin. It also closes the attachment ETag TOCTOU gap,
retains recoverable final-object evidence, exposes cleanup failures to Hangfire,
enforces stronger database invariants, and prevents preview from offering an
external carrier that final checkout will reject under the current dual gates.

The implementation is not fully production-certified. Real R2 evidence, current
PostgreSQL migration application, a multi-warehouse winner policy, and canonical
external-carrier readiness ownership remain mandatory closure items.
