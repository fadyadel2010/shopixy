# Shopixy Knowledge Base Cross-System Certification Report

Certification ID: CS-003

Relationship: POS Admin ↔ Affiliate

Status: PASS

Certification Type: Cross-System Relationship Certification

Certification Standard: `.ai/hermes/CROSS_SYSTEM_CERTIFICATION.md`

Knowledge Base Version: v1.0

---

# Certification Summary

Relationship: POS Admin ↔ Affiliate

Status: PASS

The POS Admin ↔ Affiliate relationship is certified because the participating system guides now describe the same architectural relationship from compatible perspectives and preserve the same authority model.

POS Admin is certified as the tenant operational surface for Affiliate administration. Affiliate is certified as the business system for affiliate identity, referral evidence, attribution, commission lifecycle, payout readiness, payout evidence, analytics, notifications, and affiliate-facing operations. Both guides agree that operational intent can originate in POS Admin, but durable affiliate truth is admitted or rejected only by backend SQL/Core/Affiliate authority.

The certified relationship preserves the Shopixy Knowledge Base standards for bidirectional relationship consistency, lifecycle ownership, presentation-vs-authority separation, evidence-vs-authority separation, SQL authority, fail-closed uncertainty, tenant scope, identity isolation, financial readiness, and cross-system references.

This certification records the permanent PASS state for the relationship. It does not introduce findings, corrective actions, implementation guidance, or new business rules.

---

# Scope

This certification validates only the architectural relationship between POS Admin and Affiliate.

It confirms that the POS Admin system guide and Affiliate system guide describe a consistent cross-system model for:

- POS Admin tenant operators using an Affiliate Operational Surface.
- Affiliate operational intent entering backend authority before becoming durable state.
- Affiliate Platform views presenting admitted affiliate state.
- Reverse affiliate evidence flowing back through backend authority before reaching POS Admin.
- Shared Core rules remaining owned by their Core guides rather than duplicated or redefined by either system guide.

This certification does not certify implementation correctness, runtime behavior, endpoint behavior, UI design, database schema, production readiness, test execution, feature completeness, code quality, or future changes.

It also does not certify unrelated relationships such as POS Admin ↔ Storefront, POS Admin ↔ CommerceHub, CommerceHub ↔ Affiliate, Storefront ↔ Affiliate, or Super Admin ↔ Affiliate except where those systems are referenced as boundaries that must not be confused with the certified POS Admin ↔ Affiliate relationship.

---

# Certified Relationship

The certified relationship is bidirectional and operational.

Official forward flow:

```text
POS Admin Tenant Operator
↓
Affiliate Operational Surface
↓
Backend SQL/Core/Affiliate Authority
↓
Affiliate Platform
```

Reverse operational flow:

```text
Affiliate Platform
↓
Backend SQL/Core/Affiliate Authority
↓
Affiliate Operational Surface
↓
POS Admin Tenant Operator
```

Certified interpretation:

- POS Admin provides the operational surface for affiliate administration.
- POS Admin tenant operators may configure campaign inputs, commission inputs, product/category eligibility intent, affiliate participation operations, payout review intent, approval/rejection review intent, payment-reference intent, payment-proof intent, reporting reads, analytics reads, notification reads, fraud-response operations, suspension-response operations, and recovery-response operations.
- The Affiliate Operational Surface is a POS Admin-facing operating surface only. It is not Affiliate authority, payment authority, accounting authority, settlement authority, identity authority, order authority, inventory authority, or Storefront checkout authority.
- Backend SQL/Core/Affiliate lifecycle owners admit or reject operational intent. They decide whether tenant, affiliate, campaign, product, category, identity, attribution, order, payment, accounting, wallet, transfer evidence, fraud, suspension, recovery, payout, and readiness facts are current and valid.
- Affiliate Platform presents admitted affiliate state. Affiliate-facing dashboards, links, commission history, payout state, account state, analytics, and notifications are views over admitted authority, not independent sources of truth.
- Reverse affiliate evidence always returns through backend authority before reaching POS Admin. Affiliate-origin account state, referral evidence, attribution evidence, commission evidence, payout request evidence, fraud evidence, recovery evidence, analytics evidence, notification evidence, and paid evidence must be admitted, rejected, reconciled, or failed closed by backend authority before POS Admin may rely on it operationally.
- Operational dashboards, payout queues, analytics, referral links, cookies, DTOs, and UI state never become authority.
- Payment proof is evidence only. It does not prove external bank/provider finality, settlement, accounting recognition, balance withdrawal, or paid commission truth until admitted by backend payout/evidence authority and the relevant Payments/Accounting boundaries.
- POS Admin payout approval is operational intent until admitted. It cannot create payout lifecycle truth from frontend amount, wallet, affiliate, store, currency, status, or executor fields.
- Affiliate attribution is backend admitted. Referral URLs, query parameters, cookies, copied links, browser state, analytics first-touch/last-touch reports, and operator judgment are candidate evidence only.
- Commission lifecycle is backend owned. Commission snapshots, pending/earned state, approval, reversal, balance movement, withdrawal, paid evidence, negative balance, and recovery remain SQL/Core/Affiliate authority.
- Settlement and accounting remain outside Affiliate operational ownership. Affiliate can participate in payout readiness and evidence allocation, but settlement finality and ledger truth remain Payments/Accounting authority.

The certified model therefore allows POS Admin to operate Affiliate while preventing POS Admin, Affiliate dashboards, frontend state, external proof, analytics, notifications, projections, or copied referral evidence from becoming competing authority.

---

# Certified Architectural Areas

The POS Admin and Affiliate guides are certified consistent for the following architectural areas:

## Affiliate Operational Surface

Both guides describe POS Admin as the Affiliate Operational Surface for tenant-side affiliate administration. The surface submits intent, exposes evidence, and supports operations. It does not own Affiliate authority.

## Affiliate Management

Both guides preserve affiliate identity and account state as backend Affiliate/Security authority. POS Admin may manage tenant-side affiliate participation where allowed, but affiliate session truth, account capability, suspension, closure, verification, and protected access remain backend owned.

## Campaign Configuration

Both guides agree that POS Admin can configure campaign inputs and eligibility intent, while backend authority validates tenant scope, campaign state, product/category lineage, policy state, and current eligibility before Affiliate or Storefront may consume the result.

## Commission Configuration

Both guides agree that POS Admin may configure commission policy inputs, but checkout-time and commission-lifecycle authority remain backend owned. Configuration changes do not rewrite historical commission snapshots.

## Commission Lifecycle

Both guides preserve the commission lifecycle as backend Affiliate authority consuming Orders, Payments, Accounting, and delivery/fulfillment evidence. Candidate, earned/pending, approved, reversed, paid evidence, withdrawal, and balance movement remain distinct lifecycle concepts.

## Commission Ledger

Both guides treat the commission ledger as SQL-authoritative. Dashboards, reports, payout queues, analytics, and frontend totals are derivative evidence only.

## Affiliate Balances

Both guides agree that affiliate balances derive from current SQL ledger truth: approved net commission, reversals, withdrawals, negative movements, and active payout reservations. Displayed dashboard balances are not payout authority.

## Referral Tracking

Both guides agree that referral links, copied URLs, headers, browser evidence, and cookies are candidate evidence only. Backend Affiliate authority must admit referral evidence before it can influence Storefront entry, checkout, attribution, or commission.

## Attribution

Both guides agree that durable attribution is backend admitted and order-bound. A referred conversion remains a Storefront-origin generic order with immutable affiliate attribution, not a separate Affiliate checkout, order, payment, fulfillment, or inventory path.

## Campaign Eligibility

Both guides agree that campaign eligibility is current-authority dependent and must be revalidated by backend authority before admission. Stale campaign cards, copied links, analytics, and presentation surfaces cannot preserve eligibility after current authority rejects it.

## Category Eligibility

Both guides agree that category eligibility remains dependent on current backend catalog/category and affiliate policy authority. Category presentation is evidence only.

## Product Eligibility

