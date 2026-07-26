# PASS SF-SHP2 Shipping Session Authority Certification

PointOfSale Storefront Shipping Session runtime is production-certifiable after quote-boundary cache and DTO-admission hardening.

Shipping Session remains backend-owned. The frontend owns only address candidates, selected shipping-method candidates, refresh/retry/navigation, and admitted presentation. Backend checkout-session and checkout services own session identity, cart/customer/tenant binding, selected-method admission, address binding, pricing/quote evidence, checkout token issuance, expiration/recovery handling, persistence, and SQL truth.

Runtime review covered only Shipping Session runtime: Storefront checkout shipping/address/summary, shipping method selector, checkout pricing preview, checkout session create/update/recovery, shipping quote read presentation, directly consumed backend checkout/shipping APIs, and admin shipping method/configuration surfaces that feed Storefront selection. Shipping fee calculation, carrier APIs, shipment creation, tracking, fulfillment, split orders, audit, warehouse, and inventory internals were not recertified here.

Closed risk:
- `POST /api/storefront/shipping/quotes` returned quote-shaped presentation evidence without explicit no-store semantics.
- Legacy Storefront shipping quote presentation accepted raw quote arrays into React state without one admission boundary.

Hardening:
- Added no-store response semantics to Storefront shipping quote responses.
- Added no-store frontend request headers for quote reads.
- Added Storefront quote DTO admission before React state, rejecting malformed quote options, duplicate quote identity, invalid currency, invalid money, invalid delivery windows, and missing carrier/service evidence.
- Added `ShippingSessionAuthorityCertificationTests` to freeze the runtime boundary.

Authority answers:
- Shipping Session: backend checkout-session SQL authority.
- Shipping Quote: backend pricing/quote API evidence, accepted only as presentation until final checkout revalidation.
- Shipping Quote Token: no accepted frontend-owned token exists in the inspected runtime; carrier quote IDs are presentation evidence only.
- Checkout Shipping Token: backend-generated checkout/payment token after accepted order creation where applicable.
- Shipping Method Binding: backend final checkout revalidates tenant-owned SQL shipping method and snapshots accepted evidence.
- Address Binding: backend checkout/session admission binds address fields to the current tenant/cart/customer or session.
- Customer Binding: backend authenticated customer claims or guest session owner, never `X-Customer-Id` from an untrusted caller.
- Cart Binding: backend cart owner and live cart revalidation.
- Session Expiration: backend checkout-session status/recovery-token validation and abandonment/invalidated timestamps.
- Quote Expiration: no durable frontend quote authority; pricing/quote evidence must refresh and final checkout recomputes.
- Tenant Scope: trusted backend tenant context and authenticated tenant/customer checks.
- SQL Truth: PostgreSQL checkout sessions, orders, shipping method rows, payment/session/token rows, and final checkout snapshots.

Pipeline:
- Address Candidate begins in Storefront form state.
- Shipping Method Candidate begins in Storefront selected method ID.
- Backend Shipping Admission begins at checkout-session update, pricing preview, and final checkout request handling.
- Shipping Session begins when backend `CheckoutSessionService.CreateOrResumeSessionWithOutcomeAsync` creates or resumes a tenant/cart/customer-bound SQL checkout session.
- Authority begins when backend validates tenant, owner, cart, customer/session, active canonical session, live cart, address, selected method, and final pricing before persistence or accepted DTO emission.
- Shipping Quote begins at backend quote/pricing response emission and remains presentation evidence until final checkout revalidation.
- Session Expiration begins at backend recovery-token/session status validation, abandonment, invalidation, and converted-session handling.
- SQL persistence begins in `CheckoutSessions` for session state and in final SQL order snapshot on accepted checkout.
- Presentation begins only after backend DTO emission and frontend DTO admission.

Cache/replay/multi-tab/recovery findings:
- React, React Query, Zustand, LocalStorage, SessionStorage, browser cache/BFCache, Redis, and Mongo projections cannot become Shipping Session Authority.
- LocalStorage persists only selected candidate IDs/step state, not accepted quotes, pricing, checkout tokens, or order results.
- SessionStorage may restore address presentation candidates only and is cleared after accepted checkout.
- Multiple tabs can submit/update candidates, but backend cart-scoped locks, active-session uniqueness, canonical session resolution, and final checkout revalidation decide authority.
- Replay, duplicate selection, duplicate quote fetch, duplicate refresh, and retry cannot fabricate a session; checkout idempotency and SQL/session resolution collapse or reject duplicates.
- Recovery revalidates the signed token, current tenant, non-converted session, recovery nonce, live cart, and requires review on cart/manual-wallet changes. Stale sessions are rejected, invalidated, or forced through current backend re-entry.

Boundary findings:
- Checkout consumes selected shipping/address as candidates and revalidates current backend authority.
- Orders persist only final accepted backend shipping snapshots.
- Payment, Coupons, Promotions, Smart Offers, Affiliate, CommerceHub, Marketplace, and Inventory are adjacent evidence or final checkout participants only; none can overwrite Shipping Session Authority.
- Redis and Mongo are derivative/cache/projection inputs and must re-enter SQL-backed shipping/checkout authority before accepted presentation or persistence.

Production certification:
- Frontend cannot fabricate an accepted Shipping Session, Shipping Quote, Shipping Token, or Selected Shipping authority.
- Replay cannot fabricate a Shipping Session.
- Duplicate tabs cannot overwrite backend Shipping Session Authority.
- React, React Query, Zustand, LocalStorage, Redis, and Mongo projections cannot become authority.
- Stale Shipping Sessions cannot survive recovery as authority; they must validate, re-enter current backend authority, require review, or fail closed.

Runtime changes were required and applied only to the shipping quote presentation boundary. Shipping Session is production-certifiable.
