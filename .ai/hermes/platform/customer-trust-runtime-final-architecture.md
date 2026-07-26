# Customer Trust / Moderation / Risk Runtime — Implementation-Backed Canonical Architecture

> **Authority rule:** Implementation is the ONLY source of truth. Every claim below is cited to a
> file:line in the live `PointOfSale` codebase. Where names ("Customer Trust", "Moderation",
> "Reputation") imply a capability that is NOT actually implemented, the gap is reported as a
> discrepancy rather than invented.

---

## Executive Summary

Shopixy does **not** have a single "Customer Trust Runtime" in the marketing sense. Reverse-engineering the
implementation reveals **two cooperating subsystems** that together cover trust / risk / moderation / abuse
evidence:

1. **Phone Risk Runtime** (`PointOfSale.API/Services/PhoneRisk/`, controller `api/admin/phones`) — a
   **tenant-scoped, merchant-operated** block/spam subsystem. This is the closest thing to "blocked
   customer / blocked phone / abuse report" in the codebase. Its domain is the **phone number**, not the
   customer identity. It supports **explicit merchant block/unblock only** (per its own XML doc:
   `PhoneRiskProfile.cs:5-8`) — *not* automatic risk scoring, *not* reputation aggregation.

2. **User Governance Runtime** (`PointOfSale.API/Services/Governance/`, controller
   `api/super-admin/users`) — a **cross-tenant, Super-Admin-operated** governance/risk subsystem. This is
   the real "risk evaluation / moderation decision / audit trail" engine. It computes a risk level
   (Low/Medium/High) from CommerceHub order/refund/return/debt aggregates, but **the risk score is
   advisory read-only**; durable enforcement is done through a **status state machine**
   (Active → Suspended → Banned → FinanciallyLocked) and a **Super-Admin blacklist** of email/phone/IP/device.

**Classification (evidence-backed):** This is a **Hybrid Governance + Phone-Risk Runtime** — primarily a
**Moderation & Risk Runtime** with a **tenant-scoped Phone-Block sub-runtime**, wrapped by a
**cross-tenant Super-Admin governance authority**. It is **NOT** a customer-reputation-scoring product:
there is no trust score, no weighted multi-admin aggregation, no automatic warnings, no appeals workflow, no
telemetry event bus for trust changes. See "Runtime Classification" and "Documentation Validation".

---

## Architecture Vision

The system protects three parties without owning their core data:
- **Merchants (POS Admin)** block abusive phone numbers at the storefront checkout boundary
  (`CheckoutService.cs:980`, `StorefrontAuthService.cs:211/395`).
- **Super Admin** governs platform participants (CommerceHub customers, vendors, Super Admins) via risk
  evaluation + status actions + blacklist, enforced at login/checkout/refund/payout boundaries.
- **Identity / Orders / Customer runtimes keep their authority** — governance only *gates* them through
  `Ensure*AllowedAsync` guard methods; it never writes an Order, Customer, or payment record.

---

## Runtime Purpose

- **Phone Risk:** let a merchant explicitly block a phone number, unblock it, and record spam reports
  tied to an order, so abusive customers are stopped at storefront registration / login / checkout
  (`AdminPhonesController.cs`, `PhoneRiskProfileService.cs`).
- **User Governance:** let Super Admin evaluate participant risk from live CommerceHub aggregates, apply
  governance status (suspend/ban/financial-lock/activate), and maintain a denylist of
  email/phone/IP/device signals that blocks registration and login platform-wide
  (`IUserGovernanceService.cs`, `UserGovernanceService.cs`, `SuperAdminUsersController.cs`).

## Runtime Scope

| Subsystem | Scope | Authority Boundary |
|---|---|---|
| Phone Risk | Per-tenant phone numbers only (`PhoneRiskProfile.TenantId`, `PhoneRiskProfileConfiguration.cs:15`) | Merchant (Owner/Admin/Manager) via `api/admin/phones` |
| User Governance | Platform participants: Customer, Vendor, Admin (`UserGovernanceRole.cs`) | Super Admin only via `api/super-admin/users` |
| Global Phone Reputation | Cross-tenant, read-only warning intelligence (`GlobalPhoneReputation.cs:3-6`) | **Never** used for blocking |

## Runtime Owns

- `PhoneRiskProfile` (blocked phone, reason, who/when) — `PhoneRiskProfile.cs`
- `PhoneSpamReport` (immutable per-order spam record) — `PhoneSpamReport.cs`
- `GlobalPhoneReputation` (cross-tenant spam count, warning-only) — `GlobalPhoneReputation.cs`
- `UserGovernanceProfile` (participant risk level, status, KYC, marketplace approval, audit fields) — `UserGovernanceProfile.cs`
- `UserGovernanceBlacklistEntry` (denylist of email/phone/IP/device) — `UserGovernanceBlacklistEntry.cs`
- `UserGovernanceAuditLog` (append-only moderation timeline) — `UserGovernanceAuditLog.cs`

## Runtime Does NOT Own