Both guides agree that product eligibility depends on current product, category, publication, price, stock, tenant, and affiliate policy authority. Product cards, affiliate product lists, search results, and stale projections cannot create eligibility.

## Fraud Protection

Both guides preserve fraud protection as backend Affiliate/Security/Payments/Accounting authority. Suspicious payout proof, referral abuse, repeated attribution abuse, campaign abuse, manual override attempts, duplicate payout attempts, and stale evidence must fail closed unless current backend authority admits them.

## Identity Collision

Both guides agree that affiliate identity is separate from POS Admin users, Storefront customers, CommerceHub customers, vendors, and Super Admins. Identity collision does not authorize attribution, commission, payout, session capability, or cross-surface access.

## Self Referral Protection

Both guides agree that self-referral is not admitted merely because affiliate and customer identities are individually valid. Backend identity, customer, tenant, payment, and fraud authority must explicitly admit any allowed case; otherwise it fails closed.

## Affiliate Suspension

Both guides agree that suspended, closed, inactive, unverified, deleted, or governance-restricted affiliate capability fails closed for protected operations. POS Admin presentation cannot restore capability.

## Affiliate Recovery

Both guides agree that recovery re-enters current SQL Affiliate/Core authority. Recovery may collect evidence, route review, or converge state, but it cannot invent attribution, commission, payout, payment, order, accounting, identity, or settlement truth.

## Payout Requests

Both guides agree that payout request amount is intent only. Backend payout request authority recomputes current balance, active request reservation, wallet snapshot, tenant/store scope, currency, request lifecycle, and readiness.

## Payout Review

Both guides agree that POS Admin/Finance may review payout requests as an operational surface. Review does not bypass backend payout authority, readiness gates, wallet binding, transfer evidence allocation, Payments authority, or Accounting authority.

## Approval / Rejection

Both guides agree that approval or rejection from POS Admin is operational intent until backend authority admits the resulting payout-review state. Approval does not create paid state, settlement, ledger truth, external transfer finality, or balance withdrawal by itself.

## Payment Proof

Both guides agree that payment proof and transfer references are evidence. They become payout-relevant only after backend verification, binding, allocation, idempotency checks, scope checks, and financial readiness validation.

## Paid Evidence

Both guides agree that paid commission evidence records local affiliate lifecycle outcome only after backend admission. It is not external bank/provider finality and does not replace accounting settlement.

## Wallet / Destination

Both guides agree that payout destination authority depends on current affiliate identity and captured wallet snapshot boundaries. Later wallet edits cannot rewrite historical payout request or payout evidence snapshots.

## Payout Readiness

Both guides agree that payout readiness is outward-money readiness and fails closed under unresolved external money, provider/refund ambiguity, critical backlog, ambiguous scope, accounting gaps, currency conflict, stale wallet evidence, insufficient current balance, active request reservations, or unresolved fraud/evidence concerns.

## Settlement Participation

Both guides agree that Affiliate may participate in payout readiness and paid evidence workflows, but settlement remains outside Affiliate operational ownership. Settlement authority is owned by the relevant Payments/Accounting/settlement boundaries.

## Accounting Separation

Both guides agree that Accounting remains separate and authoritative. Affiliate commission, reversal, payout evidence, withdrawal, paid evidence, and reports must converge through accounting authority before becoming ledger or financial reporting truth.

## Refund Effects

Both guides agree that refunds reduce or reverse payable commission only after current payment/refund/order authority admits the effect. Refunded value cannot remain payable merely because prior attribution existed.

## Chargeback Effects

Both guides agree that chargeback and provider-dispute evidence are Payments/Reconciliation authority. Affiliate payout readiness must fail closed while chargeback or external-money truth is unresolved.

## Negative Balance

Both guides agree that post-payout refunds, chargebacks, reversals, or recovery events may create bounded negative-balance and recovery effects under current SQL authority. Dashboards cannot manually correct or erase those effects.

## Recovery

Both guides agree that recovery reloads current SQL attribution, commission, payout, wallet, payment, refund, order, accounting, and external-money evidence. It converges through authority and fails closed where proof is missing.

