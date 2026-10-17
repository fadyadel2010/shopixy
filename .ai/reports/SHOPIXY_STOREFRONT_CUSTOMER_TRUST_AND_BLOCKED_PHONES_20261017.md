# Customer trust evidence and Blocked Phones — focused follow-up

Date: 2026-10-17. **Outcome: confirmed Arabic and trust/block integration defects repaired and activated; focused hosted, configured PostgreSQL, component and authorization checks pass. This is not a guarantee that a customer is trustworthy or unrestricted platform certification.**

Evidence: `.ai/evidence/storefront-trust-20261017/`. Backend TRX: `PointOfSale.Tests/TestResults/customer-trust-*.trx`. Separate, overlapping runs are not additive.

## User request and correction to previous coverage

The user reported missing Arabic at `/ecommerce/blocked-phones` and requested verification of the Orders **Customer trust evidence / Safe** section and its connection to that page.

The earlier Phase 4 EN/AR route smoke checked visibility/direction/width, not translation completeness. A fresh Arabic browser observation reproduced an English title/subtitle because **the entire `blockedPhones` translation namespace was absent**. The previous smoke must not be read as proof that this screen was fully translated. The Phase 4 report now explicitly records that correction and the latest broad-run limitations.

## Confirmed repairs

| Finding | Repair / behavioral evidence |
|---|---|
| Missing Arabic screen strings | Added matching English/Arabic keys for title, subtitle, search, empty state, table columns, unblock confirmation and failures. Real populated Arabic UI and confirmation workflow verified. Dates use Arabic locale; headers align with reading direction; phone numbers/search values preserve left-to-right `+country-code` presentation. Mobile table scrolls within its container. |
| Safe displayed alongside an explicit manual block | The manual block takes precedence in the trust card; a blocked identity is never shown as Safe. Missing/unusable identity also cannot show Safe. The underlying API's heuristic risk level remains separate from the authoritative `phoneRisk.isBlocked` enforcement flag. |
| No direct identity-preserving connection between screens | Added **Manage blocked phones** / **إدارة الأرقام المحظورة**, linking to the exact canonical phone search. Blocked Phones initializes that search from the link. |
| Cached trust survived unblock on the separate page | Supported unblock invalidates all trust queries in the current account/store workspace, not another tenant. Order phone actions also invalidate all current-workspace trust records. Actual SPA history return—not reload—shows the fresh unblocked badge. |
| Duplicate/wrong-target phone actions | Added synchronous in-flight exclusion and submit-time order/workspace capture. A later selection cannot retarget an already-submitted phone mutation. Duplicate unblock is excluded while confirmation is pending. |
| Unblock/readback and last-page state | Distinguish committed unblock with failed refresh from a failed mutation; return to the last valid page after removing its final row. Component tests cover cancellation, readback failure and pagination. |
| Mutable account phone could replace the order's checkout identity | Shared `OrderPhoneIdentity` resolves the saved checkout/shipping phone first, with nonblank profile fallbacks only for legacy orders lacking a snapshot. Trust, spam reporting and order detail display use this same identity. This matches the phone checked by checkout enforcement and prevents a later account edit from silently retargeting the order action. |
| Equivalent phone formats undercounted order history | Tenant/source/non-deleted candidate projection is followed by the shared country-aware normalizer. Local, international, punctuation, whitespace and Arabic-digit forms count as the same identity. The SQL regex is only a candidate prefilter: identical suffixes do **not** establish identity. Foreign/deleted/marketplace orders remain excluded. |
| Invalid legacy phone caused an unhandled trust read | Return unavailable evidence with no usable phone/action target, not Safe and not a silently substituted account identity. |

Safe now has an explicit explanation: **rule-based evidence, not a guarantee; manual blocking takes precedence.** Existing scoring thresholds and the distinction between spam reporting and manually blocking were not changed.

## Failing-before and passing-after evidence

