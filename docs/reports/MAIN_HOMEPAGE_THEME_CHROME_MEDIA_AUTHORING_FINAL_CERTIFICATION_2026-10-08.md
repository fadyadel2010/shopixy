# Main Homepage — Final Two-Gap Remediation Certification

Date: **2026-10-08** (local development date; browser/log artifacts use UTC, including October 7 timestamps).  
Continuation: `MAIN_HOMEPAGE_THEME_CHROME_MEDIA_AUTHORING_FINAL_CERTIFICATION_2026-10-07.md`.  
Admin: `http://localhost:5173/`; API: `http://localhost:5010/`; Storefront: `http://ahramstore.localhost:5174/`.  
Browser: **headed Microsoft Edge**, registered `playwright-cli` session **`opencode-ui-test`**.

## Verdict

**PASS for the two acceptance gaps covered by this task**, with the bounded environmental/deployment conditions in §P.

1. **StoreConfig Mongo convergence: PASS.** Real Homepage publications A/B, atomic Header/Footer publication C, and a confirmed whole-StoreConfig rollback passed SQL → transactional event delivery → registered consumer → persisted Mongo convergence. No merchant Mongo document was manually updated, replaced, or deleted.
2. **Theme-faithful Admin Homepage draft preview: PASS.** The actual Storefront theme/runtime renders the authenticated draft in an isolated viewport. All **10 supported themes** passed English/Arabic, LTR/RTL, desktop/mobile, Header/announcement/hero/Footer, uploaded draft image, reload, discard, controlled publication, and headed visual comparison. An additional all-theme read-only section/card sweep and mobile-menu/candidate-theme sweep passed.

This verdict does **not** extend the previous report's production-R2 certification or certify the entire repository. No R2 configuration, bucket/CORS/policy, payment/shipping, marketing, or unrelated application feature was changed. No commit was made. Existing worktree changes were preserved, including concurrent unrelated work.

## A. Mongo projection root cause

The original September 24 Mongo document had:

```text
database: ecommerce_read_db
collection: storefront_configs
_id: 9cd01634-f469-4b76-ad20-13a74b88cf13
TenantId: c9c5c1ae-949e-498e-bc63-07242daeb49d
TenantSlug: ahramstore
UpdatedAt: 2026-09-24T16:27:22.149Z
LastEventId: 1db9e131-d371-4acf-bfc5-b1bd19e7cc57
LastEventOccurredAtUtc: 2026-09-24T16:25:57.690Z
LastEventVersion: 2
StorefrontTheme: modern
Homepage image: empty
```

SQL already contained the correct Alizon/live Homepage state and publication revision 226. SQL `ProcessedEvents` proved that the v2 consumer had processed October 7 StoreConfig events, including publication and rollback, while the Mongo document remained unchanged.

The actual failure was **version-domain confusion**:

- `IntegrationEvent.EventVersion` defaults to **1** and is documented as an event/schema version.
- `StoreConfigPublishedEvent.ConfigVersion` is the publication revision. Its derived `Version` compatibility alias does not change the base `EventVersion` backing field.
- The Mongo replacement filter compared `LastEventVersion` against the base `EventVersion`.
- A historical document watermark of **2** permanently rejected current schema-v1 publications, regardless of their newer timestamp and correct SQL content.
- The guarded zero-match/duplicate-key path treated this rejection as a successfully handled stale event and completed its SQL EventId fence. There were therefore successful consumer acknowledgements without configuration convergence.

The canonical mapper's live/draft selection was not the cause. SQL-backed Storefront delivery remained correct.

Two additional delivery omissions were identified: the StoreConfig projection did not consume `NavigationPublishedEvent` or `StorefrontThemeActivatedEvent`, although these existing authorities change published StoreConfig state.

## B. Exact event path and topology/queue cause

