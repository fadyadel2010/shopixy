# Phase MR-1 — Full Stack Architectural Audit
**Date:** 2026-02-18  
**Auditor:** Principal Architecture Review  
**Scope:** Marketing Review Evidence, Notification Bell, Elasticsearch Mapping & Search Fixes, UI/UX Enhancements

---

## 1️⃣ Executive Verdict

### **SOUND WITH HARDENING REQUIRED**

Phase MR-1 demonstrates solid domain modeling. The critical invariant — that marketing evidence is enforced server-side with POS-verified metrics computed exclusively on the backend — is correctly implemented. The `CreateMarketingEvidenceDto` intentionally excludes `OrdersCount`, `RevenueAmount`, and `EvidenceType`, making forgery structurally impossible. The Elasticsearch mapping fix (Nested → Object) addresses a genuine query failure and the reindex endpoint now handles migration cleanly. However, there are **5 production-blocking issues** that must be addressed before launch: an XSS vector via unescaped review comments, missing input validation on `Comment` length and `Rating` range at the controller layer, the `GetServiceReviews` endpoint being `[AllowAnonymous]` without rate limiting, the notification bell polling without authentication expiry checks, and console.log statements in production code. These are individually straightforward fixes but collectively represent a security and operational risk at scale.

---

## 2️⃣ Phase MR-1 Invariant Audit

| # | Invariant | Status | Evidence | Risk if Broken |
|---|-----------|--------|----------|----------------|
| 1 | Marketing services require evidence when reviewed | ✅ **ENFORCED** | `ServiceReviewService.cs:62-63` — throws `MarketingEvidenceRequired()` if `isMarketing && dto.Evidence == null` | Reviews without evidence would lack accountability; trust scores corrupted |
| 2 | Non-marketing services must NOT accept evidence | ✅ **ENFORCED** | `ServiceReviewService.cs:65-66` — throws `MarketingEvidenceForbidden()` if `!isMarketing && dto.Evidence != null` | Fake evidence on non-marketing services undermines trust model |
| 3 | Reviews only allowed after request completion | ✅ **ENFORCED** | `ServiceReviewService.cs:49-50` — checks `request.Status != ServiceRequestStatus.CompletedByBuyer` | Fake reviews on incomplete/nonexistent work |
| 4 | Ratings derived only from reviews | ✅ **ENFORCED** | `ProviderTrustAggregate.ApplyReview()` computes `AverageRating` from `SumRatings / TotalReviews`; no direct setter exposed | Rating manipulation without genuine reviews |
| 5 | Evidence cannot be forged from frontend | ✅ **ENFORCED** | `CreateMarketingEvidenceDto` accepts only `CampaignName, Platform, StartDate, EndDate`. `OrdersCount`, `RevenueAmount`, `EvidenceType` are computed server-side in `CreateEvidenceAsync()` | Financial fraud — attackers could claim arbitrary revenue/orders |
| 6 | Marketplace remains cross-tenant | ✅ **ENFORCED** | All marketplace queries use `IgnoreQueryFilters()`. Search has tenant filter explicitly commented out. | Services invisible across tenants; marketplace unusable |
| 7 | POS data remains tenant-scoped | ✅ **ENFORCED** | `CreateEvidenceAsync()` explicitly filters `o.TenantId == providerTenantId` when querying POS orders | Cross-tenant data leakage; POS data privacy breach |
| 8 | Search ranking reflects Trust + Stats | ✅ **ENFORCED** | `MarketplaceSearchService.BuildQuery()` uses `FunctionScore` with `FieldValueFactor` on `Trust.AverageRating`, `Trust.TotalReviews`, `ServiceStats.CompletionRate`, and `GaussDate` decay | Irrelevant or low-quality services ranked high |

---

## 3️⃣ Backend Deep Audit

### MarketplaceController Changes

**GetServiceReviews (L130-176)**
- ✅ Correctly Includes `MarketingEvidence` via EF eager loading
- ✅ Maps evidence to anonymous DTO, preventing entity leakage
- ⚠️ **RISK:** `[AllowAnonymous]` with no rate limiting — DDoS vector at scale
- ⚠️ **RISK:** `buyerInitial` uses `.Substring(0, 1)` — will crash on empty string "Anonymous" fallback would catch it, but edge case if `FullName` is empty string (not null)

