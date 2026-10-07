# MAIN Product Media — Simple, Variable, Cart and Checkout Certification

**Date:** 2026-10-07  
**Overall requested certification: CONDITIONAL PASS**  
**Checkout image remediation: PASS — actually rendered in headed Microsoft Edge.**

Simple-product primary media, distinct variant media, canonical fallback, cart, Checkout and pre-payment Review were verified visibly. The final automated headed sweeps passed **24 Storefront checkpoints and 16 Admin/POS checkpoints**, covering English/LTR, Arabic/RTL, desktop 1440×1000 and mobile 390×844. PostgreSQL, MongoDB, Redis and direct private R2 object reads were independently inspected.

The overall verdict is conditional because a new disposable order could not safely complete the existing WhatsApp phone-verification gate. Post-order success/history image persistence is consequently **not browser-certified**. Existing historical order image snapshots and the media-deletion retention limitation below are not claimed repaired. This is not a full-repository test-suite PASS.

## 1. Root cause and exact reproduced Checkout defect

`CartService.AddItemInternalAsync` originally selected the right product/variant image **only when creating a new line**, persisted it as `CartItemDto.ThumbnailImage`, and never recomputed it in `RevalidateCartProductsAsync`. Revalidation refreshed availability, identity, stock, pricing and promotions but retained the image snapshot indefinitely in Redis.

This causes both missing-image and stale-image defects:

- Add a product before uploading its image: the saved null thumbnail remains null after upload.
- Change primary media: the product API is current, but cart/Checkout still use the previous thumbnail.
- Remove the snapshotted media: the old registered-media URL returns 404; Checkout's existing image component falls back to a placeholder.
- Change a variant's `MediaId`: existing cart lines keep its previous image until the canonical cart mapping is repaired.

### Actual broken browser scenario

| Field | Captured value |
|---|---|
| Tenant | `c9c5c1ae-949e-498e-bc63-07242daeb49d` — AhramStore |
| Simple product | `15e044bc-eaa1-c905-31a4-658aac4ad14b` |
| Internal canonical simple SKU/VariantId | `a9705901-79f8-4bb6-a8a0-9d26b1c2ceaa` |
| Guest cart | `eb93828e-c42e-4258-aaf0-d84e3c27fded` |
| Cart identity | Tenant + session owner + persisted VariantId; ProductId retained for grouping |
| Old thumbnail filename | `b32ab60853ec4c93bd585a7ff802c402.png` |
| Current primary filename after reorder | `95f2a814f3b94951b37bf436a8775150.png` |
| Product DTO | Current `media` and `images` contained the new primary |
| Cart DTO after reload | Still returned old URL in `thumbnailImage` |
| Browser request | `/api/upload/product-media/15e044bc-eaa1-c905-31a4-658aac4ad14b/b32ab60853ec4c93bd585a7ff802c402.png` |
| Browser response after deleting old registration | **404**; request #1479 in the retained CLI request log |
| Checkout UI | Placeholder, despite a valid remaining canonical primary image |

The working comparison was the Storefront product DTO/detail gallery; the broken comparison was the reloaded cart DTO and Checkout image GET. Before deleting the old registration, Checkout visibly retained the old image after a primary reorder. After deletion it requested the dead URL.

The tester's original ProductId/VariantId was not supplied. This report establishes an independently reproduced defect, rather than asserting that an unspecified original cart was inspected. The two initially published AhramStore products had no canonical media in their product DTOs; their placeholders were not treated as storage failures.

### Complete propagation chain

```text
SQL Product + ProductMedia + ProductVariant.MediaId
  -> ProductSearchSqlAuthorityOverlay (single/batch current authority)
  -> ProductMediaImageResolver
  -> StorefrontProductService variant.ImageUrl / product gallery
  -> CartService add/revalidation -> CartItemDto.ThumbnailImage
  -> tenant/owner-scoped Redis JSON cart
  -> GET /api/storefront/cart
  -> normalizeCartResponse / normalizeCartItemResponse
       (thumbnailImage -> imageUrl; name -> productName)
  -> Zustand cartStore hydration
  -> Checkout OrderSummary reads the authoritative cartStore lines
  -> resolveProductImage(item) -> OptimizedImage
  -> browser same-origin GET /api/upload/product-media/{ProductId}/{filename}
  -> tenant/product/registration lookup -> IR2StorageService -> private R2 bytes
```

