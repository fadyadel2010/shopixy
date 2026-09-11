# Shopixy M-1D Vendor Refund, Return, And Financial Certification

Date: 2026-09-02

## Verdict

**CODE VERIFIED: PASS. TEST VERIFIED: PASS for the focused M-1D boundary. API VERIFIED: PASS. DATABASE VERIFIED: PASS for the canonical order and operational ledger. ACCOUNTING GL VERIFIED: PASS. EVENT VERIFIED: PASS. PROJECTION VERIFIED: PASS. ORD-CH-0009-A RUNTIME DELIVERY REPAIR GATE: PASS. HUMAN VERIFIED: PARTIAL for the Super Admin desktop/mobile refund execution/replay path only. ORD-CH-0004 HUMAN MATRIX: INVALIDATED BY LATER CANONICAL CHANGE AND UNRESOLVED EXTERNAL-MONEY CLAIM. Overall M-1D certification: NOT YET COMPLETE.**

One fresh two-vendor COD order completed through isolated vendor fulfillment, parent delivery aggregation, COD collection, vendor earnings, a vendor-B-only customer refund request, platform approval, return logistics, vendor receipt, Development-only COD debt-reduction execution, accounting posting, exact event observation, idempotent replay, and derivative projection convergence. Vendor A remained unaffected throughout the vendor-B return and refund.

The refund is `Paid` through explicit Development simulation evidence. The execution records internal COD debt reduction and refund recognition only: transaction reference prefix `DEV-SIM:`, gateway status `development-simulated-cod-refund`, `IsDevelopmentSimulation=true`, and `ExternalMoneyMovementConfirmed=false`. No external transfer or money-sent claim is made. A human operator subsequently observed the Super Admin desktop execution and replay path for `ORD-CH-0009-A`; customer, vendor, and mobile observations remain unverified, and no API, database, queue, or projection result is promoted to human evidence.

## Post-Certification Return Authority Correction

Correction date: 2026-09-03

- A subsequent current-worktree review found that POS responses granted return logistics actions while every POS logistics command failed with `403 RETURN_LOGISTICS_WORKFLOW_REQUIRED`. The only usable controls were on Super Admin, contradicting the ownership model in which the vendor holding the product manages fulfillment and return transport after platform refund approval.
- `CommerceHubReturnPolicy` remains the sole transition policy for `Pending -> PickupScheduled -> PickedUp -> InTransit -> DeliveredToVendor`, vendor receipt confirmation, and terminal failure. The override policy and override DTO fields were removed rather than retained as a second authority path.
- POS vendor commands now carry the authenticated tenant as `vendorId`, reject foreign returns as `404`, require the current backend status, lock the canonical SQL row, reject stale status with `409 RETURN_STATUS_CONFLICT`, and revalidate the legal next transition before mutation. Missing `ExpectedStatus` fails with `428 RETURN_EXPECTED_STATUS_REQUIRED` before service dispatch.
- The POS vendor frontend displays only backend-granted return actions, re-fetches the current vendor-scoped order before dispatch, submits the refreshed return status as `expectedStatus`, and exposes tracking, courier, notes, failure reason, and receipt controls.
- Super Admin return HTTP authority is read-only. Its `override-status` and `mark-failed` routes, mutation DTOs, frontend service functions, buttons, state, and transition modal were removed. Super Admin retains refund review/approval/rejection and return monitoring.
- Focused service regressions prove legal vendor progression, stale-status rejection, cross-vendor hiding, rejection of a Super Admin logistics source, and missing-controller-precondition rejection. Architecture tests freeze the read-only Super Admin surface and vendor-only mutation surface.
- The earlier runtime response showing `CanOverrideStatus=true` and `AllowedOverrideStatuses=[PickupScheduled]` came from the superseded interim build and is not current authority evidence. No canonical lifecycle mutation was issued during this correction.
- The `highquality` tenant read returned refund `4b3a80dc-9571-4843-90a9-c2096c5b8262` under `ORD-CH-0004-A`; the `shopixy` tenant returned zero matches for that foreign suborder. An unauthenticated Super Admin return read returned `401`.
- A PostgreSQL read-only transaction re-proved `WaitingForReturn/Pending`, `OnPickup`, zero execution attempts, zero execution rows, zero refund financial transactions, and zero accounting outbox rows after restart.
- Verification passed for the corrected source: focused backend `18/18`; focused vendor/Super Admin frontend `22/22`; Release compilation and production frontend build. Existing repository analyzer and large-chunk warnings remain.
- After restarting the corrected API, `GET /health/live` and `GET /health/marketplace` returned `200`. Runtime OpenAPI exposed POS vendor POST routes for schedule pickup, pickup, transit, delivery, failure, and both receipt aliases, while Super Admin exposed only `GET /api/super-admin/returns/{returnId}`.
- Runtime OpenAPI confirmed `canSchedulePickup` remains a backend grant, `expectedStatus` is present on the POS status request, `428` is documented for missing preconditions, and `canOverrideStatus` plus `allowedOverrideStatuses` are absent. An unauthenticated Super Admin return read returned `401`.
- An earlier broad architecture selection surfaced two unrelated stale source-string assertions against the concurrently dirty vendor-finance authority file. The changed return-authority tests were then isolated and passed; no unrelated file was rewritten to hide those failures.
- `GET /health/live` and `GET /health/marketplace` returned `200`. Aggregate health remains non-green because Elasticsearch is RED; projection drift and disabled certificate/Caddy integrations are also reported as degraded. These are not promoted to a globally healthy claim.
- No authenticated return mutation was executed after this correction because preserving the canonical refund state was required. Tenant isolation, authentication rejection, SQL state, controller preconditions, service transitions, frontend contract, and builds were verified independently. Human verification remains `NOT VERIFIED`.

