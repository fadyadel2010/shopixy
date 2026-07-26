# GLOSSARY

Architectural glossary for the Shopixy / PointOfSale platform.

## Purpose

This document standardizes terminology across the entire Shopixy platform so
that Hermes interprets every architectural term with one durable, consistent
meaning.

It supplements the Mental Model. It does not replace System Guides. Where the
Mental Model explains how the platform thinks and System Guides explain how a
specific domain works, this Glossary fixes *what the words mean*. Ambiguous
reasoning and terminology-driven hallucinations are reduced because each term
has exactly one definition.

## How To Use This Glossary

If Hermes encounters an unfamiliar architectural term:

↓

Check this glossary first.

↓

Then continue browsing (Mental Model or the relevant System Guide).

Never invent meanings. If a term is not here, say it is undefined and look for
the originating System Guide rather than guessing. Do not assume a term means
what it means in another platform.

---

# Core Terms

----------------------------------------------------------

## Authority

What it IS

The single source that is permitted to decide a fact for the platform. For
Shopixy, PostgreSQL is the authority for business state.

What it is NOT

Not a cache, not a derivative store, not a UI display, not a prediction.

Primary Owner

Platform architecture (data-layer governance). The authority is a property of
the system design, not a team.

Common Systems

Every system that reads or writes business state: CommerceHub, Storefront,
Payments, Ledger, Inventory, Fulfillment.

Common AI Mistakes

Treating MongoDB, Redis, Elasticsearch, or a UI value as authoritative. Treating
a projection as the authority. Treating "most recent" or "fastest" as
authoritative.

----------------------------------------------------------

## Truth

What it IS

The current correct state of a fact as recorded by the System of Record. Truth
is what the authority holds.

What it is NOT

Not an approximation, not a cached snapshot, not a read-model value, not a
display string.

Primary Owner

The System of Record (PostgreSQL) for the domain in question.

Common Systems

All domains. Truth lives only in the write model / system of record.

Common AI Mistakes

Equating a projection with truth. Believing Read Model Lag means truth changed.
Recomputing truth from a stale client total instead of from the record.

----------------------------------------------------------

## Evidence

What it IS

Verifiable proof that an external or internal fact occurred, recorded at its
source (e.g. a provider webhook, JWT claims, a signed ledger entry).

What it is NOT

Not opinion, not a cached display, not an inferred state, not a UI field.

Primary Owner

The system that captures the event at its boundary (payments provider adapter,
identity service, ledger).

Common Systems

Payments, Identity, Ledger, Fulfillment.

Common AI Mistakes

Trusting UI/JWT-free client values as evidence. Treating a projection as
evidence. Accepting stale-client totals as proof of state.

----------------------------------------------------------

## Lifecycle

What it IS

The ordered set of states and transitions an entity moves through from creation
to terminal closure (e.g. order states, payment states).

What it is NOT

Not a single status field, not a UI label, not an ad-hoc state someone assigned.

Primary Owner

The domain's Lifecycle Owner (see Lifecycle Owner).

Common Systems

Order, Payment, Fulfillment, Settlement, Refund.

Common AI Mistakes

Inventing intermediate states. Treating a presentation label as a lifecycle
state. Skipping required transitions.

----------------------------------------------------------

## Lifecycle Owner

What it IS

The single service responsible for advancing an entity through its lifecycle and
for all writes to that entity's state.

What it is NOT

Not a team title, not a UI, not any service that merely reads the state.

Primary Owner

The service designated by architecture (e.g. IPaymentLifecycleService owns
Payment, order lifecycle owns Order, IOrderFulfillmentService owns Fulfillment).

Common Systems

Payments, Orders, Fulfillment, Settlement.

Common AI Mistakes

Writing state directly to an entity from a service that is not its lifecycle
owner. Assuming any service may mutate a lifecycle entity.

----------------------------------------------------------

## Operational Surface

What it IS

The set of actions, endpoints, and operations a role or system is permitted to
perform within a bounded domain.

What it is NOT

Not the full platform, not an internal implementation detail, not a UI screen.

Primary Owner

The owning domain team, constrained by Governance and Authorization.

Common Systems

POS Admin, CommerceHub, Service Marketplace, Storefront.

Common AI Mistakes

Assuming Service Marketplace has payments/accounting/inventory/order ownership
just because it coordinates requests. Assuming Super Admin governs CommerceHub.

----------------------------------------------------------

## Presentation

What it IS

A rendered, human-facing representation of state. What the user sees.

What it is NOT