| Boundary | Proven implementation/runtime |
|---|---|
| Business authority | PostgreSQL `StoreConfigs`, current `TenantId`/config `Id`; `CustomizationJson.pages.home.live`, `navigation.live`, theme live fields, `LiveVersion`, retained version snapshots |
| Homepage draft/save/publish | `AdminStoreConfigController` → `AdminStoreConfigService`; canonical admission, RowVersion/If-Match, publish lock, `AppendVersionSnapshot` |
| Transaction/outbox | `PersistStoreConfigChangeAsync` stages events before `SaveChangesAsync`; existing scoped `MassTransitEventPublisher`/EF bus outbox, PostgreSQL commit and existing delivery service |
| Events | `StoreConfigCreatedEvent`, `StoreConfigUpdatedEvent`, `StoreConfigPublishedEvent`, `StoreConfigRolledBackEvent`; additionally existing Navigation publication and theme activation events |
| Payload | EventId, tenant/config identifiers, timestamps, correlation/idempotency identifiers, store/theme metadata and publication revision; no Homepage byte payload, draft JSON, signed image URL, storage secret, or payment secret |
| Broker | Configured RabbitMQ host; MassTransit fanout message exchanges such as `PointOfSale.Core.Events:StoreConfigPublishedEvent` and `...:NavigationPublishedEvent` |
| Current vhost | **`/`**. The separately running **`/shopixy-qa`** consumer is a different environment, not this local path |
| Current receiver | `storefront-config-projection-v2`, registered `StorefrontConfigProjectionConsumer` |
| Compatibility receiver | `storefront-config-projection`, the **same** consumer, now explicitly registered |
| Mongo | `IMongoDatabase` configured as **`ecommerce_read_db`**, collection **`storefront_configs`**, `_id = StoreConfig.Id.ToString()`, tenant stored as GUID string |
| Tenant lineage | `TenantLineageAuthority.LoadCurrentStoreConfigProjectionAuthorityAsync`; explicit tenant/config relationship and active SQL tenant checks |
| Duplicate fence | SQL `ProjectionEventFence`/`ProcessedEvents`; PostgreSQL advisory lock and completed EventId evidence |
| Freshness | Mongo **`LiveVersion`**, recomposed from the SQL revision actually read, then timestamp within that revision; compatibility `LastEvent*` diagnostics retained |
| Retry/observation | Existing exponential consumer retry: 5 attempts, 1–30 second interval bounds; exceptions are logged and rethrown, failure/lag telemetry retained, exhausted deliveries use endpoint `_error` queues and MassTransit fault handling |

The definition had explicitly renamed the endpoint to **`storefront-config-projection-v2`**. RabbitMQ's durable old queue and bindings survive such a rename; they are not migrated automatically.

The forensic broker audit proved:

- Root-vhost legacy queue: **241 ready messages**, **0 consumers**.
- Root-vhost v2 queue: **0 queued messages**, **1 consumer**, actual acknowledged deliveries.
- Both old and v2 endpoint exchanges remained bound to the StoreConfig message exchanges. New publications were consequently copied to the abandoned queue as well as delivered to v2.
- QA-vhost v2 error queue: **5 existing messages**; its historical connectivity faults were not the root-vhost version-fence cause and were left alone.

The old queue was therefore **not harmless**: it retained business events and continued accumulating deliveries.

## C. Exact convergence fix and safe migration

Changes are confined to the existing projection/event architecture:

1. Add `StorefrontConfigDocument.LiveVersion` as a distinct SQL publication watermark. Atomic replacement no longer orders StoreConfig business state by a transport/schema number.
2. Recompose the **current published SQL state** and fence by the revision actually read. Legacy documents without this new watermark are admitted through the normal consumer on delivery, without a manual migration overwrite.
3. Preserve timestamp ordering within the same revision and guard EventId duplicates. An older event cannot overwrite a document already representing a newer SQL revision. An old notification can legitimately rebuild the newest SQL state, because it is an invalidation, not a historical content snapshot.
4. Published, rollback and generic StoreConfig notifications now carry the current publication revision in the existing EventVersion compatibility field as well as the publication-specific field where present. The **new Mongo fence does not depend on that compatibility field**.
5. A completed/overlapping EventId can repair a missing or older derivative document. It still must pass SQL lineage and atomic Mongo freshness/key guards. A duplicate against an already current projection remains a no-op.
6. Consume the existing Navigation publication and theme activation events using the same StoreConfig recomposition method. Header/Footer still owns its atomic publication scope.
7. Reject missing tenant identity and misrouted aggregate/tenant combinations through retry/error handling. A mismatched event must not delete another valid tenant aggregate's projection. Existing inactive/deleted-tenant fail-closed behavior remains.
8. Explicitly configure **both** root-vhost queues with the same consumer. Runtime inspection caught that manually configuring the legacy endpoint suppresses MassTransit's automatic endpoint generation for that consumer; v2 is therefore explicitly configured too.

