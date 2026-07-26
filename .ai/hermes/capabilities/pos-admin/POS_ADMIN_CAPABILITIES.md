# POS Admin Business Capability Inventory

# Purpose

This document catalogs the durable Business Capabilities that belong to the POS Admin domain in the Shopixy / PointOfSale Knowledge Base.

It is a discovery inventory for future Hermes Operational Playbooks. It is not a playbook, not a system guide, not architecture documentation, and not a README. Its job is to help Hermes understand:

- which POS Admin business capabilities already exist in the Knowledge Base;
- which capabilities are major enough to deserve dedicated playbooks;
- which lifecycle or authority owner each capability depends on;
- which primary and related systems each capability touches;
- which capabilities are mission-critical, operational, or supporting.

The inventory treats POS Admin as the tenant operational surface: it initiates tenant business intent, CommerceHub Vendor Module intent, Affiliate operational intent, and Service Marketplace provider/consumer intent. Durable truth remains owned by PostgreSQL and the owning lifecycle/domain authority named for each capability.

# Discovery Rules

Capabilities were identified only from the existing Knowledge Base corpus:

- `.ai/hermes/systems/pos-admin/README.md`
- `.ai/hermes/TASK_ROUTING.md`
- `.ai/hermes/MENTAL_MODEL.md`
- `.ai/hermes/GLOSSARY.md`
- Core Guides: `.ai/hermes/02-architecture.md`, `03-inventory.md`, `04-payments.md`, `05-orders.md`, `06-security-identity.md`, `07-platform-overview.md`, `08-qa-philosophy.md`
- Cross-system certifications for POS Admin relationships: Storefront, CommerceHub, Affiliate, Service Marketplace, and Super Admin
- Existing certification documents referenced by the POS Admin guide where needed

A Business Capability is a durable business function. It remains meaningful if screens, components, endpoints, DTOs, route names, implementation classes, or UI flows change.

Business capability is not the same as UI page.

A page may expose several capabilities, and one capability may appear across many pages.

Business capability is not the same as component.

A component is implementation/presentation. A capability is the business function being operated.

Business capability is not the same as API.

An API endpoint is a delivery mechanism. A capability is the stable business function the endpoint supports.

Business capability is not the same as implementation detail.

Tables, services, DTO names, background jobs, caches, projections, and frontend state are not capabilities. They may be authority or evidence references, but they are not the business function itself.

Capabilities were included only when they are already present in the source guides or certification corpus. No future, speculative, or inferred capabilities are added.

# Capability Classification

## Catalog

| Capability Name | Purpose | Primary Lifecycle Owner | Primary System | Related Systems | Business Criticality | Recommended Playbook |
|---|---|---|---|---|---|---|
| Product Management | Define and maintain tenant products, variants, units, categories, media, and product lifecycle inputs. | Catalog/Product authority | POS Admin | Storefront, CommerceHub Vendor Module, Inventory, Search, Affiliate | Tier 1 | YES |
| Product Publication and Visibility | Control which tenant products become eligible for customer-facing presentation after backend admission. | Catalog/publication authority | POS Admin | Storefront, CommerceHub, Affiliate, Search | Tier 1 | YES |
| Storefront Content and CMS Management | Author and publish tenant content, pages, homepage/navigation content, SEO inputs, and public presentation content. | Catalog/publication and CMS authority | POS Admin | Storefront, Search, Analytics | Tier 2 | YES |
| Store Builder and Branding Configuration | Configure tenant storefront appearance, theme, branding, logo, layout, and public identity presentation. | Tenant/configuration authority | POS Admin | Storefront | Tier 3 | NO |

## Pricing

| Capability Name | Purpose | Primary Lifecycle Owner | Primary System | Related Systems | Business Criticality | Recommended Playbook |
|---|---|---|---|---|---|---|
| Pricing Management | Configure unit/default-unit pricing inputs while final sale and checkout totals remain backend-recomputed. | Pricing/Catalog authority | POS Admin | Storefront, Orders, Payments | Tier 1 | YES |
| Coupon Management | Configure coupon candidates and redemption inputs; backend authority decides eligibility, usage, amount, and final application. | Coupon/Promotion/Pricing authority | POS Admin | Storefront, Checkout, Orders, Payments | Tier 2 | YES |
| Promotion Management | Configure tenant promotion inputs whose financial effect is admitted during checkout/order authority. | Promotion/Pricing authority | POS Admin | Storefront, Checkout, Orders, Payments | Tier 2 | YES |
| Smart Offer Management | Configure Smart Offer inputs and presentation behavior; backend authority decides eligibility and financial effect. | Offers/Pricing authority | POS Admin | Storefront, Checkout, Orders | Tier 2 | YES |

## Inventory

