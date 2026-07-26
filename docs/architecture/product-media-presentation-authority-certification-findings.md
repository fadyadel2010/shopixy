# PASS SF-PA3.5 Product Media Presentation Authority Certification Findings

Date: 2026-06-28
Runtime certified: `PointOfSale.Storefront`

## Scope Reviewed

Storefront frontend files reviewed:
- `PointOfSale.Storefront/src/pages/ProductDetailsPage.tsx`
- `PointOfSale.Storefront/src/components/sections/defaults/product-hero.tsx`
- `PointOfSale.Storefront/src/components/sections/defaults/product-gallery.tsx`
- `PointOfSale.Storefront/src/components/sections/defaults/product-video.tsx`
- `PointOfSale.Storefront/src/components/products/ProductMediaGallery.tsx`
- `PointOfSale.Storefront/src/components/products/ProductMediaViewer.tsx`
- `PointOfSale.Storefront/src/components/products/ProductGalleryModal.tsx`
- `PointOfSale.Storefront/src/components/products/productMediaGalleryUtils.ts`
- `PointOfSale.Storefront/src/hooks/useVariantSelector.ts`
- `PointOfSale.Storefront/src/types/product.ts`
- `PointOfSale.Storefront/src/services/api/storefrontApi.ts`

Backend endpoints reviewed because Storefront Product Detail media consumes them:
- `GET /api/storefront/products/{slug}`
- `GET /api/storefront/products/by-id/{id}`
- `POST /api/storefront/products/by-id/{id}/variant-resolution`

Backend services reviewed:
- `PointOfSale.API/Controllers/StorefrontProductsController.cs`
- `PointOfSale.API/Services/Storefront/StorefrontProductService.cs`
- `PointOfSale.API/DTOs/Storefront/StorefrontDtos.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Infrastructure/Consumers/ProductMediaChangedEventConsumer.cs`

CommerceHub, Marketplace, upload pipeline, CDN implementation, image optimization, admin/media management screens, and unrelated affiliate runtime were not certified by this pass.

## Findings

Media authority is backend-owned. Storefront Product Detail media now renders only backend-admitted `ProductReadDto.Media` entries. The previous React fallback that converted legacy `product.images` into synthetic gallery items with generated `legacy-image-*` IDs was removed.

Primary image authority is backend-owned. The visible primary media is the active item from the backend media DTO ordered by backend position. Storefront may hold `activeIndex` only.

Variant media authority is backend-owned. Variant resolution re-enters backend authority and returns a backend variant with `MediaId`; frontend can prefer that admitted media ID but cannot resolve media ownership or invent a replacement.

Gallery order is backend-owned. SQL media `Position` is preserved through projection, DTO mapping, and frontend sorting. Frontend carousel position does not reorder the gallery.

Video authority is backend-owned. SQL media recomposition/projection now admits `image` and `video` media. Backend DTO mapping composes provider presentation evidence (`presentationUrl`, `thumbnailUrl`); frontend no longer derives YouTube embed or thumbnail URLs.

Failure behavior is fail closed. Missing media renders no product media; missing provider playback evidence withholds video preview. Malformed/stale cache/projection candidates must re-enter backend SQL authority or disappear.

## Runtime Pipeline

Resolved Product
-> Backend SQL ProductMedia / Variant MediaId authority
-> ProductSearchSqlAuthorityOverlay / ProductMediaChangedEventConsumer
-> StorefrontProductService DTO mapping
-> ProductReadDto.Media admission
-> ProductMediaGallery / ProductMediaViewer / ProductGalleryModal / product-video presentation

Media Authority begins at current SQL `ProductMedia` and variant `MediaId`.
Media Composition begins in backend DTO mapping.
Storefront presentation begins after `ProductReadDto.Media` is admitted by the backend response.

## Boundary Review

Media Boundary: backend SQL/service authority; recovery source is SQL; failure withholds media.
Gallery Boundary: backend `Position`; recovery source is SQL overlay/projection; failure withholds stale/malformed entries.
Variant Media Boundary: backend variant resolver plus `MediaId`; recovery source is backend product read; failure clears selected variant media.
Video Boundary: backend media DTO `url`/`presentationUrl`; recovery source is SQL media; failure withholds video.
Presentation Boundary: Storefront carousel/zoom/fullscreen/rendering; recovery source is fresh backend read; failure shows empty media state.
Recovery Boundary: backend product read re-entry; Redis/Mongo/Search/browser state are derivative only.
Projection Boundary: Mongo/Search copy SQL media only; not media authority.

## Runtime Hardening Performed

- Removed React gallery construction from legacy `product.images`.
- Removed frontend YouTube thumbnail/embed source derivation from Product Detail media presentation.
- Added backend-composed `ProductMediaDto.PresentationUrl` and `ProductMediaDto.ThumbnailUrl`.
- Allowed current SQL media overlay and media projection to admit backend `video` media as well as images.
- Narrowed Product Detail video posters to admitted image media.
- Added architecture regression freezes for the Storefront media presentation boundary.

## Files Modified

Runtime files:
- `PointOfSale.Storefront/src/components/products/productMediaGalleryUtils.ts`
- `PointOfSale.Storefront/src/components/products/ProductMediaGallery.tsx`
- `PointOfSale.Storefront/src/components/products/ProductMediaViewer.tsx`
- `PointOfSale.Storefront/src/components/products/ProductGalleryModal.tsx`
- `PointOfSale.Storefront/src/components/sections/defaults/product-video.tsx`
- `PointOfSale.Storefront/src/types/product.ts`
- `PointOfSale.API/DTOs/Storefront/StorefrontDtos.cs`
- `PointOfSale.API/Services/Storefront/StorefrontProductService.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Infrastructure/Consumers/ProductMediaChangedEventConsumer.cs`

Regression freezes:
- `PointOfSale.Tests/Architecture/StorefrontCatalogPresentationAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/ProductDetailMediaPublicProjectionCertificationTests.cs`

Architecture/context updates:
- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/product-media-presentation-authority-certification-findings.md`

## Certification Answer

PointOfSale.Storefront Product Media Presentation is production-certifiable for the inspected runtime after hardening. Backend exclusively owns Product Media, Primary Image, Gallery Order, Variant Media, Video, Thumbnail, Media Composition, projection, recovery, and failure behavior. Storefront frontend cannot construct galleries, reorder media authority, fabricate video sources, or make Redis/Mongo/browser/React state media authority.
