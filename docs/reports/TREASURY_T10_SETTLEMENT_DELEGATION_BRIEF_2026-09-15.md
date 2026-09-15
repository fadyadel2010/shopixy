# Treasury T-10 Settlement Execution and Evidence Integrity Delegation Brief - 2026-09-15

## 1. Executive Decision

**Discovery verdict: C, unsafe and requiring bounded remediation.** The payment settlement persistence, membership constraints, accounting outbox, SQL-recomposed posting, journal shapes, and post-settlement refund classification are materially implemented. They are not certifiable as settlement execution authority because the only production ingress is an Owner/Admin API whose caller supplies the provider/carrier, settlement identity, amount, fees, membership selector, and bank reference. No Stripe payout event, Paymob payout event, carrier remittance event, bank statement match, or other independently authenticated settlement evidence is consumed.

T-10 must fail closed rather than hash operator assertions and call the result provider evidence. The approved implementation is containment-only: both payment settlement POST endpoints must return a stable non-mutating evidence-required conflict before calling `PaymentLifecycleService`. Existing SQL settlement/outbox replay readers remain intact for any historical rows. Positive provider settlement, multi-state batch workflow, provider reversal, and statement reconciliation require a broader provider integration and are STOP conditions.

Discovery was read-only for financial data. PostgreSQL used `REPEATABLE READ`, `SET TRANSACTION READ ONLY`, and rollback. Authenticated API business calls were GET-only; issuing the Development test token may update test-session identity but does not authorize or mutate a financial fact.

## 2. Settlement Authority Inventory

| Provider/channel | Collection account | Clearing account | Current settlement authority | Evidence source and identity | Destination | Fees / gross / net | Refund / reversal | Outbox / UI / status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Stripe | `1120 Stripe Clearing` | `1120` | `RecordGatewaySettlementAsync`, reachable from operator POST | Operator request; `SettlementId`, optional `PayoutBatchId`, order or intent selectors. Provider payout evidence: **UNKNOWN — REQUIRES EVIDENCE** | Fixed tenant Bank `1030`; `BankReference` is descriptive input | `6010`; gross is exact resolved order total; net=gross-fees | Pre-settlement refund credits clearing; post-settlement refund credits Bank. Provider settlement reversal unsupported | Accounting outbox exists; no execution UI; order jumps Collected to Settled |
| Paymob | `1110 Visa Clearing` | `1110` | Same gateway method | Operator request. Paymob payment webhook replay evidence exists as a domain type, but no payout event or settlement linkage exists | Fixed Bank `1030` | `6010`; same shape | Same refund behavior; settlement reversal unsupported | Same |
| Card / Visa | `1110 Visa Clearing` | `1110` | Aliases normalized to Paymob gateway authority | No independent card-acquirer settlement adapter; **UNKNOWN — REQUIRES EVIDENCE** | Fixed Bank `1030` | `6010`; same shape | Same | Same |
| COD | `1130 COD Clearing` | `1130` | `RecordOfflineSettlementAsync`, reachable from operator POST | Operator-supplied `ShippingCompany`, `SettlementId`, order IDs and amounts. No remittance evidence adapter | Fixed Bank `1030` | `6020 Shipping Fees`; gross exact order total; net=gross-fees | Pre/post settlement refund split exists; carrier reversal unsupported | Accounting outbox exists; no execution UI; order jumps Collected to Settled |
| Bosta, Aramex, Mylerz, DHL, FedEx | COD collection where applicable | `1130` only by COD lifecycle | Shipping adapters govern shipment events, not remittance settlement | Shipment webhook evidence is not settlement evidence; remittance identity **UNKNOWN — REQUIRES EVIDENCE** | **UNKNOWN — REQUIRES EVIDENCE** beyond current fixed Bank behavior | **UNKNOWN — REQUIRES EVIDENCE** | **UNKNOWN — REQUIRES EVIDENCE** | No carrier settlement UI/state |
| Manual carrier | COD collection | `1130` | No external carrier authority | Manual shipment status is not remittance evidence | **UNKNOWN — REQUIRES EVIDENCE** | **UNKNOWN — REQUIRES EVIDENCE** | **UNKNOWN — REQUIRES EVIDENCE** | Unsupported for certified settlement |
| Wallet / ManualWallet | `1140 Wallet Clearing` | `1140` | No settlement-capable lifecycle mapping | **UNKNOWN — REQUIRES EVIDENCE** | **UNKNOWN — REQUIRES EVIDENCE** | Payment method maps `6010`, but settlement accounting is unsupported | Refund before settlement uses clearing; post-settlement cannot be established | Review UI exists; settlement UI absent |
| Fawry | No certified account | None | Explicitly rejected by T-09 | Provider settlement authority unavailable | None | None | Refund/settlement contained | UI explains unavailable; stable authority rejection |
| Bank transfer | Direct tenant Bank `1030` | None | Immediate collection authority, not a clearing settlement | Bank payment evidence belongs to collection workflow | Bank `1030` | No settlement batch | Normal refund authority | No T-10 batch |
| CommerceHub marketplace payout | Platform/vendor finance control accounts | Not `PaymentSettlementBatch` clearing | `SuperAdminPayoutOrchestrationService` and `CommerceHubPayoutService.MarkCompletedAsync` | Payout item allocation, immutable destination snapshot, handoff token, external reference/bank transaction, completion fingerprint | Approved payout method destination | Payout amount; not gateway gross/fee/net | Failed and reconciliation-required states exist; reversal policy remains domain-specific | CommerceHub accounting outbox and SuperAdmin workflow; separate outward payout family |
| CommerceHub vendor settlement control | CommerceHub ledger | Not payment clearing | `CommerceHubVendorFinancialControlService.RecordSettlementPaymentAsync` | Vendor/idempotency/external reference, EGP-only | Direction depends settlement type | Single amount, not provider gross/fee/net | Adjustment paths are separate | Separate vendor-control UI/read model |
| Affiliate payout | `2105 Affiliate Payable` to authorized Cashbox | None | `AffiliateSettlementOrchestrator` | Verified transfer evidence, rail/reference, expected and allocated amount, fingerprint, destination snapshot for payout request | Approved affiliate payout destination; accounting currently credits authorized Cashbox | Allocated payout amount | Paid-then-reversed recovery policy unsupported and fails closed | Serializable transaction and domain outbox; separate outward payout family |

