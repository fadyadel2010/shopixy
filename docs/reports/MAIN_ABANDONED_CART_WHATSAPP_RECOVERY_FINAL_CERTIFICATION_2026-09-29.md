# MAIN Abandoned Cart WhatsApp Recovery Final Certification - 2026-09-29

## 1. Verdict

**Backend recovery authority, tenant isolation, token integrity, current-cart restoration, order attribution, retry behavior, Admin readiness, WhatsApp recovery-authorization control, bilingual responsive presentation, fail-closed dispatch controls, and focused regression tests: PASS.**

**Overall production certification: CONDITIONAL PASS.**

**Live external recovery-message dispatch: NOT CERTIFIED and MUST REMAIN DISABLED.**

The application is safe in its current configuration because external workflow dispatch is explicitly disabled, the reviewed workflow contract must be opted into, the tenant has not authorized its WhatsApp account for abandoned-cart recovery, and the checked-in n8n workflow is inactive. Two conditions prevent unconditional production sign-off:

1. Migration `20260929163000_MAIN_AbandonedCartCanceledStatusAuthority` has not been applied to the shared database. Runtime code that writes the new `Canceled` terminal status must not be deployed before that migration.
2. The checked-in n8n workflow is not suitable for live dispatch. It requires a separate remediation and controlled end-to-end certification before `AbandonedCart:ExternalWorkflowDispatchEnabled` may be set to `true`.

No recovery message was sent, no recovery setting was changed, no cart or order was created or modified, and no migration was applied during this certification.

## 2. Certified Scope

This certification covered:

- Idle-cart discovery and atomic claim-before-publish behavior.
- WhatsApp-only contact eligibility.
- Tenant automation, WhatsApp account, use-case authorization, connection, workflow-gate, contract-version, URL, and tenant-secret readiness.
- Dispatch-time revalidation after a recovery event has been queued.
- Recovery workflow request signing and deterministic idempotency identity.
- Tenant-bound, owner-bound, expiring recovery links.
- Backend-authoritative live-cart restoration without trusting stored prices or discounts.
- Ecommerce-order recovery closure and recovered-order attribution.
- Recovery callback authentication, tenant scope, idempotency, and telemetry.
- Admin configuration, readiness, localization, accessibility, and responsive behavior.
- Admin WhatsApp-account recovery-authorization control, including confirmation and fail-closed availability.
- Storefront recovery-link localization, directionality, responsive behavior, and duplicate-request prevention.
- Entity Framework model/migration alignment.
- Read-only authenticated runtime inspection.

This certification deliberately excluded live n8n activation, Baileys recovery dispatch, customer-message delivery, recovery-setting mutation, cart mutation, order placement, and shared-database migration.

## 3. Runtime State

Read-only inspection established the following state for AhramStore:

| Control | State |
|---|---|
| API liveness | `200 Healthy` at `GET /health/live` |
| Recovery automation | Disabled |
| WhatsApp account | Enabled and connected |
| Recovery use-case authorization | `useForAbandonedCartRecovery=false` |
| External workflow dispatch | `false` in checked-in application configuration |
| Workflow contract version | `2026-09-29` |
| Checked-in n8n workflow | `active: false` |
| Recovery migration | Generated and validated, not applied |

Shared AhramStore state was re-read after the UI verification below and remained `useForAbandonedCartRecovery=false`, `enabled=true`, `status=Connected`. No recovery control was left changed.

`GET /health/ready` has a payout-reconciliation failure outside this scope. It was not treated as the recovery liveness gate.

## 4. Authority And Dispatch Boundary

The certified application path is:

```text
Hangfire scanner
  -> atomic Active-to-Abandoned claim
  -> TriggerRecoveryEvent
  -> current tenant/cart/account/workflow revalidation
  -> atomic Abandoned-to-Pending claim
  -> signed, idempotent n8n webhook request
  -> Pending-to-Triggered or retry/failure transition
```

Controls verified in source and tests:

- Only tenants with recovery enabled are scanned.
- Scanner candidates must have a verified phone number; email fallback was removed.
- `TryMarkAsAbandonedAsync` verifies both `Active` state and the expected last-activity timestamp before publication.
- Queued events re-enter active-tenant, current-cart, current-account, use-case authorization, connection, workflow-gate, contract-version, URL, secret, opt-out, and rate-limit authority before dispatch.
- Legacy email-channel recovery events are rejected by the consumer.
- The consumer transitions to `Pending` atomically, preventing duplicate concurrent orchestration.
- Failed webhook admission returns the cart to `Abandoned` until the retry ceiling, then marks it `Failed`.
- Stale `Pending` records are reset for retry.
- The webhook carries an HMAC-SHA256 signature, timestamp, nonce, tenant/cart lineage headers, and deterministic idempotency key.
- The checked-in default remains fail-closed with `ExternalWorkflowDispatchEnabled=false` and an empty webhook URL.

