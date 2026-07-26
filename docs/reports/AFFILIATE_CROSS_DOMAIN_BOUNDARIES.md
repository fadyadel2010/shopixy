# Affiliate Platform — Cross-Domain Boundary Report

> Source of truth: implementation in `C:\Users\fadya\source\repos\PointOfSale`. All claims cite `file:line`.
> UI/React excluded per scope. "Affiliate" = `PointOfSale.Infrastructure/Services/Affiliate`, `Consumers/Affiliate`, `Core/Entities/Affiliate`, plus the `Affiliate*` controllers.

---

## 1. Merchant Platform Boundary

Affiliate is **derivative** for product/order data and **authoritative (tenant-owned source)** for the commission policy rate.

### What Affiliate READS from Merchant
- **Products** — read only for validation/gating. In `AffiliateService.EnableAffiliateForProductAsync` / `EnableAffiliateWithCategoryAsync` / `DisableAffiliateForProductAsync` the product is loaded purely to confirm existence and tenant ownership (`_context.Products.FirstOrDefaultAsync(p => p.Id == productId && p.TenantId == tenantId)`):
  - `AffiliateService.cs:192`, `:264`, `:352`, `:467`
- **Orders / OrderItems** — read at commission time. `RecordCommissionForOrderAsync` loads the order with items, `IgnoreQueryFilters` (called from a MassTransit consumer, no HTTP tenant context) and derives tenant from the order itself:
  - `AffiliateService.cs:1377-1380` (`_context.Orders.IgnoreQueryFilters().Include(o => o.Items)`)
- **Tenants** — read for currency resolution during payout-evidence verification (`_context.Tenants` for `tenant.Currency`): `AffiliatePayoutEvidenceService.cs:59-63`.
- **Commissionable amounts** are taken from **immutable checkout-time snapshots on `OrderItem`**, not from live product data:
  - `item.AffiliateCommissionBaseAmountSnapshot` / `AffiliateCommissionEligible` / `AffiliateCommissionRateSnapshot` / `AffiliateCommissionAmountSnapshot` — `AffiliateService.cs:1520-1546`, `:1561-1564`
  - DB CHECK `CK_OrderItems_AffiliateCommissionSnapshot_Valid` enforces snapshot integrity — `ApplicationDbContextModelSnapshot.cs:9438`.

### What Affiliate WRITES (to shared Merchant tables)
- Affiliate **does not write** to `Products`, `Orders`, or `OrderItems` business columns. It only *reads* the product/order and stamps a commission ledger.
- Affiliate writes its own tables: `AffiliateAccounts`, `AffiliateAttributions`, `AffiliateCommissionLedger`, `AffiliateBalanceLedger`, `AffiliatePayoutRequests`, `AffiliateProductPolicies`.

### Events
- **Inbound (emitted by Merchant/Checkout):** `AffiliateOrderPlacedEvent` (consumed by `AffiliateOrderAttributionConsumer.cs`) — carries both `TenantId` and `StoreTenantId`; the store's tenant is authoritative (`AffiliateOrderAttributionConsumer.cs:68-70`).
- **Outbound (emitted by Affiliate):** `AffiliateCommissionEarnedEvent` (`AffiliateService.cs:1591`), `AffiliateProductPolicyChangedEvent` (`AffiliateService.cs:242, :331, :412, :435`), `AffiliateCategoryUpdatedEvent`/`AffiliateCategoryDeactivatedEvent` (category lifecycle — see §5).

### Ownership / Authority
- **`AffiliateProductPolicy` is tenant-owned (Merchant sets the rate).** `AffiliateProductPolicy.cs:14` (`ITenantOwned`), `:53-71` (`Create(tenantId, productId, commissionRate, …)`), `:74-81` (`UpdateRate`). Invariant comment: *"Only tenant admin can create/update — affiliate NEVER controls rate"* (`AffiliateProductPolicy.cs:11`).
- Affiliate is **derivative** on product/order facts (it reads snapshots the Merchant/Checkout domain authored) but **authoritative** for the policy record and the commission ledger it owns.

