# Super Admin System Guide

Audience: Senior QA Engineers, AI Agents, and new contributors.

This is a SYSTEM guide.
It is not a domain guide, feature guide, implementation guide, or UI guide.

The Core Guides own the durable domain rules:

- `.ai/hermes/02-architecture.md`
- `.ai/hermes/03-inventory.md`
- `.ai/hermes/04-payments.md`
- `.ai/hermes/05-orders.md`
- `.ai/hermes/06-security-identity.md`
- `.ai/hermes/07-platform-overview.md`
- `.ai/hermes/08-qa-philosophy.md`

This guide explains how Super Admin operates as a complete CommerceHub governance system. It intentionally avoids API details, code details, endpoint names, and UI mechanics.

The most important rule:

Super Admin owns CommerceHub governance.
Super Admin does not own the entire Shopixy platform.
Super Admin governs POS tenants, Storefront data, Affiliate participation, orders, inventory, payments, accounting, and customers only where those facts participate inside CommerceHub.

Outside CommerceHub, the owning system remains autonomous and authority remains with the Core lifecycle owner.

---

# Purpose

Super Admin exists to operate, protect, moderate, finance-control, and recover CommerceHub as a marketplace.

A Senior QA Engineer or AI Agent should use this guide to understand:

- what Super Admin is allowed to govern;
- what Super Admin is never allowed to own;
- how marketplace governance affects vendors, customers, listings, payments, commission debt, settlements, refunds, and emergency operations;
- which Core guide owns the underlying business rules;
- which system remains authoritative when Super Admin takes an operational action.

Super Admin is an operational governance system over CommerceHub. It is not a shortcut around Inventory, Orders, Payments, Accounting, Fulfillment, Security, Storefront, POS Admin, or Affiliate authority.

When testing Super Admin behavior, the QA question is never only “can the admin do this?”

The correct question is:

```text
Is this a CommerceHub governance action, and did it re-enter the owning Core authority instead of becoming shadow authority?
```

---

# What is Super Admin?

Super Admin is the platform-operator system for CommerceHub marketplace governance.

It gives trusted platform actors the ability to operate the marketplace as a business: admit vendors, approve or suppress marketplace visibility, enforce COD commission debt controls, moderate listings and content, review suspicious activity, monitor settlements and payouts, execute governed payout recognition, trigger marketplace repair, and run emergency containment.

## Its purpose

Super Admin exists because CommerceHub is a shared marketplace where one platform must coordinate many autonomous tenants acting as vendors.

CommerceHub needs platform-level governance because:

- vendor visibility affects all marketplace customers;
- vendor debt and settlement risk affect platform cash and vendor fairness;
- marketplace categories, brands, homepage curation, reports, and moderation are shared marketplace assets;
- public marketplace discovery must not depend only on a tenant’s own POS Admin decisions;
- emergencies may require marketplace-wide suppression, payment-intake containment, recovery, or reindexing;
- dangerous operations must be audited and constrained by platform authority.

## Business value

Super Admin protects CommerceHub business integrity:

- customers see only vendors and listings currently admitted by marketplace governance;
- vendors cannot continue new marketplace sales when COD commission debt exceeds the hard risk boundary;
- settlement and payout operations remain readiness-gated and auditable;
- marketplace visibility can be repaired from SQL authority after projection drift;
- suspicious, unsafe, or fraudulent marketplace behavior can be reviewed without giving operators direct ownership of money, stock, or order state;
- emergency operations can contain marketplace harm while preserving Core authority boundaries.

## What it owns

Super Admin owns CommerceHub operational governance, including:

- marketplace governance decisions;
- vendor approval, suspension, suppression, restoration eligibility, and visibility governance inside CommerceHub;
- marketplace categories, brands, curation, moderation, homepage/discovery governance, and shared marketplace configuration;
- marketplace financial operations visibility and governed execution workflows where the authoritative finance rules allow them;
- COD commission debt enforcement as marketplace risk governance;
- payout execution recognition workflow for CommerceHub vendors, with readiness gates and external transfer evidence;
- marketplace audit, support triage, fraud review, operational reports, analytics views, repair/reindex controls, and emergency containment;
- dangerous operational primitives when explicitly scoped, audited, SQL-first, and runbook-governed.

## What it governs

Super Admin governs CommerceHub participation.

It governs:

- POS tenants while they are acting as CommerceHub vendors;
- vendor marketplace visibility;
- vendor capability to accept new CommerceHub orders;
- CommerceHub listing admission and suppression;
- CommerceHub public discovery safety;
- marketplace COD debt risk;
- CommerceHub payout execution workflow;
- marketplace emergency controls and operational repair.

This governance is scoped to CommerceHub. It does not convert Super Admin into the owner of the tenant’s entire business.

## What it NEVER owns

Super Admin never owns:

- POS Admin as a tenant back-office system;
- tenant Storefronts;
- Affiliate as a separate business system;
- inventory authority;
- payment authority;
- order authority;
- fulfillment authority;
- accounting or ledger authority;
- customer identity outside CommerceHub customer governance;
- merchant price authority except where marketplace policy admits or suppresses CommerceHub visibility;
- external bank, provider, carrier, or wallet side effects;
- derivative projections as truth.

Super Admin may trigger, block, review, suppress, restore, contain, or escalate. It may not rewrite Core truth.

## Relationship with CommerceHub

CommerceHub is the system Super Admin owns operationally.

CommerceHub provides the marketplace: vendor participation, multi-vendor discovery, marketplace cart, split checkout, marketplace orders and sub-orders, EGP marketplace finance, vendor earnings, COD commission debt, settlement monitoring, payout readiness, returns/refunds, marketplace reports, and marketplace governance.

Super Admin governs this marketplace layer. It decides whether vendors and listings are admitted to marketplace participation and whether marketplace operations must be contained or repaired.

CommerceHub still does not let Super Admin bypass Core lifecycle authority. CommerceHub uses Orders, Payments, Inventory, Accounting, Security, Search, Notifications, and Reconciliation as owning domains.

## Relationship with POS Admin

POS Admin belongs to the tenant.

A POS tenant becomes visible to Super Admin only when that tenant participates in CommerceHub as a Vendor.

Super Admin can govern that tenant’s marketplace participation:

- approve or reject vendor onboarding;
- suppress the vendor from CommerceHub;
- block the vendor from new marketplace orders due to governance or COD debt;
- monitor vendor marketplace finance;
- review vendor marketplace payouts, settlements, refunds, and commission debt;
- require recovery or moderation for marketplace-visible records.

Super Admin does not own the tenant’s POS Admin outside CommerceHub.

Outside CommerceHub, the POS tenant remains autonomous and is governed by tenant authority, Core lifecycle services, and tenant-scoped Security/Identity rules.

## Relationship with Storefront

Storefront belongs to the tenant.

Super Admin does not directly manage tenant storefronts. It may govern marketplace policies that use tenant product, pricing, catalog, media, or inventory evidence for CommerceHub publication, but the tenant Storefront remains a separate system.

A tenant product may be visible in a tenant Storefront while being suppressed from CommerceHub, or absent from Storefront while a marketplace publication path separately fails admission. QA must not assume one surface owns the other.

## Relationship with Affiliate

Affiliate is a separate business system.

Super Admin may govern marketplace policies that affect Affiliate participation inside CommerceHub only where a certified CommerceHub marketplace-affiliate path exists. Storefront affiliate attribution rules must not be copied into CommerceHub marketplace orders unless the source documents explicitly certify that relationship.

Affiliate identity, attribution, commission lifecycle, payout readiness, and payout evidence remain owned by the Affiliate system and Core financial authorities.

## Relationship with Customers

CommerceHub Customers are marketplace customers.

Super Admin may govern customer-facing marketplace safety indirectly through vendor visibility, listing suppression, moderation, fraud review, support workflows, refund review, and emergency containment.

Super Admin does not become customer identity authority, payment authority, order authority, refund authority, or fulfillment authority.

## Relationship with Vendors

A Vendor is a POS tenant participating in CommerceHub.

Inside CommerceHub, Super Admin governs the vendor’s marketplace admission, visibility, operating capability, debt status, suspension, recovery, and payout execution workflow.

Outside CommerceHub, that POS tenant remains independent.

---

# Primary Responsibilities

Each responsibility below explains why it exists, which Core guide owns the underlying business rules, and which system actually owns authority.

## Marketplace Governance

Why it exists: CommerceHub is a shared marketplace. The platform needs a governed way to decide which vendors, listings, categories, homepage content, promotions, and discovery candidates may appear to marketplace customers.

Core guide owner: `02-architecture.md`, `06-security-identity.md`, `07-platform-overview.md`, `08-qa-philosophy.md`.

Actual authority: CommerceHub marketplace governance in PostgreSQL plus current Super Admin platform actor authority. Public visibility remains derived from current SQL listing, product, vendor, category, tenant, inventory, price, and governance authority.

Super Admin role: decide governance policy and marketplace admission. It does not create visibility if current SQL authority rejects the vendor or listing.

## Vendor Governance