- **Customer identity** — `UserGovernanceService.Evaluation.cs:383` resolves subjects from
  `CommerceHubCustomers` / `Tenants` / `SuperAdmins` (read-only). Governance profile is a *sidecar*.
- **Orders / Payments / Refunds / Returns** — it only *aggregates* them for risk signals
  (`UserGovernanceService.Aggregates.cs:25-102`) and *gates* their operations.
- **Inventory / Catalog / Pricing / Shipping** — no authority.
- **Reputation scoring / trust score / weighted aggregation** — not implemented (see discrepancies).
- **Automatic blocking** — phone blocking is explicit-merchant-only (`PhoneRiskProfile.cs:5-8`); governance
  enforcement of Suspended/Banned is manual Super-Admin action, except one auto path:
  vendor debt → FinanciallyLocked (`UserGovernanceService.Evaluation.cs:150-177`).

---

## Architecture Principles

1. **SQL is the single source of truth.** All six governance/phone entities are EF Core tables
   (`ApplicationDbContext.cs:141-143, 365-371`). No Mongo/Elastic read models for this capability were
   found (grep of `PointOfSale.Infrastructure/MongoDB`, `Elasticsearch` returned none).
2. **No event bus / outbox for this capability.** A grep for governance/phone integration events
   (`PhoneBlocked`, `CustomerBanned`, `GovernanceEvent`, `PhoneSpamReported`) returned only the entity
   definitions, not event/consumer classes. Enforcement is **synchronous and inline** at call sites.
3. **Tenant isolation on Phone Risk.** `PhoneRiskProfiles` is `ITenantOwned`
   (`PhoneRiskProfile.cs:9`); unique `(TenantId, PhoneNumber)` (`PhoneRiskProfileConfiguration.cs:43-45`).
   `GlobalPhoneReputation` is intentionally **tenant-less** (cross-tenant warning).
4. **Super-Admin authority isolation.** Every governance mutation requires `CurrentSuperAdminId`
   (`SuperAdminUsersController.cs:357, 413, 506, 534`). System/admin self-protection guards exist
   (`UserGovernanceService.cs:573-589`).
5. **Fail-closed gating.** Guard methods throw `UserGovernanceException` (403/409) when status blocks the
   operation; they never silently allow (`UserGovernanceService.cs:131-231`).
6. **Append-only evidence.** `UserGovernanceAuditLog` cannot be updated/deleted
   (`ApplicationDbContext.cs:6116-6127`). `PhoneSpamReport` is immutable, one per `(TenantId, OrderId)`
   (`PhoneSpamReportConfiguration.cs:30-32`).
7. **Risk is advisory; status is authoritative.** `BuildEvaluation` computes `RiskLevel`
   (`UserGovernanceService.Evaluation.cs:286-299`) but the persisted `profile.RiskLevel` is overwritten by
   evaluation on every read (`SyncProfileWithEvaluation`, `Evaluation.cs:344-369`); blocking depends on
   `Status`, not `RiskLevel`.

---

## Core Discovery (implementation definitions)

| Concept | Implementation | Evidence |
|---|---|---|
| **Blocked Phone** | `PhoneRiskProfile.IsBlocked=true` for a tenant+normalized phone | `PhoneRiskProfile.cs:15`, `PhoneRiskProfileService.BlockAsync:112-153` |
| **Blocked Customer** | No customer-block entity exists. Equivalent = `UserGovernanceProfile.Status` ∈ {Suspended, Banned} on a `CommerceHubCustomer` subject | `UserGovernanceProfile.cs:10`, `UserGovernanceStatus.cs` |
| **Spam** | `PhoneSpamReport` row per order; increments `PhoneRiskProfile.SpamReports` (tenant) and `GlobalPhoneReputation.SpamReports` (global) | `PhoneRiskProfileService.ReportSpamAsync:184-278` |
| **Abuse Report** | Implemented **only as phone spam** tied to an order. No generic "abuse report" entity. | `PhoneSpamReport.cs`; grep for `AbuseReport` → 0 hits |
| **Customer Reputation** | Not implemented as a score. Only `RiskLevel` Low/Medium/High (advisory) and `SuspiciousFlags` count. | `UserGovernanceProfile.cs:11,20`; `Evaluation.cs:286-299` |
| **Customer Warning** | Not implemented. No warning entity/state/threshold exists. | grep `Warning` in governance/phone → 0 runtime hits |
| **Customer Risk** | `UserGovernanceRiskLevel` Low/Medium/High, derived from aggregates. Advisory only. | `UserGovernanceRiskLevel.cs`, `Evaluation.cs:286-299` |
| **Customer Trust** | Not implemented as a trust score/level. No "Trust" concept in code. | grep `TrustScore|TrustLevel` → 0 |

---

## Customer Trust Runtime (requested pipeline)

The requested pipeline (Customer → Order History → Reports → Evidence → Aggregation → Risk Evaluation →
Trust Decision → Warning → Manual Review → Admin Decision) maps **partially**:

- **Customer / Order History / Reports / Evidence** → present as aggregates + spam reports + audit log.
- **Aggregation** → `UserGovernanceService.Aggregates.cs` (orders/refunds/returns/vendor debt).
- **Risk Evaluation** → `UserGovernanceService.Evaluation.cs:200-342` (thresholds → Low/Med/High).
- **Trust Decision** → **not implemented** as automatic trust. Decision is manual Super-Admin status change.
- **Warning** → **not implemented**.
- **Manual Review / Admin Decision** → present via `ApplyActionAsync`
  (`UserGovernanceService.cs:534-660`) + blacklist upsert/revoke.

So the pipeline terminates at *Admin Decision*; there is no automated trust/warning stage.

---

## Runtime Authority

| Concern | Authoritative owner (implementation) |
|---|---|
| Blocked Phones | `PhoneRiskProfileService` + `PhoneRiskProfiles` table (per tenant) |
| Blocked Customers | `UserGovernanceService` + `UserGovernanceProfile.Status` (Super Admin) |
| Spam Reports | `PhoneRiskProfileService.ReportSpamAsync` + `PhoneSpamReports` + `GlobalPhoneReputations` |
| Warnings | **No owner** — not implemented |
| Risk Level | Computed by `UserGovernanceService.Evaluation.BuildEvaluation`; persisted on `UserGovernanceProfile.RiskLevel` (advisory) |
| Trust Level | **No implementation** |
| Customer Reputation | Summarised only as `RiskLevel` + `SuspiciousFlags`; no score table |
| Customer Notes | `UserGovernanceProfile.StatusReason` / `LastRiskReason` (free text, admin-set) |
| Manual Blocks | `PhoneRiskProfileService.BlockAsync` (phone) and `ApplyActionAsync(Banned)` (customer/vendor/admin) |
| Automatic Warnings | **None** |
| Fraud Indicators | Derived signals in `BuildEvaluation` (`Evaluation.cs:243-284`), stored as `LastRiskReason` string |
| Evidence | `UserGovernanceAuditLog` (moderation) + `PhoneSpamReport` (immutable spam record) |

---

## Blocking Runtime

| Block type | Implementation | Evidence |
|---|---|---|
| Manual Block (phone) | `PhoneRiskProfileService.BlockAsync` sets `IsBlocked=true` | `PhoneRiskProfileService.cs:112-153` |
| Phone Block | Same as above; enforced at `CheckoutService.cs:980`, `StorefrontAuthService.cs:211/395`, `StorefrontRefreshTokenService.cs:214/475`, `StorefrontAccountController.cs:128` |
| Customer Block | `ApplyActionAsync(..., Banned/Suspended)` on `UserGovernanceProfile.Status` | `SuperAdminUsersController.cs:162-210` |
| Temporary Block | **Not implemented** — no expiry column on `PhoneRiskProfile` or `UserGovernanceProfile`. Blocks are indefinite until explicit unblock/activate. |
| Permanent Block | Same as manual; no expiry = effectively permanent |
| Automatic Block | Phone: **none** (explicit only, `PhoneRiskProfile.cs:5-8`). Governance: only auto **FinanciallyLocked** for vendors by debt (`Evaluation.cs:150-177`). |
| Appeal | **Not implemented** — no appeal entity/endpoint |
| Unblock | `PhoneRiskProfileService.UnblockAsync` (`PhoneRiskProfileService.cs:155-182`); governance `activate`/`financial-unlock` (`SuperAdminUsersController.cs:182-210`) |
| Expiration | **Not implemented** |

---

## Reputation Runtime

The requested reputation signals map as follows:

| Signal | Implemented? | Where |
|---|---|---|
| Spam Reports | Yes (phone) | `PhoneSpamReport`, `GlobalPhoneReputation` |
| Abuse Reports | Partial — only phone spam tied to order | `PhoneSpamReport.cs` |
| Delivery Refusals / No Answer / Fake Orders | **No** dedicated signal | not found |
| Repeated Cancellations / Returns | Return count aggregated (`ReturnAggregate`), but no *cancellation* aggregate | `Aggregates.cs:80-102` |
| Repeated Abuse | Only phone spam repetition | `PhoneRiskProfileService.ReportSpamAsync` |
| Admin Reports | Modeled as governance actions + audit log, not "reports" | `UserGovernanceAuditLog.cs` |
| Community Reports | **No** — single merchant reports spam per order; no community/multi-reporter model |
| Risk Indicators | `BuildEvaluation` signals: refund rate >40%, vendor debt over threshold, failed payments ≥3, rapid refunds ≥3 in 7d, dispute rate ≥25%, blacklist match | `Evaluation.cs:19-22, 243-284` |

There is **no weighted, multi-administrator, confidence-scored reputation engine**. Reputation = one
advisory `RiskLevel` recomputed on read.

---

## Trust Aggregation Runtime

Requested: Report Aggregation, Thresholds, Weighting, Confidence, Trust Score, Risk Score, Warning
Threshold, Escalation.

- **Report Aggregation:** spam counts (`PhoneSpamReports` count per phone, `Evaluation.cs:219-225`);
  order/refund/return counts per customer/vendor (`Aggregates.cs`).