There is no separate Checkout product-media lookup or independent Checkout image-selection authority. Pricing-preview DTOs are not the source of line images. `checkoutStore` also copies the normalized line image into recovered/session line data. `CheckoutService` copies `ThumbnailImage` to new `OrderItem.ImageUrl`; Admin/Mongo order mappers subsequently preserve that snapshot.

## 2. Canonical media authority and existing security model

| Concern | Exact authority |
|---|---|
| Media writes | PostgreSQL `ProductMedia`, through `ProductMediaService` and the authenticated Admin media controller |
| Bytes | Existing `IR2StorageService`; MAIN runtime is configured R2, not Development file storage |
| Media identity | `ProductMedia.Id` with `TenantId` and `ProductId` |
| Gallery order | `ProductMedia.Position`; add appends, reorder supplies every media ID once and resequences positions |
| Primary image | First nonblank **image** in canonical position order; videos cannot become image thumbnails |
| Variant association | SQL `ProductVariant.Id` / `ProductVariant.MediaId`, pointing into its product's media gallery |
| Database ownership enforcement | Live composite FK `(TenantId, ProductId, MediaId)` -> `ProductMedia(TenantId, ProductId, Id)` confirmed through `pg_constraint` |
| Mongo | Derivative `ProductDocument.Media[]` and `Variants[].MediaId`; not media write authority |
| Projection | `ProductMediaChangedEventConsumer` loads SQL media authority and uses version/event fencing; variant projection carries MediaId |
| Storefront reads | Mongo candidates/Redis cached DTOs are recomposed through current SQL authority before presentation |
| URL generation | Existing upload action returns root-relative registered-media API URL when R2 PublicUrl is empty |
| Delivery | API-proxied media route; the browser does not receive private R2 credentials or signed object URLs |
| Tenant isolation | Trusted host/request tenant resolution, EF filters and explicit tenant/product checks; object key reconstructed from tenant context |

Runtime `PublicUrl` is **not configured**. The existing intentionally anonymous contract is **registered product-media content in the resolved tenant**. The GET requires an exact media registration and a non-deleted owning product. It does not itself impose Published status; draft editor media already participates in this registered-media contract. This existing distinction between public registration and product discoverability was preserved, not broadened.

R2 key format remains:

```text
tenants/{TenantId:D}/products/{ProductId:D}/{32-hex-filename}.png
```

The variant picker uploads/selects product-gallery media and stores its MediaId; variant-specific display does not require a separate variant upload architecture. The general upload endpoint also recognizes variant paths, but that is not the canonical variant-picker workflow certified here.

## 3. Remediation at canonical boundaries

1. Added `ProductMediaImageResolver` for SQL-composed product documents and SQL-only POS product response paths.
2. Cart addition and revalidation use the same resolver. Null, changed and removed thumbnail snapshots are repaired on read and written back to Redis. Existing-line re-addition also refreshes the image.
3. Storefront variant DTO image mapping shares that resolver. Legacy variant image strings cannot override canonical media.
4. POS quick search and quick-access queries now include ProductMedia and map simple/variant/fallback images canonically. They previously read only legacy strings. Barcode response compatibility images now come from canonical gallery mapping.
5. A second real lifecycle failure was fixed: upload/reorder/delete advances the product `xmin` concurrency version. The editor previously retained its old version and Save Matrix returned a conflict after its own uploads.
   - Successful media mutations return previous/current product versions in response headers.
   - The editor advances its accepted version **only when the previous version exactly matches its current accepted version**.
   - Local drafts remain intact; a genuine concurrent-edit version mismatch is not silently accepted.
6. Media mutations invalidate the variant-table gallery read. Selecting newly uploaded media immediately updates the draft preview; clearing an image also clears the draft legacy URL. An unassigned draft displays the current product primary preview.

No Checkout-only fetch, hardcoded application image, permission bypass, public bucket change or storage-provider replacement was introduced. Checkout's rendering component was not changed.

## 4. Explicit simple/variable selection contract

- **Simple:** use product primary canonical image on cards, detail, cart and Checkout. A simple product's internal default SKU does not supersede product primary media.
- **Variable, selected variant:** resolve that precise VariantId, then its MediaId within the owning product's canonical image media.
- **Variable, no assigned/remaining image:** use product primary canonical image.
- **Invalid/foreign VariantId:** not a valid media selector; cart admission/revalidation rejects invalid identity. The resolver returns no image for an unknown variable-product VariantId.
- **No canonical image:** return no canonical thumbnail; the established empty-media presentation remains. Canonical paths do not adopt arbitrary legacy variant URLs.

