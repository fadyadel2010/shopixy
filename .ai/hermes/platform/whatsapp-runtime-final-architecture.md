1# WhatsApp Runtime — FINAL CANONICAL ARCHITECTURE

> Status: Implementation-backed canonical specification.
> Authority: The C# backend (`PointOfSale.*`) and the Node sidecar
> (`PointOfSale.WhatsApp.Baileys`) are the ONLY source of truth. All prior
> markdown (`WHATSAPP-*.md`, `docs/WHATSAPP_*`), code comments, UI labels, and
> naming are treated as secondary and were re-verified against code. Every claim
> is cited to `file:line`. Where evidence was not found, this document says so
> explicitly and never fabricates.
> Scope note: The React module (`PointOfSale.Web/src/modules/whatsapp/**`) is a
> frontend and is deliberately excluded — this is a runtime document.

---

## Executive Summary

The "WhatsApp Runtime" is **not** a messaging service, **not** a notification
service, and **not** a thin API wrapper. Reverse engineering the implementation
shows it is a **tenant-scoped, fail-closed outbound communication delivery
runtime built as a Strangler-Fig transport abstraction over two interchangeable
WhatsApp providers**, whose defining architectural property is that **outbound
message content is never trusted from the triggering event — it is rebuilt from
current PostgreSQL authority at dispatch time**
(`PointOfSale.Infrastructure/WhatsApp/Services/WhatsAppDispatchAuthority.cs:17-75`).

Concretely, the runtime:

- Owns WhatsApp **account identity, connection state, templates, per-message
  delivery logs, and operational safety** (kill switch, circuit breaker, rate
  limits) for each tenant.
- Owns **nothing** about orders, payments, customers, or OTP business truth — it
  reads those from their owning SQL tables at send time and refuses to send if
  the authoritative state no longer justifies the message.
- Is **send-only**: there is no inbound WhatsApp message ingestion and no
  delivery-status callback pipeline in the backend. The terminal success state
  is `SENT` (provider accepted), not `DELIVERED`/`READ`.
- Routes every send through a **single central enforcement point**
  (`WhatsAppMessageDispatcher`) that applies kill switch → circuit breaker → SQL
  re-authorization → phone normalization → consent → rate limit → transport, in
  that fixed order.

Architecturally, it is best classified as a **Provider-Abstracted Outbound
Delivery Runtime with SQL-Authority Re-Derivation** — a delivery engine that
treats messaging providers as swappable, untrusted edges and treats the
platform's relational store as the sole arbiter of what may be sent.

---

## Architecture Vision

1. **Providers are replaceable, untrusted edges.** Two transports —
   browser-driving Playwright and a Node/Baileys sidecar — implement one
   interface (`IWhatsAppTransport`,
   `PointOfSale.Infrastructure/WhatsApp/Interfaces/IWhatsAppTransport.cs:7-54`)
   and are selected per-tenant by the `Tenant.UseBaileys` feature gate with **no
   automatic fallback** (`WhatsAppMessageDispatcher.cs:288-293`). This is an
   explicit Strangler-Fig migration posture, not redundancy.
2. **The message is a projection of SQL, not of the event.** The event/queue
   payload and the Mongo log select *work*; they never decide customer, order,
   payment, account, or tenant truth (`WhatsAppDispatchAuthority.cs:17-19`).
3. **Fail closed everywhere.** Missing tenant, disconnected account, opened
   circuit, unavailable Redis, stale authority → the message is skipped or
   failed, never force-sent.
4. **Safety is first-class, not a flag.** Kill switch and circuit breaker are
   checked before any transport work and are the first two gates in the pipeline.
5. **Delivery is auditable and idempotent.** Every attempt is a Mongo log with an
   idempotency key and a lease-based single-dispatch claim.

---

## Runtime Purpose

To deliver a bounded set of tenant-authorized transactional WhatsApp messages
(order confirmation, OTP verification, manual-payment approval/rejection
notifications, and abandoned-cart recovery) reliably, safely, and idempotently,
while guaranteeing that the *content and permission to send* always reflect
current SQL state rather than the potentially-stale event that triggered the send.

---

## Runtime Scope

In scope (owned by this runtime):

- WhatsApp account lifecycle & connection state (`WhatsAppAccount`).
- Per-tenant provider selection (Playwright vs Baileys transport).
- Message templates (CRUD, validation, rendering).
- The dispatch pipeline and its safety gates.
- Delivery logging, analytics, retry.
- Operational safety: kill switch, circuit breaker, Redis + SQL rate limiting,
  consent/opt-out.

Out of scope (deferred to owning domains):

- Order truth, payment truth, customer identity, OTP business meaning — read-only
  at dispatch.
- Money movement, accounting, inventory — never touched.
- Inbound WhatsApp conversations, delivery receipts, read receipts — not
  implemented in the backend.

---

## Runtime Owns

| Concept | SQL/store truth it writes | Evidence |
|---|---|---|
| WhatsApp account identity & status | `WhatsAppAccounts` (PostgreSQL) | `PointOfSale.Core/Entities/WhatsAppAccount.cs:9-76` |
| Use-case ownership (one connected account per use case) | `WhatsAppAccounts` uniqueness enforced in code | `WhatsAppAccountService.cs:241-266` |
| Message templates | `WhatsAppMessageTemplates` (PostgreSQL) | `WhatsAppMessageTemplate.cs:5-40`; `WhatsAppAccountService.cs:168-239` |
| Delivery log (attempt state machine) | `WhatsAppMessageLogs` (MongoDB) | `WhatsAppMessageLogDocument.cs:9-88` |
| Session credentials | Redis (encrypted) | `WhatsAppSessionManager.cs:25-71` |
| Kill switch state | Redis | `WhatsAppKillSwitch.cs:26-60` |
| Circuit breaker state | Redis | `WhatsAppCircuitBreaker.cs:33-202` |
| Throughput rate limits | Redis | `WhatsAppRateLimiter.cs:24-105` |
| Consent / opt-out & per-recipient window | `WhatsAppRateLimits` (PostgreSQL) | `WhatsAppRateLimit.cs:17-115`; `WhatsAppRateLimitService.cs:28-163` |

---

## Runtime Does NOT Own

- **Orders** — read-only via `Orders`/`Order.Items` at authorization
  (`WhatsAppDispatchAuthority.cs:102-140`).
- **Payments** — read-only via `PaymentIntents` + `Orders.PaymentStatus`; it only
  *reflects* lifecycle decisions, never mutates them
  (`WhatsAppDispatchAuthority.cs:142-211`).
- **OTP business meaning** — the runtime renders/sends the code; generation and
  verification authority live in the OTP service (see OTP Runtime).
- **Tenant lifecycle** — read-only via `TenantLineageAuthority.IsActiveTenantAsync`
  (`WhatsAppDispatchAuthority.cs:44`).
- **Money, accounting, inventory** — zero references in the runtime.

---

## Architecture Principles

1. **Single central enforcement point.** All delivery flows through
   `WhatsAppMessageDispatcher.DispatchInternalAsync`
   (`WhatsAppMessageDispatcher.cs:98-372`). No consumer sends directly.
2. **Fixed gate order.** Terminal-state short-circuit → lease claim → identity
   parse → kill switch → circuit breaker → SQL re-authorization → phone
   normalization → messaging eligibility (consent) → throughput rate limit →
   jitter → transport routing → send (`WhatsAppMessageDispatcher.cs:107-304`).
3. **SQL re-derivation of content.** `authorized.MessageText` is rebuilt from SQL;
   the comment at `WhatsAppMessageDispatcher.cs:266` states "Mongo/event payload
   text is not send authority."
4. **Idempotent logs.** A message log requires an idempotency key; duplicate keys
   return the existing document instead of double-inserting
   (`WhatsAppMessageLogService.cs:20-42`).
5. **Lease-based single dispatch.** `TryClaimDispatchAsync` atomically transitions
   `PENDING/QUEUED/FAILED` (or lease-expired `DISPATCHING`) → `DISPATCHING` with a
   2-minute lease and a max-attempt guard (`WhatsAppMessageLogService.cs:68-103`;
   lease params `WhatsAppMessageDispatcher.cs:113-119`).
