# Shopixy AI QA Onboarding — Core QA Philosophy

Audience: a Senior QA Engineer, human or AI, joining Shopixy / PointOfSale.

This guide is different from the domain guides. It does not teach architecture, inventory, payments, orders, security, or platform mechanics. Those are covered by the previous onboarding guides.

This guide teaches how a QA engineer should think when testing Shopixy.

It contains durable principles only. It intentionally avoids temporary implementation details, checklist-style coverage, and repeated domain rules. When a domain-specific fact is needed, use the owning guide listed in Source Documents. When documentation conflicts, prefer `.ai-context/*`; if the conflict remains unresolved, document it instead of guessing.

# Purpose

The purpose of this guide is to give QA a durable reasoning model for Shopixy.

Shopixy is not a simple UI over independent features. It is a multi-tenant commerce operating platform where one customer action can touch identity, tenant scope, order lifecycle, inventory, payment, refund, ledger, projection, provider evidence, and recovery behavior.

A QA engineer therefore cannot reason only from what a screen shows or what one API response says. The core QA question is always:

What is the authority, what is merely evidence, and what should the system do when the evidence is late, stale, missing, duplicated, contradictory, or scoped incorrectly?

This guide exists so QA engineers and AI agents test the platform as a system of authorities, not as a set of happy-path feature screens.

# The Mission of QA at Shopixy

QA exists to protect the integrity of the platform under uncertainty.

Shopixy accepts that distributed systems are imperfect:

- read models lag;
- caches expire late;
- webhooks arrive twice, late, or out of order;
- providers report success after local state changed;
- customers retry, refresh, double-click, and replay stale browser state;
- operators repair projections during incidents;
- tenants share infrastructure while requiring strict isolation;
- financial operations may be partially external and locally unresolved.

QA's mission is not to prove that the normal path looks correct once. QA's mission is to prove that the platform remains safe when reality is messy.

A strong Shopixy QA engineer protects:

- business truth from derivative state;
- tenant boundaries from client claims and projection leaks;
- money from double-counting, false finality, and unsafe outward movement;
- inventory from stale reservations and resurrected stock visibility;
- lifecycle owners from direct mutation shortcuts;
- recovery paths from becoming shadow authorities;
- accepted tradeoffs from being misreported as defects;
- real defects from being dismissed as eventual consistency.

The highest-value QA work is adversarial: it asks what happens when every non-authoritative signal is wrong at the worst possible time.

# Core QA Philosophy

## Authority before behavior

Do not start with “what did the UI do?” Start with “who was allowed to decide?”

A behavior that appears correct can still be a bug if it bypassed the authority boundary. A behavior that appears inconvenient can be correct if the authority boundary intentionally failed closed.

At Shopixy, the first QA move is always to identify the authority owner before judging behavior.

## Evidence is not truth

Evidence may be useful, but it is not automatically authoritative.

Evidence includes tokens, claims, browser state, frontend validation, provider callbacks, webhook payloads, carrier events, queue messages, Mongo documents, Redis values, Elasticsearch documents, dashboards, reports, and logs.

Evidence becomes meaningful only after the correct backend authority admits it. Until then, it is a candidate signal.

## PostgreSQL decides internal business truth

The SQL commit boundary is the internal durability boundary. PostgreSQL is the authoritative write model for durable business truth.

Derivative systems may accelerate reads, discovery, reporting, or operations, but they do not decide final business truth.

If a projection and PostgreSQL disagree, QA should assume PostgreSQL wins unless an authoritative source explicitly says otherwise.

## Fail closed is a feature

Shopixy prefers integrity over availability when truth is missing, stale, contradictory, or externally uncertain.

A fail-closed outcome can be correct even if it blocks a customer, pauses payout, withholds a projection, rejects checkout, or requires manual review.

QA must distinguish a protective block from a product defect. The block is a defect only when the owning authority says the action should have been allowed and the required proof existed.

## Test uncertainty, not only success

The important failures are not usually ordinary validation errors. They are uncertainty cases:

- external provider outcome unknown;
- webhook arrives after terminal local state;
- projection missing while SQL exists;
- duplicate event tries to replay side effects;
- stale cache claims visibility that SQL removed;
- tenant scope is supplied by a client instead of trusted resolution;
- checkout evidence changes during the final transaction;
- recovery job finds a candidate whose current SQL authority has changed.

