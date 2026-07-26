# Fawry Final Re-Certification

Date: 2026-07-15  
Scope: POS Admin and Storefront Fawry reference-payment creation and webhook processing  
Excluded: CommerceHub and inactive advanced-operation application commands  
Governing authority: `PAYMENT_REFERENCE_FREEZE.md`  
Runtime posture: certified, provider-neutral, and permanently frozen  
Board posture: final independent current-source certification review; no implementation change authorized

## Executive Summary

Fawry is **not Software Certified** and **not Reference Provider Certified** in
the current working tree.

The board independently reviewed all `.ai-context` files, the complete named
Fawry certification record, the Payment Reference Freeze, the final Payment
Runtime certification, the Stripe and Paymob reference decisions, the current
Fawry application, adapter, bridge, webhook, replay, persistence, migration,
health, telemetry, Storefront, and regression implementation, and the current
official Fawry reference-payment and Server Notification V2 contracts.

Historical findings are not carried forward merely because they once existed.
The following closures are real:

- the legacy simulated reference-number path has been replaced by an official
  HTTPS adapter boundary;
- official Notification V2 field parsing and `messageSignature` verification
  replace the historical raw-body HMAC contract;
- POS Admin and Storefront creation and webhook handling converge through the
  Fawry application/Runtime path;
- durable webhook replay storage, uniqueness, prior-evidence loading, and the
  existing `PaymentLifecycleService` writer are present;
- the raw durable webhook-payload and raw-signature finding is closed by the
  Fawry Data Protection envelope, recursive signature removal, signature
  digest, and fail-closed legacy backfill;
- the frozen Payment Runtime contains no Fawry protocol, credential, HTTP, SQL,
  EF Core, controller, frontend, or lifecycle-persistence logic.

Those closures do not establish final certification. Current source contains
six High findings:

1. the active application path does not supply required official PayAtFawry
   charge fields and supplies a non-numeric internal payment reference where
   the official charge contract requires a merchant reference number;
2. the adapter accepts an official charge response without verifying the
   documented Fawry response signature or exact amount, currency, and merchant
   reference;
3. ambiguous charge submission has no durable mutation-started checkpoint and
   can be automatically submitted again;
4. webhook credential/capability admission bypasses the certified tenant
   configuration resolver and can use a disabled or capability-ineligible
   tenant payment method;
5. bridge credentials are a reusable public request property rather than the
   required single-use, overlap-safe, consume-and-clear handoff;
6. provider HTTP response bodies can cross the adapter boundary in canonical
   error metadata with ineffective key-name replacement that leaves secret or
   customer values intact.

These are current source defects, not absent live credentials, deployment
preferences, theoretical risks, or historical findings. They prevent
Architecture, Implementation, Security, Software, and Reference Provider
certification. No Runtime change is required or authorized to close them.

The Payment Runtime remains provider-neutral and frozen. Stripe and the bounded
Paymob reference architecture retain their existing certification. Fawry must
adapt to that architecture.

## Audit Basis

The board read and applied:

- all seven files under `.ai-context`;
- `PAYMENT_REFERENCE_FREEZE.md` as controlling authority;
- `PAYMENT_RUNTIME_FINAL_CERTIFICATION.md`;
- `STRIPE_FINAL_RECERTIFICATION.md`;
- `PAYMOB_FINAL_RECERTIFICATION.md`;
- `PAYMENT_0_5C_FAWRY_DISCOVERY.md`;
- `fawry-payment-authority-certification-findings.md`;
- `FAWRY_1_FOUNDATION_CERTIFICATION.md`;
- `FAWRY_2_PROVIDER_ADAPTER.md`;
- `FAWRY_3_RUNTIME_BRIDGE.md`;
- `FAWRY_4_WEBHOOK_EVIDENCE_ADAPTER.md`;
- `FAWRY_5_ADVANCED_OPERATIONS.md`;
- `FAWRY_CONTRACT_CLOSURE_1.md`;
- `FAWRY_CONTRACT_CLOSURE_2.md`;
- `FAWRY_CONVERGENCE_1.md`;
- `FAWRY_PRODUCTION_1_OPERATIONAL_READINESS.md`;
- `FAWRY_PRODUCTION_2_SANDBOX_VALIDATION.md`;
- `FAWRY_CERTIFICATION_CLOSURE_1.md`.