CommerceHub and Affiliate payouts have settlement semantics but are not gateway/COD clearing settlement authorities and must not be merged into `PaymentSettlementBatch` by T-10.

## 3. State Machines

| Family | Actual authoritative states | Journal / Treasury / Cash Flow | Retry and UI |
| --- | --- | --- | --- |
| Gateway/COD payment settlement | `Order.PaymentStatus`: Pending/Authorized/COD -> Collected -> Settled. `PaymentSettlementBatch` has no status; creation means recorded as settled. There are no persisted Eligible, Batched, Submitted, Failed, Rejected, Reconciled, Reversed, Disputed, or Partially Settled batch states | Collection debits clearing. Settlement credits clearing gross, debits Bank net and fee expense. Cash Flow treats both `10xx` and `11xx` as cash equivalents | Rejected calls persist no state. Durable accounting retry is in `AccountingOutboxMessages`; no settlement-state UI exists |
| Payment Runtime domain model | Defines Pending, PartiallySettled, Settled, Failed, ReconciliationRequired, Replayed, Duplicate, Rejected | No proof connects this in-memory runtime model to `PaymentSettlementBatch` execution authority | Not accepted as persistence evidence |
| CommerceHub payout | Pending -> Processing -> Completed; Failed and ReconciliationRequired | Separate outward payout posting and readiness controls | SuperAdmin orchestration; tenant execution contained |
| Affiliate payout request | Pending -> Approved -> Paid or Rejected | Separate payable-to-authorized-asset payout journal | Serializable retry; admin/affiliate surfaces |

The candidate global Pending -> Eligible -> Batched -> Submitted -> Settled -> Reconciled state machine is not implemented for gateway/COD settlement. T-10 must not invent those states.

## 4. Evidence Contract

Current durable gateway/COD fields are limited to tenant, settlement type/id, provider, optional payout batch ID, gross, fees, currency, optional bank reference, order count, system settlement timestamp, creation metadata, and member order/payment-intent snapshots.

| Requested evidence element | Current authority |
| --- | --- |
| Provider/carrier | Request string persisted; gateway allow-list exists; COD carrier is unrestricted |
| SettlementId | Request string; unique only by tenant and settlement type |
| ExternalBatchId | Optional gateway `PayoutBatchId`; never populated by production provider code. COD has none |
| Currency / GrossAmount / Fees / NetAmount | Currency, gross and fees persisted; net derived as gross-fees |
| Eligible payment/order IDs | Live `PaymentSettlementOrders`; optional payment-intent ID |
| SettlementTimestamp | Server `DateTime.UtcNow`, not provider settlement timestamp |
| DestinationAccount | Not persisted; accounting resolves hard-coded tenant Bank account code `1030` |
| EvidenceReference | `BankReference` is unverified request text and is not posting authority |
| EvidenceHash | Absent |
| Provider response/status | Absent |
| Tenant | Persisted and query-enforced |
| MutationVersion | Absent on settlement batch/outbox |
| CreatedBy/System | Base creation time exists; no settlement actor identity is persisted |

