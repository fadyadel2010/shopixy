# Treasury Phase 2A Global Source Matrix - 2026-09-14

## 1. Executive Decision

**Verdict: FAIL.** The three audited tenants have balanced journals, zero ledger-cache drift, active Treasury mappings, durable payment-settlement boundaries, and no pending or failed accounting outbox row. The global quality gate nevertheless fails because two live financial domains are not closed end to end:

| Severity | Finding |
| --- | --- |
| High | Affiliate commission reversal changes the Affiliate subledger and projections but has no registered GL reversal consumer. Shopixy has EGP 120 reversed in the commission ledger while `2105 Affiliate Payable` still carries EGP 120. |
| High | Storefront and CommerceHub order accounting recognizes sales/refunds but does not post merchandise COGS/inventory relief. CommerceHub inventory processing has live sale stock movements, so operational inventory and the GL inventory asset are not one certified flow. |
| High | Fawry is accepted as a payment provider elsewhere, but e-commerce account resolution falls back to Stripe clearing and durable gateway settlement rejects Fawry. No live Fawry accounting row exists in the three audited tenants. |
| Medium | No live `PaymentSettlementBatch`, CommerceHub payout, vendor settlement, or Affiliate payout exists in the audited tenants, so successful production settlement execution is not evidenced. |
| Medium | AhramStore's live supplier-card funding has a posted journal but no corresponding accounting outbox row. Current code provides a posting path; the observed historical row is a lineage exception that remains unrepaired. |
| Medium | Cash Flow identifies cash equivalents by account-code prefix `10` or `11`, not by active Treasury authority. This includes clearing, parent, and unmapped legacy-style accounts in the cash perimeter. |

No financial mutation, settlement, payout, transfer, migration, journal rewrite, cached-balance patch, or direct SQL business-data write was performed.

## 2. Scope And Questions

This Phase 2A audit maps financial sources globally rather than treating Treasury screens as the system boundary. It answers:

1. Which source event authorizes each monetary fact?
2. Which payment method and Treasury account receive or release value?
3. Is the event cash, clearing, or non-cash?
4. Which immutable journal and GL accounts record it?
5. Does it affect a customer, supplier, affiliate, vendor, or inventory subledger?
6. Does it enter Cash Flow, use a durable outbox, require settlement, and have a UI surface?
7. Does current SQL evidence prove the path for AhramStore, greatone, or Shopixy?

The inspected domains were POS sales and returns, Storefront orders and payment lifecycle, gateway/COD settlement, suppliers and purchases, supplier-card funding, initial capitalization, parties, inventory, Affiliate commissions and payouts, CommerceHub finance and payouts, Cash Flow, clearing aging, outbox state, and admin UI routes.

## 3. Method And Evidence Controls

| Control | Result |
| --- | --- |
| Database | `PosEcommerceDb_Baseline`. |
| SQL transaction | PostgreSQL `REPEATABLE READ` with `SET TRANSACTION READ ONLY`. |
| SQL capture | 2026-09-14T13:11:38.1029590Z. |
| Tenant boundary | Every business, journal, ledger, outbox, payment, and mapping query was tenant-scoped. |
| Journal population | Posted journals and reversed originals were included; reversal journals remain separate immutable entries. |
| Treasury authority | Active, non-deleted, same-tenant `TreasuryAccounts` mapping. `LedgerAccounts.IsSystem` is not Treasury authority. |
| Source review | Current application source and current EF schema were primary. Existing reports were supporting indexes only. |
| Runtime behavior | No browser or mutating API action was run in this phase. Runtime behavior not established by SQL/source is marked `UNKNOWN — REQUIRES EVIDENCE`. |
| Mutation control | Read-only inspection only. |

Schema names used by the evidence queries were `LedgerAccounts.AccountCode`, `PaymentMethods.FeesAccountId`, `LedgerJournalEntries.ReversesJournalId`, `AccountingOutboxMessages.LastError`/`IsFailed`, `TreasuryAccounts.Type`, and `TreasuryInitialCapitalizations.CutoverDate`/`PostedJournalId`.

## 4. Global Financial Source Matrix

