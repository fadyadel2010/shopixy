# Shopixy Affiliate System Guide

Audience: Senior QA Engineers, AI agents, and contributors who need to understand Affiliate as one complete Shopixy business system.

This is a SYSTEM guide.

It is not a Core guide, not a feature guide, not a domain implementation guide, and not a UI guide. It explains how the Affiliate system operates across Shopixy and points to the Core guides that own the detailed business rules.

Read first:

- `.ai/hermes/INDEX.md`
- `.ai/hermes/02-architecture.md`
- `.ai/hermes/03-inventory.md`
- `.ai/hermes/04-payments.md`
- `.ai/hermes/05-orders.md`
- `.ai/hermes/06-security-identity.md`
- `.ai/hermes/07-platform-overview.md`
- `.ai/hermes/08-qa-philosophy.md`

Authority precedence remains:

1. `.ai-context/*`
2. Owning Core guide
3. `docs/architecture/*` certification evidence
4. Source code as current-behavior evidence
5. README/comments/UI assumptions

---

# Purpose

Affiliate is the Shopixy business system that turns external traffic generation into attributable storefront conversions, commission eligibility, commission lifecycle events, payout readiness, payout requests, and payout evidence.

Affiliate exists to let approved affiliate actors promote tenant storefront products and earn commission when current backend authority proves that a referred customer journey produced an eligible order line, recognized payment/accounting value, delivery approval, and payoutable balance.

This guide teaches Affiliate as a complete operating system:

- who participates;
- what Affiliate owns;
- what Core systems it consumes;
- how referral evidence becomes attribution;
- how attribution becomes commission evidence;
- how commission becomes withdrawable;
- how payout readiness fails closed;
- how QA should reason about fraud, duplicate attribution, refunds, chargebacks, reversals, and payout rollback.

It intentionally does not repeat Core mechanics. For detailed rules, follow the referenced Core guides.

---

# What is Affiliate?

Affiliate is a traffic-generation and commission-economics system over the tenant Storefront checkout model.

It is not simply referral links.

Affiliate has its own:

- participants: visitors, customers, affiliates, POS tenants, finance operators, support, and Super Admins;
- identity model: affiliate users/accounts are separate from POS Admin, Storefront customer, CommerceHub customer, and Super Admin identities;
- operational model: POS Admin configures and manages tenant-side affiliate operations; affiliate actors operate their own portal; finance/support review payout evidence and disputes;
- attribution model: referral evidence is admitted by backend authority, preserved as protected evidence, and durably stamped only at checkout;
- commission lifecycle: candidate, earned/pending, approved, reversed, and paid evidence;
- payout readiness model: outward-money readiness fails closed under unresolved money, critical backlog, ambiguous scope, insufficient balance, stale wallet evidence, or currency conflict;
- governance model: account approval, suspension, closure, category/taxonomy governance, tenant configuration, fraud review, and support escalation;
- fraud boundaries: URL/header/browser evidence, duplicate clicks, replay, stale cookies, forged links, modified cart/checkout DTOs, dashboard totals, analytics, and payout proof are never authority;
- operational responsibilities: monitoring, audit, recovery, reconciliation, payout review, payment proof, support, and reporting without owning accounting settlement or external transfer finality.

## Business value

Affiliate creates a controlled growth channel for tenant storefronts:

- Affiliates promote products and campaigns.
- Tenants receive traffic and conversions.
- Commission is paid only when current Core authority proves eligible value.
- Finance can review payout requests using durable evidence.
- The platform can stop fraud, replay, stale attribution, and unsafe outward money movement before they become financial truth.

## What Affiliate owns

Affiliate owns the business operation of:

- affiliate registration and account lifecycle;
- affiliate identity/session authority;
- referral evidence admission;
- promotion/referral link identity;
- campaign/referral context;
- order-bound affiliate attribution evidence;
- checkout-time commission snapshot inputs for admitted order lines;
- commission ledger lifecycle after Core order/payment/accounting gates pass;
- commission reversal evidence after refunds/chargebacks/negative adjustments;
- affiliate balance calculation from SQL ledger authority;
- payout request admission;
- payout readiness participation;
- payout evidence admission/allocation workflow;
- paid commission evidence;
- affiliate dashboard and analytics presentation;
- affiliate notifications as derivative outputs.

## What Affiliate does NOT own

Affiliate never owns:

- Storefront checkout;
- order creation or order lifecycle;
- payment method eligibility, payment finality, refunds, chargebacks, payment settlement authority, or reconciliation;
- inventory stock, warehouse, reservation, fulfillment, or delivery truth;
- accounting ledger truth or financial settlement;
- customer identity;
- tenant/store identity;
- shipping/carrier authority;
- tax, coupon, promotion, price, or product lifecycle authority;
- CommerceHub marketplace order/sub-order authority;
- external bank/provider transfer finality.

Affiliate consumes those domains through backend authority and fails closed when required proof is missing.

## How Affiliate differs from Storefront

Storefront is the tenant-branded customer buying surface. It owns customer journey presentation and submits checkout intent.

Affiliate drives traffic into Storefront and supplies referral/attribution evidence. Affiliate does not create a separate checkout, payment, shipping, order, or inventory path.

A referred conversion is still a Storefront-origin generic order with immutable affiliate attribution.

## How Affiliate differs from CommerceHub

CommerceHub is the shared multi-vendor marketplace where vendors sell through a marketplace order/sub-order model.

Affiliate is traffic generation and commission economics for Storefront-origin conversions. Affiliate attribution does not make a CommerceHub sub-order an affiliate order unless a separate certified marketplace-affiliate authority model exists.

## How Affiliate differs from POS Admin

POS Admin is the tenant operator surface. It configures affiliate settings, reviews affiliates, reviews payout requests, and submits payout proof intent.

Affiliate is the business system being operated. POS Admin is not the affiliate authority itself; backend Affiliate, Orders, Payments, Accounting, Inventory, Security, and readiness boundaries remain decisive.

---

# Primary Responsibilities

Each responsibility below states why it exists, which Core guide owns the detailed business rules, and which system operates it.