6. **Fail-safe safety primitives.** Circuit breaker treats Redis failure as OPEN
   to avoid flooding and risking bans (`WhatsAppCircuitBreaker.cs:58-67`).
7. **Tenant isolation by construction.** `WhatsAppAccount : TenantEntity`; all
   reads filter by `TenantId`, consumers use `IgnoreQueryFilters()` + explicit
   tenant predicate because they run without tenant context
   (`WhatsAppAccountService.cs:114-147`).

---

## Account Runtime

- **Identity:** `WhatsAppAccount` = `{PhoneNumber (E.164), DisplayName, Status,
  Enabled, UseFor* flags, LastConnected/DisconnectedAtUtc, StatusReason}`
  (`WhatsAppAccount.cs:9-76`). It is a `TenantEntity`, so tenant ownership is
  structural.
- **Creation:** requires an *active* tenant
  (`WhatsAppAccountService.cs:42-45`); starts `PendingQR`, `Enabled=false`
  (`WhatsAppAdminController.cs:94-106`).
- **Multi-account:** a tenant may hold many accounts, but **only one connected
  account may own a given use case** — enforced by `EnsureUseCaseOwnershipAsync`
  which rejects a second enabled+connected account claiming the same
  `UseForOrderConfirmation/UseForOtpVerification/UseForPaymentApproval/UseForAbandonedCartRecovery`
  (`WhatsAppAccountService.cs:241-266`). There is **no explicit "default
  account" flag** — "the account for a use case" is resolved by query
  (`GetActiveAccountForUseCaseAsync`, `WhatsAppAccountService.cs:114-147`).
- **Activation:** enabling requires `Status == Connected`
  (`WhatsAppAccountService.cs:99-102`; controller re-checks at
  `WhatsAppAdminController.cs:281-284`).
- **Deactivation:** `ToggleAccountStatusAsync(enabled:false)`
  (`WhatsAppAccountService.cs:93-112`).
- **Deletion:** hard delete of the row, preceded by a transport-aware disconnect
  (`WhatsAppAdminController.cs:129-151`).
- **Phone change = re-auth:** changing `PhoneNumber` resets to `PendingQR`,
  clears `LastConnectedAtUtc`, and disables the account
  (`WhatsAppAccountService.cs:70-76`).
- **Credentials/secrets:** the account row holds no session secret; session
  credentials live encrypted in Redis (`WhatsAppSessionManager.cs`), and the
  Baileys sidecar persists its own auth state (see Provider Runtime).

---

## Connection Runtime

- **QR flow:** `GET accounts/{id}/qr` routes to Baileys `GetQrCodeAsync` or
  Playwright `GenerateQrCodeAsync` based on `Tenant.UseBaileys`
  (`WhatsAppAdminController.cs:157-185`); QR TTL reported as 60s
  (`WhatsAppAdminController.cs:175`).
- **Status check:** `GET accounts/{id}/status` queries the live transport; for
  Baileys it additionally requires the connected phone to match the configured
  phone (`ConnectedPhoneMatches`, `WhatsAppAdminController.cs:229-238,290-299`) —
  a hijack/identity guard. Status changes are persisted back to SQL
  (`WhatsAppAdminController.cs:246-256`).
- **Session lifecycle / recovery:** the `WhatsAppSessionHeartbeatJob`
  (Hangfire, every 5 min — `HangfireInitializationHostedService.cs:217-220`) and
  `WhatsAppSessionCleanupJob` (daily 03:00 —
  `HangfireInitializationHostedService.cs:227-230`) plus the
  `WhatsAppSessionMonitorConsumer` maintain/repair connection state. See the
  Transport & Connection evidence in the "Provider Runtime — verified internals"
  section. The heartbeat writes status transitions and, when unchanged-but-
  connected, calls `TouchSessionAsync` to refresh session TTL
  (`WhatsAppSessionHeartbeatJob.cs:177-232`); on transport error it deliberately
  does NOT change status (`:141-144`). `WhatsAppSessionMonitorConsumer`
  auto-enables an account on `Connected` and disables it on `Banned`
  (`WhatsAppSessionMonitorConsumer.cs:55-70`). The daily cleanup job does NOT
  clean `whatsapp:session:*`/`whatsapp:auth:*` keys — it only sweeps
  `whatsapp:ratelimit:*`/`whatsapp:otp:*` and is largely inert
  (`WhatsAppSessionCleanupJob.cs:45-73`); real stale-session reaping happens in
  the Node sidecar (`SessionManager.ts:1433-1515`).
- **Session storage:** encrypted in Redis with a 30-day TTL and
  `IDataProtector`; decryption failure is treated as a lost session
  (`WhatsAppSessionManager.cs:13-57`). `TouchSessionAsync` refreshes TTL
  (`WhatsAppSessionManager.cs:66-71`).

---

## Provider Runtime

- **Abstraction:** `IWhatsAppTransport` with `SendMessageAsync`, `GetQrCodeAsync`,
  `GetStatusAsync`, `DisconnectAsync` (`IWhatsAppTransport.cs:7-54`) and value
  results (`WhatsAppSendResult`, `WhatsAppQrResult`, `WhatsAppStatusResult`,
  `WhatsAppDisconnectResult`, `IWhatsAppTransport.cs:69-120`).
- **Implementations (two, keyed DI):**
  - `PlaywrightTransport` (keyed `"playwright"`) over
    `PlaywrightWhatsAppEngine` — browser-driven WhatsApp Web
    (`Program.cs:1272,1287`).
  - `BaileysWhatsAppTransport` (keyed `"baileys"`, `HttpClient`-backed) — an HTTP
    client to the Node sidecar `PointOfSale.WhatsApp.Baileys`
    (`Program.cs:1290-1291`).
- **Sidecar:** `pointofsale-whatsapp-baileys` (Express + `@whiskeysockets/baileys
  ^7.0.0-rc.9`) exposing `/sessions/:tenantId/{connect,qr,status,disconnect}`,
  `/messages/send`, and `/admin/*` routes, API-key authenticated
  (`PointOfSale.WhatsApp.Baileys/src/index.ts:1-56`,
  `src/routes/*.ts`, `package.json:2-27`). Session identity is **per tenant**,
  not per account (routes keyed by `:tenantId`).
- **Selection:** `Tenant.UseBaileys` decides transport, **no fallback**
  (`WhatsAppMessageDispatcher.cs:288-293`).
- **Send-only inbound posture:** the sidecar subscribes to `connection.update`
  and `creds.update` but **not** `messages.upsert`
  (`src/services/SessionManager.ts:578-591,695-708`) — there is no inbound
  message ingestion.

### Provider Runtime — verified internals

Baileys (Node sidecar, `@whiskeysockets/baileys` v7-rc, WhatsApp Web MD over a
websocket — no browser):

- **Transport:** C#→Node is HTTP/JSON over `HttpClient` with a hardcoded 10s
  timeout and `X-API-Key` auth (`BaileysWhatsAppTransport.cs:29-47`); the sidecar
  enforces the key on all routes except health probes (`index.ts:74`).
- **Session identity is per-tenant** (routes keyed by `:tenantId`); live sessions
  held in an in-memory `Map`, with auth creds encrypted **AES-256-GCM** in Redis
  (`whatsapp:auth:{tenant}`, 30-day TTL) and metadata at
  `whatsapp:session:{tenant}` (24h TTL) (`SessionManager.ts:49,261-262,1334-1337`;
  `AuthStatePersistence.ts:43-117`). Redis is authoritative; on restart a
  previously-`CONNECTED` session recreates the socket and reloads auth from Redis
  (`SessionManager.ts:258-322,644-753`).
- **QR:** C# `POST /sessions/{t}/connect` then `GET /sessions/{t}/qr`; Node polls
  up to 10s for a QR from `connection.update`, returns base64 PNG, or
  `alreadyConnected` (`sessions.ts:69-94,232-318`; `SessionManager.ts:790`).
- **Recovery:** jittered reconnect (5–35s, max 5 attempts), poison detection on
  auth-invalidating codes `[401,403,405,410,411,428]` → nuclear auth cleanup +
  `POISONED`/`LOGGED_OUT`, and a `SelfHealingService` (10s loop: auto-reset
  poisoned after 10min, unstick QR>60s, reconnect-storm cooldown)
  (`SessionManager.ts:60-72,835-883,963`; `SelfHealingService.ts:84-101`).