No minimum positive evidence contract can be approved without the actual provider/carrier payload and authentication mechanism. Adding generic `EvidenceHash` or `CreatedBy` fields would only make operator assertions immutable, not authoritative.

## 5. Membership Integrity

Proven controls: duplicate/empty request IDs reject; explicit identifiers resolve under tenant predicates; orders are locked in deterministic ID order; currency, gateway, Collected status and exact gross total are checked; collection journals must already be posted; live membership is `PaymentSettlementOrders`; unique `(TenantId, OrderId)` prevents an order joining two active batches; unique `(TenantId, SettlementBatchId, OrderId)` prevents duplicate batch membership.

Contained gaps: request-supplied membership is not compared to an authenticated provider settlement statement. Replay matching compares the order set but not `BankReference` or exact stored payment-intent membership. `PayoutBatchId` is indexed but not unique and has no production population path. Partially refunded, refunded, reversed, or already settled orders fail the required Collected-state check. Partial settlement is not supported.

## 6. Gross/Fee/Net Accounting

Gateway shape for Stripe and Paymob/Card/Visa:

| Account | Debit | Credit |
| --- | ---: | ---: |
| Bank `1030` | `Gross - Fees` | 0 |
| Gateway Fees `6010` | `Fees`, if nonzero | 0 |
| Provider clearing `1120` or `1110` | 0 | `Gross` |

COD shape:

| Account | Debit | Credit |
| --- | ---: | ---: |
| Bank `1030` | `Gross - Fees` | 0 |
| Shipping Fees `6020` | `Fees`, if nonzero | 0 |
| COD Clearing `1130` | 0 | `Gross` |

`ValidateSettlementAmountBounds` enforces gross > 0 and `0 <= fees <= gross`; decimal precision is `(18,2)`. These balanced shapes and `Gross = Fees + Net` are source- and unit-test-proven, but their external amounts are not provider-proven.

## 7. Destination Authority

The API accepts no ledger account ID. Posting resolves the tenant's account code `1030` through `IAccountResolver`, so arbitrary account, `IsSystem`, Cashbox, or cross-tenant destination selection is not available. `BankReference` does not choose a destination. Generic Treasury transfer remains Cashbox/Bank-only and cannot use Clearing.

The fixed Bank mapping is internally safe but not evidence-driven: there is no provider payout destination/account match and no settlement statement. Cashbox and other Treasury destinations are unsupported. **UNKNOWN — REQUIRES EVIDENCE** is the only valid answer for provider-specific destination confirmation.

## 8. Clearing Aging

`FinancialReportService.GetClearingAgingAsync` is a read-only ledger report. It groups all posted/reversed journal lines by `Account.IsClearing`, nets each account and assigns time buckets by journal dates. It does not select eligible orders, match settlement lots, read provider evidence, create batches, call settlement APIs, or post journals.

The frontend action remains disabled and says provider/carrier evidence is required. Clearing Aging is neither candidate selector nor settlement authority. Its “oldest” and bucket values are account-flow aging rather than matched open-lot aging.

## 9. Accounting Outbox

Current lineage is request -> batch plus members and order status mutation -> `AccountingOutboxMessage`, all staged on one DbContext save and lifecycle transaction -> post-commit fast path or background `AccountingOutboxProcessor` -> reload current non-deleted batch and live member count -> `CommerceAccountingService` -> `FinancialPostingService` -> journal/lines -> outbox `JournalId` and processed timestamp.

Batch/order/outbox commit is atomic. Journal processing is recoverable because settlement can commit while accounting remains pending; the outbox retries. Immediate posting wraps current batch lock, current outbox row, journal posting and processed marking in a transaction. Payload amounts are not replay authority. A journal cannot legitimately precede settlement SQL authority through this path.

Lineage limitations: the journal uses a generated `SourceId`, with settlement linkage carried by source reference/idempotency and outbox `JournalId`; `PaymentSettlementBatch` has no `JournalId` or posting status. Existing replay support must remain available even while new ingress is contained.