Primary position is a product fallback, not a replacement for VariantId identity. Variant A and B remain distinct cart lines and cache identities.

## 5. Disposable fixture identities

| Fixture | ProductId | VariantId | Canonical assigned MediaId |
|---|---|---|---|
| Simple, `CERT Media Simple 20261007` | `15e044bc-eaa1-c905-31a4-658aac4ad14b` | `a9705901-79f8-4bb6-a8a0-9d26b1c2ceaa` | None — product primary |
| Variable A | `03144bba-1880-c1d6-3062-997a1006feca` | `1435b729-5fbe-48f1-a306-2e12051ad24b` | `754adc22-3036-4d89-a7e0-65acd6842961` |
| Variable B | Same variable product | `bf1d3df2-7915-4443-b395-4827e3d62fdb` | `876dc858-9f4f-4d5d-88c5-f043f20af275` |
| Variable Fallback | Same variable product | `f510da46-7751-48fc-9ddf-abef2e037662` | Null |

Both products were created through the visible Admin editor. Five units per disposable SKU were admitted using the normal warehouse inventory-admission UI; no existing SKU stock was modified. An initial simple-product create with an empty barcode was rejected as an existing barcode collision; a unique disposable barcode then succeeded. An initial backorder-only add was rejected for zero available stock; normal disposable stock admission then permitted cart addition. Neither guard was weakened.

The variable product has four gallery registrations: primary, red A, blue B, and an extra primary-colored image used to prove A-only image reassignment. The simple product has two retained registrations. Its final primary was deliberately changed to a newly uploaded blue image to prove primary-change propagation. Identical fixture bytes in separate product keys are intentional; product identity is verified by the URL/SQL record, not color alone.

## 6. Simple-product lifecycle evidence

| Check | Result |
|---|---|
| Create in Admin / save | Succeeded after supplying unique disposable identity |
| One image upload | Real file input -> multipart upload -> registration; image decoded in editor |
| Multiple uploads | Gallery retained the existing image and appended another |
| Set/reorder primary | Real Set Primary control persisted gallery positions |
| Save after media mutation | Successful with updated version; no forced reload used as a save workaround |
| Editor reload / product-list reopen | Canonical primary rendered |
| Product detail | Admin list navigation opens the editor/detail workspace; gallery rendered |
| Storefront homepage/list/detail | Canonical primary rendered, including latest changed primary |
| Add/cart/Checkout | Persisted internal simple VariantId; correct primary thumbnail |
| Pre-payment Review | Reached using disposable shipping data; image rendered |
| Change primary after adding | Cart read and Checkout use the changed primary; Redis readback matches |
| Remove previously snapshotted registration | Before fix: 404/placeholder; after fix: remaining primary renders |
| Hard reload | Images remain present |

## 7. Variable-product lifecycle and cache evidence

Real product-gallery uploads, three variant combinations, A/B picker associations, Save Matrix, publish and editor reload succeeded. All three variant-table previews decoded at 240×240. Browser selection A -> B -> Fallback changed the detail hero to the correct assigned/fallback media.

Each variant was added through the visible Add to Cart control. Cart and Checkout showed separate A, B and Fallback lines. Changing **only A** to the fourth gallery item caused a reloaded Checkout to use `6ea677bdd3654ddba3432b61faaeef87.png` for A while B retained `28a2e6b3979d415295b0c2dd7ce08cbd.png` and Fallback retained primary. A was then restored to its red image through the normal picker/save workflow.

The selectors require backend variant resolution. Initial automation clicks issued too early did not produce a cart POST; subsequent settled, separate visible clicks succeeded. CLI script selector/synchronization failures were recorded as test-driver failures, not reported as successful product checkpoints.

### Current cart persistence

Authenticated cart `e6f41372-6c9f-4588-b622-9dc975fa2378` contains the four disposable lines. The owner ID is normalized without hyphens in the tenant/owner-scoped Redis key. A direct Redis read confirmed the exact current filenames:

| Line | Current image filename |
|---|---|
| Simple | `2741f85507354b4b8d379e281d4d03a8.png` |
| A | `846f5d5e863347859f4a747fd71d4842.png` |
| B | `28a2e6b3979d415295b0c2dd7ce08cbd.png` |
| Fallback | `3b7d2db854be4e51a6948845b2953f62.png` |