A Shopixy test is strongest when it proves deterministic convergence under these conditions.

## Protect money above convenience

Money-related behavior carries the highest integrity burden.

Provider success is evidence, not local financial truth. Refund execution may be externally uncertain. Payout readiness may be blocked by unresolved external money. Ledger truth is SQL-authoritative. Reconciliation states are intentional.

QA should treat false financial finality, duplicate recognition, unsafe outward money, and hidden external-money ambiguity as severe even when the UI impact is small.

## Protect tenant boundaries above feature completion

A cross-tenant leak is a critical failure even if the leaked action looks minor.

Tenant scope is not established by a raw header, route id, projection id, document id, token claim, or frontend selector. It is proven by trusted backend resolution plus current SQL lineage and, where relevant, tenant co-ownership of referenced records.

A feature that works only because it trusts caller-supplied tenant scope is not working.

## Protect lifecycle owners

Payment state, order state, fulfillment state, inventory state, refund state, payout state, identity/session authority, and ledger effects have owners.

Direct writes, bulk updates, projection repair shortcuts, raw bus publishes, or operational scripts that bypass those owners may produce the “right” screen while corrupting the architecture.

QA should report lifecycle bypass as an authority violation, not as a harmless implementation choice.

## Projections converge; they do not command

MongoDB, Redis, Elasticsearch, search overlays, cache hydration, read-model repair, and rebuild tooling are convergence mechanisms.

They may discover stale or missing state, but they must re-enter current authority before mutating derivative visibility.

A stale projection is not automatically a bug. A stale projection that becomes authority, resurrects visibility, hides SQL truth, leaks tenant data, or creates financial/order/inventory effects is a bug.

## Accepted tradeoffs are part of the design

Normal read-model lag, fail-closed financial gating, explicit reconciliation states, manual review, suspense, quarantine, and temporary unavailability can be intentional.

QA should not re-report accepted tradeoffs as bugs. QA should instead verify that the tradeoff remains contained, observable, and aligned with the documented authority boundary.

# QA Decision Hierarchy

When investigating any behavior, reason in this order:

Authority
↓
Identity
↓
Tenant scope
↓
Lifecycle ownership
↓
Money
↓
Inventory
↓
Events and recovery
↓
Projection and cache
↓
UI and presentation

## 1. Authority

Ask: What system or service is allowed to decide this fact?

Without authority, every later observation is ambiguous. A UI response, provider callback, or projection document cannot be judged until QA knows whether it is authority or evidence.

Authority comes first because it prevents false bug reports based on derivative lag and prevents missed bugs where the visible outcome looks correct but came from the wrong source.

## 2. Identity

Ask: Who is the actor, and is the actor still valid right now?

Tokens, cookies, roles, claims, frontend route guards, and stored sessions are evidence only. Current backend authority decides whether the actor still exists, still has the role, still belongs to the tenant, and still has an active session.

Identity comes before tenant scope because a tenant claim from an invalid actor is not useful proof.

## 3. Tenant scope

Ask: Which tenant or platform scope is proven by trusted resolution and current SQL lineage?

A valid `TenantId` value is necessary but not sufficient. The referenced objects must also belong to the same tenant where the operation requires co-ownership.

Tenant scope comes before lifecycle because a lifecycle transition in the wrong tenant is not a valid transition; it is a security failure.

## 4. Lifecycle ownership

Ask: Which lifecycle owner is allowed to mutate the protected state?

Order, payment, fulfillment, refund, return, inventory, payout, identity, and accounting transitions cannot be treated as ordinary field updates.

Lifecycle ownership comes before money and inventory because money and stock effects must be consequences of authorized lifecycle decisions, not independent side effects.

## 5. Money

Ask: Could this create, hide, duplicate, release, refund, settle, or misclassify money?

Money has priority because financial corruption may be invisible in the UI while damaging customers, tenants, vendors, affiliates, and the platform.

Under uncertainty, expect explicit states such as processing, suspense, quarantine, manual review, or reconciliation required. Do not force certainty into the test oracle unless the authority proves certainty.

## 6. Inventory

Ask: Is stock or reservation authority current, lineage-backed, tenant-correct, and safe under replay?

Inventory comes after money in the hierarchy because some inventory release/commit decisions are unsafe while payment truth is unresolved.