- **Thresholds (hard-coded constants):** `HighRefundRateThreshold=40`, `HighDisputeRateThreshold=25`,
  `MediumFailedPaymentsThreshold=3`, `HighVelocityRefundThreshold=3`
  (`UserGovernanceService.cs:19-22`).
- **Weighting:** none — signals are OR-combined into High/Medium/Low (`Evaluation.cs:286-299`). No weights.
- **Confidence:** none.
- **Trust Score / Risk Score:** `RiskLevel` enum only; no numeric score.
- **Warning Threshold / Escalation:** **not implemented** (no warning stage).

---

## Moderation Runtime

- **Admin Moderation:** `ApplyActionAsync` (Suspend/Ban/Activate/FinancialLock/Unlock) under
  `GovernanceWriteRateLimitPolicyName` (`SuperAdminUsersController.cs:162-210, 350-405`).
- **Manual Decision:** Super Admin sets `Status` + `StatusReason`.
- **Evidence Review:** `GetUserDetailsAsync` returns evaluation + last 50 audit items
  (`UserGovernanceService.cs:500-516`, `AuditWindow=50` const `:23`).
- **Override:** blacklist upsert/revoke (`SuperAdminUsersController.cs:288-348`).
- **Appeal:** **not implemented**.
- **Reason Codes:** free-text `Reason` (`NormalizeReason`, `Mappings.cs:431`); no fixed reason-code enum.
- **Comments / Notes:** `StatusReason`, `LastRiskReason`, `MarketplaceApprovalReason`,
  `KycVerificationReason` free text.

---

## Risk Runtime

| Risk class | How determined | Evidence |
|---|---|---|
| High Risk | Refund rate>40%, vendor debt≥threshold, rapid refunds, or blacklist match | `Evaluation.cs:287-294` |
| Medium Risk | Failed payments≥3 or dispute rate≥25% | `Evaluation.cs:295-299` |
| Low Risk | default | `Evaluation.cs:286` |
| Trusted Customer | `Status=Active` and `RiskLevel=Low` (implicit; no "Trusted" state) | — |
| Blocked Customer | `Status∈{Suspended,Banned}` or phone `IsBlocked` | — |
| Spam Customer | phone with `SpamReports>0` / global reputation count | `PhoneRiskProfile.SpamReports` |
| Suspicious Customer | `SuspiciousFlags>0` (`LastRiskReason` non-empty) | `Evaluation.cs:317, 353-357` |

---

## Order Runtime Integration

- **Order Creation / Validation:** `CheckoutService` calls phone-block check at step 2.9 and throws
  `CheckoutPhoneBlockedException` → order rejected (`CheckoutService.cs:974-1001`). CommerceHub checkout
  calls `EnsureCustomerCheckoutAllowedAsync` → 403/409 on Suspended/Banned
  (`CommerceHubCheckoutService.cs:155`).
- **Warning Display:** **not implemented** — no warning surfaced to admin at order time.
- **Admin Notification:** **not implemented** for trust (no event/notification path found).
- **Approval / Rejection:** rejection is the only automated outcome (phone block, governance status).
- **Block Enforcement:** inline guards; no async projection.

## Storefront Runtime Integration

- **Customer Login:** JWT middleware calls `EnsureCustomerLoginAllowedAsync`
  (`Program.cs:2561`); banned/suspended → `context.Fail` → 403.
- **Guest Checkout:** phone normalized + `IsBlockedAsync` (`CheckoutService.cs:975-983`).
- **Phone Validation:** `NormalizePhoneAsync` (E.164-ish via `IPhoneNumberNormalizer`, fallback digit
  strip) (`PhoneRiskProfileService.cs:25-55, 388-418`).
- **Blocked Customer Detection:** at registration (`StorefrontAuthService.cs:211`),
  login (`StorefrontAuthService.cs:395`), token refresh (`StorefrontRefreshTokenService.cs:214/475`),
  profile update (`StorefrontAccountController.cs:128`), CommerceHub auth
  (`CommerceHubCustomerAuthService.cs:96, 213`).
- **Error Handling:** returns `PHONE_BLOCKED` / `CUSTOMER_BANNED` / `CUSTOMER_SUSPENDED` (409/403).
- **Recovery:** unblock endpoint; no automatic recovery.

## Identity Runtime Integration

- **Customer Identity:** `CommerceHubCustomers` is the *source* of customer subject
  (`Evaluation.cs:383-398`); governance profile is a sidecar keyed by `SubjectId`.
- **Phone Number:** normalized + stored on `PhoneRiskProfile`; NOT on customer identity table for blocking.
- **Email:** used in blacklist (`UserGovernanceBlacklistType.Email`) and registration gate.
- **Multiple Accounts / Duplicate Phones / Guest Identity:** no dedup logic in governance; phone block is
  by normalized string only.

## Analytics Runtime

- **No analytics/reporting module** is wired to this capability (grep of
  `AdminAnalytics*`, `AdminReporting*`, `Infrastructure/Analytics` → 0 hits).
- **Admin visibility:** `ListBlockedAsync` paged list of blocked phones
  (`PhoneRiskProfileService.cs:280-351`); Super Admin `GetUsers` paged governed-user list
  (`SuperAdminUsersController.cs:29-104`).
