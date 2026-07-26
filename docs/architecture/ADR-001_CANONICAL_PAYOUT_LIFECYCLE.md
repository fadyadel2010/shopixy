# ADR-001 - Canonical Payout Lifecycle

**Status:** Accepted

**Date:** 2026-07

**Decision Type:** Architecture Decision Record

**Scope:** CommerceHub Finance / Payout Runtime

---

# 1. Decision

CommerceHub SHALL adopt the following payout lifecycle as the only canonical
runtime:

```text
Pending
  -> Processing
  -> Committed External Transfer Handoff
  -> Completed
  -> Operational Ledger Recording
  -> Persistence Commit
```

The direct transition:

```text
None
  -> Completed
```

is no longer considered a valid constitutional runtime.

---

# 2. Context

During CommerceHub Finance certification, implementation analysis revealed that
two independent payout execution models coexisted.

## Legacy Runtime (Architecture A)

```text
Super Admin
  -> Completed
  -> Ledger
  -> Save
```

This model directly records completed payouts after external money movement.

No durable processing state exists.

No committed external handoff exists.

## Canonical Runtime (Architecture B)

```text
Pending
  -> Processing
  -> Committed External Transfer Handoff
  -> Completed
  -> Ledger
  -> Save
```

This lifecycle introduces an explicit durable handoff boundary before terminal
completion.

---

# 3. Investigation History

The decision is based on the following certification sequence.

```text
CHF-CORE
  -> Finance Architecture Readiness Review
  -> FIN-001 Implementation Certification
  -> FIN-002 Architecture Resolution Decision
```

These investigations established that:

- Two payout execution architectures coexist.
- The coexistence is the result of architectural evolution rather than an
  isolated implementation bug.
- Database constraints, reconciliation, recovery, readiness, and financial
  safety mechanisms already implement Architecture B.
- The active Super Admin execution path still follows Architecture A.

---

# 4. Decision Rationale

Architecture B is adopted because it aligns with the constitutional finance
principles of CommerceHub.

Specifically:

- Durable external money movement.
- Explicit processing ownership.
- Replay-safe execution.
- Recovery before terminal recognition.
- Reconciliation-first financial safety.
- Serializable transaction boundaries.
- Ledger recorded only after verified completion.
- Future asynchronous payment provider compatibility.
- Multi-node operational safety.
- Accounting consistency.

Architecture A optimizes only the synchronous happy path while weakening
recovery, reconciliation, and external transfer guarantees.

---

# 5. Constitutional Principles

The canonical payout lifecycle SHALL satisfy the following principles.

## External Transfer Safety

No payout may become terminal before durable handoff commitment.

## Recovery

Every externally initiated transfer must have a recoverable runtime state.

## Reconciliation

Unknown external outcomes shall transition into reconciliation rather than
guessed completion.

## Financial Integrity

Ledger entries shall only represent terminal financial truth.

## Auditability

Every payout shall preserve:

- initiation
- reservation
- processing
- external handoff
- completion evidence
- ledger recording
- reconciliation history

## Idempotency

External transfer execution shall remain replay-safe.

---

# 6. Legacy Runtime Status

Architecture A is classified as:

**Legacy Runtime**

It exists only for historical compatibility.

No new implementation shall introduce:

```text
None
  -> Completed
```

direct transitions.

Legacy runtime shall be removed through the FIN-IMPLEMENTATION migration
program.

---

# 7. Implementation Direction

Implementation shall converge toward:

```text
Pending
  -> Processing
  -> Completed
```

using the existing canonical `CommerceHubPayoutService` lifecycle.

Super Admin payout execution shall orchestrate the canonical lifecycle instead
of constructing completed payouts directly.

Ledger recording authority shall remain exclusively inside the canonical
completion workflow.

---

# 8. Non-Goals

This ADR does not define:

- provider-specific implementation
- UI behavior
- API contract details
- migration mechanics
- database migration scripts

Those concerns are governed by the FIN-IMPLEMENTATION program.

---

# 9. Consequences

The following architectural consequences are accepted.

- Existing Super Admin payout execution must migrate.
- Existing payout tests must be updated.
- Legacy compatibility may temporarily exist during rollout.
- Database constraints remain authoritative.
- Recovery and reconciliation remain mandatory runtime components.

---

# 10. Superseded Runtime

The following execution model is superseded.

```text
Super Admin
  -> Completed
  -> Ledger
  -> Save
```

This runtime is no longer considered constitutional.

---

# 11. References

- CommerceHub Finance Discovery
- CHF-CORE
- FIN-001 - Super Admin Payout vs Terminal Handoff Certification
- FIN-002 - Architecture Resolution Decision
- FIN-IMPLEMENTATION - Canonical Runtime Migration