| Responsibility | Why it exists | Core guide that owns the business rules | Operating system / surface |
|---|---|---|---|
| Affiliate Registration | Creates a candidate affiliate identity without automatically granting authenticated or commission authority. | `06-security-identity.md`; `02-architecture.md` for SQL authority. | Affiliate portal + backend affiliate identity authority. |
| Affiliate Identity | Keeps affiliate actors isolated from POS Admin, Storefront customers, CommerceHub customers, and Super Admins. | `06-security-identity.md`. | Affiliate portal; backend identity/session authority. |
| Affiliate Attribution | Converts admitted referral evidence into one immutable order-bound attribution at checkout. | `05-orders.md`; `02-architecture.md`; `06-security-identity.md`. | Affiliate system + Storefront checkout transaction. |
| Referral Links | Provide shareable referral/campaign evidence, not checkout or order authority. | `06-security-identity.md` for trust boundary; `05-orders.md` for attribution; `07-platform-overview.md` for surface relationship. | Affiliate portal + backend referral/link authority; Storefront entry consumes it. |
| Campaign Tracking | Measures traffic, clicks, funnel movement, and conversions as evidence and reporting. | `02-architecture.md` for derivative evidence; `08-qa-philosophy.md` for evidence-vs-truth. | Affiliate analytics/reporting; consumed by affiliates, POS tenants, support. |
| Commission Calculation | Captures immutable commission snapshots from final checkout authority for admitted order lines. | `05-orders.md` for checkout/order-line authority; `04-payments.md` for money gates; `02-architecture.md` for SQL authority. | Affiliate commission system consumes Storefront checkout authority. |
| Commission Allocation | Binds commission lifecycle rows to the correct affiliate, store tenant, order, and order item. | `05-orders.md`; `04-payments.md`; `02-architecture.md`. | Backend Affiliate commission ledger workflow; Accounting/Payments remain settlement authority. |
| Commission Lifecycle | Moves commission through candidate, earned/pending, approved, reversed, and paid evidence without turning paid evidence into external transfer finality. | `04-payments.md` for financial lifecycle/accounting/readiness; `05-orders.md` for order/delivery/refund effects. | Affiliate commission system. |
| Commission Approval | Makes commission withdrawable only after current delivery, payment, tenant, and accounting authority prove it. Inventory matters only where fulfillment/delivery evidence depends on inventory lineage. | `05-orders.md`; `04-payments.md`; `03-inventory.md` where fulfillment/inventory evidence matters. | Backend Affiliate approval/recovery workflow; POS/Admin may observe. |
| Commission Reversal | Removes or reduces payable commission when refunds, partial refunds, chargebacks, or reversal authority reduce the original sale value. | `04-payments.md` for refunds/chargebacks/reconciliation; `05-orders.md` for refund/order lineage. | Affiliate commission reversal workflow consuming payment/refund authority. |
| Fraud Detection | Prevents forged attribution, self-referral abuse, identity collision, multi-attribution ambiguity, campaign abuse, duplicate referral admission, replayed links, duplicate payouts, stale browser evidence, tenant leaks, manual override abuse, and unsafe payout proof. | `06-security-identity.md`; `04-payments.md`; `08-qa-philosophy.md`. | Affiliate, Security, Finance, Support, Super Admin governance. |
| Payout Readiness | Blocks outward money until current SQL proves commission, balance, wallet, accounting, unresolved-money, currency, backlog, and evidence readiness. Affiliate participates in readiness; Payments/Accounting remain authoritative for settlement and ledger truth. | `04-payments.md`; `06-security-identity.md`; `02-architecture.md`. | Backend Affiliate payout readiness/request authority + Finance/POS Admin review surface. |
| Payout Requests | Lets affiliates request withdrawal against backend recomputed available balance, not displayed dashboard totals. | `04-payments.md`; `06-security-identity.md`. | Affiliate portal request intent; backend payout request authority. |
| Payout Evidence | Records transfer proof/evidence and allocates it without allowing one real-world transfer to fund duplicate local withdrawals. Evidence admission is not bank/provider finality or accounting settlement. | `04-payments.md`; `02-architecture.md`; `06-security-identity.md`. | POS Admin/Finance evidence submission; backend Affiliate evidence/allocation authority; Payments/Accounting settlement authority. |
| Affiliate Analytics | Shows performance, clicks, conversions, commission, and payout history as derivative reporting. | `02-architecture.md`; `08-qa-philosophy.md`; `04-payments.md` where financial truth is shown. | Affiliate dashboard, POS Admin reports, analytics/read models. |
| Affiliate Notifications | Communicates account, attribution, commission, approval, reversal, and payout events. Notifications are outputs, never authority. | `07-platform-overview.md`; relevant originating Core guide. | Notification system consuming Affiliate/Core lifecycle events. |
| Affiliate Dashboard | Presents profile, links, campaigns, earnings, requests, and evidence; it cannot create truth from UI state. | `06-security-identity.md`; `04-payments.md`; `02-architecture.md`; `08-qa-philosophy.md`. | Affiliate portal; consumed by affiliates. |

---

# Main Actors

## Visitor

An unauthenticated person who arrives through a referral link, direct Storefront navigation, search, social media, or another channel.

A visitor can carry referral candidates. A visitor does not own attribution, customer identity, cart authority, order authority, or payment authority.

## Customer

A buyer or guest completing Storefront checkout.

Customer identity and checkout authority are Storefront/Core responsibilities. The customer journey may be affiliate-attributed, but customer action does not let Affiliate bypass checkout, payment, inventory, shipping, or order rules.

## Affiliate

A separate platform actor with its own account, session, referral code/link relationships, dashboard, profile, payout wallet candidate, analytics, commission history, and payout request capability.

Affiliate identity is separate from POS Admin users, customers, CommerceHub customers, and Super Admins.

## POS Tenant

The store/business that enables affiliate participation for its products and funds commission economics from eligible Storefront conversions subject to backend Affiliate, payout readiness, Payments, Finance, and Accounting authority.

Tenant operators use POS Admin to configure campaign/commission inputs, approve affiliates where applicable, review payout requests, and submit payment proof intent.

## Finance

The operational actor or team responsible for payout review, transfer evidence, readiness blockers, reconciliation escalation, ledger/accounting checks, and financial controls.

Finance operates evidence and review workflows but does not bypass payment/accounting/readiness authority.

## Support

The operational actor handling missing attribution complaints, stale dashboard issues, rejected requests, suspicious referral behavior, payout questions, and escalation to Finance or Super Admin.

Support consumes evidence and cannot rewrite attribution, commission, payment, order, or ledger truth.

## Super Admin

The platform actor for global governance, dangerous operations, platform-wide repair, global affiliate taxonomy/category governance, and emergency containment.

Super Admin authority is platform-scoped and governed. It does not create a shortcut around Affiliate, Orders, Payments, Inventory, or Accounting lifecycle owners.

---

# Visitor Journey

```text
Visitor
↓
Affiliate Link
↓
Storefront
↓
Attribution
↓
Checkout
↓
Order
↓
Commission
↓
Refund
↓
Chargeback
↓
Commission Reversal
↓
Payout Readiness Impact
↓
Recovery / Reconciliation
```

Ownership at each stage:

1. Visitor
   - Presentation/intent only.
   - No business authority.
   - Security/Identity guide owns actor/session rules.

2. Affiliate Link
   - Referral candidate evidence.
   - Backend Affiliate authority resolves whether the link is current, active, tenant/store-scoped, product-scoped where required, and eligible.
   - URL/header/browser state is not authority.

3. Storefront
   - Storefront owns customer journey presentation and cart/checkout intent.
   - Storefront entry consumes admitted affiliate evidence.
   - Storefront does not let Affiliate own product visibility, price, stock, cart, checkout, payment, or order truth.

4. Attribution
   - Attribution begins as backend-admitted protected evidence.
   - Durable attribution begins only when checkout stamps immutable order attribution and creates the canonical attribution row in the same authority boundary.
   - One order may have only one canonical affiliate attribution.

5. Checkout
   - Checkout is Storefront/Core authority.
   - Final checkout recomputes pricing, discounts, payment eligibility, stock/reservation, shipping, promotion/coupon, affiliate evidence, and commission policy from current SQL.
   - Stale cart/browser/Redis/DTO evidence fails closed.

6. Order
   - The resulting order is a generic Storefront-origin order with affiliate attribution evidence.
   - Orders guide owns order lifecycle, cancellation, fulfillment, returns, refunds, and projections.

