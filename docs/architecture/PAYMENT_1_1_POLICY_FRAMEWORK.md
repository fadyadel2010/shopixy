# PAYMENT-1.1 Payment Policy Framework

Date: 2026-07-10
Scope: Provider-neutral payment policy contracts, facts model, rule catalog, governance, versioning, and audit architecture
Status: CERTIFIED ARCHITECTURE

Runtime modified: No.

Implementation modified: No.

Adapters modified: No.

`.ai-context` updated: Yes. PAYMENT-1.1 defines permanent policy framework truths: provider-neutral policy contracts, canonical fact ownership, canonical rule catalog, governance lifecycle, immutable policy/rule/fact/evaluation versioning, and replay-safe policy audit. It preserves the certified PAYMENT-0.8 Runtime, PAYMENT-0.9 Provider Adapter, and PAYMENT-1.0 Policy Engine boundaries.

## Executive Summary

PAYMENT-1.1 defines the permanent enterprise Payment Policy Framework.

The Payment Policy Engine certified in PAYMENT-1.0 is now governed by explicit contracts, canonical facts, a reusable rule model, a policy model, a canonical rule catalog, authoritative fact-source boundaries, governance workflows, immutable versioning, and replay-safe audit.

The framework is provider-neutral. It does not encode Stripe, Paymob, Fawry, Manual Payment, COD, Vodafone Cash, wallet, QR, reference payment, or future provider implementation behavior as policy authority. Providers expose capability, constraint, status, and evidence facts. Policy decides whether those facts allow a business action. Runtime executes only policy-approved actions through the certified state machine. Adapters translate provider facts and execute provider operations only under Runtime command.

Canonical boundary:

```text
Authoritative Facts
  -> Immutable Policy Context Snapshot
  -> Versioned Policies
  -> Versioned Rule Catalog
  -> Deterministic Evaluation
  -> Policy Decision + Audit
  -> Payment Runtime Execution
  -> Provider Adapter Translation
```

Certification decision: **PAYMENT-1.1 is certified as the enterprise Payment Policy Framework architecture.**

## Policy Contracts

Contracts are provider-neutral responsibilities only. They are not implementation instructions, persistence schemas, service designs, DTOs, controllers, adapters, or runtime modifications.

### IPolicyEngine

- Owns deterministic policy evaluation for a declared operation intent.
- Accepts a provider-neutral `IPolicyContext`.
- Evaluates published policy versions and rule versions in the configured evaluation order.
- Returns one immutable `IPolicyDecision`.
- Emits a complete `IPolicyAudit` record.
- Fails closed when required facts, versions, rule dependencies, or governance state are missing.
- Never executes payment operations, calls providers, mutates runtime state, writes transactions, or edits policies.

### IPolicy

- Represents one named, versioned policy in a policy domain.
- Declares scope, required facts, allowed operation intents, rules, dependencies, priority, fail-closed behavior, reason-code namespace, and audit requirements.
- Composes versioned rules and rule groups.
- Produces a policy-level decision candidate.
- Is immutable once published.
- Is governed through draft, review, approval, publication, deprecation, archive, and rollback lifecycle.

### IPolicyRule

- Represents one atomic deterministic condition, threshold, range, time window, capability check, constraint check, evidence check, or risk/fraud/compliance predicate.
- Declares required facts, dependency rules, priority, short-circuit behavior, reason codes, severity, and output effect.
- Reads facts only from the immutable policy context.
- Never owns facts, calls external systems, mutates runtime, or invokes adapters.
- Is immutable once published.

### IPolicyContext

- Represents the immutable evaluation input snapshot for one policy request.
- Contains operation intent, actor/system, tenant, merchant, customer, order, warehouse, shipping, payment, provider, capability, constraint, risk, fraud, settlement, evidence, and decision-history facts.
- Carries fact schema version, fact source lineage, context hash, assembly timestamp, and required completeness markers.
- Is consumed read-only by policies and rules.
- Is not frontend state, cache state, projection state, provider payload replay, or browser state.

### IPolicyFacts

- Represents canonical provider-neutral facts grouped by domain.
- Carries owner, authoritative source, lifetime, mutability, authority level, effective timestamp, and source version where applicable.
- Supports current-authority references and immutable snapshots.
- Never contains provider wire contracts as policy authority.
- Never treats cache, projection, frontend, route data, SDK result, redirect result, or raw provider payload as final fact authority.

### IPolicyDecision

- Represents the immutable policy output returned to Runtime.
- Supports Allowed, Denied, ManualReview, Quarantine, Retry, Recovery, RedirectProvider, AlternativeProvider, AlternativeMethod, RequireEvidence, and NoDecisionFailClosed outcomes.
- Includes selected method/provider/action only when allowed by policy.
- Includes reason codes, rule results, skipped rules, constraints, dependencies, decision severity, manual-review requirement, retry/recovery instructions, version evidence, and audit reference.
- Is distinct from runtime `PaymentDecision`; policy authorizes or blocks execution, while runtime records lifecycle outcomes.

### IPolicyEvaluator

- Evaluates rules and rule groups inside a policy version.
- Enforces evaluation order, priority, dependencies, short-circuit rules, and composition semantics.
- Records executed, skipped, failed, and short-circuited rules.
- Produces deterministic evaluation traces.
- Never fetches mutable external facts during evaluation.

### IPolicyAudit

- Captures replay-safe policy evaluation evidence.
- Records input snapshot hash, facts used, rules executed, rules skipped, decisions, reason codes, versions, timestamp, actor/system, operation intent, and replay metadata.
- Is append-only.
- Supports forensic replay against the exact policy, rule, catalog, fact schema, and evaluation versions used.
- Does not become mutable policy authority.

