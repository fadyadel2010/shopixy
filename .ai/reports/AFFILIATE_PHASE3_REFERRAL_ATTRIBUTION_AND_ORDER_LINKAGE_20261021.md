# Affiliate Phase 3 — referral attribution and order linkage

Date: 2026-10-21. Environment: primary Windows workspace and its configured development PostgreSQL/MongoDB. No tester-machine access, staging, commits, migrations, database reset, historical-data repair, external payment, collection, transfer, or carrier booking.

## Verdict

**PASS for the scoped referral → durable first-touch evidence → canonical order-attribution journey**, including headed Shopixy guest checkout and Greatone authenticated checkout, SQL linkage, idempotent replays, merchant isolation, security negatives, and baseline financial preservation.

**Not an unconditional repository-wide certification.** The broader backend selection timed out after at least 12 source-text assertion failures. Successful coupon application/removal is **BLOCKED** by the absence of eligible coupons; inactive lifecycle and expiry negatives use executable isolated tests rather than changing live saved links. See limitations below.

All evidence paths below are relative to `.ai/evidence/affiliate-phase3-20261021/` unless otherwise stated. Source and evidence remain unstaged/uncommitted; unrelated working-tree work was preserved.

## Implemented authority chain

1. Storefront bootstrap parses a bounded, normalized referral candidate. Tenant-scoped session storage remains a navigation hint, not attribution authority.
2. `httpClient` sends the candidate via `X-Affiliate-Ref`; only the server's validated `X-Affiliate-Mode`/`X-Affiliate-Ref` verdict enables affiliate presentation. Subscribers update banners/context reactively. Older pre-capture negative responses cannot erase a newer confirmed admission.
3. `AffiliateReferralMiddleware` admits a new product-specific referral only on a matching product landing/read. It verifies the active saved link's merchant, product eligibility, verified global credential, active affiliate profile, and credential/profile ownership bridge against SQL.
4. Existing DataProtection-protected, tenant-bound `AFF_ATTR` evidence retains the existing **first-valid-touch per merchant / 30-day** policy. `AFF_SESSION` remains the click-dedup identifier; initial parallel requests can reuse the existing shopping-session ID. Invalid protected evidence is discarded and can be replaced by a genuine matching landing.
5. Protected evidence allows later navigation, login, cart and checkout requests without relying on the URL or frontend storage. Current SQL admission is checked again. Infrastructure failure during checkout validation returns retryable HTTP 503 instead of silently losing attribution.
6. Checkout revalidates the global credential/profile bridge and promoted product's current SQL eligibility inside the existing final transaction. Existing order snapshot, `AffiliateAttributions`, checkout-session conversion, and SQL idempotency remain the authoritative persistence path.
7. `ApplicationDbContext.EnsureOrderIdentityLinks` now links the affiliate independently from the buyer. Referral is a relationship between different people, not proof they are the same canonical identity. Existing canonical conflict guards remain intact.

No new attribution table or migration was needed. No commission formula, recognition, balance, payout, settlement, payment, or accounting implementation was changed for Phase 3.

### Compatibility decisions

- New touches must match the saved link's product; subsequent protected attribution remains **store-scoped**, as before. Buying another product in that merchant does not by itself erase a valid first touch.
- Legacy profile-code admission remains limited to the existing legacy landing entry. A global profile code is not a product-promotion code, checkout identity override, or affiliate login token.
- URL, DTO and storage candidates do not override an admitted first touch or grant affiliate-private access.
- Login deliberately clears saved checkout details under the existing auth-store privacy contract. Greatone's referral and merged cart survived; details were re-entered and then survived an authenticated refresh. This was not changed to retain another user's private checkout data.

## Files changed for Phase 3