- "Top Reported Customers / Repeated Offenders / Moderation Statistics" are **not implemented** as
  dedicated analytics.

## Event Runtime

- **No integration events** for `CustomerBlocked`, `CustomerUnblocked`, `SpamReported`, `WarningCreated`,
  `RiskIncreased`, etc. (grep → only entity defs). All effects are synchronous DB writes.
- The only cross-service *read* coupling is `ServiceDiscoveryProjectionConsumer.cs:308-351`, which reads
  `UserGovernanceProfiles` (vendor Suspended/Banned) to decide CommerceHub service visibility — a
  projection *consumer* reading governance state, not an event the governance runtime emits.

## Evidence Runtime

- **Evidence Storage:** `UserGovernanceAuditLog` (append-only JSON before/after snapshots,
  `UserGovernanceAuditLog.cs`; enforced immutable `ApplicationDbContext.cs:6116-6127`) +
  `PhoneSpamReport` (immutable per order).
- **Report History / Reason History / Admin History / Audit Trail / Timeline:** all in
  `UserGovernanceAuditLog` (action type, actor, reason, before/after state, timestamp).

## Customer Timeline Runtime

- **Reports / Warnings / Orders / Blocks / Appeals / Risk Changes / Moderation Actions:**
  Reports (spam) + Blocks (phone/status) + Risk Changes (persisted on evaluation) + Moderation Actions
  (audit log) exist. **Warnings and Appeals do not.** Timeline = `UserGovernanceAuditLog` ordered by
  `TimestampUtc desc, Id desc` (`UserGovernanceService.cs:500-514`).

## Persistence Runtime

- **SQL (authoritative):** 6 tables — `PhoneRiskProfiles`, `GlobalPhoneReputation`, `PhoneSpamReports`,
  `UserGovernanceProfiles`, `UserGovernanceBlacklistEntries`, `UserGovernanceAuditLogs`
  (`ApplicationDbContext.cs:141-143, 365-371`).
- **MongoDB:** none for this capability.
- **Redis:** `OrderVelocityService` tracks order velocity (IP/phone counts, 300s window, thresholds 5/3)
  in Redis — a *soft* abuse signal, **logged but not enforced** (grep shows it is read for assessment only;
  no guard throws on velocity). `OrderVelocityService.cs:1-126`.
- **Read Models / Write Models / Snapshots / Replay / Outbox / Inbox:** none for governance/phone.

## CQRS

- **Commands:** controller actions (block/unblock/report-spam; suspend/ban/activate/financial-lock;
  blacklist upsert/revoke).
- **Queries:** `GetStatusAsync`, `ListBlockedAsync`, `GetUsersAsync`, `GetUserDetailsAsync`,
  `GetRiskAsync`, `GetBlacklistAsync` — all direct SQL `AsNoTracking`.
- **Events / Projections / Replay / Repair / Outbox / Inbox:** **none implemented** for this capability.
  This is a **CRUD + synchronous guard** architecture, not CQRS.

---

## Runtime Authority Matrix

| Concern | Owner (impl) | Writes truth? |
|---|---|---|
| Customer | `CommerceHubCustomers` (Identity runtime) | No — governance reads only |
| Identity | Identity runtime | No |
| Phone | `PhoneRiskProfile` (per tenant) | Yes (block/spam) |
| Blocked Phone | `PhoneRiskProfileService` | Yes |
| Blocked Customer | `UserGovernanceService` (Super Admin) | Yes (status) |
| Spam Reports | `PhoneRiskProfileService` | Yes |
| Evidence | `UserGovernanceAuditLog` / `PhoneSpamReport` | Yes (immutable) |
| Risk | `UserGovernanceService.Evaluation` | Yes (advisory, recomputed) |
| Trust | — | **Not implemented** |
| Moderation | `UserGovernanceService.ApplyActionAsync` | Yes |
| Warnings | — | **Not implemented** |
| Order Decision | Orders runtime (enforced via governance guard) | No — governance only gates |

---

## Cross Runtime Authority Graph

- **Storefront:** consumes `IsBlockedAsync` (phone) + `EnsureCustomerLoginAllowedAsync` + governance
  blacklist at registration/login/checkout/profile-update.
- **Identity:** governance resolves subjects from Identity tables; never writes them.
- **Catalog / Inventory / Pricing / Cart / Checkout:** checkout enforces phone block; otherwise no
  governance authority.
- **Orders:** gated by `EnsureCustomerCheckoutAllowedAsync` / `EnsureVendorOrder*AllowedAsync`.
- **Payments:** no direct governance coupling (financial lock is a status, not a payment write).
- **Shipping / Tracking:** no coupling.
- **Analytics / Notifications / Reporting:** no governance module (analytics absent; notifications absent).
- **Recovery Runtime:** not coupled.
- **AI Runtime:** not coupled.
- **CommerceHub:** `CommerceHubCheckoutService`, `CommerceHubCustomerAuthService` call governance guards;
  `ServiceDiscoveryProjectionConsumer` reads vendor governance status for visibility.