## ORD-CH-0009-A Frontend-Driven Refund Execution Addendum

- Addendum date: 2026-09-03.
- A human Super Admin operator used the live Vite frontend at `/super-admin/refunds?refundId=11cb1632-7cb8-4240-b14a-e83a6359a30f`; no direct mutation HTTP, SQL, service invocation, browser automation, delegation, or sub-agent performed the mutation.
- The target lineage is parent `ORD-CH-0009` (`b76516c5-5022-4df2-97b2-04189223c956`), suborder `ORD-CH-0009-A` (`a0c6dce4-2e7b-49fe-b33a-35f761ffdc9d`), refund `11cb1632-7cb8-4240-b14a-e83a6359a30f`, return `c6d4137c-d981-4ff1-ab79-69eb664cdb1f`, and financial refund `f128a220-e0bf-40e6-9192-21cee980fb5a`.
- The frontend called the Development-only, Super Admin-authorized canonical COD debt-reduction path with stable request key `M1D-11cb16327cb84240b14ae83a6359a30f`. The operator observed transaction reference `DEV-SIM:M1D-11cb16327cb84240b14ae83a6359a30f` and gateway status `development-simulated-cod-refund`.
- Audit records `743` and `744` explicitly contain `IsDevelopmentSimulation=true`, `ExternalMoneyMovementConfirmed=false`, the stable request key, and the simulated transaction reference. The operator confirmed that no real `1320.01 EGP` transfer occurred; this evidence must not be described as an external payout.
- The final refund is paid with exactly one execution attempt and one successful execution `f7635ef2-cab6-420a-a74e-b0f8b9dd532a`. Completion and execution timestamps remain `2026-09-03T13:48:09.6740740Z`.
- The return remains confirmed for quantity `1`, with resellable quantity `1` and damaged quantity `0`. No CommerceHub payment or payout row exists, as expected for the COD debt-reduction path.
- The financial breakdown is refund `1320.01 EGP`, commission reversal `132.00 EGP`, promo reversal `0.00 EGP`, and vendor earning reversal `1188.01 EGP`. The original earning is reversed.
- Exactly two target refund operational-ledger rows remain: vendor debt reduction credit `132.00 EGP` and platform commission debit `132.00 EGP`. At the immediate replay checkpoint, vendor debt remained `264.00 EGP` after moving from the pre-refund `396.00 EGP` balance; a later separate `ORD-CH-0004-A` refund reduced the same vendor's current aggregate debt to `132.00 EGP` and is not attributed to this replay.
- Both refund accounting messages processed without failure. Journals `JE-2026-000010` and `JE-2026-000011` each contain two lines and balance exactly at debit `132.00 EGP` and credit `132.00 EGP`.
- MongoDB converged to suborder `Refunded/Refunded` and parent payment `PartiallyRefunded`, with last event ID `2abe097a-8932-4cad-a628-2aa7271de647`, source event version `6`, and a populated projection checksum. The transient MassTransit and projection outboxes are drained; no claim of a newly captured runtime envelope payload is made for this addendum.
- The operator invoked `Replay Development simulation` from the same frontend and received the validated response acknowledgment. Read-only post-replay inspection retained execution attempts `1`, debt `264.00 EGP`, the same refund/execution IDs and timestamps, the same two refund ledger rows, the same two journals, and the same Mongo event ID/version/checksum.
- The Super Admin UI now labels this record `Development refund simulation`, says that no external money movement occurred or was confirmed, uses simulation-specific record/result/evidence labels, and leaves production payout-confirmation wording unchanged. The Development execution and replay controls are removed by the production Vite environment guard.
- Two operator-visible frontend defects were corrected during this observation: an orphaned return-modal setter crashed the refund page, and a later simulation-label variable was initially declared outside the refund drawer. Focused authority tests now freeze both corrections.
- The unrelated `/api/auth/session` `401` originates from customer-auth bootstrap; authenticated Super Admin refund list/detail requests remained `200` and the validated Development replay completed through the Super Admin session.
- Final focused verification passed `36/36` backend tests and `16/16` frontend refund-authority tests. The production Vite build passed with only the existing oversized-chunk warning.
- Final health checks returned `200 Healthy` for `/health/live` and `/health/marketplace`. `/health/ready` returned `503`; the refund-relevant MassTransit bus, projection outbox, integration outbox, accounting outbox, PostgreSQL, MongoDB, and RabbitMQ checks were healthy, while Elasticsearch remained RED with one unassigned shard and payout external-truth reconciliation reported two unrelated mismatches.
- This addendum supplies human evidence only for the Super Admin refund detail at desktop and `390x844` mobile width, plus the desktop replay path. Customer and vendor observations remain outstanding.