Why it exists: Vendors are autonomous POS tenants, but CommerceHub must protect the shared marketplace from unsafe vendors, banned actors, debt-blocked vendors, deleted tenants, governance-hidden vendors, and stale capability evidence.

Core guide owner: `06-security-identity.md`, `07-platform-overview.md`, `08-qa-philosophy.md`.

Actual authority: Backend vendor governance, Super Admin platform authority, vendor lifecycle authority, capability authority, debt authority, visibility authority, participation authority, and tenant authority.

Super Admin role: apply marketplace governance decisions to vendors inside CommerceHub. It does not own the tenant’s POS Admin outside CommerceHub.

## Vendor Onboarding

Why it exists: CommerceHub must admit only eligible POS tenants as vendors so customers can buy from marketplace participants with valid tenant lineage, marketplace capability, and governance status.

Core guide owner: `06-security-identity.md`, `07-platform-overview.md`, `05-orders.md` for marketplace order participation.

Actual authority: Current SQL tenant/vendor identity, POS actor authority, vendor eligibility, marketplace participation authority, and governance state.

Super Admin role: review and admit marketplace vendor participation. It does not create tenant identity, tenant catalog truth, inventory truth, or payment authority.

## Vendor Approval

Why it exists: Approval converts a candidate marketplace participant into a governed CommerceHub vendor, not into a platform-owned tenant.

Core guide owner: `06-security-identity.md`, `07-platform-overview.md`.

Actual authority: SQL vendor participation and governance state.

Super Admin role: approve participation after current authority proves tenant/vendor eligibility. Approval does not approve every product, price, order, or payment.

## Vendor Suspension

Why it exists: CommerceHub must stop unsafe vendors from receiving new marketplace orders or appearing in public discovery when governance or financial risk requires containment.

Core guide owner: `06-security-identity.md`, `04-payments.md`, `05-orders.md`, `07-platform-overview.md`.

Actual authority: SQL governance state, vendor debt authority, marketplace visibility authority, and checkout/order admission authority.

Super Admin role: suspend marketplace participation. Suspension blocks new CommerceHub order acceptance and public visibility when applicable. It does not cancel existing orders or rewrite their lifecycle.

## Vendor Visibility

Why it exists: Marketplace customers should only see vendors and listings currently admitted by marketplace governance and current SQL authority.

Core guide owner: `02-architecture.md`, `03-inventory.md`, `06-security-identity.md`, `07-platform-overview.md`.

Actual authority: SQL listing/product/vendor/category/tenant/price/stock/governance authority with derivative search and projection layers converging from it.

Super Admin role: govern suppression/restoration eligibility and marketplace curation. Search, homepage, recommendations, and product pages must revalidate current authority.

## Marketplace Categories

Why it exists: CommerceHub needs marketplace navigation, filtering, taxonomy, and category governance independent from any one tenant storefront.

Core guide owner: `02-architecture.md`, `06-security-identity.md`, `07-platform-overview.md`.

Actual authority: SQL category taxonomy and live category lineage plus marketplace category governance.

Super Admin role: govern shared marketplace taxonomy and curation. It does not own tenant storefront category presentation outside CommerceHub.

## Marketplace Brands

Why it exists: Brand governance keeps marketplace discovery, filtering, and trust signals consistent across vendors.

Core guide owner: `02-architecture.md`, `06-security-identity.md`, `07-platform-overview.md`.

Actual authority: SQL brand/catalog authority where brand data participates in CommerceHub, plus marketplace governance admission.

Super Admin role: govern marketplace brand admission, visibility, or moderation. It does not rewrite tenant catalog truth outside CommerceHub.

## Marketplace Moderation

Why it exists: Marketplace content, listings, reviews, homepage sections, public images, and shared discovery candidates may need suppression, review, or recovery to protect customers and vendors.

Core guide owner: `06-security-identity.md`, `07-platform-overview.md`, `08-qa-philosophy.md`.

Actual authority: Current SQL content/listing/review/category/vendor governance authority and derivative convergence controls.

Super Admin role: moderate CommerceHub-visible content. Moderation cannot restore content when current SQL authority no longer proves visibility.

## Marketplace Product Approval

Why it exists: Products from POS tenants enter CommerceHub only when publication, product, variant, category, price, stock, tenant, and vendor governance evidence are valid.

Core guide owner: `03-inventory.md`, `05-orders.md`, `06-security-identity.md`, `07-platform-overview.md`.

Actual authority: POS tenant catalog/product authority, SQL CommerceHub listing authority, inventory lineage authority, pricing authority, category lineage, and vendor governance.

Super Admin role: approve or suppress marketplace publication where marketplace governance requires it. It does not own product inventory, variant lifecycle, tenant price decisions, or POS catalog outside CommerceHub.

## Marketplace Reports

Why it exists: Operators need visibility into marketplace health, vendor performance, debt, refunds, payouts, reconciliation, and governance state.

Core guide owner: `02-architecture.md`, `04-payments.md`, `05-orders.md`, `07-platform-overview.md`.

Actual authority: SQL orders, payments, refunds, vendor earnings, financial transactions, ledger rows, settlement lineage, and governance state. Reports are derivative unless explicitly backed by current SQL authority.

Super Admin role: consume reports for decisions. Reports do not become order, payment, ledger, or inventory truth.

## Marketplace Analytics

Why it exists: CommerceHub needs operational analysis of discovery, vendor activity, sales, conversion, debt, fraud signals, and recovery trends.

Core guide owner: `02-architecture.md`, `07-platform-overview.md`, `08-qa-philosophy.md`.

Actual authority: Analytics are read models over SQL and event evidence. They are not lifecycle authority.

Super Admin role: use analytics as decision support. Analytics cannot approve payments, settle payouts, rewrite orders, or restore visibility without current SQL authority.

## Platform-wide Marketplace Configuration

Why it exists: CommerceHub needs shared marketplace settings, risk policies, category policy, moderation defaults, finance thresholds, visibility policy, and emergency controls.

Core guide owner: `02-architecture.md`, `04-payments.md`, `06-security-identity.md`, `07-platform-overview.md`.

Actual authority: SQL-backed configuration and governance records, admitted by current Super Admin platform authority. Domain-specific rules remain owned by the Core guide for that domain.

Super Admin role: configure marketplace policy. It does not create a second payment, order, inventory, or accounting rule engine.

## Marketplace Payment Methods

Why it exists: CommerceHub must know which payment methods are allowed for marketplace checkout while respecting provider evidence, tenant/provider gating, CommerceHub certification limits, and EGP marketplace currency.

Core guide owner: `04-payments.md`, `05-orders.md`, `06-security-identity.md`.

Actual authority: Payment lifecycle authority, provider admission rules, CommerceHub checkout authority, tenant/store provider eligibility where applicable, and current SQL payment-method configuration.

Super Admin role: govern marketplace payment-method policy and emergency payment intake controls. It is not payment finality authority.

## Marketplace Commission Policies

Why it exists: CommerceHub earns commission from marketplace sales. Commission policy defines how marketplace revenue is computed from admitted orders.

Core guide owner: `04-payments.md`, `05-orders.md`, `07-platform-overview.md`.

Actual authority: SQL order/sub-order financial snapshots, commission snapshots, CommerceHub financial ledger, and accounting authority.

Super Admin role: govern marketplace commission policy prospectively. It does not rewrite historical commission snapshots or ledger truth.

## Vendor Commission Tracking

Why it exists: Vendors owe marketplace commission for COD sales and may accrue payable/vendor earnings for online-payment sales.

Core guide owner: `04-payments.md`, `05-orders.md`, `07-platform-overview.md`.

Actual authority: SQL financial transactions, order/sub-order snapshots, commission snapshots, vendor earnings, refund/reversal evidence, and ledger truth.

Super Admin role: monitor commission balances and exceptions. Tracking dashboards are not debt authority unless recomposed from current SQL financial facts.

## Vendor Commission Collection

Why it exists: In COD flows the vendor receives customer cash, so marketplace commission becomes debt owed by the vendor to the marketplace.

Core guide owner: `04-payments.md`, `05-orders.md`, `07-platform-overview.md`.

Actual authority: SQL COD collection evidence, financial transaction rows, vendor debt recomposition, approved repayment/settlement evidence, and accounting ledger.

Super Admin role: review and approve marketplace commission collection evidence where the workflow requires platform approval. It does not assume marketplace cash was received at delivery time.

## Vendor Debt Management

Why it exists: COD commission debt must be tracked, thresholded, collected, adjusted, and audited to protect marketplace revenue and customer safety.

Core guide owner: `04-payments.md`, `05-orders.md`, `07-platform-overview.md`.

Actual authority: Vendor debt is recomposed from SQL commission, settlement, approved repayment, adjustment, refund, and audit evidence. Dashboards and caches are derivative.

Super Admin role: operate debt review, settlement evidence, adjustments, and recovery workflows. Debt state must remain SQL-ledger-derived.

## Vendor Blocking

Why it exists: Vendors at or above the COD debt threshold, or vendors blocked by governance, must not accept new CommerceHub orders or remain publicly discoverable.

Core guide owner: `04-payments.md`, `05-orders.md`, `06-security-identity.md`.