The final review found and closed a time-of-check/time-of-use gap: eligibility had previously been checked only before event publication. It is now checked again immediately before the consumer can claim `Pending` or invoke the external orchestrator. Disabling automation or account authority while an event is queued therefore prevents dispatch.

## 5. Cart And Token Security

Recovery tokens are protected with ASP.NET Core Data Protection and bind:

- Tenant ID.
- Cart ID.
- Session and/or customer ownership.
- Recovery purpose.
- Seven-day expiry.

The Storefront additionally requires the token tenant to match the resolved storefront tenant. Cross-tenant requests return non-disclosing `404 Cart not found` responses.

Token text is now required to be canonical Base64URL. The broader regression run exposed that changing a trailing Base64URL character could sometimes decode to identical protected bytes when non-canonical trailing bits were accepted. Canonical re-encoding is now compared before unprotecting, so any such textual mutation is rejected.

Recovery does not trust `CartSnapshotJson` as commerce authority. The endpoint reloads the live cart through the backend cart service, verifies tenant, cart, owner, expiry, item identities, quantities, prices, totals, and currency, and refuses restoration when a current backend-admitted cart cannot be produced.

Terminal `Recovered`, `Expired`, `Failed`, and `Canceled` carts cannot be restored.

## 6. Order Attribution

Storefront checkout now propagates the originating `CartId` on `OrderCreatedEvent`.

`OrderRecoveryConsumer` closes recovery only when:

- Tenant identity is present and active.
- Cart identity is present.
- Order source is `Ecommerce`.

Closure semantics are:

| Prior state | Result | Recovery attribution |
|---|---|---|
| `Triggered` | `Recovered` | Recovery timestamp and order ID retained |
| `Active`, `Abandoned`, or `Pending` | `Canceled` | Order ID retained; not counted as recovered |
| Existing terminal state | No change | Idempotent |

Cart activity no longer erases `Triggered`, preserving attribution if the customer returns and later orders. Asynchronous order-recovery persistence failures are rethrown so MassTransit retry policy can recover rather than silently lose closure.

## 7. Telemetry

Recovery statistics now use explicit tenant predicates with `IgnoreQueryFilters()` where background or explicitly scoped execution requires it.

Recovered metrics count only rows where all canonical evidence agrees:

- Status is `Recovered`.
- `RecoveredAtUtc` is present.
- `RecoveredOrderId` is present.

Workflow callbacks require either a trusted tenant-scoped Admin principal or the tenant callback API key. Anonymous callbacks fail closed when the key is absent or invalid. Callback writes are tenant/cart bound and idempotent under concurrent insertion.

## 8. Admin And Storefront UX

The Admin recovery workspace now distinguishes configuration from operational readiness. It presents separate automation, account, recovery-authorization, connection, workflow, and overall states, and blocks enabling recovery when prerequisites are absent.

Headed-browser verification covered English and Arabic at desktop and mobile widths:

- Settings, eligibility, and statistics requests returned `200`.
- The first actionable readiness blocker was displayed.
- Shared-state controls remained disabled.
- RTL/LTR direction and responsive layout were correct.
- No horizontal overflow was observed.
- No console errors were observed beyond existing React Router warnings.

The Storefront recovery page now uses the Storefront i18n instance, provides localized invalid-link and retry states, remains within the mobile viewport, and prevents duplicate recovery GETs under React Strict Mode.

At `http://ahramstore.localhost:5174/recover-cart/not-a-valid-token`, English and Arabic invalid-token states rendered correctly, exactly one recovery GET was made per load, and the API returned the expected `400`. Guest authentication probes returned expected `401` responses.

## 8.1 Recovery Authorization Control

The Admin readiness blocker directs the operator to WhatsApp settings, but the WhatsApp account card previously exposed no control for `UseForAbandonedCartRecovery`. The backend `PUT /admin/whatsapp/accounts/{id}` contract and DTO already supported the field, so the dead end was a presentation gap rather than a missing capability.

The account card now presents recovery authority explicitly:

- A `Abandoned Cart Recovery` capability badge that reflects current authority.
- A labelled `Abandoned cart recovery authority` group describing the fail-closed rule, stating the current authority, and stating that the account must additionally be enabled and connected.
- A single authorize/revoke control carrying `aria-pressed` to expose current authority, and `title` explaining why it is unavailable.
- Both directions require an explicit confirmation dialog before any request is issued.
- The control is disabled while the account is not connected, so recovery authority cannot be granted to a session that cannot deliver.
- The add-account form also exposes the same capability, defaulting to unauthorized.