QA should treat raw stock rows and reservation rows as evidence until current product, variant, warehouse, stock, and reservation lineage proves consumable authority.

## 7. Events and recovery

Ask: If the event, webhook, outbox message, or recovery candidate is duplicated, delayed, reordered, or stale, does it converge safely?

Events and recovery are trigger mechanisms. They should collapse duplicates, reload current SQL truth, preserve aggregate ordering where required, and fail closed when current authority rejects the candidate.

## 8. Projection and cache

Ask: Is the read model allowed to lag, and does it converge from authority?

Projection/caching issues are bugs only when they violate authority: false absence on critical reads, stale resurrection, cross-tenant leakage, unsafe visibility, or derivative mutation without current SQL proof.

## 9. UI and presentation

Ask: Did the UI present the authoritative state clearly and safely?

UI is last because it is not the source of truth. However, UI can still be a bug when it misleads users about non-terminal states, hides reconciliation, claims success without authority, or allows unsafe actions that the backend must later reject.

# Thinking Process

## How to investigate a bug

A Shopixy QA investigation should move from authority to symptom, not symptom to guess.

1. State the observed behavior neutrally.

   Avoid early labels like “payment failed” or “order missing.” Prefer: “The storefront displayed a failure message after a provider callback,” or “The order detail page returned 404 while SQL may contain an order.”

2. Identify the domain and owning guide.

   Decide whether the issue primarily touches architecture, inventory, payments, orders, security/identity, or platform relationships. Use the owning guide for domain rules.

3. Identify the authority owner.

   Determine what SQL authority, lifecycle owner, or platform boundary decides the disputed fact.

4. Separate authority from evidence.

   List each observed signal and classify it: SQL authority, admitted lifecycle state, external evidence, derivative projection, cache, event, log, browser state, or UI presentation.

5. Reconstruct the timeline.

   Order matters. Identify the SQL commit, external handoff, webhook arrival, outbox delivery, projection update, retry, recovery action, and UI read timing. Do not assume global event ordering; reason per aggregate where applicable.

6. Test current truth, not stale memory.

   Re-read current authority before drawing conclusions. If a projection says one thing and current SQL says another, determine whether the projection is allowed to lag or whether it should have repaired/tombstoned.

7. Apply fail-closed expectations.

   Ask whether the system had enough proof to allow the action. If not, a block, no-op, tombstone, retry, manual review, or reconciliation state may be correct.

8. Decide classification.

   Use durable categories: authority violation, tenant boundary violation, financial corruption, lifecycle violation, projection bug, performance, UX, or expected tradeoff.

9. State the smallest defensible claim.

   Report what evidence proves. Distinguish confirmed defects from theoretical risks and accepted tradeoffs.

## How to collect evidence

Collect evidence from the authority outward:

- current SQL rows and state transitions;
- lifecycle/audit/evidence rows;
- event/outbox/inbox/replay records;
- logs and telemetry;
- provider callback or external evidence;
- projections and caches;
- browser/network/UI behavior.

Do not let the easiest evidence become the strongest evidence. Browser screenshots and logs are useful, but they rarely decide business truth.

## How to decide whether something is a bug

A behavior is a bug when it violates a documented authority boundary, lifecycle rule, tenant boundary, financial invariant, inventory invariant, replay/idempotency rule, or presentation safety expectation.

A behavior is an accepted tradeoff when the documentation explicitly permits it, the system remains within the authority boundary, and the tradeoff is contained.

A behavior is unresolved when the evidence is insufficient to determine authority. In that case, QA should report the uncertainty and identify the missing proof rather than guessing.

## How to handle documentation conflicts

Use this precedence:

1. `.ai-context/*` authority files;
2. domain-specific `.ai/hermes/*` guide for the owning area;
3. certification/freeze documents under `docs/architecture/*`;
4. code and tests as evidence of current behavior;
5. comments, README, stale prose, and UI assumptions last.

If two durable sources disagree, do not silently choose the convenient one. State the conflict, name the sources, explain which source has higher authority, and identify what QA should not assume until the conflict is resolved.

# Evidence Collection

Evidence is not equal. QA should know what each evidence source can and cannot prove.

## SQL

SQL is the primary internal authority for business truth.