Actual authority: SQL vendor debt snapshots, governance state, checkout admission, discovery visibility filtering, and marketplace order lifecycle.

Super Admin role: enforce or review blocks. Blocking does not rewrite existing orders, payments, refunds, or fulfillment.

## Vendor Recovery

Why it exists: Vendors need a governed way to return to marketplace participation after debt payment, governance clearance, or visibility repair.

Core guide owner: `04-payments.md`, `05-orders.md`, `06-security-identity.md`, `08-qa-philosophy.md`.

Actual authority: Current SQL debt below threshold, active tenant/vendor governance, valid listing/product/category/stock/price authority, and lifecycle-safe recovery.

Super Admin role: restore marketplace participation only when current authority proves eligibility. Recovery cannot blindly restore old publication intent.

## Marketplace Customer Governance

Why it exists: CommerceHub must protect marketplace customers from fraud, unsafe vendors, misleading visibility, incorrect order status, and unresolved payment/refund uncertainty.

Core guide owner: `05-orders.md`, `04-payments.md`, `06-security-identity.md`, `07-platform-overview.md`.

Actual authority: CommerceHub customer identity/session authority, marketplace order authority, payment/refund authority, and support/fraud governance records.

Super Admin role: support and govern customer-facing marketplace safety. It cannot become customer identity, order, payment, refund, or fulfillment authority.

## Marketplace Refund Governance

Why it exists: Refunds affect customers, vendors, platform commission, payout readiness, ledger truth, and post-settlement debt/adjustment behavior.

Core guide owner: `04-payments.md`, `05-orders.md`, `07-platform-overview.md`.

Actual authority: Payment/refund lifecycle authority, SQL refund rows, payment state, order/sub-order lineage, settlement membership, vendor earnings, and ledger authority.

Super Admin role: review and operate marketplace refund workflows where platform governance participates. It cannot mark refund success, bypass readiness gates, or rewrite paid-out history.

## Marketplace Payout Governance

Why it exists: Vendor payouts are outward money movement and must be readiness-gated, evidence-backed, currency-safe, and audited.

Core guide owner: `04-payments.md`, `06-security-identity.md`, `07-platform-overview.md`.

Actual authority: SQL vendor earnings, payout rows, payout items, ledger payable truth, readiness gates, external transfer evidence, and accounting authority.

Super Admin role: execute or recognize CommerceHub vendor payout workflow when readiness allows and external evidence is present. It is not bank authority and cannot bypass payout readiness.

## Marketplace Settlement Monitoring

Why it exists: Operators need to know whether vendor earnings, commission debt, settlement lineage, payouts, refunds, and reconciliation are healthy.

Core guide owner: `04-payments.md`, `05-orders.md`, `07-platform-overview.md`.

Actual authority: SQL settlement rows, live settlement-order lineage, vendor financial transactions, payout/refund rows, and ledger/accounting truth.

Super Admin role: monitor, review, and escalate settlement anomalies. It must not treat settlement header counts, dashboards, or exported summaries as membership authority.

## Marketplace Audit

Why it exists: Governance, financial operations, dangerous primitives, emergency actions, vendor changes, and moderation must be reconstructable.

Core guide owner: `02-architecture.md`, `04-payments.md`, `06-security-identity.md`, `08-qa-philosophy.md`.

Actual authority: SQL audit records, financial operation audits, governance audit logs, ledger rows, request-level Super Admin operational audit, and immutable evidence records where present.

Super Admin role: create auditable operational breadcrumbs and preserve accountability. Audit does not mutate business truth by itself.

## Marketplace Support

Why it exists: Marketplace customers and vendors need escalation paths for orders, refunds, visibility, debt, payouts, and fraud concerns.

Core guide owner: `05-orders.md`, `04-payments.md`, `06-security-identity.md`, `08-qa-philosophy.md`.

Actual authority: Owning order, payment, refund, fulfillment, debt, payout, governance, and identity systems.

Super Admin role: investigate, route, and submit governed intent. It cannot impersonate tenants or customers unless a future certified support ceremony explicitly exists.

## Marketplace Fraud Review

Why it exists: CommerceHub must detect and contain suspicious vendors, customers, proofs, refunds, payouts, debt repayment, and marketplace abuse.

Core guide owner: `04-payments.md`, `05-orders.md`, `06-security-identity.md`, `08-qa-philosophy.md`.

Actual authority: Identity/session authority, payment/refund evidence authority, order authority, debt/payout readiness, governance state, and audit evidence.

Super Admin role: review and contain suspected fraud. Fraud review may block or escalate; it does not create money, stock, orders, or identity truth.

## Marketplace Emergency Operations

Why it exists: Marketplace incidents may require immediate containment: freeze payment intake, suppress listings, block unsafe vendors, stop payout/refund execution, repair projections, or diagnose cross-system drift.

Core guide owner: `02-architecture.md`, `04-payments.md`, `05-orders.md`, `06-security-identity.md`, `08-qa-philosophy.md`.

Actual authority: Current SQL Super Admin authority for emergency scope, plus Core lifecycle/reconciliation owners for payment, order, fulfillment, refund, payout, settlement, inventory, and ledger outcomes.

Super Admin role: contain CommerceHub harm. Emergency operations do not create business truth and must not roll back from derivative state.

## Marketplace Dangerous Operations

Why it exists: Some platform repair actions are powerful: global reindex, all-tenant repair, forced replay, derivative rebuild, cache invalidation, search repair, and high-risk recovery diagnostics.

Core guide owner: `02-architecture.md`, `06-security-identity.md`, `08-qa-philosophy.md`.

Actual authority: Current SQL platform actor authority plus SQL-first rebuild/recovery declarations, lifecycle owners, replay fences, and dangerous-primitive governance.

Super Admin role: operate dangerous primitives only under scope, audit, telemetry, and runbook boundaries. Dangerous operations are tools, not authority models.

---

# Main Actors

## Super Admin

A current SQL platform actor authorized to govern CommerceHub. Super Admin identity is not a tenant role and cannot be inferred from POS Admin authority.

The Super Admin actor governs marketplace operations, vendor governance, emergency containment, platform configuration, marketplace finance workflows, and dangerous operations within CommerceHub.

## Marketplace Operations

The operational team that keeps CommerceHub healthy: onboarding vendors, monitoring marketplace visibility, triaging publication issues, coordinating repair/reindex work, handling governance queues, and escalating incidents.

Marketplace Operations uses Super Admin tools but does not own Core domain authority.

## Marketplace Finance

The finance function responsible for commission tracking, COD debt monitoring, settlements, payout readiness, payout execution evidence, refund impact, reconciliation, and finance reporting.

Marketplace Finance operates CommerceHub financial workflows but ledger/payment/payout/refund authority remains with the SQL financial and lifecycle systems.

## Marketplace Support

The team that investigates customer/vendor issues: order problems, refund questions, payout disputes, visibility problems, fraud flags, and recovery requests.

Marketplace Support routes evidence and submits governed intent. It does not rewrite authority.

## Marketplace Moderators

Operators who review marketplace content, listings, reviews, category fit, public images, homepage/curation references, and suspected abuse.

Moderators can suppress or escalate marketplace-visible content through governance. They do not create product, inventory, order, or payment truth.

## POS Vendor

A POS tenant acting as a CommerceHub Vendor.

Inside CommerceHub, the POS Vendor is subject to marketplace governance, debt controls, listing visibility rules, sub-order lifecycle constraints, and payout readiness.

Outside CommerceHub, the POS tenant remains autonomous.

## CommerceHub Customer

A customer using CommerceHub marketplace discovery, cart, checkout, order tracking, refunds, support, and recovery.

Super Admin may affect the customer experience through governance, visibility, refunds, support, emergency operations, and marketplace safety, but does not own customer identity, payment finality, order lifecycle, fulfillment, or refund success.

---

# Vendor Journey

```text
Vendor Registration
↓
Vendor Review
↓
Approval
↓
Publication
↓
Selling
↓
Commission Accumulation
↓
Debt Monitoring
↓
Restriction
↓
Debt Payment
↓
Recovery
```

## Vendor Registration

Ownership: POS tenant identity and vendor participation authority.

Super Admin role: reviews the tenant as a marketplace candidate. Registration does not transfer ownership of POS Admin to Super Admin.

## Vendor Review

Ownership: Marketplace governance, tenant/vendor eligibility, Security/Identity, and CommerceHub participation authority.

Super Admin role: assesses whether the vendor may participate in CommerceHub. Product, inventory, price, and payment authority remain in their Core systems.

## Approval

Ownership: SQL vendor participation/governance state.

Super Admin role: approves marketplace participation. Approval only means CommerceHub may consider the vendor eligible. It does not guarantee every listing is visible or every order is valid.

## Publication

Ownership: POS catalog/publication intent plus CommerceHub listing authority, category lineage, vendor governance, inventory lineage, price authority, and SQL visibility filters.

Super Admin role: governs marketplace admission and suppression. POS Admin remains the tenant operational surface for source product/catalog work.

## Selling

Ownership: CommerceHub checkout, marketplace order/sub-order authority, inventory reservations, payment lifecycle, fulfillment lifecycle, and accounting.

