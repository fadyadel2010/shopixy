# Main Storefront Homepage / Theme Chrome / Media Authoring Certification

Date: 2026-10-07  
Workspace: `PointOfSale`  
Browser: **headed Microsoft Edge**, existing `playwright-cli` session **`opencode-ui-test`**  
Admin: `http://localhost:5173/`  
API: `http://localhost:5010/`  
Storefront: `http://ahramstore.localhost:5174/`

## Final verdict

**IMPLEMENTED AND FUNCTIONALLY VERIFIED; FULL REQUESTED CERTIFICATION IS NOT PASS.**

The two reported authoring problems are fixed, and the headed upload/announcement journey passed on all **10 supported themes**. English/Arabic, LTR/RTL, 1600×1000 desktop and 390×844 mobile were exercised. Saved-draft discard, immutable asset replacement, failed upload and whole-StoreConfig rollback were additionally verified.

Two broader acceptance gaps remain:

1. **The environment's MongoDB `storefront_configs` projection is stale.** Its last observed configuration update was `2026-09-24T16:27:22.149Z`, and its last event timestamp was `2026-09-24T16:25:57.690Z`. SQL/current Storefront delivery are correct; the canonical projection mapper recomposes the correct media reference, and focused consumer tests pass. End-to-end delivery to this particular projection was not demonstrated. Navigation's separate projection was verified after adding its missing rollback consumer.
2. **The existing Admin Homepage preview renders a simplified Admin section renderer**, rather than the complete selected Storefront theme/header/footer shell. The uploaded draft image now appears in that preview, but theme-faithful draft-preview pixels and global Header chrome inside the Homepage preview are not certified. Actual published Storefront theme pixels were verified separately for all supported themes.

These gaps must not be hidden by a blanket PASS. See §16 for other bounded limitations.

No commits were created. Existing unrelated changes were retained. Browser credentials are intentionally absent from this report and test scripts.

## 1. URL-only Background Image: exact root cause

The merchant Homepage is the StoreConfig builder, **not** the platform/CommerceHub `HomepageSection` management system.

Canonical content contract:

- `theme-authority/shopixy-theme-authority.json`: `hero-banner` settings, including `backgroundImage`, typed `image`, default `""`.
- `PointOfSale.Core/Constants/StorefrontSectionContracts.cs`: server admission/validation of canonical section settings.
- `PointOfSale.Web/src/modules/storefront/builder/sectionContractRegistry.js`: materializes the editor schema.
- `PointOfSale.Web/src/modules/storefront/components/builder/SettingsPanel.jsx`: selects the schema form for the selected section.
- `.../SchemaSettingsForm.jsx`, `case 'image'`: previously rendered **only a text input** with an example image URL and an `<img>` thumbnail. There was no file input, upload request, media registration or media reference model for Homepage backgrounds.

Thus the root cause was a missing media authoring contract and upload control, rather than an R2 storage outage or a hidden file-picker setting.

**Real saved URL evidence:** AhramStore's initial `hero-banner.backgroundImage` was:

`https://m.media-amazon.com/images/I/61Ud8rdu8sL._AC_SL1500_.jpg`

It rendered in the editor before uploading and was restored and decoded in the live Storefront after certification. There was no silent URL migration or deletion.

## 2. Announcement: forensic ownership proof and explicit answers

Selected theme at the start: **Alizon**.

Rendering path:

```text
StoreConfig.CustomizationJson.storefrontTheme
  -> GET /api/storefront/store-config (current tenant SQL authority)
  -> StorefrontDraftConfigMapper.MapLive
  -> StoreConfig Zustand / ThemeProvider / ThemeResolver / ThemeRegistry
  -> StorefrontLayout / ThemeLayoutResolver.resolveHeaderComponent
  -> HeaderRuntime in storefront/runtime/layout/LayoutRuntime.tsx
  -> themes/alizon/layout/header.tsx
  -> themes/alizon/layout/AnnouncementMarquee.tsx
  -> alizonTokens.ts presentation classes
```

Before this implementation, `HeaderRuntime` populated `labels.announcement` using `t('announcement.defaultText')`, and Alizon's header invoked `<AnnouncementMarquee text={labels.announcement} />`. The component's `enabled` prop defaulted to `true`. This is rendered by a global Header shell, not by Homepage's section composition.