7. Commission
   - Affiliate consumes immutable order-line snapshots plus payment/accounting/order/delivery evidence.
   - Commission becomes payable only through the Affiliate commission lifecycle and payout readiness gates.

8. Refund
   - Refund authority remains Payments/Refunds/Core authority.
   - Affiliate consumes refund evidence only after current SQL refund/payment/order authority admits it.
   - Refunded value cannot remain payable merely because it was previously attributed.

9. Chargeback
   - Chargeback and provider-dispute evidence belong to Payments/Reconciliation authority.
   - Affiliate payout readiness must fail closed while provider cash/refund truth is ambiguous.

10. Commission Reversal
   - Affiliate records bounded reversal evidence against prior commission authority.
   - Reversal cannot exceed the authoritative earned/approved value and cannot be fabricated from dashboard or operator input.

11. Payout Readiness Impact
   - Payout readiness recomputes net approved value after refunds, chargebacks, reversals, withdrawals, active requests, wallet snapshot, currency, scope, and accounting blockers.
   - A previously visible balance is not payout authority.

12. Recovery / Reconciliation
   - Recovery reloads current SQL attribution, order, payment, refund, commission, payout, and accounting evidence.
   - Uncertain external money enters reconciliation/manual review instead of false success, false failure, or duplicate outward money.

---

# Affiliate Journey

```text
Registration
↓
Approval / Verification
↓
Campaign Creation
↓
Sharing Links
↓
Generating Traffic
↓
Conversions
↓
Commission
↓
Payout Readiness
↓
Payout Request
↓
Affiliate Payout Evidence
↓
Accounting Recognition
↓
Post-Payout Refund
↓
Chargeback
↓
Negative Balance / Recovery
↓
Governance / Suspension
↓
Dispute Resolution
```

## Registration

Affiliate identity begins when backend registration accepts the candidate and persists the affiliate account/user. Registration does not automatically create authenticated session authority or commission authority.

## Approval / Verification

Email verification, account status, suspension, closure, deletion, session refresh, and login admission are backend identity authority. Unverified, inactive, suspended, closed, or deleted accounts fail closed for protected capabilities.

## Campaign Creation

Campaign/link/product selection is backend-admitted. Frontend selection, product cards, filters, search results, modal state, copied text, and stale link lists are candidate/presentation only.

## Sharing Links

Links are shareable referral evidence. A copied URL can become attribution only when backend Storefront entry and checkout revalidation admit current link, product, affiliate, tenant, and policy authority.

## Generating Traffic

Traffic/clicks are useful analytics. They are not conversions, orders, commission, or payment truth.

## Conversions

A conversion is a Storefront checkout result with durable affiliate attribution. The conversion is not owned by analytics, cookies, browser history, affiliate dashboard, or referral query string.

## Commission

Commission starts as checkout-time admitted order-line snapshot evidence, then becomes earned/pending only after payment/accounting gates, approved only after delivery/order/payment/accounting gates, reversed after refund/chargeback/reversal authority, and paid evidence only after payout authority admits the local lifecycle effect. Paid evidence is not external transfer finality or accounting settlement.

## Payout Readiness

Readiness recomputes from current SQL commission, balance, request, wallet, evidence, accounting, unresolved external-money, backlog, currency, and tenant/scope authority. It fails closed.

## Payout Request

The affiliate submits amount intent only. Backend payout request authority recomputes raw available balance, wallet snapshot, duplicate active request reservation, store scope, currency, and request lifecycle.

## Affiliate Payout Evidence

Affiliate payout evidence is outward-transfer proof candidate/evidence. It is admitted, verified, bound, and allocated by backend payout/evidence authority, while external bank/provider finality remains external evidence and Accounting/Payments remain authoritative for settlement and ledger truth.

## Accounting Recognition

Accounting recognition is not Affiliate-owned. Affiliate paid evidence, withdrawal rows, reversal rows, and transfer evidence must converge through Accounting/Payments authority before they become ledger or settlement truth.

## Post-Payout Refund

Post-payout refunds do not erase historical payout evidence or pretend external money never moved. They create bounded reversal, recovery, negative balance, reconciliation, or accounting effects under current SQL authority.

## Chargeback

Chargeback and provider-dispute evidence remain Payment/Reconciliation authority. Affiliate consumes admitted chargeback impact and must block payout readiness while external-money truth is unresolved.

## Negative Balance / Recovery

Negative balance and recovery are controlled SQL ledger effects, not dashboard corrections. Recovery recomputes current commission, reversal, payout, wallet, accounting, and external-money evidence before allowing future payout readiness.

## Governance / Suspension

Fraud, suspicious payout proof, self-referral abuse, repeated attribution abuse, or unresolved financial risk can suspend protected affiliate capabilities. Governance does not rewrite order, payment, attribution, commission, or ledger truth outside the owning authority.

## Dispute Resolution

Disputes are evidence-gathering and operational review. Support, Finance, POS Admin, or Super Admin may collect evidence and escalate, but resolution must re-enter backend Affiliate, Payments, Orders, Security, and Accounting authority and fail closed when proof is missing.

---

# Cross-System Relationships

## Storefront

Affiliate drives Storefront traffic and supplies referral evidence. Storefront owns browsing, cart, checkout intent, and customer journey presentation. Checkout remains Core/Storefront authority.

Failure risk: treating referral query parameters, browser storage, copied URLs, or banners as attribution authority.

## POS Admin

POS Admin is the tenant operational surface for affiliate configuration, affiliate management, commission configuration, payout review, payment proof intent, and operational reporting.

Failure risk: treating POS Admin UI values or frontend DTOs as commission, payout, balance, or transfer authority.

## CommerceHub

CommerceHub is a marketplace system with separate marketplace order/sub-order authority and EGP-only finance rules. Affiliate Storefront attribution does not automatically apply to CommerceHub marketplace sub-orders.

Failure risk: copying Storefront affiliate attribution into marketplace ownership.

## Orders

Orders own checkout result, order identity, lifecycle, cancellation, return, refund integration, fulfillment, and projections. Affiliate attribution is attached to a Storefront-origin order; it does not own order state.

## Payments

Payments own payment finality, provider evidence admission, refunds, chargebacks, reconciliation, settlement, and outward-money readiness. Affiliate commission and payout consume payment/accounting proof.

## Inventory

Inventory owns stock, reservations, warehouses, and fulfillment stock effects. Affiliate may require product availability for eligibility and checkout admission, but it never owns stock or reservations.

## Catalog / Products

Catalog and product authority own product lifecycle, publication, category lineage, product identity, media, variants, and product eligibility facts. Affiliate may expose eligible products and campaigns, but copied product cards, stale search results, and affiliate product lists are presentation until backend authority revalidates them.

## Pricing

Pricing authority owns current price, taxable/discountable value, unit pricing, and checkout money snapshots. Affiliate commission can consume an immutable checkout snapshot, but it cannot decide price, tax, discount, shipping, payment fee, or order total truth.

## Promotions / Coupons

Promotion and coupon authority owns promotion eligibility, coupon usage, discount limits, and conflict rules. Affiliate campaign or commission context does not override promotion/coupon authority; checkout recomputes both from current SQL.

## Shipping

Shipping owns shipping-method eligibility, carrier selection authority, shipping costs, tracking handoff, and shipment evidence. Affiliate may influence traffic source only; it cannot select carrier, mutate shipping price, or prove delivery.

