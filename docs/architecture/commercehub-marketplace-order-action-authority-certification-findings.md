# PASS M2-G Marketplace Order Action Authority Certification Findings

Marketplace Order Action Authority is production-certifiable for the inspected frontend runtime paths after runtime hardening.

## Findings

- Action findings: order actions are derivative only. Customer retry/refund controls and vendor shipment cancellation now require backend-derived action evidence and fail closed on malformed, stale, contradictory, or foreign target evidence.
- Customer findings: customer retry payment and refund request controls cannot be granted by route state, storage, React state, lifecycle inference, or isolated booleans; action booleans are validated against current backend lifecycle/payment/item evidence.
- Vendor findings: POS/Admin shipment cancellation no longer derives from frontend shipment status config. It requires current `/orders/{orderId}/fulfillment/actions` backend authority for the exact shipment order target before presentation or execution.
- Admin findings: Super Admin order monitoring remains read-only for inspected order paths. Visibility/search/detail rows do not create override, mark-paid, refund, return, settlement, or governance action authority.
- Split findings: split action presentation remains bound to backend parent/child order, lifecycle, payment, item, and vendor evidence; frontend grouping cannot fabricate child actions.
- Payment/refund findings: payment retry and refund entry are backend-owned action evidence. Payment/refund/return/settlement state cannot independently create actions.
- Lifecycle findings: lifecycle values are validation evidence only; they do not create action eligibility. Actions that contradict lifecycle/payment/item evidence fail closed.
- Bootstrap findings: initial load, focus, visibility, reconnect, and refresh re-enter backend authority and withhold stale action presentation during current refresh/failure paths.
- Cache findings: query caches, selected rows, route state, and mutation responses cannot restore action authority. Vendor action authority is fetched from current backend fulfillment actions for the selected order.
- Failure findings: 401/403/protected endpoint loss, malformed DTOs, missing action evidence, target mismatches, and failed refreshes fail closed by withholding action controls or relying on backend execution rejection.
- Adversarial findings: local/session storage, React state, contexts, caches, mutation responses, URL/search params, route/history state, cross-tab events, lifecycle inference, visibility inference, payment/refund/return state, shipment status config, and stale selected rows cannot fabricate, preserve, replay, widen, transfer, or execute order actions.

## Runtime hardening performed

- Added customer `orderActionAuthority` for retry-payment and refund action evidence validation.
- Routed customer order identity/recovery boundaries through action validation before presentation.
- Added POS/Admin `shipmentActionAuthority` and `useShipmentActionAuthority` for backend fulfillment action evidence.
- Routed shipment cancellation presentation/execution through current backend action authority and exact order-target matching.

## Exact runtime files modified

- `PointOfSale.CommerceHub/src/features/customer/orderActionAuthority.ts`
- `PointOfSale.CommerceHub/src/features/customer/orderIdentityAuthority.ts`
- `PointOfSale.Web/src/modules/shipping/shipmentActionAuthority.js`
- `PointOfSale.Web/src/modules/shipping/hooks/useShipmentsApi.js`
- `PointOfSale.Web/src/modules/shipping/components/ShipmentActions.jsx`
- `PointOfSale.Web/src/modules/shipping/pages/ShipmentDetailsPage.jsx`

## Regression freezes added

- `PointOfSale.Tests/Architecture/MarketplaceOrderActionAuthorityCertificationTests.cs`
- `PointOfSale.Web/src/modules/shipping/shipmentActionAuthority.test.ts`

## Architecture updates

- Updated `.ai-context/PROJECT_CONTEXT.md`, `.ai-context/ARCHITECTURE_TRUTH.md`, `.ai-context/KNOWN_RISKS.md`, `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`, and `.ai-context/CODEX_RULES.md` with PASS M2-G action authority truth, hardening, risks, and freeze rules.