## Notifications

Both guides agree that notifications are derivative communication outputs. Delivery, non-delivery, and notification display never create or change attribution, commission, payout, order, payment, accounting, or identity truth.

## Analytics

Both guides agree that analytics are derivative evidence over admitted authority. Analytics cannot decide attribution winners, conversion truth, commission amounts, payoutable balances, ledger truth, paid status, fraud finality, or settlement.

## Operational Change Propagation

Both guides agree on the propagation path: POS Admin intent enters backend validation against current authority; SQL Affiliate/Core authority admits or rejects the change; Affiliate Platform, Storefront entry, payout queues, reports, analytics, notifications, and dashboards converge from admitted authority.

## Dependency Mapping

Both guides use dependency mapping rather than authority transfer. The maps explain which surface operates an area, which backend/Core authority decides truth, what evidence is produced or exposed, and who consumes it.

## Authority Boundaries

Both guides preserve authority boundaries: SQL remains authoritative; Core lifecycle owners own protected state; Affiliate owns only its admitted affiliate business lifecycle; POS Admin operates but does not own Affiliate truth; Payments/Accounting/Orders/Identity/Inventory remain separate authorities where relevant.

## Business Rules

Both guides express compatible business rules for affiliate identity separation, referral evidence admission, attribution, commission lifecycle, payout readiness, payout evidence, dashboard derivation, fraud boundaries, and accounting separation.

## Never Assume

Both guides warn against treating POS Admin screens, referral URLs, cookies, dashboards, payout queues, analytics, notifications, frontend DTOs, payment proof, operator judgment, or stale projections as authority.

## QA Checklist

Both guides direct QA to identify the owning authority, distinguish operation from ownership, validate tenant and identity scope, treat evidence as evidence, verify fail-closed behavior, preserve SQL authority, and follow Core guides rather than deriving rules from UI or implementation convenience.

---

# Authority Consistency

The certified relationship preserves the following shared architectural truths:

- SQL remains authoritative.
- Affiliate Operational Surface never becomes Affiliate authority.
- Attribution is backend admitted.
- Commission lifecycle is backend owned.
- Commission ledger remains authoritative.
- Payout approval is operational intent until admitted.
- Payment proof is evidence only.
- Settlement remains outside Affiliate ownership.
- Accounting remains separate.
- Presentation never becomes authority.
- Evidence never becomes authority.

Additional certified authority statements:

- POS Admin may operate Affiliate workflows, but it does not own affiliate identity, referral admission, canonical attribution, commission snapshots, commission ledger progression, balance calculation, payout request admission, wallet snapshot binding, transfer evidence allocation, paid commission evidence, settlement participation, accounting effects, negative-balance recovery, suspension effects, or fraud protection.
- Affiliate Platform may present affiliate state, but it cannot create truth from dashboards, links, analytics, notifications, copied URLs, browser state, portal state, or stale projections.
- Backend SQL/Core/Affiliate authority is the required admission boundary for forward operational intent and reverse affiliate evidence.
- Storefront checkout, Orders, Payments, Inventory, Security/Identity, Accounting, Notifications, Analytics, and Search remain separate Core/shared domains where the Affiliate relationship depends on them.
- CommerceHub marketplace order/sub-order authority remains separate from Affiliate Storefront attribution. Storefront affiliate attribution does not automatically apply to CommerceHub marketplace orders or vendor earnings.
- Tenant and actor scope must be proven from current backend authority. Client tenant fields, route ids, JWT claims, cookies, dashboard filters, and frontend DTOs are evidence only.
- Uncertainty fails closed. Missing, stale, ambiguous, duplicated, conflicting, or externally uncertain evidence cannot authorize payout, commission, attribution, settlement, accounting, identity capability, or paid finality.

---

# Certification Notes

Future reviewers must preserve the following architectural decisions:

## Affiliate Operational Surface terminology