- **Capabilities/limits:** text only — `sendMediaMessage`/`sendTemplateMessage`
  are "not yet implemented" stubs (`MessageSender.ts:617-633`), so the C#
  `Media`/`Template` enum values are effectively unsupported. Returns a **real**
  Baileys `key.id` as the provider message id (`MessageSender.ts:570`).

Playwright (C# `PlaywrightWhatsAppEngine`, ~775 lines, real Chromium against
web.whatsapp.com):

- **`Headless = false`** — requires a *visible* browser
  (`PlaywrightWhatsAppEngine.cs:162-170`); a latent production blocker on headless
  servers. State = on-disk browser profile per account
  (`%LocalAppData%/.../WhatsAppSessions/{accountId}`), capped at 100 contexts with
  1-min idle eviction (>5min) (`:26-31,86-89,635-682`).
- **No reconnect / self-heal / poison handling.** QR = screenshot of whatever
  canvas appears (`:203-225`); send = drive the search box + message input, then
  a lenient tick-mark verify (`:293-565`).
- **The Playwright `MessageId` is a locally-generated `Guid.NewGuid()`, NOT a
  WhatsApp server id** (`:374`) — downstream consumers cannot distinguish a real
  ack from a fabricated one. The C# `IWhatsAppSessionManager` Redis blob is
  **never written by Playwright** (zero callers) — dead code relative to this
  transport. Playwright also resolves its account via the **OrderConfirmation**
  use case only (`PlaywrightTransport.cs:214-230`).

---

## Messaging Runtime

- **Creation:** consumers build a `WhatsAppMessageLogDocument` (status `PENDING`,
  with idempotency key) and enqueue dispatch (see Consumer/Event Runtime).
- **Dispatch:** `WhatsAppMessageDispatcher` is the sole sender
  (`WhatsAppMessageDispatcher.cs:88-372`).
- **Queue/ordering:** there is **no strict ordering guarantee**; the retry job
  pulls pending/failed messages sorted by `CreatedAtUtc` and processes up to 50
  per run (`WhatsAppMessageLogService.cs:138-166`,
  `WhatsAppMessageRetryJob.cs:32-70`).
- **Priority:** none explicit; transactional message *types* bypass the recipient
  cooldown (see Rate/Safety), which is the only prioritization signal
  (`WhatsAppRateLimiter.cs:51-71`).
- **Rate limiting / retry / failure / recovery:** covered in Retry, Delivery, and
  Safety runtimes below.

---

## Consumer & Event Runtime

This runtime is **event-driven**: every send originates from a MassTransit
consumer of a `WhatsApp*` domain event. The five WhatsApp consumers are
registered at `Program.cs:1949-1956`; the bus applies a global retry policy
`Intervals(1s,5s,15s,30s)` (`Program.cs:2011-2015`) to any consumer without its
own `ConsumerDefinition`. Tenant context is **not** present in the consumer
scope, so each consumer derives `TenantId` from `IntegrationEvent.TenantId`
(`IntegrationEvent.cs:117`) and passes it explicitly.

| Flow | Publisher | Event | Consumer | Log type / status | Dispatch |
|---|---|---|---|---|---|
| Order confirmation | `CheckoutService.cs:8479` | `WhatsAppOrderConfirmationRequestedEvent` | `WhatsAppOrderConfirmationConsumer` | `ORDER_CONFIRMATION`/`PENDING` | dispatch **without** inline text → rebuilt from SQL |
| OTP | `StorefrontPhoneVerificationService.cs:488` (code at `:476`) | `WhatsAppOtpRequestedEvent` | `WhatsAppOtpConsumer` | `OTP`/`PENDING`, `IsRetryable=false`, redacted payload | dispatch **with** inline ephemeral text |
| Payment submitted → admin | `PaymentService.cs:3467` (`AdminPhoneNumber=null`) | `WhatsAppPaymentApprovalRequestedEvent` | `WhatsAppPaymentApprovalRequestedConsumer` | `PaymentApprovalRequest`/`QUEUED` | **SKIPPED** (see key finding 2) |
| Payment approved → customer | `PaymentService.cs:3525` (`PublishSensitiveAsync`) | `WhatsAppPaymentApprovedNotificationEvent` | `WhatsAppPaymentApprovedNotificationConsumer` | `PaymentApproved`/`QUEUED` | rebuilt from `PaymentIntents`+`Orders` |
| Payment rejected → customer | `PaymentService.cs:3588` | `WhatsAppPaymentRejectedNotificationEvent` | `WhatsAppPaymentRejectedNotificationConsumer` | `PaymentRejected`/`QUEUED` | rebuilt from `PaymentIntents`+`Orders` |

- **Order confirmation consumer:** guards `IsOrderConfirmationEnabledAsync` +
  active account, then `templateResolver.ResolveOrderConfirmationAsync` and
  `DispatchAsync(log.Id)` with no inline text
  (`WhatsAppOrderConfirmationConsumer.cs:44-93`).
- **OTP consumer:** the 6-digit code is **pre-generated upstream** in
  `StorefrontPhoneVerificationService.GenerateOtp()` and carried inside the event
  (`WhatsAppOtpRequestedEvent.OtpCode`, `WhatsAppOtpRequestedEvent.cs:11`); the
  consumer stores `PayloadSnapshot.text = "[OTP MESSAGE - CONTENT REDACTED]"` and
  `IsRetryable=false`, then dispatches the real text inline
  (`WhatsAppOtpConsumer.cs:90-124`). Permanent-validation failures throw
  `WhatsAppOtpPermanentFailureException` (missing tenant / gate disabled / no
  account), excluded from retry by `WhatsAppOtpConsumerDefinition`
  (`Exponential(5,1s,30s,2s).Ignore<…>()`, `:152-163`).
- **Payment consumers:** all three delegate to `WhatsAppPaymentNotificationService`
  and wrap the call in `try/catch(Exception)` that **never rethrows**
  (`WhatsAppPaymentApprovalRequestedConsumer.cs:46-53`,
  `WhatsAppPaymentApprovedNotificationConsumer.cs:45-52`,
  `WhatsAppPaymentRejectedNotificationConsumer.cs:52-59`). The service layer also
  swallows all exceptions (`IWhatsAppPaymentNotificationService.cs:6-12`:
  "non-blocking, failures never thrown, no decision authority"). All three events
  are `INonAuthoritativeIntegrationEvent` — WhatsApp failures never affect the
  payment lifecycle.
- **Idempotency:** every log carries a tenant-scoped idempotency key; the Mongo
  unique sparse index `idx_idempotency_key` + `CreateLogAsync` dedupe on
  duplicate key (`MongoDbContext.cs:659-666`, `WhatsAppMessageLogService.cs:30-42`).

### Consumer & Event — key implementation findings

1. **`WhatsAppOtpService` (Redis) is dormant.** Registered at `Program.cs:1306`
   but has zero callers in the live path (repo-wide grep). The OTP code is
   generated/stored by `StorefrontPhoneVerificationService` +
   `CheckoutPhoneOtpStore`, not this class — the "WhatsApp OTP service" at the
   WhatsApp tier is dead relative to delivery (`WhatsAppOtpConsumer.cs`,
   `StorefrontPhoneVerificationService.cs:428-476`).
2. **Admin `PaymentApprovalRequest` is silently dropped.** It is dispatched with
   no inline text and the dispatcher's authority returns `null` for that message
   type (`WhatsAppDispatchAuthority.cs:69`, no `PayloadSnapshot` fallback), so it
   is marked `SKIPPED` ("Current SQL authority rejected dispatch"). Combined with
   the publisher-side `AdminPhoneNumber=null` TODO (`PaymentService.cs:3460`), no
   admin WhatsApp message is ever produced for payment review — a latent gap.
3. **Asymmetric PII handling** across the two customer payment events: `Approved`
   is `ISensitiveIntegrationEvent` + `PublishSensitiveAsync` (bypasses outbox),
   while `Rejected` carries the same `CustomerPhoneNumber` but is a plain
   `PublishAsync` (`WhatsAppPaymentRejectedNotificationEvent.cs:13` vs
   `WhatsAppPaymentApprovedNotificationEvent.cs:9`) — inconsistent sensitive-data
   treatment.