### IPolicyVersion

- Identifies immutable policy, rule, catalog, fact schema, evaluation engine, audit contract, and replay contract versions.
- Records effective dates, expiration dates, publication status, author, approver, governance ticket/reference, supersession, and rollback lineage.
- Published versions cannot be edited.
- Corrections create new versions.

### IPolicyRepository

- Provides governed access to policy definitions, rule definitions, catalog entries, versions, and publication metadata.
- Serves only approved published versions to runtime evaluation.
- Supports draft and review access only inside governance workflows.
- Preserves immutable history, rollback-by-activation, and audit lineage.
- Never lets Runtime or Adapters edit policies or rules.

## Facts Model

Facts are authoritative inputs. Rules never own facts. Policies compose facts into decisions. Runtime and Adapters may submit evidence or request context assembly, but they do not fabricate policy authority.

### Fact Dimensions

Every fact family must declare:

| Dimension | Meaning |
|---|---|
| Ownership | Which domain owns the fact before policy consumption. |
| Lifetime | Whether the fact is current authority, immutable snapshot, event evidence, or retained history. |
| Mutability | Whether the fact can change, and by which governed authority. |
| Authority | Whether the fact is authoritative, admitted evidence, derived read evidence, or presentation-only. |

### Tenant Facts

| Dimension | Definition |
|---|---|
| Ownership | Tenant administration and SQL tenant configuration authority. |
| Lifetime | Current authority for tenant status, entitlements, operating countries, enabled method families, feature posture, compliance posture, and settlement readiness; snapshotted into each policy context. |
| Mutability | Mutable only through governed tenant/admin configuration workflows. Evaluation snapshots are immutable. |
| Authority | Authoritative when read from current backend tenant authority. Frontend tenant headers, route tenant ids, cache, and projections are not authority. |

### Merchant Facts

| Dimension | Definition |
|---|---|
| Ownership | Merchant/store/vendor administration, marketplace/vendor governance where applicable, settlement/payout readiness, and risk governance. |
| Lifetime | Current authority for merchant enablement, category, restrictions, settlement holds, payout status, risk tier, marketplace/vendor role, and operational blocks; snapshotted for evaluation. |
| Mutability | Mutable only through merchant governance, marketplace governance, treasury/settlement holds, or risk administration. |
| Authority | SQL/governed backend records only. Dashboards, projections, vendor UI state, or provider account display are not policy authority. |

### Customer Facts

| Dimension | Definition |
|---|---|
| Ownership | Customer identity, account, order ownership, authentication, risk, dispute, and blocklist authorities. |
| Lifetime | Current authority for customer identity, authentication posture, account age, segment, dispute history, COD refusal history, saved-method posture, and blocklist state; immutable order/customer snapshots where required. |
| Mutability | Mutable through customer/account/risk governance. Historical policy contexts remain immutable. |
| Authority | Backend identity/account and admitted risk/dispute evidence only. Browser session state, JWT claims alone, route ids, or frontend profile data are not authority. |

### Order Facts

| Dimension | Definition |
|---|---|
| Ownership | Order/Checkout authority. |
| Lifetime | Immutable payable snapshots for amount, currency, tax, shipping, discounts, items, product categories, channel, marketplace/affiliate lineage, fulfillment dependency, refund/capture balance where applicable. Current order state may also be included as a current authority fact. |
| Mutability | Created and changed only through certified order/checkout/payment/refund/cancellation lifecycle. Original payable snapshots are immutable after admission. |
| Authority | SQL order/payable authority only. Cart UI, stale checkout DTOs, recovery snapshots, projections, and provider payloads are not order fact authority. |

### Warehouse Facts

| Dimension | Definition |
|---|---|
| Ownership | Warehouse and shipping assignment authorities. |
| Lifetime | Current authority for warehouse enablement, origin readiness, carrier assignment evidence, fulfillment capability, and location snapshot. |
| Mutability | Mutable only through warehouse/shipping administration. Evaluation snapshot is immutable. |
| Authority | Backend warehouse and carrier assignment authority only. Frontend selected warehouse is a candidate, not eligibility authority. |

### Shipping Facts

| Dimension | Definition |
|---|---|
| Ownership | Shipping method, shipping calculation, shipment lifecycle, carrier integration, and geographic authorities. |
| Lifetime | Current shipping method eligibility, carrier-backed assignment, shipping address/geography snapshot, delivery/COD compatibility, shipment status, carrier evidence, and collection requirements. |
| Mutability | Shipping methods and carrier assignments mutate through admin governance; shipment snapshots are immutable; lifecycle changes are append-only decisions. |
| Authority | SQL shipping/geographic/shipment authority and admitted carrier evidence only. Shipping quotes, frontend selection, route state, cache, and projections are not policy authority. |

### Payment Facts

| Dimension | Definition |
|---|---|
| Ownership | Certified Payment Runtime. |
| Lifetime | Current payment session, attempt, action, state, refundable/capturable/voidable balance, idempotency posture, terminal state, recovery posture, and immutable payment snapshots. |
| Mutability | Mutable only through certified runtime decisions and state-machine transitions. Policy context snapshots are immutable. |
| Authority | SQL Payment Runtime authority only. Runtime may request policy but cannot invent facts outside its authority. |

### Provider Facts