Use SQL to prove durable state: tenant lineage, order existence, payment status, refund state, ledger entries, inventory stock/reservations, fulfillment state, identity/session authority, outbox intent, and reconciliation rows.

SQL is strongest when it is current, scoped, and interpreted through the owning domain rules. A raw row is not always enough: for example, a stock row or reservation row may still require current lineage proof before it is consumable authority.

## Logs

Logs are diagnostic evidence.

Logs can prove that a path ran, failed, retried, rejected authority, collapsed replay, or emitted telemetry. Logs do not override SQL authority and should not be used to create business truth.

Logs are especially useful for fail-closed behavior because a correct no-op or tombstone may otherwise look like “nothing happened.”

## Events

Events are trigger and ordering evidence.

They can prove that a mutation staged an integration intent, that a consumer received a message, or that replay occurred. Events do not replace the SQL commit that created the intent.

A delayed or replayed event should re-enter current authority. QA should test whether duplicate events collapse and stale events converge safely.

## Browser

Browser evidence is presentation evidence.

Screenshots, DOM state, LocalStorage, route state, React/Zustand state, network responses, disabled buttons, and frontend guards show what the user experienced. They do not prove backend authority.

Browser evidence is still important when the UI misrepresents uncertainty, claims terminal success too early, hides manual review/reconciliation, or allows a user to request an action that should be clearly unavailable.

## Projection

MongoDB, Elasticsearch, read models, search documents, and derived dashboards are projection evidence.

They can prove what a read surface served. They cannot prove that the underlying business fact is true unless the owning guide says that read path first re-enters authority.

Projection evidence is useful for identifying convergence bugs: stale resurrection, false absence on critical reads, duplicate projection selection, missing tombstones, or tenant-scoping mistakes.

## Cache

Redis, browser cache, query cache, BFCache, cache keys, and warmed DTOs are cache evidence.

Caches optimize access and coordination; they do not decide business truth. A cache mismatch may be acceptable if the authoritative path repairs or rejects safely.

Cache evidence becomes serious when it grants authority, crosses tenant scope, resurrects stale visibility, or hides a fail-closed decision.

## Provider callbacks

Provider callbacks, webhooks, redirects, hosted-return responses, carrier callbacks, bank records, external refund responses, and manual proof uploads are external evidence.

They may be authentic and still not be local authority. They must be authenticated, scoped, deduplicated, amount/currency checked where relevant, and admitted through the owning lifecycle or reconciliation path.

A provider success after incompatible local terminal state is not automatically success. A provider failure after recognized local success is not automatically reversal. QA should expect explicit reconciliation, suspense, manual review, or collapse to current SQL truth.

# Bug Classification

Use durable categories so reports remain useful across releases.

## Authority Violation

A non-authoritative source makes or overwrites a business decision.

Examples of the category:

- projection or cache decides visibility without current authority;
- provider payload changes local state without lifecycle admission;
- frontend state creates or restores business authority;
- repair tooling copies derivative state back into truth.

Severity is usually high because authority violations can corrupt multiple domains silently.

## Tenant Boundary Violation

Data or action crosses tenant scope without explicit platform authority.

This includes reads, writes, projections, cache keys, search documents, events, repair tools, or financial/inventory references that mix tenant ownership.

Treat as critical unless the source documents explicitly define a platform/global authority path and the path proves current platform authority.

## Financial Corruption

Money is created, lost, duplicated, released, refunded, settled, reported, or blocked incorrectly.

This includes false payment finality, duplicate capture recognition, unsafe refund/payout readiness, ledger mismatch, settlement-lineage drift, amount/currency mismatch acceptance, or hiding unresolved external money.

Financial corruption severity is based on money integrity and blast radius, not UI visibility.

## Lifecycle Violation

Protected state changes outside its owner or through an illegal transition.

This includes direct payment/order/fulfillment/refund/status mutation, terminal-state resurrection, bypassing row/version guards, or using an operational shortcut as a lifecycle path.

A lifecycle violation can be severe even if the resulting state looks plausible.

## Projection Bug

A derivative surface fails to converge from authority.

This includes stale resurrection, false absence on critical reads, duplicate projection selection without freshness, missing tombstone/delete behavior, stale search visibility, or projection repair that does not re-read SQL.

Projection bugs are not the same as normal lag. The bug is the failure to converge safely when required.