| Question | Proven answer before implementation |
|---|---|
| 1. Owner? | **Header content, with Theme-owned presentation.** Shared Header runtime provides copy; each themed Header chooses its visual primitive. StoreConfig is storage/publication authority, not the semantic content owner. |
| 2. Where is text stored? | `PointOfSale.Storefront/src/i18n/locales/en.json` and `ar.json`, `announcement.defaultText`. English default is “Limited Edition Collection.” No merchant text in StoreConfig. |
| 3. Where is visibility stored? | Nowhere in tenant data. Component prop default `enabled = true`; empty resolved text also suppresses it. |
| 4. Where is link stored? | Nowhere; original marquee had no merchant destination and no link prop. |
| 5. Where is style stored? | Each theme's `layout/*Tokens.ts` and marquee/Header classes. Cosmetics uses an independent static strip. |
| 6. Is it hardcoded? | Promotional default is fixed application translation copy, not a merchant-authored value. Appearance/visibility are component defaults. It is not an SQL field. |
| 7. Generated from theme defaults? | The theme chooses whether to render the strip and its tokens. Copy comes from the shared translation default, not `defaultThemeConfig` in the authority JSON. |
| 8. Configurable but not exposed? | Components had text/textAr/enabled/accentClass props, but no end-to-end persisted authoring surface passed merchant values. A prop alone was not a merchant feature. |
| 9. Inherited old schema/version? | No evidence of an old merchant announcement schema. The feature originated as theme presentation defaults; legacy StoreConfig documents have no announcement node. |

Ownership was established before UI changes. No announcement setting was added to Homepage.

### New authoritative path

```text
Admin Navigation > Header controls
  -> AdminNavigationRequest.Announcement
  -> NavigationAdmissionService + HeaderAnnouncementValidator
  -> CustomizationJson.navigation.draft.header.announcement
  -> existing atomic Header/Footer navigation publish
  -> CustomizationJson.navigation.live.header.announcement
  -> StoreConfig version snapshot + NavigationPublishedEvent
  -> GET /api/storefront/navigation (SQL/live only)
  -> navigationStore -> HeaderRuntime -> selected themed Header
```

Configuration fields: `enabled`, `text`, `textAr`, `link`, `backgroundColor`, `textColor`. The shared configuration model is `PointOfSale.Core/Models/HeaderAnnouncementConfig.cs`.

Missing/null configuration preserves the old theme default. An explicitly authored empty text is empty, not silently replaced with the promotional default. “Use theme announcement default” clears the authored override. Empty appearance overrides retain each theme's visual default.

## 3. Supported-theme audit matrix

Authority is `theme-authority/shopixy-theme-authority.json`. Backend `StorefrontThemes.All`, Storefront `ThemeRegistry` and Admin `themeMarketplaceRegistry` all apply official/active/availability rules. There are **10 supported themes**, not 11.

In the following table, “before → after” describes merchant configurability and Admin exposure. `L/R` means English/Arabic localization and LTR/RTL rendering were checked; `D/M` means desktop/mobile checks passed for the delivered image/announcement controls and published rendering. Theme source paths are relative to `PointOfSale.Storefront/src/storefront/themes/`.

| Theme | Outside-block element | Current source | Original default | Merchant configurable? | Admin control exists? | Correct surface | Publication | Localized | Responsive |
|---|---|---|---|---|---|---|---|---|---|
| Modern | Announcement opt-in + global Header | `modern/layout/header.tsx` delegates to `components/layout/default/header.tsx`; shared HeaderAnnouncement | No strip | No → Yes | No → Yes | Navigation > Header | Existing atomic Navigation scope | L/R | D/M |
| Minimal | Announcement opt-in + compact Header | `minimal/layout/header.tsx`; shared HeaderAnnouncement | No strip | No → Yes | No → Yes | Navigation > Header | Existing atomic Navigation scope | L/R | D/M |
| Fashion | Announcement opt-in + secondary centered nav | `fashion/layout/header.tsx`; shared HeaderAnnouncement | No strip; global Header links | No → Yes for announcement; links already Yes | Yes | Navigation > Header | Existing atomic Navigation scope | L/R | D/M |
| Pearl | Scrolling announcement + centered secondary nav | `pearl/layout/header.tsx`, `AnnouncementMarquee.tsx`, `pearlTokens.ts` | Enabled; translation copy; Pearl accent treatment | No → Yes | No → Yes | Navigation > Header | Existing atomic Navigation scope | L/R | D/M |
| Phoenix Sports | Scrolling announcement + floating white Header pill | `phoenix/layout/header.tsx`, `AnnouncementMarquee.tsx`, `phoenixTokens.ts` | Enabled; translation copy; theme accent treatment | No → Yes | No → Yes | Navigation > Header; pill shape is Theme presentation | Existing atomic Navigation scope | L/R | D/M |
| Hype Electronics | Scrolling announcement + secondary nav | `hype/layout/header.tsx`, `AnnouncementMarquee.tsx`, `hypeTokens.ts` | Enabled; translation copy; orange brand strip | No → Yes | No → Yes | Navigation > Header | Existing atomic Navigation scope | L/R | D/M |
| Alizon | Scrolling announcement + secondary nav | `alizon/layout/header.tsx`, `AnnouncementMarquee.tsx`, `alizonTokens.ts` | Enabled; translation copy; red brand strip | No → Yes | No → Yes | Navigation > Header | Existing atomic Navigation scope | L/R | D/M |
| Sharp Jewelry | Scrolling announcement + secondary nav | `sharp/layout/header.tsx`, `AnnouncementMarquee.tsx`, `sharpTokens.ts` | Enabled; translation copy; emerald strip | No → Yes | No → Yes | Navigation > Header | Existing atomic Navigation scope | L/R | D/M |
| Cosmetics | Static announcement strip with inert previous/next buttons | `cosmetics/layout/header.tsx` | Enabled; translation copy; pale `#E7DFDB` / rose `#D15F61` | No → Yes | No → Yes | Navigation > Header | Existing atomic Navigation scope | L/R | D/M |
| Seven Womenwear | Announcement opt-in + mauve global Header | `seven/layout/header.tsx`; shared HeaderAnnouncement | No strip | No → Yes | No → Yes | Navigation > Header | Existing atomic Navigation scope | L/R | D/M |
| Electronics (experimental) | Fallback/global layout and experimental hero code | `electronics/sections`; no own `layout/header.tsx` | Registry disabled, hidden, backend/runtime unavailable | Not an available merchant theme | No activation control | Experimental registry, not a supported theme | Not certified | Not certified | Not certified |