---

## 2. CommerceHub / Marketplace Boundary

Affiliate is a **global, tenant-unbound** participant in the marketplace. The marketplace model is the central architectural decision (AF-19 / AF-19B / AF-19C).

### Global marketplace model (AF-19)
- **Affiliates are global — no tenant binding.** `AffiliateService.cs:30-34` (class doc: *"Affiliates are global (no tenant binding)", "ReferralCode is globally unique", "StoreTenantId tracks which store owes the commission", "Affiliate can earn from multiple stores"*).
- `RegisterAffiliateAccountAsync` no longer requires tenant context; `TenantId` param kept only for audit: `AffiliateService.cs:80-121` (comment *"Remove tenant check, affiliates are now global"* `:95`; global referral-code uniqueness check `:106-110`).
- Migration `af19_global_marketplace_affiliate.sql`:
  - Adds `StoreTenantId` to `AffiliateCommissionLedger`, backfills from `Orders.TenantId`, makes it NOT NULL `:24-36`.
  - Makes `ReferralCode` globally unique (drops per-tenant UQ, adds global UQ `UQ_AffiliateAccounts_ReferralCode`) `:71-89`.
  - Adds composite index `IX_AffiliateCommLedger_Affiliate_Store (AffiliateAccountId, StoreTenantId)` `:43-44` for cross-store queries.

### StoreTenantId — which store owes commission
- Commission is recorded with `StoreTenantId = order.TenantId` (the **store where the sale happened**), and `TenantId` on the ledger is `Guid.Empty` for global affiliates:
  - `AffiliateService.cs:1566-1567` (*"Record StoreTenantId ONLY (where sale happened). Affiliate has NO tenant binding"*), `:1594` (`StoreTenantId = order.TenantId`).
  - Guard at `AffiliateService.cs:2288-2298` rejects any ledger entry whose `StoreTenantId != order.TenantId` (AF-19B integrity).
- `AffiliateOrderPlacedEvent` carries `StoreTenantId`; attribution consumer defaults to it and rejects mismatch with `TenantId`: `AffiliateOrderAttributionConsumer.cs:68-70, :91, :97-100`.

