# Global Golden Treasury Reconciliation Architecture - 2026-09-15

## 1. Executive Decision

Proceed with a controlled global reconciliation harness and one evidence run composed of three tenant-isolated tracks. This is an execution architecture, not a PASS decision. No new financial mutation is authorized until a repeatable-read, read-only PostgreSQL baseline and a per-event expectation have been captured.

The design deliberately reuses clean independently certified samples where repeating a transaction would add risk without adding global linkage evidence. It permits a new small Storefront lifecycle only after its source product, inventory, cost, payment method, affiliate attribution, return, and refund authorities are proven available. Gateway/COD settlement, Fawry, CommerceHub order creation under `DebtBlocked`, manual journals, direct SQL business writes, balance patches, and historical repair are excluded.

The certification can return PASS only if every executed mutation reconciles from source through Treasury, journal, ledger, party/inventory effects, outbox, and reports, while every unavailable path is reported as `NOT EXECUTABLE` rather than promoted to PASS.

## 2. Certified Preconditions

The following are accepted as frozen certified boundaries and are regression gates, not redesign work: T-01 cache authority; T-02 clearing parity; T-03 working-capital Cash Flow reconciliation; T-04 settlement containment; T-05 Treasury classification authority; T-06 supplier-card funding; T-07 affiliate reversal GL; T-08 Storefront/CommerceHub COGS; T-09 Fawry fail-closed authority; T-10 settlement fail-closed containment; T-11 canonical mapped Cashbox/Bank cash equivalents; and Treasury UI account-catalog semantics.

The following boundaries remain explicit:

- Fawry is unsupported and must fail closed.
- Positive gateway/COD settlement has no authenticated provider/carrier evidence and is not executable.
- Shopixy CommerceHub participation is `DebtBlocked`; new marketplace order creation must not be bypassed.
- Pre-T-08 cost-unknown rows and historical test-data outbox exceptions are immutable.
- The AhramStore pre-remediation supplier-card funding journal has no accounting-outbox row and must not be backfilled.
- `1020` remains a non-Treasury legacy Asset and is not reclassified.
- Aggregate health degradation caused by unrelated external dependencies is disclosed but is not fabricated into financial failure or success.
- Repository-wide legacy test debt remains separate and must be reported with exact failed gates.

## 3. Target Tenant

No single observed tenant legitimately exposes every required channel. The smallest safe design is one certification run with three independent tenant tracks and simultaneous non-target fingerprints:

| Role | Tenant | Tenant ID | Currency | Reason |
| --- | --- | --- | --- | --- |
| Storefront and Affiliate primary | Shopixy | `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` | EGP | Only audited tenant with enabled Storefront payment methods; has the T-08 live Storefront lifecycle and T-07 Affiliate history. |
| POS, Customer, and Clearing control | greatone | `c43aebf2-4cc4-406a-a2db-4df06d40bce5` | EGP | Has a clean browser-certified POS/customer/supplier/return sample and current card-clearing evidence. |
| Supplier and Supplier Card control | AhramStore | `c9c5c1ae-949e-498e-bc63-07242daeb49d` | EGP | Has the certified funded-company-card instrument, restricted Asset, purchases, supplier payment, and statement lineage. |

All three fingerprints must be captured in the same read-only snapshot before the first business mutation. A mutation in one track must leave the other two fingerprints byte-identical except for explicitly excluded authentication/audit metadata. If no safe new Storefront plus Affiliate lifecycle is available, Shopixy's T-08 and T-07 evidence remains independent prior evidence and the unexecuted live row is `NOT EXECUTABLE`.

## 4. Global Source Matrix

Only previously independently proven scenarios or candidate scenarios approved for this run are populated. A status qualified as deterministic or source-level is not a claim of new live execution.

