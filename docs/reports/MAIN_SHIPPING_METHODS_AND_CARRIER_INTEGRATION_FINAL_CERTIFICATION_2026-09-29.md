# MAIN Shipping Methods And Carrier Integration Final Certification - 2026-09-29

## 1. Verdict

**Shipping-method authority, manual-method Storefront behavior, carrier-administration localization, accessibility, responsive presentation, focused regression tests, and read-only runtime inspection: PASS.**

**Overall production certification: CONDITIONAL PASS.**

The current tenant has no configured external carrier account, so Bosta, Aramex, Mylerz, DHL, and FedEx execution could not be certified against live provider credentials without introducing state or external side effects. The API readiness endpoint is also unhealthy because of payout reconciliation state outside the shipping scope. A broader 330-test shipping, carrier, shipment, and architecture selection retained pre-existing path-sensitive and stale source-certification failures and is not a green release gate.

The current tenant's manual shipping path is operational. Three active manual shipping methods assigned to Main Warehouse were presented by the Storefront checkout from backend DTOs with backend-authoritative prices.

## 2. Certified Scope

This certification covered:

- POS Admin carrier readiness and shipping-method administration.
- Storefront shipping address and shipping-method selection.
- English and Arabic localization.
- Desktop and mobile responsive behavior.
- Keyboard and assistive-technology semantics.
- Backend ownership of method identity, price, availability, warehouse scope, and shipment configuration.
- Credential handling, verification state, shipment registration, retry safety, and webhook trust boundaries by source review and focused tests.
- Read-only runtime evidence from authenticated Admin APIs and public health endpoints.

This certification deliberately excluded live credential verification, label creation, pickup booking, shipment cancellation, order placement, payment, and fulfillment mutation.

## 3. Supported Carrier Boundary

The supported carrier catalog remains exactly:

| Code | Carrier model |
|---|---|
| `manual` | Backend-owned manual shipping workflow |
| `bosta` | External carrier integration |
| `aramex` | External carrier integration |
| `mylerz` | External carrier integration |
| `dhl` | External carrier integration |
| `fedex` | External carrier integration |

Authenticated runtime inspection returned all six supported definitions. It also returned:

- Carrier accounts: `[]`
- Carrier audit records: `[]`
- Configured external carrier accounts: `0`
- Active manual shipping methods visible at checkout: `3`

No carrier account, secret, verification state, or audit record was created or modified during certification.

## 4. Authority Model

Source review and focused regression coverage established the following boundaries:

- PostgreSQL remains the write and runtime authority for tenant shipping methods and carrier accounts.
- Storefront projections and frontend state are presentation candidates, not shipment or pricing authority.
- Storefront checkout consumes backend shipping-method DTOs and does not calculate shipping prices or hardcode carrier choices.
- Method identity and warehouse assignment are backend-owned and revalidated by checkout and fulfillment paths.
- Order and shipment records retain the backend-resolved shipping lineage needed to prevent later frontend substitution.
- Shipment creation and external registration are deferred to backend orchestration rather than provider calls inside the checkout transaction.
- Carrier request identity, registration state, retry behavior, and reconciliation markers are persisted backend evidence.
- Credential values remain secret-bearing backend data; the Admin UI consumes readiness, fingerprint, and diagnostic evidence rather than recovering secrets.
- Webhook processing remains provider-authenticated and replay/idempotency protected.

The remediation did not add frontend rate calculation, provider execution, shipment mutation authority, or fallback credentials.

## 5. POS Admin Remediation

The carrier-administration workspace now provides:

- Complete English and Arabic strings for summaries, tabs, carrier cards, readiness, diagnostics, audit history, lifecycle actions, and shipping methods.
- Localized dates and state labels.
- Logical RTL spacing and alignment instead of left/right-specific presentation.
- Responsive summary, card, method, and dialog layouts.
- Tablist, tab, tabpanel, dialog, label, and status announcement semantics.
- An explicitly read-only `Refresh readiness` action instead of wording that implied a provider mutation.
- Stable `recommendedNextActionKey` evidence so localized presentation does not depend on matching English prose.