## Evidence Admission Bug

Evidence is accepted, rejected, scoped, deduplicated, or classified incorrectly.

This includes webhook signature mistakes, amount/currency mismatch handling, unknown provider event execution, duplicate callback processing, stale external evidence overriding current SQL, or manual proof becoming final without review.

## Idempotency / Replay Bug

Retry, duplicate delivery, double-click, redelivery, rebuild, or recovery creates a second side effect or changes a decision incorrectly.

These bugs are high risk because Shopixy intentionally operates with asynchronous delivery and retries.

## Inventory Authority Bug

Stock, reservation, warehouse, variant, or movement truth is decided from stale or incomplete evidence.

This includes raw stock summing without current lineage, stale reservation consumption, inactive-warehouse availability, superseded variant stock resurrection, duplicate release/commit, or cleanup releasing stock while payment truth is unresolved.

## Identity / Authorization Bug

An actor is accepted from stale or insufficient evidence.

This includes trusting JWT claims alone, stale session after revocation, frontend guard as authorization, role downgrade not enforced, super-admin/platform authority inferred from tenant authority, or customer/vendor/affiliate identity surfaces being conflated.

## Operational Governance Bug

A dangerous primitive or recovery/runbook path bypasses authority, scope, audit, replay collapse, or documentation.

This includes direct SQL/bulk mutation, raw bus publish, full Mongo replacement, raw search writes, Redis fanout, manual financial repair, or global repair used without the reviewed boundary.

## Performance / Availability Bug

The system is too slow, unavailable, or operationally blocked beyond the accepted design.

Performance severity depends on business impact and whether the slowdown threatens authority, recovery, backlog drain, tenant operations, or money readiness.

Do not classify intentional fail-closed gating as a performance bug unless the gate is stuck because recovery or authority proof is broken.

## UX / Presentation Bug

The UI misleads, confuses, or prevents users from understanding the authoritative state.

UX bugs are real, but they are lower severity than authority, tenant, money, lifecycle, and data-integrity bugs unless the presentation causes unsafe action or hides critical uncertainty.

## Expected Tradeoff

The behavior is documented and intentional.

Examples include normal read-model lag, fail-closed readiness blocking under uncertainty, explicit reconciliation/manual-review states, and temporary projection repair unavailability.

Do not report an expected tradeoff as a defect. Report only if implementation violates the documented boundary or the user-facing communication is misleading.

# Severity Philosophy

Severity is determined by business impact, authority impact, and blast radius — not by visual impact.

A tiny UI inconsistency can be critical if it exposes another tenant's data or claims payment success without authority. A highly visible delay can be acceptable if it is a documented fail-closed tradeoff protecting money.

Use this severity philosophy:

## Critical

Use Critical when the issue can compromise tenant isolation, corrupt money, bypass authority, leak protected data across tenants, allow unsafe outward money, resurrect terminal lifecycle state, or create unrecoverable business truth.

Critical issues may have almost no visual footprint.

## High

Use High when the issue violates a lifecycle, identity, inventory, replay, projection, or recovery boundary in a way that can produce wrong business outcomes, repeated customer harm, or operationally expensive correction, but without proven critical blast radius.

## Medium

Use Medium when the issue causes incorrect presentation, local workflow failure, delayed convergence, or contained operational friction while authority remains intact and recovery is safe.

## Low

Use Low for minor UX clarity, wording, display, or ergonomics issues that do not misrepresent authority or cause unsafe action.

## Accepted / Not a Bug

Use Accepted when the behavior is explicitly intentional and the system remains within the documented boundary.

Severity must follow the highest impacted authority. If one symptom touches both UI and money, classify by money. If one symptom touches both projection and tenant leakage, classify by tenant boundary. If one symptom touches both availability and fail-closed financial safety, classify by whether the block is correct.

# Regression Philosophy

QA should add durable regression coverage when a defect or risk proves that an authority boundary can drift.

Do not add tests only because a screen changed. Add tests when the behavior represents a durable invariant that must not regress.

## Regression tests

Add regression tests when a confirmed bug violated a documented rule and the same failure mode could return.

Good regression targets:

- direct lifecycle bypass;
- stale evidence accepted as truth;
- tenant scope inferred from client input;
- duplicate replay causing repeated side effects;
- projection resurrection after SQL removed authority;
- missing fail-closed behavior under uncertainty;
- UI claiming terminal state from non-terminal authority.

