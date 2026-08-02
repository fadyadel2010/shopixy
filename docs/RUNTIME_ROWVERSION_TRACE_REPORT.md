# Runtime RowVersion Trace Report

Date: 2026-07-31

Scope: investigation only. No architecture changes, payload changes, frontend refactors, backend refactors, or speculative fixes were made. The only code changes made during this investigation are diagnostic logs.

## Executive Result

The duplicate parent `ProductForm` PUT is not the current failing request. The failing request is the first canonical product authoring PUT issued from `ProductVariantsTab.saveMatrixChanges()`.

Runtime evidence provided for the current failure:

```text
[ProductVariantsTab] Matrix product PUT starting
expectedRowVersion = 712721

PUT /api/products/{id}

HTTP 409 Conflict
code = CONFLICT
data.status = ConcurrencyConflict
data.errors[0].field = expectedVersion
data.errors[0].message = Product has been changed by another transaction.
```

Important limitation: I could not reproduce the live request locally because the failing product id, authenticated browser session, and live database state were not available in this workspace. Therefore, the exact live `Database RowVersion = ...` value is not yet captured. Diagnostic logs have been added to capture it on the next live reproduction.

## 1. Frontend RowVersion Timeline

### GET Product to API DTO

Backend response DTO maps `Product.RowVersion` to a string:

- `PointOfSale.API/Mappers/SafeResponseMapper.cs:100` maps `RowVersion = product.RowVersion.ToString()`.
- `PointOfSale.API/Mappers/SafeResponseMapper.cs:160` maps the barcode response the same way.

The authoritative entity field is:

- `PointOfSale.Core/Entities/Product.cs:217` declares `public uint RowVersion { get; set; }`.

### ProductForm Ingress

`ProductForm` loads product details:

- `PointOfSale.Web/src/components/ProductForm.jsx:521` defines `fetchProduct()`.
- `PointOfSale.Web/src/components/ProductForm.jsx:524` calls `api.get(`/products/${productId}`)`.
- `PointOfSale.Web/src/components/ProductForm.jsx:529` assigns `setAcceptedRowVersion(product.rowVersion || product.RowVersion || null)`.

`ProductForm` stores accepted row version only in `acceptedRowVersion`:

- `PointOfSale.Web/src/components/ProductForm.jsx:316` declares `const [acceptedRowVersion, setAcceptedRowVersion] = useState(null)`.
- `PointOfSale.Web/src/components/ProductForm.jsx:851`, `:922`, and `:934` update it after successful matrix/product saves.

### ProductForm to ProductVariantsTab

`ProductForm` passes a product authoring snapshot callback to the variants tab:

- `PointOfSale.Web/src/components/ProductForm.jsx:1375-1380` renders `ProductVariantsTab` with `getProductAuthoringSnapshot={buildProductAuthoringPayload}`.

But `buildProductAuthoringPayload()` does not include `rowVersion`:

- `PointOfSale.Web/src/components/ProductForm.jsx:751-768` builds product authoring payload fields only.
- `PointOfSale.Web/src/components/ProductForm.jsx:766` explicitly sends `productUnits: hasVariants ? null : buildProductUnitsPayload()`.

Therefore, the variants tab does not receive `ProductForm.acceptedRowVersion` through this callback.

### ProductVariantsTab Ingress

`ProductVariantsTab` independently loads its own product snapshot:

- `PointOfSale.Web/src/components/ProductVariantsTab.jsx:699` defines `fetchVariants()`.
- `PointOfSale.Web/src/components/ProductVariantsTab.jsx:702-705` calls three reads in parallel, including `api.get(`/products/${productId}`)`.
- `PointOfSale.Web/src/components/ProductVariantsTab.jsx:713` assigns `nextProduct = productResponse.data || null`.
- `PointOfSale.Web/src/components/ProductVariantsTab.jsx:716` assigns `setParentProduct(nextProduct)`.

### ProductVariantsTab saveMatrixChanges()

The failing first PUT is built here:

- `PointOfSale.Web/src/components/ProductVariantsTab.jsx:1517` uses `parentProduct` if available; otherwise it fetches `/products/${productId}`.
- `PointOfSale.Web/src/components/ProductVariantsTab.jsx:1518-1524` overlays the form snapshot onto that product object.
- `PointOfSale.Web/src/components/ProductVariantsTab.jsx:1530` chooses `rowVersion = product.rowVersion || product.RowVersion || null`.
- `PointOfSale.Web/src/components/ProductVariantsTab.jsx:1537-1538` sets `requestOptions.headers['If-Match'] = `"${rowVersion}"``.
- `PointOfSale.Web/src/components/ProductVariantsTab.jsx:1562` sends `api.put(`/products/${productId}`, productData, requestOptions)`.

Source-backed conclusion: the matrix PUT row version comes from `ProductVariantsTab.parentProduct` unless `getProductAuthoringSnapshot()` supplies a `rowVersion`. Current `ProductForm.buildProductAuthoringPayload()` does not supply one.

## 2. Backend RowVersion Timeline

`ProductsController.UpdateProduct()` requires version evidence before mapping:

- `PointOfSale.API/Controllers/ProductsController.cs:327-331` calls `RequireExpectedVersion()`.
- `PointOfSale.API/Controllers/ProductsController.cs:1141-1168` accepts `If-Match` or `Expected-Version`.
- `PointOfSale.API/Controllers/ProductsController.cs:1170-1175` parses quoted or unquoted uint-range values.

`ProductAuthoringCommandMapper` extracts the expected version:

- `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringCommandMapper.cs:442-463` reads `If-Match` first, then `Expected-Version`.
- `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringCommandMapper.cs:450-453` returns parsed `If-Match`.
- `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringCommandMapper.cs:456-459` returns parsed `Expected-Version`.
- `PointOfSale.Core/Application/ProductAuthoring/ProductAuthoringCommand.cs:120` stores it as `ProductAuthoringMetadata.ExpectedVersion`.

`ProductAuthoringOrchestrator` compares expected version before mutation:

- `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:270-275` rejects stale update commands before applying changes.
- `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:1517-1519` defines stale as `product.RowVersion != expectedRowVersion`.

If not stale, EF original value is set before `SaveChangesAsync()`:

- `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:480` calls `ApplyExpectedProductRowVersion(command, product)`.
- `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:1521-1531` assigns EF `OriginalValue = expectedRowVersion`.
- `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:497-503` maps `DbUpdateConcurrencyException` to the observed `"Product has been changed by another transaction."` error.

## 3. Actual HTTP Headers

For the provided runtime row version value `712721`, source proves this request shape:

```text
If-Match: "712721"
Expected-Version: absent
Idempotency-Key: variant-authoring:{productId}:matrix:{random}
```

Evidence:

- `PointOfSale.Web/src/components/ProductVariantsTab.jsx:1531-1534` creates headers with `Idempotency-Key`.
- `PointOfSale.Web/src/components/ProductVariantsTab.jsx:1537-1538` adds quoted `If-Match` when row version exists.
- No `Expected-Version` assignment exists in this path.

Additional diagnostic frontend log now added:

- `PointOfSale.Web/src/components/ProductVariantsTab.jsx:1544-1558` logs `concurrencyHeaders.ifMatch`, `concurrencyHeaders.expectedVersion`, and `rowVersionSource`.

## 4. Database RowVersion

Not yet captured from the live failing request.

Diagnostic backend logs now added:

- `PointOfSale.API/Controllers/ProductsController.cs:343-347` logs controller raw `If-Match` and `Expected-Version`.
- `PointOfSale.API/Controllers/ProductsController.cs:355-362` logs mapper `ExpectedVersion` and whether headers match the mapped value.
- `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:1534-1540` logs `CommandExpectedVersion`, current database-loaded `Product.RowVersion`, and `ExpectedEqualsCurrent` immediately before `IsStale()`.
- `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:1546-1554` logs EF original/current row version immediately before `SaveChangesAsync()`.
- `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:1559-1567` logs EF original/current row version if EF throws `DbUpdateConcurrencyException`.

On next reproduction, grep API logs for:

```text
RUNTIME_ROWVERSION_TRACE
```

The required proof line will look like:

```text
RUNTIME_ROWVERSION_TRACE | ProductAuthoringOrchestrator.BeforeIsStale | ... CommandExpectedVersion: 712721 | DatabaseRowVersion: <live value> | ExpectedEqualsCurrent: False
```

or:

```text
RUNTIME_ROWVERSION_TRACE | ProductAuthoringOrchestrator.BeforeIsStale | ... CommandExpectedVersion: 712721 | DatabaseRowVersion: 712721 | ExpectedEqualsCurrent: True
```

## 5. Source of Stale Value

Runtime source line for the submitted value:

- `PointOfSale.Web/src/components/ProductVariantsTab.jsx:1530` selects `product.rowVersion || product.RowVersion || null`.

The selected `product` is constructed from:

- `PointOfSale.Web/src/components/ProductVariantsTab.jsx:1517` `parentProduct ? { data: parentProduct } : await api.get(...)`.
- `PointOfSale.Web/src/components/ProductVariantsTab.jsx:1521-1524` `{ ...productResponse.data, ...(formSnapshot || {}) }`.

The original `parentProduct` row version came from:

- `PointOfSale.Web/src/components/ProductVariantsTab.jsx:702-705` GET `/products/${productId}`.
- `PointOfSale.Web/src/components/ProductVariantsTab.jsx:713` `nextProduct = productResponse.data || null`.
- `PointOfSale.Web/src/components/ProductVariantsTab.jsx:716` `setParentProduct(nextProduct)`.

Source-backed conclusion: if the live backend log shows `ExpectedEqualsCurrent: False`, the stale value originated from `ProductVariantsTab.parentProduct` loaded by the variants tab GET, not from a fresh pre-save product GET and not from `ProductForm.acceptedRowVersion`.

Not yet proven by runtime logs: which request advanced the database row version after that variants-tab GET.

## 6. If Expected Equals Database RowVersion

If the next runtime trace shows `ExpectedEqualsCurrent: True` before `IsStale()`, the investigation must continue at EF concurrency.

Relevant EF configuration:

- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:1716-1718` maps `Product.RowVersion` to PostgreSQL `xmin` and marks it as `.IsRowVersion()`.
- `PointOfSale.Infrastructure/Migrations/20260729150000_ProductAuthoringXminConcurrency.cs:17-18` documents adoption of PostgreSQL `xmin` for product authoring concurrency.
- `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:1528-1530` sets EF `OriginalValue` before save.

The newly added `BeforeSaveChanges` and `DbUpdateConcurrencyException` logs will show whether EF original/current values match the command and product values.

## 7. Hidden Mutation Before Update Product

Observed matrix-save sequence from source:

1. `ProductForm.handleSubmit()` calls `variantsTabRef.current.savePendingChanges()` before the parent product PUT (`PointOfSale.Web/src/components/ProductForm.jsx:837-839`).
2. `ProductVariantsTab.savePendingChanges()` calls `saveMatrixChanges()` only when matrix mode has unsaved changes (`PointOfSale.Web/src/components/ProductVariantsTab.jsx:1629-1635`).
3. `saveMatrixChanges()` sends the canonical product PUT first (`PointOfSale.Web/src/components/ProductVariantsTab.jsx:1562`).
4. Only after the product PUT succeeds does matrix inventory handling send variant stock PUTs (`PointOfSale.Web/src/components/ProductVariantsTab.jsx:1562-1598`).

Therefore, variant matrix inventory updates do not occur before the failing first product PUT in this path.

Other frontend product-adjacent mutation paths found:

- Parent product authoring PUT: `PointOfSale.Web/src/components/ProductForm.jsx:919`.
- Product media add/delete/reorder/video: `PointOfSale.Web/src/components/ProductMediaManager.jsx:241`, `:282`, `:303`, `:347`.
- Pixel override: `PointOfSale.Web/src/components/ProductPixelOverrideTab.jsx:157`.
- Affiliate settings: `PointOfSale.Web/src/services/adminProductAffiliateApi.js:62`, `:73`, `:85`, `:96`, `:104`.
- Legacy modal product units: `PointOfSale.Web/src/components/ProductUnitsModal.jsx:84`, `:105`, `:117`.
- Legacy variant direct create/update/delete: `PointOfSale.Web/src/components/ProductVariantsTab.jsx:969`, `:972`, `:1005`.
- Variant stock changes: `PointOfSale.Web/src/components/ProductVariantsTab.jsx:1465`, `:1598`.
- Product stock modal: `PointOfSale.Web/src/components/ProductStockModal.jsx:42`.

Backend paths that can touch the `Product` row and therefore advance `xmin`:

- Product authoring sets `product.UpdatedAt = DateTime.UtcNow` during apply (`PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:1040`).
- Product media add/delete/reorder calls `TouchProductForMediaProjection(product, changedAtUtc)` and saves (`PointOfSale.API/Services/ProductMediaService.cs:103`, `:159`, `:223`, `:299-302`).
- Legacy `ProductVariantService` direct variant paths set `product.UpdatedAt = DateTime.UtcNow` (`PointOfSale.API/Services/ProductVariantService.cs:380`).

Runtime not yet captured: no available log proves one of these paths ran between the product GET and failing matrix PUT.

## 8. Product Units Participation Analysis

Product Units still render for existing variant products:

- `PointOfSale.Web/src/components/ProductForm.jsx:1690` renders product units in modal mode when `!isVariantCreateMode`.
- `PointOfSale.Web/src/components/ProductForm.jsx:1719` renders product units in workspace mode when `!isVariantCreateMode`.
- `isVariantCreateMode` means create mode plus variants, so existing variant products still render this section.

For canonical product authoring payload:

- `PointOfSale.Web/src/components/ProductForm.jsx:766` sends `productUnits: hasVariants ? null : buildProductUnitsPayload()`.

Source-backed conclusion: for an existing variant product, the visible Product Units section is not submitted in the canonical Product Authoring payload. It can update local React state if edited, but `buildProductAuthoringPayload()` sends `productUnits: null` while `hasVariants` is true.

Backend unit endpoints do participate in Product Authoring when called directly:

- `PointOfSale.API/Controllers/ProductsController.cs:1355-1448` add unit path delegates to Product Authoring.
- `PointOfSale.API/Controllers/ProductsController.cs:1474-1564` update unit path delegates to Product Authoring.
- `PointOfSale.API/Controllers/ProductsController.cs:1587-1643` delete unit path delegates to Product Authoring.

However, the rendered ProductForm Product Units section does not call those endpoints directly. The separate legacy `ProductUnitsModal` does call them, but source did not show that modal in the current ProductForm path.

## Root Cause

Proven so far:

1. The failing first product PUT uses the row version selected at `PointOfSale.Web/src/components/ProductVariantsTab.jsx:1530`.
2. That row version is sourced from `ProductVariantsTab.parentProduct`, loaded at `PointOfSale.Web/src/components/ProductVariantsTab.jsx:702-716`, because the parent form snapshot does not carry `rowVersion`.
3. The request sends `If-Match: "712721"` and no `Expected-Version`.
4. The observed backend error text is produced only after EF concurrency conflict in `ProductAuthoringOrchestrator.CommitProductAuthoringTransactionAsync()` (`PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:497-503`), not by the earlier `IsStale()` branch whose message is `"Product has been changed by a newer authoring command."`

Not yet proven:

1. Whether the live database row version was already different from `712721` before `IsStale()`.
2. If it was different, which exact earlier request advanced it.
3. If it was equal, which EF original/current/concurrency-token condition caused `DbUpdateConcurrencyException`.

Current root-cause status: incomplete pending one instrumented live reproduction. The most precise source-backed suspect is stale `ProductVariantsTab.parentProduct.rowVersion`; the exact stale-producing mutation must be identified from the new `RUNTIME_ROWVERSION_TRACE` API logs and the enhanced `[ProductVariantsTab] Matrix product PUT starting` browser log.

## Verification

Commands run:

```text
dotnet build PointOfSale.API\PointOfSale.API.csproj
npm run build
npm run test -- ProductVariantsTab.test.tsx ProductForm.test.tsx --run
```

Results:

- API build passed with 0 errors. Existing warnings remain.
- Web build passed.
- Focused frontend tests passed: 2 files, 23 tests.

