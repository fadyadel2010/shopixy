# Treasury T-09 Fawry Provider Authority Delegation Brief - 2026-09-14

## 1. Mission

Implement the smallest complete T-09 containment change for Fawry. Fawry payment transport exists, but the application has no certified Fawry clearing account, accounting payment-method mapping, settlement feed, settlement inquiry, or durable settlement authority. New Fawry financial activity must therefore fail closed before business or financial mutation, while Stripe, Paymob/Card/Visa, COD, and Manual Wallet retain their current behavior.

Do not commit. Leave the implementation and tests uncommitted for independent SOL review.

## 2. Frozen Decision

The decision is **unsupported fail-closed**, not new Fawry settlement support.

- Do not create a Fawry ledger account, Treasury account, accounting `PaymentMethod`, seed, migration, settlement batch shape, fake payout evidence, or provider-specific Cash Flow rule.
- Do not infer settlement from a Fawry payment status, reference number, transaction inquiry, refund response, webhook, manual bank reference, or generic Treasury transfer.
- Keep the dormant Fawry protocol, signature, credential, adapter, and historical presentation code unless a small change is required for containment. Translation-only provider tests may continue to certify those dormant components.
- Use one stable public reason code for this containment: `FAWRY_FINANCIAL_AUTHORITY_UNAVAILABLE`.
- User-facing wording should state that Fawry is unavailable because financial settlement authority is not configured/certified. It must not say that a transient provider outage occurred.

## 3. Source Evidence

Observed source facts:

- `PaymentProvider.Fawry`, Fawry checkout/session creation, tenant credentials, signed webhook processing, reference-code presentation, and advanced refund/capture/void transport all exist.
- `PointOfSale.API/Services/Payments/Fawry/FawryAdvancedOperationsAdapter.cs` explicitly marks settlement inquiry unsupported because no official settlement inquiry API was discovered.
- `PointOfSale.Infrastructure/Accounting/CommerceAccountingService.cs` maps Stripe and Paymob/Card, but `ResolveEcommercePaymentAccountIdAsync` defaults every other value, including Fawry and unknown providers, to Stripe.
- `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs` accepts only Stripe, Paymob, Card, or Visa for gateway settlement.
- Fawry successful webhooks can currently enter the canonical lifecycle and enqueue collection accounting.
- Pre-settlement Fawry refunds resolve their credit account through the same unsafe collection-account resolver.
- Admin provider catalog/configuration and the storefront payment-method catalog currently advertise Fawry as executable.
- Checkout validates runtime readiness before order creation, but current Fawry runtime readiness checks credentials rather than financial settlement authority.
- Checkout intent recovery currently treats Fawry as recoverable and can re-enter provider initiation.

## 4. Read-Only Data Evidence

Read-only PostgreSQL inspection of `AhramStore`, `greatone`, and `shopixy` found:

- zero Fawry `TenantPaymentMethods` rows;
- zero accounting `PaymentMethods` rows with a Fawry code;
- zero Fawry-named ledger or Treasury accounts;
- the common accounting mappings are `CASH -> 1010`, `CARD -> 1110`, `STRIPE -> 1120`, `COD -> 1130`, `WALLET -> 1140`, and `BANK_TRANSFER -> 1030`;
- no legitimate Fawry clearing or settlement authority to adopt.

Do not perform direct SQL writes, historical corrections, manual journals, balance patches, or test-data insertion into the real database.

## 5. Provider Authority Matrix

Preserve this matrix:

| Provider | New checkout/intent | Collection account | Settlement | Refund accounting | Admin/storefront posture |
| --- | --- | --- | --- | --- | --- |
| Stripe | Allowed under existing readiness | Stripe clearing | Existing gateway lifecycle | Existing pre/post-settlement behavior | Supported |
| Paymob/Card/Visa | Allowed under existing readiness | Visa/Card clearing | Existing gateway lifecycle | Existing pre/post-settlement behavior | Supported |
| COD | Allowed | COD clearing | Existing offline lifecycle | Existing behavior | Supported |
| Manual Wallet | Allowed under existing review authority | Wallet clearing | No new T-09 authority | Existing behavior | Supported |
| Fawry | Rejected before mutation | None | Rejected | Rejected before refund mutation | Visible as unsupported in admin if useful; never selectable in storefront |
| Unknown | Rejected | None; never Stripe | Rejected | Rejected | Unsupported |

## 6. Stable Error Contract

Use `FAWRY_FINANCIAL_AUTHORITY_UNAVAILABLE` consistently at Fawry financial admission boundaries. Prefer a small shared policy/constant in `PointOfSale.Core/Payments` if that avoids divergent string literals without introducing a framework.

Required semantics:

- admin configure/upsert/enable attempts: structured non-2xx response with the stable code and no tenant-row/outbox mutation;
- storefront runtime readiness: blocked with the stable code before credential/provider execution;
- checkout submission: non-2xx with the stable code before order, reservation, payment intent, provider call, or outbox mutation;
- direct or recovery payment-intent initiation: rejected before a new intent or provider checkpoint/provider call is created;
- successful/authorized Fawry webhook lifecycle admission: rejected before order status, payment intent terminal state, inventory, accounting outbox, COGS outbox, or journal mutation;
- Fawry settlement and refund commands: stable failure code and no settlement/refund/status/outbox/journal mutation.

