# PASS F-S4-C Service Marketplace Frontend Completion Evidence Authority

Service Marketplace Frontend Completion Evidence Authority is production-certifiable for inspected runtime paths after backend evidence-envelope composition, strict frontend evidence validation, and explicit suppression of unsupported notes, attachments, deliverables, previews, and downloads.

## Evidence Identity Findings
- Backend S1-D PostgreSQL request lifecycle authority owns completion evidence identity.
- Request details now carry backend-composed provider-completion and buyer-confirmation evidence phases bound to the authoritative request ID, participant ID, role, lifecycle type, and SQL completion timestamp.
- Frontend route state, chat rows, attachment rows, caches, and local state cannot mint or reassign completion evidence identity.

## Metadata Findings
- Provider and buyer completion metadata derives only from backend request status, `CompletedByProviderAt`, `CompletedByBuyerAt`, and authoritative participant identities.
- The frontend validates exact phase type, role, actor, request ownership, timestamp shape, lifecycle status, phase uniqueness, and chronology without repairing malformed values.
- Completion notes, completion attachments, and completion deliverables do not exist in the certified backend model. The DTO explicitly returns empty collections and the frontend rejects non-empty injected collections.

## Visibility Findings
- Completion evidence renders only on the protected request/chat detail surface after current request authority has finished loading and the lifecycle boundary accepts `CompletedByProvider` or `CompletedByBuyer`.
- Provider-only completion displays provider evidence only. Final buyer completion requires both phases in authoritative order.
- Refresh, request loss, lifecycle loss, actor loss, tenant loss, malformed DTOs, or failed current authority remove evidence presentation.

## Download Findings
- No completion-evidence download exists in the current runtime.
- Generic chat attachments remain F-S3-C chat attachments and are never promoted into completion evidence.
- The frontend completion-evidence boundary cannot build attachment gateway paths or signed URLs. Any future completion download requires a backend-owned evidence attachment identity and request-time authorization.

## Preview Findings
- No persisted completion-evidence preview exists in the current runtime.
- Generic chat attachment presentation and local unsent file previews are outside completion evidence authority.
- Injected completion attachment or deliverable metadata invalidates the complete evidence envelope and cannot create a preview.

## Realtime Findings
- SignalR and BroadcastChannel remain invalidate-only.
- Completion and chat events can trigger scoped request refetch but cannot append, patch, or settle completion evidence.
- Replay, reconnect, duplicate delivery, or late events cannot create evidence without a newly accepted backend request-details response.

## Cache Findings
- Completion evidence lives only inside authenticated tenant/user/request-scoped request-detail queries.
- Request queries use zero stale time, no retry, refetch-on-mount/reconnect/focus, and periodic current-authority refresh.
- Cached evidence is withheld during current request refresh and after request failure; no separate evidence cache or browser persistence exists.

## Actor-Transition Findings
- Evidence presentation requires the current authenticated user to equal the authoritative buyer or provider.
- Logout/login, actor replacement, tenant switching, request switching, and route teardown change the query scope and remove or withhold prior evidence.
- A stale DTO cannot be accepted for a replacement actor because request ownership and current participant identity are revalidated.

## History Findings
- Back, forward, refresh, bookmarks, and deep links carry request identity only.
- Route re-entry reloads protected session, request, lifecycle, participants, visibility, and completion evidence from backend authority.
- Browser history and route state contain no completion evidence phase, note, attachment, deliverable, preview, or download truth.

## Failure Findings
- 401, 403, 404, 409, 500, timeout, malformed evidence, missing phases, wrong request, wrong actor, wrong role, lifecycle contradiction, invalid timestamp, reversed chronology, duplicate phase identity, and unsupported evidence collections fail closed.
- Failed refresh cannot preserve stale evidence presentation.
- Retry or realtime convergence can restore evidence only through a new accepted backend response.

## Adversarial Findings
- Evidence replay, DTO replay, attachment replay, cache replay, history replay, realtime replay, actor switching, tenant switching, request switching, and lifecycle replay were reviewed.
- Ordinary chat text or attachments cannot become completion notes, deliverables, previews, or downloads.
- No inspected frontend path can fabricate completion evidence, drift ownership, promote evidence into completion authority, or leak evidence across requests or tenants.

## Runtime Hardening Performed
- Added a backend-composed completion-evidence envelope to current request details.
- Bound provider and buyer evidence phases to SQL lifecycle timestamps and authoritative participant identities.
- Added `deriveCompletionEvidencePresentation` with fail-closed request, actor, lifecycle, metadata, chronology, and unsupported-collection validation.
- Added dedicated completion-evidence presentation to the current request/chat detail surface.
- Kept generic chat messages and attachments outside completion evidence authority.

## Exact Runtime Files Modified
- `PointOfSale.API/Controllers/MarketplaceController.cs`
- `PointOfSale.Web/src/marketplace/types/index.ts`
- `PointOfSale.Web/src/marketplace/completionEvidenceAuthority.ts`
- `PointOfSale.Web/src/marketplace/pages/ChatPage.tsx`
- `PointOfSale.Web/src/marketplace/viewmodels/useRequestDetailsVM.ts`

## Regression Freezes Added
- `PointOfSale.Web/src/marketplace/completionEvidenceAuthority.test.ts`
- `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendCompletionEvidenceAuthorityCertificationTests.cs`

## Architecture Updates
- Updated `.ai-context/PROJECT_CONTEXT.md`.
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`.
- Updated `.ai-context/KNOWN_RISKS.md`.
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- Updated `.ai-context/CODEX_RULES.md`.

## Explicit Answers
- Who owns completion evidence authority? Backend S1-D PostgreSQL completion plus current request, lifecycle, actor, visibility, attachment, and chat authorities own truth; frontend F-S4-C owns derivative presentation only.
- Can frontend fabricate evidence? No.
- Can evidence ownership drift? No on inspected paths; request, phase, actor, role, and participant mismatches fail closed.
- Can stale evidence survive refresh? No on inspected presentation paths.
- Can evidence become completion authority? No. Evidence presentation cannot mutate or establish completion.
- Can downloads bypass backend authorization? No completion-evidence download exists; generic attachment downloads re-enter F-S3-C backend authorization.
- Can previews survive authority loss? No completion-evidence preview exists, and injected preview-shaped metadata invalidates evidence.
- Can realtime fabricate evidence? No.
- Can cross-request evidence leakage occur? No on inspected paths.
- Can tenant leakage occur? No on inspected paths.
- Were runtime changes required? Yes.
- Is Service Marketplace Frontend Completion Evidence Authority production-certifiable? Yes, for inspected runtime paths after this hardening.

## Residual Operational Risk
- Future completion notes, deliverables, evidence attachments, content types, previews, or downloads require new backend-owned storage and authorization contracts plus renewed S1-D, F-S3-C, and F-S4-C review.
- Production should retain browser and PostgreSQL-backed chaos coverage for refresh failure, lifecycle transitions, simultaneous tabs, timeout-after-commit, reconnect, actor replacement, tenant switching, and cross-tab invalidation latency.