The preserved legacy messages were inspected using RabbitMQ's **ack/requeue** diagnostic mode. They are compatible MassTransit envelopes containing Created/Updated/Published/RolledBack events with tenant/config identities, not a second historical configuration authority. Retained SQL state/version snapshots remain the content authority. They can safely run through the current SQL-recomposing consumer and its fences.

The legacy queue drained through the real application receiver. No queue purge, broker-side message deletion, ad-hoc republish, document replacement, or deletion of historical SQL state was used. Already-completed duplicates were acknowledged by the normal idempotency path; stale/missing projection repair and subsequent current publications use the same consumer.

Final root-vhost topology: **both queues have 1 consumer, 0 messages**; v2 error queue **0 messages**. The compatibility receiver is intentionally retained while durable legacy bindings/older deployments can still route there. This is not a second projection mechanism.

## D–F. Real event/consumer, Mongo, and rollback evidence

The browser journey used **controlled disposable content in AhramStore**, with the original live/draft content captured before edits and restored afterward. Real-Mongo automated consumer tests use an entirely disposable database; they do not modify the merchant projection.

`tests/browser/storeConfigConvergence.cli.js` performed actual file-input upload, Save Draft, Publish Homepage, atomic Header/Footer Publish, and confirmed Version History rollback. Each browser checkpoint was interleaved with the read-only `StoreConfigTrace --wait --checkpoint ...` diagnostic. The diagnostic polls actual persisted Mongo and fails after 45 seconds if SQL revision/media do not converge.

| Checkpoint | SQL/Mongo revision | Published media / Header | Mongo applied EventId |
|---|---:|---|---|
| A | **229 / 229** | `e9c9d01a-7a4c-412a-9642-fb92f8304b10`; title `CERT Convergence A` | `80dfa210-94ae-4b22-9388-ada47e20a6c0` |
| B | **230 / 230** | changed immutable media `525cd770-09a5-468b-9efe-c9adc5d269d4`; title `CERT Convergence B` | `661d7e9c-7084-4015-ba30-6ba564479483` |
| C | **231 / 231** | B media remains; Header `CERT Convergence C Header` / `إعلان اعتماد التقارب` | `08070372-3942-4a31-a150-6f553c5f2bd4` |
| Whole-config rollback | **232 / 232** | restored A Homepage and prior Header snapshot, restored from v229 | `1a0a4ce2-dba3-4534-a628-94a5cd3f545a` |
| Final restored merchant state | **345 / 345** | original image/title, Alizon, null authored announcement | `0027459a-80e9-434c-b7e2-a5e2c3f1e336` |

Mongo timestamps for B/C/rollback were `21:35:39.646Z`, `21:36:11.304Z`, `21:36:38.234Z` on October 7 UTC. The final restoration projected at `23:01:32.383Z`.

Evidence files:

- `.artifacts/storeconfig-trace-{A,B,C,Rollback,Restored}.json`: actual SQL live/draft snapshots, Mongo document and SQL processing-fence evidence.
- `.artifacts/gap-convergence-{A,B,C,Rollback}-storefront.png`: headed public Storefront rendering.
- `.artifacts/homepage-final-two-gaps-browser-evidence.json`: publication/media versions, private-before-publish results, preview/theme/locale/viewport/security results.

Rollback creates a **new publication revision**; it never rewinds the fence to the restored historical version. Browser reload verified persisted Storefront state at A/B/C/rollback. C's Header content is stored in the existing separate **`storefront_navigation`** document; `storefront_configs` now converges to the same current StoreConfig revision. These projections remain distinct derivatives of the same SQL publication boundary.