| Dimension | Definition |
|---|---|
| Ownership | Provider adapter registry, credential/environment governance, and admitted provider evidence. |
| Lifetime | Current provider availability, environment, account posture, provider class/candidate identity, provider session references as admitted evidence, status inquiry evidence, settlement/refund evidence. |
| Mutability | Provider availability and account posture may change through governed provider configuration; evidence is append-only after admission. |
| Authority | Adapter-exposed facts after backend admission. Provider raw payload, redirect params, SDK result, browser values, and provider support marketing are not policy authority. |

### Capability Facts

| Dimension | Definition |
|---|---|
| Ownership | Provider adapter capability registry plus tenant/provider/account configuration. |
| Lifetime | Current capability support for redirect, embedded checkout, SDK, wallet, QR, reference payment, receipt upload, manual review, capture, refund, partial refund, void, status inquiry, recovery, settlement, async confirmation, metadata, idempotency, and replay protection. |
| Mutability | Mutable through provider certification/configuration governance. Snapshotted into policy context. |
| Authority | Provider-neutral capability registry and governed configuration only. Capability is not business eligibility. |

### Constraint Facts

| Dimension | Definition |
|---|---|
| Ownership | Provider adapter constraints, tenant/merchant policy configuration, compliance, risk, shipping, and runtime constraints. |
| Lifetime | Current constraints such as requires webhook, callback, HMAC, message signature, reference number, receipt, manual approval, delivery evidence, carrier evidence, provider session, expiry, status inquiry, settlement report, or account readiness. |
| Mutability | Mutable through certified provider/configuration/governance workflows. |
| Authority | Backend-certified constraints only. Frontend hints and cached provider metadata are not authority. |

### Risk Facts

| Dimension | Definition |
|---|---|
| Ownership | Risk engine, payment risk governance, merchant/customer/order risk authorities. |
| Lifetime | Current risk tier, score, velocity, disputes, anomalies, mismatch signals, customer/device/session posture, merchant risk state; snapshotted for each decision. |
| Mutability | Mutable as risk systems admit new evidence. Evaluation snapshots are immutable. |
| Authority | Admitted backend risk evidence only. Analytics dashboards, frontend device-only claims, or cache are not final risk authority. |

### Fraud Facts

| Dimension | Definition |
|---|---|
| Ownership | Fraud engine and fraud operations governance. |
| Lifetime | Current fraud score, fraud rule hits, suspicious evidence, blocklists, velocity flags, device/session signals, verification mismatch, and quarantine recommendations. |
| Mutability | Mutable through admitted fraud evidence and operations review. |
| Authority | Backend fraud evidence and approved manual review only. Provider fraud hints are evidence until admitted. |

### Settlement Facts

| Dimension | Definition |
|---|---|
| Ownership | Payment Runtime settlement admission, Treasury, Accounting, and Reconciliation boundaries. |
| Lifetime | Current settlement readiness, unsettled/settled/reconciliation-required state, settlement balances, payout holds, provider/manual/COD settlement evidence, treasury/accounting readiness. |
| Mutability | Settlement records are immutable once admitted; readiness and holds can change through treasury/accounting/governance. |
| Authority | Payment settlement admission plus treasury/accounting evidence through certified boundaries. Dashboards and provider reports alone are not authority. |

### Evidence Facts

| Dimension | Definition |
|---|---|
| Ownership | Payment Runtime evidence admission or adjacent domain evidence admission. |
| Lifetime | Append-only admitted evidence: webhook, callback, status inquiry, receipt, carrier/driver evidence, reviewer evidence, settlement report, refund evidence, provider evidence, verification result. |
| Mutability | Immutable after admission except redaction markers, verification linkage, quarantine classification, or retention metadata. |
| Authority | Admitted evidence is usable policy input. Raw provider payloads, uploaded files before admission, frontend SDK results, and redirects are candidates only. |

### Decision History Facts

| Dimension | Definition |
|---|---|
| Ownership | Policy audit, Payment Runtime decisions, manual review, recovery, refund/capture/void/settlement decisions. |
| Lifetime | Permanent or retention-governed immutable history of prior policy and runtime decisions, reason codes, retries, denials, quarantines, reviews, overrides where certified, and recovery attempts. |
| Mutability | Append-only. Supersession creates new history entries; it does not edit old decisions. |
| Authority | Immutable decision/audit records only. Projections and dashboards are derivative. |

## Rule Model

Rules are deterministic units of policy logic. A rule may inspect facts and return an evaluation result. A rule never owns facts, creates facts, mutates facts, calls providers, edits runtime state, writes payment transactions, or performs side effects.

### Canonical Rule Attributes

| Attribute | Meaning |
|---|---|
| Rule Id | Stable catalog identity. |
| Rule Version | Immutable rule definition version. |
| Rule Type | Boolean, threshold, range, time, country, currency, capability, constraint, risk, fraud, settlement, refund, capture, retry, recovery, manual review, routing, or composition. |
| Required Facts | Fact families required for evaluation. |
| Dependencies | Rules or rule groups that must pass, fail, or execute first. |
| Priority | Evaluation order within a policy or group. |
| Severity | Informational, warning, deny, quarantine, manual review, recovery, or terminal block. |
| Effect | Allow, deny, require review, quarantine, require evidence, redirect provider, alternative method, retry, recovery, or no-op. |
| Reason Codes | Machine-stable reasons emitted when the rule affects a decision. |
| Short-Circuit | Whether the rule can stop later evaluation. |
| Audit Level | Summary, facts used, full evaluation trace, or replay-critical. |
| Effective Window | Effective and expiration dates for published use. |

### Rule Types