| Capability Name | Purpose | Primary Lifecycle Owner | Primary System | Related Systems | Business Criticality | Recommended Playbook |
|---|---|---|---|---|---|---|
| Inventory Management | Maintain stock, reservations, transfers, adjustments, returns restoration, and append-only movement history. | Inventory authority | POS Admin | Storefront, CommerceHub, Orders, Purchasing, Search | Tier 1 | YES |
| Warehouse Management | Maintain tenant warehouses and warehouse eligibility for stock operations. | Inventory/Warehouse authority | POS Admin | Purchasing, Sales, Shipping | Tier 1 | YES |
| Purchasing and Procurement | Buy goods from suppliers, receive stock into warehouses, update cost authority, and process purchase returns. | Purchasing authority plus Inventory and Ledger authority | POS Admin | Suppliers, Inventory, Accounting | Tier 1 | YES |

## Customers

| Capability Name | Purpose | Primary Lifecycle Owner | Primary System | Related Systems | Business Criticality | Recommended Playbook |
|---|---|---|---|---|---|---|
| Customer Management | Maintain tenant customer records used by POS sales, support, orders, balances, and statements. | Customer/Party authority | POS Admin | Orders, Payments, Accounting, Storefront | Tier 2 | YES |
| Supplier Management | Maintain supplier records used by purchasing, supplier payments, outstanding balances, and statements. | Supplier/Party authority | POS Admin | Purchasing, Treasury, Accounting | Tier 2 | YES |
| Party Ledger Management | Operate customer/supplier opening balances, adjustments, payments, statements, and outstanding-balance reads. | Ledger/Party authority | POS Admin | Accounting, Payments, Treasury | Tier 1 | YES |

## Orders

| Capability Name | Purpose | Primary Lifecycle Owner | Primary System | Related Systems | Business Criticality | Recommended Playbook |
|---|---|---|---|---|---|---|
| POS Sales and Checkout | Convert in-store sale intent into authoritative sale/invoice state with server-computed totals, stock effects, payment tender, idempotency, and ledger effects. | Sales/Order lifecycle authority | POS Admin | Inventory, Payments, Accounting, Customers | Tier 1 | YES |
| Order Management | Review and operate admitted tenant orders without bypassing order lifecycle authority. | Order lifecycle authority | POS Admin | Storefront, Payments, Inventory, Fulfillment, Shipping | Tier 1 | YES |
| Fulfillment Management | Process allowed fulfillment progression after authoritative order creation. | Fulfillment lifecycle authority | POS Admin | Orders, Inventory, Shipping, Notifications | Tier 1 | YES |
| Returns Management | Process sale/order returns bounded by original invoice/order snapshots and prior returns. | Return lifecycle authority | POS Admin | Inventory, Payments, Accounting, Customers | Tier 1 | YES |

## Payments

| Capability Name | Purpose | Primary Lifecycle Owner | Primary System | Related Systems | Business Criticality | Recommended Playbook |
|---|---|---|---|---|---|---|
| Payment-at-Sale Management | Accept and reconcile POS sale tender such as Cash, Card, or Mixed against server-authoritative totals. | Payment lifecycle authority | POS Admin | Orders, Accounting, Treasury | Tier 1 | YES |
| Refund Management | Initiate or operate refund/return money effects only through refund/payment lifecycle authority. | Refund/Payment lifecycle authority | POS Admin | Orders, Returns, Accounting, Treasury | Tier 1 | YES |
| Treasury and Cashbox Management | Operate cashbox, bank, transfers, expenses, income, supplier payments, and treasury movements that post to ledger authority. | Treasury/Financial posting authority | POS Admin | Accounting, Payments, Suppliers | Tier 1 | YES |
| Accounting and Ledger Management | Preserve immutable double-entry financial truth and ensure POS Admin money mutations post through ledger authority. | Accounting/Ledger authority | POS Admin | Payments, Treasury, Reports, Affiliate, CommerceHub | Tier 1 | YES |
| Financial Reporting | Produce trial balance, P&L, balance sheet, cash flow, receivables, payables, inventory valuation, and dashboards from SQL/ledger truth. | Financial reporting authority | POS Admin | Accounting, Inventory, Treasury, Analytics | Tier 2 | YES |

## Marketing

| Capability Name | Purpose | Primary Lifecycle Owner | Primary System | Related Systems | Business Criticality | Recommended Playbook |
|---|---|---|---|---|---|---|
| Affiliate Configuration and Campaign Operations | Configure tenant-side affiliate campaign, commission, product/category eligibility, participation, and operational inputs. | Affiliate authority | POS Admin Affiliate Operational Surface | Affiliate, Storefront, Orders, Payments | Tier 2 | YES |
| Affiliate Payout Review and Payment Proof | Review payout requests, approve/reject review intent, and submit payment-reference/proof intent without owning payout truth. | Affiliate payout/settlement authority | POS Admin Affiliate Operational Surface | Affiliate, Payments, Accounting, Treasury | Tier 1 | YES |
| Notifications Management | Trigger and consume communication outputs from domain events without treating delivery as business truth. | Notification/event authority | POS Admin | Orders, Payments, Shipping, Affiliate, Service Marketplace, WhatsApp | Tier 3 | NO |
| WhatsApp Communication Management | Configure and initiate WhatsApp communication and admitted OTP/verification workflows as transport evidence only. | WhatsApp transport plus Identity/Notification authority | POS Admin | Storefront, Identity, Notifications | Tier 3 | NO |
| Analytics Configuration and Reporting Inputs | Configure tenant analytics integrations and consume derived operational metrics without making analytics authoritative. | Analytics/projection authority | POS Admin | Storefront, Affiliate, Service Marketplace, Reports | Tier 3 | NO |