Not truth, not evidence, not the authority. It is downstream of truth.

Primary Owner

The frontend / presentation layer of the consuming system.

Common Systems

Storefront, POS Admin, CommerceHub console, Service Marketplace UI.

Common AI Mistakes

Reading a displayed value and treating it as authoritative truth. Believing the
label and the lifecycle state are the same thing.

----------------------------------------------------------

## Projection

What it IS

A derived, read-optimized view built from the system of record (often via the
Outbox / Projection Pipeline).

What it is NOT

Not truth, not the authority, not a write target.

Primary Owner

The read-model owner (typically a projection service fed by the Outbox).

Common Systems

MongoDB read models, Redis caches, Elasticsearch indexes, Analytics.

Common AI Mistakes

Writing to a projection. Treating a projection as the source of truth. Ignoring
that projections can lag (see Read Model Lag).

----------------------------------------------------------

## Outbox

What it IS

The durable, transactional record of events emitted by the system of record so
that projections and downstream systems can be updated reliably.

What it is NOT

Not the event bus itself, not a queue for business logic, not a read model.

Primary Owner

The system of record's write side.

Common Systems

Payments, Orders, Ledger, Fulfillment (anything that publishes IEventPublisher
events).

Common AI Mistakes

Publishing events outside the transactional outbox (lost-update risk). Treating
the outbox as the source of truth.

----------------------------------------------------------

## Read Model

What it IS

A store optimized for queries, populated from the write model via projections.

What it is NOT

Not the system of record, not writable by business logic directly.

Primary Owner

The read-model / projection owner.

Common Systems

MongoDB, Redis, Elasticsearch.

Common AI Mistakes

Writing business state into a read model. Assuming read-model consistency with
the write model at all times.

----------------------------------------------------------

## Write Model

What it IS

The authoritative, normalized representation of business state in the system of
record where all mutations occur.

What it is NOT

Not a cache, not a projection, not a denormalized view.

Primary Owner

The system of record (PostgreSQL) and its Lifecycle Owners.

Common Systems

All domains that mutate state.

Common AI Mistakes

Bypassing the write model to update a read model directly. Treating SQL
inventory as non-authoritative.

----------------------------------------------------------

## System of Record

What it IS

The single durable store that holds authoritative business state. For Shopixy
this is PostgreSQL.

What it is NOT

Not Mongo, not Redis, not Elasticsearch, not any derivative store.

Primary Owner

Platform data architecture.

Common Systems

Every business domain.

Common AI Mistakes

Reading Mongo/Redis/ES and treating them as the system of record. Believing a
derivative store "is just as good."

----------------------------------------------------------

## Tenant

What it IS

An isolated business account/namespace that owns its data within the platform.

What it is NOT

Not a vendor, not a user, not a deployment. TenantId is necessary but not
sufficient for scoping.

Primary Owner

Platform tenancy infrastructure (ITenantContext).

Common Systems

Every system. TenantId is required on all scoped operations.

Common AI Mistakes

Treating TenantId as sufficient isolation. Allowing X-Tenant-Id to be trusted
from the client. Causing cross-tenant data leaks (CRITICAL).

----------------------------------------------------------

## Vendor

What it IS

A seller/merchant operating within a tenant who supplies goods or services.

What it is NOT

Not the tenant itself, not the customer, not the platform.

Primary Owner

CommerceHub / tenant administration.

Common Systems

CommerceHub, Storefront, Inventory, Orders, Payouts.

Common AI Mistakes

Confusing vendor with tenant. Assuming a vendor can cross tenant boundaries.

----------------------------------------------------------

## Customer

What it IS

The end buyer who purchases from a storefront or marketplace.

What it is NOT

Not a vendor, not an affiliate, not a tenant admin.

Primary Owner

Storefront / CommerceHub commerce domain.

Common Systems

Storefront, Checkout, Orders, Payments, CRM.

Common AI Mistakes

Mixing customer with affiliate or vendor. Treating a customer as a tenant.

----------------------------------------------------------

## Affiliate

What it IS

A partner who refers customers and earns commission via Attribution, without
owning the storefront or inventory.

What it is NOT

Not CommerceHub, not a vendor, not a storefront operator.

Primary Owner

Affiliate domain (referral / attribution).

Common Systems

Affiliate, Attribution, Commission, Storefront referrals.

Common AI Mistakes

Equating Affiliate with CommerceHub. Assuming affiliates own orders or inventory.

----------------------------------------------------------

## CommerceHub

What it IS