| Source | Event | Method | Amount | Treasury Account | Direction | Cash/Clearing/Non-Cash | Status | Journal | GL Effect | Party Effect | Inventory Effect | Cash Flow | Outbox | Settlement Authority | UI |
| --- | --- | --- | ---: | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| POS | Sale completed | Cash | Invoice total less change | Active Cashbox, normally `1010` | In | Cash | Implemented; live evidence exists | `POS_SALE` revenue plus COGS journals | Dr Cashbox / Cr `4010`; Dr `5010` / Cr `1310` | Customer/receivable only when applicable | Relieves inventory at cost | Operating | Accounting outbox | Immediate collection; no clearing settlement | POS sales |
| POS | Sale completed | Card | Tender amount | Card clearing `1110` | In | Clearing | Implemented; source certified; tenant-specific live amount `UNKNOWN — REQUIRES EVIDENCE` | `POS_SALE` revenue plus COGS journals | Dr `1110` / Cr `4010`; Dr `5010` / Cr `1310` | None unless receivable leg | Relieves inventory at cost | Operating under current prefix rule | Accounting outbox | External card settlement workflow `UNKNOWN — REQUIRES EVIDENCE` | POS sales/payments |
| POS | Sale completed | Bank transfer | Tender amount | Active Bank, normally `1030` | In | Cash | Implemented; tenant-specific live amount `UNKNOWN — REQUIRES EVIDENCE` | `POS_SALE` revenue plus COGS journals | Dr `1030` / Cr `4010`; Dr `5010` / Cr `1310` | None unless receivable leg | Relieves inventory at cost | Operating | Accounting outbox | Immediate bank evidence | POS sales/payments |
| POS | Sale on account | Account credit | Outstanding amount | None | In | Non-cash | Implemented; tenant-specific live amount `UNKNOWN — REQUIRES EVIDENCE` | POS revenue/COGS journals | Dr `1410` / Cr `4010`; Dr `5010` / Cr `1310` | Increases customer receivable | Relieves inventory at cost | Operating working capital | Accounting outbox | Customer collection authority | POS sales/customer account |
| POS | Return/refund | Original tender allocation | Refund allocation | Original cash/clearing/receivable authority | Out | Cash, clearing, or non-cash | Implemented; tenant-specific live amount `UNKNOWN — REQUIRES EVIDENCE` | `POS_REFUND` plus COGS reversal | Dr revenue / Cr refund target; Dr `1310` / Cr `5010` | Reverses receivable where allocated | Restores returned cost | Operating | Accounting outbox | Original payment/return settlement rules | Sales returns |
| Storefront | Online payment captured | Stripe | Order total | Stripe clearing `1120` | In | Clearing | Implemented; live success in audited tenants `UNKNOWN — REQUIRES EVIDENCE` | `Order_Payment` | Dr `1120` / Cr `4020` | None | No e-commerce COGS journal found | Operating under current prefix rule | Accounting outbox | `PaymentLifecycleService.RecordGatewaySettlementAsync` | Orders; provider checkout |
| Storefront | Online payment captured | Paymob/Card | Order total | Visa/Card clearing `1110` | In | Clearing | Implemented; live success in audited tenants `UNKNOWN — REQUIRES EVIDENCE` | `Order_Payment` | Dr `1110` / Cr `4020` | None | No e-commerce COGS journal found | Operating under current prefix rule | Accounting outbox | Gateway settlement lifecycle | Orders; provider checkout |
| Storefront | Payment approved | Manual wallet | Order total | Wallet clearing `1140` | In | Clearing | Implemented; live success in audited tenants `UNKNOWN — REQUIRES EVIDENCE` | `Order_Wallet` | Dr `1140` / Cr `4020` | None | No e-commerce COGS journal found | Operating under current prefix rule | Accounting outbox | Wallet settlement authority `UNKNOWN — REQUIRES EVIDENCE` | Payment review; Orders |
| Storefront | COD collected/delivered | COD | Order total | COD clearing `1130` | In | Clearing | Implemented; Shopixy has three posted COD orders totaling EGP 1,230 | `Order_COD` | Dr `1130` / Cr `4020` | None | No e-commerce COGS journal found | Operating under current prefix rule | Accounting outbox | `RecordOfflineSettlementAsync` | Orders `cod-delivered` action |
| Storefront | Gateway capture before order match | Provider event | Captured amount | Provider clearing | In | Clearing | Implemented; live amount `UNKNOWN — REQUIRES EVIDENCE` | `UnresolvedPayment` | Dr provider clearing / Cr `2030` | None | None | Operating under current prefix rule | Accounting outbox | Resolution to order, then normal settlement | Provider webhook/recovery; no direct Treasury action |
| Storefront | Resolve unmatched capture | Provider event | Matched amount | Existing provider clearing | Classification only | Non-cash between liability/revenue | Implemented; live amount `UNKNOWN — REQUIRES EVIDENCE` | `Order_UnresolvedPaymentClearance` | Dr `2030` / Cr `4020` | None | No e-commerce COGS journal found | Operating | Accounting outbox | Payment lifecycle resolution | Operational recovery surface `UNKNOWN — REQUIRES EVIDENCE` |
| Storefront | E-commerce refund | Original payment context | Refund amount | Pre-settlement clearing or post-settlement Bank | Out | Clearing or cash | Implemented; live audited amount `UNKNOWN — REQUIRES EVIDENCE` | `Order_Refund` | Dr `4020` / Cr clearing or `1030` | None | No e-commerce COGS reversal journal found | Operating | Accounting outbox | Payment lifecycle refund authority | Orders `refund` action |
| Payment lifecycle | Gateway settlement | Stripe/Paymob/Card/Visa | Gross, fee, net | Bank `1030` and provider clearing | Clearing to cash | Clearing and cash | Implemented; zero live batches in all three tenants | `GatewaySettlement` | Dr `1030` net + Dr `6010` fee / Cr clearing gross | None | None | Current prefix classification makes net cash-pool movement fee-only | Accounting outbox after durable batch | `RecordGatewaySettlementAsync` and `PaymentSettlementBatch` | No dedicated execution UI found |
| Payment lifecycle | COD settlement | COD remittance | Gross, fee, net | Bank `1030` and COD clearing `1130` | Clearing to cash | Clearing and cash | Implemented; zero live batches in all three tenants | `CODSettlement` | Dr `1030` net + Dr `6020` fee / Cr `1130` gross | None | None | Current prefix classification makes net cash-pool movement fee-only | Accounting outbox after durable batch | `RecordOfflineSettlementAsync` and `PaymentSettlementBatch` | No dedicated execution UI found |
| Treasury | Transfer | Cashbox/Bank only | Transfer amount | Two active Cashbox/Bank mappings | In and out | Cash | Implemented; live audited amount `UNKNOWN — REQUIRES EVIDENCE` | Treasury transfer journal | Dr destination / Cr source | None | None | Zero net cash change | Direct posting | Explicitly not clearing-settlement authority | Treasury Transfers |
| Treasury | Direct cash-in | Manual | Any | Cashbox/Bank | In | Cash | Disabled | None | None | None | None | None | None | Certified collection/accounting source required | Endpoint returns containment error |
| Supplier | Purchase | Cash | Invoice total | Active Cashbox | Out | Cash | Implemented; live AhramStore evidence | Purchase journal | Dr `1310` and tax / Cr `1010` | None | Increases GL and operational inventory | Operating | Accounting outbox | Immediate | Purchase Invoice |
| Supplier | Purchase | Credit | Supplier account | Invoice total | None | Liability increase | Non-cash | Implemented; live AhramStore evidence | Purchase journal | Dr `1310` and tax / Cr `2010` | Increases supplier payable | Increases GL and operational inventory | Operating working capital | Accounting outbox | Supplier payment authority | Purchase Invoice |
| Supplier | Purchase | Supplier card | Invoice/payment amount | Active supplier-card asset instrument | Out | Cash-equivalent asset outside Treasury mapping | Implemented; live AhramStore evidence | Purchase/payment journal | Dr `1310` or `2010` / Cr supplier-card asset | Creates or clears supplier payable | Increases inventory on purchase | Investing under current classifier if instrument code is outside `10`/`11` | Accounting outbox for purchase/payment | Supplier-card authority, not Treasury transfer | Purchase Invoice; supplier-card selector |
| Supplier card | Fund instrument | Cashbox | Funding amount | Cashbox and supplier-card asset | Cashbox out, card asset in | Cash to non-Treasury asset | Implemented; AhramStore EGP 100 live journal | Funding journal | Dr supplier-card asset / Cr `1010` | None | None | Investing under current classifier | Historical live source has no matching outbox row | Supplier-card funding authority | Supplier Card Funding Instruments |
| Treasury | Initial capitalization | Allocated opening funds | Capital amount | Allocated Cashbox/Bank/card asset | In | Financing cash/non-Treasury asset | Implemented; AhramStore EGP 1,000 live | Initial capitalization journal | Dr allocated asset / Cr `3010` | None | None | Financing | Accounting outbox is processed for observed capitalization | One-time capitalization authority | Treasury Initial Capitalization |
| Party | Customer payment | Cashbox/Bank | Receipt amount | Active Cashbox/Bank | In | Cash | Implemented; live tenant-specific amount `UNKNOWN — REQUIRES EVIDENCE` | Customer payment journal | Dr cash/bank / Cr `1410` | Reduces customer receivable | None | Operating | Accounting outbox | Customer collection authority | Customer payment surfaces |
| Party | Supplier payment | Cashbox/Bank/card asset | Payment amount | Selected authorized asset | Out | Cash or supplier-card asset | Implemented; live AhramStore card payment evidence | Supplier payment journal | Dr `2010` / Cr selected asset | Reduces supplier payable | None | Operating | Accounting outbox | Supplier payment/card authority | Supplier payment surfaces |
| Party | Opening balance/adjustment | Account entry | Adjustment amount | None unless separately collected | Non-cash | Non-cash | Implemented; live tenant-specific amount `UNKNOWN — REQUIRES EVIDENCE` | Opening/adjustment journal | `1410`/`2010` against adjustment/equity account | Changes customer/supplier subledger | None | Working capital or financing | Accounting outbox | Party-ledger authority | Customer/Supplier account surfaces |
| Affiliate | Commission earned | Attributed delivered order | Commission | None | Liability increase | Non-cash | Implemented; Shopixy EGP 120 earned and approved | `AffiliateCommissionEarned` | Dr `6110` / Cr `2105` | Increases affiliate payable | None | Operating | MassTransit event pipeline, direct GL consumer | Delivery/approval precedes payout | Affiliate admin/portal |
| Affiliate | Commission reversed | Refund/cancellation | Reversed commission | None | Liability should decrease | Non-cash | Subledger implemented; GL incomplete; Shopixy EGP 120 reversed | Missing GL reversal | Required Dr `2105` / Cr `6110` is not posted | Decreases commission subledger | None | GL effect absent | Event projections only; no registered GL consumer | Refund/cancellation authority | Affiliate admin/portal |
| Affiliate | Commission paid | External transfer evidence | Allocated payout | Default Cashbox | Out | Cash | Implemented; zero live payouts/evidence/allocations in all three tenants | `AffiliateCommissionPaid` posted inside orchestrator | Dr `2105` / Cr Cashbox | Clears allocated affiliate payable | None | Operating | Transactional event/projection path; GL is synchronous in orchestrator | `AffiliateSettlementOrchestrator` | Affiliate payout administration |
| CommerceHub | Platform collection | Marketplace finance transaction | Transaction amount | Bank `1030` | In | Cash | Implemented; live greatone and Shopixy evidence | `CommerceHubLedgerPosting` | Dr `1030` / Cr `2030` | Platform suspense/control effect | Inventory handled outside this journal | Financing/operating classification depends counterpart | Accounting outbox | CommerceHub finance transaction authority | CommerceHub Finance/Payments |
| CommerceHub | Vendor payable/commission | Marketplace finance transaction | Allocated amount | None | Liability/revenue classification | Non-cash | Implemented; live greatone and Shopixy evidence | `CommerceHubLedgerPosting` | `2030`, `2040`, and `4020` according to transaction shape | Changes vendor payable | No merchandise COGS journal found | Operating | Accounting outbox | CommerceHub financial ledger | CommerceHub Finance |
| CommerceHub | Refund | Marketplace refund transaction | Refund amount | Bank/control account by transaction shape | Out | Cash/non-cash | Implemented; one live refund in greatone and one in Shopixy | `CommerceHubLedgerPosting` | Observed platform refund includes Dr `2030` / Cr `1030` | Reverses vendor/platform financial buckets | Inventory service processes return movements separately | Operating | Accounting outbox | CommerceHub refund authority | CommerceHub Finance/Payments |
| CommerceHub | Vendor payout | Payout batch | Allocated payout | Payout cash authority | Out | Cash | Implemented; zero live payouts/vendor settlements in all three tenants | `CommerceHubLedgerPosting` payout shape | Clears `2040` and cash/control account per authoritative transaction | Reduces vendor payable | None | Operating | Accounting outbox from finance transaction | `SuperAdminPayoutOrchestrationService` then `MarkCompletedAsync` | Tenant execution contained; SuperAdmin orchestration |
| Inventory | Manual adjustment | Admin adjustment | Cost value | None | Asset/offset movement expected | Non-cash | Operational adjustment exists; accounting intentionally rejected | None | GL adjustment requires authoritative SQL accounting workflow | None | Changes operational stock | No GL effect | No accounting outbox consumer | Inventory authority only | Admin Inventory |