**Authority clarification:** the real Storefront's existing current SQL read/authority overlay remains intact. Certification proves both that the derivative Mongo document converges and that the shopper runtime reflects that same published authority. It does not falsely claim Mongo became the business authority or that the Storefront was changed to depend exclusively on Mongo.

## G. Admin preview root cause / forensic trace

Original path:

```text
StorefrontBuilderPage.handlePreview
 -> await existing draft save if dirty
 -> authenticated GET /api/storefront/config/preview
 -> tenant-admitted draft StorefrontStoreConfigDto
 -> PreviewModal / Admin PreviewThemeProvider
 -> PointOfSale.Web SectionRenderer
```

That Admin renderer was a simplified section implementation. It hydrated an Admin theme context and image preview, but did not use the Storefront's theme-specific layout/section resolution or global Header/Footer runtimes.

Live runtime:

```text
StoreConfig -> ThemeContext/ThemeProvider -> ThemeResolver -> ThemeRegistry
 -> HomePage -> Layout -> StorefrontLayout
 -> ThemeLayoutResolver -> HeaderRuntime / actual selected themed Header
 -> SectionRenderer -> ThemeSectionResolver -> RuntimeSectionContainer / themed sections
 -> FooterRuntime / actual selected themed Footer
```

`HeaderRuntime` owns store-name presentation, capability slots, navigation/localization and announcement input; each selected theme owns presentation. `FooterRuntime` owns resolved Footer/navigation/localized models. Homepage media references are content inputs to the same section runtime. CSS/media queries belong to the Storefront document, not the Admin's document width.

## H–I. Shared preview contract and runtime reuse

The Homepage branch of `PreviewModal` now uses `ThemeRuntimePreview`, an isolated iframe served by the **Storefront build** at `/draft-preview.html`. Product-template preview retains its existing separate path; it was not expanded by this task.

```text
Authenticated Admin save/preview authority
 -> StorefrontPreviewService / draft StorefrontDraftConfigMapper
 -> PreviewChrome: draft Header/Footer navigation + localized Footer resolver data
 -> backend StorefrontUrlResolver-issued runtime URL
 -> exact-origin/source/session/tenant postMessage handshake
 -> isolated Storefront StoreConfig (configSource = preview)
 -> actual ThemeProvider / ThemeResolver / ThemeRegistry
 -> actual HomePage / Layout / StorefrontLayout
 -> actual HeaderRuntime / section runtime / FooterRuntime
 -> selected-theme Admin preview viewport
```

No theme-specific markup was copied into Admin. The new Storefront entry imports the **existing `HomePage`**, preserving its real theme shell, section/card runtime and guest presentation. A `MemoryRouter` keeps preview navigation within the read-only view. Existing browser-only shopper authentication can initialize independently after tenant admission; Admin credentials are not forwarded to the iframe. Shopper navigation/commerce mutations are suppressed in the read-only preview while mobile menu/language inspection remains available.

Preview DTO details:

- Current persisted draft Homepage content, registered media, order and enabled state come from the same mapper/contract.
- Explicitly empty Navigation drafts remain empty; only absence of a draft uses its existing live branch.
- Draft Header **and Footer links** are resolved from the same atomic Navigation draft, including the announcement.
- Footer content/social sections use `StorefrontFooterReadService`, in English and Arabic. Their existing independent immediate-save authority is reused; no invented Footer draft or competing publish endpoint was added.
- `PreviewChrome` is absent from public `MapLive` responses.
- Private Homepage assets retain the existing authorized **five-minute download capability**. Signed URLs are resolved for display only, not persisted as authoring/publication state.
- Preview navigation/footer stores use the admitted preview model even on forced refresh or language change, preventing a later live fetch from replacing the draft Header/Footer.
- The iframe never fetches private draft configuration itself and stores no draft document in local/session storage. A direct standalone URL renders only a waiting message.
- Device selection preserves a real **390×844** mobile document. On a narrower Admin frame, only its outer presentation scales; responsive breakpoints are still evaluated against 390 pixels.