A regression test should encode the invariant, not the incidental implementation detail.

## Certification tests

Add or extend certification tests when the rule is an architectural contract for a domain or cross-system boundary.

Certification tests are appropriate for:

- tenant isolation freezes;
- payment/order/fulfillment lifecycle ownership;
- dangerous primitive governance;
- projection/rebuild authority;
- identity/session authority;
- marketplace/public visibility authority;
- ledger/accounting replay authority;
- platform/global operation boundaries.

Certification tests should survive refactors because they protect the contract, not the current file shape.

## Integration tests

Add integration tests when correctness depends on multiple subsystems cooperating.

Use integration tests for flows where the bug appears only across boundaries: checkout + inventory + payment, refund + ledger + payout readiness, webhook + lifecycle + projection, tenant resolution + SQL lineage + derivative mutation.

Integration tests should prove the joined authority chain, not just the endpoint response.

## Recovery tests

Add recovery tests when failure, retry, replay, backlog, stale evidence, or partial external execution is part of the risk.

Recovery tests are essential for:

- late or duplicate webhooks;
- outbox retry;
- projection rebuild;
- dead-letter reset;
- payment/refund/payout uncertainty;
- inventory cleanup;
- stale cache hydration;
- interrupted rebuilds;
- operational repair tools.

Recovery tests should prove deterministic convergence: apply current truth, return existing result, no-op, tombstone/delete, retry, block, quarantine, or reconciliation-required.

# Common QA Mistakes

A new QA engineer should never assume:

- the UI that originated a request owns the truth;
- a token claim proves current identity or tenant authority;
- `X-Tenant-Id`, route tenant ids, document ids, or frontend selectors establish scope;
- a provider success callback means local payment is paid;
- a provider failure callback means local recognized money should be reversed;
- delivery means COD cash was collected unless Payment admitted collection evidence;
- a Mongo/Redis/Elasticsearch value is current business truth;
- read-model lag is automatically a bug;
- a missing projection means the SQL entity does not exist;
- a raw stock row means stock is reservable;
- a raw reservation row means reservation is consumable;
- a terminal order/payment/fulfillment state can be reopened by replay;
- a frontend success page proves backend creation;
- an admin or super-admin screen can bypass lifecycle ownership;
- Super Admin automatically has tenant mutation authority outside the documented platform boundary;
- emergency or repair tooling creates business authority;
- idempotency means immediate consistency;
- event delivery order is globally total;
- a dashboard total is ledger truth;
- a settlement header count is settlement membership authority when live lineage is required;
- CommerceHub rules can be freely copied from generic Storefront order rules;
- tenant storefront currency can legitimize CommerceHub marketplace currency;
- affiliate, treasury, and inventory “reservations” mean the same thing;
- an accepted tradeoff should be re-reported as a bug;
- a visually small issue is low severity if it touches money, tenants, or authority.

# QA Decision Tree

Use this reasoning model for every ambiguous behavior.

1. What is the disputed fact?

   If the fact is unclear, restate the symptom without assuming the cause.

2. Which domain owns the fact?

   Architecture, security/identity, orders, payments, inventory, platform operations, or a cross-system interaction?

3. What is the authoritative source?

   Identify the SQL authority, lifecycle owner, current backend authority service, or documented platform boundary.

4. Is the actor currently valid?

   Verify current identity/session/governance/role authority, not only token or frontend state.

5. Is tenant or platform scope proven?

   Verify trusted resolution and current SQL lineage. If scope comes from caller-supplied input, treat it as suspect.

6. Did the mutation cross the proper lifecycle boundary?

   If not, classify as an authority or lifecycle violation even if the visible result is correct.

7. Does money move, become recognized, become refundable, become payable, or become reportable?

   If yes, require stronger proof: lifecycle admission, amount/currency correctness, idempotency, ledger/reconciliation safety, and readiness gates.

8. Does inventory reserve, release, commit, restore, or become visible?

   If yes, require current lineage proof and confirm payment uncertainty does not make release unsafe.

9. Is any evidence external, stale, duplicated, delayed, or contradictory?

   If yes, expect deduplication, current-authority reload, collapse, no-op, quarantine, manual review, or reconciliation.