| Source | Event | Payment Method | Treasury Account | Direction | Clearing? | Settlement? | Journal | Party | Inventory | Cash Flow | Outbox | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| POS | Cash sale | Cash | Mapped Cashbox, normally `1010` | In | No | Immediate | Revenue and COGS | Optional customer | Decrease at immutable cost | Operating inflow | Accounting | PASS, independently browser/DB proven on greatone |
| POS | Card sale | Card | Card Clearing `1110` | In | Yes | Positive settlement not certified | Revenue and COGS | Optional customer | Decrease at immutable cost | Zero until Bank settlement under T-11 | Accounting | PASS for collection; settlement `NOT EXECUTABLE` |
| POS | Credit sale | Account credit | None | None | No | Customer collection later | Dr `1410`, Cr `4010`; COGS | AR increases | Decrease at immutable cost | Non-cash working capital | Accounting | PASS, independently proven on greatone |
| Customer | Receivable payment | Cash plus Card | Cashbox `1010` plus `1110` | In | Card leg only | Card settlement not certified | Dr assets, Cr `1410` | AR decreases | None | Cash leg only until Card settlement | Accounting | PASS, independently proven on greatone |
| POS | Sales return | Original authority/account credit | Original target | Out/credit | If original method | No fabricated settlement | Revenue/tax and COGS reversal | AR/credit as authorized | Resellable stock restored | Cash only for actual Cashbox/Bank line | Accounting | PASS, independently proven on greatone |
| Supplier | Cash purchase | Cash | Cashbox `1010` | Out | No | Immediate | Dr inventory/tax, Cr Cashbox | None | Increase | Operating outflow | Accounting | PASS, independently proven on AhramStore |
| Supplier | Credit purchase | Credit | None | None | No | Supplier payment later | Dr inventory/tax, Cr `2010` | AP increases | Increase | Non-cash working capital | Accounting | PASS, independently proven on AhramStore/greatone |
| Supplier | Card purchase | Funded company card | Dedicated supplier-card Asset | Out | No | Not a provider-clearing settlement | Dr inventory/tax, Cr Card Asset | AP only for credit remainder | Increase | Zero cash at purchase | Accounting | PASS, independently proven on AhramStore |
| Supplier | Supplier payment | Cash or funded company card | Cashbox or dedicated Card Asset | Out | No | Immediate/domain-specific | Dr `2010`, Cr selected asset | AP decreases | None | Cash only when mapped Cashbox/Bank used | Accounting | PASS, independently proven on AhramStore/greatone |
| Supplier | Purchase return | Original purchase authority | Original/AP target | In/credit | No | None | Inventory/tax reversal against supplier/asset authority | AP or supplier credit changes | Decrease/restoration to supplier | Only actual Cashbox/Bank movement | Accounting | PASS, independently proven on greatone |
| Storefront | COD delivered and collected | COD | COD Clearing `1130` | In | Yes | Carrier remittance not executable | Dr `1130`, Cr `4020` | Customer order only | Separate COGS commit | Zero until legitimate Bank settlement | Accounting plus COGS | PASS, independently browser/DB proven on Shopixy |
| Storefront | Fulfillment COGS | Payment-independent | None | None | No | None | Dr `5010`, Cr `1310` | None | Committed quantity decreases | Zero | `EcommerceCogs` | PASS, independently browser/DB proven on Shopixy |
| Storefront | Return and refund | Original payment context | Clearing or Bank only as source proves | Out | Possibly | No new settlement | Revenue refund plus Dr `1310`, Cr `5010` | Order refund | Resellable stock restored | Only qualifying Cashbox/Bank line | Refund plus `EcommerceCogsReversal` | PASS, independently browser/DB proven on Shopixy |
| Affiliate | Commission earned | Non-cash accrual | None | None | No | Payout separate | Dr `6110`, Cr `2105` | Affiliate payable increases | None | Zero | MassTransit EF outbox/consumer | PASS, independently proven |
| Affiliate | Commission reversed | Non-cash reversal | None | None | No | None | Dr `2105`, Cr `6110` | Affiliate payable decreases | None | Zero | MassTransit EF outbox/consumer | PASS for forward path; historical Shopixy mismatch retained |
| Affiliate | Payout | External transfer evidence | Cashbox | Out | No | Requires legitimate rail evidence | Dr `2105`, Cr Cashbox | Payable decreases | None | Operating outflow | Synchronous GL in orchestrator plus event path | `UNKNOWN — REQUIRES EVIDENCE` |
| CommerceHub | Delivery COGS | Payment-independent | None | None | No | None | Dr `5010`, Cr `1310` | Vendor finance separate | Committed quantity decreases | Zero | `CommerceHubCogs` | Implemented/tested; live post-T-08 `NOT EXECUTABLE` under governance |
| CommerceHub | Vendor finance | Platform SQL finance transaction | Bank/control per transaction shape | In/out/non-cash | Domain-specific | External payout evidence separate | `CommerceHubLedgerPosting` | Vendor payable/control changes | Separate lifecycle | Ledger-derived | Accounting | Historical safe; new order `NOT EXECUTABLE` if `DebtBlocked` |
| Clearing | Collection | Card/COD/Stripe/Wallet | `1110`/`1120`/`1130`/`1140` | In | Yes | No | Collection journal | Source-specific | Source-specific | Zero under T-11 | Accounting | PASS for legitimate collection |
| Clearing | Settlement to Bank | Provider/carrier evidence | Bank `1030` | In to Bank/out of Clearing | Yes | Required | Dr Bank, Dr fee, Cr Clearing | None | None | Bank inflow once, fee once | Accounting from durable batch | `NOT EXECUTABLE` without authenticated evidence |
| Supplier Card | Fund restricted Asset | Cashbox/Bank | Dedicated Card Asset and mapped source | Out of cash/in to Asset | No | None | Dr Card Asset, Cr Cashbox/Bank | None | None | Investing cash outflow once | Accounting for new path | PASS; historical orphan remains boundary |
| Treasury | Cashbox to Bank transfer | Internal | Cashbox and Bank | Internal | No | Not settlement | Dr destination, Cr source | None | None | Zero net | Direct certified posting | PASS in deterministic T-11 evidence; new live transfer optional only if needed |
| Fawry | Any collection/refund/settlement admission | Fawry | None | None | No | Unsupported | None | None | None | Zero | No accounting intent | PASS only for fail-closed rejection; positive path `NOT EXECUTABLE` |