| Rule Type | Responsibility |
|---|---|
| Boolean Rules | Evaluate true/false predicates such as enabled flags, required facts present, evidence complete, verification passed, provider enabled, method enabled, tenant eligible, or order eligible. |
| Threshold Rules | Compare numeric facts to limits such as amount, velocity, risk score, refund count, retry count, dispute count, settlement discrepancy, or COD ceiling. |
| Range Rules | Validate minimum/maximum amount, capturable/refundable balance, delivery-distance bands, provider limits, settlement tolerances, or supported order totals. |
| Time Rules | Validate business hours, blackout windows, capture windows, refund windows, void windows, reference expiry, QR expiry, retry backoff, review SLA, or settlement periods. |
| Country Rules | Validate billing, shipping, fulfillment, warehouse, merchant, customer, provider, tenant, or compliance geography. |
| Currency Rules | Validate payable currency, tenant currency, provider currency, settlement currency, conversion policy, and mismatch handling. |
| Capability Rules | Validate provider/method/account support for operation capabilities. |
| Constraint Rules | Validate required webhook, callback, HMAC, reference number, provider session, receipt, delivery evidence, status inquiry, manual approval, or settlement report constraints. |
| Risk Rules | Evaluate risk scores, risk tiers, velocity, mismatch signals, dispute posture, customer behavior, device/session posture, and merchant risk. |
| Fraud Rules | Evaluate fraud score, fraud hits, suspicious evidence, blocklists, stolen account indicators, receipt anomalies, and provider mismatch. |
| Settlement Rules | Validate settlement readiness, settlement evidence, reconciliation posture, treasury/accounting readiness, payout holds, COD remittance, and discrepancy handling. |
| Refund Rules | Validate refundable state, refund window, refundable balance, amount, currency, partial refund limits, reason, prior refunds, settlement interaction, and manual review requirement. |
| Capture Rules | Validate authorization state, capture window, capturable balance, partial capture support, provider capability, and risk/compliance gates. |
| Retry Rules | Validate retry count, retry window, idempotency, provider ambiguity, external-boundary uncertainty, terminal state, and recovery requirement. |
| Recovery Rules | Validate whether status inquiry, evidence rebinding, duplicate collapse, manual reconciliation, quarantine, or terminal collapse is allowed. |
| Manual Review Rules | Require human review based on risk, amount, evidence mismatch, settlement discrepancy, refund sensitivity, COD collection, merchant/customer posture, or compliance. |
| Routing Rules | Rank eligible providers, choose fallback, redirect provider, alternate method, or block routing when mandatory rules fail. |

### Rule Groups

Rule groups organize rules into reusable, versioned decision units:

| Rule Group | Responsibility |
|---|---|
| Context Admission | Required fact completeness, authority-source validation, schema version validation, context hash creation. |
| Tenant/Merchant Eligibility | Tenant, merchant, provider/method enablement, entitlement, operating country, settlement posture. |
| Customer/Order Eligibility | Customer, product, order, amount, currency, state, channel, marketplace/affiliate lineage. |
| Provider Capability/Constraint | Capability support and required constraints. |
| Risk/Fraud/Compliance | Risk score, fraud hits, compliance restrictions, sanctions/geography/product restrictions. |
| Operation Eligibility | Refund, capture, void, retry, recovery, settlement, COD, wallet, QR, reference, manual review operation rules. |
| Routing | Provider ranking, fallback, alternatives, health-aware or capability-aware routing. |
| Decision Finalization | Short-circuit resolution, reason-code aggregation, audit emission, fail-closed final decision. |

### Rule Composition

- Policies compose rules and rule groups.
- Groups may be all-of, any-of, first-match, priority-list, deny-overrides, review-overrides, quarantine-overrides, or score-threshold compositions.
- Mandatory deny rules override routing preference.
- Compliance and authority-source failures cannot be resurrected by routing or fallback rules.
- Missing required facts fail closed.
- Short-circuit rules must still record why later rules were skipped.
- Rule dependencies must be explicit and versioned.

## Policy Model

Policies are named, versioned decision units that compose rule groups for one policy domain. Policies do not execute runtime actions. They only return decisions that Runtime may enforce.

### Canonical Policies

| Policy | Responsibility |
|---|---|
| Eligibility Policy | Decides whether tenant, merchant, customer, order, product, method, provider, country, region, currency, amount, time, wallet, QR, COD, reference, or action is eligible. |
| Routing Policy | Ranks eligible providers/methods, chooses fallback, redirects provider, selects alternative method, and blocks ineligible routes. |
| Refund Policy | Decides full/partial refund eligibility, refund amount, currency, reason, window, provider support, settlement interaction, and manual review. |
| Capture Policy | Decides capture/partial capture eligibility, authorization binding, capture window, balance, provider support, and risk/compliance gates. |
| Void Policy | Decides void/cancel eligibility for pending, reference, action-required, authorized, unpaid, or uncaptured obligations. |
| Settlement Policy | Decides settlement admission eligibility, evidence requirements, discrepancy handling, reconciliation, COD/manual/provider settlement, and treasury/accounting readiness. |
| Recovery Policy | Decides retry, status inquiry, evidence rebinding, duplicate collapse, quarantine, reconciliation, or terminal collapse for ambiguous states. |
| Fraud Policy | Decides fraud block, risk reduction, quarantine, manual review, or allow based on fraud evidence. |
| Compliance Policy | Decides geography, currency, product/category, merchant, KYC/KYB, sanctions, regulatory, retention, and local-rule restrictions. |
| Manual Review Policy | Decides review requirement, reviewer role, approval separation, override eligibility where certified, and review audit. |
| Marketplace Policy | Decides marketplace payment, split-order, vendor, commission, refund, settlement, payout, routing, and evidence requirements. |
| Affiliate Policy | Decides affiliate-attributed payment constraints, commission-impacting refund/cancellation restrictions, settlement interactions, and fraud/risk limits. |