### Remaining visible elements and their owners

These rows apply to each supported theme; layout variations do not create additional business authorities.

| Themes | Element | Source / default | Configurable / Admin | Authoring owner | Published? | Localized? | Responsive? |
|---|---|---|---|---|---|---|---|
| All 10 | Brand text and home link | `HeaderRuntime.storeName`, `/` | Store name already authored in Store settings | Store identity / Header presentation | Existing StoreConfig updates | Store-owned name, no automatic translation | Theme Header responsive shell |
| All 10 | Primary/secondary nav and mobile drawer | `NavigationSlot`, `navigation.live.header.links`; second row reuses the same links | Yes / existing Navigation editor | Header Navigation | Atomic Header/Footer publication | `label` / `labelAr` | Mobile menu; no duplicated nav editor |
| All 10 | Search, authentication, language, wishlist, cart controls and overlays | Mandatory runtime capability slots | Operational capability UI, not promotional content | Platform runtime; existing authentication/search/cart authorities | Not page content publication | Runtime translations | Theme desktop/mobile slots |
| All 10 | Footer navigation | `FooterRuntime.navigation`, tenant live Footer links | Yes / Navigation > Footer | Footer Navigation | Atomic Header/Footer publication | `label` / `labelAr` | Theme footer wrapping/grid |
| All 10 | Footer content sections/social links | `FooterRuntime.sections/socialLinks`, `footerStore`; theme `layout/footer.tsx` | Yes / `/storefront/footer-content` | Existing Footer content authority | Existing Footer boundary | Existing Footer localization | Theme footer responsive grid |
| Pearl, Phoenix, Hype, Alizon, Sharp | Customer service/quick links/follow-us headings and contact label | `FooterRuntime.labels`, Storefront translation dictionary | System section labels; no bespoke merchant promo model | Platform localized Footer presentation | Not independent promotional data | Yes | Theme layout |
| All 10 | Copyright/year | `FooterRuntime.copyright`, current year + store name | Generated system label | Footer runtime | No separate versioned content | Yes | Theme wrapping |
| 9 themes (Cosmetics lacks this extra eyebrow) | “Welcome to” hero eyebrow, line/frames/gradients | Theme hero component + `t('home.welcome', {storeName:''})` | Structural theme label, not independently authored promotional copy | Homepage hero presentation | Follows hero block enabled state | Yes | Theme hero layout |
| All 10 | Hero CTA/image/title/subtitle, highlight strip and promotional badges inside sections | Canonical section settings / section runtime; product sale/stock/featured authority | Existing Homepage/Products controls; background upload added here | Homepage or Catalog, according to content | Homepage/Product boundaries | Authored strings remain authored; system labels translated | Theme section/card layouts |
| All 10, conditionally | Smart-offer popup/CTA overlay | `storefront/providers/OfferProvider.tsx`, `OfferPopup.tsx`, admitted SmartOffer data | Existing Smart Offers screen | Marketing / Smart Offers | Existing SmartOffer authority | Offer payload + runtime labels | Shared overlay; not a theme-only region |
| All 10, conditionally | Commerce AI floating widget | `components/chat/ChatWidget.tsx`, StoreConfig Commerce AI fields | Existing `/ecommerce/commerce-ai` controls | Commerce AI settings | Existing StoreConfig authority | Existing runtime localization | Shared widget, not a Homepage asset |
| All 10, conditionally | Affiliate referral banner | `components/layout/AffiliateBanner.tsx`, referral context | System disclosure, not merchant promotional content | Affiliate runtime | Not Homepage publication | **Existing English-only sentence; not remediated** | Shared flex banner; conditional journey not visually certified |