## 5. Golden Scenario

The single golden run consists of ordered tracks; track boundaries never share IDs or financial authority.

1. Capture one immutable all-three-tenant baseline, report snapshot, and SHA-256 fingerprint.
2. Reconcile the existing greatone POS/customer sample `INV-20260911-0031`, payment `a9c435b4-f3c0-4a74-93f7-e7a2a9b42593`, sales return `SR-20260911-1010`, purchase `PUR-20260911-1009`, supplier payment `f2c82f91-fcc7-4ae5-bcc2-d6cbd291c6c3`, and purchase return `PR-20260911-1011`. Browser certification renders the current POS, Payments, returns, Treasury, and report workflows; no repeat mutation is required unless a source link is missing.
3. Reconcile the existing AhramStore T-06C purchase, mixed/credit purchases, supplier-card payment, Card funding, immutable reversal/repost, statements, Treasury, and reports. Browser certification renders Purchase Invoice, Payments, Supplier Card, and statement surfaces. Historical funding is not replayed merely to obtain evidence already certified.
4. On Shopixy, attempt one new small Storefront COD order only if a current published in-stock product with immutable nonnegative cost and a legitimate Storefront COD method is available. If a current valid Affiliate promotion link for that exact product is available, enter through the real referral route; otherwise execute a non-affiliate Storefront order and mark new Affiliate browser mutation `NOT EXECUTABLE`.
5. Reconcile order creation before fulfillment. Advance the order through the real owner UI one state at a time, reconcile each state, then record COD collection only through its real action.
6. Reconcile revenue/COD clearing, inventory commit, COGS, Affiliate earned accrual if attributed, and every outbox/journal link before continuing.
7. If the UI and source authority permit a resellable full return and refund for that order, execute return first and refund second, reconciling after each. Expect source, stock, COGS, revenue, clearing, and Affiliate reversal to return to the defined net position without cash movement.
8. Do not execute carrier settlement. Render Clearing Aging and capture the disabled evidence-required workflow.
9. Render Treasury Hub, Cash Flow, P&L, Balance Sheet, Trial Balance, General Ledger, account statements, and Affiliate UI in English/Arabic at desktop/mobile sizes.
10. Capture the final all-three-tenant fingerprint and acceptance matrix.

Any unexpected accounting, party, inventory, outbox, tenant, or report delta stops the run. No corrective mutation is permitted mid-scenario.

## 6. Mutation Plan

Every permitted mutation has a mini-baseline and a frozen expected result before the browser control is activated. Amounts use the selected live order values; `S` is merchandise selling subtotal, `H` shipping, `T` tax, `C` immutable merchandise cost, `K` earned affiliate commission, and `R` the refund authorized by the source.