The board reviewed the current implementation rather than treating phase
certifications as self-proving evidence. The in-scope current source includes:

- `PaymentService` Fawry creation, retry, recovery, and handoff projection;
- `FawryTenantConfigurationResolver` and tenant payment-method authority;
- `FawryRuntimeApplicationService` and `FawryRuntimeBridge`;
- `FawryPaymentAdapter` and official request/response/error mapping;
- `FawryWebhookEvidenceAdapter` and official Notification V2 verification;
- `FawryWebhookRuntimeIntegrationService` and outcome admission;
- `FawryWebhookController`, replay/audit transaction ordering, and the single
  lifecycle writer;
- `FawrySensitiveArtifactProtector` and legacy backfill;
- Fawry options, HTTP client, health, telemetry, and DI registration;
- Fawry replay entity, EF model, migration, model snapshot, and generated SQL;
- Storefront DTO admission, state, recovery presentation, and Fawry reference
  display;
- all Fawry and frozen Payment Runtime certification tests.

Official contract comparison used the current FawryPay documentation for
[PayAtFawry reference-number creation](https://developer.fawrystaging.com/docs/server-apis/create-payment-refno-apis)
and [Server Notification V2](https://developer.fawrystaging.com/docs/card-tokens/payment-notifications/server-notification-v2).

## Certification Board

| Certification question | Decision | Board basis |
|---|---|---|
| Architecture certified? | **No.** | Checkpoint recovery, webhook configuration authority, and credential handoff do not satisfy the freeze. |
| Implementation certified? | **No.** | The active charge contract is incomplete and charge response admission is not authenticated. |
| Security certified? | **No.** | Response signatures are not verified, credential handoff is reusable, and canonical errors can retain sensitive response values. |
| Operations certified? | **No.** | Source scaffolding exists, but executing-path metrics and configured retry governance are not wired. |
| Migration certified? | **No overall.** | The Fawry replay migration is source-valid; repository model drift and the target database remain unverified. |
| Regression certified? | **No.** | Applicable slices are green, but the complete Fawry/Runtime aggregate is 185/192 and official-contract/checkpoint gaps lack passing behavioral proof. |
| Reference Provider certified? | **No.** | Six High source findings remain. |
| Provider-neutral Runtime preserved? | **Yes.** | No Fawry or persistence concern was found in Runtime; 94/94 Runtime tests pass. |
| Architecture frozen? | **Payment architecture and Runtime: yes. Fawry implementation: no.** | Provider corrections belong outside Runtime. |
| Software certified? | **No.** | Open High source findings block software certification. |
| Operational deployment pending only? | **No.** | Source architecture, implementation, and security correction is required before deployment-only work. |
| Ready for future providers? | **Yes, against the frozen Stripe/Paymob reference architecture; Fawry is not a reference template.** | Provider neutrality remains intact. |

## Closed Findings

| Historical finding | Classification | Current disposition | Current evidence |
|---|---|---|---|
| Simulated deterministic Fawry reference generation instead of official provider execution | High | **Closed** | Active creation delegates through `IFawryRuntimeApplicationService`, `IFawryRuntimeBridge`, and the HTTPS charge adapter. The remaining official request defects are separately classified below. |
| Custom raw-payload HMAC and historical notification-field mismatch | High | **Closed** | `FawryWebhookEvidenceAdapter` parses Notification V2 fields and performs fixed-time SHA-256 `messageSignature` verification over the documented field order. |
| Direct legacy POS Admin/Storefront Fawry creation and webhook interpretation | High | **Closed** | `PaymentService` and `FawryWebhookController` delegate to the certified application integration boundaries. |
| Missing durable webhook replay authority | High | **Closed** | `FawryWebhookReplayRecords`, prior-evidence loading, tenant/provider/event uniqueness, and transactional replay append exist. |
| Raw webhook payload and raw message-signature persistence | High | **Closed** | New and backfilled Fawry webhook payloads use a purpose-isolated encrypted envelope with embedded signature removal; only a SHA-256 signature digest is durable. |
| Runtime provider coupling, Runtime SQL, or Fawry-owned Runtime stage sequencing | Critical | **Closed / not present** | Current Runtime source has no Fawry, provider transport, credential, EF Core, or SQL dependency; `PaymentRuntimeOrchestrator` remains the stage sequencer. |
| Storefront payment/reference authority | High | **Closed / not present** | Storefront accepts only complete backend Fawry handoff artifacts and cannot mark payment finality. |

The board does not keep the historical simulation, HMAC, convergence, replay,
or durable webhook-artifact findings open. The remaining findings below have
different current root causes.

## Remaining Findings

### FAWRY-FINAL-001 — Active charge request does not satisfy the official PayAtFawry contract

- Classification: **High**
- Affected certification: Architecture, Implementation, Software, Reference
  Provider, Sandbox readiness.
- Current evidence:
  - `PaymentService.cs:2295-2309` invokes the active Fawry application service
    without customer mobile, customer email, or language.
  - `FawryRuntimeApplicationService.cs:173` supplies the internal
    `PAY-YYYYMMDD-...` payment reference as `MerchantReference`.
  - `FawryPaymentAdapter.cs:129-138` treats customer mobile, customer email,
    and language as optional.
  - `FawryPaymentAdapter.cs:657-670` emits `PAYATFAWRY` and tests certify
    non-numeric values such as `merchant-ref-123` and `POS-...`.
  - Current official reference-charge documentation requires customer mobile,
    customer email, language, `PayAtFawry`, and a merchant reference number.
- Impact: the source-certified active request can fail official contract
  validation before a usable provider reference exists. The certification
  suite validates a repository-shaped request rather than the complete official
  request used by the production application path.
- Required correction location: Fawry application/adapter contract only. No
  Runtime change.

### FAWRY-FINAL-002 — Charge response integrity and exact outcome admission are absent

- Classification: **High**
- Affected certification: Implementation, Security, Software, Reference
  Provider.
- Current evidence:
  - the official charge response documents a SHA-256 response signature over
    provider reference, merchant reference, amounts, status, method, fees,
    customer fields, and the secure key;
  - `FawryChargeResponse` at `FawryPaymentAdapter.cs:525-580` does not model the
    response signature or the complete signed field set;
  - `FawryPaymentAdapter.InitializePaymentAsync` accepts the response based on
    HTTP status, provider status code, and existence of a provider reference;
  - `FawryRuntimeApplicationService.ProjectExecutionResult` admits the
    reference without exact response amount, currency, merchant reference,
    environment/account, or response-signature validation.
- Impact: an unauthenticated, mismatched, or corrupted charge response can
  become the durable provider/reference handoff. TLS transport does not replace
  the provider's documented message-integrity contract.
- Required correction location: adapter verification plus application Outcome
  Admission. No Runtime change.

### FAWRY-FINAL-003 — No durable mutation-started checkpoint prevents blind charge recreation

- Classification: **High**
- Affected certification: Architecture, Implementation, Replay, Recovery,
  Software, Reference Provider.
- Current evidence:
  - the local `PaymentIntent` is correctly committed before the provider call;
  - no durable field distinguishes “provider mutation never started” from
    “provider may have accepted but the response was lost”;
  - after timeout/network ambiguity, `ProviderIntentId` and `ReferenceCode`
    remain empty;
  - `PaymentService.cs:2204-2213` and `2317-2330` classify that row as incomplete
    and submit the charge again through the same creation method;
  - the Fawry discovery and Foundation record identify no certified provider
    idempotency-key contract. A deterministic merchant reference assists
    reconciliation but does not prove automatic recreation is safe.
- Impact: timeout, connection loss, process failure, or rollback after possible
  provider acceptance can create or attempt a second external charge/session.
  This violates the freeze's mandatory pre-mutation checkpoint and
  indeterminate-outcome reconciliation rule.
- Required correction location: application-owned checkpoint, inquiry, and
  recovery state. No Runtime change.

### FAWRY-FINAL-004 — Webhook credential and capability authority bypasses the shared resolver

- Classification: **High**
- Affected certification: Architecture, Tenant authority, Credential
  governance, Security, Software.
- Current evidence:
  - server-authoritative tenant resolution in `PaymentWebhookRequestPipeline`
    is present and correct;
  - after tenant resolution, `FawryWebhookController.cs:114` calls its own
    `GetWebhookSecretAsync`;
  - `FawryWebhookController.cs:530-555` decrypts a tenant row directly but does
    not require `IsEnabled`, online/webhook capability, Sandbox/Production
    agreement, current provider enablement, `CallbackExecutionEnabled`,
    `WebhookExecutionEnabled`, or callback/webhook certification flags;
  - `FawryTenantConfigurationResolver` governs payment creation only and exposes
    no webhook capability.
- Impact: a disabled or environment/capability-ineligible tenant Fawry method
  can remain webhook verification and executable lifecycle authority. The
  controller also becomes a second credential-configuration authority.
- Required correction location: application-owned current tenant
  configuration resolution for webhook capability. No Runtime change.

### FAWRY-FINAL-005 — Credential handoff is reusable rather than single-use and consume-and-clear

- Classification: **High**
- Affected certification: Architecture, Credential governance, Security,
  Reference Provider.
- Current evidence:
  - tenant credential selection and decryption are correctly application-owned;
  - `FawryExecutionRequest.Credentials` at `FawryRuntimeBridge.cs:70` is a public
    immutable property containing merchant code, API key, and secure key;
  - the bridge passes the property directly to the adapter and does not consume,
    clear, invalidate, or reject reuse;
  - the request is constructible and reusable by any holder and has no
    overlap-safe one-operation token semantics.
- Impact: this does not meet the exact one-shot credential-handoff standard
  applied to the certified providers. Credentials remain outside Runtime and
  durable evidence, but the bridge contract itself retains reusable credential
  authority.
- Required correction location: provider application/adapter handoff. No
  Runtime change.

### FAWRY-FINAL-006 — Canonical provider errors can retain secret and customer response values

- Classification: **High**
- Affected certification: Security, Artifact protection, Provider boundary,
  Software.
- Current evidence:
  - `FawryPaymentAdapter.cs:93-96` reads up to 512 characters of arbitrary
    provider error body;
  - `FawryPaymentAdapter.cs:233-237` places that body in canonical error metadata
    as `provider_diagnostic`;
  - `FawryPaymentAdapter.cs:308-318` replaces only the strings `signature`,
    `secureKey`, and `apiKey`; it does not remove the corresponding JSON values,
    customer fields, provider tokens, or other sensitive data;
  - the existing test body `{ signature: "secret" }` becomes an object whose
    key is renamed but whose value `secret` remains, while the assertion checks
    only that the word `signature` disappeared;
  - the bridge copies canonical error metadata into rejected
    `ProviderEvidence`.
- Impact: a provider error that echoes an authentication or customer value can
  move it across the adapter boundary into Runtime/application artifacts and
  downstream diagnostics. The webhook artifact closure does not cover this
  separate outbound error path.
- Required correction location: bounded, allow-listed canonical error mapping.
  No Runtime change.

### FAWRY-FINAL-007 — Authenticated terminal-negative callbacks are acknowledged without lifecycle or replay convergence

- Classification: **Medium**
- Affected certification: Implementation completeness, Recovery, Regression.
- Current evidence:
  - `FawryWebhookOutcomeAdmissionService` classifies `FAILED`, `EXPIRED`, and
    `CANCELED/CANCELLED` as terminal but sets both Runtime admission and
    lifecycle eligibility false;
  - `FawryWebhookController.cs:265-274` marks these events `Skipped`, commits the
    audit row, and returns HTTP 200;
  - replay records are appended only after a positive lifecycle result;
  - no negative lifecycle request updates the current pending
    `PaymentIntent`/order through the single writer.
- Impact: trusted negative provider evidence can remain durably acknowledged
  while local SQL stays pending until separate expiration or manual recovery.
  No duplicate writer is created, but the claimed source support for failure,
  cancellation, and expiration is incomplete.
- Certification effect: non-blocking by itself under the board rule, but the
  capability is not certified as implemented.

### FAWRY-FINAL-008 — Executing-path telemetry and configured retry governance are not wired

- Classification: **Operational / Deployment**
- Affected certification: Operations and deployment readiness.
- Current evidence:
  - `FawryOperationalTelemetry` declares and exports the required counters;
  - production-source search finds no Fawry `Record*` call outside the telemetry
    declaration;
  - `FawryOptions.RetryCount` is validated, but
    `FawryFoundationServiceCollectionExtensions.cs:24-28` configures only base
    URI and timeout on the named adapter client;
  - no executing-path latency histogram or environment evidence exists.
- Impact: documented Fawry dashboards/alerts cannot observe real execution, and
  reported retry configuration does not describe the HTTP client. Blind retries
  must not be added around charge creation; any retry policy must respect the
  checkpoint and ambiguity finding.

### FAWRY-FINAL-009 — Migration and target-database deployment state are not converged

- Classification: **Operational / Deployment**
- Affected certification: Migration, Regression, Deployment readiness.
- Current evidence:
  - EF discovers `20260715120000_FawryContractClosure2WebhookReplay`;
  - generated idempotent SQL creates the table, evidence-hash index,
    provider-reference index, unique tenant/provider/event replay fence, and
    migration-history row;
  - `dotnet ef migrations has-pending-model-changes` reports current model drift
    after the last migration;
  - four Fawry reliability tests cannot seed `Orders` because the local
    PostgreSQL schema lacks `BillingAddressAdmissionEvidenceId`;
  - no target environment proves the Fawry replay migration, Data Protection
    key ring, or legacy backfill.
- Impact: the Fawry replay migration artifact is source-valid, but the complete
  model and target deployment are not certified.

### FAWRY-FINAL-010 — Official Sandbox and live operational evidence are absent

- Classification: **External**
- Affected certification: Sandbox, Deployment, Live Operational certification.
- Current evidence: no named Fawry Sandbox merchant, tenant credentials, public
  signed callback, provider-delivered events, success/failure/expiration drill,
  duplicate/replay drill, isolation drill, rotation drill, inquiry/recovery
  drill, alert firing, or latency evidence is available.
- Impact: independently blocks Sandbox and live operational labels. It would
  not by itself block source Software Certification after source findings close.

### FAWRY-FINAL-011 — CommerceHub and active advanced-operation commands

- Classification: **Out of Scope**
- Current evidence:
  - no active CommerceHub Fawry provider path was found;
  - refund, partial refund, capture, void, and inquiry remain translation-only
    adapter capabilities with no certified application command, checkpoint,
    replay, Runtime outcome, or lifecycle-writer activation path.
- Certification effect: no effect on the named POS Admin + Storefront
  reference-payment scope. These capabilities must not be advertised as active.

### FAWRY-FINAL-012 — Runtime modification, Runtime SQL, provider-owned lifecycle, and duplicate active lifecycle writer

- Classification: **False Positive**
- Current evidence:
  - Runtime source scans find no Fawry/provider HTTP, credential, EF Core, SQL,
    controller, or frontend dependency;
  - Fawry adapters do not call individual Runtime stages;
  - the webhook controller calls `PaymentLifecycleService` only after exact
    positive Runtime outcome admission inside the application transaction;
  - the advanced adapter is inactive and no alternate active Fawry lifecycle
    writer was found.
- Certification effect: none. The source High findings are outside Runtime and
  must be corrected outside Runtime.

## Finding Classifications

| Classification | Remaining count | Certification effect |
|---|---:|---|
| Critical | 0 | None open. |
| High | 6 | Blocks Architecture, Implementation, Security, Software, and Reference Provider certification. |
| Medium | 1 | Non-blocking by itself under the board rule; the named negative-event capability remains uncertified. |
| Operational / Deployment | 2 | Blocks Operations, whole-model Migration, Deployment, and live readiness; does not replace source findings. |
| External | 1 | Blocks Sandbox and Live Operational certification only. |
| Out of Scope | 1 | No effect on the bounded reference-payment decision. |
| False Positive | 1 | Confirms the frozen Runtime and single-writer boundaries are preserved. |

## Architecture Summary

### Preserved

```text
Storefront / POS Admin
  -> PaymentService
  -> Fawry Runtime Application Service
  -> Fawry Runtime Bridge
  -> Fawry Payment Adapter
  -> canonical ProviderEvidence
  -> PaymentRuntimeOrchestrator
  -> exact application outcome admission
  -> PaymentIntent handoff projection
```

```text
bounded webhook ingress
  -> server-authoritative tenant resolution
  -> Fawry Notification V2 evidence adapter
  -> application replay / duplicate admission
  -> PaymentRuntimeOrchestrator
  -> exact positive outcome admission
  -> PaymentLifecycleService
  -> SQL webhook audit + replay record
```

The provider-neutral Runtime, its stage order, and its storage-free boundary are
preserved. No Fawry-specific Runtime change is required. Storefront remains a
presentation consumer. `PaymentLifecycleService` remains the single active
durable payment/order lifecycle writer.

### Not certified

The active provider boundary is not yet the frozen reference shape because:

- the official request and response contracts are not fully admitted;
- the response-integrity contract is omitted;
- charge mutation checkpoint/recovery does not distinguish unstarted from
  ambiguous provider acceptance;
- webhook credential capability has a second controller-owned resolution path;
- credential handoff is reusable rather than one-shot;
- canonical provider error metadata is not safely bounded.

Architecture decision: **not certified for Fawry**.  
Provider-neutral Runtime decision: **preserved and frozen**.

## Security Summary

Verified security controls:

- server-authoritative webhook tenant resolution from stored payment identity;
- official Notification V2 SHA-256 `messageSignature` verification with
  fixed-time comparison;
- bounded webhook body and rate limiting;
- tenant configuration encryption at rest;
- durable replay uniqueness and transactional positive lifecycle convergence;
- encrypted, signature-scrubbed webhook payload evidence;
- signature digest instead of raw durable signature;
- no credential or provider protocol inside Runtime;
- no reviewed Storefront authority to mark payment success.

Security certification is nevertheless withheld because charge response
authentication, exact response admission, one-shot credential handoff, current
webhook configuration authority, and bounded canonical error diagnostics are
not satisfied.

## Replay And Recovery Summary

Webhook replay architecture is source-valid for positive admitted events:

- tenant + provider + provider event uniqueness exists in both webhook log and
  Fawry replay storage;
- replay/duplicate reservation precedes lifecycle mutation;
- positive lifecycle write, webhook completion, and replay append commit under
  the controller transaction;
- prior evidence is loaded outside Runtime and supplied to Runtime;
- duplicate or concurrent delivery cannot enter a second lifecycle write.

Charge-creation recovery is not certified. A committed local `PaymentIntent`
exists before provider I/O, but the absence of a mutation-started/ambiguous
checkpoint makes that row insufficient to decide whether charge creation can be
repeated. Recovery must use inquiry or reconciliation after indeterminate
acceptance, not infer that an empty local provider reference means the provider
was never called.

## Regression Summary

Independent execution on 2026-07-15:

| Validation | Result | Decision |
|---|---:|---|
| Fawry or Payment Runtime aggregate | **185 passed, 7 failed, 0 skipped** | Not green |
| Applicable Fawry slice excluding the two pre-existing failing classes | **90 passed, 0 failed, 0 skipped** | Existing certification behavior green |
| Frozen Payment Runtime slice | **94 passed, 0 failed, 0 skipped** | Runtime preserved |
| API build | **Passed, 0 warnings, 0 errors** | Green |
| Storefront production build | **Passed** | Green; existing bundle/import warnings only |
| EF Fawry replay migration discovery | Listed | Source-valid |
| EF idempotent migration SQL | Table and all required indexes generated | Source-valid |
| EF pending-model check | Failed: model changes after last migration | Deployment not converged |

The seven aggregate failures are:

- four `FawryWebhookReliabilityTests` blocked during fixture setup by the stale
  local PostgreSQL `Orders` schema;
- three `FawryPaymentAuthorityCertificationTests` containing historical
  source/text assertions for pre-convergence implementation strings and a
  prohibited permanent `.ai-context` marker.

These failures are reported honestly and are not used to reopen closed
simulation, webhook-field, or artifact-protection findings. Conversely, the 90
passing applicable Fawry tests do not prove the official required request
fields, response signature, ambiguous mutation checkpoint, shared webhook
credential authority, or one-shot handoff. No test currently closes those High
findings.

Regression decision: **not certified overall**.

## Migration Summary

The Fawry replay migration itself is source-certified:

- EF discovers `20260715120000_FawryContractClosure2WebhookReplay`;
- the migration carries explicit `DbContext` and migration metadata;
- the model and snapshot contain `FawryWebhookReplayRecord`;
- generated idempotent SQL creates `FawryWebhookReplayRecords`;
- generated SQL creates the evidence-hash and provider-reference indexes;
- generated SQL creates the unique tenant/provider/event index filtered on
  non-deleted records;
- generated SQL writes the migration-history row.

Whole-model Migration Certification is withheld because current EF model drift
exists and the local/target database state is not converged. This is classified
Operational / Deployment and is not misrepresented as the root cause of the
source High findings.

## Deployment Summary

Fawry is not ready for deployment. Before deployment validation can begin, the
six High source findings must close and final re-certification must pass.

After source closure, deployment still requires:

1. a reproducible release artifact and reviewed migration set;
2. application and verification of all current EF migrations;
3. protected tenant Sandbox credentials and consistent environment/capability
   flags;
4. public HTTPS callback/webhook registration with Fawry;
5. Data Protection key-ring durability, restore proof, and legacy backfill
   verification;
6. executing-path metrics, dashboards, alerts, owners, and escalation routes;
7. success, failure, cancellation, expiration, duplicate, concurrent replay,
   timeout, ambiguous charge, recovery, and secret-rotation drills;
8. two-tenant isolation with independent merchant credentials;
9. measured create, webhook, recovery, and database latency;
10. rollback, emergency disable, retention, and reconciliation proof.

Inactive advanced operations require a separate application-contract,
checkpoint, replay, Runtime-outcome, lifecycle, and Sandbox certification before
activation.

## Certification Decision

The board applies the same standard used for Stripe and Paymob. Operational,
Deployment, External, Out of Scope, False Positive, and non-blocking Medium
findings would not withhold Software Certification by themselves. That rule does
not apply here because six High current-source findings remain.

No finding requires or authorizes a Payment Runtime modification. Fawry must
close its provider/application/security boundaries against the frozen Runtime,
then undergo another independent final re-certification.

No new permanent cross-provider architectural truth was discovered. Existing
checkpoint, provider-response authentication, tenant credential authority,
one-shot handoff, bounded canonical error, replay, and single-writer truths are
sufficient. Therefore `.ai-context` is not modified.

## Final Decision

WITHHOLD CERTIFICATION