No additional independent theme collection ribbon, separate theme promo overlay, independent floating merchant CTA, or theme-specific hardcoded promotional campaign was found outside the paths above. Decorative hero frames, Header pills, scroll shadows and footer color treatment are theme implementation primitives, not extra hidden merchant content records.

Cosmetics' previous/next buttons had no rotating announcements or handlers. They were removed when connecting the static strip to the Header announcement contract; inert controls were not retained as fake authoring functionality.

## 4. Product authoring contract

| Surface | Authority |
|---|---|
| Homepage | Section content/order/enabled state, hero title/subtitle/CTA, homepage imagery, selected products/categories, section backgrounds and highlights |
| Navigation > Header | Header links and the global announcement text/Arabic text/visibility/link/appearance override |
| Navigation > Footer | Footer links; retains the same atomic publication as Header |
| Footer content | Existing Footer content sections/social links and their existing authority |
| Appearance > Themes | Explicit live theme activation and signed read-only theme candidates; activation does not publish Homepage or Navigation |
| Appearance > Colors & Typography | Existing theme draft/live override and publication lifecycle |
| Store settings / operational areas | Store identity, Commerce AI, Smart Offers, catalog/auth/payment/shipping according to existing owners |

There is one announcement editor. Appearance remains responsible for general visual tokens; the optional appearance override attached to a specific announcement is edited beside that announcement. No competing control exists in Homepage or Colors & Typography.

## 5. Implemented changes

### Backend / storage

- `HomepageMediaAsset`: immutable registration, tenant ID, object key, canonical content type, size, created timestamp.
- Tenant-filtered EF model, index/FK configuration and migration `20261007190000_MAIN_HomepageMediaAssets`.
- `HomepageMediaController`: authorized server-mediated upload, authorized short-lived preview capability and publication-gated storefront byte delivery.
- `HomepageMediaReferences`: canonical GUID reference and published Homepage reference checks.
- `AdminStoreConfigService`: validate registered media belongs to tenant at draft-save and publish admission; response exposes `LivePages` for published-state discard.
- `StorefrontPreviewService`: draft-only rendering DTO resolves canonical media references to five-minute download capabilities without persisting signed URLs.
- `HeaderAnnouncementConfig`, `HeaderAnnouncementValidator`, existing Navigation DTO/storage/admission adapters, draft save/publish paths and live Navigation DTO.
- Navigation projection retains the announcement; now additionally consumes `StoreConfigRolledBackEvent` and recomposes its derivative live Navigation state from SQL with the existing fence.
- Validation failures for announcement input produce validation responses, not generic internal failures.

### Admin / Storefront

- `HomepageImageField.jsx`: accessible file input, upload/replace/remove, selected/no-image/uploading/error states, legacy URL field, invalid URL/load state and thumbnail preview.
- Existing hero background schema now uses that control.
- `HeaderAnnouncementSettings.jsx`: localized text/visibility/link/color controls, draft content preview, reset-to-theme default.
- `NavigationEditor`: announcement included in the existing draft, autosave, atomic publish and published-snapshot discard.
- Existing Homepage save concurrency guard remains authoritative; discard admits its published candidate before persisting it, so it does not mistake the deliberate replacement for a newer concurrent edit.
- Existing simplified Admin hero preview now renders background imagery and overlay opacity. It resolves private media through authenticated preview access.
- Header runtime reads the live announcement; independent theme marquee renderers receive authored settings. Previously strip-less supported themes opt in through the same Header contract.
- English/Arabic translations added under `homepageMedia` and `headerAnnouncement`.
- Editor stacks its palette/canvas/settings below the wide-screen breakpoint, with responsive page margins and no squeezed desktop columns on mobile. Preview actions wrap; mobile preview width is 390.
- Pixel-driven fixes: Minimal hero honors authored text color; Pearl keeps white Header controls on a dark surface; Cosmetics controls use a legible normal-flow Header; Seven moves language/wishlist into its mobile menu so the brand is not clipped.
- Navigation state clears old tenant data when loading a different tenant identity.

### Two integration defects found and corrected during real browser checks

1. The announcement value object was initially in `Core.Entities`; the existing `PreventEntityExposureFilter` correctly blocked DTOs referencing it. It now lives in `Core.Models`, and a guard regression test covers the response.
2. The platform's request normalization filter treats `[Required]` empty strings as errors even with `AllowEmptyStrings`. Optional strings are now nullable at binding and normalized by the canonical announcement validator. A request-filter regression test covers empty link/colors/Arabic text. No global validation or entity-exposure guard was disabled.

## 6. R2 / media architecture

```text
Local file selected
 -> authenticated Owner/Admin multipart POST /api/admin/homepage-media
 -> existing IFileValidationService / Images profile (signature/type/size)
 -> existing IR2StorageService.UploadFileAsync
 -> tenants/{tenantGuid}/homepage/{assetGuid}/{storage-generated-fileName}
 -> immutable HomepageMediaAssets row
 -> /api/storefront/homepage-media/{assetGuid} reference
 -> Homepage draft save / authenticated preview
 -> existing Homepage publication
 -> Storefront receives canonical reference
 -> content request resolves verified tenant + current enabled live Homepage reference
 -> private object streamed through existing IR2StorageService.DownloadObjectAsync
```