### Policy Composition

- Evaluation begins with Context Admission.
- Eligibility policies remove disallowed methods, providers, actions, and operations.
- Routing policy evaluates eligible candidates only.
- Operation-specific policy evaluates refund, capture, void, settlement, retry, recovery, COD, wallet, QR, reference, or manual-review intent.
- Risk, Fraud, and Compliance policies may deny, quarantine, lower limits, or require manual review at any point where configured.
- Manual Review policy finalizes whether automation may proceed.
- Decision Finalization produces one policy decision.

### Policy Dependencies

| Dependency | Rule |
|---|---|
| Routing depends on Eligibility | Routing cannot consider a denied method/provider. |
| Operation policies depend on Runtime state facts | Refund/capture/void/retry/recovery/settlement cannot evaluate without current runtime state and balances. |
| Compliance can override all business preference | Compliance deny or review requirement dominates provider preference and routing. |
| Fraud can quarantine or review | Fraud rules can block automation even when eligibility and capability pass. |
| Settlement depends on admitted value | Settlement policy cannot settle value that Payment Runtime has not confirmed, captured, collected, or admitted. |
| Marketplace/Affiliate depend on order lineage | Marketplace and affiliate policies must consume immutable order/suborder/attribution facts, not projections. |

### Evaluation Order

Default order:

```text
Context Admission
  -> Tenant / Merchant Eligibility
  -> Customer / Order / Product Eligibility
  -> Payment Method Eligibility
  -> Provider Capability / Constraint Eligibility
  -> Compliance
  -> Risk / Fraud
  -> Operation Policy
  -> Routing
  -> Manual Review
  -> Recovery / Settlement where applicable
  -> Decision Finalization
```

Priority rule:

- Authority-source failures, missing required facts, tenant denial, compliance denial, and currency/amount hard blocks short-circuit before routing.
- Manual review, quarantine, and recovery may short-circuit execution while preserving a non-terminal path.
- Provider preference cannot override eligibility, risk, fraud, compliance, or settlement readiness.

### Policy Version

Each policy version includes:

- Policy id.
- Policy version id.
- Domain.
- Required fact schema version.
- Rule catalog version.
- Rule versions.
- Evaluation version.
- Effective date.
- Expiration date.
- Publication status.
- Approval evidence.
- Supersession lineage.
- Rollback lineage.
- Audit contract version.
- Replay contract version.

## Rule Catalog

The canonical rule catalog is the enterprise namespace for reusable policy rules. Catalog entries are provider-neutral. Provider-specific behavior enters only as capability, constraint, or evidence facts.