## Post-Addendum ORD-CH-0004 Canonical Invalidation

- During the human customer check, the CommerceHub customer frontend correctly displayed both `ORD-CH-0004-A` and `ORD-CH-0004-B` as refunded. This no longer matched the historical vendor-B-only checklist, so it was recorded as a certification failure rather than hidden as a UI defect.
- Read-only PostgreSQL inspection proved that vendor-A refund `4b3a80dc-9571-4843-90a9-c2096c5b8262` is now paid with financial refund `2b5c660d-a837-43e7-a084-4ed5d9d21d22` and execution `f757c121-e2eb-4d29-9b6e-ad50a2bbff18`. Vendor B's historical refund remains paid.
- Vendor-A execution occurred at `2026-09-03T15:57:20.6885150Z` through the regular manual-confirmation path with transaction reference `333` and gateway status `manual-confirmed`.
- Audit records `747` and `748` identify the Super Admin actor and explicitly assert `IsDevelopmentSimulation=false` and `ExternalMoneyMovementConfirmed=true`. The operator cannot verify that an external `1320.01 EGP` transfer actually occurred, so external-money truth is unresolved and fails closed.
- PostgreSQL now records both suborders as `Refunded/Refunded`; MongoDB agrees at parent `Refunded/Refunded`, event version `12`, with vendor A and vendor B both `Refunded/Refunded`. The customer frontend was presenting current canonical truth.
- No data was reverted, repaired, or directly mutated during this investigation. The historical vendor-B-only human-isolation scenario cannot be recovered by relabeling current evidence.
- To prevent another accidental false external-money claim in Development, COD debt-reduction refunds now present only the Development simulation action. The real manual payout-confirmation action remains available for production builds and non-COD execution paths.
- `ORD-CH-0009` provides a separate current isolation candidate: vendor A is refunded through explicit Development simulation while vendor B remains `Pending/CashOnDelivery` with no refund. The customer/vendor human matrix may use that lineage, but it remains open until every required role and responsive surface is observed.

## Customer Simulation Presentation Correction

- Human observation in the actual CommerceHub customer application at `http://localhost:5175` confirmed the current `ORD-CH-0009` split: vendor-A item OPPO is refunded for `1320.01 EGP` with a `DEV-SIM:` reference, while vendor-B item Nokia remains `Pending/CashOnDelivery` for `400.00 EGP` with no refund.
- The same observation found a truthful-presentation defect on both the customer refunds page and customer order-details page: the simulated completion said `Your refund has been sent successfully.`, labeled its timestamp `Paid at`, and labeled the simulation identifier as a transaction reference.
- Both customer surfaces now recognize the canonical `DEV-SIM:` evidence, present `Development refund simulation`, explicitly say `Internal refund recognition only. No external money movement occurred.`, label the identifier `Simulation reference`, and label the timestamp `Simulation recorded at`. Non-simulated completed refunds retain the existing payment wording.
- Long simulation references now wrap instead of overflowing narrow customer layouts.
- Focused payment-visibility architecture tests passed `2/2`. The CommerceHub TypeScript and production Vite build passed; the Web production Vite build also passed after the Development COD action guard. Existing bundle-size and stale Browserslist-data warnings remain.
- The pre-correction screenshots establish discovery of the defect and current split-order isolation, not a customer presentation PASS. Corrected desktop/mobile customer observation remains required before HUMAN VERIFIED can advance.

## ORD-CH-0009-A Runtime Delivery Repair Gate

