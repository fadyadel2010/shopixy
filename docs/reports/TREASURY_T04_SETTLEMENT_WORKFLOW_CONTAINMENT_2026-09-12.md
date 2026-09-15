# Treasury T-04 Settlement Workflow Containment - 2026-09-12

## 1. Decision

**T-04 is closed by safe containment.** Clearing Aging no longer advertises a manual settlement that redirects to a generic transfer guaranteed to reject Clearing accounts and incorrectly targets Cashbox. Clearing settlement remains available only through the provider/carrier evidence-backed Payment Lifecycle authority.

This is not certification that a live shopixy settlement completed. Shopixy has no eligible settlement batch/order evidence with which to perform one without inventing a financial fact.

## 2. Scope

This change addresses only T-04 from the Phase 1 Treasury audit. It removes the false Clearing Aging affordance, narrows the generic transfer UI to its actual Cashbox/Bank contract, and makes gateway clearing-account selection fail closed for unsupported providers. It does not change T-03 or T-05 through T-09.

## 3. Tenant Boundary

All live financial checks in this report are scoped to:

| Field | Value |
| --- | --- |
| Tenant | `shopixy` |
| Tenant ID | `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` |
| Currency | EGP |
| Database | `PosEcommerceDb_Baseline` |
| API | `http://localhost:5010` |

The known POS Admin browser session belongs to `greatone`. It was not used as shopixy financial or UI-content evidence.

## 4. Original Defect

`TreasuryClearingAging.jsx` presented a Settle action for each pending clearing line. The handler navigated to `/treasury/transfers` with the Clearing account as source, Cashbox `1010` as destination, the pending amount, and a settlement-shaped reference.

The destination was semantically wrong because certified provider settlement posts net proceeds to Bank. The operation was also impossible: the active generic transfer authority permits only Cashbox or Bank Treasury accounts on both sides and rejects Clearing accounts.

## 5. Reproduction Path

The pre-fix path was:

```text
Clearing Aging Settle
  -> /treasury/transfers
  -> source = Clearing
  -> destination = Cashbox 1010
  -> POST /api/treasury/operations/transfer
  -> TreasuryOperationService.TransferAsync
  -> reject: source lacks Cashbox/Bank authority
```

The action could never perform the settlement it advertised.

## 6. Accounting Authority

The authority hierarchy remains:

1. Current tenant-owned provider/carrier and order records authorize settlement.
2. `PaymentLifecycleService` validates exact evidence and persists the settlement batch plus order membership.
3. The accounting outbox durably carries the posting intent.
4. `AccountingOutboxProcessor` reloads current SQL settlement authority.
5. `CommerceAccountingService` constructs the provider-specific balanced journal.
6. `FinancialPostingService` validates authority and appends immutable journal and ledger lines.

An aggregate Clearing Aging balance is a report result, not settlement authority.

## 7. Certified Settlement Entry Points

| Settlement type | HTTP entry point | Lifecycle method |
| --- | --- | --- |
| Gateway | `POST /api/payments/lifecycle/settlements/gateway` | `RecordGatewaySettlementAsync` |
| COD/carrier | `POST /api/payments/lifecycle/settlements/cod` | `RecordOfflineSettlementAsync` |

Both routes require Owner/Admin authorization and bind settlement processing to `ITenantContext.RequiredTenantId`.

## 8. Gateway Evidence Contract

Gateway settlement requires a settlement ID, supported gateway, positive gross amount, nonnegative fees, currency, and exactly one selector: payout batch ID, explicit order IDs, or payment-intent IDs.

The lifecycle resolves current tenant-owned records and validates resolved count, tenant, currency, gateway, exact gross total, collected status, collection-accounting readiness, prior settlement membership, and replay consistency before creating authority.

## 9. COD Evidence Contract

COD settlement requires a settlement ID, carrier identity, exact eligible order membership, gross amount, fees, and currency. The offline lifecycle validates tenant, carrier, order state, totals, accounting readiness, and duplicate evidence before creating the batch and membership rows.

## 10. Durable Settlement Lineage

`PaymentSettlementBatches` stores the settlement header and evidence identity. `PaymentSettlementOrders` stores immutable tenant/order membership and optional payment-intent lineage. Unique indexes prevent duplicate tenant/type/settlement identity and duplicate order settlement membership.

These rows, reloaded against current non-deleted membership, are accounting replay authority. A UI-selected account balance is not a substitute.

## 11. Journal Direction

Certified gateway settlement posts:

| Line | Direction | Amount |
| --- | --- | --- |
| Bank `1030` | Debit | Gross less fees |
| Gateway Fees `6010` | Debit | Fees, when nonzero |
| Provider clearing | Credit | Gross |