## 10. Idempotency

Current identity is tenant + settlement family + `SettlementId`. Journal/outbox keys are `GATEWAY_SETTLEMENT:{TenantId}:{SettlementId}` and `COD_SETTLEMENT:{TenantId}:{SettlementId}`. Same ID and matching persisted provider, payout batch, gross, fees, currency and order set returns idempotent success. Changed values reject. Journal posting also refuses an existing key with a different financial shape.

Gaps: replay equality omits bank reference and exact payment-intent lineage; external payout identity is not authenticated; settlement IDs can be reused across tenants because uniqueness is tenant-scoped. Whether a provider settlement ID is globally unique or account-scoped is **UNKNOWN — REQUIRES EVIDENCE**. Containment makes all new positive replay attempts non-mutating.

## 11. Concurrency

Production defaults to Serializable isolation and requires PostgreSQL/SQL Server row locking. Settlement orders are locked in deterministic order before state validation. For overlapping order sets, one transaction can move the order to Settled and the other then fails the Collected-state requirement; unique order membership is database defense in depth. Same-identity races also meet unique batch/outbox constraints.

No focused relational gateway/COD concurrent-settlement test was found. Exceptions from a late unique violation may be normalized as a generic internal failure rather than a stable conflict. This is an unproven execution property, but new execution will be disabled by T-10 containment.

## 12. Refund Interaction

Before settlement, a collected payment refund posts Dr e-commerce sales and Cr the original clearing account; a full refund becomes Refunded and is ineligible for settlement. Partial refunds become PartiallyRefunded and are also ineligible because settlement requires Collected, so settling only the remaining portion is unsupported.

After settlement, live batch and member lineage classify the refund as post-settlement and post Dr e-commerce sales / Cr Bank `1030`. The refund row snapshots batch/type/id/payout/timestamp and outbox posting reloads current authority. Missing/deleted/drifted settlement lineage fails closed. Provider-side refund and settlement statement reconciliation remain **UNKNOWN — REQUIRES EVIDENCE**.

## 13. Reversal / Failure

Input validation and pre-commit exceptions roll back batch, members, order status and outbox. Post-commit accounting failure leaves durable outbox recovery. There is no persisted provider submission/failure/rejection state because recording currently means settled.

No gateway/COD settlement reversal command, provider reversal event, invalid-evidence quarantine, rejected batch, bank-posting failure state, or compensating settlement journal workflow exists. Once posted, any correction would require a separately authorized compensating journal; direct mutation is forbidden. This broader lifecycle is a STOP condition, not a T-10 invention.

## 14. Cash Flow

Cash Flow is ledger-derived but defines every `10xx` and `11xx` account as cash equivalent. It therefore includes Bank, Cashbox, Visa/Stripe/COD/Wallet clearing, parent accounts, and unmapped legacy-style assets. Collection into clearing appears as cash-pool inflow. Settlement credits clearing gross, debits Bank net, and debits fee expense, so the combined cash-equivalent movement is only negative fees; gross is not counted twice in that combined perimeter.

This proves current behavior, not a bank-only cash statement. Actual Bank settlement inflow is not separately surfaced as cash increase because clearing was already included. Changing the cash perimeter is outside T-10 and forbidden in this delegation.

## 15. Treasury Reconciliation

With zero payment settlement batches in the audited tenants, the observed reconciliation is a no-settlement baseline:

| Tenant | Active clearing cache | Ledger-derived clearing | Payment batches / members / settlement journals / settlement outbox |
| --- | ---: | ---: | --- |
| AhramStore | EGP 0 | EGP 0 | `0 / 0 / 0 / 0` |
| greatone | EGP 14 | EGP 14 | `0 / 0 / 0 / 0` |
| shopixy | EGP 55 | EGP 55 | `0 / 0 / 0 / 0` |

For shopixy, authenticated GETs returned Bank `1030` EGP `10,410.10`, Visa `1110` EGP `55.00`, and other clearing accounts zero; Clearing Aging returned the same EGP `55.00`. The Visa amount is POS card lineage and has no eligible e-commerce payment membership. A provider-level opening + collections - refunds/reversals - settled gross = closing equation cannot be certified without provider statement and lot membership.

## 16. Real Data Evidence

