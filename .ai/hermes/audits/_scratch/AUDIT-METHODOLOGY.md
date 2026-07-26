# SHOPIXY ERP — ACCOUNTING ARCHITECTURE AUDIT METHODOLOGY

You are one of several independent domain auditors contributing to the canonical
ACCOUNTING-ARCHITECTURE-AUDIT.md. Read this file first and follow it exactly.

## Golden Rules
1. IMPLEMENTATION is ALWAYS the source of truth. Never infer behavior. Never assume ERP
   standards are implemented. If documentation and implementation disagree, IMPLEMENTATION WINS
   — document the discrepancy as a finding.
2. If a claimed behavior does not exist in source code, state NOT IMPLEMENTED explicitly.
3. Every finding MUST be backed by real code you read. Include exact file:line references.
4. Never speculate. Never invent missing ERP features. Never recommend rewrites without evidence.
5. Each auditor works independently. NEVER read other auditors' scratch files or summaries.
   Only implementation, only your canonical doc, only evidence.

## Canonical doc for your domain
Read the FLOW-ACC-XX doc given in your task prompt (it is the only doc input for your domain).

## Known Architectural Principles (validate implementation against these)
- General Ledger is the financial source of truth.
- AccountLedger is the party (customer/supplier) balance source of truth.
- Financial Reports are read-only projections.
- Statement Of Account is an AccountLedger projection.
- Cashbox never owns balances.
- FinancialPostingService owns journal creation.
- AccountingOutbox owns asynchronous accounting.
- Adjustments create new journals (never mutate historical journals).
- Historical journals are immutable.
- Business Intent is stored separately from accounting truth.
- Reports never mutate state.
- Every financial mutation must preserve double-entry accounting.
- Tenant isolation must always be preserved.
- Idempotency must always be preserved.
- Financial truth must never be duplicated.
- Every mutation must remain auditable.
- Every financial workflow must remain deterministic.

## What you must verify (all that apply to your domain)
Business Rules, Runtime, Controllers, Services, Entities, DTOs, Outbox, Ledger, Journal,
FinancialPosting, CQRS, DDD, Aggregate ownership, Read models, Transactions, Locks, Isolation
levels, Idempotency, Audit, Authorization, Validation, Tenant isolation, Async pipeline, Error
handling, Browser workflow, Missing workflow, Dead code, Unused entities, Incomplete
implementation, Broken invariants.

## Finding schema (every finding must contain ALL of these)
- Finding ID (e.g. F-06-01)
- Title
- Severity (CRITICAL / HIGH / MEDIUM / LOW / ARCHITECTURAL IMPROVEMENT)
- Evidence (what you observed)
- Code references (file:line, at least one real reference)
- Documentation references (doc section/page)
- Implementation proof (the actual code behavior, quoted or described precisely)
- Business impact
- Financial impact
- Accounting impact
- Architecture impact
- Operational impact
- Risk explanation
- Root cause
- Recommended solution
- Migration impact
- Regression risk
- Priority (P0/P1/P2/P3)
- Confidence (High / Medium / Low)

## Required validation matrix (answer YES/NO for every finding)
Double Entry, CQRS, DDD, ERP, Accounting, Auditability, Source Of Truth, Tenant Isolation,
Idempotency, Financial Integrity, Eventual Consistency, Aggregate Boundary.

## Severity definitions
- CRITICAL: Financial corruption possible; accounting truth violated; double-entry broken;
  journal/ledger inconsistency; source-of-truth violation; tenant isolation violation; audit
  integrity violation.
- HIGH: Architectural violation; incorrect ownership; missing validation; missing idempotency;
  missing financial/accounting control; dangerous async behavior; race condition; concurrency issue.
- MEDIUM: Design inconsistency; DDD boundary leak; CQRS inconsistency; incomplete workflow;
  poor UX affecting accounting.
- LOW: Minor implementation issue; naming issue; code duplication; documentation mismatch.
- ARCHITECTURAL IMPROVEMENT: No defect; can significantly improve architecture.

## Mandatory deep-investigation questions (answer each explicitly in your report)
- Can historical journals be modified? (immutability)
- Can ledger truth diverge from GL?
- Can AccountLedger diverge from reality?
- Can Reports become inconsistent?
- Can Daily Closing produce accounting inconsistencies?
- Can Adjustments bypass FinancialPostingService?
- Can Expenses bypass Outbox?
- Can Payments create inconsistent balances?
- Can Statement Of Account disagree with General Ledger?
- Can asynchronous posting lose financial integrity?
- Can idempotency fail?
- Can replay corrupt accounting?
- Can race conditions create duplicate journals?
- Can balances become negative unexpectedly?
- Can orphan records exist?
- Can audit records become inconsistent?

## Output instructions
1. Write your COMPLETE findings (every finding in the schema above, plus the deep-investigation
   answers, plus a short Strengths list and a short Missing-implementation list) to the scratch
   file path given in your task prompt using the write_file tool.
2. Then return a concise summary message: domain, total finding count by severity, the top 3 most
   severe findings (ID + title + severity + one-line proof), and whether implementation generally
   matches the canonical doc.

Do NOT return the full findings inline (they will be read from the scratch file).
Return only the concise summary.