## Fulfillment

Fulfillment owns packing, delivery, completion, cancellation, and return-related physical lifecycle evidence. Affiliate approval may consume admitted delivery/fulfillment evidence, but Affiliate does not own fulfillment truth.

## Accounting

Accounting owns immutable financial reporting and ledger truth. Affiliate commission, reversal, balance, withdrawal, and payout accounting effects must converge through accounting authority.

## Search

Search exposes affiliate-discoverable products, campaigns, links, and storefront entry points as derivative discovery. Elasticsearch/search results are projection only and must not admit stale product/category/campaign eligibility or resurrect hidden/deleted products.

## CMS

CMS, landing pages, banners, and content blocks present affiliate campaigns and referral context. They are presentation surfaces; backend Affiliate, Storefront, Catalog, and Checkout authority decide whether traffic becomes attribution or commission.

## Security / Identity

Security/Identity owns affiliate login, refresh, account activity, email verification, session admission, role isolation, tenant scope, and protected capability admission. Affiliate identity cannot become POS Admin, Storefront customer, CommerceHub customer, vendor, or Super Admin identity.

## Finance

Finance is an operational review surface for payout readiness blockers, evidence review, transfer proof, reconciliation escalation, and controls. Finance does not bypass Affiliate, Payments, or Accounting authority.

## Support

Support handles missing attribution, fraud complaints, stale dashboard reports, payout questions, and dispute escalation. Support may collect evidence, but it cannot rewrite attribution, commission, payout, order, payment, or ledger truth.

## Notifications

Notifications communicate affiliate and Core lifecycle events. Notification delivery/non-delivery never changes attribution, commission, payout, order, or payment truth.

## WhatsApp / Messaging

WhatsApp and messaging transports may deliver referral links, campaign messages, notifications, or support communication. Transport delivery is communication evidence only; it never creates attribution, customer identity, commission, payout, or payment authority.

## Analytics

Analytics show traffic, campaign performance, conversion reporting, commission summaries, and dashboards. Analytics are derivative evidence and cannot create conversions, commission, ledger, or payout truth.

## Super Admin

Super Admin governs platform-wide taxonomy, emergency containment, dangerous repair, global operational controls, and platform support escalation. It does not bypass lifecycle owners.

---

# How POS Admin Drives Affiliate

POS Admin drives Affiliate operationally, not authoritatively.

## Campaign configuration

POS Admin configures which tenant products/categories/policies can participate in affiliate promotion. Backend authority validates tenant scope, product lifecycle, category, price, stock/backorder eligibility, commission ranges, and current store context.

Propagation:

```text
POS Admin configuration intent
↓
Backend validation against current tenant/product/category/price/stock authority
↓
SQL configuration/policy authority
↓
Affiliate discovery/link eligibility projections
↓
Storefront entry/cart/checkout revalidation
```

## Commission configuration

POS Admin may configure commission rate/cap/category/policy inputs. Checkout later locks and snapshots the currently valid commission policy for each admitted order line. Policy changes after checkout do not rewrite historical commission snapshots.

Propagation:

```text
Commission configuration
↓
Backend policy admission
↓
Current eligibility/discovery
↓
Checkout-time snapshot
↓
Commission ledger lifecycle
```

## Affiliate approval

POS Admin may review or approve tenant-side participation depending on the operating model. Affiliate account/session truth remains backend Affiliate/Security authority. Suspended, closed, unverified, or deleted accounts fail closed.

## Payout review

POS Admin/Finance reviews payout requests as an operational surface. Backend payout authority owns request status admission, balance calculation, wallet snapshot parity, readiness gates, and transfer-evidence allocation. Payments and Accounting remain authoritative for financial settlement and ledger truth.

## Payment proof

POS Admin/Finance may submit payment reference/proof intent. Proof is evidence only until backend verifies, binds, allocates, and commits it under payout/evidence authority. Manual or forged proof must not become cash truth, bank/provider finality, or accounting settlement.

## Operational management

POS Admin consumes affiliate reads: affiliate list, detail, orders, earnings, withdrawal/payout queue, reports, and readiness. These reads are derivative views over backend SQL authority and must fail closed or re-enter backend reads when identity, tenant, role, or data authority is stale.

---

# How Affiliate Differs from CommerceHub

Affiliate and CommerceHub are adjacent systems, not interchangeable systems.

## Traffic generation vs marketplace selling

Affiliate:

- Sends visitors to a tenant Storefront.
- Measures referral/campaign evidence.
- Earns commission from eligible Storefront conversions.

CommerceHub:

- Hosts marketplace discovery and checkout across vendors.
- Creates marketplace orders and vendor sub-orders.
- Manages marketplace governance, vendor visibility, EGP marketplace finance, vendor earnings, and marketplace payout concerns.

## Commission vs vendor earnings

Affiliate commission:

- Paid to an affiliate for traffic/conversion attribution.
- Derived from Storefront order-line commission snapshots and Affiliate commission ledger lifecycle.

Vendor earnings:

- Paid to a vendor/tenant for selling goods through CommerceHub.
- Derived from marketplace order/sub-order finance, vendor earning authority, settlement, refunds, and marketplace reconciliation.

## Affiliate attribution vs marketplace ownership

Affiliate attribution:

- Answers: which affiliate gets credit for a Storefront conversion?
- Bound by referral evidence, Storefront checkout, immutable order attribution, and commission snapshots.

Marketplace ownership:

- Answers: which vendor/store owns which marketplace sub-order and financial obligation?
- Bound by CommerceHub marketplace order/sub-order authority and vendor/store lineage.

Never assume Storefront affiliate attribution creates CommerceHub marketplace ownership, vendor earnings, or marketplace commission authority.

---

# Affiliate Administration Map

This is a dependency map, not an authority map.

It shows which operational surface depends on which Core/Backend authority and who consumes the result. It does not transfer authority to the UI or dashboard.

| Operational area | Who operates it | Depends on | Produces / exposes | Consumed by |
|---|---|---|---|---|
| Affiliate Dashboard | Affiliate | Affiliate identity/session, commission ledger, payout requests, analytics projections | Profile, links, commission, payout, analytics presentation | Affiliate |
| Affiliate Management | POS Admin / Support | Affiliate identity, account lifecycle, tenant scope, role authorization | Affiliate list/detail/status evidence | POS tenant, Support, Finance |
| Commission Rules | POS Admin | Product/category/policy eligibility, pricing, inventory visibility, tenant scope | Commission policy/configuration evidence | Checkout, Affiliate discovery, POS Admin |
| Commission Queue | Affiliate backend / Finance | Order attribution, immutable order-line snapshots, payment/accounting gates, delivery gates | Candidate/earned/approved/reversed/paid lifecycle rows | Finance, Affiliate dashboard, reports |
| Payout Queue | Affiliate backend / Finance / POS Admin | Net approved balance, active payout reservations, readiness, wallet snapshot, transfer evidence | Payout requests and review states | Finance, POS tenant, Affiliate |
| Affiliate Analytics | Affiliate analytics/read models | Attribution, clicks, conversions, commission ledger, order/payment/accounting evidence | Dashboard/reporting metrics | Affiliate, POS Admin, Support |
| Fraud Review | Support / Finance / Super Admin | Identity/session evidence, referral admission, attribution uniqueness, payout evidence, duplicate/replay controls | Review decisions, holds, escalations | Support, Finance, Super Admin |
| Notifications | Notification system | Affiliate/Core lifecycle events | Communication delivery attempts | Affiliate, POS tenant, Support |
| Reports | POS Admin / Finance / Super Admin | SQL ledger, attribution, order/payment/accounting authority, derivative projections | Operational and financial reporting | Tenant operators, Finance, Support |