- **Builder (Storefront Builder):** no governance coupling found.

## Business Invariants (implementation-backed)

1. **Blocked phones must block storefront registration, login, token refresh, profile update, and
   checkout** for that tenant+phone — enforced at 6 call sites
   (`StorefrontAuthService.cs:211/395`, `StorefrontRefreshTokenService.cs:214/475`,
   `StorefrontAccountController.cs:128`, `CheckoutService.cs:980`).
2. **One spam report per tenant+order** — unique index `(TenantId, OrderId)`
   (`PhoneSpamReportConfiguration.cs:30-32`); repeated button presses idempotent
   (`PhoneRiskProfileService.cs:195-217`).
3. **Global phone reputation is warning-only, never auto-blocking** — `GlobalPhoneReputation.cs:3-6`;
   `GetGlobalSpamReportsAsync` is read-only, never flips `IsBlocked`.
4. **Banned/Suspended customers cannot log in or checkout** — `EnsureCustomerLoginAllowedAsync`
   (`:149-174`), `EnsureCustomerCheckoutAllowedAsync` (`:131-147`).
5. **System Super Admin is protected** — cannot be suspended/banned/locked
   (`UserGovernanceService.cs:583-589`); self-ban forbidden (`:573-581`).
6. **Audit log is append-only** — update/delete blocked (`ApplicationDbContext.cs:6116-6127`).
7. **Governance actions require Super-Admin identity** — `CurrentSuperAdminId` guard in controller
   (`SuperAdminUsersController.cs:357` et al.).
8. **Blacklisted signals block registration** — email/phone/IP/device match → 409
   (`EnsureRegistrationAllowedAsync:89-129`).
9. **Vendor debt auto-financial-lock** — only automatic status transition in the system
   (`Evaluation.cs:150-177`); runs under `AllowAutoFinancialLock`.
10. **Phone normalization is mandatory** before any block/spam compare
    (`NormalizePhoneAsync`, `PhoneRiskProfileService.cs:25-55`) — prevents bypass via formatting.

---

## Runtime State Machines

### Phone Risk (per tenant+phone)
```
(unknown)
   └─ BlockAsync ──────────────► Blocked (IsBlocked=true, reason, by, at)
                                   │
                                   └─ UnblockAsync ─► Blocked=false (stays as profile, SpamReports retained)
   ReportSpamAsync ──► increments SpamReports (tenant) + GlobalPhoneReputation.SpamReports (global)
```
No expiry, no appeal, no warning states.

### User Governance (per subject = customer/vendor/admin)
```
Active ──(Suspend)──► Suspended ──(Ban)──► Banned
   ▲                      │                  │
   └────(Activate)────────┘──────────────────┘
Active/Suspended/Banned ──(FinancialLock, vendor debt auto or admin)──► FinanciallyLocked
FinanciallyLocked ──(FinancialUnlock/Activate)──► Active
```
Status drives enforcement; `RiskLevel` is recomputed on every evaluation but does NOT drive transitions
(`SyncProfileWithEvaluation`, `Evaluation.cs:344-369`).

---

## Runtime Timeline (one complete customer journey)

Step: **Customer places CommerceHub order**
- Owner: Orders runtime. Governance hook: `CommerceHubCheckoutService.cs:155`
  `EnsureCustomerCheckoutAllowedAsync`.
- Authority: Orders owns the order; governance only *reads* status.
- Synchronization: synchronous, same request.
- Consistency: read `UserGovernanceProfile.Status`; if Suspended/Banned → 403/409, order aborted.
- Events: none.
- Persistence: no governance write.
- Failure/Retry: exception propagates as API error; client retries blocked.

Step: **Admin reports Spam (per order, per phone)**
- Owner: `PhoneRiskProfileService.ReportSpamAsync`.
- Authority: writes `PhoneSpamReports` + updates `PhoneRiskProfiles.SpamReports` + `GlobalPhoneReputations`.
- Synchronization: same request; idempotent on `(TenantId, OrderId)`.
- Consistency: SQL commit; global count updated too.
- Events: none.
- Persistence: 3 tables updated in one `SaveChangesAsync`.
- Failure: `DbUpdateException` on duplicate → `ChangeTracker.Clear()`, still counts existing
  (`PhoneRiskProfileService.cs:207-217`).

Step: **Evidence stored**
- `PhoneSpamReport` immutable per order; `UserGovernanceAuditLog` only on governance *status* actions
  (not on spam).

Step: **Aggregation / Threshold**
- Phone spam: simple count, no threshold for auto-action.
- Customer risk: `BuildEvaluation` aggregates orders/refunds/returns; thresholds
  (`:19-22, 243-284`) → RiskLevel.

Step: **Warning Created**
- **Not implemented.** Journey diverges from the requested pipeline here.

Step: **Customer places another order / Admin sees risk**
- Admin opens `GET /api/super-admin/users/{id}` or `/risk`; risk shown, but **no automatic warning
  surfaced at order time**.