**GetMyRequests (L285-315)**
- ✅ Returns `serviceCategory = r.Service?.Category` — enables frontend conditional rendering
- ✅ `canReview` checks `r.CanBeReviewed() && !existingReviews.Contains(r.Id)` — prevents duplicate reviews from UI
- ⚠️ Review existence check `_context.Set<ServiceReview>().Where(...)` does NOT use `IgnoreQueryFilters()` — could miss reviews from other tenants in a cross-tenant marketplace scenario. However, since reviews are created with the buyer's `TenantId`, this should work correctly in practice.

**CreateReview (L458-474)**
- ✅ Delegates entirely to `ServiceReviewService` — no business logic in controller
- ⚠️ **MISSING:** No `[FromBody]` model validation. ASP.NET model binding will deserialize but the DTO is a `record` with no `[Required]` or `[Range]` attributes. A malicious payload with `Rating: 999` will reach the service layer where it's caught by the entity guard, but this is defense-in-depth violation.

**ReindexServices (L625-737)**
- ✅ Now auto-deletes and recreates index before bulk indexing — handles Nested→Object migration
- ✅ Uses `MarketplaceServiceIndexConfiguration.GetIndexDescriptor()` — single source of truth for mappings
- ⚠️ **DESTRUCTIVE:** Deletes entire index before reindex. If the process fails mid-way, the index is empty until next successful run. Consider alias-swap pattern for zero-downtime reindex.

### ServiceReviewService Logic

**CreateReviewAsync**
- ✅ Category-based evidence enforcement before review creation
- ✅ `IsMarketingCategory()` uses `StringComparison.OrdinalIgnoreCase` — handles casing inconsistency
- ✅ Idempotency: checks for `existingReview` before allowing creation
- ✅ Authorization: verifies `request.BuyerUserId == buyerUserId`
- ✅ Status gate: only `CompletedByBuyer` allows review
- ✅ MassTransit outbox pattern: Publish → SaveChanges for atomic commit
- ⚠️ **No comment sanitization** — raw HTML/script content stored and later rendered