Dependency Map, not Authority Map:

- `Operates it` means the human/system that uses the surface.
- `Depends on` means the Core or backend authority that must decide truth.
- `Produces / exposes` means what the surface shows or emits.
- `Consumed by` means who uses that evidence operationally.

No row means the surface owns the Core business rule.

---

# Affiliate Dependency Matrix

This is a dependency matrix, not an ownership transfer. `Authoritative Source` names the source that decides truth. `Affiliate Role` names what Affiliate contributes or consumes. `Consumer` names who uses the result operationally.

| Affiliate Area | Authoritative Source | Affiliate Role | Consumer |
|---|---|---|---|
| Affiliate identity/session | Security/Identity + backend Affiliate SQL authority | Owns affiliate actor admission and protected capability checks for the affiliate surface | Affiliate portal, POS Admin affiliate management |
| Registration acceptance | Backend Affiliate identity authority + current SQL | Admits candidate affiliate accounts without granting automatic session or commission authority | Affiliate portal, Support |
| Account approval/suspension/closure | Affiliate governance/backend; Super Admin only for platform-scope governance where applicable | Applies affiliate account capability state; does not bypass Security/Identity | Affiliate portal, POS Admin, Support |
| Product eligibility | Catalog/Product + Inventory + Pricing + Affiliate configuration authority | Presents and admits eligible affiliate product/campaign candidates for later revalidation | Affiliate discovery, promotion links, Storefront entry |
| Referral link identity | Backend Affiliate SQL authority | Owns referral/link identity and current link admission | Affiliate, Storefront entry, Support |
| Referral evidence admission | Backend Affiliate authority + Storefront tenant resolution | Supplies admitted referral evidence; URL/browser/header values remain candidates | Storefront, Checkout |
| Attribution | Checkout/order authority stamping canonical attribution with admitted Affiliate evidence | Supplies protected attribution evidence and records order-bound attribution after checkout admission | Orders, commission lifecycle, analytics |
| Cart affiliate line evidence | Storefront cart authority + Affiliate line admission | Supplies per-line candidate evidence that must re-enter checkout authority | Checkout |
| Checkout commission snapshot | Storefront checkout/order authority consuming current Affiliate policy | Supplies policy/context; checkout captures immutable order-line commission snapshots | Affiliate commission lifecycle |
| Order lifecycle | Orders Core | Consumes order status, cancellation, delivery, return, and refund effects | Affiliate commission approval/reversal, dashboard |
| Payment finality | Payments Core | Consumes admitted payment/accounting proof for earning, approval, and payout readiness | Commission earning, payout readiness |
| Refund/chargeback/reversal | Payments/Refunds/Reconciliation Core | Consumes admitted reversal impact and records bounded commission reversals | Commission reversal, payout readiness |
| Delivery/fulfillment evidence | Orders/Fulfillment/Inventory Core | Consumes admitted delivery evidence for approval; does not own fulfillment truth | Commission approval |
| Commission ledger | Backend Affiliate SQL ledger authority | Owns append-only commission lifecycle evidence after Core gates pass | Affiliate dashboard, POS Admin, Finance, reports |
| Balance | Backend Affiliate balance authority recomposed from SQL ledgers | Computes available balance from approved net commission, reversals, withdrawals, and active requests | Affiliate payout requests, Finance |
| Payout request | Backend Affiliate payout request authority | Admits amount intent, wallet snapshot, request reservation, scope, and lifecycle | Affiliate portal, POS Admin payout queue |
| Transfer evidence | Backend Affiliate payout/evidence allocation authority; Payments/Accounting for settlement and ledger truth; external rail for its own side effect | Admits, binds, and allocates proof candidate; never owns bank/provider finality or accounting settlement | Payout evidence review, reconciliation, reports |
| Accounting | Accounting/Payments Core | Consumes accounting readiness and ledger effects; does not replace ledger authority | Payout readiness, reports, finance reconciliation |
| Notifications | Notification system consuming lifecycle events | Emits communication outputs only | Affiliate, POS tenant, Support |
| Analytics | Analytics/read models over SQL authority | Exposes derivative campaign, funnel, commission, and payout reporting | Affiliate dashboard, POS Admin, Support |

---

# Affiliate Operational Flow

```text
Affiliate
↓
Campaign
↓
Referral Link
↓
Visitor
↓
Storefront
↓
Checkout
↓
Order
↓
Attribution
↓
Commission
↓
Payout Readiness / Evidence
```

Operational interpretation:

1. Affiliate identity must be current and admitted.
2. Campaign/link must be current backend SQL authority, not copied text alone.
3. Visitor carries referral candidate evidence.
4. Storefront entry admits or rejects referral evidence under current tenant/link/product/affiliate authority.
5. Checkout revalidates all Storefront and Affiliate evidence inside the final authority boundary.
6. Order is created by Orders/Checkout authority with immutable affiliate attribution evidence.
7. Commission consumes immutable order-line snapshots after payment/accounting/order gates.
8. Payout readiness/evidence consumes net approved commission after reversals and readiness gates; settlement and external transfer finality remain outside Affiliate.

---

# Affiliate Operational Change Propagation

```text
Configuration
↓
Validation
↓
Attribution
↓
Commission
↓
Reporting
↓
Payout Readiness
```

## Configuration

POS Admin or platform governance changes affiliate settings, category, campaign, product eligibility, commission rules, or account lifecycle. This is intent until backend validation accepts it.

## Validation

Backend revalidates tenant, actor, product lifecycle, category, price, stock/backorder, affiliate account, link, policy, and role authority. Invalid, stale, cross-tenant, or ambiguous input fails closed.

## Attribution

Attribution uses admitted evidence only. A stale link may remain visible as text, but every real use re-enters backend admission and checkout revalidation before durable attribution exists.

## Commission

Commission uses immutable checkout snapshots. Later configuration changes do not rewrite historical commission. Refunds, chargebacks, cancellations, delivery failures, payment uncertainty, or accounting gaps can block, reverse, or defer lifecycle progression.

## Reporting

Dashboards and analytics project from current or converging SQL authority. They do not make commission, balance, payout, or conversion truth.

## Payout Readiness

Readiness recomputes current external-money ambiguity, accounting backlog, refund/payout overlap, balance, request reservations, currency, wallet snapshot, transfer evidence, and tenant/scope proof. It fails closed when uncertainty remains.

## Authority, Evidence, Projection, Presentation, Operational Surface

- Authority: PostgreSQL plus the owning Core/Affiliate lifecycle boundary.
- Evidence: referral links, protected attribution cookies, webhooks, transfer proof, logs, events, analytics, and user/operator input.
- Projection: MongoDB, Elasticsearch, Redis, dashboards, analytics, search, read models, and reports.
- Presentation: portal screens, POS Admin screens, banners, badges, copied URLs, buttons, and status text.
- Operational Surface: Affiliate Portal, POS Admin screens, finance queues, support tools, Super Admin consoles, dashboards, reports, and runbook-driven operations. They submit intent, expose evidence, or trigger backend workflows; they do not own business truth unless the owning backend authority admits the change.

