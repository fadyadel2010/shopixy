# CLOSURE 1 Evidence Database Hardening Certification

Date: 2026-06-01

## Scope

This closure moves certified append-only forensic evidence protection from EF-only enforcement to PostgreSQL enforcement. It does not change marketplace workflows, authority ownership, governance, finance, search, or recovery behavior.

## Evidence Inventory

| Evidence family | PostgreSQL tables |
|---|---|
| Generic and tenant audit | `AuditLogs`, `StoreConfigAuditEntries` |
| Governance audit | `UserGovernanceAuditLogs` |
| CommerceHub finance and reconciliation evidence | `CommerceHubFinancialTransactions`, `CommerceHubFinancialAuditLogs`, `CommerceHubFinancialOperationAudits`, `CommerceHubFinancialReconciliationReports`, `CommerceHubVendorSettlements`, `CommerceHubVendorPaymentAuditLogs` |
| Core accounting evidence | `LedgerEntries`, `CommerceAccountingAudits` |
| Dead-letter archive | `MarketplaceEventOutboxDeadLetterArchives` |
| Merchant lifecycle evidence | `CommerceHubMerchantLifecycleLedgerEntries` |
| Recovery-operation evidence | `CommerceHubRecoveryOperationLedgerEntries` |

Transactional payout, refund, settlement, payment, and order rows remain lifecycle authority records. They are not reclassified as immutable forensic ledgers where certified workflows still require controlled state transitions.

## Database Role Matrix

| Role | SELECT | INSERT | UPDATE | DELETE | TRUNCATE | ALTER / DROP |
|---|---:|---:|---:|---:|---:|---:|
| Application role via `pointofsale_evidence_writer` | Yes | Yes | No | No | No | No |
| Background worker role via `pointofsale_evidence_writer` | Yes | Yes | No | No | No | No |
| Reporting role via `pointofsale_evidence_reader` | Yes | No | No | No | No | No |
| Maintenance role via `pointofsale_evidence_maintenance` | Yes | No | No | No | No | No |
| Migration role via `pointofsale_evidence_migration` | Reviewed schema administration only | Reviewed schema administration only | Reviewed forward migration only | Reviewed forward migration only | Reviewed forward migration only | Yes, through evidence owner membership |
| Database administration / superuser | Deployment break-glass authority | Deployment break-glass authority | Deployment break-glass authority | Deployment break-glass authority | Deployment break-glass authority | Deployment break-glass authority |

`ProvisionForensicEvidenceRoles.sql` transfers evidence-table ownership to `pointofsale_evidence_owner`. Runtime identities are rejected if they inherit evidence-owner or migration authority.

## Append-Only Enforcement Findings

Migration `20260601190000_EvidenceDatabaseHardening` installs `forensic.reject_append_only_evidence_mutation()` and a statement trigger on every inventoried table. PostgreSQL rejects `UPDATE`, `DELETE`, and `TRUNCATE` with SQLSTATE `55000` and the marker `FORENSIC_APPEND_ONLY_VIOLATION`. Inserts remain supported.

The trigger is the final database authority even if application code bypasses EF with direct SQL. Runtime ACLs add defense in depth after deployment role provisioning.

## Migration Safety Findings

The migration fails if an inventoried table is missing, preventing silent partial protection. Its `Down` migration is deliberately irreversible and fails with `FORENSIC_APPEND_ONLY_ROLLBACK_PROHIBITED`; future evidence schema evolution must use reviewed forward migrations.

Future privileged migrations can still deliberately alter or remove protections. `EvidenceDatabaseHardeningCertificationTests` freezes the inventory, trigger, ACL-provisioning, verification, and irreversible-rollback contract so accidental removal is visible in CI.

## Operational Tooling Findings

Reviewed repository SQL tooling contains webhook redaction and business idempotency cleanup paths, but no evidence-ledger update, delete, or truncate path. Maintenance identities receive read-only evidence access. Any future evidence retention or archival tooling must export/archive first and use a reviewed privileged migration or externally governed retention procedure.

## Runtime Bypass Findings

Reviewed runtime raw SQL and bulk-update paths do not target the evidence inventory. If future runtime code issues direct evidence `UPDATE`, `DELETE`, or `TRUNCATE`, PostgreSQL rejects it before mutation. EF append-only guards remain in place as an earlier application boundary.

## Forensic Integrity Findings

`VerifyForensicEvidenceDatabaseHardening.sql` reports role privileges and installed triggers, fails on inventory or ACL drift, and actively probes direct SQL update/delete rejection against representative evidence tables. Run it after migrations and role provisioning in each environment.

## Chaos Findings

The closure explicitly covers accidental runtime SQL misuse, maintenance operator misuse, bulk mutation, delete, truncate, partial migration inventory drift, and rollback. A migration identity or database superuser can still perform reviewed break-glass schema changes; PostgreSQL cannot prevent its own superuser from deliberately disabling protections.

## Monitoring Findings

Alert on PostgreSQL errors containing `FORENSIC_APPEND_ONLY_VIOLATION`, `FORENSIC_APPEND_ONLY_INVENTORY_DRIFT`, `FORENSIC_ROLE_SEPARATION_VIOLATION`, `FORENSIC_APPEND_ONLY_VERIFICATION_FAILED`, or `FORENSIC_APPEND_ONLY_ROLLBACK_PROHIBITED`. Run the verification SQL after migrations and on a scheduled deployment-control cadence. External alert-pack installation remains deployment work.

## Runtime Hardening Performed

- Added irreversible PostgreSQL append-only triggers for the certified evidence inventory.
- Revoked public mutation privileges in the EF migration.
- Added deploy-time evidence owner, writer, reader, migration, and maintenance role provisioning.
- Added post-deployment ACL, trigger, and direct-SQL verification.

## Regression Freeze Added

Added `EvidenceDatabaseHardeningCertificationTests`.

## Explicit Answers

1. Can application roles update evidence? No, after role provisioning.
2. Can application roles delete evidence? No, after role provisioning.
3. Can runtime code bypass protections? No for `UPDATE`, `DELETE`, or `TRUNCATE`; PostgreSQL rejects direct SQL attempts.
4. Can maintenance tooling mutate evidence? Not through the maintenance role. It is read-only.
5. Can future migrations remove protections? Only through reviewed privileged forward migration or database break-glass authority. Rollback fails closed.
6. Does the database now enforce append-only behavior? Yes after applying the migration.
7. Are critical evidence-mutation risks still present? No for ordinary application, worker, reporting, and maintenance identities after provisioning. Superuser misuse remains a deployment break-glass risk.
8. Is PASS 15.6 Launch Blocker #1 eliminated? PASS 15.6 Launch Blocker #1 is eliminated after deployment verification. Repository implementation is complete; production still must apply provisioning and preserve verification evidence.

## Remaining Debt

- Configure and verify the role script in each deployed environment.
- Install alerts for PostgreSQL forensic violation markers.
- Configure external WORM/SIEM retention and automated delivery.
- Replace process-memory tenant-audit fallback with outage-surviving durable handoff.
- Produce backup, restore, archive-retention, and operator-rehearsal evidence.