| Event | Initial state | Action | Treasury delta | GL delta | Party delta | Inventory delta | Cash Flow delta | P&L delta | Balance Sheet delta | Outbox | Idempotency | Reversal |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Storefront order creation | Published/in-stock product, active COD, no source order | Submit once in Storefront | `0` | `0` before collection/COGS | Order only | Reservation only | `0` | `0` | No recognized-money change | Domain order events only | Same checkout key returns same order; changed payload conflicts | Cancel only through lifecycle if needed; no manual cleanup |
| Fulfillment delivery | One paid/collectible COD order, committed quantity `0` | Advance real owner workflow to Delivered | `0` until collection action | COGS Dr `5010`/Cr `1310` = `C` | No financial party delta | Sellable `-qty`, committed `+qty` | `0` | COGS `+C` | Inventory Asset `-C`, retained earnings `-C` | One processed `EcommerceCogs` | Duplicate delivery has one stock/COGS effect | Resellable return restores quantity and reverses COGS |
| COD collection | Delivered COD order not collected | Use real COD collection action once | `1130 + (S+H+T)` | Dr `1130`, Cr revenue/tax/shipping accounts per source = total | Order payment becomes collected | `0` beyond delivery | `0` because Clearing is not cash | Revenue/fees/tax exactly per journal | Clearing Asset increases with offsetting economics/liability | One processed collection message | Replay returns/collapses to original effect; changed identity conflicts | Refund uses original clearing authority before settlement |
| Affiliate earned, conditional | Valid persisted attribution and commission candidate; no earned row | Triggered only by certified delivery lifecycle | `0` | Dr `6110`, Cr `2105` = `K` | Affiliate payable `+K` | `0` | `0` | Affiliate expense `+K` | Liability `+K`, equity `-K` | Durable Affiliate event and one accounting consumer effect | Duplicate event one journal; changed payload fails | Refund/cancellation creates source-bound reversal |
| Resellable return, conditional | Delivered committed quantity, no prior return | Record real return once | `0` before refund | Dr `1310`, Cr `5010` = returned cost | No payment delta | Sellable `+qty`, returned `+qty` | `0` | COGS `-C` | Inventory Asset `+C`, equity `+C` | One processed COGS-reversal message when authorized | Duplicate receipt one quantity/COGS effect | Damaged quantity is not treated as resellable |
| Storefront refund, conditional | Source refund authority and returned quantity proven | Submit one full source-bound refund | `1130 - R` before settlement | Revenue/tax/shipping reversal against `1130` per original authority | Payment/refund lifecycle changes; Affiliate payable `-K` when valid reversal exists | No second stock mutation | `0` before settlement | Revenue and Affiliate expense reverse exactly as journals specify | Clearing and liabilities/equity reverse accordingly | Refund accounting plus Affiliate and COGS lineage as applicable | Same refund ID one effect; changed payload conflicts | No provider reversal or manual compensation is invented |
| Negative settlement probe | No authenticated provider/carrier evidence | Browser-authenticated call to contained endpoint only if needed | `0` | `0` | `0` | `0` | `0` | `0` | `0` | None | Stable repeated HTTP 409 | Not applicable |

The existing greatone and AhramStore records are read/reconciliation evidence, not new mutation authorization. If a new POS, customer, supplier, purchase, or transfer event becomes necessary to close a missing link, execution stops until an additional exact row with fixed amounts and expected deltas is appended to this plan.

## 7. Treasury Authority

Treasury role is defined only by an active, non-deleted, same-tenant `TreasuryAccount` mapping to an active, non-deleted Asset account. Cashbox and Bank are available cash and generic transfer endpoints. Clearing is money in transit and cannot use the generic transfer path. Supplier-card Assets, `1020`, chart parents, reserves, and unmapped Assets remain report-visible but have no Treasury movement authority.

For every mapped account, certification computes `opening + posted/reversed debit-normalized movement = closing`, compares `LedgerAccounts.CurrentBalance` with ledger-line aggregation, and ties the result to the Treasury API/UI. Negative-account handling follows each account's normal-balance and `PreventNegative` rules; no UI sign convention can override ledger arithmetic.

## 8. Clearing

The run may prove collection into `1110` or `1130`; it may not create a settlement. Clearing collection does not enter T-11 cash. A legitimate future settlement would reduce the exact clearing balance and increase mapped Bank `1030` once, with fees once, but this run lacks authenticated provider/carrier remittance evidence.

The harness must separately report balances for `1110`, `1120`, `1130`, and `1140`, source journals, source IDs, ages, and any settlement batch membership. Existing zero settlement-batch/member state and the disabled Clearing Aging action are positive evidence only for containment. `NOT EXECUTABLE` remains the positive settlement result.

