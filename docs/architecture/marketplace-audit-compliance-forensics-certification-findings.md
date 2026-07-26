# PASS 15.5 Marketplace Audit, Compliance & Forensics Certification

Date: 2026-06-01

## Scope

This certification asks whether a future investigator can reconstruct CommerceHub reality after a dispute, fraud claim, regulator inquiry, tax audit, vendor complaint, customer complaint, operator misconduct report, or financial incident. Prior PASS 11.1, PASS 11.5, PASS 12.6, PASS 13.7, and PASS 14.1 through PASS 15.4 remain authoritative.

## Evidence Inventory

- Generic SQL audit: `AuditLogs`, including tenant admin activity and Super Admin request/domain records.
- Governance evidence: `UserGovernanceAuditLogs`, SQL tenant lifecycle fields, listing governance state, and Super Admin governance audit records.
- Merchant listing evidence: SQL `CommerceHubListings`, publication state, category/attribute snapshots, price override attribution fields, mutation versions, and price override integration events.
- Financial evidence: marketplace orders/sub-orders, payments and attempts, vendor earnings, refunds and executions, payouts and payout items, vendor settlements, `CommerceHubFinancialTransactions`, `CommerceHubFinancialAuditLogs`, `CommerceHubFinancialOperationAudits`, `CommerceHubVendorPaymentAuditLogs`, accounting journals, `LedgerEntries`, `CommerceAccountingAudits`, and accounting outbox rows.
- Recovery evidence: marketplace event outbox rows, failed-event dead-letter archives, projection outbox rows, stock-sync outbox rows, processed-event fences, structured logs, and convergence telemetry.
- Supporting evidence: refund evidence media fingerprints, payout handoff/evidence fields, idempotency keys, correlation ids, request ids, timestamps, actor snapshots, and SQL reconciliation reports.

## Operator Accountability Findings

Super Admin governance, operations, finance, emergency, and break-glass-adjacent API actions capture actor identity, request id, timestamp, endpoint, method, result, and domain details. Runtime hardening now persists Super Admin audit rows through an isolated SQL scope instead of the lossy process-memory audit queue. Deployment-level config changes to emergency stops still require external infrastructure change audit.

## Merchant Accountability Findings

Merchant CommerceHub price override changes are reconstructable through actor metadata, reason, correlation id, mutation version, and immutable override events. Current listing rows prove latest publication/category/attribute intent, but a complete immutable merchant listing lifecycle ledger is absent. Publication, unpublish, category, attribute, and bulk-operation historical reconstruction is incomplete once transient outbox retention expires.

## Governance Evidence Findings

Vendor governance actions are append-only through `UserGovernanceAuditLogs`. Listing suppression/restore and tenant suspension/restore are captured in durable Super Admin audit rows with reason and actor. Current SQL governance state remains authoritative. A unified governance timeline search across listing, tenant, and vendor evidence is still missing.

## Financial Evidence Findings

Certified money flows are reconstructable. Orders, payment attempts, earnings, commissions, refunds, payouts, settlements, ledger transactions, accounting audits, journals, outboxes, correlation ids, fingerprints, and idempotency keys form evidence chains. PASS 15.5 extends EF append-only protection to financial audit rows, core ledger entries, vendor-payment audits, and accounting posting audits.

## Recovery Evidence Findings

Failed marketplace events retain dead-letter archive evidence and correlation ids. Reindex and manual verification actions have durable Super Admin audit records. Background auto-healing, replay, and recovery execution still lack a unified durable operation ledger with start, finish, actor/system identity, reason, scope, outcome, and correlation. Structured logs and telemetry are useful but are not sufficient regulatory evidence.

## Reconstruction Findings

- Listing Lifecycle: partially reconstructable; latest SQL state and override events exist, but full immutable merchant listing history is incomplete.
- Merchant Pricing Change: reconstructable for PASS 13.7 overrides.
- Listing Suppression: reconstructable for Super Admin governance actions.
- Vendor Suspension: reconstructable through governance audit rows.
- Refund: reconstructable for certified SQL/ledger paths.
- Payout: reconstructable for certified SQL/ledger paths.
- Settlement: reconstructable for certified SQL/ledger paths.
- Replay Incident: partially reconstructable from outbox archives, logs, and telemetry.
- Recovery Incident: partially reconstructable; no unified durable recovery-operation ledger exists.

## Audit Integrity Findings