- Parent `ORD-CH-0009` (`b76516c5-5022-4df2-97b2-04189223c956`) and vendor-A suborder `ORD-CH-0009-A` (`a0c6dce4-2e7b-49fe-b33a-35f761ffdc9d`) were classified as valid current COD lineage requiring reservation repair, not as a disposable fixture.
- The source correction gives COD reservations no expiry while retaining the 30-minute lease for online payments. Reconciliation excludes live COD lineage from expiry cleanup but leaves cancelled and refunded COD lineage cleanup-eligible.
- The original reservation `d7c5632b-761d-4d59-a1a9-db1355963c41` had already expired and been released by the stale runtime behavior.
- A Development-only, Super Admin-authorized, tenant/order/version-scoped, audited endpoint repaired the exact missing quantity through `IInventoryConsistencyService.ReserveOrderItemAsync`; it did not directly update reservation rows.
- Canonical repair created reservation `cf031833-81bb-4e1d-8af9-78a5d7f557cb` for exact quantity `1.00`, warehouse `ecc6f369-11fa-46cf-ba65-f3d038458c78`, with `ExpiresAtUtc=null`. Repair replay created no duplicate stock or reservation effect.
- The vendor-A canonical delivery PATCH returned `200`, moved the suborder from `Shipped/CashOnDelivery` to `Delivered/Collected`, and advanced lifecycle version `3` to `4` at `2026-09-02T18:18:18.551178Z`.
- Parent state correctly remained `Shipped/CashOnDelivery` because vendor-B suborder `ORD-CH-0009-B` remained `Pending/CashOnDelivery`.
- SQL inventory committed the repaired reservation exactly once. Stock moved from quantity/reserved/available `39/1/38` to `38/0/38`; sale movement `c3ec8df2-60ce-4fb0-a252-123b67ff3a2a` and applied inbox event `697bb730-6800-feab-3b49-a0195f6c2cfa` are the unique delivery consumption pair.
- Exact stale replay with expected version `3` returned `409 SUBORDER_VERSION_CONFLICT`. Current-version semantic replay returned `200` at version `4` and produced no second movement, inbox record, earning, financial transaction, outbox row, or journal.
- Vendor B received `403 SUBORDER_FORBIDDEN`; a wrong suborder returned `404`. Anonymous, customer, and Super Admin requests failed closed in tenant resolution and did not reach mutation authority.
- Customer order projection returned vendor A as `Delivered/Collected` with `paidAt=2026-09-02T18:18:18.551Z`, while vendor B remained pending. Vendor-A action authority returned version `4` with no available fulfillment action.
- Financial authority contains exactly one available vendor earning `a4f1e823-9ada-4ba9-8ab4-90946559c667` for `1188.01 EGP`, plus three unique transactions: vendor COD cash credit `1320.01`, vendor commission debit `132.00`, and platform commission credit `132.00`.
- All three `CommerceHubLedgerPosting` outbox rows processed with `AttemptCount=0`, `IsFailed=false`. Journals `JE-2026-000004`, `JE-2026-000005`, and `JE-2026-000006` are posted, contain two entries each, and balance exactly at `132.00`, `1320.01`, and `132.00 EGP` respectively.
- Unrelated refund `4b3a80dc-9571-4843-90a9-c2096c5b8262` remained `WaitingForReturn`, its return remained `Pending`, execution attempts remained zero, and no execution row was created.
- Focused backend tests passed `34/34`; focused vendor frontend tests passed `39/39`; isolated Release API, CommerceHub customer frontend, and vendor/admin frontend builds completed with zero errors.
- `GET /health/marketplace` and `GET /health/live` returned `200`. Delivery-relevant outbox, stock reservation, PostgreSQL, MongoDB, RabbitMQ, and CommerceHub platform checks were healthy with zero pending or critical outbox rows.
- Aggregate `/health` and `/health/ready` returned `503` solely because the shared Elasticsearch cluster was RED with one unassigned shard. The CommerceHub search alias and smoke checks remained healthy, so this is recorded as an unrelated infrastructure residual rather than hidden or promoted to a globally healthy result.
- This gate contains machine evidence only. No browser automation or human desktop/mobile observation was performed.

## Certified Boundary

- PostgreSQL remains authoritative for the marketplace order, isolated suborders, payment lifecycle, fulfillment, vendor earnings, refund, return, operational financial transactions, and audit records.
- A vendor can fulfill and inspect only its own suborder and manages only its own return logistics and delivered return receipt.
- Super Admin approves or rejects the refund request and can monitor the return, but cannot schedule pickup, advance transport, mark delivery, fail the return, or confirm vendor receipt.
- Refund creation is customer-idempotent and approval creates the canonical vendor-scoped return.
- COD refund approval does not claim that money was sent. Execution remains a separate privileged action.
- The certification execution endpoint exists only in Development, requires Super Admin authorization, uses the canonical refund service, and cannot claim external money movement.
- MongoDB is a derivative order projection and RabbitMQ/MassTransit provide delivery and convergence rather than business authority.
- The accounting outbox is a durable handoff from CommerceHub financial transactions to the general ledger.