The preview URL uses backend tenant URL authority. Production config uses `VITE_ADMIN_ORIGIN`; no implicit referrer or wildcard parent is trusted. The renderer verifies **parent source, exact origin, per-open nonce, DTO tenant identity, required chrome and language**. Reload re-handshakes the same admitted preview snapshot. Closing/reopening requests a fresh authenticated DTO/capability.

For a draft-theme candidate distinct from live selection, the existing **signed theme capability** is validated in addition to Owner/Admin authorization. The Admin preview API parameter is **`draftThemePreviewToken`**, deliberately separate from the public token middleware parameter. Public theme capabilities do not authorize Admin drafts. The builder's optional candidate token never activates/publishes a theme or content.

The frame's CSP permits its configured Admin ancestor on this dedicated document; public Storefront documents retain `frame-ancestors 'none'`. Admin framing supports the backend-issued HTTPS Storefront origin and local development Storefront subdomains. Authorization requirements were not weakened.

## J–M. Ten-theme headed visual certification

Authority: the same 10 official available themes established in the prior report. Experimental Electronics is not an available supported theme.

For **each** supported theme, the real merchant journey performed:

1. Explicit Theme activation through Admin, then open Homepage.
2. Device upload of an immutable private Homepage image; change title; Save Draft.
3. Author English/Arabic Header announcement in Navigation; save its existing draft.
4. Reload Homepage; open complete themed draft; reload the embedded runtime.
5. Prove anonymous image **404**, draft preview **401**, unchanged public title/announcement and absent public PreviewChrome.
6. Capture English/Arabic desktop and 390×844 mobile Header/hero and Footer screenshots.
7. Discard saved Homepage and Header drafts; reopen preview and verify live snapshot restoration.
8. Re-admit the same controlled draft, publish Homepage and Header/Footer through their original boundaries solely for equivalent public-render comparison.
9. Capture the real public Storefront with a matching guest identity/viewport; compare screenshot pixels.
10. Restore original merchant image/title/default announcement and Alizon through application controls.

**Results:** 40 final visual cells, **160 Header/hero/Footer screenshots**, plus 40 read-only equivalent-theme section/card cells (**80 screenshots**). All were viewed through paired contact sheets, supplemented by full-sized screenshot inspection. This is not a DOM-only parity claim.

| Theme | English/LTR + Arabic/RTL | Desktop + mobile | Shell, media, reload/discard/publish | Max top / Footer raster difference after bounded framing alignment |
|---|---|---|---|---:|
| Modern | PASS | PASS | PASS | 2.06% / 2.33% |
| Minimal | PASS | PASS | PASS | 1.96% / 2.47% |
| Fashion | PASS | PASS | PASS | 2.05% / 2.55% |
| Pearl | PASS | PASS | PASS | 2.04% / 2.09% |
| Phoenix Sports | PASS | PASS | PASS | 2.29% / 2.13% |
| Hype Electronics | PASS | PASS | PASS | 2.60% / 2.16% |
| Alizon | PASS | PASS | PASS | 2.04% / 2.05% |
| Sharp Jewelry | PASS | PASS | PASS | 2.25% / 2.03% |
| Cosmetics | PASS | PASS | PASS | 2.57% / 1.55% |
| Seven Womenwear | PASS | PASS | PASS | 2.72% / 1.76% |

All 10 additionally passed the **20-cell actual 390×844 Admin/mobile-menu journey** with signed candidate themes while live remained Seven at that checkpoint. Final live selection was subsequently restored to Alizon. These checks demonstrate live Theme A / preview candidate Theme B without activating B.

Visual reuse includes actual Header height/layout, store-name presentation, navigation/mobile menu, announcement strip, hero imagery/title/CTA, section order, real theme-specific product cards/category layout, spacing, typography and Footer structure. Authored English Homepage text remains authored English in Arabic mode, as in live Storefront; system labels and explicitly authored Arabic announcements localize through the same runtime.

### Exact bounded visual differences