## 8. POS/Admin evidence

The 16-checkpoint final headed sweep exercised product list, simple editor/gallery, variable editor/gallery/table, and POS quick-access cards in both languages and viewport sizes. Every certified fixture image decoded to 240×240 with nonzero rendered dimensions. Navigation and full page reloads reloaded the same canonical media.

POS search displayed the product primary image, and its variant-selection modal displayed red A, blue B and green fallback distinctly. The POS cart row component does not render product images; adding new thumbnail UI to that surface was not necessary. Admin Orders list/detail currently render textual item information, not product `<img>` elements; their DTO ImageUrl snapshot chain was audited rather than claiming nonexistent image presentation.

## 9. Storefront, cart, Checkout and Review browser evidence

**Browser:** registered `opencode-ui-test`, headed Microsoft Edge, reused after `playwright-cli list`.  
**Runtime:** Admin localhost:5173; AhramStore storefront ahramstore.localhost:5174; API localhost:5010.

Final read-only sweep: homepage, listing, simple detail, variable detail, cart, Checkout × two languages × two viewport sizes = **24 passed checkpoints**. The script checks current product DTO media against actual cart DTO selection, checks the detail hero rather than accepting only gallery thumbnails, scrolls expected images into view, verifies decoded dimensions, and monitors registered-media responses.

Additional interactive checks covered upload/readback, primary reorder, A/B/Fallback selection/addition, variant reassignment, pre-payment Review, hard reload and anonymous/authenticated reads. The guest cart rendered all four lines before login; the supplied customer login then merged them into the authenticated cart and images remained correct.

Screenshot evidence is retained locally under `.artifacts/`:

- `media-cert-checkout-missing-before.png` — reproduced missing-image scenario.
- `media-cert-admin-save-conflict.png` — own-upload concurrency conflict before remediation.
- `media-cert-admin-variants-reload.png` — saved variant media after reload.
- `media-cert-admin-list.png` — canonical Admin list thumbnails.
- `media-cert-pos-variant-selector.png` — distinct A/B/fallback in POS selector.
- `media-cert-checkout-en-desktop-top.png` — visible simple/A/B Checkout lines.
- `media-cert-en-1440-checkout.png`, `media-cert-ar-1440-checkout.png` — desktop summary captures, including bottom lines after internal scrolling.
- `media-cert-en-390-checkout.png`, `media-cert-ar-390-checkout.png` — mobile summary captures, including B/fallback after scrolling.
- `media-cert-review-ar-desktop.png`, `media-cert-review-ar-mobile.png` — pre-payment Review.
- `media-cert-auth-checkout-mobile.png` — authenticated readback.
- `media-cert-pos-{en,ar}-{1440,390}.png` — responsive/localized POS cards.

Some screenshots show only a subset of summary lines because the existing item list is scrollable. Top/bottom captures and per-image scrolling/decoded-image assertions together cover all four lines. Screenshots were visually inspected, including the distinct POS variants and Checkout lines. Artifact files are local evidence, not embedded credentials.

Rerunnable CLI scripts:

```powershell
playwright-cli -s=opencode-ui-test run-code --filename=tests/browser/productMediaLifecycle.cli.js
playwright-cli -s=opencode-ui-test run-code --filename=tests/browser/productMediaAdmin.cli.js
```

They assume the documented retained disposable fixtures and four cart lines. They do not place orders.

## 10. R2 and media-delivery evidence

A temporary **read-only** audit utility loaded existing Development configuration/user-secrets internally, queried only the two fixture products, and used the existing R2 service to read each retained object. It did not print credentials, signed URLs or connection strings.

| Bytes | SHA-256 | Fixture |
|---:|---|---|
| 2711 | `b53e4e748786134b563138784bdc65e0a90bdf21d7364d5d1ce01e7fb034509c` | Green primary/fallback |
| 2829 | `d60b10b37eed0843f0d9f2d49b85360c3ae7fba5ab7cde935dd10c45f3b8fcec` | Red A |
| 2553 | `a1177afe73c31d8a2b131b5a5a6dd7304180c18f602a8bf198ba89072e985c41` | Blue B / final simple primary |

All six retained R2 objects returned `image/png`, correct object ContentLength and matching byte/hash reads. Browser API-media responses returned **200 image/png**, with identical decoded bytes/hashes. The proxied browser response used streaming/chunked delivery and did not expose a Content-Length header; actual body length was checked, not assumed.