## Verification Matrix

| Evidence class | Command/evidence | Result |
| --- | --- | --- |
| CODE VERIFIED | Checkout, fulfillment, payment, refund, return, finance, safety guard, accounting mapper, forensic API, outbox, and projection source inspection | PASS |
| TEST VERIFIED | `CommerceAccountingServiceTests` | 35/35 passed |
| TEST VERIFIED | Combined accounting, requeue, rate-limit, financial lifecycle, forensic API, and cash normal-balance selection | 44/44 passed |
| TEST VERIFIED | Refund execution, parent aggregation/replay repair, COD reconciliation, integrity, and Paymob readiness selection | 21/21 passed |
| API VERIFIED | Canonical execution and identical replay returned `200`; protected refund/order/integrity reads returned `200`; unauthenticated execution returned `401` | PASS |
| DATABASE VERIFIED | Read-only PostgreSQL lineage, financial transaction, operation audit, outbox, journal, earning, execution, and replay inspection | PASS |
| ACCOUNTING GL VERIFIED | Refund postings became balanced journals `JE-2026-000024` and `JE-2026-000025` with processed outbox rows | PASS |
| EVENT VERIFIED | A pre-bound observer captured the exact non-redelivered `CommerceHubRefundFinanciallyExecutedEvent`; the queue was drained and deleted after evidence capture | PASS |
| PROJECTION VERIFIED | Mongo `commercehub_orders` converged to the repaired PostgreSQL state at source mutation/event version `10` | PASS |
| FORENSIC VERIFIED | Rebuilt Release API search/export returned the canonical refund and return audit chain without EF entity exposure | PASS |
| HUMAN VERIFIED | Super Admin desktop/mobile refund simulation and desktop replay for `ORD-CH-0009-A` | PARTIAL PASS; customer/vendor evidence remains outstanding and `ORD-CH-0004` is invalidated |
| Marketplace health | `GET /health/marketplace` | Healthy; zero pending PostgreSQL migrations or reported marketplace drift |

## Live Lineage

- Certification run: `M1D-R2-20260901T200106Z-dce4ad7d`.
- Parent order: `5bbf6683-a683-429f-a6c7-2ff0395571c1`, `ORD-CH-0004`, `1720.01 EGP`.
- Vendor A: `a6c9e71e-dd48-49cb-947a-6f460423360e`.
- Vendor A suborder/item: `c06062a1-f245-47ef-8490-335f2ac6b9ce` / `842aefa2-187c-4adc-8e56-8252a6ea9fa0`, `1320.01 EGP`.
- Vendor B: `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`.
- Vendor B suborder/item: `5c2d83fd-cbe4-4f6d-9784-e0cdec619fac` / `ca446f54-b46e-4ef0-939b-860dd5ba175d`, `400.00 EGP`.
- Refund idempotency key: `M1D-R2-refund-vendor-b-001`.
- Refund: `1587335d-558b-450c-af32-d7949998f3cf`, `400.00 EGP`, `Paid`.
- Return: `7b28baf5-e9b5-4696-aab0-dbf5e4f23a03`, vendor B item quantity `1`, received as resellable.
- Financial refund: `27d986c3-ae59-4db1-9cff-bfc42c3181af`, `Completed`.
- Refund execution: `f18ed413-7458-45ec-b24c-4ab8a5eb7942`; exactly one successful execution attempt.
- Refund execution path: `CashOnDeliveryDebtReduction`; evidence type `DevelopmentSimulatedCodCustomerRefund`; no external money movement confirmed.

## Fulfillment Results

- The two vendor suborders progressed independently through `Processing`, `Shipped`, and `Delivered`.
- After only one suborder was delivered, the parent remained `Shipped/CashOnDelivery`.
- After both were delivered, the parent became `Delivered/Collected`.
- Duplicate delivery was idempotent and did not duplicate financial rows.
- A vendor request for the other vendor's suborder returned `403 SUBORDER_FORBIDDEN`.
- Final vendor earnings were `1188.01 EGP` for vendor A and `360.00 EGP` for vendor B.

## Refund And Return Results