- `admin-red.json`: **six failing / one passing** new component checks before the frontend repair: missing Arabic, contradictory Safe/Blocked, missing-phone Safe, absent deep link, stale workspace cache, and ignored deep-link search.
- `customer-trust-red.trx`: **four failing / one passing** before backend repair: changed/empty account phone overriding snapshot, equivalent-format history, invalid legacy identity. Subsequent tests add Unicode/whitespace and scoring boundaries; not every added case was separately red-run.
- Final focused backend: **66 passed, zero failed/skipped**, `customer-trust-verified-final.trx`:
  - 12 trust/snapshot/history/scoring cases;
  - 10 existing phone-risk regressions;
  - two explicit configured-database cases;
  - 42 MVC authorization cases (six routes × seven personas).
- `admin-focused-verified-final.json`: **31 passing tests**, including ten new trust/Blocked Phones checks and existing Orders/lifecycle regressions. This final focused run follows the test-only ES library compatibility correction.
- Full Admin suite: **1,930 passed, zero failed/pending**, `admin-full-final.json`, bounded to two workers. Earlier full follow-up run passed 1,927 before three additional component cases. The final test-only library-compatibility edit is separately covered by the final focused run/typecheck.
- Admin standalone `npx tsc --noEmit`: **exit 0** after replacing a test-only `Array.at` unsupported by the project's configured library target. No compiler target was weakened or changed.
- Scoped lint: `lint-final.json`, **exit 0, zero errors / eight Fast Refresh export warnings in the existing mixed-export Orders module**. ESLint legacy-config deprecation advisory is not hidden; this is not warning-free or whole-repository lint certification.
- Admin production build: `admin-build-final.log`, **exit 0**, after the final screen source changes; chunk-size warning retained. Subsequent edit was test-only.

## Configured PostgreSQL verification, without fixture provisioning

`CustomerTrustConfiguredTests.ExistingPostgres_TrustSnapshotAliasesBlockUnblockAndSpam_OnlyOwnedRollback` uses the **existing configured database** and an explicit transaction containing only generated synthetic tenant/customer/order/reputation records. It invokes no schema migration/reset/provisioning helper and no event publisher/provider.

It verifies:

1. A different account phone cannot override the saved checkout phone, in either trust or actual Admin order-detail service output.
2. Three equivalent-format historical records, including Arabic digits, produce **three other orders / two cancellations** and the established Suspicious threshold.
3. Foreign-tenant access returns no trust record.
4. Block is visible through both order trust and the blocked list; equivalent-format unblock removes it from that list.
5. Repeating spam report creates exactly **one** report and aggregate count; reporting does **not** silently block the phone.
6. Explicit rollback and a **new DbContext observer** find the generated tenant/orders/profiles/reports/global reputation absent.

Fixture IDs: `configured-fixture.json`; final result: `configured-trust-result.json`. These are real SQL/service checks with a controlled velocity dependency, not full HTTP checkout or real delivery.

## API activation

The user stopped/restarted the API after the isolated build completed. Only project runtime artifacts were copied; application configuration files were not replaced. Before-install artifacts are preserved under `.opencode-tmp/storefront-trust-20261017/api-before/`.

- `api-install.json`: installed files match the isolated build.
- `api-active.json`: PID **23980**, started `2026-10-16T21:30:18.423757Z`, Development, `http://localhost:5010`.
- Active API DLL SHA-256: `424401DC2CF403D1BE85DE3F8532D0956DEE68A7B96F00AA7D2178624659FB58`.
- Infrastructure/Core assembly hashes remain the previously captured values. API build warning level was suppressed; this is not a warning-free-source claim.

## Actual hosted workflow

Reused the recorded synthetic order `ee42f429-fa3d-46b6-988a-b900cc3e8648` / `ORD-C9C5-2026-01054` and QA phone `+201000000099`, starting **unblocked / zero local and global reports**. No new checkout order was submitted.

`browser-trust-closed-final.json`: **26 checks, zero failed checks/harness failures/page exceptions**:

- Both **English and Arabic**: initial Safe → actual Orders **Block Phone** → fresh order trust reports blocked → **no Safe badge**.
- Follow the actual Manage blocked phones link: canonical phone remains the search target; matching blocked row and localized heading are visible.
- Cancel unblock: authoritative state remains blocked. Confirm unblock: actual API persists unblocked, list refreshes.
- Use **SPA browser history** to return to the same order: retained QueryClient does not resurrect the old blocked state; Safe and Block Phone reappear without reloading.
- Arabic populated table headings, Arabic date formatting, LTR phone presentation, and settled **390px** mobile layout verified. Screenshot: `blocked-phones-ar-mobile.png`.
- Final enforcement/report state matches the initial state. No `/report-spam` POST occurred in this round trip.