- Admin framing/debug toolbar/rounded outer corners are outside the theme document. Its element screenshot and the standalone browser screenshot differ by up to **2 raster pixels** in framing due to Windows zoom/subpixel rasterization.
- The iframe's RTL native scrollbar is on the left while the standalone Edge scrollbar is on the right. The measured content-origin difference was about **15.2 CSS pixels**. Raster alignment uses that measured gutter offset, not an arbitrary content displacement. Theme content width matches on mobile; desktop width differed by approximately **0.8 CSS pixels**.
- RGB comparison excludes a **20-pixel outer framing band**, permits only ±1-pixel adjustment around that measured content origin, and counts pixels exceeding **24 RGB channel levels**. Maximum final top/Footer changed-pixel ratio is **2.72%**. Anti-aliasing and independent marquee positions account for bounded remaining differences; this is not a zero-pixel identity claim.
- Computed Header/Footer font family, font size and height match within the actual equivalent configuration. The typography mismatch found during initial review was fixed by sharing the live entry's required Inter **800/900** weights explicitly with the preview, rather than importing unrelated page CSS.
- Lazy catalog imagery is allowed to load through its actual URL before card capture. The read-only section sweep aligns the same section below the sticky Header; generic scrollIntoView initially captured different portions of long carousels and was corrected in the harness.

Screenshot families:

```text
.artifacts/gap-parity-{theme}-{en|ar}-{desktop|mobile}-{preview|live}-{top|footer}.png
.artifacts/gap-middle-{theme}-{en|ar}-{desktop|mobile}-{preview|storefront}.png
.artifacts/gap-signed-candidate-{theme}-{en|ar}-admin-mobile-menu.png
.artifacts/gap-contact-{en|ar}-{desktop|mobile}-{top|footer|middle}-{0|1}.png
```

The **24 paired contact sheets** were visually reviewed. Machine evidence includes both rendered section-order snapshots and pixel measurements, but neither substitutes for screenshot review.

## N. Security and authority evidence

| Guarantee | Evidence |
|---|---|
| Owner/Admin-only private preview | Existing role attributes retained; real anonymous preview **401** |
| Public theme capability cannot read Admin draft | Real anonymous request with a valid signed candidate and `draftThemePreviewToken`: **401** |
| Invalid candidate capability rejected | Real authenticated invalid-token request: **401**; controller regression |
| Draft media not public | Real new unpublished image **404** for every theme; uploaded preview image decoded/rendered only through authorized capability |
| No public draft DTO leakage | Public StoreConfig lacks PreviewChrome; public title/image/Header remain the previous live snapshot before publication |
| Conflicting tenant identity rejected | Real conflicting `X-Tenant-Id` preview request: **403** |
| Cross-tenant asset preview rejected | `HomepageMediaAuthoringTests.TenantB_CannotResolveTenantAAsset_AndUploadRequiresAdminRole`, foreign-reference admission tests; no new auth bypass |
| Message isolation | Contract/component tests reject foreign origin/frame/tenant/nonce; no wildcard `postMessage` target |
| Standalone draft renderer | Real anonymous standalone visit: **0 Headers**, waiting message, no draft fetch |
| Mongo derivative only | SQL-backed public authority/read overlay and publication-gated media checks unchanged |
| Event secret separation | Publication regression verifies identifier/revision notification, no CustomizationJson/preview payload; existing publisher path contains no image lease/storage key secret |
| Browser secret separation | No POS token, R2 secret signing key, storage account secret, or server configuration is sent to the iframe. Existing signed download capability is reused |

**Capability detail:** when the configured storage provider uses SigV4 URLs, its short-lived URL includes the provider-required key identifier/`X-Amz-Credential` descriptor and signature. That is the pre-existing scoped download capability, not the secret signing key or unrestricted storage credentials. This report does not claim that the literal word “Credential” is absent from that URL, nor that an intentionally authorized bearer lease cannot be used by its holder before expiry.

Two distinct authenticated merchant actors were covered by service/controller/query-filter regression, not by creating another browser merchant account. Browser conflict/anonymous checks and backend tenant-isolation checks are identified separately.

## O. Automated regression, builds, browser evidence

Final backend focused run (**real Mongo integration enabled**):