Direct object keys matched the tenant/product key format in section 2 and the filename identities in sections 1/7. Runtime R2 readiness was Healthy. No certified image request returned 401/403/404/500, CORS failure or mixed-content failure in the final sweeps. The deliberate pre-fix deleted-media GET returned 404 as expected.

## 11. Cache/projection audit

- **Mongo:** live media counts SQL/Mongo were 2/2 simple and 4/4 variable. Media IDs, URLs, positions and each variant's MediaId agreed. Projection remains derivative and fenced by SQL version/ownership authority.
- **SQL overlay:** cart add uses current single-product authority; cart reads use batch current product/media/variant authority. Sparse/stale Mongo media is replaced by SQL media. Focused cart tests deliberately supply Mongo candidates without gallery data.
- **Redis:** the root cause was an unrefreshed line image snapshot. Corrected cart images were independently read from stored Redis JSON after primary/variant changes.
- **Product caches:** Storefront detail/list services recompose cached candidates with current SQL media; tenant-specific invalidation generations remain in place.
- **Frontend:** the Storefront uses Zustand and local product loading, not React Query for these product/cart surfaces. API cart normalization carries authoritative images to cart and Checkout. The Admin uses separate media/variant-gallery reads; mutation revision invalidation repairs its stale gallery cache.
- **Memoization/state:** variant image selection is keyed by the selected VariantId/MediaId; A-only mutation did not alter B. OptimizedImage resets its source on prop change. Product detail is fetched on route entry; this certification does not promise live push updates to an idle page before a data refresh.
- **Observed transitions:** save/reload, primary change/reload, variant change/reload Checkout, navigate away/back, full browser navigation/reload, and guest-to-authenticated login all retained/currently recomputed images.

## 12. Security and tenant isolation

Actual fresh anonymous browser context checks:

| Request | Result |
|---|---|
| Registered AhramStore media, no POS/customer auth | **200** |
| Same host with forged foreign X-Tenant-Id | **403** |
| Anonymous media-registration mutation | **401** |
| Different live tenant host requesting AhramStore media URL | **404** |

The alternate requesting tenant cannot receive AhramStore media even when it knows the image URL. SQL's composite variant-media FK prevents foreign tenant/product associations. Existing automated registered-media tests also verify another tenant receives NotFound and cannot invoke storage for unregistered content. Cart identity tests reject mismatched/cross-tenant variants.

No permission policy, anonymous mutation rule, tenant check, storage ACL, object visibility setting or host authority was removed. A supplied image URL is not used as tenant authority. Current resolver tests reject foreign/unmatched media references and do not let legacy URLs win.

## 13. Order-related chain and safe checkout limitation

The guest flow reached Shipping -> COD Payment -> **Review** using clearly disposable name/address/notes. The summary rendered correct simple/A/B/fallback media. **Place Order remained disabled by the existing WhatsApp OTP verification requirement.** No code was sent to the disposable phone, no verification gate was bypassed, and no payment/refund was performed.

Source trace:

- `CheckoutService`: new order line `ImageUrl = item.ThumbnailImage`.
- SQL OrderItem image is a stored order snapshot; order DTO/projection mappers carry ImageUrl.
- `AdminOrderService`: maps existing SQL order line ImageUrl into AdminOrderItemDto.
- Storefront order DTO/hook/`OrderItemsList`: presents the stored image snapshot.

**Not certified:** creation/readback of a new order, order-success/history rendering of these fixture products, and historical orders predating this remediation. Existing historical null/stale snapshots were not rewritten. Also, deleting a ProductMedia registration makes its registered-media API URL unavailable even when an old order snapshot references that URL; long-term order-image retention needs an explicit retained-media lifecycle contract. It was not solved by weakening registration/tenant checks or by making R2 public.

## 14. Automated tests, production builds and classified failures

| Verification | Final result |
|---|---|
| Backend focused product/media/cart/resolver/mapper tests | **37 passed, 0 failed, 0 skipped** |
| Admin ProductForm, media-manager and variant authoring tests | **82 passed, 0 failed** |
| Storefront OrderSummary, cart response, cart persistence, checkout validation tests | **28 passed, 0 failed** |
| Full solution Release build | Succeeded, 0 errors; compiler warnings suppressed in the final quiet command |
| Admin production build | Succeeded; existing large-bundle warning |
| Storefront `tsc && vite build` | Succeeded; existing Browserslist/dynamic-import/chunk warnings |
| Headed final Storefront sweep | **24 passed checkpoints**, no recorded media response failures |
| Headed final Admin/POS sweep | **16 passed checkpoints**, no recorded media response failures |
| Whitespace validation | `git diff --check` succeeded; Windows LF/CRLF notices only |