The matrix separates source authorization from presentation. A Cashbox or Bank account can participate in an authorized journal, but its existence does not authorize settlement. Clearing movement requires the payment lifecycle, supplier-card movement requires the instrument authority, Affiliate payout requires its orchestrator, and CommerceHub payout requires SuperAdmin orchestration.

## 5. Source-Of-Truth Hierarchy

The certified hierarchy is:

1. Tenant-scoped SQL business records authorize the financial event and amount.
2. A durable domain or accounting outbox carries asynchronous intent where the workflow is asynchronous.
3. `FinancialPostingService` validates and creates balanced, idempotent, immutable journal entries and ledger lines.
4. `LedgerJournalEntries` and `LedgerLines` are accounting and reporting authority.
5. `LedgerAccounts.CurrentBalance` is a denormalized cache that must reconcile to ledger lines.
6. Active `TreasuryAccounts` mappings define operational Cashbox, Bank, or Clearing authority.
7. Settlement batch, payout evidence, allocation, supplier-card instrument, and capitalization records provide narrower authorities where applicable.
8. Reports, API DTOs, projections, and UI state are derived and cannot authorize money movement.

The ledger accepts `Posted` and `Reversed` journal headers in reporting because a reversed original remains immutable and is offset by a separate reversal journal. Generic transfer and account flags must not substitute for source-specific authority.

