# Evidence Database Hardening Runbook

Date: 2026-06-01

## Purpose

Apply and verify PostgreSQL append-only forensic evidence enforcement after application migrations.

## Deployment Order

1. Apply EF migrations with the dedicated migration login, including `20260601190000_EvidenceDatabaseHardening`.
2. Run `PointOfSale.Infrastructure/Scripts/ProvisionForensicEvidenceRoles.sql` with a PostgreSQL administration identity and the environment login names supplied as psql variables.
3. Run `PointOfSale.Infrastructure/Scripts/VerifyForensicEvidenceDatabaseHardening.sql`.
4. Retain the verification output with deployment evidence.
5. Alert on PostgreSQL errors containing `FORENSIC_APPEND_ONLY_` or `FORENSIC_ROLE_SEPARATION_VIOLATION`.

## Expected Runtime Access

| Identity | Evidence access |
|---|---|
| Application login | `SELECT`, `INSERT` |
| Background-worker login | `SELECT`, `INSERT` |
| Reporting login | `SELECT` |
| Maintenance login | `SELECT` |
| Migration login | Reviewed schema administration through explicit role activation |

## Failure Handling

- `FORENSIC_APPEND_ONLY_INVENTORY_DRIFT`: stop deployment and reconcile missing or renamed evidence tables.
- `FORENSIC_ROLE_SEPARATION_VIOLATION`: stop deployment and remove runtime membership in privileged evidence roles.
- `FORENSIC_APPEND_ONLY_VERIFICATION_FAILED`: stop deployment and repair ACL or trigger drift.
- `FORENSIC_APPEND_ONLY_VIOLATION`: treat as an application defect, operator mistake, or attempted forensic mutation. Preserve PostgreSQL logs and investigate.
- `FORENSIC_APPEND_ONLY_ROLLBACK_PROHIBITED`: do not force rollback. Prepare a reviewed forward migration.

## Break Glass

Only the database administration identity or reviewed migration identity may alter evidence protections. Record actor, reason, incident id, exact SQL, start/end timestamps, and post-change verification output in the external incident system.