The multi-vendor commerce platform where Vendors sell through Storefronts under
a tenant.

What it is NOT

Not the Service Marketplace, not the Affiliate system, not governed by CommerceHub
Super Admin as an external customer platform.

Primary Owner

CommerceHub domain owners, under tenant governance.

Common Systems

Vendors, Storefronts, Catalog, Orders, Inventory, Pricing.

Common AI Mistakes

Confusing it with the Service Marketplace. Assuming affiliate == CommerceHub.

----------------------------------------------------------

## Storefront

What it IS

The customer-facing selling surface (web/app) where purchases happen.

What it is NOT

Not the admin, not CommerceHub internals, not the Service Marketplace.

Primary Owner

Storefront domain (per tenant / vendor).

Common Systems

Checkout, Catalog, Pricing, Orders, Payments.

Common AI Mistakes

Treating a storefront order as identical to a marketplace order.

----------------------------------------------------------

## POS Admin

What it IS

The internal administration console for operating the platform and tenants.

What it is NOT

Not a customer surface, not a storefront, not the Service Marketplace.

Primary Owner

Platform operations (POS Admin domain).

Common Systems

Tenants, Vendors, Service Marketplace (internal B2B only), governance.

Common AI Mistakes

Assuming POS Admin is customer-facing. Forgetting Service Marketplace exists
only inside POS Admin.

----------------------------------------------------------

## Service Marketplace

What it IS

An internal POS-to-POS B2B space inside POS Admin only, where POS instances
coordinate service requests, chat, completion, and ratings.

What it is NOT

Not an external customer platform, not CommerceHub, not Storefront, not
Affiliate. It never owns payments, accounting, inventory, or orders.

Primary Owner

POS Admin (internal). Not governed by CommerceHub Super Admin.

Common Systems

POS Admin, internal B2B service requests, chat, ratings.

Common AI Mistakes

Giving it external customers. Assuming it owns payments/accounting/inventory.
Treating it as governed by CommerceHub Super Admin.

----------------------------------------------------------

## Super Admin

What it IS

The highest internal administrative role for platform/tenant operations.

What it is NOT

Not an external customer, not a CommerceHub governor over Service Marketplace.

Primary Owner

Platform governance.

Common Systems

POS Admin, governance, tenant administration.

Common AI Mistakes

Assuming Super Admin governs the Service Marketplace or CommerceHub vendor
economics directly.

----------------------------------------------------------

## Order

What it IS

A customer purchase commitment that moves through the order lifecycle and
triggers fulfillment and payment.

What it is NOT

Not a marketplace sub-order, not an invoice, not a payment.

Primary Owner

Order lifecycle service.

Common Systems

Storefront, Checkout, Orders, Fulfillment, Payments, Ledger.

Common AI Mistakes

Treating a Marketplace Order's sub-orders as independent Orders. Skipping
lifecycle transitions.

----------------------------------------------------------

## Marketplace Order

What it IS

An order placed on a marketplace (CommerceHub/Storefront) that spans multiple
vendors and is split into Sub-Orders.

What it is NOT

Not a single vendor order, not a Sub-Order itself.

Primary Owner

Order lifecycle service (marketplace orchestration).

Common Systems

CommerceHub, Storefront, Orders, Fulfillment, Settlement.

Common AI Mistakes

Confusing it with a plain Storefront Order. Treating Sub-Orders as the parent.

----------------------------------------------------------

## Sub-Order

What it IS

A vendor-scoped portion of a Marketplace Order, owned by one vendor and fulfilled
independently.

What it is NOT

Not the parent Marketplace Order, not a standalone customer order.

Primary Owner

Order lifecycle service (per vendor scope).

Common Systems

Fulfillment, Settlement, Payout, Inventory.

Common AI Mistakes

Treating a sub-order as the customer's order. Missing that settlement/payout
happen at sub-order level.

----------------------------------------------------------

## Checkout

What it IS

The serialized, transactional process that converts a cart into an Order,
including payment capture under a re-lock transaction.

What it is NOT

Not fulfillment, not settlement, not a draft.

Primary Owner

Checkout domain (serializable transaction).

Common Systems

Storefront, Cart, Payments, Orders, Inventory.

Common AI Mistakes

Running checkout without the serializable re-lock. Treating checkout as
non-transactional.

----------------------------------------------------------

## Inventory

What it IS

Authoritative stock quantities for items, held SQL-authoritatively.

What it is NOT

Not a projection, not a cached count, not Redis-only state.

Primary Owner

Inventory domain (SQL-authoritative).