- Production reuses `R2StorageService`. Development reuses the existing private `DevelopmentFileStorageService` registration behind the same interface.
- Maximum image size: 10 MB. Supported image profile: JPEG, PNG, WebP, GIF. Signature checks are server-side; browser MIME checks are advisory.
- This does not copy ProductMedia ranking, product/variant identity, promotion, gallery deletion or product publication semantics.
- The generic legacy `IMediaService` contract returns URLs and cannot represent registration/publication authority for this lifecycle. The existing platform R2 object-storage layer safely provides the reusable primitive.
- A separate tenant-scoped `homepage` namespace prevents mixing asset lifecycles with product media.
- Bucket public URLs returned by the generic storage primitive are **not returned to the client or persisted in page settings**. No public ACL, bucket permission or credential configuration was changed.
- A preview download lease is an intentional, expiring bearer capability granted after same-tenant Admin authorization, not a permanently public object.
- Removing/replacing a page reference does not delete an immutable registration/object. Existing versions need them for rollback.

Migration was applied successfully to the local development database. `dotnet ef migrations has-pending-model-changes ... --no-build` reported no pending model changes at the migration checkpoint.

## 7. Backward compatibility

| Behavior | Evidence/result |
|---|---|
| Existing valid image URL | Retained; actual Amazon URL rendered before and after certification |
| New upload | Canonical GUID media reference, no public/signed URL persisted |
| Invalid URL | Localized actionable error; unsafe URL is not rendered by the upload control |
| Invalid file / upload failure | Real invalid PNG rejected; previous draft reference unchanged; localized error |
| Remove | Detaches reference; object retained for history |
| Replace | New immutable registration/reference; previous live asset remains available until publish |
| Draft preview | Uses current persisted draft and authorized private capability |
| Publish | Existing Homepage draft becomes live atomically |
| Discard | Published Homepage/Navigation snapshot saved back to draft through existing save API |
| Rollback | Previous live media and announcement restored; existing authority intentionally preserves current drafts |
| Refresh | Saved canonical asset and Header settings survive reload |

No automatic migration of URLs. The server retains its existing safe-authored-URL validation rules. The media reference itself does not grant access to another tenant.

## 8. Draft / publish / discard / rollback evidence

All supported themes received a real local image through the Admin upload control, saved/reloaded it, previewed it, published Homepage, edited bilingual announcement text/link/style, saved/reloaded it, and published the existing Header/Footer navigation scope.

Each theme also:

1. Published announcement visibility off.
2. Verified the region disappeared, rather than being hidden with CSS.
3. Edited visibility on and discarded back to the published hidden state.
4. Published visibility on again.

Additional full asset lifecycle: `tests/browser/homepageMediaRollback.cli.js`.

Successful evidence:

| Checkpoint | Actual result |
|---|---|
| Published A | `/api/storefront/homepage-media/0eb70e5a-3e0a-49e5-918d-1671df9faab1` |
| Saved replacement discarded | SQL draft and local editor restored A |
| Published B | `/api/storefront/homepage-media/046b6d56-4e27-4e99-806b-7a14598f8479` |
| After B publish | A = **404**, B = **200** |
| Rollback target | StoreConfig version **221** (new publication created; version was not rewound) |
| After rollback | A = **200**, B = **404** |
| Header rollback | Original bilingual Seven announcement/link/colors restored |
| Draft preservation | Existing rollback behavior keeps current draft; a subsequent Discard restores live snapshot |
| Bad PNG | Upload error, no draft-reference corruption |

The first attempted discard integration exposed an interaction with existing “newer edits during save” protection. The candidate-admission fix has both an Admin component regression test and the successful browser lifecycle above. Test timing failures were corrected to wait for actual save responses; they were not counted as application passes.

## 9. Admin evidence

Scripts:

- `tests/browser/homepageThemeAuthoring.cli.js`: one theme per invocation; actual theme activation and authoring controls, no API-only substitute for the merchant journey.
- `tests/browser/homepageThemeResponsive.cli.js`: final responsive sweep after pixel fixes, including both Appearance screens.
- `tests/browser/homepageMediaRollback.cli.js`: invalid upload, replacement, saved-draft discard, publication and confirmed rollback through Version History.

File chooser upload was exercised with separate `playwright-cli click` and `upload` commands first. Repeated scripted uploads use the same accessible file input's `setInputFiles`, because the CLI's modal bookkeeping interrupts a single long `run-code` file-chooser sequence. This does not bypass the upload handler/API.

Screenshots include:

- `.artifacts/homepage-draft-reload-en-desktop.png`
- `.artifacts/homepage-preview-en-desktop.png`
- `.artifacts/header-announcement-saved-en-desktop.png`
- `.artifacts/homepage-invalid-upload-preserves-draft.png`
- `.artifacts/homepage-final-{theme}-{language}-{width}-homepage.png`
- `.artifacts/homepage-final-{theme}-{language}-{width}-navigation.png`
- `.artifacts/homepage-final-{theme}-{language}-{width}-appearance-themes.png`
- `.artifacts/homepage-final-{theme}-{language}-{width}-appearance-colors-typography.png`

New controls have real labels and button actions. The file input is visually hidden but labeled and reachable through keyboard-operable Upload/Replace buttons. Mobile pages scroll normally to all settings/actions; desktop settings use the existing scroll panel.

## 10. Storefront evidence

For all 10 supported themes:

- Selected theme activated through Admin; runtime config read from server.
- Uploaded image decoded (240×240 certification fixture) and verified in a real rendered `<img>` or CSS background.
- Announcement had actual nonzero pixel dimensions and authored background `rgb(18, 52, 87)` during certification.
- English and Arabic text, destination and visibility verified.
- Final screenshots inspected, not only DOM/control existence.
- Global strip handled at Header scope, including on themes that previously had none.

Evidence families:

- `.artifacts/homepage-cert-{theme}-{language}-{width}-storefront.png`
- `.artifacts/homepage-final-{theme}-{language}-{width}-storefront.png`
- `.artifacts/homepage-media-announcement-rollback-live.png`
- `.artifacts/homepage-restored-original-alizon.png`

Draft media content requests returned **404 before publication** in the cross-theme run. No page reload was added as an application workaround; browser reloads are deliberate persistence/certification checkpoints.

## 11. English / Arabic / LTR / RTL

Localized new controls/states: upload, replace, remove, selected, no image, upload in progress, invalid file, upload failure, invalid/load-failed URL, image preview, announcement title/description, visibility, English/Arabic copy, optional link, appearance defaults and reset-to-theme default.

Existing localized Save Draft, Discard, Publish Homepage, Preview, and atomic Header/Footer publication controls were reused.

The merchant's authored English hero content does not automatically become Arabic; it remains authored content. Arabic announcement copy is explicitly authored through `textAr`, with fallback to authored English when Arabic is empty.

No raw new translation keys were observed. Existing unrelated system text, such as the Affiliate disclosure sentence and some older Version History labels, is not a new localized feature and is not covered by a blanket localization PASS.

## 12. Desktop / mobile

- Desktop certification viewport: **1600×1000**.
- Mobile certification viewport: **390×844**.
- Final geometry compares `scrollWidth` against `documentElement.clientWidth`, not `innerWidth` (which includes the Windows scrollbar).
- A 350–400 ms resize settling interval prevents measuring the Admin shell midway through its existing layout transition.
- Final mobile Homepage image/control width is additionally checked, so a squeezed desktop column cannot incorrectly pass a document-overflow assertion.
- Pixel inspection caused remediation of Pearl, Cosmetics, Minimal and Seven presentation issues described in §5.

Final read-only responsive sweep: **200 cells** = 10 themes × 2 languages × 2 viewports × 5 surfaces. All passed measured document overflow and screenshot capture.

## 13. Automated regression / builds

Final focused backend command:

```powershell
dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" -c Release --no-restore --filter "FullyQualifiedName~HomepageMediaAuthoringTests|FullyQualifiedName~HeaderAnnouncementAuthoringTests|FullyQualifiedName~EntityExposureHardeningTests|FullyQualifiedName~AdminStoreConfigRollbackSafetyTests|FullyQualifiedName~AdminStoreConfigPublishReliabilityTests|FullyQualifiedName~NavigationAdmissionServiceTests|FullyQualifiedName~StorefrontNavigationRuntimeTests|FullyQualifiedName~ApiRequestNormalizationFilterTests|FullyQualifiedName~StorefrontConfigProjectionConsumerTests" -v:q -p:WarningLevel=0
```

Result: **64 passed, 0 failed**. Coverage includes own/foreign media, upload registration/private namespace, anonymous draft denial, draft reload/publish/discard/rollback, URL compatibility, historical unknown-section safety, announcement content/link/visibility/publication, unsafe link rejection, entity-exposure/request-normalization integration, SQL live Navigation authority and projection of live media rather than draft media.

Final focused Admin command:

```powershell
npm test -- --run src/modules/storefront/pages/StorefrontBuilderPage.runtimeLoop.test.tsx src/modules/storefront/cms/pages/NavigationEditor.test.tsx src/modules/storefront/components/builder/HomepageImageField.test.tsx src/modules/storefront/cms/components/HeaderAnnouncementSettings.test.tsx src/modules/storefront/cms/services src/modules/storefront/builder
```

Result: **62 passed, 0 failed** at the final discard-fix checkpoint. Includes upload/reference/failure/legacy URL, English/Arabic controls, Navigation DTO round-trip, saved-announcement discard and published-image discard/concurrency protection.