The native Report Spam confirmation was separately opened using the actual button and **dismissed with the CLI's dedicated dialog command**. `browser-spam-cancel.json` confirms `hasReportedSpam=false`, zero local/global reports and unblocked afterward. **No hosted spam report was committed**; idempotent report persistence is proven by the rollback SQL case, not misrepresented as a live-browser report submission.

`browser-authority.json`: actual cookie-free trust read **401**, cookie-free order block **401**, authenticated foreign-tenant spoof **403**. The 42 MVC policy cases separately use controlled authentication and admitted-action short-circuiting; they are not seven real provisioned hosted accounts.

## Failed harness attempts

- Two early browser sessions terminated because the CLI run-code sandbox has no global `URL`; an event callback threw outside the main try/catch. `browser-harness-url-reference.err` identifies this harness defect. URL parsing was moved to browser evaluation or ordinary string parsing. Those empty output files are not passes.
- A native-dialog attempt returned no completed JSON; the final verification uses separate `click` and `dialog-dismiss` commands instead of claiming that attempt succeeded.
- One run completed the English round trip, then tried to click the language switch behind the open Orders drawer. The harness now closes/reopens the drawer through actual controls before switching language.
- One mobile screenshot captured the layout during its existing 300ms resize transition. Waiting for the transition to settle yields a bounded page; no global Layout workaround was applied.
- One component test initially expected a different Arabic article in confirmation text; the assertion was corrected to the actual intended phrase. Test-only Playwright locator options and ES `Array.at` usage were reconciled with Testing Library/project types.
- The first cleanup observer projected computed journal totals without loading ledger lines, yielding misleading zero/zero values. The observer—not accounting data—was corrected to sum persisted ledger entries in SQL and assert the exact EGP 40 revenue / EGP 10 COGS. The earlier output is retained as `hosted-cleanup-readonly-before-ledger-projection-fix.json`, not treated as financial evidence.

## Cleanup and retained records

After the last hosted round trip, `customer-trust-hosted-cleanup-verified-final.trx` passes a fresh independent **READ ONLY**, repeatable-read PostgreSQL observer (`hosted-cleanup-readonly.json`):

- QA phone **unblocked**, local spam reports **0**, committed scoped spam-report rows **0**, global report count **0**.
- The pre-existing synthetic orders remain **Cancelled/Failed EGP 35** and **Completed/Collected EGP 40**.
- Their two existing balanced journals remain unchanged in count; no extra financial/stock lifecycle was invoked.
- Supported block/unblock retains the latest block reason/timestamp/profile/audit history. Those metadata changes are not erased or falsely called byte-for-byte data restoration.
- Temporary configured SQL fixture records are rolled back and independently absent.
- No WhatsApp account/configuration, payment-provider, carrier-provider or outbound-message operation was performed in this focused follow-up. The earlier QA-disabled WhatsApp account remains a separately disclosed Phase 4 restoration issue, not a trust-flow success claim.

## Preservation and limits

`originals/`, `baseline-hashes.json`, `preservation-final.json` and `task-files-manifest.json` isolate this follow-up from the already-dirty worktree and earlier Phase 4 changes. No staging, commit, reset/revert or broad cleanup.

The latest wider Phase 4 rerun remains **404 passed / four native-scheduler managed-fixture initialization failures**; its database connection/migration timeouts are recorded in the Phase 4 report, not turned green by these focused tests. That legacy auto-provisioning path was not used for this follow-up. The prior Phase 3 broad failures also remain disclosed.

Limitations: Safe is not fraud certification. No large-history performance benchmark, real multi-customer account switching, global-report concurrency race, every-country phone matrix, committed hosted spam report, or new hosted blocked/unblocked checkout submission was executed here. Current-store identity/block persistence, scoring boundaries, authorization and UI integration have the evidence above; lower-level verification is not substituted for unexecuted end-to-end cases.