```powershell
$env:STORECONFIG_MONGO_INTEGRATION='1'
dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" -c Release --no-restore --filter "FullyQualifiedName~StoreConfigConvergenceTests|FullyQualifiedName~StorefrontConfigProjectionConsumerTests|FullyQualifiedName~StorefrontThemeDraftPreviewTests|FullyQualifiedName~StorefrontPreviewServiceTests|FullyQualifiedName~AdminStoreConfigControllerHardeningTests|FullyQualifiedName~HomepageMediaAuthoringTests|FullyQualifiedName~HeaderAnnouncementAuthoringTests|FullyQualifiedName~AdminStoreConfigRollbackSafetyTests|FullyQualifiedName~AdminStoreConfigPublishReliabilityTests|FullyQualifiedName~NavigationAdmissionServiceTests|FullyQualifiedName~StorefrontNavigationRuntimeTests|FullyQualifiedName~ApiRequestNormalizationFilterTests|FullyQualifiedName~EntityExposureHardeningTests" -v:q -p:WarningLevel=0
```

**82 passed, 0 failed, 0 skipped.** The two opt-in real-Mongo tests use a disposable `storeconfig_cert_*` database and the application consumer. They cover schema-v2/current-v1 history, actual Mongo atomic version filters, duplicate replay, old-after-new delivery, live-only content, rollback/new revision, missing derivative rebuild, completed-fence older-derivative repair, fresh consumer/context restart, thrown Mongo timeout then same-EventId retry, foreign tenant and mismatched aggregate rejection. Their SQL authority fixture is EF InMemory; the separate headed journey proves the real PostgreSQL outbox/RabbitMQ path. Simulating missing derivative loss is confined to this disposable test database.

Existing preview/publication architecture assertions were updated for the actual Homepage runtime and the new SQL publication watermark. The separate focused `StoreBuilderPreviewAuthorityCertificationTests` run passed **3/3**; the unchanged archived certification artifacts are not a replacement for this task's runtime evidence.

Focused Admin:

```powershell
npm test -- --run src/modules/storefront/components/builder/ThemeRuntimePreview.test.tsx src/modules/storefront/pages/StorefrontBuilderPage.runtimeLoop.test.tsx src/modules/storefront/cms/pages/NavigationEditor.test.tsx src/modules/storefront/components/builder/HomepageImageField.test.tsx src/modules/storefront/cms/components/HeaderAnnouncementSettings.test.tsx src/modules/storefront/cms/services src/modules/storefront/builder
```

**68 passed, 0 failed**. Includes exact-origin/frame/session handshake, foreign/unsafe authority denial, locale/reload re-admission, fixed mobile document/scaled Admin frame, existing save/discard/publication/media/Header boundaries.

Focused Storefront:

```powershell
npm test -- --run src/storefront/preview src/storefront/runtime/cache/browserRuntimeCacheRecovery.test.ts src/storefront/themes src/storefront/presentation/HeaderAnnouncement.test.tsx
```

**172 passed, 0 failed**. Includes draft candidate theme resolution, foreign origin/tenant/session denial, preview chrome overriding forced live-cache refresh, Arabic Footer resolution, registry/layout/theme/Header regressions.

Builds/checks:

| Check | Result |
|---|---|
| Backend/API Debug build checkpoint | PASS |
| Backend/API Release compilation via focused tests | PASS |
| Admin production `npm run build` | PASS |
| Storefront production `npm run build` (`tsc && vite build`, both HTML entries) | PASS |
| `git diff --check` | PASS; repository CRLF conversion warnings only |
| Real Mongo A/B/C/whole-config rollback/final restoration | PASS |
| Headed ten-theme upload/draft/discard/publish/pixel sweep | PASS |
| Headed 20-cell signed-theme/mobile-menu boundary sweep | PASS |
| Headed 40-cell section/card sweep | PASS |

Existing bundle-size/circular re-export/Browserslist and Vitest transform deprecation warnings were observed; unrelated application features were not refactored to remove them. A full repository test-suite PASS is not claimed.

### Reproducible artifacts/tooling