---

## Template Runtime

- **Authority:** `WhatsAppMessageTemplates` in PostgreSQL, tenant-scoped
  (`WhatsAppMessageTemplate.cs:5-40`).
- **CRUD:** via `WhatsAppAccountService` (`CreateTemplateAsync`/`Update`/`Delete`,
  `WhatsAppAccountService.cs:168-239`) exposed at `api/admin/whatsapp/templates`
  (`WhatsAppAdminController.cs:404-464`).
- **Uniqueness:** one template per `(TenantId, Type, Language)`
  (`WhatsAppAccountService.cs:172-182,207-218`).
- **Validation:** `NormalizeAndValidateTemplate` allows only a fixed variable set
  per type — `OrderConfirmation` → `{order_number, customer_name, total,
  currency, items_count, store_name}`, `OTP` → `{otp_code}`; any other or
  malformed `{{...}}` is rejected (`WhatsAppAccountService.cs:268-299`). Note:
  only `OrderConfirmation` and `OTP` template types are validatable — other types
  throw "Unsupported WhatsApp template type."
- **Rendering:** `WhatsAppTemplateResolver` does `{{var}}` substitution with a
  **code fallback** template when no DB row exists, and a language fallback chain
  `requested → en → code default` (`WhatsAppTemplateResolver.cs:22-126`).
- **Versioning / approval / localization:** no version history and no approval
  workflow exist (only `CreatedAtUtc`/`UpdatedAtUtc` timestamps,
  `WhatsAppMessageTemplate.cs:31-39`). Localization is limited to the `Language`
  column + the en fallback (`WhatsAppTemplateResolver.cs:99-111`). Payment
  approval/rejection text is **hardcoded** in the dispatch authority, not
  template-driven (`WhatsAppDispatchAuthority.cs:156-178`).

---

## Delivery Runtime

- **Status set (Mongo `status`):** `PENDING`, `DISPATCHING`, `SENT`, `FAILED`,
  `SKIPPED`, `DELIVERY_UNKNOWN` (`WhatsAppMessageLogDocument.cs:45-46`;
  transitions in `WhatsAppMessageDispatcher.cs`).
- **Terminal states:** `SENT`, `SKIPPED`, `DELIVERY_UNKNOWN` short-circuit
  re-dispatch (`WhatsAppMessageDispatcher.cs:107-111`).
- **`DELIVERY_UNKNOWN`** is used for *ambiguous* transport failures
  (timeout/connection/network/socket) so they are not falsely marked failed and
  not blindly retried (`WhatsAppMessageDispatcher.cs:333-336,374-388`), and on
  exception (`WhatsAppMessageDispatcher.cs:365`).
- **No DELIVERED/READ/EXPIRED ingestion:** the terminal success is `SENT` =
  provider accepted. There is no delivery-receipt/read-receipt synchronization in
  the backend (no `messages.upsert`/receipt handler; confirmed absent).
  `providerMessageId` is stored (`WhatsAppMessageLogDocument.cs:60-61`) but never
  reconciled against a later delivery event.
- **Completion is lease-guarded:** `CompleteDispatchAsync` only mutates a doc that
  still holds the matching `DispatchLeaseId` and is `DISPATCHING`
  (`WhatsAppMessageLogService.cs:105-136`), preventing a stale worker from
  overwriting a newer state.

---

## Retry Runtime

- **Policy:** Hangfire `WhatsAppMessageRetryJob` every 5 min
  (`HangfireInitializationHostedService.cs:222-225`) pulls retryable
  pending/failed/lease-expired messages (`WhatsAppMessageLogService.cs:138-166`).
- **Max attempts:** `MaxRetries = 3` in the job
  (`WhatsAppMessageRetryJob.cs:17`); the claim path also enforces `maxAttempts:3`
  (`WhatsAppMessageDispatcher.cs:118`; `WhatsAppMessageLogService.cs:78`).
- **Backoff:** no exponential backoff — fixed 5-min cadence plus a per-send random
  jitter delay (`WhatsAppRateLimiter.cs:107-111`, applied
  `WhatsAppMessageDispatcher.cs:262-264`).
- **Poison/OTP:** OTP messages are **not retryable** (`IsRetryable=false`) because
  their content is never persisted for security — the retry query filters
  `IsRetryable == true` (`WhatsAppMessageLogService.cs:143-144`,
  `WhatsAppMessageLogDocument.cs:81-87`).
- **Dead letter:** no dedicated DLQ collection; exhausted messages are left in
  `FAILED` with reason "Max retries exceeded" (`WhatsAppMessageRetryJob.cs:62-65`).
- **Ordering:** best-effort by `CreatedAtUtc`, not guaranteed
  (`WhatsAppMessageLogService.cs:163`).

---

## Circuit Breaker Runtime

- **Scope:** per **account** (`WhatsAppCircuitBreaker.cs`, keyed by `accountId`).
- **State machine:** CLOSED → OPEN → HALFOPEN → (success:CLOSED | failure:OPEN),
  stored in Redis (`WhatsAppCircuitBreaker.cs:33-202`).
- **Thresholds:** open after `FailureThreshold=5` failures; open duration
  `OpenStateDurationSeconds=60`; close after `HalfOpenTestRequests=2` successes
  (`WhatsAppCircuitBreaker.cs:16-19`).
- **Fail-safe:** Redis error → treated as OPEN to prevent message floods/bans
  (`WhatsAppCircuitBreaker.cs:58-67`).
- **Integration:** checked as gate #3; success/failure recorded post-send
  (`WhatsAppMessageDispatcher.cs:162-171,310,332,364`).

---

## Safety Runtime

- **Kill switch:** per-tenant Redis flag with 30-day TTL, set/cleared with an
  audited actor identity from JWT claims
  (`WhatsAppKillSwitch.cs:26-60`; controller extracts real admin identity
  `WhatsAppAdminController.cs:364-398`). Activation publishes
  `WhatsAppKillSwitchActivatedEvent` (`WhatsAppKillSwitch.cs:43-49`). It is the
  **first** gate and short-circuits to `SKIPPED`
  (`WhatsAppMessageDispatcher.cs:149-159`).
- **Emergency stop scope:** kill switch is **tenant-scoped only** — there is no
  global/platform WhatsApp kill switch in this runtime (key is
  `whatsapp:killswitch:{tenantId}`, `WhatsAppKillSwitch.cs:60`).
- **Tenant isolation:** every authorization path re-validates tenant lineage and
  scopes by `TenantId` (`WhatsAppDispatchAuthority.cs:42-57`).
- **Message blocking:** consent/opt-out (`WhatsAppRateLimitService`, SQL) and
  recipient cooldown (Redis) both block sends; OTP bypasses consent
  (`WhatsAppDispatchAuthority.cs:83-88`).
- **Provider blocking:** circuit breaker per account; disabled/disconnected
  accounts are refused at authorization (`WhatsAppDispatchAuthority.cs:49-62`).

---

## Analytics Runtime

- **Source:** aggregated on demand from the Mongo `WhatsAppMessageLogs` collection
  (`WhatsAppMessageLogService.GetAnalyticsAsync`,
  `WhatsAppMessageLogService.cs:209-248`).
- **Metrics:** total sent/failed/pending, by-type, by-status, and a computed
  success rate `sent/(sent+failed)` (`WhatsAppMessageLogService.cs:231-247`).
- **Realtime telemetry:** OTP dispatch success/failure is emitted to
  `CommerceReliabilityTelemetry` with a bounded error taxonomy to avoid
  unbounded metric cardinality (`WhatsAppMessageDispatcher.cs:156-168,
  219-220,325-328,351-356,404-454`).
- **No latency/delivery-time analytics** are computed (no DELIVERED timestamps
  exist to measure against).

---

## Logging Runtime

- **Delivery log:** the Mongo `WhatsAppMessageLogDocument` is the immutable(-by-
  intent) per-attempt audit record: masked phone, type, related entity,
  idempotency key, correlation id, attempts, lease, provider message id, failure
  reason (`WhatsAppMessageLogDocument.cs:1-88`). Full recipient phone IS stored
  in `targetPhone` alongside the masked copy (`WhatsAppMessageLogDocument.cs:21-28`).