- The customer created one vendor-B-only refund for the exact vendor-B order item and replay returned the same refund.
- Platform approval created return `7b28baf5-e9b5-4696-aab0-dbf5e4f23a03`.
- During the original historical certification run, a vendor scheduling attempt returned `403 RETURN_LOGISTICS_WORKFLOW_REQUIRED` and platform authority progressed the return through `PickupScheduled`, `PickedUp`, `InTransit`, and `DeliveredToVendor`. That authority behavior was later classified as a defect and removed; these bullets remain only as historical evidence of how the already-recorded canonical run was produced.
- Vendor A's receipt attempt returned `404`, preserving tenant isolation.
- Vendor B confirmed receipt with `resellableQuantity=1` and `damagedQuantity=0`.
- Return automation moved the refund to `AwaitingPayout` with `CanExecute=true`.
- The canonical Development endpoint executed vendor B's refund once and an identical request replay returned the same `200` response without duplicate financial rows.
- Vendor B became `Refunded/Refunded`; vendor A remained `Delivered/Collected`.
- The parent settled at `Delivered/PartiallyRefunded` after aggregate-status replay repair.
- Vendor A's earning remained `1188.01 EGP`. Vendor B's original `360.00 EGP` earning became `Reversed`.

## Financial Results

- Vendor A final statement remained COD cash collection `+1320.01`, commission `-132.00`, net `1188.01 EGP`.
- Vendor B's pre-refund statement was COD cash collection `+400.00`, commission `-40.00`, net `360.00 EGP`.
- Initial platform commission was `132.00 + 40.00 = 172.00 EGP`; the vendor-B reversal reduced the final platform net to `132.00 EGP`.
- Vendor B's refund posted a `40.00 EGP` vendor receivable credit and matching `40.00 EGP` platform receivable debit.
- Vendor B debt moved from `40.00 EGP` before the refund to `0.00 EGP` after it; platform cash and vendor payable remained `0.00 EGP` for this operation.
- Financial refund breakdown was `400.00 EGP` amount, `40.00 EGP` commission reversal, `0.00 EGP` promo reversal, and `360.00 EGP` vendor earning reversal.
- Replay retained exactly one execution, one financial refund, two refund transactions, two refund journals, and one committed financial operation audit.
- Final reconciliation returned `status=OK`, platform net `132.00`, vendor net `1588.01`, and `totalTransactions=8`.
- Reconciliation reported zero missing idempotency keys, missing correlation IDs, duplicate keys, duplicate references, missing pairs, duplicate entries, correlation mismatches, payable/receivable mismatches, orphan transactions, order/refund/payout/adjustment flow mismatches, multi-currency references, and invalid entity scopes.
- After the integrity and COD pair-classification fixes, the global endpoint honestly returned `ISSUES_DETECTED`, `totalIssueCount=6`, and six older unrelated order commission mismatches. Refund flow mismatch, missing refund reversal, missing pair, orphan transaction, payable, receivable, duplicate, correlation, multi-currency, and invalid-scope counters were zero. None of the sampled issues referenced the M-1D order or suborders.

## Accounting Outbox Remediation

- All six initial canonical `CommerceHubLedgerPosting` messages were durably staged.
- The two vendor commission debits and two platform commission credits processed immediately.
- The vendor-B `400.00 EGP` and vendor-A `1320.01 EGP` COD cash collection rows entered non-terminal retry.
- The failure was deterministic: domain `Credit/Cash` means an increase in the vendor cash bucket, but `CommerceAccountingService` treated it as a literal credit to the bank asset account. `FinancialPostingService` correctly rejected the resulting apparent treasury outflow because an order collection is not outflow authority.
- `ResolveCommerceHubPostingAccounts` now applies asset normal-balance behavior only to `CommerceHubFinancialBucket.Cash`: cash `Credit` debits bank, while cash `Debit` credits bank. Payable and receivable mapping is unchanged.
- Regression coverage verifies both collection and refund/payout directions.
- The stale pre-remediation API won the first scheduled retry and deferred both rows again. A new Development-only, super-admin-authorized requeue endpoint now supports recovery of non-terminal delayed messages after a deployment without direct database mutation.
- After restart, both canonical rows were requeued through that endpoint and the rebuilt processor handled exactly two messages. Accounting outbox status became `pending=0`, `failed=0`, and `processed=13`.
- Vendor B posted as journal `JE-2026-000023`: debit bank account `1030` by `400.00` and credit treasury suspense `2030` by `400.00`.
- Vendor A posted as journal `JE-2026-000003`: debit bank account `1030` by `1320.01` and credit treasury suspense `2030` by `1320.01`.
- Both outbox rows retained their original business IDs and finished with `AttemptCount=0`, `IsFailed=false`, and no last error.
- Refund transaction `1aecc309-d995-4567-ac63-0c3f072f6e13` produced journal `JE-2026-000024`, debit `40.00`, credit `40.00`, two lines, and `Posted` status.
- Refund transaction `a23c91d9-0052-412c-916d-42a711743445` produced journal `JE-2026-000025`, debit `40.00`, credit `40.00`, two lines, and `Posted` status.
- Both refund outbox rows were processed with `AttemptCount=0`, `IsFailed=false`, no last error, and the expected journal IDs.

