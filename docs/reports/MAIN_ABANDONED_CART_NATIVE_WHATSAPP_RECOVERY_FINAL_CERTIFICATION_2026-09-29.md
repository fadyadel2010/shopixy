# MAIN Native Abandoned Cart WhatsApp Recovery Final Certification - 2026-09-29

Final runtime verification completed on 2026-09-30.

## 1. Verdict

**Native scheduling, durable recovery state, dispatch-time authority, tenant isolation, retry/idempotency behavior, canonical order attribution, recovery-token safety, Admin readiness, Storefront recovery UX, and focused regression coverage: PASS.**

**Overall production certification: CONDITIONAL PASS.**

**Live external WhatsApp delivery: NOT CERTIFIED.**

The application-owned recovery path replaces active n8n orchestration. Recovery remains effectively disabled for AhramStore, and no real WhatsApp message, payment, shipment, cart mutation, order placement, or settings mutation was performed during certification.

One external-delivery condition remains:

1. A controlled delivery test with an explicitly authorized test recipient and transport is required before claiming live WhatsApp-delivery certification. There is no production no-op transport, so this certification used a mocked `IWhatsAppMessageDispatcher` against real PostgreSQL state.

## 2. Certified Native Path

The certified path is:

```text
Hangfire abandoned-cart scanner
  -> atomic cart lifecycle claim
  -> identity-only TriggerRecoveryEvent
  -> MassTransit recovery consumer
  -> durable PostgreSQL message schedule
  -> Hangfire due-message dispatcher
  -> lease and current-state authority reconstruction
  -> central audited WhatsApp dispatcher
  -> sent, retry, suppressed, disabled, or terminal failure state
```

The discovery event carries only `TenantId`, `AbandonedCartId`, and `CartId`. Phone number, account, recovery URL, message content, eligibility, and other delivery authority are rebuilt from current trusted state at dispatch time.

## 3. Native Scheduling And Delivery Controls

Verified controls include:

- PostgreSQL-backed first- and second-message schedules.
- Atomic schedule creation and replay-safe idempotency.
- Per-message status, attempt count, due time, dispatch-log identity, lease identity, and lease expiry.
- Expired-lease reclamation.
- Bounded retries with terminal cart failure after exhaustion.
- Revalidation of cart lifecycle, tenant settings, account enabled/connected state, opt-out, rate limits, and current dispatch authority before every send.
- Suppression when the cart is no longer eligible.
- Second-message timing measured from the successful first-message send.
- Daytime and overnight sending windows.
- Tenant IANA time-zone handling, including deterministic daylight-saving gaps and overlaps.
- Central `IWhatsAppMessageDispatcher` enforcement rather than direct transport calls.
- Runtime health for PostgreSQL scheduling, Hangfire workers, and the audited dispatcher.

No separate `UseForAbandonedCartRecovery` authorization gate remains in active behavior. Recovery uses the tenant's single enabled and connected WhatsApp account, subject to recovery settings and dispatch-time controls.

## 4. Lifecycle And Attribution

Cart lifecycle transitions are atomic and fail closed. Discovery failures from either `Abandoned` or `Pending` are accounted for, preventing terminal schedule exhaustion from being rediscovered as new work.

Recovery completion is attributed only to a canonical order that:

- Belongs to the same tenant.
- Is not deleted.
- Has source `Ecommerce`.
- References the same cart.
- Was created after the recovery trigger time.

If those conditions are not met, completion closes as `Canceled` rather than inflating recovered-cart metrics. Existing terminal states remain idempotent.

## 5. Token And Storefront Safety

Recovery links remain tenant-, cart-, and owner-bound through ASP.NET Core Data Protection. Canonical Base64URL validation rejects textual token mutations that decode to the same byte sequence.

The recovery endpoint reloads the current backend cart and does not trust stored snapshot prices, discounts, totals, or currency as commerce authority. Invalid or cross-store tokens return safe non-disclosing errors.

Headed Storefront checks covered English and Arabic invalid-token states at desktop and mobile widths:

- The API returned `400` for the invalid token.
- Localized safe errors rendered correctly.
- No cart or cross-tenant data was disclosed.
- RTL presentation was correct.
- No horizontal mobile overflow was observed.

## 6. Admin Readiness And Account UX

The Admin recovery workspace reports:

- Persisted configuration state.
- WhatsApp account enabled and connected state.
- Recovery enabled state.
- Native worker health.
- Overall operational readiness.

Known backend `ineligibilityCode` values are authoritative. If a supported code is absent, the UI derives a fail-closed blocker from current account, runtime, and automation state. Account-class failures link to WhatsApp settings; `recovery_disabled` links to the local recovery control.

The obsolete recovery-authority UI and request field were removed. Focused tests verify that legacy API responses containing either value of `useForAbandonedCartRecovery` do not expose a separate grant/revoke control. Account enabled/disabled state remains the governing account administration control.

Headed Admin verification covered English and Arabic at desktop and 390 x 844 mobile dimensions:

- Settings, eligibility, and statistics APIs returned `200`.
- AhramStore recovery remained effectively disabled. No settings row is persisted, so fail-safe defaults apply with `enableAbandonedRecovery=false`.
- The connected account and native worker state rendered correctly.
- The heading remained accessible.
- No horizontal overflow was observed.

## 7. n8n Retirement

The following active orchestration artifacts were removed:

- `N8nRecoveryOrchestrator`.
- `IRecoveryOrchestrator`.
- n8n startup and configuration validation.
- External-workflow readiness fields and UI.
- Separate account recovery authorization behavior.
- Anonymous `POST /api/admin/ecommerce/abandoned-cart-recovery/recovery-log` callback.
- `AbandonedCartRecoveryLogRequest` callback DTO.
- `TenantAutomationSettings.N8nWebhookApiKey` runtime property.

Migration `20260930145500_MAIN_RetireN8nRecoveryCallback` drops the obsolete database column. Security tests assert that neither the legacy callback action nor credential property is present.

Historical migrations, archived reports, and the inactive checked-in workflow may retain n8n names as historical evidence; they are not active runtime paths.

## 8. Migration State

Applied to `PosEcommerceDb_Baseline`:

- `20260929115948_MainWhatsAppSingleAccountPerTenant`
- `20260929163000_MAIN_AbandonedCartCanceledStatusAuthority`
- `20260929215702_MAIN_NativeAbandonedCartRecoveryScheduler`
- `20260930145500_MAIN_RetireN8nRecoveryCallback`

The retirement migration was applied successfully after the pre-retirement API process was stopped. Its migration-history row and removal of the obsolete callback credential column were verified. A pre-change backup was restored into an isolated temporary database to verify legacy data. Both the backup and live compatibility column contained zero populated callback credentials, so retirement caused no credential loss. The temporary audit database was removed.

EF reports no model changes after the retirement migration snapshot update.

Database backups retained:

- `C:\Users\fadya\AppData\Local\Temp\opencode\PosEcommerceDb_Baseline_pre_abandoned_cart_20260930.backup`
- `C:\Users\fadya\AppData\Local\Temp\opencode\PosEcommerceDb_Baseline_pre_native_recovery_20260930.backup`
- `C:\Users\fadya\AppData\Local\Temp\opencode\PosEcommerceDb_Baseline_pre_retire_n8n_20260930_160404.backup`

## 9. Automated Evidence

| Gate | Result |
|---|---|
| Native scheduler PostgreSQL scenarios | PASS: replay, lease recovery, retry exhaustion, first-send progression, and terminal failure |
| Focused recovery and security backend slice after callback retirement | PASS: 66 passed, 0 failed |
| Focused Admin recovery and account UI suites | PASS: 7 passed, 0 failed |
| Storefront phone normalization tests | PASS: 3 passed, 0 failed |
| Full solution isolated build | PASS: 0 errors; existing analyzer warnings remain |
| POS Admin production build | PASS; existing large-chunk warning remains |
| Storefront production build | PASS; existing chunking and Browserslist warnings remain |
| EF pending-model check | PASS: no model changes after the latest migration |
| `git diff --check` | PASS; line-ending notices only |

An earlier mixed `ProductionSecurityHardeningTests` run used an isolated output path outside the repository, causing three source-file lookup failures, and also surfaced an unrelated existing allowlist failure for `UploadController.GetProductMediaContent`. The recovery and security scopes were rerun from a repository-compatible output with `POINT_OF_SALE_SOLUTION_ROOT` supplied, and all 66 relevant tests passed.

## 10. Runtime Evidence

Read-only runtime checks established:

| Check | Result |
|---|---|
| `GET /health/live` | `200` |
| `GET /health/ready` | `503` |
| Final API process | One `PointOfSale.API` process owns both `127.0.0.1:5010` and `[::1]:5010` |
| PostgreSQL | Healthy |
| MongoDB | Healthy |
| RabbitMQ and MassTransit | Healthy |
| Recovery MassTransit endpoints | Healthy and ready |
| Native recovery scheduler | Operational; fresh Hangfire worker and both required recurring jobs verified by runtime health |
| Native recovery dispatcher | Operational |
| Admin settings, eligibility, and stats APIs | `200` after final restart |
| Recovery automation | Effective disabled default; `automationEnabled=false`, `ineligibilityCode=recovery_disabled` |

The final eligibility response reported the WhatsApp account as present, enabled, and connected; `nativeSchedulerOperational=true`; `nativeDispatcherOperational=true`; and `isEligible=false` solely because recovery is disabled. The stricter scheduler health implementation requires a Hangfire server heartbeat newer than five minutes and registration of both `abandoned-cart-scanner` and `abandoned-cart-native-dispatcher`, so the successful response verifies both requirements in the restarted final binary.

The global readiness failure is solely the existing `payout_external_truth_readiness` mismatch. Recovery infrastructure and its MassTransit endpoints were healthy. Optional certificate-provider and Caddy checks were degraded because those integrations are disabled, not because of recovery. Transient Admin request failures recorded while the API was intentionally stopped cleared after restart; all three recovery page requests then returned `200`.

## 11. Production Conditions

For deployment:

1. Keep recovery disabled during rollout.
2. Deploy the migrated schema and final API together, ensuring no pre-retirement API instance can restart against the retired schema.
3. Verify liveness, readiness details, Hangfire registration, recovery queues, and Admin eligibility in the target environment.
4. Use an isolated authorized recipient and an approved transport test plan before enabling recovery for live delivery.
5. Confirm suppression, retries, idempotency, audit records, opt-out, and order attribution from runtime evidence before declaring live dispatch certified.

Native orchestration and the restarted MAIN runtime are certified while recovery remains disabled. Live customer-message delivery is not certified until the controlled external-delivery condition is completed.