## 6. Tenant And Live Evidence Baseline

| Tenant | Tenant ID | Active accounts | Active Treasury mappings | Clearing accounts | Key live scope |
| --- | --- | ---: | ---: | ---: | --- |
| AhramStore | `c9c5c1ae-949e-498e-bc63-07242daeb49d` | 33 | 6 | 4 | Initial capitalization, supplier-card funding, purchases, supplier payment, and reversal. |
| greatone | `c43aebf2-4cc4-406a-a2db-4df06d40bce5` | 32 | 6 | CommerceHub orders, payments, refund, and GL postings. |
| Shopixy | `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` | 32 | 6 | Storefront COD orders, CommerceHub activity, and Affiliate commission history. |

All 97 active accounts reconcile `CurrentBalance` to their ledger-derived balance with zero drift. This closes the Phase 1 cache-drift defect for the observed snapshot, but does not by itself certify every source workflow.

All amounts in the observed scope are EGP. Claims about another currency are `UNKNOWN — REQUIRES EVIDENCE`.

## 7. Ledger And Treasury Authority

Each tenant has six active Treasury mappings: Cashbox `1010`, Bank `1030`, and four clearing accounts `1110` Visa/Card, `1120` Stripe, `1130` COD, and `1140` Wallet. The mappings are same-tenant and point to active asset accounts.

Key authority distinctions:

| Object | Authority |
| --- | --- |
| `LedgerAccounts` | Accounting classification, normal balance, flags, and balance cache. |
| `TreasuryAccounts` | Physical/presentational Cashbox, Bank, or Clearing authority. |
| `PaymentMethods` | Collection target/clearing and optional fee-account mapping. |
| `PaymentSettlementBatches` and members | Which collected orders a specific external settlement may clear. |
| `SupplierCardFundingInstruments` | Authorized supplier-card asset and funding source relationship. |
| `TreasuryInitialCapitalizations` and allocations | One-time opening capital source and asset allocation. |