## Administration

| Capability Name | Purpose | Primary Lifecycle Owner | Primary System | Related Systems | Business Criticality | Recommended Playbook |
|---|---|---|---|---|---|---|
| Tenant Configuration | Manage tenant settings, currency, locale, regional configuration, operational defaults, storefront settings, and tenant business configuration. | Tenant/configuration authority | POS Admin | Storefront, Payments, Pricing, Security | Tier 2 | YES |
| Tenant-Scoped Search and Projection Repair | Trigger tenant-scoped SQL-recomposed search/read-model repair without performing global platform repair. | Projection/Search authority | POS Admin | Search, Catalog, Inventory, Storefront, Super Admin | Tier 2 | YES |
| Operational Reporting and Dashboard Consumption | Consume operational dashboards and reports while preserving SQL/ledger/projection authority boundaries. | Reporting/Analytics authority | POS Admin | Accounting, Inventory, Orders, Payments | Tier 3 | NO |

## Security

| Capability Name | Purpose | Primary Lifecycle Owner | Primary System | Related Systems | Business Criticality | Recommended Playbook |
|---|---|---|---|---|---|---|
| Operator Identity and Session Management | Authenticate tenant operators, refresh/logout sessions, onboard tenant owner/cashier identities, and fail closed on stale or revoked access. | Identity/session authority | POS Admin | Security/Identity, Tenant authority | Tier 1 | YES |
| Role and Authorization Management | Enforce POS Admin role boundaries for cashier, manager, warehouse, finance, support, marketing, owner, and admin operations. | Authorization/role authority | POS Admin | Security/Identity, Tenant authority | Tier 1 | YES |
| Tenant Isolation | Ensure every POS Admin operation remains within the trusted tenant scope and never reads or mutates another tenant's data. | Tenant authority | POS Admin | All systems | Tier 1 | YES |

## Configuration

| Capability Name | Purpose | Primary Lifecycle Owner | Primary System | Related Systems | Business Criticality | Recommended Playbook |
|---|---|---|---|---|---|---|
| Storefront Settings Management | Configure public storefront settings that Storefront may present after backend admission. | Tenant/configuration authority | POS Admin | Storefront, CMS, Branding, Analytics | Tier 2 | YES |
| Payment Method Configuration | Configure tenant payment-method eligibility and provider availability while payment finality remains lifecycle-owned. | Tenant payment-method and Payment authority | POS Admin | Storefront, Payments, Checkout | Tier 1 | YES |
| Shipping Method Configuration | Configure tenant shipping options, costs, ETA candidates, zones, and carrier-facing inputs. | Shipping/Order/Fulfillment authority | POS Admin | Storefront, Orders, Fulfillment, Carriers | Tier 2 | YES |

## CommerceHub Vendor Module

| Capability Name | Purpose | Primary Lifecycle Owner | Primary System | Related Systems | Business Criticality | Recommended Playbook |
|---|---|---|---|---|---|---|
| CommerceHub Vendor Participation | Allow a POS tenant to participate as a CommerceHub vendor through an embedded operational surface. | CommerceHub vendor/participation authority | POS Admin Vendor Module | CommerceHub, Super Admin, Catalog, Inventory | Tier 2 | YES |
| Marketplace Order Fulfillment Operations | Review vendor-facing marketplace sub-orders and submit allowed fulfillment, cancellation, or delivery intent. | CommerceHub marketplace order/sub-order authority | POS Admin Vendor Module | CommerceHub, Orders, Inventory, Shipping, Payments | Tier 1 | YES |
| Marketplace Earnings and Finance Visibility | Present vendor earnings, marketplace finance, COD debt, settlement, payout readiness, and payment evidence without making them tenant accounting. | CommerceHub finance/settlement authority | POS Admin Vendor Module | CommerceHub, Payments, Accounting, Super Admin | Tier 2 | YES |
| Marketplace Governance Response | Consume vendor status, suspension, ban, visibility, recovery, and governance evidence while preserving tenant-business independence. | CommerceHub governance/Super Admin authority | POS Admin Vendor Module | CommerceHub, Super Admin, Search | Tier 2 | YES |

## Service Marketplace Participation