Evidence can be useful, and different evidence types have different admission requirements and confidence levels. Protected/signed/backend evidence can be stronger than browser or user/operator evidence, but it becomes business truth only after the owning authority admits it.

---

# Affiliate Authority Boundaries

Affiliate consumes Core domains. It does not own them.

Affiliate never owns:

- Orders.
- Payments.
- Inventory.
- Accounting.
- Customer identity.
- Financial settlement.
- Payment settlement authority.
- Refund finality.
- Chargeback finality.
- Provider/bank execution truth.
- External bank/provider transfer finality.
- Shipping or fulfillment truth.
- Storefront checkout runtime.
- CommerceHub marketplace order/sub-order authority.
- Product price, tax, coupon, promotion, or stock truth.

Affiliate may:

- admit referral evidence;
- attach immutable attribution to a Storefront order through checkout authority;
- snapshot commission inputs for admitted order lines;
- record commission lifecycle rows after Core gates pass;
- reverse commission after refund/chargeback/reversal authority;
- compute available balance from current SQL ledger truth;
- admit payout requests only when current authority permits;
- participate in payout readiness and transfer-evidence allocation;
- record paid commission evidence without owning accounting settlement or external transfer finality.

If Core authority is missing, contradictory, stale, unresolved, or tenant-unsafe, Affiliate must fail closed.

---

# Business Rules

Durable Affiliate system rules:

1. PostgreSQL is the authority for affiliate identity, attribution, commission, balance, payout request, payout evidence, and allocation truth.
2. Affiliate identity is separate from POS Admin, Storefront customer, CommerceHub customer, vendor, and Super Admin identities.
3. Registration does not create authenticated session authority.
4. Login/refresh require current verified and active backend affiliate authority.
5. Suspended, closed, deleted, inactive, or unverified affiliates fail closed for protected capabilities.
6. Referral URLs, query parameters, headers, browser storage, copied text, and frontend state are referral candidates only.
7. Durable attribution begins only when backend checkout authority stamps the order and canonical attribution together.
8. One order can have only one canonical affiliate attribution.
9. Storefront affiliate attribution applies to Storefront-origin generic orders, not CommerceHub marketplace sub-orders unless separately certified.
10. Commission eligibility is captured at checkout per admitted order line; normal lines do not inherit commission from the order header or cart.
11. Commission amount uses immutable checkout snapshots, not current policy, current product state, dashboard totals, analytics, or frontend calculations.
12. Pending/earned commission requires backend payment and accounting authority.
13. Approved commission requires delivery/order/payment/accounting authority.
14. Pending/earned commission is not withdrawable.
15. Candidate, earned/pending, approved, reversed, paid evidence, payout request, withdrawal, and balance movement are related but different lifecycle concepts.
16. Candidate commission is checkout-time admitted order-line snapshot evidence.
17. Earned/pending commission requires backend payment and accounting authority and remains non-withdrawable.
18. Approved commission requires delivery/order/payment/accounting authority and can feed payoutable balance only net of reversals.
19. Paid commission evidence records local commission lifecycle outcome; it does not prove external bank/provider finality or accounting settlement.
20. Affiliate balance movement and withdrawal authority record account-level money movement separately from commission lifecycle evidence.
21. Withdrawable balance derives from net approved commission after reversals, allowed negative movements, withdrawals, and active payout reservations.
22. Refunds, partial refunds, chargebacks, or confirmed reversals reduce payable commission through reversal authority.
23. Post-payout refunds or chargebacks never erase historical payout evidence; they create bounded reversal, recovery, negative balance, reconciliation, and accounting effects under current SQL authority.
24. Payout request amount is intent only; backend recomputes raw available balance.
25. Wallet snapshots are captured at request/review boundaries; later wallet edits never rewrite historical payout evidence.
26. Payout destination identity must remain bound to the current affiliate account and admitted wallet snapshot; ambiguous destination evidence fails closed.
27. Payout readiness is outward-money readiness and fails closed under unresolved external money, provider-refund ambiguity, critical backlog, ambiguous scope, accounting gaps, currency conflict, stale wallet evidence, or insufficient current balance.
28. Transfer evidence is proof candidate/evidence until verified, bound, allocated, and committed by payout/evidence authority; it is not bank/provider transfer finality or accounting ledger truth.
29. One real-world transfer reference cannot fund multiple local withdrawals.
30. Self-referral, affiliate/customer identity collision, affiliate re-registration after suspension, multi-attribution ambiguity, campaign abuse, category/product eligibility abuse, manual override attempts, and suspicious payout proof combined with referral fraud all fail closed unless explicit current backend authority admits them.
31. Support, POS Admin, Finance, or Super Admin cannot rewrite canonical attribution, commission, payout, payment, order, or accounting truth by manual override outside governed backend authority.
32. Dashboard, analytics, notifications, projections, and reports are derivative evidence only.
33. Tenant/store economics are scoped by current SQL store tenant authority, not by affiliate JWT tenant claims or frontend route state.
34. Global/platform affiliate taxonomy governance is Super Admin/platform authority; tenant admin surfaces cannot mutate global authority.
35. Read-model lag can be accepted; stale projection becoming authority is a defect.

---

# Never Assume

- Never assume Affiliate is just referral links.
- Never assume a referral URL proves attribution.
- Never assume `?ref=`, `X-Affiliate-Ref`, browser storage, clipboard text, or a banner creates commission.
- Never assume the first UI-visible affiliate wins unless backend protected evidence and current SQL authority admit it.
- Never assume a cart line with old affiliate evidence remains eligible at checkout.
- Never assume all items in a mixed cart are affiliate commission items.
- Never assume order-level attribution makes every order item commissionable.
- Never assume current commission policy rewrites historical commission snapshots.
- Never assume pending/earned commission is withdrawable.
- Never assume delivery alone approves commission if payment/accounting authority is missing.
- Never assume payment success from a provider, browser, dashboard, or notification is payment authority.
- Never assume refunded, charged-back, cancelled, or fully reversed value remains payable.
- Never assume post-payout refund or chargeback erases historical paid evidence; it creates recovery/reversal/accounting effects.
- Never assume self-referral is allowed because the affiliate and customer identities are both valid.
- Never assume affiliate/customer/POS Admin identity collision can create commission without current backend policy admission.
- Never assume multiple referral candidates, repeated clicks, or analytics-first-touch metrics decide attribution without protected backend evidence.
- Never assume campaign, category, or product eligibility remains valid because a stale campaign card, search result, or link is visible.
- Never assume a suspended affiliate can re-register or reuse another identity to regain commission capability without governed backend admission.
- Never assume a payout request reserves money twice on replay.
- Never assume a payout proof/reference is trusted because POS Admin submitted it.
- Never assume one transfer reference can be allocated independently per tenant/store.
- Never assume later wallet edits can rewrite an already captured payout request or payout evidence snapshot.
- Never assume dashboard balance equals payoutable balance.
- Never assume positive manual adjustments or bonuses manufacture earned payoutable commission unless the owning authority explicitly allows it.
- Never assume Affiliate payout readiness can ignore unresolved captured-payment, provider-refund, compensation, or ambiguous critical backlog evidence.
- Never assume Affiliate Storefront attribution applies to CommerceHub marketplace orders.
- Never assume an affiliate identity token can operate POS Admin, Storefront customer, CommerceHub customer, vendor, or Super Admin surfaces.
- Never assume Support, Finance, POS Admin, or Super Admin can rewrite attribution, commission, payout, payment, order, or accounting truth outside governed authority.