Do not hide this permanent authority failure behind `PAYMENT_RUNTIME_UNAVAILABLE`, `PROVIDER_NOT_SUPPORTED`, `SETTLEMENT_MISMATCH`, or a generic HTTP 500 when the provider is known to be Fawry.

## 7. Admin Catalog And Mutation

Keep Fawry discoverable in the POS Admin catalog only if it is clearly marked unsupported. `PaymentMethodCatalogProviderDto.IsSupported` already exists and should become truthful; add one concise reason-code/message field only if needed by the UI contract.

- Existing tenant rows, if any outside the audited tenants, must render as unsupported regardless of stored `IsEnabled` or credential validity.
- Disable Fawry Configure, Enable, and editable operational controls in `PointOfSale.Web/src/pages/PaymentMethods.jsx`.
- The UI must show a clear unsupported/financial-authority-unavailable state on desktop and mobile rather than presenting credential fields as a route to activation.
- Server-side mutation rejection is mandatory; UI disabling alone is not authority.
- Do not delete or rewrite existing tenant rows.

## 8. Storefront Catalog And Selection

Fawry must never be returned as an available storefront payment method, even if a stale Mongo projection says enabled/configured and a SQL tenant row is enabled.

- Make the SQL/runtime authority suppress Fawry deterministically with `FAWRY_FINANCIAL_AUTHORITY_UNAVAILABLE`.
- Preserve all existing suppression rules for deleted, disabled, invalid, or runtime-unready providers.
- Storefront React should continue to render only the API-admitted list. Do not add a hardcoded list or frontend-only Fawry filter unless it is strictly defense in depth.
- Historical Fawry success/reference rendering may remain for already-issued DTOs; it must not make Fawry selectable.

## 9. Checkout And Payment Intent Admission

Reject Fawry at the earliest provider-specific boundary after basic request/tenant validation and before any order, inventory reservation, payment intent, provider checkpoint, provider HTTP call, idempotency completion, or outbox mutation.

- The normal checkout path must emit the stable code rather than commit an order and fail post-commit during payment-intent creation.
- The direct `PaymentsController`/`PaymentService` path must not bypass containment.
- `FawryPaymentGateway` should be defense in depth if needed, not the only guard.
- Checkout payment-intent recovery must not initiate or resume Fawry. It should leave no new financial facts and follow an existing safe unrecoverable/expiry path where applicable; do not invent a destructive historical cleanup.
- Do not remove Fawry from the enum or deserialize old persisted values as another provider.

## 10. Webhook And Capture Containment

Authenticated Fawry transport evidence is not clearing or settlement authority.

- A Fawry `Authorized` or `Succeeded` gateway webhook must not transition an order/payment intent, commit inventory, enqueue collection/COGS/suspense accounting, or post a journal.
- Return a deterministic non-success contract carrying `FAWRY_FINANCIAL_AUTHORITY_UNAVAILABLE`; avoid endless generic-500 behavior if the controller can return the stable permanent rejection safely.
- Invalid signatures, malformed payloads, replay detection, and redaction behavior must remain intact.
- Failure/cancellation evidence may retain existing non-money behavior only if it cannot authorize collection or accounting; do not broaden the task into a webhook rewrite.
- Keep enough audit/log evidence to diagnose an attempted callback without treating that log as payment authority.

## 11. Settlement Containment

Fawry gateway settlement must fail before selector resolution or any durable mutation with `FAWRY_FINANCIAL_AUTHORITY_UNAVAILABLE`.

- No `PaymentSettlementBatch`, member, order status, event, accounting outbox, or journal may be created.
- Existing Stripe/Paymob/Card/Visa validation, tenant scoping, exact selector, amount/currency matching, accounting-readiness, and idempotency behavior must remain unchanged.
- Do not use `TreasuryTransfers`, the retired generic settle route, a bank reference, or Fawry transaction inquiry as substitute settlement authority.

## 12. Refund And Reversal Containment

Because no Fawry clearing or post-settlement lineage is certified, an application refund command for a Fawry order must fail before provider refund execution or local refund mutation with the stable code.

- No `OrderRefund`, refund allocation, payment status, stock restoration/release, affiliate reversal, accounting/COGS outbox, or journal may be created by the rejected command.
- Do not map a Fawry refund to Stripe clearing, Visa clearing, Wallet clearing, Bank, or Treasury Suspense.
- Keep existing Stripe/Paymob/COD/Manual Wallet refund and T-08 stock/COGS behavior unchanged.
- Dormant translation-only Fawry refund adapter tests can remain; they do not prove application refund authority.

## 13. Accounting Resolver Defense

Remove the unknown-to-Stripe fallback in `ResolveEcommercePaymentAccountIdAsync`.