| Capability Name | Purpose | Primary Lifecycle Owner | Primary System | Related Systems | Business Criticality | Recommended Playbook |
|---|---|---|---|---|---|---|
| Service Provider Operations | Let authenticated POS Admin tenant actors maintain provider profiles, publish services, review requests, accept/reject requests, and mark provider-side completion. | Service Marketplace authority | POS Admin Service Marketplace | Service Marketplace, Identity, Notifications, Search | Tier 2 | YES |
| Service Consumer Operations | Let authenticated POS Admin tenant actors discover internal services, submit service requests, collaborate, confirm completion, and submit eligible ratings/reviews. | Service Marketplace authority | POS Admin Service Marketplace | Service Marketplace, Identity, Notifications, Search | Tier 2 | YES |
| Service Request Lifecycle Management | Coordinate request admission, acceptance/rejection, provider completion, consumer final completion, closure, and recovery. | Service Marketplace request lifecycle authority | POS Admin Service Marketplace | Service Marketplace, Notifications, Analytics, Search | Tier 1 | YES |
| Request-Bound Chat and Attachments | Support collaboration evidence inside an admitted service request without making chat or attachments lifecycle authority. | Service Marketplace chat/participant authority | POS Admin Service Marketplace | Service Marketplace, Notifications, Storage, Identity | Tier 3 | NO |
| Service Ratings, Reviews, Trust, and Reputation | Present ratings/reviews/trust after final completed request authority admits eligibility and review state. | Service Marketplace rating/reputation authority | POS Admin Service Marketplace | Service Marketplace, Analytics, Search | Tier 2 | YES |

# Capability Dependency Map

This map lists major POS Admin capabilities and the systems or business facts they consume, produce, touch, and must never own.

## Product Management

Consumes:
- Tenant authority
- Product/category/unit/variant lineage
- Inventory purchasability evidence
- Publication state

Produces:
- Catalog operational intent
- Product/variant/category/unit updates after backend admission
- Publication/search/projection convergence candidates

Touches:
- Storefront catalog presentation
- CommerceHub vendor listing eligibility
- Affiliate product eligibility
- Search projections
- Inventory availability presentation

Never Owns:
- Inventory stock truth
- Checkout totals
- Storefront presentation truth
- CommerceHub marketplace governance
- Affiliate attribution or commission

## Product Publication and Visibility

Consumes:
- Current product lifecycle state
- Category, media, variant, price, stock, tenant, and governance authority
- Search/projection convergence rules

Produces:
- Publication intent
- Visibility eligibility after SQL authority admits it
- Projection/reindex candidates

Touches:
- Storefront product lists and detail
- CommerceHub listing visibility
- Affiliate product discovery
- Elasticsearch/Mongo/Redis derivative views

Never Owns:
- Search as truth
- Inventory availability
- Marketplace governance
- Customer checkout eligibility without backend recomputation

## Pricing Management

Consumes:
- Product/unit price authority
- Tenant currency/configuration
- Promotion/coupon/Smart Offer candidates
- Current checkout/order authority

Produces:
- Price input changes
- Pricing candidates for sale, Storefront, and checkout recomputation

Touches:
- POS sales
- Storefront checkout
- Coupons, promotions, Smart Offers
- Payments and accounting snapshots

Never Owns:
- Final checkout payable amount by itself
- Payment finality
- Historical commission/order snapshots
- Client-submitted totals

## Inventory Management

Consumes:
- Current product/variant/warehouse lineage
- Reservations
- Stock movements
- Returns and fulfillment evidence

Produces:
- Stock mutations admitted by inventory authority
- Reservation/release/commit effects
- Append-only movement history
- Projection convergence events

Touches:
- POS sales
- Storefront checkout
- CommerceHub marketplace order fulfillment
- Purchasing
- Returns
- Search availability

Never Owns:
- Payment finality
- Order lifecycle state
- Accounting truth
- Marketplace governance
- Search documents as stock truth

## Warehouse Management

Consumes:
- Tenant authority
- Warehouse activity state
- Product/variant/stock lineage

Produces:
- Warehouse eligibility for stock movement, transfer, receiving, reservation, and return restoration

Touches:
- Purchasing
- Inventory transfers
- POS sale availability
- Shipping/fulfillment handoff

Never Owns:
- Product publication
- Payment/ledger truth
- Cross-tenant warehouse access

## Purchasing and Procurement

Consumes:
- Supplier authority
- Product/variant/unit authority
- Warehouse authority
- Inventory authority
- Ledger authority

Produces:
- Purchase receiving intent
- Stock and cost effects after admission
- Supplier liability/accounting effects
- Purchase-return correction paths

Touches:
- Suppliers
- Inventory
- Party ledger
- Accounting
- Reports

Never Owns:
- Arbitrary inventory adjustment outside purchasing authority
- Completed purchase rewriting
- Supplier balance shadows as truth

## POS Sales and Checkout

Consumes:
- Customer authority
- Product/price/tax/discount authority
- Inventory/warehouse authority
- Payment-method/tender authority
- Idempotency and tenant authority

Produces:
- Authoritative sale/invoice after backend admission
- Stock reservation/commit effects
- Payment-at-sale evidence
- Ledger/accounting effects

Touches:
- Orders
- Inventory
- Payments
- Customer ledger
- Treasury
- Reports

Never Owns:
- Client-submitted totals
- Payment lifecycle state outside the payment owner
- Inventory stock mutation outside inventory authority
- Ledger rows directly

## Order Management

Consumes:
- Backend-admitted order state
- Payment state
- Fulfillment state
- Inventory reservation/commit state
- Tenant/customer authority

Produces:
- Operational review and allowed order intent
- Customer support and fulfillment workflows