---

# QA Checklist

System-understanding checklist only:

- [ ] Did I identify whether the behavior belongs to Affiliate, Storefront, POS Admin, CommerceHub, Orders, Payments, Inventory, Accounting, Security, Analytics, or Notifications?
- [ ] Did I read the owning Core guide instead of duplicating or guessing the Core rule?
- [ ] Is PostgreSQL/lifecycle authority separated from portal/POS Admin/dashboard/projection presentation?
- [ ] Is affiliate identity isolated from POS Admin, customer, CommerceHub, vendor, and Super Admin identities?
- [ ] Is the affiliate account current, verified, active, and not suspended/closed/deleted where required?
- [ ] Is referral/link evidence admitted by backend current SQL authority, not by URL/header/browser state?
- [ ] Are self-referral, identity collision, campaign abuse, and manual override attempts rejected unless current backend authority explicitly admits them?
- [ ] Is attribution stamped only by checkout/order authority?
- [ ] Are multi-attribution candidates resolved by protected backend evidence and current SQL policy, not analytics or repeated clicks?
- [ ] Is the order a Storefront-origin generic order, not a CommerceHub marketplace sub-order?
- [ ] Are commission snapshots immutable and captured at checkout after final validation?
- [ ] Are mixed-cart lines independently admitted for commission?
- [ ] Are payment and accounting gates satisfied before pending/earned commission?
- [ ] Are delivery/order/payment/accounting gates satisfied before approval?
- [ ] Are refunds, partial refunds, chargebacks, cancellations, and reversals reflected before payout readiness?
- [ ] Is withdrawable balance recomputed from SQL, not a dashboard value?
- [ ] Are active payout requests counted as reservations against availability?
- [ ] Is payout destination identity bound to the affiliate account and captured wallet snapshot, with later wallet edits unable to rewrite historical payout evidence?
- [ ] Is payout readiness evaluated inside the payout authority boundary and fail-closed under unresolved money/backlog/scope ambiguity?
- [ ] Is payout evidence verified, globally idempotent, amount/currency/tenant/reference-bound, and not trusted as UI proof?
- [ ] Are post-payout refunds/chargebacks represented as bounded reversal, recovery, negative-balance, reconciliation, or accounting effects instead of erasing historical paid evidence?
- [ ] Are analytics and notifications treated as derivative outputs?
- [ ] Is any failure a true authority violation, or an expected fail-closed tradeoff?

---

# Common Failure Scenarios

## Forged referral code creates attribution

Business failure: URL/header/browser evidence bypassed backend link and tenant admission.

Expected behavior: backend admits only current active store-scoped link and active affiliate/product/category/price/stock authority; otherwise no attribution.

## Duplicate attribution from repeated clicks

Business failure: multiple referral attempts create multiple winners or overwrite protected attribution evidence without authority.

Expected behavior: backend protected evidence and current SQL validation decide; one order has one canonical attribution.

## Multi-attribution ambiguity

Business failure: analytics first-touch, last-click UI, repeated clicks, query strings, or multiple cookies decide the winning affiliate.

Expected behavior: backend protected evidence and current SQL attribution policy decide one canonical attribution; ambiguous or conflicting candidates fail closed.

## Self-referral creates commission

Business failure: an affiliate uses the same actor/customer/payment/tenant relationship to manufacture commission without explicit backend policy admission.

Expected behavior: identity, customer, tenant, account, and fraud authority revalidate the relationship; unsupported self-referral fails closed and creates no commission.

## Affiliate/customer identity collision

Business failure: a valid affiliate identity and a valid customer identity are treated as proof that the same or related actor can earn commission.

Expected behavior: Security/Identity and Affiliate authority keep actor domains isolated and apply current policy; collision or ambiguous ownership blocks attribution/commission.

## Stale affiliate link survives account or product lifecycle loss

Business failure: a copied URL or cached link continues to create affiliate context after affiliate suspension, link deactivation, product archive/delete/hide, invalid category, missing price, or stock loss.

Expected behavior: stale text may exist, but every use re-enters backend admission and fails closed.

## Campaign or category abuse

Business failure: stale campaign pages, copied links, product cards, search results, or category projections keep admitting commission after campaign, category, product, price, stock, or tenant eligibility changed.

Expected behavior: Storefront entry, cart, and checkout revalidate current SQL campaign/product/category/price/stock authority; stale campaign evidence fails closed.

## Suspended affiliate re-registers to bypass governance

Business failure: a suspended, closed, deleted, or fraud-marked affiliate regains capability through re-registration, alternate identity, or stale session state.

Expected behavior: backend identity/governance authority decides current capability; ambiguous or linked-risk re-registration fails closed or requires governed review before protected capabilities return.

## Mixed cart gives commission on non-affiliate items

Business failure: non-admitted lines inherit attribution from the cart/order/header.

Expected behavior: each order line needs independent backend line admission and immutable commission snapshot.

## Checkout trusts cart affiliate evidence

Business failure: Redis cart, browser state, or cart DTO becomes financial commission authority.

Expected behavior: checkout revalidates affiliate account, link, product, policy, category, price, stock, payment, shipping, and order authority inside the final transaction.

## Commission amount changes after policy update

Business failure: historical commission is recalculated from current policy/product/category state.

Expected behavior: commission uses immutable checkout snapshots; later config changes affect future eligible checkouts only.

## Pending commission appears withdrawable

Business failure: earned/pending commission is included in available withdrawal amount.

Expected behavior: withdrawable balance includes net approved commission after reversals, deductions, withdrawals, and active request reservations; pending/earned rows are excluded.

## Delivery approves commission without payment/accounting proof

Business failure: shipment/delivery event alone creates withdrawable commission.

Expected behavior: approval requires current delivery, payment, accounting, tenant, and earned-entry authority.

## Cancelled order creates commission

Business failure: cancelled or fully refunded terminal orders create pending/earned commission.

Expected behavior: terminal cancelled/fully refunded orders are blocked before pending creation.

## Refund does not reverse commission

Business failure: refunded value remains payable to affiliate.

Expected behavior: refund/chargeback/reversal authority creates bounded commission reversals and payout uses net approved value after reversals.

## Partial refund over-reverses or under-reverses commission

Business failure: partial refund replay fully reverses the item or preserves too much commission.

Expected behavior: reversal is proportional/bounded by authoritative refund-line/cumulative refund evidence and cannot exceed earned authority.

## Chargeback impact ignored

Business failure: external dispute/chargeback or provider refund ambiguity is ignored and payout proceeds.

Expected behavior: payment/refund/reconciliation authority controls finality; unresolved provider cash/refund ambiguity blocks payout readiness until reconciled.

## Duplicate commission from event replay

Business failure: duplicate payment/delivery/refund events create duplicate earned, approved, reversed, or paid rows.

Expected behavior: append-only SQL ledger and idempotency collapse replays.

## Payout request over-reserves balance

Business failure: duplicate browser retry creates multiple active payout reservations for the same amount.