Step: **Decision (Admin)**
- Super Admin `POST /api/super-admin/users/{id}/ban` (or suspend) → `ApplyActionAsync` writes
  `Status=Banned`, appends `UserGovernanceAuditLog`, rate-limited, requires Super-Admin identity.

Step: **Order Accepted or Rejected**
- Future `EnsureCustomerCheckoutAllowedAsync` / `EnsureCustomerLoginAllowedAsync` now throw → rejected.
- No appeal; only `activate` reverses.

Step: **Appeal → Unblock**
- **Appeal not implemented.** Unblock = admin `activate` (governance) or merchant `unblock` (phone).

---

## Runtime Classification (evidence-backed)

This runtime is fundamentally a **Hybrid Moderation + Risk Governance Runtime with a tenant-scoped
Phone-Block sub-runtime**. Supporting evidence:

- **Moderation:** yes — `ApplyActionAsync`, blacklist upsert/revoke, append-only audit log, Super-Admin
  authority isolation. (`SuperAdminUsersController.cs`, `UserGovernanceService.cs`,
  `UserGovernanceAuditLog.cs`)
- **Risk:** yes — `BuildEvaluation` computes Low/Medium/High from aggregates + thresholds
  (`Evaluation.cs`). But advisory only.
- **Identity/Security:** partial — blacklist of email/phone/IP/device blocks registration/login
  (`EnsureRegistrationAllowedAsync:89-129`); JWT middleware enforces customer status (`Program.cs:2561`).
- **Fraud:** weak — `OrderVelocityService` (Redis) tracks velocity but is **not enforced**
  (assessment only).
- **Reputation:** **no** — no score, no weighting, no multi-admin aggregation, no trust level.
- **Trust:** **no** — no trust score/level concept in code.
- **Commerce:** only as a gate on CommerceHub orders/checkout; owns no commerce data.

**Conclusion:** Classify as **Customer Moderation & Risk Governance Runtime (hybrid)**. The phrases
"Customer Trust", "Customer Reputation", "Customer Warning" from the prompt are **not implemented** as
first-class concepts; do not document them as existing capabilities.

---

## Final Architecture Question — answered with citations

**"How does Shopixy evaluate customer trustworthiness, aggregate abuse evidence across administrators,
preserve authoritative moderation decisions, protect storefront merchants from abusive customers, and
integrate trust signals into order management without violating the authority of Identity, Orders, or
Customer runtimes?"**

1. **Evaluate trustworthiness:** It does *not* compute a trust score. It computes an **advisory**
   `UserGovernanceRiskLevel` (Low/Medium/High) from live CommerceHub order/refund/return/debt aggregates
   using hard-coded thresholds (`UserGovernanceService.Evaluation.cs:19-22, 200-342`). For phones, it
   tracks a **spam report count** (`PhoneRiskProfileService.ReportSpamAsync:184-278`).

2. **Aggregate abuse evidence across administrators:** Spam evidence is aggregated per phone across the
   tenant (`PhoneSpamReports` count) and globally (`GlobalPhoneReputation`) — but it is a single
   incrementing counter, **not** a multi-administrator weighted reputation. Moderation evidence across
   admins is captured as an **append-only audit trail** (`UserGovernanceAuditLog`,
   `ApplicationDbContext.cs:6116-6127`), keyed by target + actor + before/after JSON snapshots
   (`UserGovernanceService.cs:634-660`).

3. **Preserve authoritative moderation decisions:** Decisions live in `UserGovernanceProfile.Status`
   (Active/Suspended/Banned/FinanciallyLocked) for platform participants and `PhoneRiskProfile.IsBlocked`
   for tenant phones. Audit log is immutable; status transitions are rate-limited and require Super-Admin
   identity (`SuperAdminUsersController.cs:162-210, 350-405`). System-admin self-protection prevents
   tampering (`UserGovernanceService.cs:573-589`).

4. **Protect storefront merchants from abusive customers:** Merchant blocks a phone via
   `api/admin/phones/block`; the block is enforced at **6 storefront boundaries** — registration, login,
   token refresh, profile update, CommerceHub auth, and checkout
   (`StorefrontAuthService.cs:211/395`, `StorefrontRefreshTokenService.cs:214/475`,
   `StorefrontAccountController.cs:128`, `CommerceHubCustomerAuthService.cs:96/213`,
   `CheckoutService.cs:980`). Super Admin additionally gates CommerceHub checkout/login/refund/payout via
   `Ensure*AllowedAsync` (`UserGovernanceService.cs:131-231`).

5. **Integrate signals into order management without violating Identity/Orders/Customer authority:**
   Governance is a **pure gate**. It never writes an Order, Customer, or payment. At order creation it
   calls `IsBlockedAsync` (phone) and `EnsureCustomerCheckoutAllowedAsync` (status) and throws to reject
   (`CheckoutService.cs:974-1001`, `CommerceHubCheckoutService.cs:155`). Subjects are *resolved read-only*
   from Identity tables (`UserGovernanceService.Evaluation.cs:371-450`). Customer identity remains owned by
   the Identity runtime; Orders remain owned by the Orders runtime; governance only fails the request
   closed when policy forbids it.