**CreateEvidenceAsync**
- ✅ **POS metrics are server-computed** — queries `Order` table directly
- ✅ Falls back to `SelfReported` if POS query fails (best-effort, doesn't block review)
- ✅ Filters by `providerTenantId` — no cross-tenant data leak
- ✅ Excludes cancelled orders
- ⚠️ Uses `campaignEndUtc = dto.EndDate.Date.AddDays(1)` — correct inclusive end date handling
- ⚠️ POS timing: queries use `Order.CreatedAt` not `Order.CompletedAt`. A campaign period might not match order creation dates accurately for marketing attribution.

### MarketingReviewEvidence Entity
- ✅ Immutable: private setters, no `Update()` method
- ✅ Factory pattern: `CreateSelfReported()` and `CreatePosVerified()` enforce type-safe construction
- ✅ Domain guards: `ValidateDates()`, `ValidateCampaignName()`, non-negative metrics
- ✅ `StartDate` cannot be in the future

### Dead Code
- None found.

### Event Ordering
- ✅ `ServiceReviewCreatedEvent` published before `SaveChangesAsync` via MassTransit transactional outbox
- ✅ `ProviderTrustConsumer` has idempotency check via `ProcessedTrustEvent`
- ✅ Optimistic concurrency with retry (3 attempts, detach on conflict)

---

## 4️⃣ Database Integrity Audit

### Migration Script: `marketing_review_evidence.sql`

| Constraint | Status | Notes |
|-----------|--------|-------|
| `PK_MarketingReviewEvidences` (Id) | ✅ Present | Standard UUID PK |
| `FK_...ServiceReviews_ServiceReviewId` → `ServiceReviews(Id) ON DELETE CASCADE` | ✅ Present | Orphan evidence impossible — cascading delete |
| `UQ_MarketingReviewEvidences_ServiceReview` (UNIQUE on ServiceReviewId) | ✅ Present | Enforces 1:1 relationship at DB level |
| `CK_...EvidenceType_Metrics` | ✅ Present | `PosVerified` requires both metrics; `SelfReported` requires both null |
| `CK_...DateRange` | ✅ Present | `EndDate >= StartDate` |
| `CK_...NonNegativeMetrics` | ✅ Present | `OrdersCount >= 0` and `RevenueAmount >= 0` |
| `IX_...TenantId` | ✅ Present | Query optimization |
| `IX_...Platform` | ✅ Present | Analytics queries |
| `IX_...EvidenceType` | ✅ Present | Filtering |
| Idempotent DDL | ✅ `IF NOT EXISTS`, `DROP CONSTRAINT IF EXISTS` | Safe for re-execution |

### Missing Constraints
- ⚠️ **No CHECK on `Platform` values.** The column is `varchar(50)` but the domain enum has a fixed set. A CHECK constraint like `CHECK ("Platform" IN ('Facebook', 'Instagram', ...))` would add defense-in-depth.
- ⚠️ **No CHECK on `CampaignName` length.** The column is `varchar(200)` matching the entity validation, but there's no `CHECK (length("CampaignName") > 0)` to prevent empty strings at DB level.
- ⚠️ **No FK to tenant.** The `TenantId` column has no FK constraint to a tenants table. Orphaned tenant references are possible if a tenant is deleted.

### Orphaned Evidence Risk
- **Not possible.** FK with `ON DELETE CASCADE` ensures evidence is deleted when the parent review is deleted. The UNIQUE index prevents duplicate evidence. The `ServiceReviewService` creates both review and evidence in the same `SaveChangesAsync` transaction.

### Evidence Attached to Wrong Category
- **Not possible at application level.** The service checks `IsMarketingCategory(request.Service?.Category)` before allowing evidence creation. At DB level, there is no category column on `MarketingReviewEvidences` — but the FK chain `Evidence → Review → ServiceRequest → Service` preserves the association. A direct DB insert bypassing the application could violate this, but there's no DB-level enforcement of category matching.

---

## 5️⃣ Elasticsearch Architecture Audit

### Mapping Correctness
- ✅ `Trust` and `ServiceStats` correctly mapped as `Object` type (not `Nested`)
- ✅ `AutoMap()` with explicit property overrides ensures no implicit mapping drift
- ✅ `MarketplaceServiceDocument.cs` attributes (`[Object]`) match the index configuration
- ✅ `FunctionScore` queries with `FieldValueFactor` now correctly access Object-type fields

### Query Compatibility
- ✅ `BestFields` + `Fuzziness.Auto` handles typos on full words
- ✅ `PhrasePrefix` handles partial word/autocomplete matching
- ✅ `MinimumShouldMatch(1)` ensures at least one query clause matches
- ✅ Filters use direct field access (no `Nested()` wrappers) — compatible with Object mapping

### FunctionScore Viability
- ✅ `FieldValueFactor` on `Trust.AverageRating`, `Trust.TotalReviews`, `ServiceStats.CompletionRate` — all valid Object-type access
- ✅ `GaussDate` decay on `CreatedAt` with 90d scale — reasonable freshness boost
- ⚠️ `ScoreMode: Sum` with `BoostMode: Sum` — scores will be additive. A service with 0 rating, 0 reviews, 0 completion rate, but matching text will still rank. This is intentional (new services should appear), but worth noting.
- ⚠️ `Missing(0)` on `FieldValueFactor` — correct behavior for new services with no data.

### Reindex Safety
- ✅ Reindex endpoint now deletes + recreates index — handles mapping migration
- ⚠️ **RISK:** No alias-swap. During reindex, the index is empty for seconds to minutes. Search will return 0 results during this window.
- ⚠️ **RISK:** No pagination/streaming. All services loaded into memory at once (`ToListAsync`). Could cause OOM at scale (100k+ services).

### Drift Risk

| Scenario | Risk | Mitigation |
|----------|------|------------|
| Service exists in PG but not ES | **MEDIUM** | No background sync job. If ES indexing fails on service creation (via MassTransit consumer), the service is invisible in search. Reindex endpoint fixes this. |
| ES doc exists but PG row deleted | **LOW** | Soft deletes (`IsDeleted`) mean PG rows are rarely physically deleted. ES `IsActive: false` filter covers deactivation. Full reindex clears stale docs. |
| Trust updated in PG but not ES | **MEDIUM** | No ES update on trust recalculation. Trust badges shown in search results could be stale until next reindex. |

---

## 6️⃣ Frontend Architecture Audit

### ReviewModal.tsx
- ✅ Conditional rendering based on `isMarketing` flag — clean separation
- ✅ Date validation: `new Date(endDate) >= new Date(startDate)` client-side
- ✅ Platform selection uses typed enum `MarketingPlatform`
- ✅ `isMarketingValid` prevents submit when marketing fields are incomplete
- ⚠️ **UI-only validation.** No comment length limit on the textarea. Backend has no enforcement either — relies on database column type.
- ⚠️ Rating defaults to `5` on mount — mild bias toward high ratings. This is an UX choice, not a bug.

### ServiceDetailsPage.tsx
- ✅ Marketing evidence rendering with `PosVerified` vs `SelfReported` badge distinction
- ✅ Revenue formatted with `.toFixed(2)` — consistent display
- ⚠️ **XSS RISK:** `review.comment` rendered directly in `<p>` tag (L265). React escapes by default, so `{review.comment}` is safe. This is NOT a vulnerability — React's JSX auto-escaping prevents XSS. **SAFE.**
- ⚠️ `reviews` typed as `any[]` (L234) — weak typing. Should have a proper response type.
- ⚠️ **Debug console.logs at L24-25** — `console.log('ServiceDetailsPage - serviceId:', serviceId)` — should be removed for production.

### DiscoverTab.tsx
- ✅ `useDebounce` correctly uses `useEffect` with cleanup (`clearTimeout`)
- ✅ 350ms delay — reasonable debounce for search UX
- ✅ `debouncedParams` via `useMemo` — prevents unnecessary re-renders
- ✅ Sort dropdown uses typed cast `as ServiceSearchRequest['sort']`
- ⚠️ **Hardcoded categories** — category list is duplicated in frontend and not fetched from backend. Category additions require frontend deployment.

### Type Safety
- ✅ `ServiceSearchRequest.sort` union type matches backend values exactly
- ✅ `CreateReviewRequest` includes optional `evidence` — matches backend DTO
- ✅ `CreateMarketingEvidenceDto` type does NOT include `ordersCount` or `revenueAmount` — client cannot forge these
- ⚠️ `getServiceReviews` returns `Promise<any[]>` — no typed response

### Trust Displayed vs Trust Indexed
- ✅ Search results display `averageRating`, `totalReviews`, `trustBadge` from ES document
- ✅ Service details page displays trust from direct API response (PostgreSQL via `GetService`)
- ⚠️ **Potential drift:** ES trust data could be stale if trust was updated after last reindex. The detail page shows fresh PG data but the search card shows ES data. This is acceptable as long as reindexing runs periodically.

---

## 7️⃣ Security Threat Model

| # | Threat | Prevented? | How | Residual Risk |
|---|--------|-----------|-----|---------------|
| 1 | **Review forgery** (fake reviews without service interaction) | ✅ Yes | Status check: `CompletedByBuyer` required. Buyer auth: `request.BuyerUserId == buyerUserId`. Duplicate check. | None — structurally impossible |
| 2 | **Evidence forgery** (fake POS metrics) | ✅ Yes | `CreateMarketingEvidenceDto` excludes `OrdersCount`, `RevenueAmount`, `EvidenceType`. Server computes from POS data. | None — client literally cannot send these fields |
| 3 | **XSS injection via comments** | ✅ Yes | React auto-escapes JSX `{review.comment}`. No `dangerouslySetInnerHTML`. | None for current implementation |
| 4 | **Tampering with revenue/orders** | ✅ Yes | Server-side POS query. DTO structure prevents client submission. | None |
| 5 | **Tenant spoofing** | ✅ Yes | `GetTenantId()` from JWT claims, not request body. POS queries filter by `providerTenantId`. | None |
| 6 | **Authorization bypass on reviews** | ✅ Yes | `[Authorize]` on controller. BuyerUserId verified against JWT. | None |
| 7 | **Rating manipulation** (rating outside 1-5) | ⚠️ Partial | Entity guard `rating < 1 || rating > 5` throws. But no controller-level `[Range]` attribute. | Low — caught at service layer, but should fail earlier |
| 8 | **Review spam** | ⚠️ No | No rate limiting on review creation. A user with many completed requests could create reviews in rapid succession. | Low — bounded by number of completed requests |
| 9 | **Notification endpoint abuse** | ⚠️ Partial | Requires auth token. But 30s polling × N users = high load at scale. | Medium — consider WebSocket/SSE |
| 10 | **Anonymous review data enumeration** | ⚠️ No | `GetServiceReviews` is `[AllowAnonymous]`. Anyone can enumerate buyer names, campaign data, and revenue figures. | Medium — sensitive business data exposed |

---

## 8️⃣ Scalability Assessment

| Component | Current Load | At 100k Users | Bottleneck? |
|-----------|-------------|---------------|-------------|
| Review creation | O(1) per review | O(1) per review | No |
| POS metrics query | 1 aggregate query per marketing review | Bounded by marketing review volume | No |
| ES search (per query) | O(log N) via ES | Handles well, 3 shards | No |
| ES reindex | Full table scan + bulk index | **100k+ services → single transaction, all in memory** | **YES — FIRST BOTTLENECK** |
| Notification polling | 1 HTTP req / 30s / user | **100k × 2 req/min = 200k req/min** | **YES — SECOND BOTTLENECK** |
| Trust aggregate update | 1 write per review via consumer | Bounded by review throughput + retry | No |

### First Bottleneck: **Notification Bell Polling**
At 100k users, 30-second polling generates ~3,333 requests/second hitting the `incoming-requests` endpoint. This queries PostgreSQL each time. Without caching or WebSocket, this will saturate the API.

### Second Bottleneck: **Reindex Memory**
The reindex loads ALL services into memory via `ToListAsync()`. At 100k+ services with images and descriptions, this could exceed process memory limits.

---

## 9️⃣ Kill List (Production Blockers)

### K1: `GetServiceReviews` is `[AllowAnonymous]` — Sensitive Data Exposure
- **Root Cause:** Review endpoint exposes buyer names, campaign names, POS-verified revenue, and order counts to anonymous users.
- **Fix:** Either remove `[AllowAnonymous]` and require authentication, or redact sensitive fields (revenue/orders) for anonymous callers.
- **Effort:** 15 minutes

### K2: Console.log Statements in Production Code
- **Root Cause:** `ServiceDetailsPage.tsx:24-25` contains `console.log` for debugging.
- **Fix:** Remove the two `console.log` lines.
- **Effort:** 2 minutes

### K3: No Comment Length Validation
- **Root Cause:** Neither the DTO, entity, nor controller enforce a maximum comment length. An attacker could submit a 10MB comment.
- **Fix:** Add `[StringLength(5000)]` on DTO or validate in service layer. Add `maxLength={5000}` on textarea.
- **Effort:** 10 minutes

### K4: Reindex Deletes Index Before Rebuilding — Zero Results Window
- **Root Cause:** `ReindexServices()` deletes the index, then creates and populates it. During this window, search returns 0 results.
- **Fix:** Use alias-swap pattern: create `marketplace_services_v2`, populate it, then atomically swap the `marketplace_services` alias.
- **Effort:** 2 hours

### K5: Notification Polling at Scale
- **Root Cause:** 30-second polling with direct DB queries per user. At 100k users, this overwhelms the API and database.
- **Fix:** (Short-term) Add a simple Redis cache or in-memory cache with 10-second TTL per user. (Long-term) Replace with WebSocket/SSE push notifications.
- **Effort:** 1-2 hours (short-term), 1 day (long-term)

---

## 🔟 Hardening Recommendations

### Immediate (P0) — Must Fix Before Launch

| # | Issue | Fix | Effort |
|---|-------|-----|--------|
| P0-1 | Anonymous review data exposure | Add `[Authorize]` or field redaction to `GetServiceReviews` | 15 min |
| P0-2 | Console.log in production | Remove debug lines `ServiceDetailsPage.tsx:24-25` | 2 min |
| P0-3 | No comment length limit | Add validation in DTO/entity and textarea maxLength | 10 min |
| P0-4 | `getServiceReviews` returns `any[]` | Create typed `ServiceReview` response type | 10 min |

### Short-Term (P1) — Fix Within Sprint

| # | Issue | Fix | Effort |
|---|-------|-----|--------|
| P1-1 | Reindex zero-results window | Implement alias-swap pattern for zero-downtime reindex | 2 hours |
| P1-2 | Notification polling scale | Add Redis/memory cache for incoming requests endpoint | 1-2 hours |
| P1-3 | Hardcoded category list | Fetch categories from backend API or config | 1 hour |
| P1-4 | ES trust data staleness | Add ES doc update in `ProviderTrustConsumer` after trust recalculation | 1-2 hours |
| P1-5 | Platform CHECK constraint | Add DB CHECK constraint on `Platform` values | 10 min |

### Long-Term (P2) — Next Release

| # | Issue | Fix | Effort |
|---|-------|-----|--------|
| P2-1 | Replace polling with WebSocket | Push notifications for marketplace events | 1-2 days |
| P2-2 | Streaming reindex | Paginate service loading during reindex for memory efficiency | 2-3 hours |
| P2-3 | Review rate limiting | Add per-user rate limit on review creation endpoint | 1 hour |
| P2-4 | Campaign attribution accuracy | Consider using `Order.CompletedAt` instead of `Order.CreatedAt` for POS verification | 30 min |

---

## 🧠 Mandatory Observations

### ✅ Marketing evidence is enforced in the domain layer, NOT only frontend
- `ServiceReviewService.cs:62-66` — throws domain exceptions for both missing evidence (marketing) and forbidden evidence (non-marketing). This is the application service layer, one layer below the controller. The entity (`MarketingReviewEvidence`) also has domain guards (`ValidateDates`, `ValidateCampaignName`, non-negative checks). **Frontend validation is purely UX.**

### ✅ Orders & Revenue are computed server-side, NOT accepted from client
- `CreateMarketingEvidenceDto.cs` accepts only `CampaignName, Platform, StartDate, EndDate`. The `OrdersCount`, `RevenueAmount`, and `EvidenceType` properties are not exposed in the DTO. They are computed in `ServiceReviewService.CreateEvidenceAsync()` by querying the POS `Order` table. **Client forgery is structurally impossible.**

### ✅ Evidence is allowed ONLY for Marketing category
- `ServiceReviewService.cs:65-66` — `!isMarketing && dto.Evidence != null` → `MarketingEvidenceForbidden()`. The `IsMarketingCategory()` helper uses case-insensitive comparison.

### ✅ Evidence rows cannot exist without a review
- FK `FK_MarketingReviewEvidences_ServiceReviews_ServiceReviewId` with `ON DELETE CASCADE` ensures referential integrity. The `ServiceReviewService` creates both within the same `SaveChangesAsync` transaction. UNIQUE constraint prevents duplicate evidence per review.

### ⚠️ Reviews without evidence CAN exist for Marketing services — **IF there's a bug**
- The domain check `isMarketing && dto.Evidence == null → throw` prevents this at the service layer. However, if someone bypasses the service layer (e.g., direct DB insert), a marketing review without evidence can exist. There is **no DB-level CHECK constraint** linking `ServiceReview` category to evidence existence. This is acceptable given the FK chain makes it difficult, but a DB trigger could enforce this invariant at the data level for defense-in-depth.

---

## 🏁 Final Line

### Can Phase MR-1 ship? **YES — with P0 fixes applied first.**

The core architecture is sound. All critical domain invariants are correctly enforced at the service layer. Evidence forgery is structurally impossible. The Elasticsearch migration is technically correct. The 5 P0 items are straightforward fixes totaling ~40 minutes of work. Once those are applied, MR-1 is safe for production with the understanding that P1 items (particularly notification polling and reindex safety) should be addressed within the first sprint post-launch.