| Rule Id | Rule Name | Type | Primary Facts | Default Effect |
|---|---|---|---|---|
| PAY-RULE-001 | Tenant Enabled | Boolean | Tenant | Deny when tenant inactive, suspended, deleted, or not payment-enabled. |
| PAY-RULE-002 | Merchant Enabled | Boolean | Merchant | Deny when merchant/vendor/store is inactive, blocked, or operationally held. |
| PAY-RULE-003 | Provider Enabled | Boolean | Provider, Tenant, Merchant | Deny provider candidate when not enabled for tenant/merchant/account/environment. |
| PAY-RULE-004 | Method Enabled | Boolean | Payment, Tenant, Merchant | Deny payment method when disabled, hidden, unsupported, or unavailable. |
| PAY-RULE-005 | Country Allowed | Country | Tenant, Merchant, Customer, Order, Shipping, Provider | Deny when required country dimension is blocked or unsupported. |
| PAY-RULE-006 | Region Allowed | Country | Geographic, Shipping, Provider, Compliance | Deny when region/governorate/state/district/zone is blocked or unsupported. |
| PAY-RULE-007 | Currency Allowed | Currency | Order, Tenant, Provider, Settlement | Deny when payable/settlement/provider currency is unsupported or mismatched. |
| PAY-RULE-008 | Amount Allowed | Range | Order, Payment, Provider, Tenant | Deny when amount is below minimum, above maximum, invalid, or outside provider/tenant limits. |
| PAY-RULE-009 | Risk Threshold | Threshold | Risk | Deny, review, or quarantine when risk exceeds configured threshold. |
| PAY-RULE-010 | Velocity Threshold | Threshold | Risk, Customer, Merchant, Decision History | Deny, review, or quarantine when velocity exceeds limits. |
| PAY-RULE-011 | Settlement Ready | Settlement | Settlement, Merchant, Payment | Block settlement or payment operations when settlement posture is not ready. |
| PAY-RULE-012 | Refund Window | Time | Payment, Refund, Provider | Deny or review refund outside allowed window. |
| PAY-RULE-013 | Capture Window | Time | Payment, Capture, Provider | Deny or review capture outside authorization/capture window. |
| PAY-RULE-014 | Void Window | Time | Payment, Void, Provider | Deny void after void window or after collection/capture when refund is required. |
| PAY-RULE-015 | COD Allowed | Boolean | Shipping, Warehouse, Customer, Order, Risk, Merchant | Allow COD only when shipping, customer, geography, merchant, risk, and amount rules pass. |
| PAY-RULE-016 | Wallet Allowed | Capability | Provider, Capability, Customer, Currency, Country | Allow wallet only when provider/account/method/customer/country/currency facts pass. |
| PAY-RULE-017 | QR Allowed | Capability | Provider, Capability, Amount, Expiry, Risk | Allow QR only when supported, within expiry/amount/risk/evidence constraints. |
| PAY-RULE-018 | Reference Allowed | Capability | Provider, Capability, Customer, Order, Expiry | Allow reference payment only when reference issuance/renewal rules pass. |
| PAY-RULE-019 | Manual Review Required | Manual Review | Risk, Fraud, Evidence, Amount, Settlement, Refund | Require manual review when configured triggers fire. |
| PAY-RULE-020 | Evidence Complete | Boolean | Evidence | Deny, review, quarantine, or recovery when required evidence is incomplete. |
| PAY-RULE-021 | Verification Passed | Boolean | Evidence, PaymentVerification | Allow evidence-driven action only after verification passed or duplicate collapsed. |
| PAY-RULE-022 | Order Eligible | Boolean | Order | Deny when order state, payable snapshot, fulfillment dependency, or lineage is not eligible. |
| PAY-RULE-023 | Product Eligible | Boolean | Order, Product, Compliance | Deny or review restricted, regulated, blocked, hazardous, digital/physical incompatible, or COD-excluded products. |
| PAY-RULE-024 | Customer Eligible | Boolean | Customer, Risk, Fraud | Deny, review, or quarantine blocked, risky, unauthenticated, disputed, or COD-abusive customers. |
| PAY-RULE-025 | Tenant Eligible | Boolean | Tenant, Compliance | Deny when tenant entitlement, status, operating geography, compliance, or settlement readiness fails. |
| PAY-RULE-026 | Provider Capability Supported | Capability | Provider, Capability | Deny operation when capability is unsupported for provider/method/account. |
| PAY-RULE-027 | Provider Constraint Satisfied | Constraint | Provider, Constraint, Evidence | Require action/review/recovery when constraints are not satisfied. |
| PAY-RULE-028 | Refund Balance Available | Range | Payment, Refund | Deny refund exceeding refundable balance or invalid currency. |
| PAY-RULE-029 | Partial Refund Allowed | Capability | Provider, Payment, Refund | Deny partial refund when unsupported or limit exceeded. |
| PAY-RULE-030 | Capture Balance Available | Range | Payment, Capture | Deny capture exceeding capturable balance. |
| PAY-RULE-031 | Partial Capture Allowed | Capability | Provider, Payment, Capture | Deny partial capture when unsupported or limit exceeded. |
| PAY-RULE-032 | Retry Safe | Retry | Payment, Provider, Evidence, Decision History | Allow retry only when external ambiguity is fenced and attempt limits pass. |
| PAY-RULE-033 | Recovery Required | Recovery | Payment, Evidence, Provider | Require recovery when status is ambiguous, stale, missing, duplicated, or externally uncertain. |
| PAY-RULE-034 | Quarantine Required | Risk/Fraud/Recovery | Evidence, Payment, Risk, Fraud | Quarantine plausible but mismatched or ambiguous facts. |
| PAY-RULE-035 | Provider Health Eligible | Routing | Provider, Capability, Decision History | Deprioritize, redirect, or deny provider based on admitted health/availability facts. |
| PAY-RULE-036 | Routing Priority | Routing | Provider, Tenant, Merchant, Capability, Risk | Rank only eligible providers according to versioned priority. |
| PAY-RULE-037 | Alternative Method Allowed | Routing | Method, Provider, Tenant, Order | Offer alternative method only after eligible method evaluation. |
| PAY-RULE-038 | Alternative Provider Allowed | Routing | Provider, Capability, Constraints, Risk | Offer fallback provider only after eligibility and constraints pass. |
| PAY-RULE-039 | Marketplace Eligible | Boolean | Marketplace, Order, Merchant, Settlement | Deny marketplace payment when vendor/suborder/commission/settlement facts fail. |
| PAY-RULE-040 | Affiliate Eligible | Boolean | Affiliate, Order, Payment, Risk | Deny or review affiliate-attributed payment when attribution/commission/risk facts fail. |
| PAY-RULE-041 | Compliance Allowed | Compliance | Tenant, Merchant, Customer, Product, Country, Currency | Deny or review when compliance restrictions apply. |
| PAY-RULE-042 | Fraud Blocklist | Fraud | Fraud, Customer, Merchant, Payment | Deny when blocklist or confirmed fraud evidence applies. |
| PAY-RULE-043 | Settlement Evidence Complete | Settlement | Settlement, Evidence, Payment | Block settlement or require reconciliation when evidence incomplete. |
| PAY-RULE-044 | Reconciliation Required | Settlement/Recovery | Settlement, Payment, Evidence | Require reconciliation when settlement/payment/provider facts diverge. |
| PAY-RULE-045 | Authority Source Valid | Boolean | All Facts | Fail closed when fact source is frontend, cache, projection, browser, raw provider payload, or stale replay. |
| PAY-RULE-046 | Fact Schema Compatible | Boolean | PolicyContext, Fact Schema | Fail closed when policy version cannot evaluate supplied fact schema version. |
| PAY-RULE-047 | Decision Replay Compatible | Boolean | Decision History, Versions | Fail closed or route to replay tooling when exact versions are unavailable. |
| PAY-RULE-048 | Evidence Hash Valid | Evidence | Evidence | Quarantine or deny when evidence hash/integrity metadata is missing or invalid. |
| PAY-RULE-049 | Actor Authorized | Boolean | Actor, Operation, Governance | Deny operation when actor/system lacks authority for requested policy intent. |
| PAY-RULE-050 | Override Allowed | Manual Review/Governance | Actor, Decision History, Policy | Permit override only when a certified override policy exists and audit requirements pass. |

Catalog growth rules:

- New catalog entries require governance review and immutable versioning.
- Rule ids are never reused.
- Rule meaning cannot be edited after publication.
- Deprecated rules remain replayable.
- Provider-specific names do not enter canonical rule ids.