### Affiliate vs CommerceHub marketplace orders vs merchant store orders
- **Marketplace attribution/commission/orders are separated** by `StoreTenantId` + the `AffiliateAuthorityTenantId` field (normalized from the affiliate's own tenant): emitted on `AffiliateCommissionEarnedEvent` as `AffiliateAuthorityTenantId = NormalizeAffiliateAuthorityTenantId(affiliate.TenantId)` — `AffiliateService.cs:1595, :973`.
- The settlement orchestrator treats marketplace backbone health as a **payout gating precondition** — it blocks outward money movement if CommerceHub marketplace aggregates are stale/unresolved:
  - `AffiliateSettlementOrchestrator.cs:1571` (`EnsureAffiliateAmbiguousMarketplaceAggregateBackboneReadyAsync`), `:1642` (`EnsureAffiliateMarketplaceBackboneReadyAsync`), `:1648` (drains `CommerceHubPaymentCompensationOutboxMessages`), `:1699` (`CommerceHubUnresolvedPaymentWebhooks`), `:1734-1850` (marketplace event-blocker reconciliation, `IsAmbiguousCriticalMarketplaceAggregate`, `MarketplaceEventOutboxMessage`).
- **Read:** Affiliate never writes to CommerceHub tables; it *reads* CommerceHub readiness/compensation outboxes purely to decide whether it may release a payout.
- **Write:** none to CommerceHub-owned tables.

### Lifecycle / account state (migration)
- `AF_PA34_AffiliateAccountLifecycle.sql`: adds `ClosedAtUtc`/`ClosureReason` and widens `Status` CHECK to `(0,1,2,3)` to permit terminal `Closed` — backend-owned closure evidence.

---

## 3. Payments Boundary

Affiliate is **reactive**: it records commission only after payment capture. No payment column is owned by affiliate.

### How payment capture triggers commission recording
- `AffiliatePaymentCapturedConsumers.cs` contains consumers for **all** capture channels:
  - `OnlinePaymentCapturedAffiliateConsumer` (`IConsumer<OnlinePaymentCapturedEvent>`) — Stripe/Paymob etc. `:36-116`
  - `CodDeliveredAffiliateConsumer` (`IConsumer<OrderCodDeliveredEvent>`) — COD collected at delivery `:122-201`
  - `WalletPaymentApprovedAffiliateConsumer` (`IConsumer<WalletPaymentApprovedEvent>`) — manual wallet approved by admin `:207-392`
  - `OrderCollectionAccountingPostedAffiliateConsumer` (`IConsumer<OrderCollectionAccountingPostedEvent>`) — replays at the authoritative accounting-posted boundary so capture can't outrun ledger truth `:398-464`
- Every consumer delegates to `IAffiliateService.RecordCommissionForOrderAsync(orderId)` (`:82, :167, :259, :443`). No commission math lives in the consumer (`:33-34`).

### Payment statuses that BLOCK commission
- Gating in `AffiliateService.RecordCommissionForOrderAsync`:
  - Terminal order status → skip: `OrderStatus.Cancelled or OrderStatus.Refunded` → no pending commission `AffiliateService.cs:1427-1435`.
  - **Payment authority gate:** commission only created when `HasPendingCommissionPaymentAuthority(order.PaymentStatus)` is true `:1437-1445`.
  - The authoritative set is `PaymentStatus.Collected`, `Settled`, `PartiallyRefunded` — `AffiliateService.cs:1675-1680`. Anything else (Pending, CashOnDelivery, Rejected, Expired, Refunded) is blocked (certified by `AffiliateCommissionCandidatePendingAuthorityCertificationTests.cs:33-37`).
  - **Accounting gate:** `FinancialPostingStatus` must be `Posted` or `NotRequired`; otherwise commission is **deferred** via `AffiliateCommissionDeferredException` (rethrows for retry) `:1447-1458`.
  - **Unresolved/captured payment gating at payout:** the orchestrator blocks payout while a payment is in a blocking unresolved state — `BlockingUnresolvedPaymentStatuses = ["PendingResolution","PendingTenantResolution","RoutedToSuspense","SuspenseMatchedPaymentPendingClearance"]` (`AffiliateSettlementOrchestrator.cs:77-83`); analogous refund statuses `:84-87`.

### Events emitted
- `AffiliateCommissionEarnedEvent` (only when commission actually recorded) `AffiliateService.cs:1591`.

---

## 4. Treasury & Accounting Boundary

Affiliate sub-ledger is **authoritative for its own ledgers** (`AffiliateBalanceLedger`, `AffiliateCommissionLedger`) and **posts to the General Ledger through the Accounting/Treasury services** — it never writes GL rows directly.

### Settlement orchestrator integrations
`AffiliateSettlementOrchestrator.cs` injects:
- `IFinancialPostingService _postingService` (`:64, :102`) — double-entry journal Post.
- `IAccountResolver _accountResolver` (`:65, :103`) — resolve GL accounts per tenant.
- `ICashboxLedgerAccountService _cashboxLedgerAccountService` (`:66, :104`) — resolve the store's cashbox account.

### Ledger postings ON PAYOUT
Two distinct posting paths exist:

1. **Orchestrator direct posting (AF-38H):** inside the SERIALIZABLE payout transaction, after creating the `AffiliateBalanceLedger.Withdrawal`, the orchestrator posts the **Paid** journal directly:
   - `PostingRequest` for each paid order: `Debit(AffiliatePayable, total)` / `Credit(cashAccount, total)` — `AffiliateSettlementOrchestrator.cs:1535-1559` (`_postingService.PostJournalAsync`, `:1559`). `payableAccount` resolved via `IAccountResolver`; `cashAccount` via `ICashboxLedgerAccountService.ResolveDefaultCashboxAccountAsync` (fallback to generic `CashboxMain`).
   - Emits `AffiliateCommissionPaidEvent` per order `:714-727` (`TenantId = storeTenantId`, `StoreTenantId = storeTenantId`).

2. **Event-driven bridge (AF-14 / AF-14C) consumers:** `AffiliateAccountingConsumer.cs` posts both legs:
   - **Earned:** `Debit 6110 Affiliate Marketing Expense`, `Credit 2105 Affiliate Payable` — `AffiliateCommissionEarnedAccountingConsumer.cs:143-152`. Idempotency key `AFFILIATE_COMM_EARNED:{tenantId}:{orderId}` `:130`. Uses `evt.StoreTenantId` for accounting (`AffiliateCommissionEarnedAccountingConsumer.cs:73, :106`).
   - **Paid:** `Debit 2105 Affiliate Payable`, `Credit 1010 Cash in Hand` (or cashbox account) — `AffiliateCommissionPaidAccountingConsumer.cs:347-356`. Idempotency key `AFFILIATE_COMM_PAID:{tenantId}:{orderId}:{settlementReferenceId}` `:334`.
   - **Out-of-order guard (AF-14C):** Paid journal is **blocked** unless the Earned journal already exists (`ExistsByIdempotencyKeyAsync` check `:278-294`) — prevents negative payable.

### Treasury / external-transfer evidence (HMAC reconciliation)
- `AffiliatePayoutEvidenceService.RegisterVerifiedEvidenceAsync` records external transfer evidence and **verifies a SHA-256 HMAC signature** before trusting it:
  - `VerifySignature(...)` builds canonical payload and validates against `_options.ExternalEvidenceHmacSecret` (must be ≥32 chars) `AffiliatePayoutEvidenceService.cs:138-164`; `const TrustedVerificationSource = "HMAC_RECONCILIATION"` `:15`.
  - Currency must match the **store base currency** (`_db.Tenants` read) `:59-69`.
- The orchestrator refuses payout unless verified evidence is allowed: `EnsureVerifiedExternalPayoutEvidenceAllowedAsync(...)` called before the transaction `AffiliateSettlementOrchestrator.cs:135-141`; `AllocateExternalTransferEvidenceAsync` writes the evidence row `:380-390`. Reference must be unique across paid requests `:267-283`.

### Authority summary
- Affiliate **owns** `AffiliateCommissionLedger` / `AffiliateBalanceLedger` (monetary truth for commissions).
- Treasury/Accounting **owns** the GL. Affiliate reaches it only via `IFinancialPostingService` (serializable posting) and `IAccountResolver`/`ICashboxLedgerAccountService` — never direct GL writes.

---

## 5. Notifications / Identity / SuperAdmin Boundary

### Notifications
- **No `INotificationService` is injected anywhere in Affiliate code** (repo-wide grep for `INotificationService` → 0 matches). Affiliate notifications are **direct SMTP emails**, not the notification service abstraction.
- Emitted from `AffiliateAuthController` (identity lifecycle only — not payout/approval):
  - **Email verification:** `AffiliateAuthController.cs:591-595` (`SendEmailAsync(affiliateUser.Email, "Verify your affiliate account", …)`).
  - **Password reset:** `AffiliateAuthController.cs:606-610`.
- **No payout-approved / payout-executed email** is sent by affiliate code (grep for payout+email in affiliate scope → none). Payout completion is communicated via the `AffiliateCommissionPaidEvent` (consumed by Accounting; see §4), not by email to the affiliate.
- Merchant/WhatsApp payment notifications (`IWhatsAppPaymentNotificationService`) are a **separate domain** and not fired by affiliate.

### Identity (AffiliateUser — separate JWT)
- `AffiliateUser` is an **independent identity, decoupled from POS `User`**: *"AffiliateUser ≠ POS User: separate JWT, separate auth boundary"* — `AffiliateUser.cs:5-18` (KILL LIST: *"Never shares JWT with POS app", "Never inherits from User entity"*).
- 1:1 with `AffiliateAccount` (`AffiliateUser.cs:9, :37-45`); email unique per `(TenantId, Email)` (legacy tenant scoping retained for identity even though account is global — `AffiliateAuthController.cs:50` documents that StoreSlug is no longer required for auth, AF-19).
- JWT issued under a **dedicated affiliate issuer**: `Program.cs:2737` (`ValidIssuer = affiliateJwtSettings.Issuer`); governed by `SuperAdminSecurityConstants.PolicyName` for category mutations.

### SuperAdmin (governance)
- **`AdminAffiliateCategoryController`** owns the **global affiliate category taxonomy** (AF-30 / AF-30A):
  - Global, hierarchical (Main → Sub, max depth 2), no soft deletes (IsActive toggle only) — `AdminAffiliateCategoryController.cs:12-33`.
  - **READ** endpoints: any `Owner`/`Admin` (`[Authorize(Roles = "Owner,Admin")]` `:61, :117`).
  - **MUTATION** endpoints (create/update/activate/deactivate): **Super Admin ONLY** via `[Authorize(Policy = SuperAdminSecurityConstants.PolicyName)]` `:149, :198, :258, :377, :442`.
  - Writes `AffiliateCategories` and emits `AffiliateCategoryUpdatedEvent` / `AffiliateCategoryDeactivatedEvent` for MongoDB projection sync (`:321, :410, :459`). On publish failure it logs `PROJECTION-DRIFT` (critical) — `:341-345, :428-432, :472-476`.
- Category is a **hard gate** before a product can be affiliate-enabled: `AffiliateProductPolicy` requires a non-null, active `AffiliateCategoryId` (`AffiliateProductPolicy.cs:45-49, :95-117`; `AffiliateService.cs:206-238, :251-256`). Thus SuperAdmin taxonomy authority flows down into Merchant product enablement.

---

## Boundary Summary Table

| Domain | Affiliate READs | Affiliate WRITEs | Key Events | Authority |
|---|---|---|---|---|
| Merchant | `Products` (existence/tenant), `Orders`+`OrderItems` (snapshots), `Tenants` (currency) | none to Merchant tables; owns `AffiliateProductPolicies`, `AffiliateCommissionLedger` | in: `AffiliateOrderPlacedEvent`; out: `AffiliateCommissionEarnedEvent`, `AffiliateProductPolicyChangedEvent` | **derivative** on product/order; **authoritative** on policy rate (tenant-owned) |
| CommerceHub/Marketplace | `CommerceHubPaymentCompensationOutboxMessages`, `CommerceHubUnresolvedPaymentWebhooks`, `MarketplaceEventOutboxMessage` (readiness only) | none | `AffiliateCommissionEarnedEvent` carries `StoreTenantId` + `AffiliateAuthorityTenantId` | **global/tenant-unbound**; `StoreTenantId` = store that owes commission |
| Payments | `Orders.PaymentStatus`, `FinancialPostingStatus` (gating) | none | in: `OnlinePaymentCapturedEvent`, `OrderCodDeliveredEvent`, `WalletPaymentApprovedEvent`, `OrderCollectionAccountingPostedEvent`; out: `AffiliateCommissionEarnedEvent` | **reactive** — only records post-capture |
| Treasury/Accounting | `Tenants` (currency), GL accounts via `IAccountResolver`/`ICashboxLedgerAccountService` | owns sub-ledgers; posts GL via `IFinancialPostingService` | out: `AffiliateCommissionPaidEvent`, journal postings | **owns** sub-ledger; **derivative** of GL via services |
| Notifications/Identity/SuperAdmin | `AffiliateUser`, `AffiliateCategories` (SuperAdmin) | `AffiliateUser` (email tokens), `AffiliateCategories` | out: verification/reset emails, `AffiliateCategory*Event` | separate JWT; SuperAdmin owns taxonomy authority |