Expected behavior: backend serializable request admission and active duplicate detection return existing authority or fail closed.

## Payout evidence forgery

Business failure: manual proof/reference is accepted without verification, scope, amount, currency, or replay binding.

Expected behavior: evidence is verified and bound before payout recognition/allocation; forged/stale/conflicting evidence fails closed.

## Suspicious payout proof combined with referral fraud

Business failure: suspicious transfer proof is accepted even though the underlying attribution, campaign, identity, or commission lineage is under fraud review.

Expected behavior: payout readiness rechecks fraud holds, attribution lineage, commission ledger, wallet snapshot, accounting readiness, and transfer evidence; unresolved fraud or money ambiguity blocks payout readiness.

## Manual operator override rewrites attribution

Business failure: Support, POS Admin, Finance, or Super Admin changes the winning affiliate, campaign, commission, payout, payment, or order truth from a dashboard, queue, report, or support tool.

Expected behavior: operator surfaces collect evidence and submit governed intent only; canonical attribution, commission, payout, order, payment, and accounting truth re-enter backend authority or fail closed.

## Duplicate payout from same transfer reference

Business failure: one bank/provider transfer funds multiple withdrawals across stores or replays.

Expected behavior: transfer evidence allocation idempotency is global per rail/reference; matching replay converges, conflicting replay fails closed.

## Payout rollback after external transfer uncertainty

Business failure: local failure after external transfer is treated as safe non-execution or silently retried as a second payment.

Expected behavior: recovery reloads SQL evidence/request/ledger/accounting rows; uncertain external outcome enters processing/reconciliation-required/manual review, not duplicate outward money or false failed state.

## Post-payout refund erases paid evidence

Business failure: a refund or chargeback after payout deletes or rewrites historical paid commission evidence as if the external transfer never happened.

Expected behavior: historical paid evidence remains append-only. Current SQL authority creates bounded reversal, negative balance, recovery, reconciliation, and accounting effects and blocks future payout readiness when needed.

## Dashboard balance is treated as truth

Business failure: displayed balance or analytics total decides payout.

Expected behavior: payout authority recomputes raw balance from SQL ledgers and active requests.

## Cross-tenant affiliate data leak

Business failure: POS Admin route, dashboard, affiliate id, payout id, or evidence reference exposes another tenant/store's affiliate economics.

Expected behavior: backend trusted tenant/store scope and SQL predicates decide; foreign ids fail closed as denied/not found.

## Read-model lag misclassified as defect

Business failure: QA reports normal projection lag as a bug without checking SQL authority.

Expected behavior: projection lag is accepted when authority is safe and convergence/rebuild fails closed; stale projection becoming authority is the defect.

---

# Source Documents

Authoritative Core / context documents:

- `.ai/hermes/INDEX.md`
- `.ai/hermes/02-architecture.md`
- `.ai/hermes/03-inventory.md`
- `.ai/hermes/04-payments.md`
- `.ai/hermes/05-orders.md`
- `.ai/hermes/06-security-identity.md`
- `.ai/hermes/07-platform-overview.md`
- `.ai/hermes/08-qa-philosophy.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `.ai-context/KNOWN_RISKS.md`

System guides referenced, not duplicated:

- `.ai/hermes/systems/storefront/README.md`
- `.ai/hermes/systems/pos-admin/README.md`
- `.ai/hermes/systems/commercehub/README.md`

Affiliate certification / architecture evidence:

- `docs/architecture/AFFILIATE_CHECKOUT_0_ARCHITECTURE.md`
- `docs/architecture/AFFILIATE_CHECKOUT_1_RUNTIME.md`
- `docs/architecture/AFFILIATE_CHECKOUT_2_APIS.md`
- `docs/architecture/AFFILIATE_CHECKOUT_2_5_UX_NAVIGATION_CERTIFICATION.md`
- `docs/architecture/AFFILIATE_CHECKOUT_3_FRONTEND.md`
- `docs/architecture/AFFILIATE_CHECKOUT_4_OPERATIONAL_READINESS_CERTIFICATION.md`
- `docs/architecture/AFFILIATE_CHECKOUT_FINAL_END_TO_END_DEVELOPMENT_CERTIFICATION_FREEZE.md`
- `docs/architecture/affiliate-identity-authority-certification-findings.md`
- `docs/architecture/affiliate-identity-registration-entry-authority-certification-findings.md`
- `docs/architecture/affiliate-actor-profile-session-read-authority-certification-findings.md`
- `docs/architecture/affiliate-account-state-machine-business-lifecycle-certification-findings.md`
- `docs/architecture/affiliate-profile-payment-candidate-authority-certification-findings.md`
- `docs/architecture/frontend-affiliate-authority-certification-findings.md`
- `docs/architecture/affiliate-configuration-entry-authority-certification-findings.md`
- `docs/architecture/affiliate-configuration-read-authority-certification-findings.md`
- `docs/architecture/affiliate-configuration-edit-authority-certification-findings.md`
- `docs/architecture/affiliate-configuration-submission-mutation-authority-certification-findings.md`
- `docs/architecture/affiliate-configuration-authorization-client-trust-boundary-certification-findings.md`
- `docs/architecture/affiliate-configuration-ux-accessibility-human-factors-certification-findings.md`
- `docs/architecture/affiliate-product-eligibility-entry-navigation-authority-certification-findings.md`
- `docs/architecture/affiliate-product-context-projection-read-authority-certification-findings.md`
- `docs/architecture/affiliate-product-eligibility-candidate-decision-certification-findings.md`
- `docs/architecture/affiliate-product-decision-commitment-certification-findings.md`
- `docs/architecture/affiliate-product-eligibility-business-lifecycle-certification-findings.md`
- `docs/architecture/affiliate-product-discovery-read-authority-certification-findings.md`
- `docs/architecture/affiliate-product-selection-candidate-authority-certification-findings.md`
- `docs/architecture/affiliate-promotion-link-generation-authority-certification-findings.md`
- `docs/architecture/affiliate-promotion-link-resolution-storefront-entry-attribution-admission-certification-findings.md`
- `docs/architecture/affiliate-customer-journey-attribution-persistence-certification-findings.md`
- `docs/architecture/affiliate-cart-attribution-line-admission-mixed-cart-certification-findings.md`
- `docs/architecture/affiliate-checkout-attribution-order-line-admission-commission-candidate-certification-findings.md`
- `docs/architecture/affiliate-commission-candidate-lifecycle-pending-financial-authority-certification-findings.md`
- `docs/architecture/affiliate-commission-approval-withdrawable-balance-financial-recovery-certification-findings.md`
- `docs/architecture/affiliate-commission-authority-certification-findings.md`
- `docs/architecture/affiliate-payout-authority-certification-findings.md`
- `docs/architecture/affiliate-withdrawal-request-authority-certification-findings.md`
- `docs/architecture/affiliate-financial-recovery-replay-idempotency-ledger-integrity-certification-findings.md`
- `docs/architecture/affiliate-attribution-authority-certification-findings.md`
- `docs/architecture/affiliate-order-lineage-admin-presentation-authority-certification-findings.md`

Notes:

- Development/runtime authority certification and production operational readiness are separate evidence classes. This guide does not infer operational readiness from source-level architecture certification alone.
- Where detailed Core mechanics are needed, this guide intentionally references the owning Core guide instead of duplicating it.