The 2026-09-15 read-only capture found zero `PaymentSettlementBatches`, `PaymentSettlementOrders`, gateway/COD settlement journals, and gateway/COD settlement outbox rows in AhramStore, greatone and shopixy. It also found zero CommerceHub payouts, CommerceHub vendor settlements, Affiliate payout requests and Affiliate transfer-evidence rows in those tenants. There are zero Stripe, Paymob and Fawry replay records, zero payment intents with `PayoutBatchId`, and zero shipping webhook rows in the audited scope.

No current order in the three tenants has `PaymentStatus.Collected`; shopixy's previously observed COD orders are now refunded, so they are not eligible. Classification: gateway/COD batch/accounting mechanics are implemented but unexercised (B); provider/carrier evidence ingestion and reversal/state lifecycle are missing implementation (A); positive sandbox certification is provider-evidence unavailable (D); no legitimate eligible settlement fixture exists (E). CommerceHub marketplace participation may also be governance blocked (C), but it is a separate payout family.

Historical test data, current data and certified golden data are distinct. T-08 order history is current audited data but not settlement-eligible golden data. No certified golden gateway/COD settlement exists.

## 17. Frontend Scope

No dedicated gateway/COD settlement page, details page, evidence viewer, statement page, or destination selector exists in `PointOfSale.Web`. No web service calls the settlement endpoints. Clearing Aging retains T-04 containment: no transfer navigation, no Cashbox prefill and only a disabled Provider workflow action. Treasury Transfers filters to Cashbox/Bank.

The UI does not fabricate settlement or offer an impossible route. It also cannot display batch evidence or status because the backend has no read model/state machine for them. No frontend production change is approved for T-10.

## 18. Findings / Prioritization

| ID | Severity | Provider / state | Observed | Expected | Evidence | Financial risk / scope | Recommendation |
| --- | --- | --- | --- | --- | --- | --- | --- |
| T10-01 | P0 | Stripe, Paymob/Card/Visa, COD / execution | Owner/Admin can author all claimed settlement facts and immediately move clearing to Bank | Independently authenticated provider/carrier evidence must authorize identity, membership and amounts | `PaymentLifecycleController` POST DTOs and no payout/remittance adapter | Fabricated bank inflow, premature clearing reduction, false Settled status | Disable both POST routes until real evidence integration exists |
| T10-02 | P1 | Gateway/COD / lifecycle | Batch creation means Settled; no submitted, failed, reconciled or reversed state | Persist only states supported by actual provider workflow and compensate posted reversals | Batch entity has no status; lifecycle jumps order Collected -> Settled | Failed/reversed external settlements cannot be represented or reconciled | STOP; broader provider lifecycle design required |
| T10-03 | P2 | Gateway replay | Replay comparison omits bank reference and exact payment-intent membership | Same external identity with changed evidence must conflict | `SettlementBatchMatches` compares order set but not those fields | Changed evidence may collapse as replay | Containment now; include canonical provider evidence fingerprint in future design |
| T10-04 | P2 | COD carrier | `ShippingCompany` accepts arbitrary text unrelated to carrier registry/remittance | Explicit carrier authority and authenticated remittance evidence | Offline request and service validation | False carrier identity can authorize settlement | Contain; do not infer settlement from shipment webhooks |
| T10-05 | P1 | All / destination | Bank account is safely fixed internally but not matched to external payout destination | Provider evidence and tenant Treasury authority must agree | Accounting resolves `1030`; no destination evidence | Deposit can be attributed to wrong real bank | Future provider contract; no arbitrary ledger ID |
| T10-06 | P1 | All / concurrency | Serializable locks and unique membership exist, but no focused relational settlement race proof | One winner, deterministic replay/conflict, no duplicate movement | Source and schema; no focused test | Residual concurrency normalization risk | Containment tests now; relational race tests with future positive adapter |
| T10-07 | P2 | Wallet / unsupported | Wallet clearing exists with no settlement authority | Explicit provider settlement or fail closed | Provider allow-list excludes Wallet | Wallet balance cannot be legitimately settled | Preserve unsupported status |
| T10-08 | P3 | Clearing Aging / reporting | Account-flow aging says “settlements” but is not lot/member evidence | Read-only reporting must not imply authority | Report service and disabled UI action | Operator ambiguity | Existing disabled evidence message is acceptable; no redesign |
| T10-09 | P2 | Cash Flow / reporting | Clearing is included in the `10xx/11xx` cash-equivalent perimeter | Disclose broad cash pool; avoid counting collection plus settlement twice | `IsCashEquivalent` prefix rule | Bank-only liquidity is obscured, but combined flow does not double count gross | Document; do not change under T-10 |
| T10-10 | P2 | Statements / reconciliation | No provider settlement statement, evidence hash or reconciliation result | Immutable external statement linkage | Source and zero live rows | No external-to-ledger proof | STOP until provider evidence is available |