## Forensic Results

- The original forensic `search` and `export` endpoints returned `500` because they exposed tracked EF entity types and `PreventEntityExposureFilter` correctly blocked the response.
- Both endpoints now project lifecycle, recovery, audit, governance, and financial evidence into explicit response shapes.
- Runtime `search?refundId=1587335d-558b-450c-af32-d7949998f3cf` returned four matching audit records.
- After the final API restart, forensic health reported audit queue depth `0` and audit retry count `0`.
- Runtime export `ad71a04f-846b-493d-8970-b1f13717a5a5` returned ten canonical refund/return audit records and eight canonical financial audit records.
- The audit chain retained `REFUND_REQUEST_CREATED`, `PAYOUT_DETAILS_USED_FOR_REFUND`, `RETURN_CREATED`, `REFUND_APPROVED`, four platform return status overrides, `REFUND_AWAITING_PAYOUT`, and vendor `RETURN_CONFIRMED`.
- All ten canonical audit records had `SuccessFlag=true`.

## Event And Projection Results

- The `commercehub-order-projection` RabbitMQ queue had one active consumer.
- Observer queue `m1d-refund-evidence-20260902` was bound before execution to `PointOfSale.Core.Events:CommerceHubRefundFinanciallyExecutedEvent`.
- It captured exactly one non-redelivered envelope with message ID `14070000-1704-5722-342d-08df08ecabb0` and correlation/event ID `9398e3c5-f35f-4741-8f82-6d39afc31aa1`.
- The envelope contained the exact order, vendor-B suborder, vendor, refund, financial refund, and return IDs; amount `400.00 EGP`; vendor debt reduction `40.00 EGP`; aggregate version `9`; Development simulation metadata; and `ExternalMoneyMovementConfirmed=false`.
- After capture, the observer had zero ready and zero unacknowledged messages and was deleted.
- MongoDB contained one `commercehub_orders` document for `5bbf6683-a683-429f-a6c7-2ff0395571c1` / `ORD-CH-0004`.
- The first refund execution exposed a source aggregation defect: mixed `Delivered + Refunded` suborders produced parent `Shipped/PartiallyRefunded` at version `9`.
- `CommerceHubOrderStateHelper` now treats refunded terminal suborders as neutral when aggregating the remaining fulfilled suborders, and idempotent refund replay repairs an inconsistent parent without duplicating financial effects.
- Exact replay repaired PostgreSQL to parent `Delivered/PartiallyRefunded`, version `10`, while vendor A remained `Delivered/Collected` and vendor B remained `Refunded/Refunded`.
- The normal status event converged MongoDB to the same version `10` state with last event ID `edb5244b-b851-46db-aef3-812c434d576a` and checksum `34C5FB665D8E17AFAFDDDB46F3F8244AF1D5F8C3A6EBDF2D6E5229FE341AD071`.

## Defects Remediated

1. **P0: missing customer refund rate-limit policies.** Registered `CustomerRefundRateLimit` and `CustomerRefundUploadRateLimit` and added endpoint-policy coverage.
2. **P0: missing singleton financial safety guard row.** Added idempotent `PlatformCash` initialization before lock acquisition for PostgreSQL and SQLite while preserving PostgreSQL `FOR UPDATE` behavior.
3. **P1: forensic entity exposure.** Replaced direct EF entity responses with explicit projections and added architecture coverage.
4. **P1: CommerceHub cash direction mapped to the wrong GL side.** Applied cash asset normal-balance mapping and added two-direction accounting regression coverage.
5. **P1: non-terminal accounting retries could not be administratively requeued after remediation.** Added a Development-only, super-admin-authorized requeue path that resets only an existing unprocessed message.
6. **P1: Development tenant test-token reliability.** Resolved the actor from the tenant-scoped POS identity before token issuance.
7. **P1: COD delivery scope lifetime.** Kept financial workflow and payment lifecycle mutation scopes active through `SaveChangesAsync`.
8. **P1: no safe Development COD refund execution evidence.** Added a Development-only, Super Admin-authorized canonical simulation path with explicit no-external-money semantics.
9. **P1: no exact refund financial event evidence.** Added `CommerceHubRefundFinanciallyExecutedEvent`, projection consumption, and pre-bound observer evidence.
10. **P1: integrity summary/detail contradiction.** Kept live issue counters aligned with sampled live issues instead of replacing them with differently scoped snapshot counters.
11. **P1: mixed delivered/refunded parent regressed to shipped.** Fixed terminal-status aggregation and added idempotent replay repair without duplicate financial effects.
12. **P2: Paymob readiness reported only global credentials.** Separated effective tenant/global credential reporting while retaining fail-closed readiness checks.
13. **P1: valid COD commission reversal pair was classified as orphaned.** Reconciliation now requires the platform debit and vendor credit commission pair for COD refunds instead of nonexistent online-refund rows.
14. **P1: return logistics authority was assigned to Super Admin instead of the owning vendor.** Removed Super Admin return mutation routes and controls; moved each legal logistics and failure action to the tenant-scoped POS vendor surface with locked expected-status validation.

