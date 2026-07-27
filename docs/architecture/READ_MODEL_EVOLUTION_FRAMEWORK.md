# Production-Safe Read Model Evolution Framework

## 1. Executive Summary

SQL remains the single source of truth. MongoDB and Elasticsearch remain derived read models. Read-model schema evolution is safe only when runtime reads are backward compatible until convergence is certified, projection writes stamp schema/projection metadata, and strict filters begin only after every existing document can prove the new shape.

This framework implements that contract in code:

- `PointOfSale.Infrastructure/ReadModels/Evolution/ReadModelEvolution.cs` defines read-model ownership, versions, compatibility policy, convergence snapshots, and validation.
- `PointOfSale.Infrastructure/Elasticsearch/Services/ReadModelEvolutionQueryFilters.cs` provides version-aware query filters for fields that become runtime-required.
- Product Mongo and Elasticsearch documents now stamp `ReadModelName`, `ReadModelSchemaVersion`, `ProjectionVersion`, and `ProjectionConvergedAtUtc`.
- Product search/discovery uses an evolution-compatible filter for `isSearchable`: explicit `false` is excluded; missing is tolerated until convergence; SQL authority is reapplied before product responses.
- `PointOfSale.Tests/Architecture/ReadModelEvolutionFrameworkCertificationTests.cs` certifies old, mixed, and converged read-model behavior.

## 2. Current Weakness Analysis

The platform already has strong foundations: SQL authority overlays, monotonic source mutation versions, event fences, idempotent upserts, tombstones, and repair services. The missing invariant was cross-read-model schema evolution. A new field could become a strict query predicate before existing MongoDB or Elasticsearch documents contained that field.

Break points found in the audit:

| Area | Weakness | Runtime Risk |
| --- | --- | --- |
| Elasticsearch filters | Newly added boolean filters used immediately as strict terms | Old documents without the field disappear from result sets |
| Elasticsearch strict mappings | Existing strict index may reject newly stamped fields unless mapping is extended first | Projection writes fail after deployment |
| Mongo documents | No generic schema/projection version metadata | Convergence cannot be proven automatically |
| Projection writers | Some writers rely on model defaults | Hydrated old documents can keep old metadata |
| Operational docs | No reusable migration playbook | Future teams may repeat ad hoc fixes |

## 3. Read Model Inventory

| Read Model | Store | Owner | Projection Source | Consumers | Mapper | Versioning |
| --- | --- | --- | --- | --- | --- | --- |
| `storefront.product` | MongoDB | Storefront Catalog | PostgreSQL Product aggregate plus authoritative events | Product readers, cart/search hydration, promotion projection | `ProductSearchSqlAuthorityOverlay -> ProductSqlAuthorityProjectionWriter` | `ReadModelSchemaVersion`, `ProjectionVersion` |
| `storefront.product` | Elasticsearch | Storefront Search | SQL-recomposed `ProductDocument` | `ProductDiscoveryService`, `ProductSearchIndexService` | `ProductSearchDocumentMapper` | schema, projection, index version |
| `storefront.category` | MongoDB | Storefront Catalog | PostgreSQL Category aggregate events | `CategoryReadRepository`, category/promotion projections | category projection consumers | schema, projection version |
| `storefront.category` | Elasticsearch | Storefront Search | Category projection | `UnifiedSearchIndexService` | `AdminSearchSyncController.MapToCategorySearchDocument` | schema, projection, index version |
| `storefront.promotion` | MongoDB | Promotions | PostgreSQL Promotion aggregate events | promotion projection and discount repair | `PromotionProjectionConsumer` | schema, projection version |
| `commercehub.product` | MongoDB | CommerceHub Marketplace | SQL listing/product/stock/store/currency/shipping events | CommerceHub read/search services | CommerceHub projection mappers | schema, projection version |
| `commercehub.product` | Elasticsearch | CommerceHub Search | CommerceHub Mongo projection with SQL overlay | `CommerceHubSearchReadService`, `CommerceHubSearchIndexService` | `CommerceHubSearchDocumentMapper` | schema, projection, index version |

The registry is intentionally code-owned. A new read model is not production-ready until it is registered and validated.

## 4. Read Model Evolution Architecture

Every read model has:

- A stable `ReadModelName`.
- A `ReadModelSchemaVersion` for document shape.
- A `ProjectionVersion` for projection logic.
- An optional `CurrentIndexVersion` for Elasticsearch mappings and aliases.
- A compatibility policy.
- A convergence strategy.
- A list of runtime-required field requirements.

The invariant is:

`SQL authority -> compatible projection write -> version-stamped document -> compatible read -> convergence certification -> strict read`

The framework supports:

| Change | Rule |
| --- | --- |
| New field | Write the field, stamp a higher schema version, tolerate missing until convergence, then strict filtering may begin |
| Removed field | Stop writing the field after consumers no longer need it; readers ignore unknown/extra fields |
| Renamed field | Write both names during expand phase; consumers read new with old fallback; remove old after convergence |
| Semantic change | Introduce a new field or schema version; do not silently reuse old meaning |
| Type change | Add new typed field, dual-write, converge, then remove old field |
| Projection version change | Bump `ProjectionVersion`; reject stale writes using existing freshness guards |
| Elasticsearch mapping change | Create/update mapping before writes; use alias rebuild for incompatible changes |
| Mongo document change | Preserve `[BsonIgnoreExtraElements]`; stamp schema/projection metadata; converge in background |

## 5. Compatibility Strategy

New runtime filters are never strict on day one. During compatibility:

- `field == requiredValue` is accepted.
- missing field is accepted only for fields declared in the registry.
- explicit opposite values are rejected.
- public product paths reapply SQL authority before response shaping.