Common Systems

Catalog, Checkout, Orders, Reservations, Fulfillment.

Common AI Mistakes

Trusting a cached inventory count over SQL. Decrementing a projection instead of
the write model.

----------------------------------------------------------

## Reservation

What it IS

A temporary hold of inventory (or capacity) during checkout/fulfillment to
prevent oversell.

What it is NOT

Not a sale, not attribution, not a permanent decrement.

Primary Owner

Inventory / Checkout lifecycle.

Common Systems

Checkout, Inventory, Orders, Fulfillment.

Common AI Mistakes

Confusing reservation with attribution. Letting reservations leak without
expiry. Treating a reservation as revenue.

----------------------------------------------------------

## Pricing

What it IS

The computed price of items/carts based on catalog, rules, and context.

What it is NOT

Not promotion alone, not a coupon, not a smart offer.

Primary Owner

Pricing domain.

Common Systems

Catalog, Storefront, Checkout, Orders.

Common AI Mistakes

Mixing pricing with promotion. Recomputing price on the client as truth.

----------------------------------------------------------

## Promotion

What it IS

A time/scoped rule that adjusts pricing (discounts, bundles) at checkout.

What it is NOT

Not a coupon code by itself, not a smart offer, not pricing base.

Primary Owner

Promotions domain.

Common Systems

Storefront, Checkout, Pricing, Orders.

Common AI Mistakes

Equating promotion with coupon. Assuming promotions survive without lifecycle
expiry.

----------------------------------------------------------

## Coupon

What it IS

A coded voucher that applies a specific promotion/discount when redeemed.

What it is NOT

Not a promotion definition itself, not a smart offer.

Primary Owner

Promotions / Coupon domain.

Common Systems

Checkout, Storefront, Pricing, Orders.

Common AI Mistakes

Treating coupon as the promotion. Allowing reuse beyond redemption rules.

----------------------------------------------------------

## Smart Offer

What it IS

A dynamic, context-aware offer (often personalized) presented to a customer.

What it is NOT

Not a static coupon, not a promotion rule by itself.

Primary Owner

Offers / personalization domain.

Common Systems

Storefront, Checkout, Pricing.

Common AI Mistakes

Confusing smart offer with coupon or promotion. Treating the offer as committed
price.

----------------------------------------------------------

## Payment

What it IS

The act and record of capturing money from a customer for an order.

What it is NOT

Not settlement, not accounting, not payout.

Primary Owner

IPaymentLifecycleService.

Common Systems

Checkout, Orders, Ledger, Settlement, Refunds.

Common AI Mistakes

Treating payment as settled. Writing payment state outside its lifecycle owner.

----------------------------------------------------------

## Payment Intent

What it IS

A tracked intent to collect a payment, spanning the pre-capture lifecycle until
capture or failure.

What it is NOT

Not the settled funds, not the payout.

Primary Owner

Payments domain (lifecycle).

Common Systems

Checkout, Payments, Orders.

Common AI Mistakes

Treating an intent as captured money. Skipping idempotency on intent creation.

----------------------------------------------------------

## Settlement

What it IS

The movement of captured funds from the processor/platform to the right party
accounts, net of fees.

What it is NOT

Not the payment capture, not accounting ledger entries, not payout to vendor.

Primary Owner

Settlement domain (treasury).

Common Systems

Payments, Ledger, Payout, Commission.

Common AI Mistakes

Equating settlement with payment. Confusing settlement with accounting entries.

----------------------------------------------------------

## Accounting

What it IS

The ledger-level recording of financial movements (double-entry) for audit and
reconciliation.

What it is NOT

Not settlement movement itself, not payment, not payout.

Primary Owner

Ledger / treasury accounting.

Common Systems

Ledger, Settlement, Payout, Refund, Commission.

Common AI Mistakes

Confusing accounting entries with settlement. Believing accounting moves money.

----------------------------------------------------------

## Ledger

What it IS

The immutable, double-entry record of all financial movements.

What it is NOT

Not a cache, not a projection, not the movement of funds itself.

Primary Owner

Ledger domain.

Common Systems

Payments, Settlement, Payout, Refund, Commission.

Common AI Mistakes

Writing mutable entries. Treating the ledger as a read model.

----------------------------------------------------------

## Payout

What it IS

The disbursement of owed funds to a vendor or partner after settlement.

What it is NOT

Not settlement, not payment capture, not commission calculation.

Primary Owner

Treasury / Payout domain.

Common Systems

Settlement, Ledger, Vendor, Commission.