Touches:
- Storefront orders
- POS sales invoices
- Payments
- Fulfillment
- Returns/refunds
- Notifications

Never Owns:
- Direct lifecycle state mutation
- CommerceHub sub-order rules as generic order rules
- Missing projection as proof no order exists

## Fulfillment Management

Consumes:
- Order lifecycle authority
- Inventory reservation/stock authority
- Shipment/carrier evidence
- Tenant and role authority

Produces:
- Fulfillment progression intent after backend admission
- Shipment and notification triggers

Touches:
- Orders
- Inventory
- Shipping
- Payments/COD evidence where applicable
- Notifications

Never Owns:
- Payment collection truth
- Shipment evidence as final order truth
- Direct order status override

## Returns Management

Consumes:
- Original order/invoice snapshots
- Prior return history
- Inventory restoration authority
- Refund/payment authority

Produces:
- Bounded return intent
- Stock restoration candidates
- Refund/accounting effects after admission

Touches:
- Payments
- Inventory
- Accounting
- Customer ledger
- Reports

Never Owns:
- Client return prices/tax totals
- Unbounded quantity/amount return
- Refund finality outside payment/refund authority

## Payment-at-Sale Management

Consumes:
- Server-authoritative sale totals
- Tenant payment-method/tender eligibility
- Provider/manual evidence where relevant
- Payment lifecycle authority

Produces:
- Cash/card/mixed tender evidence
- Payment operation intent for admitted sale

Touches:
- Sales
- Orders
- Treasury
- Accounting
- Reports

Never Owns:
- Provider callbacks as final truth
- Deferred credit sale execution where the corpus says fail closed
- Marketplace payments
- Storefront provider finality

## Refund Management

Consumes:
- Return/order authority
- Payment/refund lifecycle authority
- Settlement/readiness evidence
- Ledger authority

Produces:
- Refund intent and admitted refund effects
- Accounting/reconciliation evidence

Touches:
- Returns
- Payments
- Treasury
- Accounting
- Customer communication

Never Owns:
- Refund success from UI or provider payload alone
- Chargeback authority
- Outward money without readiness gates

## Treasury and Cashbox Management

Consumes:
- Tenant/role authority
- Payment and sale evidence
- Supplier/customer party authority
- Ledger authority

Produces:
- Cashbox open/close, bank, transfer, expense, income, and supplier-payment intent
- Balanced financial posting after admission

Touches:
- Accounting
- Payments
- Suppliers
- Reports
- Payout review surfaces

Never Owns:
- Ledger rows directly
- Marketplace finance
- Affiliate payout settlement
- Provider/bank finality by presentation

## Accounting and Ledger Management

Consumes:
- Admitted sale, payment, return, treasury, purchase, supplier, affiliate, and marketplace finance events
- Accounting outbox/evidence

Produces:
- Immutable double-entry ledger truth
- Financial reporting basis
- Reconciliation basis

Touches:
- Payments
- Treasury
- Purchasing
- Party ledger
- Affiliate payouts
- CommerceHub finance

Never Owns:
- Money movement itself
- Payment finality
- Settlement membership outside live SQL lineage
- Dashboard totals as truth

## Financial Reporting

Consumes:
- Posted ledger history
- SQL stock truth
- Party ledger history
- Date/filter criteria

Produces:
- Trial balance, P&L, balance sheet, cash flow, receivable/payable, inventory valuation, dashboards

Touches:
- Accounting
- Inventory
- Treasury
- Party ledger
- Analytics

Never Owns:
- Ledger mutation
- Stock mutation
- Cached dashboard values as authority

## Operator Identity, Role, and Tenant Isolation

Consumes:
- Current SQL user/session/role/tenant authority
- Trusted tenant resolution
- Active tenant lineage
- Authorization rules

Produces:
- Admitted POS Admin access and role-scoped operational capability
- Fail-closed rejection of stale, revoked, disabled, cross-tenant, or over-privileged access

Touches:
- Every POS Admin capability
- Security/Identity
- Super Admin boundary
- CommerceHub Vendor Module
- Service Marketplace

Never Owns:
- Super Admin platform authority
- Cross-tenant access
- JWT/claims as authority
- Frontend role toggles as authorization

## Tenant Configuration

Consumes:
- Tenant authority
- Storefront settings authority
- Payment/shipping/currency/locale configuration rules

Produces:
- Tenant operational settings and admitted storefront-facing configuration

Touches:
- Storefront
- Payments
- Shipping
- Pricing
- CMS
- Analytics

Never Owns:
- Tenant identity from browser/route state
- Cross-tenant configuration
- CommerceHub EGP marketplace currency

## Tenant-Scoped Search and Projection Repair

Consumes:
- Current SQL catalog/publication/inventory/media/category authority
- Projection lag/rebuild candidates
- Tenant scope

Produces:
- Tenant-scoped reindex/repair/rebuild intent
- SQL-recomposed projection convergence

Touches:
- Search
- Catalog
- Storefront
- CommerceHub/Affiliate discovery candidates
- Analytics/read models