- **Audit events:** kill switch activation and status changes flow through the
  event bus (`WhatsAppKillSwitch.cs:43-49`; `Events/WhatsApp/*`).
- **Retention/filtering:** admin log query supports paging + status/type filters
  (`WhatsAppMessageLogService.cs:176-207`;
  `WhatsAppAdminController.cs:312-345`). No automated log retention/TTL was found
  on the message-log collection (flagged as debt).

---

## Webhook Runtime

- **Finding: there is no WhatsApp webhook runtime in the backend.** No inbound
  webhook controller, no signature verification, no replay-protection, and no
  outbound callback registration exist for WhatsApp (grep of
  `PointOfSale.Infrastructure/WhatsApp` and WhatsApp controllers returns no
  webhook handler). Idempotency is achieved at the *log* layer (idempotency key,
  `WhatsAppMessageLogService.cs:20-42`), not at a webhook layer.
- The Baileys sidecar exposes an **HTTP command API** (send/qr/status/disconnect),
  API-key authenticated (`src/middleware/auth.ts`, `src/index.ts`), which is a
  request/response control plane — not a WhatsApp platform webhook. It does not
  push inbound-message events back to the C# backend.

---
## CQRS / Storage Model

The runtime is deliberately **poly-store**, mapping cleanly onto the platform rule
"PostgreSQL authoritative; Mongo/Redis derivative":

| Store | Role | What lives there | Evidence |
|---|---|---|---|
| PostgreSQL | **Authority (write model)** | `WhatsAppAccounts`, `WhatsAppMessageTemplates`, `WhatsAppRateLimits` (consent/opt-out), and all business truth read at dispatch (Orders, PaymentIntents, Tenants) | `WhatsAppAccount.cs`, `WhatsAppMessageTemplate.cs`, `WhatsAppRateLimit.cs`, `WhatsAppDispatchAuthority.cs:44-211` |
| MongoDB | **Derivative log / read model** | `WhatsAppMessageLogs` (attempt state machine, analytics source) | `WhatsAppMessageLogDocument.cs`; `WhatsAppMessageLogService.cs` |
| Redis | **Ephemeral operational state** | encrypted sessions, kill switch, circuit-breaker state, throughput counters/cooldowns | `WhatsAppSessionManager.cs`, `WhatsAppKillSwitch.cs`, `WhatsAppCircuitBreaker.cs`, `WhatsAppRateLimiter.cs` |

- **Projection usage:** unlike Product/CommerceHub, there is **no event-sourced
  projection consumer rebuilding a Mongo read model from SQL**. The Mongo log is
  written directly by the runtime (command-side) and read back for admin
  list/analytics. It is a *log*, not a rebuilt projection.
- **Freshness/consistency:** the critical consistency mechanism is **read-time
  SQL re-authorization** in `WhatsAppDispatchAuthority` — the Mongo log/event may
  be stale, so content and permission are recomputed from SQL immediately before
  send (`WhatsAppDispatchAuthority.cs:17-19,37-75`). This is the runtime's
  answer to eventual-consistency drift.
- **Write model:** commands = create/enable/disable account, CRUD template,
  create log + claim/complete dispatch, set/clear kill switch.
- **Read model:** admin account list, template list, message-log page, analytics
  aggregate, connection status.

---

## Runtime Authority Matrix

| Concept | Business Owner | Mutation Authority | Read Authority | Projection/Log Authority | Runtime Truth |
|---|---|---|---|---|---|
| WhatsApp account | This runtime | `WhatsAppAccountService` (SQL) | Admin API, dispatch authority | — | `WhatsAppAccounts` (PostgreSQL) |
| Connection status | This runtime | Status check + heartbeat jobs write SQL | Admin API | — | `WhatsAppAccounts.Status` + live transport |
| Template | This runtime | `WhatsAppAccountService` (SQL) | `WhatsAppTemplateResolver`, Admin API | — | `WhatsAppMessageTemplates` (PostgreSQL) |
| Message / delivery attempt | This runtime | `WhatsAppMessageLogService` (Mongo, lease-guarded) | Admin API, analytics | Mongo log | `WhatsAppMessageLogs` (MongoDB) |
| Delivery status | This runtime (send-side only) | Dispatcher via lease | Admin API | Mongo log | `SENT`/`FAILED`/… (no external receipt truth) |
| Consent / opt-out | This runtime | `WhatsAppRateLimitService` (SQL upsert) | Dispatch authority | — | `WhatsAppRateLimits` (PostgreSQL) |
| Throughput limits | This runtime | `WhatsAppRateLimiter` (Redis) | Dispatcher | — | Redis counters |
| Kill switch | This runtime | `WhatsAppKillSwitch` (Redis) | Dispatcher, Admin API | Event bus | Redis flag |
| Circuit state | This runtime | `WhatsAppCircuitBreaker` (Redis) | Dispatcher | — | Redis state |
| Order truth | Order domain | **NOT this runtime** | Read-only at dispatch | — | `Orders` |
| Payment truth | Payment lifecycle | **NOT this runtime** | Read-only at dispatch | — | `PaymentIntents`/`Orders.PaymentStatus` |
| Tenant truth | Tenant/Identity | **NOT this runtime** | Read-only (`TenantLineageAuthority`) | — | `Tenants` |

### Runtime Authority Divergence

- **Provider vs SQL status divergence** is explicitly reconciled: connection
  status is read from the live transport and *persisted back* to SQL when it
  differs (`WhatsAppAdminController.cs:246-256`). For Baileys, SQL is only marked
  Connected if the connected phone matches the configured phone
  (`WhatsAppAdminController.cs:229-238`).
- **Two rate-limit authorities coexist** (Redis throughput vs SQL consent) — see
  Technical Debt.

---

## Cross-Domain Authority Graph

| Peer domain | Reads | Writes | Events | Dependency / Sync |
|---|---|---|---|---|
| Orders | `Orders`, `Order.Items` for order-confirmation content & guest phone (`WhatsAppDispatchAuthority.cs:102-140`) | none | consumes order-confirmation trigger event | Read-only, fail-closed if order cancelled/refunded/deleted |
| Payments (lifecycle) | `PaymentIntents` + `Orders.PaymentStatus` to gate approved/rejected notifications (`WhatsAppDispatchAuthority.cs:142-211`) | none | consumes payment approval/approved/rejected events | Read-only; refuses to send unless intent+order status agree |
| Identity / OTP | renders/sends OTP; verification owned by OTP service | none to identity | consumes OTP-requested event | Content ephemeral, non-persisted, non-retryable |
| Customers / Storefront | guest phone from order; consent per phone (`WhatsAppRateLimits`) | consent/opt-out rows | — | Opt-out honored for non-OTP |
| Tenant / SuperAdmin | `TenantLineageAuthority.IsActiveTenantAsync`, `Tenant.UseBaileys`, `Tenant.Status/IsActive` | none | — | Fail-closed on inactive tenant |
| Notifications | independent of the generic notification runtime | none | own event namespace `Events/WhatsApp/*` | WhatsApp is a *separate* delivery channel, not the notification service |
| Abandoned Cart | provides an enabled account + templates + consent for recovery sends | consent rows | consumed by cart scanner job | Recovery uses SQL consent + 24h window |
| Reporting/Analytics | own Mongo-derived analytics | none | — | Self-contained |

- **Verified ZERO-authority peers** (no references found in the runtime):
  Accounting/GL, Inventory, Shipping writes, Treasury, Affiliate commissions,
  CommerceHub financial tables. The runtime touches **no money and no stock**.

---
## Business Invariants (implementation-backed)

1. **Only a transport-verified, connected, enabled account can be a send source.**
   Authorization requires `Enabled && Status==Connected`
   (`WhatsAppDispatchAuthority.cs:49-57`); enabling requires Connected
   (`WhatsAppAccountService.cs:99-102`).
2. **An account may only send a message type it owns.** `AccountOwnsMessageType`
   maps message type → `UseFor*` flag (`WhatsAppDispatchAuthority.cs:213-220`).
3. **At most one connected account owns a use case per tenant.**
   (`WhatsAppAccountService.cs:241-266`).