Common AI Mistakes

Paying out before settlement. Payout without reconciled ledger state.

----------------------------------------------------------

## Refund

What it IS

A return of captured funds to a customer for a completed/partial order.

What it is NOT

Not a chargeback, not a reversal of settlement, not a payout.

Primary Owner

Payments / refunds lifecycle.

Common Systems

Payments, Ledger, Orders, Settlement.

Common AI Mistakes

Confusing refund with chargeback. Refunding without lifecycle state.

----------------------------------------------------------

## Chargeback

What it IS

A forced reversal initiated by the card issuer/customer bank, outside platform
control.

What it is NOT

Not a refund, not a voluntary return.

Primary Owner

Payments / risk domain (external-driven).

Common Systems

Payments, Ledger, Risk, Reconciliation.

Common AI Mistakes

Treating chargeback as a refund. Ignoring its external authority.

----------------------------------------------------------

## Commission

What it IS

The fee/earnings owed to an affiliate, vendor, or platform per agreement.

What it is NOT

Not a payout itself, not attribution, not settlement.

Primary Owner

Commission domain (treasury).

Common Systems

Affiliate, Attribution, Settlement, Payout.

Common AI Mistakes

Paying commission without attribution. Confusing commission with payout.

----------------------------------------------------------

## Attribution

What it IS

The recorded linkage of a sale/event to a source (affiliate, channel) for
commission.

What it is NOT

Not a reservation, not a commission payment, not a sale itself.

Primary Owner

Attribution domain.

Common Systems

Affiliate, Commission, Analytics, Marketing.

Common AI Mistakes

Confusing attribution with reservation. Attributing without evidence.

----------------------------------------------------------

## Notification

What it IS

A message delivered to a user/system about an event (email, SMS, push, WhatsApp).

What it is NOT

Not a business event itself, not the source of truth.

Primary Owner

Notification domain.

Common Systems

Orders, Payments, Fulfillment, WhatsApp, Email.

Common AI Mistakes

Treating a notification as the business event. Using notifications as evidence.

----------------------------------------------------------

## Analytics

What it IS

Aggregated, derived reporting built from projections for decision-making.

What it is NOT

Not truth, not the system of record, not operational state.

Primary Owner

Analytics / data domain.

Common Systems

MongoDB/ES read models, dashboards, reporting.

Common AI Mistakes

Querying analytics for authoritative state. Ignoring read-model lag.

----------------------------------------------------------

## Search

What it IS

The queryable index (Elasticsearch) over catalog/content for discovery.

What it is NOT

Not the system of record, not a write target, not truth.

Primary Owner

Search domain (ES).

Common Systems

Catalog, Storefront, CommerceHub.

Common AI Mistakes

Writing to search as truth. Trusting search over SQL for stock.

----------------------------------------------------------

## Identity

What it IS

The verified representation of who a user/principal is (subject, claims).

What it is NOT

Not authentication, not authorization, not a role by itself.

Primary Owner

Identity service.

Common Systems

Auth, Authorization, Tenant scoping, all secured systems.

Common AI Mistakes

Confusing identity with authentication. Trusting identity claims without
verification.

----------------------------------------------------------

## Authentication

What it IS

The process of verifying a principal's identity (login, token, JWT).

What it is NOT

Not authorization, not identity definition, not a permission.

Primary Owner

Auth / identity service.

Common Systems

All secured endpoints.

Common AI Mistakes

Equating authentication with authorization. Accepting tokens without claim
verification.

----------------------------------------------------------

## Authorization

What it IS

The decision of what an authenticated principal is permitted to do.

What it is NOT

Not authentication, not identity, not tenancy alone.

Primary Owner

Authorization / governance.

Common Systems

All secured systems, operational surfaces.

Common AI Mistakes

Confusing authorization with authentication. Assuming tenant scope == full
authorization.

----------------------------------------------------------

## Governance

What it IS

The rules and controls defining who may operate what, and how (policies, roles,
boundaries).

What it is NOT

Not ownership of data, not a team, not operational action itself.

Primary Owner

Platform governance.

Common Systems

POS Admin, Super Admin, Authorization, Audit.

Common AI Mistakes

Confusing governance with ownership. Assuming Super Admin governs everything
including Service Marketplace.

----------------------------------------------------------

## Ownership

What it IS

Which service/domain is responsible for an entity's state and lifecycle.

What it is NOT

Not governance, not a team, not a UI.

Primary Owner

Architecture (lifecycle owners).

Common Systems