Final relative-URL compatibility hardening retained existing schema-supported relative image paths; the focused image-control suite was rerun with **5 passed** and the Admin production build passed again.

Storefront command:

```powershell
npm test -- --run src/storefront/themes src/storefront/presentation/HeaderAnnouncement.test.tsx src/storefront/runtime/cache/browserRuntimeCacheRecovery.test.ts src/components/theme-components
```

Result: **169 passed, 0 failed**. Includes independent theme marquee visibility/localization, rendered Header content/link/style, theme registry/layout ownership and tenant-navigation cache clearing.

Builds:

| Check | Result |
|---|---|
| API Debug build | Passed after terminating only identified local API binary for rebuild |
| Backend Release build/test | Passed |
| Admin `npm run build` | Passed |
| Storefront `npm run build` (`tsc && vite build`) | Passed |
| `git diff --check` | Passed; repository CRLF conversion warnings only |
| EF pending-model check | No pending changes at migration checkpoint |

Failure classification:

- Initial backend compile output copy failed because a running local API locked Core/Infrastructure DLLs; compilation itself had no source errors. Rebuilt successfully and used Release test output to avoid repeating the lock.
- One new backend test initially serialized a discard DTO with PascalCase while the builder boundary expects camelCase; corrected the test to send the real wire format.
- Pearl's pre-existing static source test required `bg-transparent`; it failed after the deliberate legibility fix and now asserts the new dark Header contract. It was not an unrelated test failure.
- Storefront production build caught an unused React import in the new test; removed and rebuilt successfully.
- Browser harness initially mishandled file-chooser modal continuation, compared uppercase theme text case-sensitively, attempted Publish after an identical draft, or raced saves/layout transitions. The successful reruns and final evidence supersede those failed attempts.
- No full repository test-suite PASS is claimed. Broad unrelated WhatsApp/Product/Sidebar work was already in the worktree.

## 14. Browser checkpoints

Machine-readable evidence: **`.artifacts/homepage-theme-browser-evidence.json`**.

| Set | Count | Result |
|---|---|---|
| Full device upload + announcement journey | 10 supported themes | Passed |
| Authoring locale/viewport/surface cells | 120 | Passed for delivered feature journey |
| Final responsive cells including Appearance | 200 | Passed |
| Visibility off / discard / on | Every supported theme | Passed |
| Invalid upload / saved-media discard / asset replacement / confirmed rollback | Additional full browser journey | Passed |
| Actual restored legacy URL / original theme/default chrome | Final live checkpoint | Passed |
| Complete selected-theme draft preview | Not provided by existing Admin renderer | **Not certified** |

`playwright-cli list` proved that `opencode-ui-test` was headed and registered before reuse. No new persistent Edge profile or unrelated browser process was used or terminated.

## 15. Security / tenant isolation / backend authority / cache

### Authorization and tenant evidence

- Upload and Admin image preview use `[Authorize(Roles = "Owner,Admin")]`, matching StoreConfig authoring authority.
- Backend tenant is `ITenantContext.RequiredTenantId`; neither a URL nor an arbitrary form field selects an asset tenant.
- Asset EF query filter + explicit same-tenant predicates on registration lookup.
- Draft-save/publish admit only media GUIDs registered in the current tenant.
- Tenant B context cannot enumerate Tenant A registrations, preview their GUIDs or fetch their content. Direct controller/EF regression tests return NotFound; foreign reference save is rejected without mutating the previous draft.
- Real non-Admin Storefront upload request returned **401**.
- Real conflicting untrusted `X-Tenant-Id` request returned **403**.
- Real newly uploaded draft images returned **404** anonymously before publish.
- Real replaced image returned **404** after publish; restored historical image returned **200** after rollback.
- No credentials, R2 keys or server object paths are added to browser responses. Preview leases are intentionally authorized short-lived capabilities.
- Existing generic request normalization and entity-exposure guards remain enabled, with focused regressions.

The browser security requests prove public/customer denial and tenant-header conflict handling. The two distinct authenticated Admin tenant actors were covered at service/controller/query level, not by creating a second merchant account in the real browser.

### Backend/API path summary

| Operation | Backend authority / endpoint |
|---|---|
| Upload/register | `POST /api/admin/homepage-media` / HomepageMediaController / existing R2 service / SQL media registration |
| Admin media preview | `GET /api/admin/homepage-media/{id}/preview` / same tenant Admin + 5-minute lease |
| Homepage save | `PUT /api/admin/store-config/pages/home` / AdminStoreConfigService / canonical section admission + same-tenant media |
| Homepage publish | `POST /api/admin/store-config/pages/home/publish` / existing lock + RowVersion + outbox/snapshot |
| Draft rendering DTO | Existing authenticated `/api/storefront/config/preview` / StorefrontPreviewService / current draft |
| Live rendering DTO | `/api/storefront/store-config` / current active tenant SQL / MapLive |
| Published image bytes | `/api/storefront/homepage-media/{id}` / current enabled live Homepage + tenant and storefront status |
| Header save/publish | Existing `/api/admin/cms/navigation` and `/publish` / same atomic Header/Footer snapshot |
| Live Header DTO | `/api/storefront/navigation` / current live SQL + existing target recomposition |
| Rollback | Existing `/api/admin/store-config/rollback/{version}` / confirmed version + existing live/draft boundary |