**Where implementation contradicts the prompt's assumptions:** there is **no** customer trust score, no
customer reputation engine, no automatic warnings, no appeal workflow, no trust/risk event bus, and no
CQRS/outbox for this capability. The "Customer Trust Runtime" of the prompt is, in implementation, a
**Moderation + Risk Governance Runtime** (Super-Admin) plus a **tenant-scoped Phone-Block/Spam Runtime**
(merchant). Document accordingly; do not attribute non-existent capabilities.

---

## Documentation Validation (discrepancies vs prompt assumptions)

| Prompt assumption | Implementation reality | Evidence |
|---|---|---|
| Customer Trust Runtime exists | No trust concept; only governance + phone-risk | grep `Trust` → 0 |
| Customer Reputation Runtime | Advisory `RiskLevel` only; no reputation score | `UserGovernanceProfile.cs:11` |
| Customer Warning | Not implemented | grep `Warning` → 0 runtime hits |
| Automatic Warnings | None | — |
| Appeal workflow | Not implemented | grep `Appeal` → only `OrderVelocityService` (unrelated) |
| Temporary / Expiring blocks | No expiry columns | `PhoneRiskProfile.cs`, `UserGovernanceProfile.cs` |
| Trust Score / Risk Score numeric | Enum only | `UserGovernanceRiskLevel.cs` |
| Weighted multi-admin aggregation | Single incrementing counter | `PhoneRiskProfileService.ReportSpamAsync` |
| CQRS / Outbox / Events / Projections | None for this capability | grep events → entity defs only |
| Mongo/Elastic read models | None | grep `Infrastructure/MongoDB|Elasticsearch` → 0 |
| Analytics for blocked/spam/risk | No module | grep `AdminAnalytics|Reporting` → 0 |
| Community Reports / multi-reporter | Single merchant-per-order spam report | `PhoneSpamReport.cs` |

---

## Certification Update - 2026-07-22

Implementation-backed review found one Phone Risk isolation drift and one vendor governance error-code
specificity drift. Both were corrected without adding new enterprise capabilities.

### Implementation Changes

- `PhoneRiskProfileService` now scopes `PhoneRiskProfiles` profile lookup, block, unblock, status,
  `IsBlockedAsync`, spam profile update, and blocked-phone listing queries by
  `(TenantId, PhoneNumber)` (`PhoneRiskProfileService.cs:68,86,123,162,228,287`).
- `UserGovernanceService.Mappings.EnsureMarketplaceEligibility` now preserves specific vendor governance
  outcomes before the generic marketplace eligibility fallback: `VENDOR_BANNED`, `VENDOR_SUSPENDED`, and
  `VENDOR_FINANCIAL_LOCKED` (`UserGovernanceService.Mappings.cs:337-357`).
- Regression coverage was added for same-phone cross-tenant blocking/spam isolation
  (`PhoneRiskProfileServiceTests.cs`).
- Existing user-governance tests were aligned with the current marketplace authority invariant that listing
  restoration requires KYC verification and marketplace approval.

### Runtime Classification

Classification remains unchanged: **Hybrid Phone Risk Runtime + User Governance Runtime**. The implementation
still has no customer trust score, weighted reputation engine, warning workflow, appeal workflow, governance
event bus, governance outbox/inbox, Mongo/Elastic read model, or CQRS projection pipeline for this capability.

### Authority Matrix

Authority remains synchronized:

- Identity owns customer identity; governance resolves subjects read-only.
- Orders own orders; governance only gates checkout/order operations.
- Payments own payments; governance only gates payout/refund eligibility.
- CommerceHub owns commerce workflow; governance only supplies synchronous eligibility guards.
- Inventory owns inventory; governance does not mutate stock.
- Governance owns moderation status, blacklist, KYC/marketplace approval, and audit log.
- Phone Risk owns tenant-scoped phone blocking and spam report counters.

### Business Invariants

Certified invariants after correction:

- Blocked phones are tenant-scoped and cannot block another merchant using the same normalized phone.
- Phone normalization still occurs before all phone-risk comparisons.
- Global phone reputation remains advisory only and still does not auto-block.
- Spam reports remain immutable per tenant/order and update tenant/global counts only.
- Suspended/banned governance status remains authoritative over risk level.
- Vendor listing/order guards fail closed with status-specific governance errors before generic marketplace
  eligibility checks.
- Audit log remains append-only; governance profile subject/role ownership remains immutable.

### Technical Debt

- Existing broader architecture/doc freeze tests reference `.ai-context` markers outside this canonical
  Customer Trust document and may fail independently of this runtime.
- Integration tests named `TrustEngineIntegrationTests` target a separate provider trust/review aggregate and
  require the configured PostgreSQL host; they are not evidence of this Customer Trust / Moderation / Risk
  runtime.

### Certification

Certification result for this canonical runtime: **CERTIFIED WITH OBSERVATIONS**.

*Generated by implementation reverse-engineering and updated by implementation-backed certification. Every
claim is scoped to the live `PointOfSale` repository and this canonical Customer Trust / Moderation / Risk
runtime only.*