Payments, Orders, Fulfillment, Inventory.

Common AI Mistakes

Confusing ownership with governance. Writing to an entity from a non-owner.

----------------------------------------------------------

## Visibility

What it IS

Whether data is exposed to a role/tenant (can they see it).

What it is NOT

Not publication, not authorization alone, not ownership.

Primary Owner

Authorization / tenancy.

Common Systems

Storefront, POS Admin, CommerceHub, Service Marketplace.

Common AI Mistakes

Confusing visibility with publication. Assuming visible == published.

----------------------------------------------------------

## Publication

What it IS

The act of making an entity active/available to its consumers (publish state).

What it is NOT

Not visibility, not a read model, not mere storage.

Primary Owner

The owning domain's lifecycle.

Common Systems

Catalog, Storefront, CommerceHub, Promotions.

Common AI Mistakes

Equating publication with visibility. Publishing without lifecycle state.

----------------------------------------------------------

## Synchronization

What it IS

The process of bringing a derivative store (read model) in line with the write
model via the projection pipeline.

What it is NOT

Not truth, not the authority, not a one-time copy.

Primary Owner

Projection pipeline owner.

Common Systems

Mongo, Redis, ES, Analytics.

Common AI Mistakes

Assuming sync is instant. Writing to the derivative instead of syncing.

----------------------------------------------------------

## Recovery

What it IS

Restoring correct state or operation after a failure or interruption.

What it is NOT

Not reconciliation, not a business correction, not a refund.

Primary Owner

The affected domain's lifecycle owner.

Common Systems

Payments, Orders, Fulfillment, Projection Pipeline.

Common AI Mistakes

Confusing recovery with reconciliation. Recovering without fail-closed discipline.

----------------------------------------------------------

## Reconciliation

What it IS

Comparing records across systems (e.g. ledger vs provider) to confirm they
agree; some mismatches are intentional states.

What it is NOT

Not recovery, not a fix, not settlement.

Primary Owner

Treasury / reconciliation domain.

Common Systems

Ledger, Payments, Settlement, Payout.

Common AI Mistakes

Treating intentional reconciliation states as errors. Confusing with recovery.

----------------------------------------------------------

## Fail Closed

What it IS

The principle that on uncertainty or failure, the system denies/blocks rather
than allowing an unsafe action.

What it is NOT

Not fail-open, not a retry, not a soft warning.

Primary Owner

Architecture / security.

Common Systems

Payments, Auth, Tenancy, Checkout.

Common AI Mistakes

Failing open on payment/tenancy uncertainty. Treating a warning as safe.

----------------------------------------------------------

## Idempotency

What it IS

The property that repeating an operation produces the same result without
duplicate side effects.

What it is NOT

Not a retry by itself, not a cache, not eventual consistency.

Primary Owner

The operation's owning service.

Common Systems

Payments, Checkout, Outbox, Event publishing.

Common AI Mistakes

Missing idempotency keys on payment/checkout. Double-charging on retry.

----------------------------------------------------------

## Projection Pipeline

What it IS

The mechanism (Outbox -> consumers -> read stores) that builds and updates
projections.

What it is NOT

Not the system of record, not truth, not a single service.

Primary Owner

Projection / read-model owner.

Common Systems

Mongo, Redis, ES, Analytics, Notifications.

Common AI Mistakes

Writing directly to read stores. Assuming pipeline is always caught up.

----------------------------------------------------------

## Dangerous Operation

What it IS

An operation with high blast radius or irreversible impact that requires
controls, review, or gating.

What it is NOT

Not an emergency-only action, not a routine write, not a bug fix.

Primary Owner

The owning domain, under governance.

Common Systems

Payments, Ledger, Tenancy, Data deletion.

Common AI Mistakes

Confusing with emergency operation. Running without controls.

----------------------------------------------------------

## Emergency Operation

What it IS

A time-critical action taken to stop harm or restore service, often bypassing
normal flow under strict audit.

What it is NOT

Not a dangerous operation by default, not routine, not unlogged.

Primary Owner

Operations / on-call, under audit.

Common Systems

Incident response, Payments, Tenancy, Projection recovery.

Common AI Mistakes

Confusing with dangerous operation. Acting without audit trail.

----------------------------------------------------------

## Manual Review

What it IS

A human-in-the-loop step required before an action proceeds (e.g. suspicious
payment, payout approval).

What it is NOT

Not automation, not a terminal failure state, not a bug.

Primary Owner

The owning domain's review process.

Common Systems

