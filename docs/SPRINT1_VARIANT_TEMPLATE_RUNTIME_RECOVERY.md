# SPRINT-1 Variant Template Runtime Recovery

## Root Cause

Variant Template generation exposed two canonical-runtime gaps that manual matrix editing did not reliably hit:

1. Template/generated drafts can produce rows with no persisted `variantId`. The backend variant synchronization path matched existing variants only by `VariantId`, then marked all unmatched existing variants deleted and created all null-id rows as new variants. That made generated drafts semantically different from manual drafts even when the attribute combination identified the same sellable variant.
2. `GET /products/{id}` embedded variant references did not include every authoring field used by the matrix runtime. In particular, `costPrice`, `weight`, and `mediaId` were absent, so ProductForm could build a baseline that was not semantically equal to the canonical variant DTO returned by `/products/{id}/variants`.

Independent authoring bugs also remained:

- Existing Variant Products still rendered Product Units.
- Variant Matrix still allowed direct inventory editing and post-authoring stock updates.
- Variant weight could be persisted by the variant endpoint path, but the canonical product response could not round-trip variant weight.

## Frontend Findings

- Manual matrix rows and template-generated rows both pass through `ProductVariantsTab -> toProductAuthoringVariant -> ProductForm.saveProductAuthoring`.
- Template-generated new rows used frontend runtime keys for rendering (`Color=Red|Size=M`) and mapped to `variantId: null`, while existing rows mapped to persisted IDs.
- The frontend did not include `attributeIds`, `attributeNames`, `optionGroups`, `optionValues`, temporary IDs, or frontend keys in the DTO. These remain UI-only runtime state.
- Inventory was still present as a matrix-editable draft field and in bulk actions, with a post-save stock mutation path.
- Product Units were hidden only for variant create mode, not existing Variant Products.

## Backend Findings

- `ProductAuthoringOrchestrator.SyncAuthoredVariantsAsync` previously treated `VariantId` as the only existing-variant identity.
- The database already owns canonical uniqueness through `ProductVariant.AttributeCombinationKey`.
- Existing variant detection needed to accept the same canonical attribute key used by uniqueness and storefront identity.
- Variant weight persistence existed in `ProductVariant.Weight`, EF config, command mapping, and orchestrator create/update assignment.
- Canonical product response mapping omitted variant `CostPrice`, `Weight`, and `MediaId`, causing reload/baseline divergence.

## Runtime Comparison

Manual edited row:

- `id`: persisted variant ID
- `sku`, `barcode`, `purchasePrice/costPrice`, `salePrice/price`, `onlinePrice`, `weight`: row draft fields
- `attributes`: normalized by name/value
- `sortOrder`, `isDefault`, `isActive`, `images`: mapped from matrix state
- `rowVersion`: parent ProductForm only
- `inventory`: no longer authorable

Template generated row:

- `id`: persisted ID only when matched to an existing row; otherwise null until save
- `sku`, `barcode`, `purchasePrice/costPrice`, `salePrice/price`, `onlinePrice`, `weight`: same row draft fields
- `attributes`: generated from option groups, then normalized into the same name/value dictionary
- `sortOrder`, `isDefault`, `isActive`, `images`: mapped the same as manual rows
- `optionGroups`, `optionValues`, frontend keys, temporary IDs: remain UI-only and are not serialized
- `inventory`: no longer authorable

Divergence now starts and ends before DTO mapping: generated rows may have no `variantId`, but backend synchronization canonicalizes that by matching existing rows through `AttributeCombinationKey`.

## DTO Comparison

Canonical authored variant DTO now has the same semantic fields for both paths:

- `variantId`
- `sku`
- `barcode`
- `attributes`
- `price`
- `onlinePrice`
- `costPrice`
- `weight`
- `mediaId`
- `imageUrl`
- `isActive`
- `isDefault`
- `sortOrder`

Runtime-only fields intentionally absent:

- `optionGroups`
- `optionValues`
- `attributeIds`
- `attributeNames`
- frontend keys
- temporary IDs
- inventory/warehouse stock edits
- rowVersion

## Fixes Applied

- Backend variant sync now resolves existing variants by `VariantId` first, then by canonical `AttributeCombinationKey` for generated null-id drafts. For legacy rows with attributes but no stored key, the key is computed from loaded attributes before matching.
- Product response variant references now include `CostPrice`, `Weight`, and `MediaId`.
- Product Units section and readiness items are hidden for all Variant Products, not only variant create mode.
- Variant Matrix direct inventory editing was removed.
- Variant bulk inventory editing was removed.
- Post-authoring inventory update calls from variant authoring were removed.
- Matrix stock is shown as read-only context only.

## Regression Coverage

- `ProductVariantsTab.test.tsx`
  - Generated variants still hand off to parent ProductForm authoring owner.
  - Inventory editing is absent.
  - Weight validation remains active.
  - Bulk price/SKU flows remain covered.
- `ProductForm.test.tsx`
  - Product Units hidden for variant create.
  - Product Units hidden for existing Variant Products.
  - ProductForm remains the only product authoring PUT owner for pending variant drafts.
- `ProductAuthoringVariantTemplateRecoverySourceTests.cs`
  - Backend sync must keep attribute-key fallback matching.
  - Product response variants must expose canonical authoring fields.

## Remaining Observations

- Existing project warnings remain during backend test compilation, including pre-existing nullable/analyzer warnings outside this sprint.
- The sprint did not redesign inventory architecture. Inventory remains owned by opening balance, purchases, transfers, adjustments, and stock mutation workflows.

## Success Criteria

- Manual-created variants and template-generated variants now map into the same canonical authored variant DTO shape.
- Variant templates no longer rely on null-id create/delete behavior when an existing canonical attribute identity is present.
- Product Units are not rendered for Variant Products.
- Variant Matrix no longer edits inventory directly.
- Variant weight round-trips through canonical product response reload.
- Product Authoring remains the single save pipeline.

## Validation

- PASS: `npm test -- --run src/components/ProductVariantsTab.test.tsx src/components/ProductForm.test.tsx`
- PASS: `npm run build`
- PASS: `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter "FullyQualifiedName~ProductAuthoringVariantTemplateRecoverySourceTests"`