Verification:

| Check | Result |
|---|---|
| Frontend recovery-authorization tests | PASS: 4 passed, 0 failed |
| POS Admin production build | PASS |
| Admin accounts tab, Arabic | Group, badge, unauthorized state, and enabled control rendered |
| Admin accounts tab, English | Same control rendered with English copy |
| Confirmation dialog on authorize | Correct English text; dismissed, so no request was issued |
| Network after dismissal | No `PUT /admin/whatsapp/accounts/{id}`; only `GET` |
| Shared AhramStore account after verification | `useForAbandonedCartRecovery=false` unchanged |

The `i18nCertification` gate still fails on unrelated pre-existing `storefront.homepageEditor.*` English gaps. No `whatsapp.*` key is reported missing, and the new Arabic and English keys are structurally paired.

## 8.2 Readiness Precedence Defect

Live use exposed a presentation defect on the recovery workspace. After recovery authority was granted, the page still reported the external-workflow blocker while the backend's authoritative code was `recovery_disabled`.

Cause: the page re-derived its own blocker precedence and never evaluated the saved automation state, so it skipped the backend's fail-fast `recovery_disabled` gate and fell through to `workflow_not_configured`. The single offered action was a link to WhatsApp settings, which the operator had already completed, so the page dead-ended them away from the only control that could unblock them. The save-rejection toast used the backend code, so the toast and the panel could disagree.

The page now treats the backend `ineligibilityCode` as authoritative when it is a known code, falls back to a derived chain that includes `recovery_disabled`, and routes the operator to the control that actually unblocks them: the WhatsApp settings link is shown only for account-class blockers, and the `recovery_disabled` case links to the local recovery toggle. The save-rejection toast and the panel now resolve from the same code.

| Check | Result |
|---|---|
| Readiness precedence tests | PASS: 4 passed, 0 failed |
| Live page, AhramStore, automation disabled | Reports "Recovery is saved as disabled. No recovery workflow can start." |
| Misleading workflow message under the same state | Absent |
| WhatsApp settings link under the same state | Not offered |
| New action link | Present, targets `#enable-recovery`, resolves to the existing toggle |
| POS Admin production build | PASS |

## 9. Automated Verification

| Gate | Result |
|---|---|
| Final recovery/WhatsApp/phone-verification/backend architecture slice | PASS: 61 passed, 0 failed, 0 skipped |
| Focused abandoned-cart remediation slice before broader run | PASS: 24 passed, 0 failed, 0 skipped |
| Full solution build | PASS: 0 errors; existing warnings retained |
| Isolated test-project build | PASS: 0 errors |
| POS Admin production build | PASS |
| Storefront production build | PASS |
| EF migration list | PASS: migration discovered |
| EF pending-model check | PASS: no model changes after the migration |
| Generated migration SQL review | PASS: only the abandoned-cart recovery-status constraint changes |
| WhatsApp recovery-authorization frontend tests | PASS: 4 passed, 0 failed |
| Recovery readiness precedence tests | PASS: 4 passed, 0 failed |
| POS Admin production build after control change | PASS |
| `i18nCertification` gate | FAIL on pre-existing unrelated `storefront.homepageEditor.*` English gaps; no `whatsapp.*` gap |
| `git diff --check` | PASS; line-ending notices only |

Tests were built into an isolated workspace artifact path because the running API held normal development binaries. The temporary artifacts were removed after verification.

## 10. Migration Evidence

Migration:

```text
20260929163000_MAIN_AbandonedCartCanceledStatusAuthority
```

The migration replaces `CK_AbandonedCarts_RecoveryStatus` so it admits:

```text
Active, Abandoned, Pending, Triggered, Recovered, Expired, Failed, Canceled
```

Its `Down` path restores the prior seven-status constraint. The EF snapshot and runtime model agree with the migration, and `dotnet ef migrations has-pending-model-changes` reported no pending model changes.

The migration was intentionally not applied to the shared database. The backend revision and migration must be deployed in the correct order; application code that writes `Canceled` is incompatible with the old check constraint.

## 11. External Workflow Blocker

The checked-in `deploy/n8n-workflows/abandoned-cart-recovery-workflow.json` is inactive and must not be activated as the reviewed `2026-09-29` production contract.

The remaining blockers are concrete:

1. The backend scanner already waits until the first-message abandonment threshold, but the workflow waits `firstMessageDelayMinutes` again.
2. Working hours use the n8n host's local clock rather than an explicit, reviewed tenant timezone contract.
3. Delayed first and second sends do not re-enter current SQL cart, order, account, consent, opt-out, or recovery authorization before delivery.
4. The two message nodes target inconsistent WhatsApp routes: `/messages/send` and `/api/whatsapp/send`.
5. Delivery bypasses the application's central WhatsApp dispatch authority and its audit/idempotency controls.
6. The workflow does not validate `workflowContractVersion`, `firstMessageDueAtUtc`, or `timeZoneId`.
7. The recovery-result callback does not provide `X-Recovery-Api-Key`, so the now-authenticated callback is expected to reject it.
8. Message content and delivery outcomes were not exercised against an isolated authorized customer/device.