React does not create another publication authority. RowVersion and server locks/outbox remain in the existing services.

### Cache / projection findings

- Live StoreConfig and Navigation requests read authoritative SQL. Redis/Mongo projections are derivative; a stale projected asset does not authorize a content request.
- Media content and Admin media preview declare no-store; each published asset request rechecks current authority. Replacements use new immutable references.
- Browser StoreConfig/Navigation refresh owners and invalidation generations are reused. Navigation tests additionally assert that previous-tenant announcement content is cleared while the next tenant loads.
- Existing StoreConfig projection mapper reads `pages.home.live`, not draft, and preserves the canonical media reference. A focused consumer test now distinguishes live media from draft media.
- Observed Mongo `storefront_configs` remained stale from September 24. No manual projection overwrite/reload workaround was used to conceal this failure. Its mapper output was independently compared with the current SQL published reference and was correct.
- Additional broker audit found the old root-vhost `storefront-config-projection` queue with 241 queued messages and no consumer, alongside a consumed `storefront-config-projection-v2` queue. A separate QA-vhost error queue contains older Mongo TLS/connection-timeout faults. Those QA faults are not asserted to be the cause of the current local stale document. The diagnostic inspected/requeued an error message without purging or replaying shared queues; no broker configuration was changed.
- Navigation projection originally did not consume whole-StoreConfig rollback. This is fixed using the existing projection/fence. A real rollback with the rebuilt consumer changed the Mongo announcement from “CERT Rollback Changed Announcement” back to the original published Seven announcement.
- Already-open Storefront tabs follow the existing runtime refresh/cache policy, not a new push/reload mechanism. Browser persistence checkpoints explicitly reload. Immediate push updates to every open shopper tab are not claimed.

## 16. Remaining limitations and restored store state

1. **End-to-end StoreConfig MongoDB projection delivery is unresolved** in the running environment; source mapper/tests and live SQL-backed rendering pass, but the derivative persisted document is stale.
2. **Admin Homepage draft preview is not the complete theme renderer.** It now correctly renders uploaded draft imagery, but full selected-theme draft shell fidelity remains an architectural gap.
3. **Production Cloudflare R2 was not exercised against live credentials.** Browser storage used the existing DevelopmentFileStorageService behind IR2StorageService. Production service reuse, private mediated delivery and authorization were verified in code/tests; production bucket policy/CORS were not changed or independently certified.
4. Immutable historical media are retained deliberately. No asset garbage collector/media-library deletion UI was introduced. Unreferenced uploads can accumulate; a future cleanup must check current drafts, live state and retained versions before deletion.
5. Upload progress is a localized in-progress state, not a measured byte-percentage indicator.
6. The complete replacement/discard/rollback browser sequence was performed on the shared authority with the Seven theme. Every theme's normal upload/draft/reload/publish and Header visibility/discard/publish path passed; a separate full rollback for each of 10 themes was not executed.
7. Conditional shared SmartOffer/Commerce AI/Affiliate paths were source-audited for ownership. Their full conditional campaigns/customer journeys and all old translations were not independently recertified in this task. AffiliateBanner has existing English-only disclosure text.
8. Theme decorative primitives and system labels remain theme/platform presentation; there is no universal low-level CSS editor. Merchant announcements have the specific appearance controls requested; unrelated theme-token editing was not expanded into duplicate configuration screens.

### Final AhramStore restoration

After browser certification, the merchant-visible original configuration was restored through real Admin controls:

- Theme: **Alizon**.
- Background: original Amazon image URL, decoded successfully in live Storefront.
- Announcement: **null authored override**, returning to Alizon's existing translation/theme default.
- Header/Footer links were preserved.
- Current draft reflects the restored published Homepage/Header state.

Certification-created immutable uploads and StoreConfig publication history remain as legitimate historical evidence. They were not silently deleted. Their content is not publicly served once no longer referenced by current enabled published Homepage state.

### Required next certification work

- Diagnose the existing StoreConfig projection event-delivery/freshness issue and demonstrate SQL → outbox → consumer → current Mongo document convergence without manual overwrites.
- Reconcile the Admin preview renderer with the full selected-theme runtime through an authenticated preview authority, then repeat theme/locale/viewport draft-preview pixel certification.
- If release acceptance requires production-R2 evidence or a separate full rollback per theme, run those explicit remaining checkpoints before upgrading this report to PASS.