4. **Message content is derived from current SQL, not the event.**
   (`WhatsAppDispatchAuthority.cs:17-19,64-75`, dispatcher uses
   `authorized.MessageText`, `WhatsAppMessageDispatcher.cs:266-267`).
5. **A payment-approved WhatsApp message may only be sent if the PaymentIntent is
   Paid and the order payment status is Collected/Settled/PartiallyRefunded.**
   (`WhatsAppDispatchAuthority.cs:148-158`). Rejected requires intent Rejected +
   order PaymentStatus Rejected (`WhatsAppDispatchAuthority.cs:167-173`).
   Payment-approval-*request* type is never a direct send
   (`WhatsAppDispatchAuthority.cs:69`).
6. **Order-confirmation only for live orders with a guest phone.** Cancelled/
   refunded/deleted orders or empty phone → no send
   (`WhatsAppDispatchAuthority.cs:107-117`).
7. **Kill switch blocks all dispatch for the tenant.**
   (`WhatsAppMessageDispatcher.cs:149-159`).
8. **Open circuit fails the message (not skip).**
   (`WhatsAppMessageDispatcher.cs:162-171`).
9. **A message is dispatched at most once per lease; terminal states are never
   re-sent.** (`WhatsAppMessageLogService.cs:68-136`,
   `WhatsAppMessageDispatcher.cs:107-124`).
10. **OTP content is never persisted and never retried.**
    (`WhatsAppMessageLogDocument.cs:81-87`, `WhatsAppMessageLogService.cs:143-144`,
    dispatch authority requires ephemeral text `WhatsAppDispatchAuthority.cs:70-72`).
11. **Consent/opt-out blocks non-OTP messages; OTP bypasses consent.**
    (`WhatsAppDispatchAuthority.cs:83-88`).
12. **Provider failure never mutates business data** — dispatcher only writes the
    log/circuit/rate-limit state; no order/payment write path exists.
13. **Inactive tenant cannot send.**
    (`WhatsAppDispatchAuthority.cs:44-47`; `WhatsAppAccountService.cs:42-45`).
14. **Enabling requires no conflicting use-case owner** even after the fact
    (`WhatsAppAccountService.cs:104-107,241-266`).

---

## Architectural Decisions (WHY)

- **Why SQL re-derivation at dispatch?** Because triggers are asynchronous events
  and Mongo logs are snapshots; an order can be cancelled or a payment reversed
  between enqueue and send. Rebuilding from SQL guarantees the platform never
  sends a message that current truth no longer justifies
  (`WhatsAppDispatchAuthority.cs:17-19`).
- **Why two providers with a feature gate and no fallback?** Strangler-Fig
  migration from browser-driven Playwright to the lighter Baileys sidecar.
  Explicit routing (no auto-fallback) makes rollout observable and rollback
  instant via `Tenant.UseBaileys`, and avoids silently sending through an
  untested path (`WhatsAppMessageDispatcher.cs:30-40,288-293`).
- **Why polling/heartbeat instead of provider webhooks?** WhatsApp Web/Baileys
  sessions are long-lived socket connections without a first-class delivery
  webhook to this backend; the runtime therefore polls status and runs a 5-min
  heartbeat + daily cleanup to detect and repair dead sessions
  (`HangfireInitializationHostedService.cs:217-230`).
- **Why a circuit breaker?** WhatsApp aggressively bans numbers that flood during
  provider instability; per-account breaking + Redis-fail-safe-OPEN trades
  deliverability for ban avoidance (`WhatsAppCircuitBreaker.cs:29-67`).
- **Why retries capped at 3 with no backoff, and OTP excluded?** Transactional
  value decays quickly and OTP secrets must not persist; bounded retries limit
  spam/ban risk (`WhatsAppMessageRetryJob.cs:17,62-65`;
  `WhatsAppMessageLogDocument.cs:81-87`).
- **Why kill switch is tenant-scoped and audited?** Operators need a per-tenant
  break-glass with accountability; identity is pulled from JWT claims
  (`WhatsAppAdminController.cs:364-398`).
- **Why analytics separated into the log store?** Delivery analytics are
  high-volume append-only audit data unsuited to the relational authority store;
  they live in Mongo and are aggregated on read (`WhatsAppMessageLogService.cs:209-248`).

---

## Message Lifecycle (end to end)

```
Business event (Order confirmed / OTP requested / Payment approved|rejected)
  -> Event published (IEventPublisher)
  -> WhatsApp consumer creates WhatsAppMessageLogDocument (PENDING, idempotency key)
  -> Dispatch enqueued (DispatchAsync)
  -> Dispatcher: terminal-state check
  -> Lease claim (PENDING/QUEUED/FAILED -> DISPATCHING, +1 attempt, 2-min lease)
  -> Kill switch gate            (killed -> SKIPPED)
  -> Circuit breaker gate        (open   -> FAILED)
  -> SQL RE-AUTHORIZATION        (stale/invalid -> SKIPPED; rebuilds MessageText)
  -> Phone normalization (E.164) (invalid -> FAILED + WhatsAppMessageFailedEvent)
  -> Messaging eligibility/consent (opted out -> SKIPPED; OTP bypasses)
  -> Throughput rate limit        (exceeded -> FAILED + event; transactional bypass cooldown)
  -> Jitter delay
  -> Transport routing (UseBaileys ? Baileys : Playwright ; NO fallback)
  -> Send
       success -> SENT (+ providerMessageId) + record rate/circuit + WhatsAppMessageSentEvent
       failure -> FAILED or DELIVERY_UNKNOWN (ambiguous) + circuit failure + WhatsAppMessageFailedEvent
       exception-> DELIVERY_UNKNOWN + circuit failure (no throw; retry job handles)
```

| Stage | Runtime Owner | Authority | Sync | Failure behaviour | Recovery | Consistency |
|---|---|---|---|---|---|---|
| Trigger event | Order/Payment/Identity domains | Domain SQL | async bus | event lost → no send | domain re-emit | at-least-once |
| Log creation | Message log service | Mongo | sync | duplicate key → returns existing | idempotency key | idempotent |
| Lease claim | Message log service | Mongo atomic | sync | already claimed/terminal/exhausted → no-op | lease expiry re-claim | single-dispatch |
| Kill switch | Kill switch (Redis) | Redis | sync | SKIPPED | operator revive | fail-closed |
| Circuit | Circuit breaker (Redis) | Redis | sync | FAILED / OPEN | half-open probes | fail-safe |
| SQL re-auth | Dispatch authority | PostgreSQL | sync | SKIPPED | n/a (correct by design) | strong, read-time |
| Normalize | Phone normalizer | code | sync | FAILED + event | manual data fix | deterministic |
| Consent | Rate-limit service | PostgreSQL | sync | SKIPPED | opt-in reset | strong |
| Rate limit | Rate limiter | Redis | sync | FAILED + event | window/TTL expiry | eventual |
| Send | Transport (Baileys/Playwright) | provider | sync | FAILED/DELIVERY_UNKNOWN | retry job (≤3) | at-least-once, no receipt |
| Status update | Message log service | Mongo lease-guarded | sync | lease mismatch → no overwrite | — | monotonic per lease |

---

## Account Lifecycle (end to end)

```
Create (PendingQR, Enabled=false; active tenant required)
  -> Configure use-case flags (UseFor*)
  -> Connect: GET qr -> scan (Baileys sidecar / Playwright engine)
  -> Authenticate: session persisted (Redis encrypted / sidecar auth state)
  -> Status check -> Status=Connected persisted to SQL (Baileys: phone must match)
  -> Enable (allowed only if Connected AND no conflicting use-case owner) -> Ready
  -> Heartbeat job monitors; Disconnect on socket loss -> Status=Disconnected
  -> Reconnect (auto via session; or re-QR if session lost/expired 30d TTL)
  -> Deactivate (ToggleAccountStatus false) / Banned (WhatsApp) / phone-change resets to PendingQR
  -> Delete (transport disconnect + hard delete row)
```

