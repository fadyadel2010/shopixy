# PASS M11 CommerceHub Return Logistics Authority Certification Findings

Date: 2026-06-11

Status: Updated 2026-09-03. The original platform-logistics assignment was superseded; the owning POS vendor now owns return logistics and Super Admin return access is read-only.

## Findings

- Return findings: PostgreSQL `CommerceHubReturnRequests`, linked SQL refund requests, immutable marketplace order/sub-order/item snapshots, SQL audit logs, and inventory receipt rows own return identity, workflow, evidence, receipt, inspection quantities, completion, and lineage.
- Eligibility findings: return creation is reachable only from approved SQL refund requests after customer refund eligibility has already proven SQL customer/order-item ownership, quantity bounds, current item eligibility, and refund workflow state.
- Ownership findings: customer reads filter by authenticated SQL customer id; all logistics and receipt commands require the trusted tenant `VendorId`; Super Admin can monitor but cannot mutate return state. Foreign customers and vendors cannot access or mutate inspected return authority.
- Workflow findings: vendor-managed return states are transition-policy bounded, expected-status fenced, row-locked, serializable, audited, and terminal-state aware.
- Shipment findings: pickup scheduling, picked-up, in-transit, delivered-to-vendor, and failure transitions require owning-vendor POS authority. Backend DTO grants are presentation evidence only and each command re-enters SQL authority.
- Receipt findings: vendor receipt confirmation remains tenant-scoped and requires the SQL return to already be `DeliveredToVendor`. Confirmed receipt is idempotent for matching quantities and rejects divergent replay.
- Inspection findings: received resellable/damaged quantities are SQL persisted, bounded by approved return quantity and delivered order-item/reservation evidence, and applied through inventory return receipt authority.
- Return/refund lineage findings: returns can move refund requests to `WaitingForReturn` or `AwaitingPayout` when configured SQL milestones are reached, but returns never execute refunds directly. Refund execution remains owned by M4 refund authority.
- Projection findings: no inspected MongoDB, Redis, Elasticsearch, search, analytics, or frontend projection path owns return mutation authority. Return reads and critical state derive from SQL.
- Isolation findings: vendor APIs use trusted tenant context and return `404` for foreign vendor returns; customer APIs filter by authenticated customer id; the Super Admin return API is platform-scoped and GET-only.
- Concurrency findings: logistics and receipt mutations run in serializable SQL transactions with `FOR UPDATE` return/refund row locks. Receipt inventory side effects use idempotent event evidence and retry on serialization failures.
- Historical findings: audit logs, return timestamps, receipt quantities, refund status changes, inventory inbox rows, and stock movements preserve reconstructable SQL lineage. Replay must collapse against current SQL state.
- Adversarial findings: a vendor cannot use another tenant's return, a stale status, an illegal step, or a DTO grant alone to unlock a refund milestone. Super Admin has no return mutation endpoint.

## Runtime Hardening Performed

- Added owning `vendorId` and locked `expectedStatus` checks to pickup scheduling, picked-up, in-transit, delivered, and failed service commands.
- Removed Super Admin override/failure routes and UI controls. POS/Admin now exposes the policy-granted logistics, failure, and receipt actions for the owning vendor.

## Exact Runtime Files Modified

- `PointOfSale.API/Services/CommerceHub/ReturnManagementService.cs`
- `PointOfSale.API/Controllers/CommerceHubRefundsController.cs` (compile-only preservation of existing M4 workflow-required responses)

## Regression Freezes Added

- `PointOfSale.Tests/CommerceHub/ReturnManagementServiceTests.cs`
- `PointOfSale.Tests/Architecture/CommerceHubReturnAuthorityCertificationTests.cs`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/commercehub-return-logistics-authority-certification-findings.md`

## Explicit Answers

- Who owns return authority? PostgreSQL return workflow truth through `CommerceHubReturnRequests`, linked refund workflow rows, marketplace order/sub-order/item snapshots, audit rows, inventory receipt evidence, and platform/vender-scoped service boundaries.
- Can returns be forged? No inspected path can forge return identity; return rows are created from approved SQL refund workflow only and are unique per refund.
- Can customers return products they never purchased? No inspected path permits that; eligibility inherits SQL customer/order-item ownership from refund request authority.
- Can workflow steps be bypassed? No after hardening; vendor/POS transitions require current expected status and remain policy-bound under SQL locks.
- Can tracking be spoofed? Client tracking fields cannot create milestones by themselves. Accepted vendor logistics tracking remains audited SQL evidence, not financial truth.
- Can inspections be bypassed? No inspected path bypasses receipt/inspection; vendor confirmation requires delivered SQL state and bounded receipt quantities.
- Can returns create refunds directly? No. Returns provide milestone evidence only; refund execution remains separate M4 authority.
- Can tenant leakage occur? No inspected path exposes or mutates foreign return authority after SQL customer/vendor/SuperAdmin scoping.
- Were runtime code changes required? Yes. Logistics authority was moved from Super Admin to the owning vendor at the controller, service, DTO, and frontend boundaries.
- Is CommerceHub Return Logistics Authority production-certifiable? Yes, for inspected runtime authority paths. Operational chaos proof and production monitoring remain maturity work.
