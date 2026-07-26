# PASS F-S3-C Service Marketplace Frontend Attachment Authority

Service Marketplace Frontend Attachment Authority is production-certifiable for inspected runtime paths after attachment/message identity binding, upload-response validation, transition cleanup, persisted-preview suppression, and download cache hardening.

## Attachment Identity Findings
- Backend S1-C chat and attachment authority own attachment identity and ownership.
- The attachment route identifier is the owning backend message ID. The frontend now rejects a history row when `attachmentId` differs from `message.id`.
- Attachment presentation requires a current validated message bound to the current request and participants.

## Metadata Findings
- The backend supplies the attachment filename from its validated object key. The frontend validates but does not repair, derive, or replace it.
- Empty, oversized, path-shaped, control-character, and mismatched filenames fail closed.
- File size and content type are not present in persisted message DTOs and are therefore not presented as persisted attachment truth.

## Visibility Findings
- Attachments render only inside currently accepted F-S3-A/F-S3-B chat history.
- Request, lifecycle, actor, tenant, history, or malformed DTO failure removes the entire message and attachment presentation.
- Local selected-file intent is cleared before paint when request, actor, or send capability changes.

## Download Findings
- The frontend creates only the same-origin authenticated attachment gateway path from the validated backend message ID.
- Every gateway request rechecks authenticated tenant and SQL conversation visibility before issuing a five-minute server-signed object-storage URL.
- Gateway redirects now carry `no-store`, `private`, `max-age=0`, `Pragma: no-cache`, and expired response headers.

## Preview Findings
- Persisted inline image preview inferred from filename extension was removed because filename extension is not authoritative content-type metadata.
- Persisted attachments use generic download presentation until backend DTOs supply validated content type suitable for a renewed preview boundary.
- Local pre-upload image preview is browser-local unsent intent, uses the selected `File`, revokes object URLs, and is cleared on authority transitions.

## Realtime Findings
- SignalR remains an invalidation trigger only and cannot append or fabricate attachment rows.
- Reconnect rejoins current chat authority and refetches complete REST history.
- Replayed or foreign-request events cannot create attachment identity, metadata, visibility, preview, or download presentation.

## Cache Findings
- Attachments live only inside tenant/user/request-scoped chat query results.
- Cached history is withheld during current refresh and after failure, and prior actor scopes are removed.
- No attachment object key, signed URL, blob URL, or persisted attachment DTO is stored in browser storage or a separate frontend cache.

## Actor-Transition Findings
- Logout/login, actor switching, tenant switching, route changes, and send-capability loss clear selected-file intent and fence stale upload/send completions.
- Upload responses must contain a server object key structurally bound to the initiating request and current user.
- Stale upload completion cannot clear, unlock, or submit the replacement actor/request composer.

## History Findings
- Browser history and deep links carry request identity only and re-enter current request/chat authority.
- Attachment paths are generated only from accepted current history rows.
- Back/forward, refresh, bookmarks, and route replay cannot supply attachment metadata, object keys, or signed URLs.

## Failure Findings
- 401, 403, 404, 500, timeout, malformed message DTO, mismatched attachment ID, unsafe filename, malformed upload response, foreign request key, foreign actor key, and storage failure fail closed.
- Failed current refresh withholds stale attachment presentation.
- Failed or ambiguous sends invalidate authoritative chat history through F-S3-B convergence.

## Concurrency Findings
- Same-tab sends are synchronously fenced.
- Request/actor transition increments a send-operation generation so stale upload/send completion cannot mutate replacement composer state.
- Realtime, reconnect, refresh, upload, send, actor-switch, and route races converge through current identity checks and complete REST history validation.

## Adversarial Findings
- Attachment-ID replay, filename replay, DTO replay, object-key replay, URL replay, cache replay, history replay, SignalR replay, actor switching, tenant switching, and cross-request injection were reviewed.
- The frontend cannot fabricate an accepted attachment, reassign it to another message, infer persisted preview type, preserve it after authority loss, or grant download authority.

## Runtime Hardening Performed
- Bound `attachmentId` to the owning backend message ID.
- Added a frontend upload-response boundary for tenant-shaped, request-bound, user-bound server object keys and matching filenames.
- Routed chat uploads through the canonical marketplace API client.
- Cleared local attachment/composer intent and fenced stale completion state across authority transitions.
- Removed filename-derived persisted inline previews.
- Added non-cacheable response headers to the authenticated attachment redirect.

## Exact Runtime Files Modified
- `PointOfSale.Web/src/marketplace/attachmentAuthority.ts`
- `PointOfSale.Web/src/marketplace/chatAuthority.ts`
- `PointOfSale.Web/src/marketplace/api/marketplaceApi.ts`
- `PointOfSale.Web/src/marketplace/components/ChatMessage.tsx`
- `PointOfSale.Web/src/marketplace/pages/ChatPage.tsx`
- `PointOfSale.API/Controllers/MarketplaceController.cs`

## Regression Freezes Added
- `PointOfSale.Web/src/marketplace/attachmentAuthority.test.ts`
- Updated `PointOfSale.Web/src/marketplace/chatAuthority.test.ts`.
- `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendAttachmentAuthorityCertificationTests.cs`
- Updated `PointOfSale.Tests/MarketplaceAttachmentSecurityTests.cs`.

## Architecture Updates
- Updated `.ai-context/PROJECT_CONTEXT.md`.
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`.
- Updated `.ai-context/KNOWN_RISKS.md`.
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- Updated `.ai-context/CODEX_RULES.md`.

## Explicit Answers
- Who owns attachment authority? Backend S1-C SQL chat/request authority plus backend attachment storage and download authorization authority.
- Can frontend fabricate attachments? No.
- Can attachment ownership drift? No on inspected paths; attachment ID must equal the owning message ID and upload keys must match current request/user scope.
- Can stale attachments survive refresh? No on inspected presentation paths.
- Can download URLs bypass authority? Frontend-generated paths cannot; each gateway request rechecks backend authority. Short-lived signed storage URLs remain server-issued backend download authority.
- Can previews survive authority loss? No. Persisted filename-derived inline previews were removed, and local unsent previews are revoked/cleared.
- Can SignalR fabricate attachments? No.
- Can cross-request attachment leakage occur? No on inspected paths.
- Can tenant leakage occur? No on inspected paths.
- Were runtime changes required? Yes.
- Is Service Marketplace Frontend Attachment Authority production-certifiable? Yes, for inspected runtime paths after this hardening.

## Residual Operational Risk
- Production should retain browser and PostgreSQL-backed chaos coverage for lifecycle loss during upload, timeout-after-upload orphan cleanup, simultaneous tabs, signed-URL expiry, object-storage CORS/cache behavior, reconnect during actor changes, and attachment malware/content-disposition policy.