For product searchability this is implemented by `ReadModelEvolutionQueryFilters.RequiredBoolean`. The compatibility behavior prevents old documents from disappearing, while SQL recomposition prevents Mongo or Elasticsearch from becoming business authority.

Strict filtering begins only when `ReadModelEvolutionPolicy.Evaluate` receives a convergence snapshot proving:

- all observed documents are at or above `CurrentSchemaVersion`;
- all observed documents are at or above `CurrentProjectionVersion`.

## 6. Projection Versioning Strategy

Projection version ownership lives with the projection owner:

- SQL entities keep source mutation ordering, for example `ProjectionMutationVersion`.
- Mongo/Elasticsearch documents keep derived schema/projection versions.
- Elasticsearch definitions also carry `CurrentIndexVersion`.

Mixed versions are allowed during deployment. Runtime reads use compatible semantics until convergence is certified.

## 7. Automatic Convergence Strategy

Normal schema evolution should converge automatically through a hybrid model:

- New writes stamp current schema/projection versions.
- Existing products converge through SQL-authority recomposition and projection refresh.
- Elasticsearch converges through background reindex or alias rebuild for incompatible mappings.
- Lazy reads may repair old Mongo documents only after SQL authority recomposition.
- Manual scripts are reserved for forensics, not the primary migration mechanism.

Current implementation evidence:

- `ProductSqlAuthorityProjectionWriter` stamps current metadata on recomposed Mongo product documents.
- `ProductSearchDocumentMapper` stamps current metadata on Elasticsearch product documents.
- Existing search index mapping updates include evolution metadata fields before writes.

## 8. Deployment Strategy

The deployment model is expand, converge, contract:

1. Expand: deploy code that can read old and new documents, writes new metadata, and updates Elasticsearch mappings.
2. Converge: background projection refresh/reindex rewrites old documents.
3. Certify: convergence snapshot proves every document is current.
4. Contract: strict filters may begin and obsolete compatibility fields can be removed.

This supports rolling, blue/green, canary, mixed consumers, mixed projections, and interrupted deployments.

## 9. Failure Recovery Strategy

Failure mode behavior:

| Failure | Runtime Behavior |
| --- | --- |
| Missing new field | Compatible filter admits candidate; SQL authority revalidates before product response |
| Unknown extra field | Mongo ignores extra fields where configured; Elasticsearch explicit mappings must be expanded first |
| Half-migrated projection | Compatible mode remains active |
| Interrupted convergence | Restart convergence; strict mode remains disabled |
| Old application instance | Continues reading old-compatible shape |
| New application with old documents | Runtime remains available |
| Bad Elasticsearch mapping | Mapping update fails visibly; app startup continues but search fails explicitly instead of returning silent empty results |

## 10. Runtime Validation Matrix

| Scenario | Expected Result | Evidence |
| --- | --- | --- |
| Old index -> new app | Missing runtime field tolerated; explicit false rejected | `ReadModelEvolutionQueryFilters.RequiredBoolean` |
| Old Mongo -> new app | SQL recomposition stamps metadata on rewrite | `ProductSqlAuthorityProjectionWriter` |
| Mixed projections | Compatible mode remains active | `ReadModelEvolutionPolicy` tests |
| Partial convergence | Strict filters disabled | certification test |
| Complete convergence | Strict filters enabled | certification test |
| Interrupted convergence | No convergence snapshot, compatible mode | policy behavior |
| New docs | Current schema/projection metadata stamped | product document tests |

## 11. Migration Guidelines

For any future read model change:

1. Register or update the read model in `ReadModelEvolutionRegistry`.
2. Bump schema version for document-shape changes.
3. Bump projection version for projection-logic changes.
4. Bump index version for Elasticsearch mapping or analyzer changes.
5. Add compatibility defaults for every new runtime-required field.
6. Ensure consumers reapply SQL authority before enforcing SQL-owned business fields.
7. Add projection refresh or reindex convergence.
8. Add certification tests for old, mixed, converged, and interrupted states.
9. Remove obsolete compatibility only after convergence certification.

## 12. Operational Playbook

- Watch convergence counts by read model name, schema version, and projection version.
- Alert if convergence stalls.
- Treat search backend failures as unavailable, not empty data.
- Never make MongoDB or Elasticsearch the source of business truth.
- Do not run manual rebuild scripts for normal evolution; use replay, background refresh, or alias rebuild.
- Keep rollback safe by allowing old and new app versions to read both old and new document shapes.

## 13. Future Read Model Checklist

Before adding fields such as `IsFeatured`, `SearchRank`, `Collections`, `Badges`, `Attributes`, `Brand`, `SEO`, `Ratings`, inventory flags, or promotions:

- Is SQL still the authority?
- Is the read model registered?
- Is the field additive, renamed, removed, semantic, type, or mapping evolution?
- Is a compatibility default declared?
- Is strict filtering delayed until convergence?
- Are old documents tolerated?
- Is explicit false/null handled differently from missing?
- Is convergence automatic?
- Is mapping expansion or alias rebuild handled before writes?
- Are mixed app versions safe?
- Are certification tests present?

## 14. Production Readiness Assessment

The implemented framework eliminates the observed defect class for product search/discovery and creates the reusable contract for all future read-model evolution. Remaining production hardening is to persist convergence snapshots and expose operational metrics per registered read model. Until that is wired, the safe default is compatible mode, which preserves runtime availability and SQL authority.

Final recommendation: adopt this framework as the mandatory gate for every MongoDB and Elasticsearch read-model change. No runtime-required read-model field should be introduced without registry metadata, compatibility semantics, automatic convergence, and certification tests.