Certified COD settlement follows the same net-to-Bank and gross-from-COD-Clearing structure. No settlement posts provider proceeds to Cashbox merely because an operator selected it.

## 12. Provider Mapping

Gateway settlement now has an explicit clearing authority map:

| Provider | Clearing account |
| --- | --- |
| Stripe | `1120` Stripe Clearing |
| Paymob | `1110` Visa Clearing |
| Card | `1110` Visa Clearing |
| Visa | `1110` Visa Clearing |

Matching is case-insensitive after trimming.

## 13. Unsupported Provider Behavior

Unknown gateway values no longer fall back to Stripe Clearing. `PaymentLifecycleService` rejects unsupported providers before resolving or mutating orders, settlement rows, or outbox state. `CommerceAccountingService` independently throws if invoked with a provider that has no configured clearing-account authority.

The public lifecycle contract normalizes this rejection to `SETTLEMENT_MISMATCH`; the internal telemetry reason remains `unsupported_gateway`.

## 14. Generic Transfer Boundary

Generic transfer remains a distinct operation for movement between active, same-tenant Cashbox and Bank Treasury accounts. Both `TreasuryOperationService` and `TreasuryTransferService` enforce that boundary before posting.

Generic transfer does not validate provider payouts, carrier remittances, eligible orders, payment intents, gross-to-order totals, or settlement replay. It therefore must not represent Clearing settlement.

## 15. Legacy Manual Endpoint

`POST /api/admin/accounting/treasury/settle` remains disabled and returns HTTP `410 Gone` with code `SETTLEMENT_AUTHORITY_REQUIRED`. The implementation does not call `TreasuryTransferService.SettleClearingAsync`.

This containment is intentional. Re-enabling the endpoint would allow operator assumptions to replace provider/order evidence.

## 16. Clearing Aging UI Change

The false Settle handler, confirmation flow, Cashbox prefill, settlement navigation state, and settling state were removed from `TreasuryClearingAging.jsx`.

Each pending line now shows a disabled `Provider workflow` action. Its explanation states that settlement is recorded from provider or carrier evidence linked to eligible orders. General Ledger drill-down and exports remain available.

## 17. Transfers UI Change

`TreasuryTransfers.jsx` now filters the API account list to `Cashbox` and `Bank`. Validation rejects any source or destination not present in that filtered set before submission.

The page description and examples now describe generic cashbox/bank movement rather than COD settlement. Existing navigation-state support remains for legitimate generic transfers only.

## 18. Defense In Depth

The T-04 boundary is enforced at multiple layers:

| Layer | Control |
| --- | --- |
| Clearing Aging UI | No actionable manual settlement route |
| Transfers UI | Cashbox/Bank options and validation only |
| Generic transfer services | Cashbox/Bank authority required on both sides |
| Legacy settlement controller | HTTP 410 containment |
| Payment lifecycle | Provider/carrier and order evidence validation |
| Settlement persistence | Unique batch and order-lineage constraints |
| Accounting processor | Reloads current SQL authority before posting |
| Accounting mapper | Explicit provider-to-clearing map; no fallback |

The UI is not the security boundary; backend controls remain authoritative.

## 19. Shopixy Starting State

The T-02 read-only database reconciliation established zero `PaymentSettlementBatches` and zero `PaymentSettlementOrders` for shopixy. The current clearing state is:

| Account | Balance |
| --- | ---: |
| Visa Clearing `1110` | 55.00 |
| Stripe Clearing `1120` | 0.00 |
| COD Clearing `1130` | 0.00 |
| Wallet Clearing `1140` | 0.00 |

The EGP 55.00 Visa balance comes from POS sales-invoice card tender lineage, not an eligible e-commerce `Order`/`PaymentIntent` settlement set.

## 20. Why No Real Settlement Was Run

A successful lifecycle call would require real provider/carrier evidence and exact eligible order membership. None exists for the EGP 55.00 POS card balance. Creating a batch, modifying an order, or fabricating payout identifiers merely to force a successful test would manufacture accounting authority.

Accordingly, runtime certification uses controlled rejection requests and before/after balance comparison. Successful settlement posting is covered by focused controlled tests, not fabricated live business data.

## 21. Live API Verification

After the API was restarted with the changed build, a shopixy-scoped development token was used. Results:

| Check | HTTP | Result |
| --- | ---: | --- |
| `GET /api/treasury/balances?accountCodes=1030,1110,1120,1130,1140` | 200 | Expected balances returned |
| `GET /api/financialreports/clearing-aging?asOf=2026-09-12T23:59:59Z` | 200 | Visa only; total pending EGP 55.00 |
| `GET /api/treasury/accounts` | 200 | Cashbox, Bank, and four Clearing mappings returned |
| Unsupported provider gateway settlement | 409 | `SETTLEMENT_MISMATCH`; provider explicitly unsupported |
| Clearing `1110` to Bank `1030` generic transfer | 400 | Clearing rejected for lack of Cashbox/Bank authority |
| Legacy manual clearing settlement | 410 | `SETTLEMENT_AUTHORITY_REQUIRED` |

The development token issuer may update testing-session identity state; it did not authorize a financial fact.

## 22. Runtime Non-Mutation Proof

Balances immediately before and after all controlled rejection calls were identical:

| Account | Before | After | Change |
| --- | ---: | ---: | ---: |
| Bank `1030` | 10,410.10 | 10,410.10 | 0.00 |
| Visa Clearing `1110` | 55.00 | 55.00 | 0.00 |
| Stripe Clearing `1120` | 0.00 | 0.00 | 0.00 |
| COD Clearing `1130` | 0.00 | 0.00 | 0.00 |
| Wallet Clearing `1140` | 0.00 | 0.00 | 0.00 |

No successful transfer or settlement response, journal number, or financial state transition was returned. PostgreSQL CLI tooling was unavailable for a fresh direct row-count query, so the earlier read-only zero-batch baseline is not misrepresented as a new SQL observation.

## 23. Automated Coverage

Focused tests cover:

| Area | Assertion |
| --- | --- |
| Generic transfer | Clearing source rejected before posting |
| UI architecture | No Clearing Aging transfer navigation or Cashbox settlement prefill |
| Legacy endpoint | HTTP 410 authority containment remains in source contract |
| Provider map | Stripe, Paymob, Card, and Visa select explicit clearing accounts |
| Unknown provider | Fails without invoking posting |
| Lifecycle unknown provider | No batch, membership, order transition, or accounting call |
| Replay | Identical gateway evidence is idempotent |
| Changed replay | Same settlement ID with changed evidence is rejected |
| Settlement authority | Current batch/order lineage governs accounting replay |

The final focused run passed `61/61`, with zero failures and zero skips.

## 24. Build Verification

`npm run build` for `PointOfSale.Web` passed. The only reported frontend build issue was the existing large-chunk warning.

The focused .NET run compiled into an isolated artifacts directory because the running API locks normal build output on Windows. Repository-wide existing compiler and xUnit analyzer warnings remain outside T-04.

Direct ESLint execution was unavailable because the repository uses ESLint 9 without an `eslint.config.js`; this is a tooling gap, not a T-04 runtime failure.

## 25. Browser Limitation

No shopixy browser-content certification is claimed. The available authenticated browser identity belongs to `greatone`, and mixing that UI session with shopixy API/database balances would violate tenant evidence boundaries.

Source-level UI architecture and the production frontend build are certified. A future shopixy browser session may verify settled English, Arabic/RTL, and mobile presentation, but it is not required to prove backend authority containment.

## 26. Residual Risks And Follow-Up

| Item | Status |
| --- | --- |
| Operator-triggered provider settlement UI | Not present; provider/carrier workflow integration remains future work |
| Wallet settlement | No dedicated wallet settlement posting authority; remains unsupported |
| POS card clearing settlement | EGP 55.00 lacks e-commerce order settlement lineage; requires a separately designed POS acquiring authority |
| Clearing Aging semantics | Still account-flow aging rather than matched settlement-lot aging; tracked as T-07 |
| Direct SQL post-check | Not repeated during T-04 due unavailable local PostgreSQL CLI |

These limits do not justify restoring the false generic-transfer settlement path.

## 27. Closure

| Gate | Result |
| --- | --- |
| False Clearing Aging settlement route removed | PASS |
| Cashbox destination assumption removed | PASS |
| Generic transfer UI restricted to Cashbox/Bank | PASS |
| Backend rejects Clearing generic transfer | PASS |
| Manual settlement shortcut remains disabled | PASS |
| Supported provider mapping explicit | PASS |
| Unsupported provider fallback removed | PASS |
| Settlement replay and changed-evidence behavior covered | PASS |
| Live shopixy rejection checks preserve balances | PASS |
| Focused automated tests | PASS, 61/61 |
| Frontend production build | PASS |
| Real shopixy settlement executed | NOT RUN, no legitimate evidence |
| Shopixy browser content certification | NOT CLAIMED |

**T-04 closure verdict: PASS by containment and authority preservation.** Clearing Aging no longer promises an impossible or unauthorized operation. Any future actionable settlement UI must collect or select real provider/carrier evidence and invoke Payment Lifecycle; it must not route Clearing balances through generic Treasury transfer.
