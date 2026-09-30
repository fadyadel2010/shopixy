# Abandoned Checkouts Analytics and Localization Final Certification

**Date:** 2026-09-30  
**Branch:** `main`  
**Surface:** `/ecommerce/abandoned-checkouts`  
**Result:** PASS WITH OPERATIONAL OBSERVATION

## Executive Result

The Abandoned Checkouts administration surface is certified for authoritative analytics, English and Arabic localization, tenant isolation, projection-drift tolerance, failure-state accuracy, and desktop/mobile operation.

The certification used read-only database inspection and authenticated browser requests. It did not create or modify carts, checkout sessions, orders, settings, recovery schedules, payments, shipments, or messages.

The remaining operational observation is that the MongoDB read projection was not populated in the audited environment. The page now detects this drift and serves tenant-scoped PostgreSQL authority instead of presenting a false empty state. Repairing or replaying the projection remains a separate operational task and is not required for page correctness.

## Authority Decision

- PostgreSQL `CheckoutSession` is the lifecycle authority.
- MongoDB `abandoned_checkouts` remains a read projection, not an authority.
- Invalidated sessions are excluded from list and metric calculations.
- Admin statistics are calculated directly from tenant-scoped PostgreSQL data.
- List requests retain the MongoDB read path only while per-status lifecycle counts agree with PostgreSQL.
- Projection disagreement triggers a read-only PostgreSQL fallback and a structured warning.
- Missing MongoDB detail documents fall back to a tenant-scoped PostgreSQL detail query.
- No schema migration was required.

The audit found authoritative checkout data in PostgreSQL while the corresponding MongoDB projection was empty. The previous successful empty API response was therefore a projection result, not a genuine empty business result.

## Metric Semantics

- Total sessions: all non-invalidated authoritative sessions for the tenant.
- Abandonment rate: `abandoned / total * 100`.
- Conversion rate: `converted-to-order / total * 100`.
- Estimated abandoned value: abandoned cart snapshots only; it is explicitly not described as confirmed lost revenue.
- Estimated value is unavailable rather than misleading when values are incomplete or span multiple currencies.
- Percentages are supplied by the backend and formatted by the selected locale; the client does not recalculate them.

Live authenticated results reconciled exactly with the aggregate-only PostgreSQL audit. Exact tenant counts and financial values are intentionally omitted from this report.

## Backend Remediation

- Added PostgreSQL authority queries for statistics, drift fallback lists, and missing projection details.
- Added deterministic secondary ordering for stable pagination.
- Added explicit invalid-status rejection.
- Added escaped, case-insensitive name, phone, and email search on the SQL fallback.
- Added currency/value-availability metadata.
- Removed the raw recovery token from the abandoned-checkout detail contract.
- Retained the authorized signed recovery URL needed by the detail workflow.
- Restricted checkout conversion attribution to `OrderSource.Ecommerce`; POS, Affiliate, and CommerceHub orders cannot mark checkout sessions converted through this consumer.
- Preserved `Owner,Admin,Manager` authorization and tenant predicates on every authoritative query.

## Frontend Remediation

- Completed English and Arabic copy for the page, filters, table, metrics, detail drawer, pagination, failures, and empty states.
- Added RTL layout and locale-aware numbers, percentages, currency, dates, relative times, and durations.
- Localized known device, status, and traffic-source classifications.
- Added deferred search and request sequencing so superseded responses cannot replace newer results.
- Separated initial loading, refresh, stale-data, API failure, permission, authentication, genuine empty, and constrained no-match states.
- Kept previously loaded rows visible when a refresh fails.
- Added accessible filter state, search labeling, dialog semantics, and responsive table containment.
- Clarified that a checkout recovery link restores checkout only and does not confer Smart Cart Recovery eligibility or send WhatsApp.

## Headed Browser Certification

Authenticated Microsoft Edge testing used the existing isolated `opencode-ui-test` session.