## 9. POS

The greatone 2026-09-11 sample is the primary clean linkage set. It already proves one browser-driven credit sale, mixed customer payment, sales return, credit purchase, cash supplier payment, purchase return, payment replay/conflict, inventory movement, customer/supplier subledgers, accounting outbox, balanced journals, and balanced reports.

The global harness must reconstruct each source-to-journal edge by IDs rather than accept aggregate totals. It must also show Cashbox `1010 -6.00`, Card Clearing `1110 +7.00`, Inventory `1310 +10.00`, AR `1410 +9.00`, AP `2010 +10.00`, Revenue `4010 +20.00`, and COGS `5010 +10.00` for that certified sample. Browser coverage verifies the live screens and API reads; it does not claim a repeated sale unless a new mutation is separately planned.

## 10. Customer

Customer authority is the customer account/subledger plus source sale/payment/return rows. The certified greatone customer closes at EGP `9.00` across shadow balance, account ledger, and transaction projection, with chronological balances `-12.00`, `30.00`, and `9.00`; GL control account `1410` must contain the matching controlled delta.

The harness must prevent aggregate AR from masquerading as per-customer reconciliation. It records customer ID, each source document, amount, running balance, accounting outbox, journal, and `1410` line. A Card customer payment enters Clearing rather than cash, and a credit sale changes revenue/AR without changing Treasury or cash.

## 11. Supplier

AhramStore supplies funded-card purchase/payment evidence; greatone supplies the clean credit-purchase, cash-payment, and purchase-return loop. The harness must tie each supplier transaction and running balance to `2010`, inventory/tax, selected Cashbox/Card Asset, outbox, and journal.

The AhramStore dedicated `T06C-CARD-ASSET` is a restricted Asset, not cash. Funding moved EGP `100.00` from Cashbox to the Card Asset once; subsequent Card purchases/payments reduced only the Card Asset. The certified closing balance EGP `35.00`, supplier Credit EGP `5.00`, inventory EGP `80.00`, and Cashbox EGP `890.00` must reconcile without replaying the historical funding or fabricating its absent outbox.

## 12. Storefront

The prior Shopixy T-08 sample is the fallback clean evidence: order `ORD-83B3-2026-01034` froze EGP `350.00` unit cost, committed one unit, posted one Dr `5010`/Cr `1310` COGS journal, restored the unit on a resellable return, and posted one exact opposite COGS reversal. Its order, refund, outbox, journal, inventory reservation, and stock links must remain queryable.

A new live COD loop is authorized only if preflight proves a current product/variant, available stock, immutable cost, active Storefront COD method, and owner return/refund controls. Master cost, stock, payment configuration, and product publication must not be edited for certification. If no such product exists, the new loop is `NOT EXECUTABLE` and the independently certified sample remains prior evidence.

## 13. Affiliate

Earned accounting is Dr `6110`/Cr `2105`; reversal is Dr `2105`/Cr `6110`; neither moves cash. Approval alone has no journal. Payout is outside this run unless legitimate immutable external transfer evidence already exists; none may be created for certification.

The historical Shopixy EGP `120.00` reversal/GL mismatch predates T-07 and remains an immutable historical boundary. It is not active evidence that the forward path is broken and it is not repaired. A new live Affiliate lifecycle is attempted only through a valid current promotion link and real Storefront order. If referral eligibility or UI access is unavailable, browser execution is `NOT EXECUTABLE`; T-07 deterministic forward-path proof remains independently certified but is not mislabeled as a new live PASS.

## 14. CommerceHub

CommerceHub COGS and reversal are implemented, source-bound, tenant-scoped, idempotent, and tested under T-08. Existing historical orders and financial transactions may be reconciled read-only. Shopixy participation was last proven `DebtBlocked`, so no new marketplace order, delivery, payout, settlement, or refund may be manufactured or forced through development endpoints.

The final row must state `implemented; tested; historical safe; live execution pending governance` when `DebtBlocked` remains current. A live settlement or payout also requires independently authenticated external evidence and remains `NOT EXECUTABLE` without it.

## 15. Accounting

PostgreSQL immutable double-entry journals and ledger lines are the sole reporting authority. For every golden source, the harness captures tenant, source ID, outbox ID/type/key/version, journal ID/number/source/reference/date/status, ordered debit/credit lines, and account cache after posting. Total debit must equal total credit after each major domain and at final state.