Runtime hardening adds central EF append-only guards for `AuditLog`, `CommerceHubFinancialAuditLog`, `CommerceHubVendorPaymentAuditLog`, `CommerceAccountingAudit`, `StoreConfigAuditEntry`, `LedgerEntry`, and `MarketplaceEventOutboxDeadLetterArchive`. Existing guards continue protecting CommerceHub financial transactions, safety audits, reconciliation reports, settlements, and user governance audits. Database-role restrictions, WORM export, cryptographic chaining, and external SIEM retention remain unimplemented.

## Compliance Findings

Financial audit readiness is strong for certified SQL/ledger flows. Marketplace, operator, tax, and regulatory readiness remain partial because merchant lifecycle history, recovery-operation history, retention policy enforcement, and externally immutable export are incomplete.

## Retention Findings

Retention is insufficient for production forensic certification. Marketplace published outbox defaults to 14 days, processed-event evidence to 30 days, accounting processed outbox to 14 days, and failed accounting outbox to 30 days. Dead-letter archive retention is stronger but does not replace durable business-event history. No formal retention schedule protects every audit, ledger, governance, operational, and financial evidence family.

## Searchability Findings

Tenant audit search/export, vendor finance audit timelines, governance user timelines, and marketplace dashboards exist. Operators still lack one incident search surface spanning actor, tenant, listing, order, refund, payout, correlation id, request id, recovery operation, and time range.

## Frontend / Backend Alignment Findings

Displayed tenant audit, governance, operations, and finance evidence is backend-derived. Existing frontends do not claim a complete forensic timeline. A unified incident-forensics workspace remains missing.

## Incident Forensics Findings

Core finance incidents can answer who, what, when, why, and how through SQL/ledger evidence chains. Governance incidents can usually answer those questions for Super Admin and vendor-governance actions. Merchant listing and background recovery incidents cannot always answer all five questions after transient evidence expires.

## Chaos Findings

The review actively hunted unaudited money movement, unaudited governance, suppression, payout, refund, deletion, overwrite, missing correlation, missing actor, replay, and recovery gaps. No new certified money-movement bypass was found. Confirmed forensic gaps remain: lossy generic tenant audit processing on database failure, incomplete immutable merchant lifecycle history, short transient-evidence retention, and incomplete durable replay/recovery operation history.

## Monitoring Findings

Finance readiness gates, reconciliation states, health checks, convergence telemetry, structured logs, and dashboards detect many runtime failures. Missing audit-row detection, audit queue backlog/loss detection, retention-expiry reporting, and cross-evidence parity alerts are incomplete.

## Runtime Hardening Performed

- Added centralized EF append-only guards for major audit, accounting, ledger, and dead-letter evidence families.
- Moved Super Admin audit persistence from the process-memory queue to isolated SQL persistence.

## Regression Freeze Added

Added `MarketplaceAuditForensicsCertificationTests` and aligned `SuperAdminAuditServiceTests`.

## Architecture Updates

Critical forensic evidence is append-only at the reviewed EF boundary. Super Admin operator evidence is durable SQL evidence, not process-memory best effort. Full compliance certification remains blocked until retention, merchant lifecycle history, recovery-operation history, search, and external immutability controls mature.

## Remaining Debt

- Replace generic tenant audit queue loss behavior with durable outbox/retry semantics and backlog telemetry.
- Add immutable merchant listing lifecycle events for publish, update, category, attribute, bulk, and unpublish actions.
- Add a durable recovery-operation ledger for replay, reindex, repair, verification, and auto-healing.
- Define and enforce retention schedules for each evidence family.
- Add database-role restrictions, external immutable export/WORM retention, SIEM integration, cross-evidence parity alerts, and a unified incident search workspace.

## Explicit Answers

- Can operator actions be reconstructed? Partially. Super Admin API actions are durable; deployment config changes and background operations need stronger evidence.
- Can merchant actions be reconstructed? Partially. Override changes are reconstructable; the full listing lifecycle is not.
- Can governance actions be reconstructed? Yes for certified Super Admin and user-governance flows.
- Can money movement be reconstructed? Yes for certified SQL/ledger flows.
- Can replay/recovery actions be reconstructed? Partially.
- Is audit history trustworthy? Partially. Reviewed EF mutation paths are append-only, but database-role and external immutability controls are incomplete.
- Is evidence retention sufficient? No.
- Are compliance controls sufficient? No.
- Are there critical forensic gaps? Yes.
- Is CommerceHub audit/compliance/forensics production-ready? No.