| Stage | Authority | Validation | Failure behaviour | Recovery |
|---|---|---|---|---|
| Create | `WhatsAppAccountService` | active tenant | throws if tenant inactive | fix tenant |
| Configure | `WhatsAppAccountService`/controller | use-case ownership on enable | conflict → throws | pick different account |
| Connect/QR | transport | account exists | QR error surfaced | re-request QR (60s TTL) |
| Authenticate | session manager / sidecar | session encrypt/persist | decrypt fail → lost session | re-scan QR |
| Ready/Enable | `WhatsAppAccountService` | Status==Connected | BadRequest if not connected | connect first |
| Disconnect | heartbeat/monitor | — | Status=Disconnected | auto-reconnect |
| Phone change | `WhatsAppAccountService` | — | resets PendingQR + disable | re-scan |
| Delete | controller + service | — | disconnect then remove | n/a |

---

## WhatsApp Decision Matrix

| Decision | Business Rule | Runtime Owner | Authority | Validation Layer | Failure Behaviour | Evidence |
|---|---|---|---|---|---|---|
| Can a disconnected account send? | No | Dispatch authority | SQL | authorize | `null` → SKIPPED | `WhatsAppDispatchAuthority.cs:49-57` |
| Can a disabled account send? | No | Dispatch authority | SQL | authorize | SKIPPED | `WhatsAppDispatchAuthority.cs:49-57` |
| Can an account send a type it doesn't own? | No | Dispatch authority | SQL | authorize | SKIPPED | `WhatsAppDispatchAuthority.cs:59,213-220` |
| Can disabled/absent template block send? | No — code fallback template used | Template resolver | code | resolve | fallback content | `WhatsAppTemplateResolver.cs:31-43,65-74` |
| Can retries bypass the kill switch? | No | Dispatcher | Redis | gate #1 | SKIPPED each attempt | `WhatsAppMessageDispatcher.cs:149-159` |
| Can delivery mutate the message content? | No — content rebuilt each attempt | Dispatch authority | SQL | authorize | recomputed | `WhatsAppDispatchAuthority.cs:64-75` |
| Can a provider failure lose the message? | No (ambiguous) — DELIVERY_UNKNOWN + retry | Dispatcher/retry | Mongo | send | retriable/terminal-safe | `WhatsAppMessageDispatcher.cs:333-336,359-371` |
| Can one tenant use another tenant's account? | No | Dispatch authority | SQL tenant scope | authorize | SKIPPED | `WhatsAppDispatchAuthority.cs:42-57` |
| Can multiple accounts send the same use case simultaneously? | No (one connected owner) | Account service | SQL | enable | throws on conflict | `WhatsAppAccountService.cs:241-266` |
| Can a message send twice? | No (lease + idempotency) | Log service | Mongo | claim/complete | single-dispatch | `WhatsAppMessageLogService.cs:68-136` |
| Can OTP be retried from stored content? | No | Log service | Mongo | retry filter | excluded | `WhatsAppMessageLogService.cs:143-144` |
| Can a message send if payment truth disagrees? | No | Dispatch authority | SQL | authorize | SKIPPED | `WhatsAppDispatchAuthority.cs:148-173` |
| Can Redis outage flood WhatsApp? | No — circuit fails OPEN | Circuit breaker | Redis | gate #2 | blocked | `WhatsAppCircuitBreaker.cs:58-67` |
| Can admin payment-review be delivered? | No (dropped) — authority returns null + no admin phone | Dispatch authority | SQL | authorize | SKIPPED | `WhatsAppDispatchAuthority.cs:69`, `PaymentService.cs:3460` |

---
## Technical Debt (implementation-backed)

1. **Dual, uncoordinated rate-limit authorities.** Throughput limiting lives in
   Redis (`WhatsAppRateLimiter`, tenant-daily/account-hourly/recipient-cooldown,
   `WhatsAppRateLimiter.cs:24-105`) while consent + a 24h per-recipient message
   window lives in PostgreSQL (`WhatsAppRateLimitService` /
   `WhatsAppRateLimit`, `WhatsAppRateLimitService.cs:28-163`). They use different
   windows/limits and are not reconciled — a recipient can be "allowed" by one and
   "blocked" by the other. Confusing dual source of truth.
2. **Rate-limit constant vs comment drift.** `WhatsAppRateLimit.MaxMessagesPerWindow`
   is `25` but the doc-comment says "Increased from 3 to 10" and the class summary
   says "Max 3 recovery messages" (`WhatsAppRateLimit.cs:7-8,53-57`). Comments are
   stale relative to code.
3. **Transactional-bypass type list is string-fragile.** The cooldown bypass
   compares against a hardcoded `{ORDER_CONFIRMATION, SHIPMENT_*}` list with ad-hoc
   underscore normalization (`WhatsAppRateLimiter.cs:54-57`), while message types
   elsewhere are `OTP`, `OrderConfirmation`, `PAYMENTAPPROVED`, etc. Mismatched
   naming conventions risk a transactional message being wrongly cooled down.
4. **Payment notification text is hardcoded, not templated.** Approved/rejected
   copy is string-literal in the dispatch authority
   (`WhatsAppDispatchAuthority.cs:156-158,178`), bypassing the template runtime and
   its localization/validation — inconsistent with order-confirmation/OTP.
5. **Template validation only supports two types.** `NormalizeAndValidateTemplate`
   throws for any type other than `OrderConfirmation`/`OTP`
   (`WhatsAppAccountService.cs:274-282`), yet accounts advertise payment-approval
   and abandoned-cart use cases — those have no tenant-editable templates.
6. **No delivery/read receipt ingestion.** `providerMessageId` is stored but never
   reconciled; `DELIVERED/READ/EXPIRED` states are defined conceptually but never
   set. Delivery "success" is provider-accept only
   (`WhatsAppMessageDispatcher.cs:306-323`).
7. **No message-log retention/TTL.** The Mongo `WhatsAppMessageLogs` grows
   unbounded; only a session-cleanup job (not a log-cleanup job) exists
   (`HangfireInitializationHostedService.cs:227-230`).
8. **Full recipient phone stored in plaintext** in `targetPhone` alongside the
   masked copy (`WhatsAppMessageLogDocument.cs:21-28`) — PII at rest without a
   documented retention/redaction policy.
9. **Retry has no backoff and coarse ordering.** Fixed 5-min cadence + jitter only;
   ordering is best-effort by `CreatedAtUtc` (`WhatsAppMessageRetryJob.cs`,
   `WhatsAppMessageLogService.cs:163`). Under load, newer transactional messages can
   queue behind older stuck ones.
10. **`MapBaileysStatusToAccountStatus` appears unused.** Defined in the controller
    (`WhatsAppAdminController.cs:261-271`) but status mapping is done inline
    (`WhatsAppAdminController.cs:229-238`) — dead/duplicate mapping code.
11. **Two transports, doubled surface area.** Playwright engine (~775 lines) and the
    Baileys sidecar coexist indefinitely; the Strangler-Fig migration has no
    code-level end-state marker, so both must be maintained (build error artifact
    `build_error_whatsapp.txt` present in repo).
12. **Kill switch is tenant-only.** No platform-global WhatsApp emergency stop; a
    provider-wide incident must be mitigated tenant-by-tenant
    (`WhatsAppKillSwitch.cs:60`).
13. **`WhatsAppOtpService` (Redis) is dormant.** Registered (`Program.cs:1306`)
    with zero callers; OTP generation/storage lives in
    `StorefrontPhoneVerificationService` + `CheckoutPhoneOtpStore`. Dead code and
    a misleading service name (`WhatsAppOtpService.cs`,
    `StorefrontPhoneVerificationService.cs:428-476`); the consumer's redaction
    comment even references "the WhatsAppOtpService" as if it feeds the flow.
14. **Admin `PaymentApprovalRequest` is silently dropped at dispatch.** Dispatched
    with no inline text; the dispatcher's authority returns `null` for that type
    (`WhatsAppDispatchAuthority.cs:69`), so it is marked `SKIPPED` — no admin
    payment-review WhatsApp is ever delivered (compounded by
    `AdminPhoneNumber=null`, `PaymentService.cs:3460`). Latent functional gap.
15. **Asymmetric PII handling** between payment events: `Approved` is
    `ISensitiveIntegrationEvent`+`PublishSensitiveAsync` while `Rejected` is plain
    `PublishAsync` despite carrying the same `CustomerPhoneNumber`
    (`WhatsAppPaymentApproved/RejectedNotificationEvent.cs:9,13`). Inconsistent
    sensitive-data treatment.