`1020 POS Visa Account` remains an unmapped legacy-style asset. It is not Treasury authority even though the Cash Flow prefix rule currently includes it. Parent account codes `1000` and `1100` are also classifications, not movement endpoints.

## 8. Payment Method Mapping

The baseline accounting mappings are consistent across AhramStore, greatone, and Shopixy:

| Method code | Ledger target | Classification | Settlement expectation |
| --- | --- | --- | --- |
| `CASH` | `1010` | Cash | Immediate. |
| `CARD` | `1110` | Clearing | Gateway/card settlement. |
| `STRIPE` | `1120` | Clearing | Gateway settlement. |
| `COD` | `1130` | Clearing | COD remittance settlement. |
| `WALLET` | `1140` | Clearing | Wallet settlement authority `UNKNOWN — REQUIRES EVIDENCE`. |
| `BANK_TRANSFER` | `1030` | Cash | Bank evidence; no clearing move. |

Only Shopixy had enabled Storefront `TenantPaymentMethods` in the observed three-tenant scope: `COD`, `STRIPE`, `PAYMOB`, and `MANUALWALLET`; all observed provider configurations were sandbox. Configuration proves availability, not a successful collection or settlement.

Fawry is parsed as a `PaymentProvider`, but `ResolveEcommercePaymentAccountIdAsync` recognizes Stripe, Paymob/Card, COD, and Wallet and defaults all other values to Stripe (`CommerceAccountingService.cs:1497-1511`). Gateway settlement explicitly supports only Stripe, Paymob, Card, or Visa (`PaymentLifecycleService.cs:1897-1905`). Therefore a certified Fawry clearing/settlement mapping is absent.

## 9. POS Sales And Returns

POS has the most complete accounting path. A posted sale creates revenue/cash-or-receivable lines and, when cost is nonzero, a separate COGS/inventory journal (`CommerceAccountingService.cs:47-211`). Returns reverse the tender/revenue allocation and restore inventory cost (`CommerceAccountingService.cs:754-913`). Posting is asynchronous through `AccountingOutboxProcessor` and idempotency is enforced at journal level.

Controls confirmed in source:

| Control | Result |
| --- | --- |
| Multi-tender allocation | Each tender resolves through tenant payment/Treasury authority. |
| Change | Belongs to the cash leg rather than inflating card settlement. |
| Credit sale | Uses Accounts Receivable rather than pretending collection occurred. |
| COGS | `5010` debit and `1310` credit on sale; opposite on eligible return. |
| Reversal | Immutable compensating journal, not line mutation. |
| Tenant-specific live end-to-end POS sample in this Phase 2A snapshot | `UNKNOWN — REQUIRES EVIDENCE`. |

## 10. Storefront Orders And Collections

Storefront accounting is collection-driven:

| Event | Journal effect |
| --- | --- |
| Successful online capture | Dr provider clearing / Cr `4020 E-commerce Sales`. |
| COD delivered/collected | Dr `1130` / Cr `4020`. |
| Manual wallet approved | Dr `1140` / Cr `4020`. |
| Unmatched provider capture | Dr provider clearing / Cr `2030 Treasury Suspense`. |
| Later order resolution | Dr `2030` / Cr `4020`. |
| Refund before settlement | Dr `4020` / Cr original clearing. |
| Refund after settlement | Dr `4020` / Cr Bank `1030`. |

Shopixy live order evidence contains five pending/unposted orders totaling EGP 2,050 and three status-5 posted COD orders totaling EGP 1,230. The three posted rows evidence the COD revenue/clearing path; they do not evidence an external COD remittance batch.

No e-commerce collection method posts COGS/inventory relief in `PostOnlinePaymentCapturedAsync`, `PostCodDeliveredAsync`, `PostWalletApprovedAsync`, or `PostEcommerceRefundAsync` (`CommerceAccountingService.cs:1076-1475`). The financial posting marks revenue/refund complete while inventory lifecycle is separate. That split is not globally certified.

## 11. Payment Settlement Authority

Durable settlement belongs to `PaymentLifecycleService`, not Treasury transfer:

| Settlement | Required authority | GL effect |
| --- | --- | --- |
| Gateway | Provider, settlement ID, currency, gross/fees, exactly one order selector, matching collected orders, and existing collection journals | Dr Bank net + Dr `6010` fees / Cr provider clearing gross. |
| COD | Settlement ID, currency, gross/fees, selected collected COD orders, and existing collection journals | Dr Bank net + Dr `6020` fees / Cr `1130` gross. |

The service persists `PaymentSettlementBatch` and order membership before enqueuing accounting (`PaymentLifecycleService.cs:317-536`, `:573-819`). It validates tenant, provider, currency, totals, collection status, duplicate contents, and accounting readiness. The generic `POST api/admin/accounting/treasury/settle` route returns HTTP 410 with `SETTLEMENT_AUTHORITY_REQUIRED` (`AdminAccountingController.cs:118-126`).

There are zero settlement batches and zero settlement members for the three audited tenants. Successful external settlement, fee reconciliation, duplicate replay, and post-settlement refund behavior in live data are `UNKNOWN — REQUIRES EVIDENCE`.