Super Admin role: governs whether the vendor may receive new marketplace orders. It does not create or rewrite orders.

## Commission Accumulation

Ownership: CommerceHub finance, commission snapshots, financial transactions, and ledger authority.

Super Admin role: monitors commission owed or earned. It does not recalculate historical order commission from current policy.

## Debt Monitoring

Ownership: SQL-ledger-derived vendor debt authority.

Super Admin role: monitors COD commission debt and settlement evidence. Dashboards are operational evidence, not authority by themselves.

## Restriction

Ownership: Vendor governance, debt threshold authority, marketplace checkout admission, and public visibility filters.

Super Admin role: enforces restriction when debt or governance state requires it. Restriction blocks new marketplace business; it does not cancel existing orders.

## Debt Payment

Ownership: Payment/finance/accounting authority, repayment evidence, settlement evidence, and ledger posting.

Super Admin role: reviews and admits repayment/settlement evidence where workflow requires platform review. Payment evidence does not become cash truth until admitted by the owning authority.

## Recovery

Ownership: Current SQL governance, debt below threshold, active tenant/vendor state, listing/product/category/price/stock eligibility, and derivative repair.

Super Admin role: restores marketplace participation only when current authority proves eligibility. Recovery cannot blindly restore stale visibility.

---

# Customer Journey

```text
Customer
↓
CommerceHub
↓
Checkout
↓
Order
↓
Payment
↓
Fulfillment
↓
Refund
↓
Support
↓
Recovery
```

## Customer

Super Admin governs customer safety indirectly through marketplace policy, fraud review, support operations, and emergency containment.

Authority remains with CommerceHub customer identity/session authority.

## CommerceHub

Super Admin governs marketplace visibility, vendor participation, curation, moderation, and emergency state.

Authority for discovery remains current SQL listing/product/vendor/category/tenant/price/stock/governance authority plus derivative convergence.

## Checkout

Super Admin can influence checkout eligibility by vendor suspension, COD debt restriction, marketplace payment policy, emergency stops, or visibility suppression.

Checkout authority remains CommerceHub checkout/order authority and Core payment/inventory rules.

## Order

Super Admin may monitor marketplace orders and support operational review.

Marketplace order/sub-order authority remains with CommerceHub order lifecycle. Super Admin cannot rewrite orders.

## Payment

Super Admin may govern payment method policy, emergency payment intake, payment/reconciliation review, and finance operations.

Payment authority remains with the Payment lifecycle. Provider callbacks and Super Admin screens are evidence only.

## Fulfillment

Super Admin may monitor, contain, or escalate fulfillment issues.

Fulfillment authority remains with the fulfillment/order lifecycle owner. Vendor fulfillment intent comes from the POS vendor surface and is admitted by backend authority.

## Refund

Super Admin may review marketplace refund workflows and support escalations.

Refund authority remains with payment/refund lifecycle, order/sub-order lineage, settlement lineage, and ledger readiness.

## Support

Super Admin can investigate and route support issues.

Support cannot impersonate, rewrite, or bypass lifecycle authority without a certified ceremony.

## Recovery

Super Admin can trigger marketplace repair, reindex, reconciliation review, and emergency recovery.

Recovery must recompose from current SQL authority and fail closed when authority is missing or contradictory.

---

# COD Financial Model

COD in CommerceHub is not immediate marketplace cash.

The complete model is:

```text
Customer
↓
Vendor receives money
↓
Marketplace commission becomes owed
↓
Commission debt accumulates
↓
Threshold reached
↓
Automatic Marketplace restriction
↓
Vendor pays commission
↓
Restriction removed
```

## Customer

The customer pays cash on delivery for a CommerceHub order.

## Vendor receives money

The vendor, not Super Admin and not the marketplace platform, receives the customer cash at delivery.

Delivery and cash collection remain distinct facts unless the payment authority admits the collection evidence.

## Marketplace commission becomes owed

Because the vendor received COD cash, the marketplace commission becomes a receivable owed by the vendor.

This is debt, not platform-held money.

## Commission debt accumulates

Vendor COD commission debt is recomposed from SQL financial facts: commission evidence, settlements, approved repayments, adjustments, refunds, and audit evidence.

Super Admin does not calculate debt from a dashboard total, cache value, spreadsheet, or frontend state.

## Threshold reached

When debt reaches the hard marketplace risk threshold, the vendor becomes unsafe for new marketplace orders.

The threshold rule is CommerceHub finance/debt authority, not a UI decision.

## Automatic Marketplace restriction

Restriction blocks new CommerceHub order acceptance and public marketplace visibility where certified.

It does not cancel existing orders, erase debt, rewrite fulfillment, or alter payment history.

## Vendor pays commission

Vendor repayment or settlement evidence must be admitted by the owning finance/accounting authority.

Super Admin reviews or approves evidence where required, but evidence is not cash truth until admitted.

## Restriction removed

Restriction is removed only when current SQL debt drops below the required boundary and governance/tenant/listing eligibility remains valid.

Super Admin does not receive COD money immediately.
Super Admin tracks and governs commission debt.
Super Admin must never model COD as if platform cash was already received.

---

# Online Payment Financial Model

Online payment in CommerceHub follows a different financial model from COD.

```text
Customer
↓
Marketplace receives payment
↓
Order delivered
↓
Marketplace deducts commission
↓
Vendor payout
```

## Customer

The customer pays through an online provider admitted by Payment authority.

## Marketplace receives payment

Marketplace/platform cash recognition depends on admitted payment lifecycle evidence and SQL financial authority. Provider success is evidence until admitted.

## Order delivered

Delivery affects earning availability, refund windows, payout eligibility, and settlement readiness. Fulfillment evidence is not payment authority by itself.

## Marketplace deducts commission

Commission is deducted from SQL-authoritative order/sub-order snapshots and ledger-backed finance workflows.

Current Super Admin policy does not rewrite historical commission snapshots.

## Vendor payout

Vendor payout is outward money movement. It requires ledger-confirmed earnings, payout readiness, no blocking refund/reconciliation state, currency safety, external transfer evidence, and Super Admin execution recognition where the workflow requires it.

## Settlement ownership

Settlement ownership belongs to CommerceHub finance, ledger, payout, refund, accounting, and reconciliation authority in SQL.

Super Admin monitors and executes governed workflow steps. It does not own settlement truth by screen action alone.

## Payment authority

Payment finality belongs to Payment lifecycle authority.

Super Admin does not become payment authority.

## Accounting ownership

Accounting ownership belongs to immutable PostgreSQL ledger/journal authority and accounting outbox workflows.

Super Admin does not own accounting.

---

# How Super Admin Governs CommerceHub

Super Admin governs CommerceHub through operational decisions that must pass through existing authority owners.

## Vendor governance

Super Admin can approve, suppress, suspend, reactivate, review, or restrict CommerceHub vendors.

It must not treat vendor governance as POS tenant ownership.

## Marketplace governance

Super Admin can govern marketplace categories, brands, curation, moderation, visibility policy, homepage/shared discovery content, and marketplace configuration.

Visibility still depends on current SQL listing/product/vendor/category/tenant/stock/price authority.

## Customer governance

Super Admin can protect marketplace customers through fraud review, support escalation, refund review, vendor suppression, marketplace emergency containment, and recovery.

Customer identity, order state, payment finality, fulfillment, and refund success remain owned elsewhere.

## Visibility

Super Admin may suppress or restore marketplace visibility only when current authority proves the entity is eligible.

Derivative projections must delete, tombstone, no-op, or fail closed when SQL no longer proves visibility.

## Suspension

Super Admin may suspend vendors for governance or financial reasons.

Suspension affects new marketplace participation and visibility. It must not rewrite existing orders, payment state, fulfillment state, or ledger history.

## Emergency Operations

Super Admin may freeze marketplace payment intake, suppress vendors/listings, block payout/refund execution where readiness gates require, trigger repair/reindex, run diagnostics, or contain marketplace-wide harm.

Emergency action must name its authority and preserve Core lifecycle ownership.

## Repair

Super Admin may trigger CommerceHub repair, rebuild, recovery, reindex, or replay only as SQL-first convergence work.

Repair must never copy Mongo, Redis, Elasticsearch, dashboard, export, support-tool, or queue payload state back into truth.

## Reconciliation

Super Admin may monitor and escalate reconciliation anomalies.

Reconciliation detects and routes uncertainty. It does not create money, override ledger, or complete payment/refund/payout finality without the owning authority.

## Dangerous Operations

Super Admin may operate dangerous primitives only with current platform authority, audit, telemetry, scope, and runbook controls.

Dangerous operations do not become authority shortcuts.

---

# Relationship with POS Admin

This boundary is critical.

Super Admin does not own POS Admin.
Super Admin never manages the whole POS Admin.

POS Admin is the tenant’s operational back office. It owns the tenant-facing operational surface for products, inventory, catalog work, tenant settings, POS sales, purchasing, tenant reports, tenant storefront inputs, shipping configuration, and CommerceHub vendor operations.