New focused regressions cover missing/stale Redis thumbnail repair, primary changes, selected-variant changes, deleted-media fallback, Redis persistence, simple default-SKU behavior, variant isolation, rejected unknown identities, legacy suppression, SQL-only POS mappings, and accepted editor-version chaining without accepting concurrent versions. Existing relevant tests cover anonymous registered-media access, cross-tenant denial, cart identity, normalization/Checkout rendering and reload-state behavior.

Representative executed commands:

```powershell
dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" -c Release --filter "FullyQualifiedName~ProductMediaImageResolverTests|FullyQualifiedName~CartItemIdentityContractTests|FullyQualifiedName~ProductMediaUploadContractTests|FullyQualifiedName~ProductResponseMapperTests" --no-restore -v quiet -p:WarningLevel=0
dotnet build "PointOfSale.sln" -c Release --no-restore -v quiet -p:WarningLevel=0
# In PointOfSale.Web:
npm test -- --run src/components/ProductForm.test.tsx src/components/ProductMediaManager.test.tsx src/components/ProductVariantsTab.test.tsx src/components/ProductVariantsTabWave4.test.tsx --maxWorkers=1
npm run build
# In PointOfSale.Storefront:
npm test -- --run src/components/checkout/OrderSummary.test.tsx src/services/api/cartResponse.test.ts src/store/cartStore.persistence.test.ts src/store/checkoutStore.validation.test.ts src/utils/resolveProductImage.test.ts --maxWorkers=1
npm run build
```

The Storefront command matched **four** existing test files/28 tests; `src/utils/resolveProductImage.test.ts` did not exist and is not counted as a passing file.

### Failures classified, not hidden

- First Debug build could not replace a DLL held by the running Windows API. Verified API restart resolved the lock.
- Initial Admin run while compilation ran concurrently: 45 passed/3 timeout failures. A single-worker rerun passed all 48 original selected tests; after new regressions the final selection passed 82. No timeout threshold was raised.
- Broader `ProductDetailMediaPublicProjectionCertificationTests`: **2 passed, 2 failed**. Failures assert source substrings in unchanged `SeoService` and `ProductSearchService` (`Cached product slug resolution rejected...`, `sqlProjection is { IsActive: true, IsOnlineVisible: true }`). They are unchanged-file/source-string expectation failures, not a claimed green broader suite. The media-projection and Storefront detail assertions passed.
- New POS fixture test initially omitted the required category navigation; its product was filtered by the required EF relationship. Correctly seeded fixture then passed. Intermediate local audit/CLI syntax/selector errors were corrected before final evidence.
- Overall readiness remains **503** from `payout_external_truth_readiness` reporting a reconciliation mismatch. PostgreSQL, MongoDB, Redis, RabbitMQ, Elasticsearch and R2 readiness were Healthy. This unrelated financial readiness condition was not changed.

The full repository test suite was not run.

## 15. Final limitations, fixture state and verdict

**CONDITIONAL PASS for the complete requested audit/certification.** The current-product media propagation/display defect is remedied and Checkout rendering is proven in a real headed browser. All certified simple/variable/variant/fallback/cart/Checkout/Review image checks succeeded across the specified languages, directions and viewports, with correct private R2 delivery and tenant controls.

Remaining conditions:

1. Safely complete the existing phone-verification ceremony for a disposable COD order, then certify new order-detail/success/history persistence. This session stopped at Review.
2. Historical order snapshots and registered-media deletion retention are not fully certified/remediated; see section 13.
3. The two unrelated architecture source-string checks and unrelated financial readiness failure remain.
4. This was not an exhaustive audit of every independent marketplace/affiliate image renderer; the canonical Admin, POS and Storefront product/cart/Checkout paths listed above were traced and verified.

The two clearly named disposable products, their stock/media and the four disposable cart lines remain for reproducibility. Only these fixtures were mutated. A removed temporary media registration left its exact R2 object under the existing uncoupled deletion behavior; no broad object cleanup was performed. Existing real products/orders and unrelated worktree changes were preserved. No commits were created.
