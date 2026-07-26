# PASS 15.5B Forensic Evidence Hardening Certification

Date: 2026-06-01

## Merchant Lifecycle Findings

CommerceHub listing publication, update, unpublish, merchant bulk operations, governance suppression, restore, and rejected restore now append SQL lifecycle evidence with tenant, listing, product, variant, actor, timestamp, correlation id, request id, reason, and JSON details. Lifecycle records are guarded as append-only evidence.

## Recovery Ledger Findings

Super Admin global reindex, tenant reindex, catch-up reindex, listing verification enqueue, tenant verification enqueue, and background CommerceHub auto-healing outcomes now append durable SQL recovery evidence. Records include operation type, actor or system identity, tenant/listing scope, start/end time, outcome, reason, correlation id, request id, and details.

## Retention Findings

The forensic API exposes an owned retention inventory. Audit, governance, finance ledger, merchant lifecycle, recovery, and dead-letter evidence are retain policies. Published outbox and processed-event rows remain explicitly transient only after durable evidence exists. Deployment must still configure backup and archive retention.

## Incident Search Findings

`api/super-admin/marketplace-forensics/search` provides one operator search surface across actor, tenant, listing, order, refund, payout, correlation id, request id, operation id, and date range. It returns merchant lifecycle, recovery, and generic audit evidence. Financial entity search remains based on indexed audit references plus existing finance consoles.

## Export Findings

`api/super-admin/marketplace-forensics/export` exports audit, governance, financial, lifecycle, and recovery evidence as a bounded transfer artifact. It explicitly does not claim WORM immutability after transfer. The receiving SIEM or object store must enforce WORM retention.

## Audit Durability Findings

Generic tenant audit now persists through an isolated SQL scope first. Database failures fall back to the retry queue, and retry depth/count are visible through `api/super-admin/marketplace-forensics/health`. Super Admin audit remains isolated SQL persistence. SQL append-only guards cover both new ledgers and the PASS 15.5 evidence families.

## Compliance Findings

Marketplace compliance maturity improves materially: merchant lifecycle and certified CommerceHub recovery history are durable, search is practical, export transfer exists, retention ownership is explicit, and audit fallback backlog is visible. Database-role restrictions and deployment-enforced WORM/SIEM retention remain external controls.

## Reconstruction Findings

- Merchant listing lifecycle: reconstructable for wired merchant and governance mutation paths.
- Listing suppression and restore: reconstructable.
- Vendor suspension and tenant suspension: reconstructable through existing governance and Super Admin audits.
- Price override change: reconstructable through lifecycle and PASS 13.7 evidence.
- Refund, payout, and settlement: reconstructable through existing SQL/ledger evidence and bounded export.
- Replay and recovery incident: reconstructable for certified CommerceHub operations and auto-healing paths.

## Chaos Findings

The pass hunted evidence deletion, overwrite, actor loss, correlation loss, replay without evidence, recovery without evidence, retention ambiguity, search gaps, and export overclaims. New SQL ledger rows are append-only at the EF boundary. External database-role enforcement remains required because EF guards cannot govern privileged direct SQL.

## Monitoring Findings

The forensic health endpoint reports tenant-audit retry depth/count and recent lifecycle/recovery evidence volume. Existing operations diagnostics continue reporting projection and auto-healing state. Deployment alert rules still need to scrape and alert on these values.

## Runtime Hardening Performed

- Added append-only merchant lifecycle and CommerceHub recovery-operation SQL ledgers.
- Wired merchant, bulk, governance, Super Admin recovery, and auto-healing paths.
- Changed generic tenant audit from memory-first to isolated SQL-first persistence with retry fallback.
- Added unified forensic search, bounded export, retention inventory, and evidence-health APIs.
- Added additive migration `20260601153000_CommerceHubForensicEvidenceHardening`.

## Regression Freeze Added

Added `MarketplaceForensicEvidenceCertificationTests`.

## Downstream Closure Update

PRODUCTION READINESS CLOSURE 1 now implements PostgreSQL append-only triggers, evidence-owner separation, runtime writer/reader roles, read-only maintenance access, irreversible rollback protection, and deployment verification SQL. See `docs/architecture/evidence-database-hardening-certification-findings.md`.

Environment rollout evidence remains required: apply role provisioning, run verification, retain results, and alert on PostgreSQL forensic-violation markers.

## Remaining Debt

- Apply and verify the implemented PostgreSQL evidence-role provisioning in each deployed environment.
- Configure external SIEM or object storage with WORM retention and export delivery automation.
- Add deployment alert rules for audit retries, evidence silence, retention jobs, and cross-evidence parity.
- Extend recovery ledgers to non-CommerceHub hosted recovery workers.
- Replace the in-memory audit retry fallback with a durable SQL audit outbox if audit writes must survive a process crash while PostgreSQL is unavailable.

## Explicit Answers

- Can merchant lifecycle be reconstructed? Yes for the wired CommerceHub merchant and governance paths.
- Can recovery incidents be reconstructed? Yes for the certified CommerceHub paths.
- Is evidence retention sufficient? Partially. SQL retain policies are explicit; deployment archive guarantees remain required.
- Is forensic search sufficient? Sufficient for core CommerceHub incident reconstruction; deeper finance joins remain operational maturity work.
- Is audit durability sufficient? Improved materially. SQL-first persistence is trustworthy during normal operation; PostgreSQL-outage process-crash durability still needs an audit outbox.
- Are compliance controls now sufficient? Not for final production certification without external WORM/SIEM and database-role controls.
- Are any critical forensic gaps still present? Yes. Deployment-enforced WORM/SIEM retention, database-role restrictions, and outage-surviving generic audit fallback remain.
- Is CommerceHub forensic readiness production-ready? No. The remaining controls are explicit and bounded.