- `PointOfSale.API/Middleware/AffiliateReferralMiddleware.cs`
- New `PointOfSale.API/Services/Affiliate/AffiliatePromotionAttributionAuthority.cs`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.Storefront/src/bootstrap/affiliateRefTracker.ts`
- `PointOfSale.Storefront/src/bootstrap/affiliateRefTracker.test.ts` (new)
- `PointOfSale.Storefront/src/services/api/httpClient.ts`
- `PointOfSale.Storefront/src/services/api/httpClient.privacy.test.ts` (updated mock)
- `PointOfSale.Storefront/src/contexts/AffiliateContext.tsx`
- `PointOfSale.Storefront/src/components/layout/AffiliateBanner.tsx`
- `PointOfSale.Storefront/src/components/checkout/AffiliateGuestCheckoutBanner.tsx`
- New `PointOfSale.Tests/Affiliate/AffiliateReferralJourneyTests.cs`
- Phase 3 evidence and this report.

The workspace contains other pre-existing Phase 1/2 and unrelated changes. This list is not a claim that the entire working-tree diff belongs to Phase 3. Initial inventory: `working-tree-before.txt`.

## Authoritative order results

Both saved links resolve to global credential `01a03e11-e2b3-7875-9b59-6e25f59f03d1` and profile `01a03e11-e234-7d40-b77e-99f9463c7a40`.

| Field | Shopixy | Greatone |
|---|---|---|
| Tenant | `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` | `c43aebf2-4cc4-406a-a2db-4df06d40bce5` |
| Product | Samsung Galaxy S26 Ultra | Dell Latitude 7420 i5/16GB/256GB SSD |
| Product ID | `606a46bc-24b5-76af-834d-34933906d5f2` | `8cff905d-5513-3c64-3698-e3fededc8287` |
| Saved promotion link | `01a15f25-4119-7d7e-a868-e1e38d8e3c75` | `01a15f25-746b-72c1-b20d-c29516b4e3c4` |
| Referral code | `K8YUSVCRWF` | `2T9LXU8E2I` |
| Checkout | Guest | Signed-in synthetic customer |
| Order | `ORD-83B3-2026-01091` | `ORD-C43A-2026-01093` |
| Order ID | `d1102ed5-4256-4cef-80ca-41fe57c6163e` | `32a82d41-2ae6-47f9-aa4f-cd5d17b1e8d6` |
| Attribution ID | `01a15fac-cc86-7196-a830-b6c77ee082e3` | `01a15fc1-1b2a-71e6-a3ca-f56aeb944144` |
| Cart | `e57e2c30-b56c-40f1-bee5-d5d31aac6b1e` | `5709ce5c-c1a0-46f9-8aa8-b175e4dc7a5a` |
| Converted checkout session | `c3d5965c-2368-481a-93cd-226646cd25bd` | `11d699c3-0e96-45d6-8d2f-a7fdeb856970` |
| Quantity / unit price | 1 / 770 EGP | 1 / 2,200 EGP |
| Shipping / tax / discount / payment fee | All zero | All zero |
| Final total | **770 EGP** | **2,200 EGP** |
| Payment / fulfillment | COD, unpaid, pending, unfulfilled | COD, unpaid, pending, unfulfilled |
| Canonical attribution rows | 1 | 1 |
| Commission / payment-intent / settlement-order rows | 0 / 0 / 0 | 0 / 0 / 0 |
| Accepted replay | HTTP 200, same order ID | HTTP 200, same order ID |

Primary evidence: `orders-final.json`, `order-linkage.json`, `shopixy-idempotent-retry.json`, `greatone-fixed-submit-result.txt`, `greatone-idempotent-retry-result.txt`.

### Greatone failure found and fixed

The real authenticated checkout originally failed with HTTP 500 and a rolled-back SQL transaction. The context attempted to merge affiliate canonical identity `c1a819b0-be67-42e7-9417-5513ea26f767` with buyer canonical identity `2dbcd5e1-b7e2-4778-890d-b2ac34e3bfce`.

An executable regression reproduced the failure for both synchronous and asynchronous saves: `authenticated-identity-before.log`/`.trx`. Removing the buyer's preferred canonical identity from the affiliate-link call fixed the conflation without rewriting either identity or weakening the shared conflict guard. Guest/unattributed cases and existing cross-system identity guards pass. `order-linkage.json` confirms both distinct existing identities remain intact after Greatone checkout.

`orders-before-greatone-fixed-retry.json` established there was no committed Greatone order before retry. Failed attempted order `33a1a816-291f-477c-bc15-343b5abe9527` was not persisted. A consumed order-number sequence during rollback is not an extra order.

## Headed browser evidence

Microsoft Edge session `opencode-ui-test` was registered as headed. Real Storefront/backend requests were used; no mocked successful checkout responses, forged Host headers, or direct SQL inserts were used to obtain these orders.

| Journey / case | Result | Evidence |
|---|---|---|
| Shopixy saved product link → matching product admission | PASS | `shopixy-prepare-result.txt`, `shopixy-product.png` |
| Variant selection → add to cart → cart refresh | PASS | `shopixy-cart-checkout-result.txt`, `shopixy-cart.png` |
| Saved guest details → shipping-option changes → refresh → payment/review | PASS | `shopixy-details-result.txt`, `shopixy-payment.png`, `shopixy-review.png` |
| Real Shopixy COD order and SQL-confirmed replay | PASS | `shopixy-confirmation.png`, `orders-after-shopixy.json`, `shopixy-idempotent-retry.json` |
| Greatone without a referral does not inherit Shopixy | PASS | `greatone-prepare-result.txt` |
| Greatone link → cart remove/re-add → one aborted noncommitting cart read → retry | PASS | `greatone-persistence-result.txt`, `greatone-cart-request-abort.png` |
| Synthetic registration/login while checking out; referral and cart survive | PASS | `greatone-auth-result.json`, `greatone-registration.png`, `greatone-checkout-after-login.png` |
| Authenticated saved details refresh, shipping changes and back-step navigation | PASS | `greatone-review-result.txt`, `greatone-review.png` |
| Invalid coupon rejected; referral and totals retained | PASS | HTTP 400 `COUPON_INVALID` in `greatone-review-result.txt` |
| Greatone checkout, replay, confirmation refresh | PASS | `greatone-fixed-submit-result.txt`, `greatone-idempotent-retry-result.txt`, `greatone-confirmation-refreshed.png` |
| Return to Shopixy restores its own referral, not Greatone's | PASS | `greatone-idempotent-retry-result.txt`, `shopixy-return-isolated.png` |

### Security negatives

`security-browser-final-result.txt` records fresh-context real HTTP checks:

- Unknown, malformed, foreign-merchant and global-profile codes: no admitted affiliate mode or attribution cookie.
- Valid Shopixy code on a different **existing** product: product HTTP 200, affiliate mode false, no attribution capture. A nonexistent substituted product also returned 404 without admission.
- Foreign `X-Tenant-Id` on Shopixy's host: HTTP 403.
- Referral candidate on the affiliate-private route: HTTP 401; referral does not authenticate an affiliate.
- Tampered protected cookie: no admitted mode; genuine matching landing can recapture valid evidence.
- Invalid later hint and a different valid later saved link: existing first touch remains `K8YUSVCRWF`.
- Deliberately copying Shopixy's protected cookie to Greatone: rejected; no Greatone affiliate banner/admitted mode.
- Screenshots: `invalid-ref-no-banner.png`, `foreign-cookie-no-banner.png`.

Executable isolated journey tests also reject inactive/deleted links, unverified/deleted credentials, mismatched profile bridges, suspended profiles, unpublished/unsearchable/hidden products, disabled policy, inactive categories/merchants, unrelated landing requests, expired/tampered evidence, and authority changes at final checkout. No live link/profile lifecycle was changed to manufacture these cases.

## Click persistence and idempotency

The existing Mongo `affiliate_clicks` unique index remains `(tenantId, sessionId, productId)`; no index/schema change was made. Middleware events may repeat, but the consumer suppresses duplicate persisted clicks.

Read-only Mongo evidence (`clicks-readback.json`) plus SHA-256 session matching (`click-session-digests-result.txt`, `stock-and-click-comparison.json`) confirms **one click per actual Shopixy/Greatone journey session** despite navigation, refresh, retries and repeated API calls. No duplicate tenant/session/product group exists in the readback. Other day-scope rows include Phase 2, abandoned diagnostic sessions and negative-test recaptures; they are not extra orders or duplicate clicks.

Click conversion fields are still null in these unpaid COD cases. They are analytics, not order-attribution/payment authority. The SQL order snapshot and one `AffiliateAttributions` row per order provide the proven order linkage; this report does not claim paid conversion or earned commission.

SQL `OrderRequestIdempotency` has one completed HTTP-200 record for each new order. Both replays returned the original order. Final SQL totals are **62 orders / 9 attributions**, exactly baseline plus the two synthetic cases.

## Preservation and retained synthetic data

`baseline.json` → `final-readback.json`, checked by `compare-preservation.py` and recorded in `preservation-comparison.json`:

| Authority | Before → after | Result |
|---|---|---|
| Original orders | 60 original full-row digests | Every original unchanged |
| Original attributions | 7 original full-row digests | Every original unchanged |
| Saved links | 5 → 5 | Full fingerprint unchanged; all five retained |
| Affiliate profiles | 4 → 4 | Full fingerprint unchanged |
| Commission ledger | 10 → 10 | Full fingerprint unchanged |
| Balance ledger | 0 → 0 | Full fingerprint unchanged |
| Payout requests | 0 → 0 | Full fingerprint unchanged |
| Accounting journals | 220 → 220 | Full fingerprint unchanged |
| Ledger lines | 459 → 459 | Full fingerprint unchanged |
| Accounting outbox | 30 → 30 | Full fingerprint unchanged |

There are no settlement-order rows for either synthetic order. A global pre-task settlement-table fingerprint was not captured, so no broader settlement-table equality claim is made.

Retained ordinary application side effects:

- Two clearly marked synthetic pending/unpaid COD orders and their item/attribution/idempotency/session records, as listed above.
- One synthetic Greatone customer `d607f698-0e88-410b-a0d1-6ebacc222b7c`; registration/login HTTP 200; verification dispatch reported false. Credentials stay in approved private temporary storage, not the report/evidence.
- One unit reserved for each purchased variant. `catalog.json` → `catalog-final.json` and `stock-and-click-comparison.json` show physical quantities unchanged and only the expected reserved quantities moving from 0 to 1. Reservations are not committed/released; synthetic orders were not canceled or otherwise rewritten after verification.
- Click analytics and ordinary event/session/projection side effects. No earned/approved/paid commission, payout, settlement, or accounting posting was created by these checkouts.

Order notes begin `AFFILIATE_PHASE3_20261021_` and state **synthetic; DO NOT FULFILL, CONTACT, OR COLLECT**. These orders/reservations/customer were intentionally retained, not silently cleaned up.

Safety evidence: `readiness.json` confirms enabled sandbox COD and supported manual/free shipping. `notification-safety.json` confirms neither participating merchant has a WhatsApp account. The existing safe API launcher used process-local loopback SMTP (`127.0.0.1:2527`); no external email delivery was enabled. No external gateway, carrier, settlement or collection workflow was invoked.

## Tests and quality gates

| Check | Result | Evidence |
|---|---|---|
| Final selected backend regression suite | **260/260 passed, 0 skipped** | `selected-complete.log`/`.trx`; `run-selected-final.ps1` |
| Expanded referral/identity subset (included above) | **49/49 passed**: 45 journey cases + 4 existing identity cases | `journey-final-expanded.log`/`.trx` |
| Storefront selected regression suite | **436/436 passed**, 52 files | `storefront-regressions-final.log` |
| Tracker cases (included above) | **11 passed** | `affiliateRefTracker.test.ts`, `frontend-focused.log` |
| Affiliate portal selection | **59/59 passed**, 15 files | `portal-regressions.log` |
| Storefront typecheck / final production build | PASS | `storefront-typecheck.log`, `storefront-build-final.log` |
| Admin TypeScript check / production build | PASS | `admin-typecheck-final.log`, `admin-build.log` |
| Scoped Storefront lint | PASS, **0 errors / 3 fast-refresh warnings**, exit 0 | `storefront-focused-lint-verified.log` |
| Diff whitespace check | PASS, exit 0 | `diff-check-final.log` |
| Evidence JWT scan | No JWT values found in scanned Phase 3 text artifacts | `evidence-token-scan.json`; raw token-bearing diagnostics retained only privately |
| Broad backend selection | **FAILED / INCOMPLETE**, timed out at 240 seconds; at least 12 failures | `backend-final.log`, `broad-failures.json` |

The scoped lint command uses the maintained legacy-config wrapper and resolves plugins from `PointOfSale.Web`; initial direct ESLint 9/config and plugin-resolution failures are retained in earlier logs, not presented as passing runs. A regex escape error found by lint was fixed. Deprecation, chunk-size, existing circular-import and fast-refresh warnings remain; no unrelated configuration migration was attempted.

### Broad backend failures — not hidden by the selected green suite

The timed-out broad run reported source-text/assertion failures in:

1. `AffiliateOrderLineageAdminPresentationAuthorityCertificationTests.FrontendOrders_MustAdmitAffiliateEvidenceBeforeRendering`
2. `GlobalReliabilityInfrastructureExtractionTests.SearchAndAffiliateRepairPaths_MustRecomposeCurrentAuthorityBeforeVisibilityMutation`
3. `AffiliateGovernanceCertificationTests.AffiliateTokenGeneration_MustNotEmitTenantIdClaim`
4. `DataAuthorityViolationHardeningTests.AffiliatePayoutDecisions_MustUseSqlLedgersNotMongoMoneyProjections`
5. `CrossSystemCriticalRiskHardeningTests.AffiliateAttributionPlaceholder_MustNotExposePrePaymentOrderAsPaid`
6. `AffiliateCommissionApprovalWithdrawableBalanceCertificationTests.ManualDeliveryEvidence_MustBePersistedForLaterFinancialRevalidation`
7. `AffiliateWithdrawalRequestAuthorityCertificationTests.CertificationArtifacts_MustRecordPassAfPa61`
8. `SearchSeoPublicIndexCertificationTests.LegacyAffiliatePortalDiscoveryStaysSqlAuthorityBound`
9. `AffiliateA11GapClosureCertificationTests.InventoryReservationOutbox_MustBeGuardedAndCommittedWithOrderTransaction`
10. `AffiliateProductEligibilityLifecycleCertificationTests.AffiliateDiscovery_ReadAdmission_RequiresActiveCategoryAuthority`
11. `AdminAffiliateCategoryGovernanceTests.GlobalAffiliateCategoryMutations_MustRequireSuperAdminPolicy`
12. `AffiliateFrontendAuthorityCertificationTests.AffiliateFrontendSessionCache_MustRemainProfileCacheOnly`

Items 8, 10 and 12 also appear in the pre-implementation Phase 3 `backend-baseline.log`. Phase 2 previously documented 10 and 12. The other nine are **not independently baseline-certified here**. Their tests were not weakened or edited to make this task green. No complete broad-suite pass count is claimed, and the remaining runtime test population was not fully executed by that timed-out command.

## Limitations and handoff

- **BLOCKED:** successful coupon apply/remove preservation because `coupons-before.json` is empty for the participating merchants. No coupon was invented or historical discount data changed. Invalid-coupon rejection was verified live.
- Inactive/suspended/deleted lifecycle and 30-day expiry tests are automated isolated cases; no live historical affiliate link, profile or product was disabled. InMemory tests do not constitute PostgreSQL concurrency/race stress certification. Real PostgreSQL commit/rollback and replay were exercised through the headed checkouts.
- Shopixy's initial successful submit navigated before DevTools could read its response body. The error is preserved in `shopixy-submit-result.txt`; SQL was checked before replay, the real confirmation was captured, and replay returned the original order. This is not counted as an unverified checkout.
- An initial Greatone `route.fetch` diagnostic failed before transmission because Node could not resolve `*.localhost`. The raw token-bearing diagnostic was moved to private temporary storage; `greatone-submit-dns-error.txt` is sanitized. Real browser transport subsequently exposed the genuine identity conflict, then successfully verified the fix.
- Some screenshots show existing untranslated `affiliate.storeBadge` text and unrelated Storefront console/build warnings; this task did not certify all presentation/localization or repair unrelated warnings.
- API ran on port 5010 through the safe launcher during verification (observed PID 18824 after restart). **Post-report status update:** the launcher subsequently exited; a read-only listener check confirmed no API listener on port 5010. It was not restarted after completion. The headed test session was retained. No user-owned unrelated processes were terminated.
- The two pending COD orders and their reservations must not be fulfilled/contacted/collected. No cleanup, cancellation, payment, commission recognition, or commit is authorized by this report.

**Stop point:** scoped implementation and evidence complete; repository-wide failing assertions and successful-coupon coverage remain explicitly unresolved.