Never Owns:
- Global index deletion/recreation
- All-tenant repair
- Projection as truth
- Platform dangerous repair authority

## Shipping Method Configuration and Shipping Operations

Consumes:
- Tenant shipping configuration
- Order/fulfillment authority
- Carrier evidence
- Geographic and cost eligibility

Produces:
- Shipping option candidates
- Carrier handoff/shipment intent
- Tracking evidence after admission

Touches:
- Storefront checkout
- Orders
- Fulfillment
- Payments/COD evidence
- Notifications

Never Owns:
- Fulfillment truth by carrier event alone
- Order delivery finality from tracking presentation
- Payment collection truth

## Affiliate Configuration and Campaign Operations

Consumes:
- Tenant authority
- Affiliate identity/governance authority
- Product/category/price/stock eligibility
- Storefront order/checkout authority

Produces:
- Campaign, commission, eligibility, and affiliate-management operational intent
- Affiliate operational evidence and reports after backend admission

Touches:
- Storefront referral entry
- Orders
- Payments
- Accounting
- Analytics
- Notifications

Never Owns:
- Attribution finality
- Commission snapshots
- Affiliate session truth
- Storefront checkout authority
- CommerceHub marketplace attribution

## Affiliate Payout Review and Payment Proof

Consumes:
- Payout request snapshots
- Affiliate balance/commission authority
- Wallet/destination evidence
- Readiness gates
- Payment/accounting authority

Produces:
- Review, approval/rejection, payment-reference, and payment-proof intent after admission
- Paid evidence only when backend authority admits it

Touches:
- Affiliate
- Payments
- Accounting
- Treasury
- Fraud/recovery

Never Owns:
- Amount, wallet, affiliate, store, currency, executor, or paid state from frontend DTOs
- External bank/provider finality
- Settlement truth
- Duplicate withdrawal funding

## CommerceHub Vendor Participation

Consumes:
- Tenant/vendor eligibility
- CommerceHub governance
- Product/listing/inventory/price evidence
- Super Admin governance outcomes

Produces:
- Vendor participation intent
- Vendor-facing operational evidence
- Marketplace participation state after backend admission

Touches:
- CommerceHub
- Super Admin
- Catalog
- Inventory
- Search

Never Owns:
- Marketplace authority itself
- Tenant business ownership
- Storefront authority
- Platform governance

## Marketplace Order Fulfillment Operations

Consumes:
- CommerceHub marketplace order/sub-order authority
- StoreId/vendor scope
- Payment/COD readiness gates
- Inventory reservation/stock authority

Produces:
- Vendor-facing fulfillment, cancellation, or delivery intent after admission

Touches:
- CommerceHub marketplace orders
- Inventory
- Shipping
- Payments
- Vendor earnings

Never Owns:
- Generic POS/Storefront order lifecycle
- Another tenant's sub-order
- Marketplace finance/payout readiness
- Super Admin governance

## Marketplace Earnings and Finance Visibility

Consumes:
- CommerceHub earnings, settlement, COD debt, payout readiness, refund impact, payment evidence, and ledger authority

Produces:
- Vendor-facing finance and earnings visibility
- Operational response to readiness or recovery state

Touches:
- CommerceHub finance
- Payments
- Accounting
- Super Admin
- Reports

Never Owns:
- Tenant accounting
- Tenant treasury
- POS payment-at-sale
- Storefront payment methods
- Settlement finality by presentation

## Marketplace Governance Response

Consumes:
- CommerceHub/Super Admin governance state
- Vendor eligibility, suspension, ban, visibility, recovery, and debt evidence

Produces:
- Vendor operational response to marketplace governance outcomes

Touches:
- CommerceHub Vendor Module
- Super Admin
- Marketplace visibility/search
- Vendor operations

Never Owns:
- Governance decision authority
- Tenant disablement outside CommerceHub
- Inventory/payment/order lifecycle truth
- Service Marketplace governance

## Service Provider Operations

Consumes:
- POS Admin tenant actor identity
- Provider profile authority
- Service listing/publication authority
- Service request authority

Produces:
- Provider profile/service publication intent
- Request review, acceptance/rejection, provider completion intent

Touches:
- Service Marketplace
- Search/discovery
- Chat
- Notifications
- Ratings/trust

Never Owns:
- Payments, accounting, inventory, or orders
- Storefront/CommerceHub/Affiliate participants
- Chat as lifecycle truth
- Cross-tenant service authority outside admitted request boundaries

## Service Consumer Operations

Consumes:
- POS Admin tenant actor identity
- Service discovery/detail evidence
- Provider profile/trust evidence
- Request authority

Produces:
- Service request intent
- Consumer confirmation and rating/review intent after eligibility

Touches:
- Service Marketplace
- Search
- Chat/attachments
- Notifications
- Trust/reputation

Never Owns:
- Customer-facing commerce
- Generic orders or CommerceHub sub-orders
- Payment/accounting truth
- Search result as request authority

## Service Request Lifecycle Management

Consumes:
- Service listing authority
- Provider/consumer participant authority
- Request lifecycle state
- Chat/completion/rating eligibility evidence