Payments, Payout, Risk, Refunds.

Common AI Mistakes

Treating manual review as terminal FAILED. Auto-approving what needs review.

----------------------------------------------------------

## Evidence Queue

What it IS

A holding area for evidence awaiting verification or processing before it
affects state.

What it is NOT

Not the system of record, not a read model, not a business event by itself.

Primary Owner

The capturing domain (e.g. payments provider adapter).

Common Systems

Payments, Identity, Fulfillment webhooks.

Common AI Mistakes

Acting on queued evidence before verification. Treating the queue as truth.

----------------------------------------------------------

## Read Model Lag

What it IS

The delay between a write to the system of record and its appearance in a
projection/read model.

What it is NOT

Not truth changing, not a bug by itself, not eventual inconsistency of truth.

Primary Owner

Projection pipeline owner.

Common Systems

Mongo, Redis, ES, Analytics, Search.

Common AI Mistakes

Reading a lagged value as truth. Recomputing truth from a stale client total.

---

# Commonly Confused Terms

## Authority vs Evidence

| | Authority | Evidence |
|---|---|---|
| What | The source permitted to decide a fact. | Proof that a fact occurred, at its source. |
| Scope | Designates the system of record (PostgreSQL). | A recorded artifact (webhook, claim, entry). |
| Why they differ | Authority is *who decides*; evidence is *what proves*. A webhook is evidence that a payment happened, but PostgreSQL remains the authority that records it. You cannot make Mongo authoritative by collecting more evidence. |

## Authority vs Presentation

| | Authority | Presentation |
|---|---|---|
| What | The source that decides truth. | A rendered view of state. |
| Direction | Upstream of everything. | Downstream of truth. |
| Why they differ | Authority is the decider; presentation is a reflection. A displayed "Paid" badge is presentation and can be wrong or lagged — only the authority's record proves payment state. |

## Governance vs Ownership

| | Governance | Ownership |
|---|---|---|
| What | Rules/controls over who may operate what. | Which service is responsible for an entity's state. |
| Question | "May you?" | "Whose job is it?" |
| Why they differ | Governance sets boundaries; ownership assigns responsibility. Super Admin governs, but the Payment lifecycle owner *owns* payment state. Governance never makes a service the owner. |

## Payment vs Settlement

| | Payment | Settlement |
|---|---|---|
| What | Capturing money from the customer. | Moving captured funds to the right parties. |
| Time | At checkout/order. | After capture, net of fees. |
| Why they differ | Payment is intake; settlement is distribution. A payment can succeed while settlement (and payout) is still pending. They are different lifecycle stages owned by different services. |

## Settlement vs Accounting

| | Settlement | Accounting |
|---|---|---|
| What | Movement of funds between parties. | Ledger recording of financial movements. |
| Effect | Money actually moves. | Entries record that it moved. |
| Why they differ | Settlement is the physical/financial transfer; accounting is the immutable double-entry record of it. Accounting does not move money; it proves the move happened and enables reconciliation. |

## Refund vs Chargeback

| | Refund | Chargeback |
|---|---|---|
| What | Platform-initiated return of funds. | Issuer/bank-initiated forced reversal. |
| Initiator | The platform (lifecycle). | External bank/card network. |
| Why they differ | A refund is voluntary and platform-controlled; a chargeback is external, forced, and outside platform authority. Treating one as the other misroutes risk and reconciliation. |

## Vendor vs Customer

| | Vendor | Customer |
|---|---|---|
| What | Sells goods/services. | Buys them. |
| Role | Supply side. | Demand side. |
| Why they differ | A vendor fulfills and earns; a customer pays and receives. They sit on opposite sides of an order and must never be conflated in payout or attribution logic. |

## Tenant vs Vendor

| | Tenant | Vendor |
|---|---|---|
| What | Isolation namespace owning its data. | A seller within a tenant. |
| Scope | Container. | Content inside the container. |
| Why they differ | TenantId scopes isolation; a vendor is a business entity operating under a tenant. Many vendors can exist in one tenant. TenantId is necessary-not-sufficient for scoping a vendor's data. |

## Storefront Order vs Marketplace Order

| | Storefront Order | Marketplace Order |
|---|---|---|
| What | A single-vendor purchase. | A multi-vendor purchase split into Sub-Orders. |
| Structure | One order, one vendor. | Parent order + per-vendor Sub-Orders. |
| Why they differ | A storefront order maps 1:1 to fulfillment; a marketplace order must be split and settled at the Sub-Order level. Treating them identically breaks payout and fulfillment. |