| Scenario | Result |
| --- | --- |
| Authoritative list and statistics endpoints | PASS, both `200` |
| Default abandoned filter | PASS |
| All, Active, Converted filters | PASS |
| Known-match and no-match search | PASS |
| PostgreSQL-backed detail fallback | PASS |
| Controlled `503` list failure vs empty state | PASS |
| Retry after controlled failure | PASS |
| English LTR presentation | PASS |
| Arabic RTL presentation | PASS |
| Arabic numerals, currency, dates, durations, classifications | PASS |
| Desktop viewport | PASS |
| Mobile `390x844` viewport | PASS |
| Mobile detail drawer containment | PASS |
| Document-level horizontal overflow | PASS, none |
| Table overflow containment | PASS, local horizontal scroll only |
| Final page console | PASS, no errors; two existing React Router future-flag warnings |

The controlled failure route was removed before final verification. Final live endpoint traffic returned successful responses.

## Security and Boundaries

- No cross-tenant query path was identified.
- No anonymous access was introduced.
- No raw recovery token is present in the admin detail DTO.
- No token, customer identity, contact information, session identifier, cookie, credential, or private analytics value is included in this report or its screenshots.
- Abandoned Checkouts analytics and native Smart Cart Recovery remain separate domains.
- Native recovery was rechecked after certification: configuration and eligibility requests returned `200`; recovery remained disabled and ineligible with `recovery_disabled`.
- No WhatsApp dispatch or recovery-setting mutation occurred.

## Automated Verification

| Command | Result |
| --- | --- |
| `npm test -- --run src/pages/AbandonedCheckouts.test.tsx` | PASS, 9 tests |
| `npm run build` in `PointOfSale.Web` | PASS; existing large-chunk warning only |
| Focused `AbandonedCheckoutAnalyticsArchitectureTests` | PASS, 3 tests |
| API build to isolated temporary output | PASS, 0 errors; existing warnings remain |
| Translation JSON parse/build consumption | PASS |
| `git diff --check` | PASS; line-ending notices only |

The full backend suite was attempted but was not a valid green baseline for this change. Unrelated existing failures included a missing shipment certification marker, SQLite `btrim` support, unrelated architecture source contracts, and stock-transfer timeout/configuration failures. The running API also locks its normal output assemblies, so focused verification used an isolated temporary output directory.

## Test Coverage Added

- English and Arabic rendering with unresolved-key checks.
- Backend-authoritative metric display.
- Generic API, permission, and authentication failures kept distinct from empty data.
- Stale-row preservation after refresh failure.
- All status-filter query contracts.
- Detail rendering and Smart Cart Recovery boundary copy.
- Required translation-key parity.
- Source contracts for PostgreSQL tenant authority, projection fallback, e-commerce-only conversion attribution, and raw-token exclusion.

## Redacted Evidence

- `docs/reports/evidence/abandoned-checkouts-en-desktop-redacted-2026-09-30.png`
- `docs/reports/evidence/abandoned-checkouts-ar-mobile-redacted-2026-09-30.png`

Both screenshots were captured from the headed session after replacing private metric values, removing customer rows, and replacing tenant identity in the browser DOM. They are presentation evidence only; runtime authority was verified separately through authenticated responses and aggregate-only inspection.

## Residual Risks

- The MongoDB lifecycle projection remains operationally stale until replayed or repaired. SQL fallback prevents false empty results but does not heal the projection.
- Drift comparison adds SQL and MongoDB aggregate work to list requests. Monitor the structured drift warning and endpoint latency if checkout volume grows substantially.
- The repository-wide backend test baseline has unrelated failures and should be restored independently.
- Existing React Router future-flag warnings remain outside this page's scope.

## Certification Decision

The page is approved for the audited behavior. Analytics are authoritative despite projection drift, localization is complete for the exercised English and Arabic workflows, failure states are truthful, security boundaries are preserved, and desktop/mobile headed checks pass. Smart Cart Recovery remained disabled and unaffected throughout certification.