Produces:
- Submitted, accepted/rejected, provider-completed, consumer-confirmed, closed, rated/reviewed request state after backend admission

Touches:
- Service Provider Operations
- Service Consumer Operations
- Chat
- Ratings/reviews
- Notifications
- Analytics/recovery

Never Owns:
- Order lifecycle
- CommerceHub marketplace lifecycle
- Payment finality
- Accounting truth
- Inventory truth

## Service Ratings, Reviews, Trust, and Reputation

Consumes:
- Final completed service request authority
- Review/rating eligibility
- Trust/reputation derivation

Produces:
- Rating/review/trust presentation after admission

Touches:
- Service discovery
- Provider profile
- Analytics
- Search/reputation presentation

Never Owns:
- Storefront product reviews
- CommerceHub marketplace governance
- Affiliate credibility
- Payment/accounting/order truth

# Capability Priority

## Tier 1 — Mission-critical

Tier 1 capabilities directly protect money, stock, order lifecycle, tenant isolation, customer/supplier balances, marketplace sub-order operations, or service-request lifecycle truth. A defect in these areas can create financial corruption, oversell, cross-tenant leakage, lifecycle bypass, irreversible fulfillment mistakes, or incorrect outward-money readiness.

Tier 1 capabilities:

1. Product Management
2. Product Publication and Visibility
3. Pricing Management
4. Inventory Management
5. Warehouse Management
6. Purchasing and Procurement
7. Party Ledger Management
8. POS Sales and Checkout
9. Order Management
10. Fulfillment Management
11. Returns Management
12. Payment-at-Sale Management
13. Refund Management
14. Treasury and Cashbox Management
15. Accounting and Ledger Management
16. Affiliate Payout Review and Payment Proof
17. Operator Identity and Session Management
18. Role and Authorization Management
19. Tenant Isolation
20. Payment Method Configuration
21. Marketplace Order Fulfillment Operations
22. Service Request Lifecycle Management

Why Tier 1:

- They mutate or guard protected business state.
- They sit on lifecycle, ledger, stock, payment, order, tenant, or service-request authority boundaries.
- They should receive dedicated playbooks first because operators and Hermes must not improvise around these boundaries.

## Tier 2 — Operational

Tier 2 capabilities are business-operational capabilities that configure or operate important workflows but generally do not by themselves finalize money, stock, or protected lifecycle truth without re-entering backend authority. They deserve playbooks when they are broad, cross-system, or prone to authority confusion.

Tier 2 capabilities:

1. Storefront Content and CMS Management
2. Coupon Management
3. Promotion Management
4. Smart Offer Management
5. Customer Management
6. Supplier Management
7. Financial Reporting
8. Affiliate Configuration and Campaign Operations
9. Tenant Configuration
10. Tenant-Scoped Search and Projection Repair
11. Storefront Settings Management
12. Shipping Method Configuration
13. CommerceHub Vendor Participation
14. Marketplace Earnings and Finance Visibility
15. Marketplace Governance Response
16. Service Provider Operations
17. Service Consumer Operations
18. Service Ratings, Reviews, Trust, and Reputation

Why Tier 2:

- They are daily operational capabilities.
- They often feed Tier 1 authority but should not be confused with authority themselves.
- They frequently touch multiple systems and need guidance to prevent boundary mistakes.

## Tier 3 — Supporting

Tier 3 capabilities support operations, communication, presentation, observation, or collaboration evidence. They are important, but they should not be playbooked before mission-critical and core operational capabilities unless an incident or product need requires it.

Tier 3 capabilities:

1. Store Builder and Branding Configuration
2. Notifications Management
3. WhatsApp Communication Management
4. Analytics Configuration and Reporting Inputs
5. Operational Reporting and Dashboard Consumption
6. Request-Bound Chat and Attachments

Why Tier 3:

- They are mostly presentation, communication, analytics, or collaboration surfaces.
- They produce evidence or derivative views rather than owning durable business truth.
- Their playbooks can usually reference higher-tier authority playbooks instead of standing alone.

# Suggested Playbook Order

Recommended order for future Hermes Operational Playbooks:

1. Tenant Isolation, Operator Identity, and Role Authorization
2. Product Management
3. Product Publication and Visibility
4. Inventory Management
5. Warehouse Management
6. Purchasing and Procurement
7. Pricing Management
8. POS Sales and Checkout
9. Payment-at-Sale Management
10. Order Management
11. Fulfillment Management
12. Returns Management
13. Refund Management
14. Treasury and Cashbox Management
15. Accounting and Ledger Management
16. Party Ledger Management
17. Payment Method Configuration
18. Shipping Method Configuration and Shipping Operations
19. Customer Management and Supplier Management
20. Financial Reporting
21. Coupon Management
22. Promotion Management
23. Smart Offer Management
24. Storefront Settings, CMS, Store Builder, Branding, and SEO
25. Tenant Configuration
26. Tenant-Scoped Search and Projection Repair
27. Affiliate Configuration and Campaign Operations
28. Affiliate Payout Review and Payment Proof
29. CommerceHub Vendor Participation
30. Marketplace Order Fulfillment Operations
31. Marketplace Earnings and Finance Visibility
32. Marketplace Governance Response
33. Service Provider Operations
34. Service Consumer Operations
35. Service Request Lifecycle Management
36. Service Ratings, Reviews, Trust, and Reputation
37. Notifications Management
38. WhatsApp Communication Management
39. Analytics Configuration and Operational Dashboard Consumption
40. Request-Bound Chat and Attachments