## Fact Sources

Policy may consume only authoritative backend facts, immutable snapshots, or admitted evidence.

| Fact Family | Authoritative Source |
|---|---|
| Tenant Configuration | Tenant SQL authority, tenant entitlement/configuration governance, compliance posture, settlement readiness. |
| Merchant Configuration | Merchant/store/vendor SQL authority, marketplace/vendor governance, payout/settlement readiness, risk holds. |
| Customer | Backend customer identity/account authority, authentication posture, dispute history, blocklist/risk evidence, admitted order ownership. |
| Order | SQL order/checkout/payable authority, immutable order/payable snapshots, marketplace/affiliate lineage, refund/capture balances from Payment Runtime. |
| Warehouse | SQL warehouse authority, carrier warehouse assignment, origin readiness, carrier account readiness. |
| Shipping | SQL shipping methods, shipping calculation authority, shipment lifecycle authority, admitted geographic snapshots, admitted carrier evidence. |
| Payment Runtime | PaymentSession, PaymentAttempt, ProviderSession evidence, PaymentAction, ProviderEvidence, PaymentVerification, PaymentDecision, PaymentTransaction, Refund, Capture, Void, Settlement, Recovery, AuditRecord. |
| Provider Capabilities | Certified provider capability registry, provider account configuration, adapter-exposed capability facts. |
| Provider Constraints | Certified provider constraint registry, provider account/environment constraints, required evidence/action metadata. |
| Risk Engine | Admitted risk score, risk tier, velocity, device/session evidence, merchant/customer/order risk, risk operations decisions. |
| Fraud Engine | Admitted fraud score, fraud hits, blocklists, suspicious evidence, quarantine recommendations, manual fraud review. |
| Settlement | Payment settlement admission, Treasury readiness, Accounting readiness, provider/manual/COD settlement evidence, reconciliation records. |
| Evidence | Runtime-admitted evidence, verification outcomes, manual review evidence, carrier/driver evidence, receipt evidence, refund/settlement evidence. |
| Decision History | Policy audit, runtime decisions, manual reviews, recovery attempts, retries, denials, quarantines, overrides where certified. |

Forbidden policy sources:

- Frontend state.
- Browser storage.
- Route parameters.
- Redirect parameters.
- SDK result state.
- Raw provider payload replay.
- Cache.
- Redis.
- MongoDB projections.
- Elasticsearch projections.
- Dashboards.
- Analytics summaries.
- Stale DTOs.
- Provider support documentation alone.
- Shipping/order/payment projections.
- Manual operator notes before backend admission.

## Governance

Policy governance owns lifecycle. Runtime and Adapters do not edit policies or rules.

### Authoring

- Policy authoring creates draft policy versions.
- Rule authoring creates draft rule versions.
- Catalog authoring creates draft catalog entries or draft catalog versions.
- Drafts are isolated from runtime evaluation.
- Drafts must declare required facts, rule dependencies, reason codes, fail-closed behavior, audit contract, and test/replay expectations.

### Review and Approval

- Review validates ownership boundaries, source authority, deterministic behavior, missing-fact behavior, provider neutrality, replayability, reason codes, audit coverage, and backward compatibility.
- Approval must be role-separated from authoring for production-impacting policies.
- High-risk policies require compliance/risk/fraud/settlement review when their domains are affected.
- Approval binds to immutable version content and validation checksum.

### Lifecycle States

| State | Meaning |
|---|---|
| Draft | Editable working version. Not visible to runtime. |
| Under Review | Candidate version locked for review. Not visible to runtime. |
| Approved | Approved but not runtime-active until publication/effective date. |
| Published | Runtime-visible for effective scope and time window. Immutable. |
| Deprecated | Still replayable; not selected for new decisions after replacement rules take effect. |
| Archived | Retained for audit/replay/history; not active for new decisions. |
| Rejected | Review failed; cannot publish without new draft/version. |
| Superseded | Replaced by a newer published version but retained for replay. |

### Effective Dates and Expiration

- Published versions must have an effective date.
- Optional expiration dates retire versions for new decisions while preserving replay.
- Future-dated policies may be published but not selected until effective.
- Expired policies remain replayable.
- Runtime evaluation must select by tenant/scope/operation/effective timestamp.

### Rollback

- Rollback activates a prior immutable published version through governance.
- Rollback never edits history.
- Rollback must create audit evidence and explain scope, actor, reason, and effective time.
- Decisions already made remain bound to their original versions.

### Review Process

Every policy/rule/catalog change must answer:

- Which facts are required?
- Who owns each fact?
- What happens when facts are missing?
- Is the rule provider-neutral?
- Does it preserve Runtime and Adapter boundaries?
- Is it deterministic?
- Is it replayable?
- Are reason codes stable?
- Does audit capture enough evidence?
- Does it require migration of policy versions, fact schema versions, or evaluation versions?
- Can it be rolled back without rewriting history?

## Versioning

Versioning is immutable. Published versions cannot be edited. Corrections create new versions.

| Version Type | Responsibility |
|---|---|
| Policy Version | Immutable policy definition, domain, composition, required facts, evaluation order, fail-closed behavior, and publication metadata. |
| Rule Version | Immutable rule predicate/calculation, dependencies, priority, effect, reason codes, and audit level. |
| Catalog Version | Immutable set of catalog entries and rule identities available for policy composition. |
| Fact Schema Version | Immutable schema contract for facts available in policy context. |
| Evaluation Version | Immutable evaluation semantics for ordering, short-circuit, composition, dependency resolution, and conflict handling. |
| Audit Version | Immutable audit schema for recorded inputs, facts, rule traces, decision, reason codes, actor, timestamp, and replay data. |
| Replay Version | Immutable replay contract describing required policy/rule/catalog/fact/evaluation/audit versions and compatibility behavior. |