At mobile width, the shipping administration remained operable through its responsive cards and horizontally available tab strip. No carrier lifecycle action was submitted.

## 6. Storefront Remediation

The checkout shipping step now provides:

- Localized address labels, placeholders, validation messages, help text, loading states, empty states, retry actions, prices, delivery estimates, and continuation controls.
- Native `fieldset` and `legend` grouping for shipping-method choices.
- Status and alert semantics for asynchronous and validation feedback.
- Localized stable system checkout fields selected by `systemFieldKey`.
- Localized file-size units while preserving the existing attachment workflow.
- Correct stale-selection fallback without introducing frontend shipping authority.
- Optional backend-provided `nameAr` presentation in Arabic, with the backend `name` retained as the fallback.

`nameAr` is display evidence only. Method IDs, prices, availability, and admission remain backend controlled.

## 7. Automated Verification

| Gate | Result |
|---|---|
| Focused backend carrier and affected architecture tests | PASS: 14 passed, 0 failed, 0 skipped |
| Final localized-name architecture assertion | PASS: 1 passed, 0 failed, 0 skipped |
| POS Admin shipping tests | PASS: 16 passed across 2 files |
| Storefront shipping tests | PASS: 26 passed across 3 files |
| POS Admin production build | PASS |
| Storefront production build | PASS |
| Admin English/Arabic locale JSON parsing | PASS |
| Storefront English/Arabic locale JSON parsing | PASS |
| Scoped `git diff --check` | PASS; line-ending notices only |
| ESLint | NOT AVAILABLE: neither frontend has an ESLint 9 flat configuration |

Backend tests used isolated output paths because the active API held normal development binaries. Generated test artifacts were removed after verification.

### Broader Backend Selection

The latest complete selected run reported:

```text
Failed: 35, Passed: 295, Skipped: 0, Total: 330
```

One of those failures was the shipping UI assertion updated in this certification. Its focused rerun passed. The complete 330-test selection was not rerun after that final one-line assertion update, so the retained broader gate remains formally red rather than being reported as an inferred 34-failure result.

The remaining observed failures include:

- Architecture tests that resolve repository files relative to `PointOfSale.Tests` and therefore look for paths such as `PointOfSale.Tests/PointOfSale.API/...` under isolated execution.
- Frozen certification-document assertions whose shared source resolver selected a different report from the extensively dirty worktree.
- Stale source-fragment assertions for shipping sessions, method ownership, tenant scope, shipment actions, cache admission, and Admin presentation.

These failures were not silently waived or broadly rewritten. They require a separate architecture-test maintenance pass before the broader suite can be used as a green release gate.

## 8. Authenticated Browser Evidence

Browser verification used the isolated `playwright-cli` session `opencode-ui-test` against:

- Admin: `http://localhost:5173/`
- Storefront: `http://ahramstore.localhost:5174/`
- API: `http://localhost:5010/`

Admin observations:

- The authenticated shipping workspace loaded all six supported carrier definitions.
- Carrier account and carrier audit responses were empty.
- English and Arabic interfaces rendered the remediated labels and RTL behavior.
- Desktop and mobile layouts remained usable.
- Tab semantics and localized readiness presentation were exposed.

Storefront observations:

- The AhramStore tenant resolved correctly.
- Checkout presented three active backend shipping methods assigned to Main Warehouse.
- Method selection used backend method identities and displayed backend prices.
- English and Arabic checkout states rendered, including localized system address labels.
- Mobile checkout retained the shipping choices and form controls without losing the accessible group structure.

An in-stock iPhone was added to the authenticated cart only to reach checkout. No checkout submission, order, payment, shipment, label, pickup, or carrier request occurred.

## 9. Runtime Health

Both `GET /health` and `GET /health/ready` returned HTTP `503` with `Unhealthy` status.

Readiness blocker:

- `payout_external_truth_readiness`: unhealthy because the latest reconciliation status is `Critical` with one mismatch.

Additional full-health blocker observed during the request:

- `redis_latency`: unhealthy at 292 ms while Redis remained connected and its separate reachability check was healthy.

Shipping-relevant healthy evidence included:

- MassTransit bus and registered endpoints.
- `shipping-method-projection` endpoint.
- `shipment-external-registration-requested` endpoint.
- `order-shipment-orchestration` endpoint.
- CommerceHub external-outcome recovery with zero pending carrier-registration warnings or critical items.
- PostgreSQL, Redis reachability, MongoDB, Elasticsearch, RabbitMQ, and R2 connectivity.
- Integration, projection, provider-delivery, MassTransit, and accounting outbox readiness.

The full health response also reported degraded projection isolation for 58 preserved foreign-tenant MongoDB documents and disabled certificate-provider/Caddy-edge integrations. No health-state mutation was attempted within this shipping task.

## 10. External Carrier Limitation

External-carrier runtime execution is **NOT CERTIFIED** for this tenant because there are no configured external carrier accounts. This means the browser and authenticated API evidence cannot establish live:

- Credential acceptance by Bosta, Aramex, Mylerz, DHL, or FedEx.
- Provider rate quotation.
- Label or airway-bill creation.
- Pickup booking.
- Provider-side cancellation.
- Signed webhook delivery from a real provider.
- Provider reconciliation after an ambiguous external outcome.

The code-level containment and focused regression tests pass, but external production readiness requires a separately authorized test tenant, non-production provider credentials, explicit permission for provider calls, and a controlled cleanup/reconciliation plan.

## 11. Principal Files

POS Admin:

- `PointOfSale.Web/src/modules/shipping-administration/ShippingAdministrationWorkspace.jsx`
- `PointOfSale.Web/src/modules/shipping-administration/components/CarrierAccountCard.jsx`
- `PointOfSale.Web/src/modules/shipping-administration/components/ShippingMethodsTab.jsx`
- `PointOfSale.Web/src/modules/shipping-administration/shippingAdministrationApi.js`
- `PointOfSale.Web/src/modules/shipping-administration/ShippingAdministrationWorkspace.test.tsx`
- `PointOfSale.Web/src/i18n/locales/en.json`
- `PointOfSale.Web/src/i18n/locales/ar.json`

Storefront:

- `PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx`
- `PointOfSale.Storefront/src/components/checkout/ShippingStep.localization.test.ts`
- `PointOfSale.Storefront/src/store/checkoutStore.ts`
- `PointOfSale.Storefront/src/types/checkout.ts`
- `PointOfSale.Storefront/src/i18n/locales/en.json`
- `PointOfSale.Storefront/src/i18n/locales/ar.json`

Backend certification assertions:

- `PointOfSale.Tests/Architecture/POSAdminCheckout3FrontendCertificationTests.cs`
- `PointOfSale.Tests/Architecture/ShippingMethodFinalAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/StorefrontShippingMethodAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Shipping/CarrierIntegrationAuthorityRemediationTests.cs`

Workspace guidance:

- `AGENTS.md`

## 12. Security And Mutation Statement

- No credential, cookie, token, carrier secret, or authentication value was written to this report.
- No carrier credential was saved, reset, validated, enabled, disabled, or deleted.
- No label, pickup, shipment cancellation, provider registration, order, payment, or financial operation was executed.
- Authenticated carrier and audit inspection was read-only.
- Browser state changed only by adding a product to the cart to expose checkout shipping behavior.
- Unrelated modified and untracked worktree content was preserved.

## 13. Conclusion

The manual shipping-method path and the shipping administration/checkout presentation are remediated and pass focused certification. Backend authority remains intact, localization and accessibility are materially improved, and no external or financial side effect was performed.

The result is a conditional pass rather than an unconditional external-carrier production sign-off. Closure requires a green broader architecture gate, healthy environment readiness, and separately authorized live-provider certification using configured non-production carrier accounts.