Reasoning:

- Start with identity, role, and tenant isolation because every POS Admin capability depends on them.
- Then document catalog/product/publication because Storefront, inventory, pricing, search, CommerceHub, and Affiliate all consume admitted catalog authority.
- Inventory, warehouse, and purchasing come before sales because sale/checkout correctness depends on current stock and lineage.
- Pricing comes before checkout/payment because sale totals and checkout totals must be recomputed from authoritative pricing inputs.
- Sales, payment, order, fulfillment, returns, refunds, treasury, accounting, and party ledger form the core money-and-stock chain and should be playbooked before supporting surfaces.
- Configuration, reporting, search repair, and marketing capabilities come next because they are operationally important but depend on the core authority chain.
- Affiliate, CommerceHub Vendor Module, and Service Marketplace playbooks should follow once the core POS Admin authority model is established, because each is an embedded operational surface that is easy to confuse with ownership.
- Communication, WhatsApp, analytics, dashboards, and chat should come last because they are evidence/presentation/collaboration surfaces and must reference the owning authority playbooks rather than define their own truth.

# Future Expansion

Before creating a new POS Admin playbook for a future capability, evaluate the candidate with these rules:

1. Confirm it is a durable business function, not a page, component, endpoint, dialog, export, import, button, or implementation detail.
2. Locate it in the existing Knowledge Base before naming it as a capability. If the corpus does not already describe it, do not invent it.
3. Identify the primary lifecycle or authority owner. If no owner can be named, the candidate is not ready for a playbook.
4. Identify the primary system and related systems. A POS Admin capability may operate another system's surface, but it must not be described as owning that system's truth unless the corpus says so.
5. Decide whether the capability mutates protected state, configures inputs, presents evidence, or supports communication/analytics.
6. Assign the priority tier based on business risk:
   - Tier 1 if it can corrupt money, stock, orders, payments, ledger, tenant isolation, or protected lifecycle truth.
   - Tier 2 if it configures or operates important business workflows that feed Tier 1 authority.
   - Tier 3 if it is mainly presentation, communication, analytics, or collaboration evidence.
7. Recommend a dedicated playbook only when the capability has recurring operational decisions, authority boundaries, lifecycle ownership, fail-closed behavior, or cross-system confusion risk.
8. Do not create a playbook for a capability until the capability inventory names its owner, systems touched, dependency map, priority tier, and playbook need.
9. Keep capability documents separate from operational playbooks: capability inventories classify what exists; playbooks instruct how Hermes should operate it.

# Source Documents

Primary Knowledge Base documents synthesized:

- `.ai/hermes/systems/pos-admin/README.md`
- `.ai/hermes/TASK_ROUTING.md`
- `.ai/hermes/MENTAL_MODEL.md`
- `.ai/hermes/GLOSSARY.md`
- `.ai/hermes/02-architecture.md`
- `.ai/hermes/03-inventory.md`
- `.ai/hermes/04-payments.md`
- `.ai/hermes/05-orders.md`
- `.ai/hermes/06-security-identity.md`
- `.ai/hermes/07-platform-overview.md`
- `.ai/hermes/certification/01-pos-admin-storefront.md`
- `.ai/hermes/certification/02-pos-admin-commercehub.md`
- `.ai/hermes/certification/03-pos-admin-affiliate.md`
- `.ai/hermes/certification/04-pos-admin-service-marketplace.md`
- `.ai/hermes/certification/05-pos-admin-super-admin.md`

Certification families consulted through POS Admin guide references:

- POS Admin product, inventory, purchasing, sales, customer/supplier, treasury, reporting, user-role, tenant, and session authority certifications
- POS Admin payout review/payment-proof certifications
- Storefront relationship certifications for catalog, pricing, checkout, payment, shipping, CMS, analytics, WhatsApp, and configuration boundaries
- CommerceHub Vendor Module / CommerceHub relationship certifications for vendor participation, marketplace order, marketplace finance, governance, visibility, earnings, payments, and settlement boundaries
- Affiliate relationship certifications for campaign, attribution, commission, payout, fraud, analytics, and notification boundaries
- Service Marketplace certifications for listing, request engagement, chat, completion, rating/reputation, analytics, frontend final certification, search, recovery, and trust boundaries

# Inventory Summary

- Number of capabilities discovered: 46
- Number of Tier 1 capabilities: 22
- Number of Tier 2 capabilities: 18
- Number of Tier 3 capabilities: 6
- Recommended dedicated playbooks: 40
- Not recommended as dedicated standalone playbooks: 6

This document created only the POS Admin capability inventory. It does not create any operational playbooks.