- Every accepted code/alias must map explicitly.
- Fawry and unknown/blank provider values must fail without calling `IAccountResolver.GetPaymentAccountIdAsync` and without calling `IFinancialPostingService.PostJournalAsync`.
- Apply the same no-fallback property to online capture, unresolved capture, CommerceHub unresolved clearance, and pre-settlement refund paths that share this resolver.
- Preserve explicit mappings for Stripe, Paymob/Card, COD, and Wallet.
- Do not change Cash Flow or Clearing Aging calculations; preventing false journals is the T-09 reporting fix.

## 14. Data, Schema, And Historical Boundary

No schema or migration is required or authorized.

- No seed changes.
- No account-code additions.
- No historical order/payment/refund mutation.
- No automatic disabling update against existing real database rows.
- No backfill or corrective journal.
- Existing stored enum/value compatibility must remain readable.

## 15. Required Tests

Add or update focused tests proving all of the following:

- admin catalog marks Fawry unsupported and server mutations reject without persistence/events;
- admin React renders Fawry unavailable and exposes no active configure/enable action;
- storefront API suppresses a stale enabled/configured Fawry document and SQL row while leaving supported providers visible;
- checkout Fawry admission returns the stable code before order/reservation/intent/outbox mutation;
- direct and recovery intent initiation cannot call the Fawry provider or create an intent/checkpoint;
- successful Fawry webhook admission cannot call the lifecycle money mutation or alter durable financial/business state;
- Fawry settlement fails with the stable code before batch/member/status/outbox/journal mutation;
- Fawry refund fails with the stable code before refund/status/stock/affiliate/outbox/journal mutation;
- Fawry and unknown accounting collection resolution never call account resolution or posting and never use Stripe clearing;
- positive controls for Stripe, Paymob/Card/Visa, COD, and Manual Wallet remain green;
- tenant isolation and duplicate/idempotency controls are not weakened.

Prefer behavioral tests. Update old source-string architecture tests only where their former assertion directly contradicts this new frozen authority decision; do not delete broad Fawry transport coverage merely to make the suite green.

## 16. Allowed Scope

Touch only files required by the behavior above. Expected candidates are:

- a small shared policy/constant under `PointOfSale.Core/Payments/`;
- `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs`;
- `PointOfSale.API/Controllers/StorefrontPaymentMethodsController.cs`;
- `PointOfSale.API/Controllers/PaymentsController.cs` and/or the relevant payment exception contract;
- `PointOfSale.API/Controllers/FawryWebhookController.cs`;
- `PointOfSale.API/Services/Payments/StorefrontPaymentRuntimeReadinessService.cs`;
- `PointOfSale.API/Services/Payments/PaymentService.cs`;
- `PointOfSale.API/Services/Payments/Fawry/FawryPaymentGateway.cs`;
- `PointOfSale.API/Services/Checkout/CheckoutService.cs` and `CheckoutPaymentIntentRecoveryService.cs` only if required for earliest no-mutation admission;
- `PointOfSale.Infrastructure/Accounting/CommerceAccountingService.cs`;
- `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs`;
- focused files under `PointOfSale.Tests/`, `PointOfSale.Web/src/pages/PaymentMethods*`, and existing Storefront tests only as required.

Do not touch configuration files, secrets, migrations, snapshots, generated files, unrelated reports, or unrelated business domains.

## 17. Worktree And Action Safety

The repository was already heavily dirty before T-09. HEAD at brief creation was `0de1c542bd316026de06bd5cc8f7111878b1b0e6`.

`CommerceAccountingService.cs`, `PaymentLifecycleService.cs`, their tests, and many other files contain uncommitted T-06/T-07/T-08 work. Preserve every pre-existing change. Never reset, checkout, restore, clean, reformat wholesale, or replace a whole file. Read the current working copy and make narrow additive edits around existing code.

No unrelated refactors, renames, cleanup, dependency changes, broad formatting, `git add`, `git commit`, database writes, server startup against real data, or destructive commands. If a correct implementation requires scope outside this brief, stop and report the blocker rather than expanding scope.

## 18. Verification Loop And Report Contract

Run the narrow tests first, fix failures, then run the broader gates that are feasible:

```text
dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" -c Release --no-restore --filter "FullyQualifiedName~Fawry|FullyQualifiedName~PaymentMethod|FullyQualifiedName~PaymentLifecycleSettlementAuthority|FullyQualifiedName~PaymentLifecycleRefundAuthority|FullyQualifiedName~CommerceAccountingService"
dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" -c Release --no-restore
dotnet build "PointOfSale.sln" -c Release --no-restore
npm test -- --run src/pages/PaymentMethods.test.tsx
npm run build
```

Run the Web commands from `PointOfSale.Web`. If Storefront files change, also run from `PointOfSale.Storefront`:

```text
npm test -- --run
npm run build
```

Confirm the final diff contains only intended T-09 deltas layered on the dirty baseline. Do not claim a gate passed unless you ran it and captured the result.

End with a report in exactly this shape:

1. Root cause and implemented authority decision.
2. Files touched and why.
3. Exact no-mutation boundaries established.
4. Tests/builds run with pass/fail counts.
5. Pre-existing failures or dirty-worktree interactions kept separate.
6. Deviations, unresolved risks, or decisions needed.