New asynchronous financial events require source transaction to durable outbox to processor to journal linkage, with zero unexplained pending/failed rows. The certified exceptions remain explicit: historical AhramStore supplier-card funding lacks an outbox; Affiliate earned/reversed use MassTransit EF outbox and consumers; Affiliate paid posts synchronously inside its orchestrator; generic Treasury transfer posts through its certified direct posting service. None authorizes a bypass or a fabricated `AccountingOutboxMessage`.

Representative replay checks use the real application endpoint and original idempotency key only when a prior certificate does not already supply clean evidence. Same identity and payload must return/collapse to one effect. Changed payload must conflict. Counts and hashes for source, journal, ledger, stock, party, Treasury, and outbox are compared before and after.

## 16. Cash Flow

Cash is the T-11 perimeter: active, non-deleted, same-tenant Asset accounts with active, non-deleted Treasury mappings of exact type Cashbox or Bank. Clearing, supplier-card Assets, `1020`, parents, reserves, and unmapped Assets are non-cash.

The harness proves `opening cash + net cash change = closing cash` and ties closing cash to mapped Cashbox/Bank ledger balances. It separately verifies that Storefront/POS COGS, inventory purchases on credit/Card Asset, Affiliate accrual/reversal, customer/supplier credit, and Clearing collection create no cash. Cashbox-to-Bank transfer nets to zero. Supplier-card funding is an Investing cash outflow once; the later Card purchase/payment is not a second cash outflow. A legitimate Clearing-to-Bank settlement would become one Operating Bank inflow, but is not executed in this run.

## 17. Cross-Tenant Safety

Before and after every mutation, capture a target mini-fingerprint and non-target protected fingerprint. At minimum include account cache and ledger aggregates, Treasury mappings, journals, ledger lines, accounting outbox, orders/items/refunds/returns, purchases/payments/returns, customer/supplier transactions, Affiliate ledger/payout evidence, CommerceHub finance/payout/settlement rows, inventory stock/reservations/movements, and payment settlement batches/members.

Foreign source, payment, order, account, and tenant IDs must fail closed where a safe read or already-certified negative test exists. Do not submit a foreign ID to a workflow that could mutate before ownership validation unless a focused deterministic test already proves the ordering. Authentication/session/audit changes are recorded separately and cannot be used to hide a business fingerprint delta.

## 18. Implementation/Certification Contract

The single Muse delegation is limited to deterministic harness support. Muse may add a local certification utility that:

- Opens PostgreSQL with `REPEATABLE READ`, executes `SET TRANSACTION READ ONLY`, and always rolls back.
- Selects only the named audited tenants and produces canonical JSON plus SHA-256 fingerprints.
- Captures all baseline/final entities, account balances, Treasury mappings, documents, accounting/outbox lineage, party/inventory facts, settlement rows, and report-reconciliation inputs required by this architecture.
- Supports whole-tenant snapshots and source-ID-focused snapshots suitable for one-mutation-at-a-time reconciliation.
- Excludes secrets, bearer tokens, cookies, connection strings, and customer personal data from output.
- Is deterministic apart from an explicitly separated capture timestamp.
- Adds focused self-tests for read-only transaction enforcement, canonical ordering/hashing, tenant selection, and secret/PII exclusion where feasible.
- Documents exact commands, output schema, changed files, tests/build, migration status, limitations, and exact diff.

Muse must not change production financial semantics, controllers, services, entities, EF mappings, migrations, model snapshots, frontend business behavior, balances, journals, history, provider evidence, settlement behavior, T-01/T-06/T-07/T-08/T-09/T-10/T-11 logic, or `1020`. Muse must not execute a business mutation, direct SQL write, manual journal, outbox insertion, history repair, fake provider confirmation, fake marketplace payout, or governance bypass. If the required evidence cannot be obtained read-only, Muse must report the limitation rather than broadening scope.

SOL must inspect every delegated file and complete all certification work independently: freeze the delegated diff, rerun focused gates, perform Release/API/Web/Storefront/CommerceHub builds as applicable, run EF pending-model verification and `git diff --check`, execute headed Edge at `1440x1000` and `390x844` in English/LTR and Arabic/RTL, drive any approved mutation only through rendered application controls, capture request/response/screenshots/source IDs, reconcile immediately with the read-only harness, stop on discrepancy, and issue the final 30-section report. Muse is never the certifier.