## 12. Suppliers, Purchases, And Supplier Cards

AhramStore supplies the live evidence for this domain:

| Flow | Evidence | Accounting |
| --- | --- | --- |
| Cash purchase | Posted | Dr Inventory/input tax / Cr Cashbox. |
| Credit purchase | Posted | Dr Inventory/input tax / Cr Supplier Payable. |
| Supplier-card purchase | Posted | Dr Inventory/input tax / Cr authorized card asset. |
| Supplier payment by card | Posted | Dr Supplier Payable / Cr authorized card asset. |
| Purchase/payment reversal | Posted immutable opposite entry | Original is retained and reversal offsets it. |
| Supplier-card funding | EGP 100 posted | Dr supplier-card asset / Cr Cashbox. |

Supplier-card assets are not Treasury accounts and must not be exposed as generic transfer endpoints. `SupplierCardAuthority` validates the same-tenant instrument and asset account. The UI and API provide a dedicated funding route.

The observed AhramStore funding journal has no corresponding `AccountingOutboxMessages` row. This is a historical lineage exception, not permission to synthesize an outbox row during an audit.

## 13. Initial Capitalization

AhramStore has one posted EGP 1,000 initial capitalization:

| Attribute | Evidence |
| --- | --- |
| Debit | Allocated opening asset, including Cashbox authority. |
| Credit | `3010 Opening Balance Equity`. |
| Journal | Posted and linked by `PostedJournalId`. |
| Allocation | One persisted allocation observed. |
| Outbox | Matching observed message is processed. |
| Cash Flow | Financing activity when the debit account is classified as cash equivalent. |

`TreasuryInitialCapitalizationService` owns the one-time record, allocation, cutover, journal, and idempotency boundary. It is not a reusable cash-in feature. Direct Treasury cash-in is disabled and must not bypass this or another certified source.

## 14. CommerceHub Finance And Payouts

Live scope:

| Tenant | Marketplace orders | Payments | Refunds | Payouts | Vendor settlements | GL financial transactions |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| AhramStore | 0 | 0 | 0 | 0 | 0 | 0 |
| greatone | 3 | 8 | 1 | 0 | 0 | 7 totaling EGP 10,780 |
| Shopixy | 10 | 3 | 1 | 0 | 0 | 14 totaling EGP 12,570.12 |

CommerceHub accounting attribution is derived through `AccountingOutboxMessages.BusinessEntityId` plus `TenantId`; `CommerceHubFinancialTransactions` has no `AccountingTenantId` column. Each canonical finance transaction becomes a distinct `CommerceHubLedgerPosting` message and journal.

Observed shapes include:

| Shape | Journal |
| --- | --- |
| Platform collection | Dr `1030` / Cr `2030`. |
| Vendor payable | Dr `2030` / Cr `2040`. |
| Commission/revenue | Dr `2030` / Cr `4020`. |
| Platform refund | Dr `2030` / Cr `1030`. |

Payout execution authority is `SuperAdminPayoutOrchestrationService` followed by `CommerceHubPayoutService.MarkCompletedAsync`; tenant-facing payout execution routes are contained. With no live payout or vendor settlement, external transfer evidence, bank effect, replay behavior, and payable clearance are `UNKNOWN — REQUIRES EVIDENCE`.

## 15. Affiliate Commissions And Payouts

The intended lifecycle is earned, reversed if necessary, approved, then paid:

| Stage | Subledger/projection | GL |
| --- | --- | --- |
| Earned | Commission ledger entry and projection | Dr `6110 Affiliate Marketing Expense` / Cr `2105 Affiliate Payable`. |
| Reversed | Reversal entry and projections | Missing. Required effect is Dr `2105` / Cr `6110`. |
| Approved | Settlement eligibility | No new journal expected. |
| Paid | Payout allocation/evidence and paid projection | Dr `2105` / Cr authorized Cashbox inside orchestrator. |

Shopixy has EGP 120 Earned, EGP 120 Reversed, EGP 120 Approved, and no Paid entries. There are zero payout requests, external transfer-evidence rows, and payout allocations in all three tenants. `2105` retains EGP 120 because only the earned accounting consumer is registered; reversal is projection-only in the current registration (`Program.cs:2104-2121`).

Paid accounting is intentionally owned synchronously by `AffiliateSettlementOrchestrator`, which uses serializable execution, settlement-scoped idempotency, external evidence/allocation, and posts `AFFILIATE_COMM_PAID` (`AffiliateSettlementOrchestrator.cs:1537-1564`). Successful live payout behavior is `UNKNOWN — REQUIRES EVIDENCE`.

The EGP 120 mismatch is pre-existing evidence and was not repaired, reversed, paid, or edited by this audit.

## 16. Customer And Supplier Party Ledgers

Party ledgers and the GL have separate responsibilities but must move together:

| Source | Party effect | GL effect |
| --- | --- | --- |
| Customer opening balance | Establishes receivable | Dr `1410` / Cr opening/adjustment authority. |
| Customer payment | Reduces receivable | Dr authorized cash/bank / Cr `1410`. |
| Supplier opening balance | Establishes payable | Dr opening/adjustment authority / Cr `2010`. |
| Supplier payment | Reduces payable | Dr `2010` / Cr authorized cash/bank/card asset. |
| Party adjustment | Changes scoped party exposure | Balanced entry through designated adjustment accounts. |