## Reservation vs Attribution

| | Reservation | Attribution |
|---|---|---|
| What | Temporary hold of inventory/capacity. | Linkage of a sale to a source for commission. |
| Domain | Inventory/Checkout. | Affiliate/Marketing. |
| Why they differ | Reservation prevents oversell during checkout; attribution assigns credit after a sale. One is about stock, the other about commission. They share no lifecycle. |

## Visibility vs Publication

| | Visibility | Publication |
|---|---|---|
| What | Whether a role/tenant can see data. | Whether an entity is made active/available. |
| Question | "Can they see it?" | "Is it live?" |
| Why they differ | An entity can be published but not visible to a given role, or visible (via admin) but not published to customers. They are independent axes controlled by different rules. |

## Projection vs Truth

| | Projection | Truth |
|---|---|---|
| What | Derived, read-optimized view. | Authoritative current state. |
| Source | Built from truth via Outbox. | Lives in the system of record. |
| Why they differ | A projection is a copy that can lag or be wrong; truth is the single correct state. Acting on a projection as if it were truth causes oversell, double-charge, and leaks. |

## Read Model vs Write Model

| | Read Model | Write Model |
|---|---|---|
| What | Query-optimized store. | Authoritative normalized store where mutations happen. |
| Mutability | Populated by projections; not a business write target. | The only place business state is mutated. |
| Why they differ | The write model is the system of record; the read model is derivative. Writing to a read model bypasses lifecycle owners and corrupts truth. |

## Notification vs Business Event

| | Notification | Business Event |
|---|---|---|
| What | A message delivered to a user/system. | A fact that occurred in the domain (via Outbox). |
| Purpose | Inform. | Drive state/projections. |
| Why they differ | A notification is a side effect of an event, not the event. Using a notification as evidence of state is wrong — the Outbox/business event is the source, and notifications can be lost or delayed. |

## Service Marketplace vs CommerceHub

| | Service Marketplace | CommerceHub |
|---|---|---|
| What | Internal POS-to-POS B2B inside POS Admin only. | Multi-vendor commerce platform for selling. |
| Customers | No external customers. | Vendors + customers via Storefront. |
| Why they differ | Service Marketplace coordinates service requests/chat/ratings and never owns payments, accounting, inventory, or orders. CommerceHub is the selling platform. They are different products with different ownership boundaries. |

## Affiliate vs CommerceHub

| | Affiliate | CommerceHub |
|---|---|---|
| What | Referral partner earning commission. | The commerce platform where selling happens. |
| Ownership | No orders/inventory; only attribution + commission. | Owns catalog, vendors, orders, inventory. |
| Why they differ | An affiliate drives traffic and earns; CommerceHub operates the store. Equating them misassigns ownership of orders and inventory. |

## Identity vs Authentication

| | Identity | Authentication |
|---|---|---|
| What | Who the principal is (subject + claims). | Verifying that identity. |
| Stage | The result represented. | The verification process. |
| Why they differ | Identity is the verified subject; authentication is how we know. You can have an identity concept without a completed authentication, and auth proves (not defines) identity. |

## Authentication vs Authorization

| | Authentication | Authorization |
|---|---|---|
| What | Verifying identity. | Deciding permitted actions. |
| Question | "Who are you?" | "What may you do?" |
| Why they differ | AuthN establishes the principal; AuthZ uses that principal to grant/deny. A user can be authenticated but unauthorized for an operational surface. They are sequential, not interchangeable. |

## Recovery vs Reconciliation

| | Recovery | Reconciliation |
|---|---|---|
| What | Restoring correct state after failure. | Comparing records to confirm agreement. |
| Action | Fix/restore. | Compare/verify. |
| Why they differ | Recovery changes state to heal it; reconciliation only compares and may find intentional mismatches. Reconciliation does not recover, and recovery does not assume records already agree. |

## Emergency Operation vs Dangerous Operation

| | Emergency Operation | Dangerous Operation |
|---|---|---|
| What | Time-critical action to stop harm/restore service. | High-blast-radius action needing controls. |
| Trigger | Incident/urgency. | Impact/irreversibility. |
| Why they differ | Emergency is about timing and harm-stoppage (often audited bypass); dangerous is about risk and required gating. An operation can be dangerous without being an emergency, and emergency without being routinely dangerous. They are gated differently. |

---

# Summary

- Glossary terms created: 62
- Comparison tables: 20
- Only GLOSSARY.md was created.
- No other files were modified.
- No memory writes occurred.