10. Is the observed surface derivative?

   If yes, decide whether lag is accepted or whether the read path was required to repair, tombstone, select freshest, or fail closed.

11. What is the safest expected outcome?

   Allow only when all required authority is proven. Otherwise expect reject, block, no-op, tombstone/delete, retry, temporary unavailable, manual review, quarantine, or reconciliation-required.

12. How should the issue be classified?

   Choose the highest authority impacted: tenant boundary, financial corruption, lifecycle violation, authority violation, inventory, projection, identity, operational governance, performance, UX, or expected tradeoff.

13. What evidence proves the claim?

   Cite authority-level evidence first. Use UI/browser evidence to show user impact, not to prove business truth.

# QA Mental Model

An excellent Shopixy QA engineer thinks like an authority investigator.

They do not ask only, “Did the feature work?” They ask:

- Who was allowed to decide?
- Was the actor still valid?
- Was tenant scope proven rather than claimed?
- Did the mutation pass through the lifecycle owner?
- Was money protected from false certainty and duplicate recognition?
- Was inventory protected from stale lineage and unsafe release?
- Did replay, recovery, and projection converge from current SQL truth?
- Did the UI present uncertainty honestly?
- Is this a confirmed defect, a theoretical risk, a mitigated risk, or an accepted tradeoff?

The strongest QA engineers are comfortable saying “this is correct even though it looks blocked” when the block protects authority, and “this is a serious bug even though the screen looks fine” when the path bypasses authority.

The one-sentence mental model:

PostgreSQL decides internal truth; lifecycle owners mutate it; external systems provide evidence; projections converge; uncertainty fails closed; QA protects the boundaries between those ideas.

# Source Documents

Primary authority files:

- `.ai-context/ARCHITECTURE_TRUTH.md` — repository-wide SQL authority, lifecycle ownership, tenant boundaries, fail-closed rules, projection/replay principles, financial and inventory authority.
- `.ai-context/PROJECT_CONTEXT.md` — platform purpose, subsystem ownership, datastores, multi-tenant architecture, financial/inventory authority boundaries, high-risk areas.
- `.ai-context/CODEX_RULES.md` — mandatory development and audit rules: context-first work, source-of-truth ownership, tenant isolation, event consistency, reconciliation awareness, risk vocabulary, dangerous primitive governance.
- `.ai-context/KNOWN_RISKS.md` — risk vocabulary and distinction between confirmed risks, mitigated risks, theoretical risks, accepted tradeoffs, and residual debt.
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md` — cross-system hardening decisions, replay/rebuild/recovery behavior, certification freezes, and fail-closed convergence patterns.

Previous onboarding guides:

- `.ai/hermes/02-architecture.md` — architectural truths, authoritative data sources, system boundaries, QA-critical rules, risk language.
- `.ai/hermes/03-inventory.md` — inventory authority, reservations, warehouse lineage, movement invariants, projection rules.
- `.ai/hermes/04-payments.md` — payment authority, provider evidence, webhooks, reconciliation, refunds, duplicate prevention, accounting integration.
- `.ai/hermes/05-orders.md` — order authority, checkout, lifecycle, inventory/payment/fulfillment/refund integration, projection/recovery.
- `.ai/hermes/06-security-identity.md` — identity model, tenant resolution, authorization, trust boundaries, dangerous operations, fail-closed rules.
- `.ai/hermes/07-platform-overview.md` — platform-level relationships, shared domains, customer journeys, cross-system invariants.

Deep-dive evidence corpus:

- `docs/architecture/*` — domain certification and freeze findings. Use these for detailed evidence and regression anchors, but resolve durable conflicts toward `.ai-context/*` and the owning domain guide.

Conflict note:

The previous onboarding guides and the inspected `.ai-context` files are consistent on the core QA philosophy: SQL authority, evidence-vs-truth separation, lifecycle ownership, tenant isolation, fail-closed uncertainty, replay-safe convergence, and accepted read-model lag. Known documented tensions are domain-specific, not philosophical: for example, older settlement prose that trusted denormalized settlement header counts is overridden by the live settlement-lineage rule, CommerceHub marketplace payment is explicitly excluded from Stripe/Paymob reference-provider certification, and the certified Payment Runtime model is not the active production payment path. QA should handle such tensions in the owning domain guide rather than inventing a new rule here.