Application gating contains these risks in the present state, but configuration flags are not a substitute for repairing and independently certifying the external workflow.

## 12. Conditions For Live Enablement

All of the following are required before live recovery dispatch can be certified:

1. Apply `20260929163000_MAIN_AbandonedCartCanceledStatusAuthority` through the normal deployment process before deploying code that writes `Canceled`.
2. Replace or remediate the n8n workflow so scheduling begins from `firstMessageDueAtUtc`, uses explicit timezone semantics, and revalidates current backend authority before every delayed send.
3. Use one reviewed WhatsApp transport contract with authenticated requests and backend-owned delivery auditing.
4. Add authenticated recovery-result callbacks and durable replay/idempotency storage appropriate for the deployed n8n topology.
5. Validate the exact `2026-09-29` contract at the workflow boundary.
6. Use an isolated tenant, test recipient, non-production workflow, explicit recovery account authorization, and explicit approval for message side effects.
7. Certify first-message, second-message, opt-out, order-before-send, order-between-sends, retry, timeout, ambiguous delivery, duplicate webhook, and callback scenarios.
8. Only after those gates pass, enable the tenant setting, recovery account use case, n8n workflow, and application dispatch flag in a controlled rollout.

## 13. Principal Files

Backend authority:

- `PointOfSale.Infrastructure/Jobs/AbandonedCartScannerJob.cs`
- `PointOfSale.Infrastructure/Consumers/AbandonedCart/RecoveryOrchestrationConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/AbandonedCart/OrderRecoveryConsumer.cs`
- `PointOfSale.Infrastructure/Services/AbandonedCartService.cs`
- `PointOfSale.Infrastructure/Services/AbandonedCartEligibilityService.cs`
- `PointOfSale.Infrastructure/Services/N8nRecoveryOrchestrator.cs`
- `PointOfSale.Infrastructure/Services/AbandonedCartRecoveryTelemetryService.cs`
- `PointOfSale.API/Controllers/Admin/AdminAbandonedCartRecoveryController.cs`
- `PointOfSale.API/Controllers/StorefrontCartRecoveryController.cs`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/appsettings.json`

Schema:

- `PointOfSale.Core/Enums/AbandonedCartRecoveryStatus.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- `PointOfSale.Infrastructure/Migrations/20260929163000_MAIN_AbandonedCartCanceledStatusAuthority.cs`

Presentation:

- `PointOfSale.Web/src/pages/AbandonedCartRecovery.jsx`
- `PointOfSale.Web/src/pages/AbandonedCartRecovery.test.tsx`
- `PointOfSale.Web/src/modules/whatsapp/pages/WhatsAppSettings.jsx`
- `PointOfSale.Web/src/modules/whatsapp/hooks/useWhatsAppApi.js`
- `PointOfSale.Web/src/modules/whatsapp/pages/WhatsAppSettings.recoveryAuthority.test.tsx`
- `PointOfSale.Web/src/i18n/locales/en.json`
- `PointOfSale.Web/src/i18n/locales/ar.json`
- `PointOfSale.Storefront/src/pages/RecoverCartPage.tsx`
- `PointOfSale.Storefront/src/i18n/locales/en.json`
- `PointOfSale.Storefront/src/i18n/locales/ar.json`

Verification and external workflow:

- `PointOfSale.Tests/AbandonedCart/AbandonedCartRecoveryCompletenessTests.cs`
- `PointOfSale.Tests/AbandonedCart/AbandonedCartRecoverySecurityTests.cs`
- `PointOfSale.Tests/Admin/AbandonedCartRecoveryTelemetryTests.cs`
- `PointOfSale.Tests/Architecture/StorefrontWhatsAppAuthorityCertificationTests.cs`
- `deploy/n8n-workflows/abandoned-cart-recovery-workflow.json`

## 14. Final Statement

The MAIN abandoned-cart recovery application path is certified as fail-closed and safe to merge with dispatch disabled. Tenant isolation, current authority, token integrity, live-cart restoration, order attribution, retry behavior, telemetry, Admin presentation, WhatsApp recovery-authorization control, and Storefront presentation have passing evidence.

This report does not authorize live recovery messages. Production dispatch remains conditional on the unapplied migration and a separately remediated, isolated, side-effect-approved n8n/WhatsApp certification.