The accounting outbox processor has dedicated handlers for customer/supplier payments, opening balances, and party adjustments. Tenant-specific reconciliation between every party balance row and its GL control-account share is `UNKNOWN — REQUIRES EVIDENCE`; the Phase 2A SQL proved account-level cache parity, not a full per-party subsidiary-ledger tie-out.

## 17. Inventory Boundary

Inventory has three distinct paths:

1. POS sale/return posts both operational quantity and GL cost effects.
2. Purchases and purchase returns post Inventory and Supplier/Cash asset effects.
3. Storefront and CommerceHub fulfillment/reconciliation write inventory reservations or `StockMovements`, while e-commerce/CommerceHub finance journals do not include `5010`/`1310` merchandise cost lines.

`PostInventoryAdjustmentAsync` explicitly rejects accounting without an authoritative SQL adjustment workflow (`CommerceAccountingService.cs:1627-1637`). This is correct containment, but it means Admin Inventory adjustment is not a certified GL inventory adjustment.

CommerceHub has live inventory sale movements and live financial postings in greatone and Shopixy. A complete order-level tie between quantity, unit cost, COGS, and `1310` is therefore not present. Exact tenant-level missing COGS valuation is `UNKNOWN — REQUIRES EVIDENCE` because the audit did not assign historical costs or create corrective journals.

## 18. Cash Flow And Financial Reports

`FinancialReportService.GetCashFlowAsync` uses ledger snapshots and journal lines inside a consistent read transaction. It includes `Posted` and `Reversed` journal headers and computes opening/closing reconciliation (`FinancialReportService.cs:402-489`). Clearing Aging is ledger-derived and filters clearing accounts.

The remaining semantic issue is the cash perimeter:

```csharp
accountCode.StartsWith("10") || accountCode.StartsWith("11")
```

This classifies Cashbox, Bank, clearing accounts, parent accounts, and unmapped `1020` as cash equivalents (`FinancialReportService.cs:892-897`). Consequences:

| Event | Current Cash Flow behavior |
| --- | --- |
| Collection into clearing | Treated as cash increase before bank settlement. |
| Clearing-to-bank settlement | Gross transfer nets to zero; only fees reduce the combined pool. |
| Supplier-card funding | Cash decrease and investing asset increase if card code is outside `10`/`11`. |
| Initial capitalization | Financing cash increase. |
| Movement in unmapped `1020` | Included despite no active Treasury mapping. |

This can be a deliberate broad cash-and-clearing report, but it is not derived from Treasury authority and its label does not disclose the distinction. A separately certified liquid-cash report excluding clearing and unmapped assets is `UNKNOWN — REQUIRES EVIDENCE`.

## 19. Accounting Outbox And Idempotency

Every existing accounting outbox row observed for AhramStore, greatone, and Shopixy is processed. Zero existing rows are pending or failed. Observed histories cover POS, party, CommerceHub, supplier, refund/reversal, settlement-capable, and capitalization event families.

The reliable path is source transaction → durable message → processor dispatch → idempotent journal → source posting status. `FinancialPostingService` also prevents one idempotency key from binding to a different financial shape.

Exceptions and boundaries:

| Case | Result |
| --- | --- |
| AhramStore supplier-card funding | Journal exists; corresponding historical accounting outbox row is absent. |
| Affiliate earned | MassTransit event consumed by registered GL consumer. |
| Affiliate paid | GL journal is synchronous inside the serializable settlement orchestrator; paid event is projection-only. |
| Affiliate reversed | Event reaches projections, but no GL consumer is registered. |
| Generic Treasury transfer | Direct certified posting; no accounting outbox. |

The zero pending/failed count proves queue state at the capture time; it does not prove that every source created a message.

## 20. API And UI Authority Map

| Surface | Capability | Authority result |
| --- | --- | --- |
| Treasury Dashboard/Hub/Accounts | Read balances and navigate | Read-only presentation. |
| Treasury Transfers | Cashbox/Bank transfer | Explicitly excludes clearing settlement. |
| Treasury Cash Flow | Ledger-derived report | Read-only. |
| Treasury Clearing Aging | Aging and journal drill-down | Read-only; directs settlement to payment lifecycle. |
| Trial Balance/P&L/Balance Sheet/General Ledger | Financial reports | Read-only. |
| Orders | COD delivered and refund actions | Calls payment/order lifecycle, not generic Treasury. |
| Payments | Payment review/status | Collection lifecycle surface. |
| Supplier Card Funding Instruments | Instrument funding | Dedicated supplier-card authority. |
| Treasury Initial Capitalization | One-time opening allocation | Dedicated capitalization authority. |
| CommerceHub Finance/Payments | Finance and payment operations | CommerceHub domain authority. |
| Affiliate admin/portal | Commission and payout operations | Affiliate subledger/orchestrator authority. |
| Dedicated COD/gateway settlement execution screen | Not found | `UNKNOWN — REQUIRES EVIDENCE`. |