A POS tenant is governed by Super Admin only while acting as a CommerceHub Vendor.
Super Admin governs only the CommerceHub Vendor Module inside POS Admin. Everything else remains tenant-owned and Core-authority-owned.

Inside CommerceHub, Super Admin may govern:

- vendor onboarding;
- vendor eligibility;
- marketplace publication admission;
- marketplace visibility;
- COD debt restriction;
- marketplace finance review;
- marketplace payout execution workflow;
- vendor marketplace support and fraud review;
- marketplace emergency containment.

Outside CommerceHub, POS Admin remains independent.

Examples:

- A POS tenant may be suspended from CommerceHub new orders due to COD commission debt while still operating its own POS Admin workflows outside CommerceHub.
- A POS tenant’s marketplace listing may be suppressed while its tenant Storefront catalog remains governed by tenant/Core authority.
- A POS vendor may submit fulfillment intent for its own CommerceHub sub-orders through POS Admin, but the marketplace order lifecycle still owns the mutation.
- Super Admin may not use marketplace governance to rewrite POS inventory, POS accounting, POS orders, or tenant settings outside CommerceHub.

Never confuse “POS tenant as CommerceHub Vendor” with “Super Admin owns POS Admin.”

---

# CommerceHub Vendor Module inside POS Admin

POS Admin has two responsibilities.

The first responsibility is running the tenant business.

The second responsibility is participating in CommerceHub as a Vendor.

The CommerceHub functionality inside POS Admin is an embedded Vendor Module. It is not the CommerceHub authority. It is not the Marketplace backend. It is simply the Vendor operational surface exposed to a POS tenant when that tenant participates in CommerceHub.

```text
POS Admin
├── Tenant Business
│     ├── Products
│     ├── Inventory
│     ├── Storefront
│     ├── Customers
│     ├── Sales
│     ├── Accounting
│     └── Settings
│
└── CommerceHub Vendor Module
      ├── Earnings
      ├── Payments
      ├── Finance
      └── Settings
```

The Vendor Module exists only because the tenant participates inside CommerceHub.

Outside CommerceHub, these screens have no role. They do not manage tenant products, tenant inventory, tenant Storefront behavior, tenant customers, tenant sales, tenant accounting, or tenant settings except where those facts participate in CommerceHub and are admitted by the owning authority.

Super Admin governs the marketplace participation represented through this Vendor Module. Super Admin does not govern the Tenant Business branch of POS Admin.

---

# Marketplace Domain Mapping

This table prevents AI Agents from confusing Marketplace domains with Storefront or tenant POS domains.

| Super Admin Module | Governed Marketplace Area | Vendor Module Surface | Does NOT Govern |
|---|---|---|---|
| Orders | Marketplace Orders | Vendor Order View | Storefront Orders |
| Payments | Marketplace Payments | CommerceHubPayments | Storefront Payment Methods |
| Finance | Marketplace Finance | CommerceHubFinance | Tenant Accounting |
| Payouts | Vendor Payout Governance | CommerceHubEarnings | Tenant Treasury |
| Refunds | Marketplace Refunds | Vendor Refund Visibility | POS Refunds |
| Commission | Marketplace Commission | Vendor Earnings | Product Pricing |
| Marketplace CMS | Marketplace Presentation | Marketplace Settings | Storefront CMS |
| Users | Marketplace Users | Vendor Participation | POS Users |
| Promo | Marketplace Promotions | Marketplace Visibility | Storefront Promotions |

The Super Admin column describes CommerceHub governance modules.

The Vendor Module Surface column describes what the POS tenant may operate as a CommerceHub Vendor.

The Does NOT Govern column names tenant-owned or Storefront-owned areas that must not be pulled into Super Admin authority.

---

# Relationship with Storefront

Storefront belongs to the tenant.

Storefront is the tenant-branded customer system for that tenant’s own commerce. It has its own customer journey, generic order model, tenant currency context, payment certification scope, checkout behavior, and Core authority dependencies.

Super Admin does not manage tenant storefronts directly.

Super Admin may affect Storefront-adjacent facts only when they participate in CommerceHub governance. For example:

- a product from POS/Admin may be eligible for CommerceHub listing only if current product, variant, stock, category, price, and vendor governance authority admit it;
- marketplace visibility may be suppressed even if the product remains visible on the tenant Storefront;
- marketplace policy may block CommerceHub checkout while Storefront checkout remains governed by Storefront/Core rules.

Storefront authority remains with Storefront and Core domain owners.

---

# Relationship with Affiliate

Affiliate is a separate business system.

Affiliate owns traffic generation, attribution, commission lifecycle, payout readiness participation, fraud boundaries, affiliate identity, referral evidence, and affiliate portal operations where certified.

Super Admin may govern marketplace policies affecting Affiliate participation inside CommerceHub only.

It must not assume:

- Storefront affiliate attribution applies to CommerceHub marketplace orders;
- Affiliate commissions can be rewritten from a Super Admin screen;
- Affiliate payout readiness can bypass payment/accounting/reconciliation authority;
- Affiliate identity can operate POS Admin, CommerceHub Vendor, CommerceHub Customer, or Super Admin surfaces.

When CommerceHub and Affiliate intersect, use the Affiliate system guide and Core guides to identify the owning authority. Super Admin remains a governance participant, not the Affiliate authority owner.

---

# Relationship with Service Marketplace

Service Marketplace is an internal POS-to-POS collaboration capability.

It is not CommerceHub, not Marketplace Governance, and not governed by Super Admin unless future architecture explicitly certifies such authority.

---

# Relationship with Payments

Super Admin governs marketplace payment operations; it does not own payment truth.

Inside CommerceHub, Super Admin may:

- configure marketplace payment availability and emergency payment-intake containment;
- observe payment provider availability and provider-health evidence;
- monitor payment exceptions, unresolved external-money evidence, and reconciliation queues;
- review refund and payout execution workflows where marketplace operations require platform review;
- block or pause unsafe outward-money operations through readiness and emergency governance.

Payment authority remains with the Payment lifecycle. Provider authority remains with external providers for their own external side effects only. Refund authority remains with refund/payment lifecycle and settlement lineage. Settlement authority remains with CommerceHub finance, settlement, reconciliation, and ledger authority. Ledger authority remains with Accounting.

Super Admin screens, queues, provider-health indicators, dashboards, payout rows, refund rows, reconciliation candidates, and operator notes are evidence or workflow surfaces. They become business truth only when the owning authority admits them.

Super Admin may govern whether CommerceHub should allow a payment method or pause marketplace payment intake. It may not decide that money was captured, collected, refunded, settled, reversed, posted, or payable.

---

# Relationship with Inventory

Inventory belongs to the tenant and is owned by Inventory authority.

Super Admin governs marketplace visibility. Visibility is not inventory. Inventory is not publication.

CommerceHub marketplace visibility may depend on inventory authority because customers should not discover or buy marketplace items that current stock, variant, warehouse, reservation, or product lineage cannot support. That dependency does not transfer stock ownership to Super Admin.

A product may have stock but be hidden from CommerceHub because marketplace governance, vendor eligibility, category lineage, publication authority, price authority, or emergency policy suppresses it.

A product may have zero stock and therefore disappear from CommerceHub or become ineligible for checkout, even if marketplace governance would otherwise allow it.

Publication is an admitted marketplace listing state. Inventory is stock/reservation/warehouse/variant lineage. A listing can be published but not currently purchasable because inventory authority rejects availability. A product can have inventory but no CommerceHub visibility because publication or governance authority rejects it.

Super Admin may suppress or restore marketplace visibility eligibility. It never modifies stock, reservations, warehouses, inventory movements, variant inventory lineage, or available quantity.

---

# Relationship with Accounting

Super Admin observes and operates CommerceHub financial workflows; Accounting owns financial truth.

Super Admin can observe:

- vendor earnings;
- commission owed and collected;
- COD debt;
- settlement status;
- payout queues and payout execution evidence;
- refund impact;
- reconciliation anomalies;
- finance dashboards and reports.

Accounting owns:

- journals;
- ledger entries;
- financial transactions;
- balances;
- accounting outbox effects;
- outward-money readiness when ledger/accounting coverage is required.

Super Admin never posts accounting entries by operational decision alone. It may submit or recognize governed evidence, but ledger and accounting effects must be produced by the owning finance/accounting authority.

Vendor earnings, debt, settlements, payouts, and commissions are observable through Super Admin, but they are not owned by the Super Admin surface. Dashboards, queue totals, reports, and exports are operational evidence unless recomposed from current SQL/ledger authority.

---

# Super Admin Administration Map

This is a dependency map, not an authority map.