16. **All payment consumers absorb all exceptions** (consumer + service layers
    never rethrow), guaranteeing non-blocking behaviour but making silent
    failures hard to detect — debugging relies on ad-hoc "*** INVOKED ***" logs.
17. **Playwright transport is production-fragile.** `Headless=false` (needs a
    visible browser) is a latent blocker on headless servers
    (`PlaywrightWhatsAppEngine.cs:166`); it has no reconnect/self-heal/poison
    handling and returns a **fabricated** `Guid.NewGuid()` message id (`:374`),
    so delivery acks under Playwright cannot be trusted. The C#
    `IWhatsAppSessionManager` Redis blob is never written by Playwright (zero
    callers) — dead relative to this transport.
18. **Baileys media/template stubs.** `sendMediaMessage`/`sendTemplateMessage` are
    "not yet implemented" (`MessageSender.ts:617-633`), so the C#
    `Media`/`Template` enum values are silently unsupported — the interface
    advertises capabilities the sidecar cannot deliver.
19. **Default sidecar encryption secret is a known placeholder**
    (`config/index.ts:39`), only enforced when `NODE_ENV=production`; in dev the
    WhatsApp MD auth-state encryption key is weak/known.
20. **Session-cleanup job is inert/mislabeled.** It never cleans
    `whatsapp:session:*`/`whatsapp:auth:*`; `cleanedSessions` actually counts OTP
    deletes, and `_sessionManager` is injected but unused
    (`WhatsAppSessionCleanupJob.cs:45-73`).
21. **Inconsistent QR/timeout values.** Node polls QR up to 10s while C# caps at
    15s but the client's own 10s `HttpClient` timeout can pre-empt the QR GET
    (`BaileysWhatsAppTransport.cs:40-41,206`, `sessions.ts:233`).

## Implementation Follow-up Items

| # | Title | Current Implementation | Why | Future Improvement | Priority | Status |
|---|---|---|---|---|---|---|
| 1 | Unify rate-limit authority | Redis throughput + SQL consent, unreconciled | Ambiguous send decisions | Single policy service reconciling both | High | Open |
| 2 | Template-drive payment copy | Hardcoded strings in dispatch authority | No localization/validation | Route through template runtime | Medium | Open |
| 3 | Delivery-receipt ingestion | None; SENT is terminal | No true delivery/read visibility | Ingest provider acks/receipts, add DELIVERED/READ | Medium | Open |
| 4 | Message-log retention | Unbounded Mongo growth + plaintext phone | Cost + PII risk | TTL index + phone redaction policy | High | Open |
| 5 | Retry backoff & priority | Fixed 5-min, best-effort order | Head-of-line blocking | Exponential backoff + type priority | Medium | Open |
| 6 | Retire a transport | Both Playwright + Baileys live | Double maintenance | Complete Baileys cutover, remove Playwright | Medium | Open |
| 7 | Global kill switch | Tenant-scoped only | Slow incident response | Add platform-global emergency stop | Low | Open |
| 8 | Fix stale comments/constants | 3/10/25 mismatch, dead mapper | Misleads maintainers | Align comments to code, delete dead code | Low | Open |
| 9 | Extend template validation | Only 2 types validatable | Payment/cart have no templates | Support all advertised use cases | Low | Open |
| 10 | Wire or remove dormant OTP service | `WhatsAppOtpService` registered, zero callers | Misleads maintainers | Either use it in delivery or delete it | Medium | Open |
| 11 | Fix dead admin payment-review path | `PaymentApprovalRequest` SKIPPED by authority; `AdminPhoneNumber=null` | No admin WhatsApp notification ever sent | Add authority case + admin phone source | High | Open |
| 12 | Normalize payment-event PII handling | `Approved` sensitive vs `Rejected` plain | Inconsistent sensitive-data treatment | Both `ISensitiveIntegrationEvent` | Medium | Open |

---

## Architectural Synthesis — What This Runtime Actually Is

Reverse engineering the implementation, forcing this into "messaging service" or
"notification service" would be wrong. The evidence points to a specific,
narrower architectural role:

**A tenant-scoped, provider-abstracted, fail-closed OUTBOUND DELIVERY RUNTIME
whose distinguishing invariant is read-time SQL re-authorization of message
content.**

The three properties that define it:

1. **Provider adapter at the edge.** It is *partly* a provider adapter — two
   interchangeable transports behind one interface, selected per tenant with no
   fallback (`IWhatsAppTransport`, `WhatsAppMessageDispatcher.cs:288-293`). But it
   is more than an adapter, because it does not merely forward calls.
2. **A safety/governance runtime around delivery.** Kill switch, per-account
   circuit breaker (Redis-fail-safe-OPEN), dual rate limiting, consent/opt-out,
   idempotency and single-dispatch leases make delivery a *governed* operation,
   not a passthrough (`WhatsAppMessageDispatcher.cs:149-260`,
   `WhatsAppCircuitBreaker.cs`, `WhatsAppKillSwitch.cs`).
3. **A SQL-authority projection engine for outbound communication.** The single
   most important architectural decision is that the message is *not* the event —
   `WhatsAppDispatchAuthority` rebuilds the payload and re-checks permission from
   PostgreSQL at the instant of send, treating the event and Mongo log as
   non-authoritative "select work only" (`WhatsAppDispatchAuthority.cs:17-19`).
   This is the same "SQL authoritative; Mongo/Redis derivative; fail closed"
   discipline the platform applies to CommerceHub/Product, expressed for an
   outbound comms channel.

It is therefore **NOT** the platform notification service (it has its own event
namespace, stores, and gating and is a distinct channel), **NOT** a workflow
engine (no long-running orchestration/state machine over business processes —
the only state machines are the per-attempt delivery log and the circuit
breaker), and **NOT** a general messaging bus. It is best named the
**WhatsApp Outbound Delivery Runtime with SQL-Authority Re-Derivation.**

Its role inside Shopixy: it is the **governed WhatsApp edge** — the platform's
only sanctioned way to push a bounded set of transactional messages to customers
over WhatsApp, engineered primarily to (a) never send something current truth
does not justify, and (b) never get the tenant's number banned.

---

## Certification

- **Confidence: High** for the C# dispatch/authority/account/template/safety/
  retry/log core, the storage model, the authority matrix, invariants, and
  lifecycles, plus the consumer/event/OTP/payment chains — all verified by direct
  reads with `file:line` citations (core by me, transport/consumer depths by two
  dedicated subagents whose reports corroborate the same conclusions).
- **Confidence: Medium-High** for the provider/transport internals (Playwright
  engine + Baileys sidecar reconnect/self-healing/heartbeat specifics): the
  dispatch-side contracts, routing, DI, Hangfire schedule, sidecar route surface,
  and send-only inbound posture are directly verified; deeper per-file transport
  mechanics were additionally reverse-engineered by a dedicated subagent and fold
  into the same conclusions.
- **Completeness:** All requested sections are present. Frontend
  (`PointOfSale.Web/src/modules/whatsapp`) intentionally excluded.
- **Verified absences (not fabricated):** no WhatsApp webhook/receipt runtime,
  no inbound-message ingestion, no delivery/read-receipt reconciliation, no
  platform-global kill switch, no template versioning/approval, no message-log
  retention job.
- **Primary evidence files:** `WhatsAppDispatchAuthority.cs`,
  `WhatsAppMessageDispatcher.cs`, `WhatsAppMessageLogService.cs`,
  `WhatsAppAccountService.cs`, `WhatsAppCircuitBreaker.cs`, `WhatsAppKillSwitch.cs`,
  `WhatsAppRateLimiter.cs`, `WhatsAppRateLimitService.cs`,
  `WhatsAppTemplateResolver.cs`, `WhatsAppSessionManager.cs`,
  `WhatsAppFeatureGateService.cs`, `WhatsAppMessageRetryJob.cs`,
  `WhatsAppMessageLogDocument.cs`, `WhatsAppAccount.cs`, `WhatsAppMessageTemplate.cs`,
  `WhatsAppRateLimit.cs`, `WhatsAppAdminController.cs`, `IWhatsAppTransport.cs`,
  `Program.cs`, `HangfireInitializationHostedService.cs`,
  `PointOfSale.WhatsApp.Baileys/src/*`.