Routes are present in `PointOfSale.Web/src/App.jsx`; finance navigation is present in `Sidebar.jsx`. `TreasuryTransfers.jsx` limits generic movement to Cashbox/Bank, and `TreasuryClearingAging.jsx` does not execute settlement. Browser rendering, authorization, RTL, responsive behavior, and real submit behavior were not exercised in Phase 2A and are `UNKNOWN — REQUIRES EVIDENCE`.

## 21. Consolidated Findings

| ID | Severity | Finding | Evidence | Gate |
| --- | --- | --- | --- | --- |
| P2A-01 | High | Affiliate reversal has no GL bridge. | Registered earned consumer only; live Shopixy EGP 120 reversed subledger and EGP 120 payable. | Blocker |
| P2A-02 | High | E-commerce/CommerceHub merchandise COGS and GL inventory relief are absent from collection/refund finance paths. | `CommerceAccountingService` e-commerce methods and live CommerceHub inventory/finance evidence. | Blocker |
| P2A-03 | High | Fawry has no explicit accounting target or settlement support; unknown gateway falls back to Stripe. | `CommerceAccountingService.cs:1497-1511`; `PaymentLifecycleService.cs:1897-1905`. | Blocker before Fawry enablement |
| P2A-04 | Medium | No live payment settlement batch exists in the three tenants. | Zero batch/member rows. | Evidence gap |
| P2A-05 | Medium | AhramStore supplier-card funding journal lacks outbox lineage. | Live source/journal/outbox comparison. | Historical exception |
| P2A-06 | Medium | Cash Flow perimeter is code-prefix based, not Treasury-authority based. | `FinancialReportService.cs:892-897`. | Reporting semantics |
| P2A-07 | Medium | No live CommerceHub or Affiliate payout proves external evidence through cash/payable clearing. | Zero payouts, vendor settlements, transfer evidence, and allocations. | Evidence gap |
| P2A-08 | Low | No dedicated payment-settlement execution UI was found. | Route/component source map. | Operational gap |

Positive controls retained:

| Control | Result |
| --- | --- |
| Ledger/cache reconciliation | 97 of 97 active accounts, zero drift. |
| Treasury mapping | Six active mappings per tenant. |
| Journal balance/idempotency | Balanced immutable posting service boundary. |
| Existing accounting outbox state | Zero pending and zero failed. |
| Manual clearing settlement | Contained with HTTP 410. |
| Generic transfer scope | Cashbox/Bank only. |
| Payment settlement model | Durable batch and order membership before posting. |

## 22. Required Remediation And Evidence

Release blockers should be resolved in this order:

1. Add an idempotent Affiliate reversal GL consumer or equivalent synchronous reversal authority. Prove full and partial reversals, duplicate delivery, out-of-order delivery, paid-then-refunded policy, and `2105`/`6110` reconciliation without editing historical journals.
2. Define the authoritative cost snapshot and post e-commerce/CommerceHub COGS and inventory relief/restoration. Prove order-level quantity/cost/GL reconciliation and idempotent refund/return handling.
3. Either define explicit Fawry payment-method, clearing, fee, and settlement authority or reject Fawry before accounting. Remove the silent Stripe fallback for unsupported providers.
4. Execute certified non-production gateway and COD settlements through `PaymentLifecycleService`, including fee, duplicate, mismatch, and post-settlement refund cases.
5. Reconcile the AhramStore supplier-card historical outbox omission through an approved remediation process; do not fabricate source history.
6. Make Cash Flow classification explicit: derive liquid cash from active Treasury Cashbox/Bank mappings, or rename/document the current combined cash-and-clearing perimeter and separately expose liquid cash.
7. Execute certified CommerceHub and Affiliate payout scenarios with immutable external evidence, payable clearance, bank/cash effect, replay proof, and tenant isolation.
8. Add or certify a settlement operations UI that submits only to the payment lifecycle and displays batch, provider, gross, fees, net, order membership, bank reference, journal, and failure state.

Required unresolved evidence:

| Claim | Status |
| --- | --- |
| Successful live Stripe collection and settlement in the three tenants | `UNKNOWN — REQUIRES EVIDENCE` |
| Successful live Paymob/Card collection and settlement in the three tenants | `UNKNOWN — REQUIRES EVIDENCE` |
| Wallet external settlement authority | `UNKNOWN — REQUIRES EVIDENCE` |
| Fawry GL and settlement design | `UNKNOWN — REQUIRES EVIDENCE` |
| CommerceHub payout end to end | `UNKNOWN — REQUIRES EVIDENCE` |
| Affiliate payout end to end | `UNKNOWN — REQUIRES EVIDENCE` |
| Per-party subsidiary-ledger reconciliation | `UNKNOWN — REQUIRES EVIDENCE` |
| Historical missing e-commerce COGS valuation | `UNKNOWN — REQUIRES EVIDENCE` |
| Browser/runtime settlement behavior | `UNKNOWN — REQUIRES EVIDENCE` |

## 23. Certification

The Phase 2A global source map is complete enough to identify source, GL, party, inventory, Cash Flow, outbox, settlement, and UI boundaries for the audited implementation and SQL snapshot. It is not a production Treasury certification because live Affiliate reversal does not close the GL, e-commerce inventory does not close through COGS, Fawry lacks explicit accounting/settlement authority, and several payout/settlement paths have no live execution evidence.

**Final verdict: FAIL**

FAIL