- `tests/diagnostics/StoreConfigTrace`: read-only SQL/Mongo/broker topology trace; optional legacy ack/requeue peek and bounded convergence checkpoints.
- `tests/browser/storeConfigConvergence.cli.js`: one actual publication/rollback stage per invocation.
- `tests/browser/homepageThemeDraftParity.cli.js`: one actual theme draft/discard/controlled-publish comparison.
- `tests/browser/homepagePreviewBoundaryResponsive.cli.js`: signed candidate/live isolation and actual mobile menus.
- `tests/browser/homepageSharedRuntimeMiddle.cli.js`: one read-only signed-theme card/section sweep per invocation.
- `tests/browser/homepageGapSecurity.cli.js`, `homepageGapRestore.cli.js`: security and restoration.
- `tests/browser/exportHomepageGapEvidence.cjs`, `homepageGapContactSheets.ps1`: export already-produced evidence and assemble original headed screenshots without modifying them.

The sequential runners invoke **playwright-cli**, not separate agents. Initial harness races were not counted as passes: browser leave-page modal bookkeeping, waiting for asynchronous save/refresh, unequal shopper authentication between comparison contexts, deferred off-screen catalog images, unaligned scroll positions, and stale default theme reads during reload were corrected. Tests await real saved/published state. The final repeated theme sweep and aligned section sweep supersede those incomplete attempts.

The existing headed browser session closed during an additional long read-only screenshot sweep. Completed primary evidence had already been exported. `playwright-cli list` then proved it absent; a new isolated **headed session with the same name** was opened, authenticated, and the bounded one-theme-at-a-time sweep completed. No unrelated browser processes were terminated. CLI help/list confirmed that an already registered headed session should be reused rather than repeatedly opened.

## P. Remaining bounded conditions and restored state

1. **Deployment configuration:** production preview requires `VITE_ADMIN_ORIGIN` set to the exact Admin origin and the static host to serve the dedicated preview document with the corresponding `frame-ancestors` HTTP header. The Vite development/preview servers implement that per-document header. Missing origin fails closed. No production reverse-proxy deployment certification is implied by the local headed check/build.
2. **Short-lived image access:** preview media remains a five-minute capability. A frame reload within that snapshot re-handshakes it; closing/reopening preview obtains fresh authenticated data/capabilities. No indefinite public draft-media URL or lease-extension authority was introduced.
3. **Footer ownership:** Footer content has its existing separate save authority; Footer links/Header announcement retain the atomic Navigation publication boundary. This task did not invent a Footer draft feature.
4. **Visual parity:** bounded framing, native RTL scrollbar, anti-aliasing and marquee-phase differences are documented in §J–M. Neither pixel-perfect identity nor equivalence between a logged-in shopper and a guest preview is claimed; comparison uses matching guest identity.
5. **Legacy compatibility receiver:** intentionally retained and healthy; eventual unbinding/removal requires proving that all publishers/deployments have migrated. It is not blindly purged/deleted. QA-vhost historical error messages remain preserved and outside this local acceptance path.
6. **Consumer retry/restart coverage:** actual broker deliveries and API/consumer restarts were observed during runtime certification. Forced Mongo failure and same-EventId retry use the real-Mongo consumer fixture plus a thrown timeout; no shared Mongo outage or destructive broker fault injection was imposed.
7. **Draft-only invalidations:** generic updates may recompose the same published SQL fields, but never copy Homepage/Navigation draft content into the live derivative. No draft becomes a publication authority.
8. **Historical retention:** immutable uploads and normal StoreConfig version/event history produced by controlled comparisons remain; no cleanup/deletion of rollback assets was performed.

**Final merchant-visible state:** Alizon; title **“Welcome to Shopixy”**; original Amazon image `https://m.media-amazon.com/images/I/61Ud8rdu8sL._AC_SL1500_.jpg`; null authored announcement/theme default; original Header/Footer links; Homepage/Navigation drafts restored through normal controls. SQL and Mongo final revision **345** matched. Subsequent section/card certification was read-only and did not alter that state.

Final headed persistence checkpoint: `.artifacts/gap-final-restored-admin-preview.png` and `.artifacts/gap-final-restored-live.png`; actual server responses confirmed Homepage draft equals live and both Header announcement branches use the theme default. Public browser reload retained the restored title/theme.

Both formerly unresolved gaps now have real runtime/headed visual evidence. The two-gap PASS is therefore not based on source-only Mongo tests or DOM-only theme assertions.