## 19. Implementation Contract

Muse must implement exactly this containment contract.

Allowed production file: `PointOfSale.API/Controllers/PaymentLifecycleController.cs`. Allowed tests: a new focused T-10 controller/architecture test file and minimal updates to directly conflicting settlement endpoint tests. No frontend change is required.

Required behavior:

1. `POST /api/payments/lifecycle/settlements/gateway` returns HTTP 409 with stable code `SETTLEMENT_EVIDENCE_REQUIRED` and a message that authenticated provider payout evidence is unavailable. It must not call `RecordGatewaySettlementAsync`.
2. `POST /api/payments/lifecycle/settlements/cod` returns HTTP 409 with the same stable code and a carrier-remittance-specific message. It must not call `RecordOfflineSettlementAsync`.
3. Rejection occurs before trusting/validating caller-authored provider, carrier, settlement ID, amount, fees, selector, bank reference or destination facts. Authorization attributes and tenant middleware remain intact.
4. Existing generic Treasury settlement remains HTTP 410. Clearing Aging and Treasury Transfers containment remain unchanged.
5. Existing settlement entities, provider mappings, SQL replay authority, accounting outbox handlers, journal shapes, `FinancialPostingService`, refund lineage, Cash Flow and historical rows remain unchanged. This preserves recovery if historical settlement/outbox rows ever exist.
6. Focused tests prove both endpoints return the stable conflict for plausible and empty payloads, neither lifecycle method is called, and the response contains no success/journal. Architecture tests prove no web settlement call/route and T-04 boundaries remain.

Forbidden areas: migrations/model snapshot, settlement entities, `PaymentLifecycleService` positive mechanics, `AccountingOutboxService`, `AccountingOutboxProcessor`, `CommerceAccountingService`, `FinancialPostingService`, Treasury services, Cash Flow/report calculations, CommerceHub payout, Affiliate payout, provider webhook adapters, frontend routes/pages, historical data, SQL business writes, journals, balance patches and outbox fabrication.

Provider mappings remain Stripe -> `1120`; Paymob/Card/Visa -> `1110`; COD -> `1130`; Wallet and Fawry unsupported. Journal shapes remain section 6 but are recovery-only, not newly executable. Destination remains tenant Bank `1030` for existing replay only. Tenant isolation remains endpoint tenant middleware plus existing SQL tenant predicates.

No migration is required. No settlement evidence field may be added. No provider behavior may be invented. STOP if tests require enabling a positive route, if a real provider adapter must be designed, if Cash Flow/Treasury redesign is needed, or if changes beyond the allow-list appear. Muse reports changed files, summary, tests/builds, migration status, limitations, diff check and pending-model result; Muse does not certify T-10.

## 20. Acceptance Matrix

| Gate | Discovery status | Delegated acceptance |
| --- | --- | --- |
| Explicit positive settlement authority | FAIL: no provider/carrier source | Not claimed; route must be contained |
| Evidence contract | FAIL for positive execution | Stable evidence-required rejection |
| Membership / no double settlement | Mechanics present, external set unproven | No new membership can be created through production API |
| Provider mapping / destination | Internally explicit, externally unproven | Preserve unchanged |
| Journal / outbox atomicity | Source-proven recovery path | Preserve unchanged; no journal/outbox on rejection |
| Idempotency / concurrency | Internal controls; positive relational proof incomplete | Every contained call is deterministic and non-mutating |
| Refund / reversal | Refund split exists; settlement reversal absent | Preserve refund code; positive settlement remains disabled |
| Cash Flow / Clearing Aging | Ledger-derived broad cash pool; aging read-only | Preserve; no delta after rejection |
| Tenant isolation | Existing endpoint and SQL scoping | Cross-tenant payload cannot create rows or movement |
| T-04 regression | Current source passes containment | Must remain passing |
| Migration | None required | EF model has no pending change |
| Browser | No settlement UI; API is only route | Real browser verifies disabled Clearing Aging and absent execution route, or authenticated API rejection if browser cannot invoke hidden route |
| Database reconciliation | Current zero settlement rows | Before/after counts, balances, journals and outbox unchanged |
| Historical integrity | No mutation in discovery | No historical rewrite |
| Final classification | Unsafe before containment | Eligible for PASS only as valid fail-closed negative certification; positive settlement remains uncertified |