## Test And Build Scope

- `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~CommerceAccountingServiceTests`: `35/35` passed.
- Combined focused Release selection for accounting service behavior, non-terminal requeue, refund rate limiting, financial guard/locking, forensic projections, and cash normal-balance behavior: `44/44` passed.
- The final active-lease requeue guard rerun passed `1/1`.
- Focused Release refund execution, aggregate replay repair, COD reconciliation, financial integrity, and Paymob readiness selection passed `21/21`.
- The final Release reconciliation-suite rerun passed `12/12`.
- `dotnet build PointOfSale.API/PointOfSale.API.csproj -c Release --no-restore`: PASS with `0` warnings and `0` errors.
- The Release solution build completed with zero errors.
- Focused test builds emitted existing repository analyzer warnings; no focused test failed.
- The post-correction refund/return authority selection passed `45/45`, including vendor ownership, current-status fencing, scope isolation, read-only Super Admin return access, and currency persistence certification.
- Focused vendor/Super Admin frontend authority tests passed `22/22`; the production Vite build passed with the existing oversized-chunk warning.
- Post-restart `/health/live` and `/health/marketplace` checks were healthy. Runtime route and OpenAPI checks were read-only; no authenticated return lifecycle mutation was issued.
- The repository-wide backend and frontend suites were not rerun as part of this closure and are not represented as clean.

## Residual Risks

- The refund evidence is a Development simulation, not proof of an external customer transfer. `ConfirmMoneySent=true` was never used and `ExternalMoneyMovementConfirmed=false` remains authoritative.
- Human UI observation covers only the Super Admin desktop refund detail, Development simulation evidence, and idempotent replay for `ORD-CH-0009-A`; customer screens, either vendor's screens, Super Admin order/finance/audit screens, and all mobile-width observations remain outstanding.
- Global integrity still reports six unrelated historical order-commission issues; the M-1D refund-specific counters and sampled entities are clean, but this is not promoted to a globally clean ledger claim.
- Existing broader test-suite noise and predecessor milestone limitations are not silently promoted by this focused certification.
- Diagnostic order `ORD-CH-0003` remains stranded at `Shipped` because its inventory reservation is not active; delivery correctly fails closed with `INVENTORY_DELIVERY_REQUIRES_ACTIVE_RESERVATION`.

## Human Evidence Checklist

| Role | Desktop and mobile evidence required | Expected observation |
| --- | --- | --- |
| Customer | Order detail, refund detail, and return detail for a fresh isolated multi-vendor lineage | OUTSTANDING: `ORD-CH-0004` is no longer vendor-B-only and cannot satisfy this check |
| Vendor A | Vendor order and finance/statement screens for the fresh lineage | OUTSTANDING: prove own-suborder visibility and no foreign refund data |
| Vendor B | Vendor order, vendor-managed return lifecycle/receipt, and finance/statement screens for the fresh lineage | OUTSTANDING: prove refunded own suborder, receipt quantities, earning reversal, debt reduction, and no foreign return visibility |
| Super Admin | Refund detail, read-only return timeline, order detail, finance integrity, and audit/forensic screens | PARTIAL: desktop/mobile refund detail, simulation/no-external-money evidence, and desktop replay observed for `ORD-CH-0009-A`; remaining screens are outstanding |

Each role requires a desktop-width and mobile-width screenshot or equivalent operator observation with timestamp, route/screen name, and PASS/FAIL notes. Credentials, tokens, payout account values, and unrelated customer PII must be redacted.

## Exit Criteria

1. Establish a fresh isolated multi-vendor lineage, or verify and disposition the later `ORD-CH-0004-A` external-money claim, before reusing a canonical order for role-isolation evidence.
2. Perform customer, vendor A, vendor B, and remaining Super Admin desktop/mobile UI observations and retain redacted screenshots or equivalent human evidence.
3. Record operator PASS/FAIL notes against the human evidence checklist without promoting machine evidence to human evidence.
4. Disposition the six unrelated historical global-integrity issues separately; they are not M-1D canonical refund failures.
5. Re-run scoped marketplace health and focused M-1D tests if any code or canonical data changes after human observation.

All machine-verifiable M-1D execution, accounting, event, replay, integrity, and projection criteria are recorded. Until the required human desktop/mobile evidence is supplied, the operational verdict remains **NOT YET COMPLETE**.