The term Affiliate Operational Surface is the certified term for the POS Admin-facing tenant operational surface that manages affiliate administration. It must remain a surface for intent, evidence, review, and operational response. It must not be renamed or reframed in a way that implies POS Admin owns Affiliate authority.

## Bidirectional operational flow

The certified relationship is not one-way. POS Admin submits operational intent toward Affiliate through backend authority, and Affiliate evidence returns to POS Admin only after backend authority admits or classifies it. Future wording must preserve both directions.

## Attribution boundaries

Attribution remains backend admitted and order-bound. Referral links, cookies, browser storage, analytics, copied URLs, and POS Admin review screens are not canonical attribution authority.

## Fraud boundaries

Fraud protection remains backend Affiliate/Security/Payments/Accounting authority. POS Admin may observe, respond, review, and escalate, but it cannot manually settle fraud questions from UI evidence.

## Identity boundaries

Affiliate identity remains separate from POS Admin user identity, Storefront customer identity, CommerceHub customer identity, vendor identity, and Super Admin identity. Identity collision and self-referral risks must continue to fail closed unless current backend policy explicitly admits them.

## Commission lifecycle

Commission snapshots, pending/earned state, approval, reversal, paid evidence, withdrawal, balance movement, negative balance, and recovery must remain backend SQL/Core/Affiliate authority. Commission policy configuration does not rewrite historical snapshots.

## Payout lifecycle

Payout request admission, payout review admission, wallet snapshot binding, readiness, evidence allocation, paid commission evidence, duplicate transfer-reference collapse, reversal, negative balance, and recovery remain backend-owned lifecycle concerns. POS Admin review and payment proof remain operational intent/evidence.

## Accounting separation

Accounting and ledger truth remain separate from Affiliate and POS Admin presentation. Affiliate commission and payout effects must converge through Accounting/Payments authority before they become financial reporting or settlement truth.

## Operational intent versus authority

The certified relationship depends on the distinction between operational intent and authority. POS Admin can initiate, review, approve/reject intent, submit proof, display queues, and consume reports. Authority admits, rejects, reconciles, quarantines, or fails closed. Future documentation must not collapse those two concepts.

---

# Future Impact

Any future modification affecting the following areas requires re-certification of the POS Admin ↔ Affiliate relationship:

- Attribution
- Referral Tracking
- Commission
- Fraud
- Identity
- Payout
- Settlement Participation
- Accounting Boundaries
- Affiliate Operational Surface
- Authority Boundaries

Re-certification is required because changes in these areas could alter who owns truth, which evidence is admissible, which surface may operate a workflow, how operational intent becomes durable state, how reverse evidence returns to POS Admin, or whether presentation/evidence remains separate from authority.

Examples of changes requiring re-certification include:

- Renaming or redefining the Affiliate Operational Surface.
- Giving POS Admin direct authority over attribution, commission, payout, fraud, settlement, accounting, or affiliate identity state.
- Allowing dashboards, payout queues, analytics, referral links, cookies, DTOs, notifications, or payment proof to become authoritative.
- Changing payout approval, transfer evidence, wallet snapshot, paid evidence, or balance-withdrawal ownership.
- Changing whether Storefront affiliate attribution applies to CommerceHub marketplace orders.
- Changing fail-closed behavior for unresolved external money, chargebacks, refunds, ambiguous scope, stale wallet evidence, duplicate transfer references, identity collision, or self-referral.
- Moving settlement or accounting authority into Affiliate or POS Admin presentation surfaces.

Until such changes are re-certified, this certification remains the permanent record of the v1.0 certified relationship.

---

# References

- `.ai/hermes/CROSS_SYSTEM_CERTIFICATION.md`
- `.ai/hermes/systems/pos-admin/README.md`
- `.ai/hermes/systems/affiliate/README.md`
- Relevant Core Guides:
  - `.ai/hermes/02-architecture.md`
  - `.ai/hermes/04-payments.md`
  - `.ai/hermes/05-orders.md`
  - `.ai/hermes/06-security-identity.md`
  - `.ai/hermes/07-platform-overview.md`
  - `.ai/hermes/08-qa-philosophy.md`