Version rules:

1. Runtime evaluation must record all versions used.
2. Replaying a decision must use the exact versions recorded, or fail closed with a replay-incomplete result.
3. Fact schema changes require compatibility declaration.
4. Rule threshold changes create new rule versions.
5. Policy composition changes create new policy versions.
6. Catalog additions create new catalog versions.
7. Deprecation does not remove replay support.
8. Rollback activates a prior version; it does not edit that version.

## Audit

Policy audit is append-only and replay-safe.

### Required Audit Fields

| Field | Purpose |
|---|---|
| Audit Id | Stable audit identity for the evaluation. |
| Operation Intent | What Runtime or governed system requested. |
| Actor/System | User, system, job, runtime process, reviewer, or recovery actor. |
| Timestamp | Evaluation timestamp and effective timestamp. |
| Input Snapshot | Immutable policy context snapshot reference or hash. |
| Facts Used | Fact families and specific fact identifiers/versions used. |
| Facts Missing | Required facts absent or invalid. |
| Rules Executed | Rule ids, versions, order, inputs, outcome, effect, reason code. |
| Rules Skipped | Rule ids, versions, skip reason, dependency or short-circuit source. |
| Decision | Final policy decision and decision severity. |
| Reason Codes | Stable machine-readable reason codes. |
| Versions | Policy, rule, catalog, fact schema, evaluation, audit, and replay versions. |
| Source Authority | Proof that facts came from current authority, immutable snapshots, or admitted evidence. |
| Context Hash | Hash of the normalized input snapshot. |
| Decision Hash | Hash of the normalized output decision. |
| Replay Metadata | Required artifacts and compatibility constraints for replay. |

### Audit Outcomes

Audit must support:

- Input snapshot inspection.
- Facts used.
- Rules executed.
- Rules skipped.
- Decision and reason codes.
- Version provenance.
- Timestamp and actor.
- Replay.
- Governance trace.
- Manual review trace.
- Recovery trace.
- Denial/quarantine/review explanation.

### Replay

Replay is valid only when:

- The exact policy version is available.
- The exact rule versions are available.
- The catalog version is available.
- The fact schema version is available.
- The evaluation version is available.
- The audit and replay contracts are available.
- The original input snapshot or exact normalized snapshot hash/materialization is available.

If replay artifacts are missing, replay fails closed and records `REPLAY_VERSION_INCOMPLETE`.

## Architecture Rules

1. Facts are authoritative inputs.
2. Rules never own facts.
3. Policies compose rules.
4. Governance owns policy and rule lifecycle.
5. Runtime never edits policies.
6. Adapters never edit rules.
7. Policies are deterministic for the same facts, versions, and evaluation semantics.
8. Policies are replayable.
9. Policies are versioned.
10. Facts are immutable snapshots during evaluation.
11. Missing required facts fail closed.
12. Frontend state is never policy authority.
13. Cache is never policy authority.
14. Projections are never policy authority.
15. Raw provider payload replay is never policy authority.
16. Provider capability is not business eligibility.
17. Provider constraints are policy inputs, not runtime branches.
18. Runtime executes only policy-approved actions.
19. Runtime may enforce policy decisions but must not hide business rules.
20. Adapters translate, expose capabilities/constraints, provide evidence candidates, and execute provider operations only under Runtime command.
21. Manual review is a policy outcome and governed workflow, not a frontend or adapter invention.
22. Silent fallback is forbidden.
23. Rollback activates an immutable prior version; it does not rewrite decisions.
24. Deprecated policies and rules remain replayable.
25. Future payment policy growth must enter through facts, rules, catalog versions, policy versions, governance, and audit without Runtime redesign.

## Certification Decision

Policy Contracts completed: **Yes**.

Facts Model completed: **Yes**.

Rule Model completed: **Yes**.

Policy Model completed: **Yes**.

Rule Catalog completed: **Yes**.

Fact Sources completed: **Yes**.

Governance completed: **Yes**.

Versioning completed: **Yes**.

Audit completed: **Yes**.

Provider-neutral: **Yes**.

Replay-safe: **Yes**.

Future-proof: **Yes**. Future policies, rules, facts, providers, methods, countries, currencies, marketplace rules, affiliate rules, risk rules, fraud rules, compliance rules, settlement rules, refund/capture/void rules, retry/recovery rules, and manual-review rules enter through governed versions and authoritative facts without Runtime redesign.

Production-certifiable: **Yes, as architecture.** Runtime integration, policy persistence, governance UI, rule authoring UI, operational controls, migration strategy, and provider-specific policy regression suites remain later phases.

Recommended next phase: **PAYMENT-1.2 Policy Persistence, Governance Workflow, and Replay Test Architecture**, followed by implementation planning for policy storage, authoring, publication, runtime invocation, and regression certification.

## Success Answers

Policy Contracts completed? **Yes**.

Facts Model completed? **Yes**.

Rule Catalog completed? **Yes**.

Governance completed? **Yes**.

Versioning completed? **Yes**.

Audit completed? **Yes**.

Provider-neutral? **Yes**.

Replay-safe? **Yes**.

Future-proof? **Yes**.

Production-certifiable? **Yes, as certified architecture.**

Recommended next phase? **PAYMENT-1.2 Policy Persistence, Governance Workflow, and Replay Test Architecture.**