```text
Super Admin Administration
│
├── Marketplace Governance
│   ├── depends on: current Super Admin authority
│   ├── depends on: SQL vendor/listing/category/tenant governance
│   └── exposes: suppression, restoration eligibility, moderation decisions
│
├── Vendor Governance
│   ├── depends on: POS tenant/vendor participation authority
│   ├── depends on: governance status, debt status, visibility status
│   └── exposes: vendor marketplace capability decisions
│
├── Marketplace Catalog Governance
│   ├── depends on: POS catalog/product/variant/category authority
│   ├── depends on: inventory, price, stock, and publication authority
│   └── exposes: CommerceHub listing admission and visibility evidence
│
├── Marketplace Finance
│   ├── depends on: payments, orders, refunds, vendor earnings, ledger
│   ├── depends on: debt recomposition and payout readiness
│   └── exposes: debt, settlement, payout, refund, and risk evidence
│
├── Marketplace Customer Support
│   ├── depends on: CommerceHub customer identity
│   ├── depends on: order/payment/refund/fulfillment lifecycle authority
│   └── exposes: support triage, fraud review, escalation intent
│
├── Marketplace Reports and Analytics
│   ├── depends on: SQL authority and derivative read models
│   └── exposes: operational evidence, not domain truth
│
├── Emergency Operations
│   ├── depends on: current Super Admin authority
│   ├── depends on: Core lifecycle/reconciliation owners
│   └── exposes: containment, freeze, repair, diagnostics, and audit
│
└── Dangerous Operations
    ├── depends on: runbook scope, audit, telemetry, SQL-first authority
    └── exposes: governed repair/rebuild/replay tooling
```

---

# Super Admin Dependency Matrix

| Marketplace Area | Authoritative Source | Super Admin Role | Consumer |
|---|---|---|---|
| Super Admin identity/session | Security/Identity + current SQL platform actor authority | Operates only after current platform authority is proven | Super Admin operations |
| Marketplace governance | SQL marketplace governance + current platform actor authority | Decide governance state, suppression, restoration, moderation | CommerceHub discovery, checkout, support |
| Vendor onboarding | SQL tenant/vendor participation and eligibility authority | Review and admit CommerceHub vendor participation | POS Vendor, CommerceHub |
| Vendor approval | SQL vendor governance/participation state | Approve marketplace participation | CommerceHub discovery and operations |
| Vendor suspension | SQL governance + debt + visibility authority | Restrict marketplace participation | Checkout, discovery, vendor operations |
| Vendor visibility | SQL listing/product/vendor/category/tenant/stock/price/governance authority | Suppress or restore marketplace visibility eligibility | CommerceHub customer surfaces |
| Marketplace categories | SQL category taxonomy and lineage | Govern shared marketplace taxonomy | Navigation, search, listing admission |
| Marketplace brands | SQL catalog/brand authority where participating in CommerceHub | Govern marketplace brand admission/moderation | Search, product detail, reports |
| Marketplace product approval | POS catalog + CommerceHub listing + inventory + price + governance authority | Admit/suppress marketplace publication | CommerceHub discovery and checkout |
| Marketplace moderation | SQL content/listing/review/governance authority | Review and suppress marketplace-visible content | Customers, vendors, support |
| Marketplace reports | SQL orders/payments/refunds/ledger/governance; derivative read models | Consume operational evidence | Operations, finance, support |
| Marketplace analytics | SQL/event-derived analytics read models | Use for decision support | Operations, governance, finance |
| Payment method governance | Payment lifecycle + payment policy + CommerceHub checkout authority | Configure/contain marketplace payment policy | Checkout, finance, customers |
| Commission policy | SQL commission policy/snapshots and finance authority | Govern prospective marketplace commission policy | Finance, vendor reports |
| Vendor commission tracking | SQL financial transactions, order snapshots, ledger | Monitor commission owed/earned | Finance, debt management |
| COD debt | SQL-ledger-derived debt recomposition | Review debt, restrictions, repayment evidence | Checkout, discovery, finance |
| Vendor blocking | SQL debt/governance/visibility/checkout authority | Enforce new-order and visibility restriction | Customers, vendors, operations |
| Vendor recovery | Current SQL governance/debt/listing eligibility | Restore only when authority proves eligibility | Vendors, discovery, checkout |
| Refund governance | Refund/payment/order/settlement/ledger authority | Review and operate workflow intent | Customers, vendors, finance |
| Payout governance | SQL earnings/payout/readiness/external evidence/ledger | Execute/recognize payout workflow when allowed | Vendors, finance, accounting |
| Settlement monitoring | SQL settlement lineage, financial transactions, ledger | Monitor and escalate anomalies | Finance, reconciliation |
| Marketplace audit | SQL audit/evidence/governance/financial records | Preserve operational accountability | Operations, support, forensics |
| Fraud review | Identity, payment, refund, order, debt, payout authority | Investigate, contain, escalate | Support, finance, governance |
| Emergency operations | Current Super Admin authority + Core lifecycle/reconciliation owners | Contain marketplace harm without bypassing owners | CommerceHub, customers, vendors |
| Dangerous operations | SQL-first runbook governance + replay/rebuild guards | Run scoped repair/rebuild/replay tooling | Operations, recovery, audit |

---

# Marketplace Operational Flow

```text
Vendor
↓
Marketplace Publication
↓
Customer Discovery
↓
Order
↓
Payment
↓
Commission
↓
Settlement
↓
Debt
↓
Recovery
```

## Vendor

A POS tenant participates as a CommerceHub Vendor. Super Admin governs marketplace participation only.

## Marketplace Publication

Publication consumes POS catalog/product/variant/stock/price/category evidence and marketplace governance. Super Admin can approve/suppress marketplace visibility but cannot create catalog or stock truth.

## Customer Discovery

Customers discover listings through CommerceHub search, browse, homepage, product detail, recommendations, and category navigation. These surfaces are derivative and must revalidate current SQL authority.

## Order

CommerceHub checkout creates marketplace order and sub-orders under order authority. Super Admin monitors but does not rewrite order state.

## Payment

Payment finality belongs to Payment authority. Super Admin may configure, monitor, contain, or review marketplace payment operations.

## Commission

Commission derives from authoritative marketplace order/sub-order financial snapshots and finance/ledger authority.

## Settlement

Settlement monitoring and payout readiness derive from SQL ledger and financial authority. Super Admin operates workflow steps, not settlement truth itself.

## Debt

COD commission debt is tracked as vendor debt because the vendor received customer cash. Super Admin governs debt restriction and repayment review.

## Recovery

Recovery restores participation only when current debt, governance, listing, tenant, category, product, inventory, and price authority admit it.

---

# Operational Change Propagation

```text
Marketplace Policy
↓
Validation
↓
CommerceHub
↓
Vendor
↓
Customer Experience
↓
Reporting
↓
Recovery
```

## Marketplace Policy

Super Admin changes marketplace policy, governance state, commission policy, category policy, visibility policy, emergency state, or operational configuration.

## Validation

The change must validate current platform actor authority and domain-specific authority. Invalid, stale, cross-tenant, or contradictory evidence fails closed.

## CommerceHub

CommerceHub consumes the accepted policy to alter marketplace admission, visibility, checkout eligibility, finance readiness, reporting, or emergency behavior.

## Vendor

Vendors experience the change as capability, visibility, debt, publication, payout, or support-state changes inside CommerceHub only.

## Customer Experience

Customers may see suppressed listings, blocked checkout for restricted vendors, changed payment availability, refund/support states, or emergency messaging.

## Reporting

Reports and dashboards reflect accepted authority after derivative convergence. They do not become authority.

## Recovery

Recovery uses current SQL authority to restore, delete, tombstone, no-op, retry, or escalate. It does not copy old derivative state back into truth.

---

# Super Admin Decision Flow

Super Admin decisions are governance decisions first. They become business effects only by passing through the owning authority chain.

```text
Super Admin Action
↓
Governance Validation
↓
Current SQL Authority
↓
Lifecycle Owner
↓
SQL Commit
↓
Outbox
↓
Projection
↓
Marketplace Update
```

## Super Admin Action

A platform actor submits governance intent: approve, suppress, restore, configure, pause, review, recognize evidence, trigger repair, or run an emergency operation.

## Governance Validation

The system proves current Super Admin platform authority and confirms the action is inside CommerceHub marketplace scope. Tenant-role claims, frontend controls, route state, cached identity, and old sessions are evidence only.

## Current SQL Authority

The affected vendor, listing, tenant, category, product, inventory, payment, refund, payout, settlement, ledger, or customer fact is reloaded from current SQL authority. If current authority does not prove the action is allowed, the action fails closed.

## Lifecycle Owner

If the action touches protected state, the owning lifecycle or domain authority admits or rejects it. Super Admin does not mutate protected state directly.

## SQL Commit

Accepted governance, evidence, lifecycle, finance, audit, or recovery state becomes durable only at the SQL commit boundary.

## Outbox

The committed change may stage events, repair work, cache invalidation, accounting work, search updates, notifications, or reconciliation activity. Event delivery is trigger evidence, not authority.

## Projection

MongoDB, Redis, Elasticsearch, dashboards, reports, queues, and Super Admin screens converge from SQL. Projection lag is expected; projection resurrection is not allowed.

## Marketplace Update

CommerceHub customer/vendor/operator experience changes only after the admitted authority is reflected through marketplace reads or projections: visibility suppressed/restored, checkout blocked/unblocked, payment availability changed, payout state updated, report refreshed, or recovery completed.

The decision flow answers the QA questions:

- Who governs it? Super Admin, when the concern is CommerceHub governance.
- Who owns it? The Core or CommerceHub authority named by current SQL.
- Who may mutate it? The owning lifecycle/domain authority.
- Who presents it? CommerceHub, POS vendor surfaces, Super Admin screens, reports, projections, or dashboards.
- Who only observes it? Super Admin when viewing finance, order, payment, inventory, accounting, reconciliation, or analytics evidence.
- Who reconciles it? The owning reconciliation/finance/lifecycle path, not the Super Admin screen.

---

# Authority Boundaries

This section is critical.

Super Admin governs CommerceHub.
Super Admin does not become a Core authority owner.
Super Admin governs Marketplace Operations.
POS Admin owns Tenant Operations.
These are two different operational surfaces.
Neither surface replaces the Core lifecycle owners.

## Super Admin NEVER becomes Payment authority

Super Admin cannot mark payments paid, captured, failed, refunded, settled, or reversed by screen action or operational convenience.

Provider callbacks, payment reports, payment dashboards, and Super Admin observations are evidence until admitted by Payment authority.

## Super Admin NEVER becomes Provider authority

External providers, banks, wallets, and carriers own their external side effects. Super Admin may observe provider-health evidence or pause marketplace provider availability, but it cannot fabricate provider execution, provider success, provider failure, transfer completion, or external refund outcome.

## Super Admin NEVER becomes Refund authority

Super Admin may review marketplace refund workflows and emergency-block unsafe refund execution, but refund success, refund capacity, refund reversal, and post-settlement refund effects remain owned by refund/payment authority, settlement lineage, and ledger readiness.

## Super Admin NEVER becomes Settlement authority

Super Admin may monitor settlement anomalies and operate settlement/payout workflows, but settlement truth remains current SQL financial lineage. Live settlement membership, not report totals or header counts, decides settlement scope.

## Super Admin NEVER becomes Inventory authority

Super Admin cannot modify stock, reservations, warehouses, movements, variant inventory lineage, or availability.

CommerceHub visibility may depend on inventory evidence, but inventory truth remains with Inventory authority.

## Super Admin NEVER becomes Accounting authority

Super Admin cannot rewrite ledger, journal entries, financial transactions, settlement lineage, or accounting outbox truth.

Accounting authority remains PostgreSQL ledger/accounting authority.

Super Admin may observe vendor earnings, commissions, debt, settlements, payouts, balances, and finance reports. Observation is not posting authority.

## Super Admin NEVER becomes Order authority

Super Admin cannot rewrite marketplace order/sub-order state, generic order state, customer ownership, vendor allocation, totals, or lifecycle transitions.

Order authority remains with the order lifecycle owner.

## Super Admin NEVER becomes Fulfillment authority

Super Admin cannot mark fulfillment complete, shipped, delivered, cancelled, or reversed outside fulfillment/order authority.

Fulfillment evidence must be admitted by the owning lifecycle.

## Super Admin governs through existing lifecycle owners

Super Admin may submit governance intent, containment, review, repair, reconciliation, or execution evidence.

The actual mutation must enter the owning lifecycle or SQL authority boundary.

Operational governance is not business authority.

---

# Business Rules

Durable Super Admin system rules:

1. Super Admin owns CommerceHub governance, not the whole Shopixy platform.
2. Super Admin governs POS tenants only while they participate as CommerceHub Vendors.
3. POS Admin remains tenant-owned outside CommerceHub.
4. Storefront remains tenant-owned and is not directly managed by Super Admin.
5. Affiliate is a separate business system; Super Admin may govern Affiliate participation only where it affects CommerceHub and is certified.
6. PostgreSQL remains the authoritative write model for CommerceHub governance, finance, orders, payments, inventory, accounting, refunds, payouts, and audit truth.
7. Super Admin identity is current SQL platform actor authority plus current governance state and token/session validity; JWTs and frontend state are evidence only.
8. Marketplace governance may suppress, restore eligibility, curate, rank, recommend, or moderate, but public visibility still requires current SQL listing/product/vendor/category/tenant/price/stock authority.
9. Vendor approval admits marketplace participation only; it does not approve all products, prices, orders, payments, or inventory.
10. Vendor suspension blocks new CommerceHub participation and public visibility where applicable; it does not rewrite existing orders.
11. COD commission debt exists because the vendor receives COD cash and owes marketplace commission.
12. Super Admin does not receive COD money immediately; it tracks and governs commission debt.
13. COD debt restriction is SQL-ledger-derived and must not be computed from dashboards, caches, or frontend state.
14. Online payment payout is outward money movement and must pass readiness gates before Super Admin execution recognition.
15. Settlement membership uses live SQL lineage, not denormalized header counts or dashboard summaries.
16. Payout execution recognition requires readiness, ledger-backed payable truth, currency safety, external transfer evidence, and audit.
17. Refund governance cannot bypass payment/refund lifecycle, settlement lineage, payout-overlap checks, or ledger readiness.
18. Emergency operations may contain marketplace harm but must not bypass lifecycle owners.
19. Repair, reindex, rebuild, and replay are SQL-first convergence tools.
20. Dangerous operations are governed primitives, not authority shortcuts.
21. Reports and analytics are operational evidence unless recomposed from current SQL authority.
22. Super Admin cannot copy derivative state into truth.
23. Current Super Admin authority is required for non-read platform operations.
24. Governance actions must be auditable.
25. If Core authority is missing, stale, contradictory, or tenant-unsafe, Super Admin actions must fail closed.

---

# Never Assume

- Never assume Super Admin owns POS Admin.
- Never assume Super Admin owns the whole platform.
- Never assume Super Admin governs the whole POS Admin instead of only the CommerceHub Vendor Module.
- Never assume Vendor Module equals CommerceHub Authority.
- Never assume CommerceHub Payments are Storefront Payment Methods.
- Never assume Marketplace Finance is Tenant Accounting.
- Never assume Marketplace Orders are Storefront Orders.
- Never assume Marketplace Refunds are POS Refunds.
- Never assume Super Admin owns Storefront.
- Never assume Super Admin owns Affiliate.
- Never assume Super Admin owns Inventory.
- Never assume Super Admin owns Orders.
- Never assume Super Admin owns Payments.
- Never assume Super Admin owns Accounting.
- Never assume Super Admin owns Fulfillment.
- Never assume Super Admin can bypass payments.
- Never assume Super Admin can modify inventory.
- Never assume marketplace visibility equals inventory availability.
- Never assume inventory availability equals CommerceHub publication.
- Never assume a product with stock must be visible in CommerceHub.
- Never assume a published marketplace listing remains purchasable when current inventory authority reports zero available stock.
- Never assume Super Admin can rewrite orders.
- Never assume Super Admin can bypass lifecycle services.
- Never assume a Super Admin screen can mark provider money final.
- Never assume provider health or provider dashboard status is payment finality.
- Never assume Super Admin can post journal entries or ledger corrections.
- Never assume a vendor suspended in CommerceHub is disabled in the tenant’s POS Admin outside CommerceHub.
- Never assume tenant Storefront visibility and CommerceHub marketplace visibility are the same thing.
- Never assume Storefront affiliate attribution applies to CommerceHub marketplace orders.
- Never assume COD means Super Admin already received customer cash.
- Never assume COD delivery alone proves marketplace commission collection.
- Never assume commission debt can be reduced without admitted repayment/settlement/accounting authority.
- Never assume vendor debt dashboards are debt authority by themselves.
- Never assume vendor recovery can restore old publication intent without current SQL eligibility.
- Never assume marketplace search visibility proves governance approval.
- Never assume marketplace reports are ledger truth.
- Never assume settlement header counts are settlement membership authority.
- Never assume payout queue rows are executable without readiness gates.
- Never assume emergency rollback can copy Mongo, Redis, Elasticsearch, queue payload, export, dashboard, or support-tool state into SQL truth.
- Never assume dangerous operations are safe just because Super Admin can access them.

---

# QA Checklist

System understanding checklist only:

- [ ] Did I confirm the behavior belongs to CommerceHub governance rather than POS Admin, Storefront, Affiliate, Inventory, Orders, Payments, Accounting, or Fulfillment?
- [ ] Can I distinguish Tenant Business from the CommerceHub Vendor Module?
- [ ] Can I distinguish Marketplace Orders from Storefront Orders?
- [ ] Can I distinguish Marketplace Finance from Tenant Accounting?
- [ ] Can I distinguish Marketplace Payments from Storefront Payment Methods?
- [ ] Can I distinguish Governance from Business Authority?
- [ ] Did I identify which Core guide owns the underlying business rule?
- [ ] Did I separate Super Admin governance from Core business authority?
- [ ] Did I verify that Super Admin is acting on a CommerceHub participation concern, not the tenant’s entire POS business?
- [ ] Did I verify that a POS tenant is governed only while acting as a CommerceHub Vendor?
- [ ] Did I avoid applying Storefront rules directly to CommerceHub marketplace orders?
- [ ] Did I avoid applying Affiliate Storefront attribution directly to CommerceHub marketplace orders unless separately certified?
- [ ] Did I identify whether the disputed fact is governance state, visibility state, order state, payment state, inventory state, fulfillment state, refund state, payout state, settlement state, ledger state, or derivative presentation?
- [ ] Did I check whether marketplace visibility re-enters current SQL authority instead of trusting search/cache/Mongo/homepage state?
- [ ] Did I distinguish marketplace visibility from inventory availability and publication status?
- [ ] Did I check whether vendor suspension affects only new marketplace participation and visibility, not existing order lifecycle truth?
- [ ] Did I identify who may mutate the fact and who only presents or observes it?
- [ ] Did I identify whether reconciliation belongs to payments, refunds, payouts, settlement, accounting, projections, or marketplace governance?
- [ ] Did I distinguish COD commission debt from online payment vendor payable?
- [ ] Did I confirm Super Admin does not receive COD cash immediately?
- [ ] Did I confirm vendor debt is SQL-ledger-derived, not dashboard-derived?
- [ ] Did I confirm payout execution is readiness-gated and evidence-backed?
- [ ] Did I confirm refund governance re-enters refund/payment authority?
- [ ] Did I confirm emergency action is containment, not lifecycle bypass?
- [ ] Did I confirm repair/reindex/rebuild/replay is SQL-first convergence?
- [ ] Did I confirm dangerous operations are scoped, audited, and not authority shortcuts?
- [ ] Did I classify dashboards, reports, analytics, frontend state, and projections as evidence unless current SQL authority admits them?
- [ ] Did I fail closed when authority is stale, missing, contradictory, or tenant-unsafe?

---

# Common Failure Scenarios

## Vendor exceeds commission debt threshold

Business failure: Vendor COD commission debt reaches the marketplace threshold but checkout or discovery still allows new CommerceHub sales.

Correct behavior: Current SQL debt authority restricts new marketplace orders and suppresses public visibility where certified. Existing orders continue through lifecycle owners.

## Vendor suspended incorrectly

Business failure: A vendor is suppressed from CommerceHub without current governance, debt, tenant, or visibility authority.

Correct behavior: Suspension must be backed by current SQL governance/debt authority and audited. If authority is missing or contradictory, fail closed and escalate.

## Marketplace restriction not removed after payment

Business failure: Vendor pays commission debt, but restriction remains despite current SQL debt being below the required boundary and governance eligibility being valid.

Correct behavior: Recovery should recompose current SQL debt, repayment/settlement evidence, tenant/vendor status, and visibility eligibility. If authority proves recovery, marketplace restriction is removed; if not, fail closed.

## COD commission mismatch

Business failure: COD commission debt does not match SQL order/sub-order commission, refund, repayment, adjustment, and settlement evidence.

Correct behavior: Debt recomposition uses SQL financial facts and ledger evidence. Dashboard or cache mismatch is evidence, not debt truth.

## Settlement mismatch

Business failure: Settlement report, header count, payout summary, or dashboard total disagrees with live SQL settlement/order/ledger lineage.

Correct behavior: Current SQL lineage and ledger authority win. Denormalized counts and reports are metadata/evidence only.

## Marketplace visibility inconsistency

Business failure: Search, homepage, recommendations, autocomplete, product detail, or category browse shows a vendor/listing that current SQL governance suppresses.

Correct behavior: Public visibility must re-enter current SQL listing/product/vendor/category/tenant/price/stock/governance authority and delete/tombstone/no-op stale projections.

## Vendor recovery

Business failure: Vendor reactivation blindly restores old listing publication after product, tenant, category, stock, price, or governance eligibility changed.

Correct behavior: Recovery restores only when current SQL authority proves eligibility. Otherwise stale restore intent is cleared or fails closed.

## Emergency freeze

Business failure: Emergency operation freezes marketplace behavior but bypasses payment, order, fulfillment, refund, payout, settlement, inventory, or ledger owners.

Correct behavior: Emergency operations contain marketplace harm while preserving lifecycle ownership and SQL authority.

## Dangerous operation misuse

Business failure: Global repair, direct SQL/bulk mutation, raw search write, cache invalidation, replay forcing, derivative rollback, or all-tenant rebuild becomes a shortcut to mutate business truth.

Correct behavior: Dangerous operations are scoped, audited, SQL-first, telemetry-visible, and runbook-governed. They never create Core truth from derivative evidence.

## Super Admin direct lifecycle override

Business failure: Super Admin marks an order delivered, payment paid, refund successful, payout completed, inventory adjusted, or ledger corrected without the owning lifecycle/authority.

Correct behavior: Super Admin submits governed intent or evidence. The owning lifecycle or SQL authority admits or rejects it.

## POS Admin ownership confusion

Business failure: QA reports that a CommerceHub vendor suspension should disable the tenant’s entire POS Admin business.

Correct behavior: Super Admin governs the POS tenant only as a CommerceHub Vendor. Outside CommerceHub, POS Admin remains tenant-autonomous.

## Storefront ownership confusion

Business failure: Super Admin marketplace suppression is expected to hide or edit the tenant Storefront directly.

Correct behavior: Storefront belongs to the tenant. CommerceHub suppression governs marketplace visibility only unless another owning authority separately acts.

## Affiliate ownership confusion

Business failure: Super Admin modifies marketplace policy and QA assumes Affiliate attribution, commission, or payout truth changed globally.

Correct behavior: Affiliate remains a separate system. CommerceHub-affecting marketplace policy does not rewrite Affiliate truth outside certified boundaries.

---

# Source Documents

Authoritative Core / Knowledge Base documents:

- `.ai/hermes/INDEX.md`
- `.ai/hermes/02-architecture.md`
- `.ai/hermes/03-inventory.md`
- `.ai/hermes/04-payments.md`
- `.ai/hermes/05-orders.md`
- `.ai/hermes/06-security-identity.md`
- `.ai/hermes/07-platform-overview.md`
- `.ai/hermes/08-qa-philosophy.md`
- `.ai/hermes/systems/commercehub/README.md`
- `.ai/hermes/systems/pos-admin/README.md`
- `.ai/hermes/systems/storefront/README.md`
- `.ai/hermes/systems/affiliate/README.md`

Authoritative `.ai-context` files:

- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/CODEX_RULES.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/KNOWN_RISKS.md`

Super Admin certification findings:

- `docs/architecture/super-admin-platform-authority-certification-findings.md`
- `docs/architecture/super-admin-operational-chaos-certification-findings.md`
- `docs/architecture/commercehub-super-admin-session-authority-certification-findings.md`
- `docs/architecture/super-admin-identity-session-security-certification-findings.md`
- `docs/architecture/emergency-governance-certification-findings.md`
- `docs/architecture/commercehub-privileged-authority-certification-findings.md`
- `docs/architecture/commercehub-authority-scope-isolation-certification-findings.md`

CommerceHub governance and marketplace authority documents:

- `docs/architecture/marketplace-governance-authority-certification-findings.md`
- `docs/architecture/marketplace-governance-certification-findings.md`
- `docs/architecture/marketplace-governance-console-certification-findings.md`
- `docs/architecture/marketplace-operations-console-certification-findings.md`
- `docs/architecture/merchant-marketplace-governance-precedence-certification-findings.md`
- `docs/architecture/marketplace-category-taxonomy-certification-findings.md`
- `docs/architecture/marketplace-analytics-authority-certification-findings.md`
- `docs/architecture/marketplace-audit-compliance-forensics-certification-findings.md`
- `docs/architecture/marketplace-forensic-evidence-hardening-certification-findings.md`
- `docs/architecture/marketplace-production-readiness-certification-findings.md`
- `docs/architecture/commercehub-vendor-governance-authority-certification-findings.md`
- `docs/architecture/commercehub-vendor-authority-certification-findings.md`
- `docs/architecture/commercehub-vendor-lifecycle-authority-certification-findings.md`
- `docs/architecture/commercehub-vendor-visibility-authority-certification-findings.md`
- `docs/architecture/commercehub-vendor-financial-participation-authority-certification-findings.md`
- `docs/architecture/commercehub-vendor-capability-authority-certification-findings.md`
- `docs/architecture/commercehub-vendor-eligibility-authority-certification-findings.md`
- `docs/architecture/commercehub-vendor-scope-isolation-authority-certification-findings.md`
- `docs/architecture/commercehub-emergency-operations-certification-findings.md`
- `docs/architecture/dangerous-operational-primitives.md`
- `docs/architecture/global-search-reindex-recovery-certification-findings.md`

CommerceHub finance, COD, settlement, payout, refund, and order authority documents:

- `docs/architecture/commercehub-finance-risk-certification-findings.md`
- `docs/architecture/commercehub-cod-risk-control-certification-findings.md`
- `docs/architecture/commercehub-settlement-payout-authority-certification-findings.md`
- `docs/architecture/commercehub-settlement-merchant-balance-certification-findings.md`
- `docs/architecture/commercehub-payout-reconciliation-certification-findings.md`
- `docs/architecture/commercehub-refund-authority-certification-findings.md`
- `docs/architecture/commercehub-refund-financial-reversal-certification-findings.md`
- `docs/architecture/commercehub-payment-authority-certification-findings.md`
- `docs/architecture/commercehub-marketplace-order-authority-certification-findings.md`
- `docs/architecture/commercehub-marketplace-order-final-authority-certification-findings.md`
