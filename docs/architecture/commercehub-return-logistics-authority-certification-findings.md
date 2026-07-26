# PASS M11 CommerceHub Return Logistics Authority Certification Findings

Date: 2026-06-11

Status: CommerceHub Return Logistics Authority is production-certifiable for the inspected runtime paths after runtime hardening.

## Findings

- Return findings: PostgreSQL `CommerceHubReturnRequests`, linked SQL refund requests, immutable marketplace order/sub-order/item snapshots, SQL audit logs, and inventory receipt rows own return identity, workflow, evidence, receipt, inspection quantities, completion, and lineage.
- Eligibility findings: return creation is reachable only from approved SQL refund requests after customer refund eligibility has already proven SQL customer/order-item ownership, quantity bounds, current item eligibility, and refund workflow state.
- Ownership findings: customer reads filter by authenticated SQL customer id; vendor receipt confirmation requires trusted tenant `VendorId`; platform logistics runs through Super Admin authority. Foreign customers and vendors cannot access or mutate inspected return authority.
- Workflow findings: return states are transition-policy bounded, row-locked, serializable, audited, and terminal-state aware. Runtime hardening removed vendor/POS authority to advance shipment milestones or fail/override logistics state.
- Shipment findings: pickup scheduling, picked-up, in-transit, delivered-to-vendor, failed, and override logistics transitions now require platform return logistics authority. Vendor/POS DTO tracking cannot create shipment truth or trigger return milestones.
- Receipt findings: vendor receipt confirmation remains tenant-scoped and requires the SQL return to already be `DeliveredToVendor`. Confirmed receipt is idempotent for matching quantities and rejects divergent replay.
- Inspection findings: received resellable/damaged quantities are SQL persisted, bounded by approved return quantity and delivered order-item/reservation evidence, and applied through inventory return receipt authority.
- Return/refund lineage findings: returns can move refund requests to `WaitingForReturn` or `AwaitingPayout` when configured SQL milestones are reached, but returns never execute refunds directly. Refund execution remains owned by M4 refund authority.
- Projection findings: no inspected MongoDB, Redis, Elasticsearch, search, analytics, or frontend projection path owns return mutation authority. Return reads and critical state derive from SQL.
- Isolation findings: vendor APIs use trusted tenant context and return `404` for foreign vendor returns; customer APIs filter by authenticated customer id; Super Admin APIs are platform-scoped.
- Concurrency findings: logistics and receipt mutations run in serializable SQL transactions with `FOR UPDATE` return/refund row locks. Receipt inventory side effects use idempotent event evidence and retry on serialization failures.
- Historical findings: audit logs, return timestamps, receipt quantities, refund status changes, inventory inbox rows, and stock movements preserve reconstructable SQL lineage. Replay must collapse against current SQL state.
- Adversarial findings: the active vendor escalation gap was POS/Admin logistics advancement from DTO tracking. It is now closed by `RETURN_LOGISTICS_WORKFLOW_REQUIRED`; vendors cannot spoof pickup/delivery to unlock refunds.

## Runtime Hardening Performed

- Added `EnsurePlatformReturnLogisticsAuthority(...)` to `ReturnManagementService` and applied it to pickup scheduling, picked-up, in-transit, delivered, failed, and override logistics mutations.
- POS/Admin vendor return endpoints can no longer advance return logistics or fail/override status through DTO tracking. Vendor authority remains limited to SQL tenant-scoped receipt confirmation after platform delivery evidence.

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
- Can workflow steps be bypassed? No after hardening; vendor/POS logistics advancement is blocked and state transitions remain policy-bound under SQL locks.
- Can tracking be spoofed? Vendor/POS DTO tracking cannot create logistics milestones after hardening. Platform logistics tracking remains audited SQL evidence, not financial truth.
- Can inspections be bypassed? No inspected path bypasses receipt/inspection; vendor confirmation requires delivered SQL state and bounded receipt quantities.
- Can returns create refunds directly? No. Returns provide milestone evidence only; refund execution remains separate M4 authority.
- Can tenant leakage occur? No inspected path exposes or mutates foreign return authority after SQL customer/vendor/SuperAdmin scoping.
- Were runtime code changes required? Yes. Vendor/POS logistics advancement was disabled at the service authority boundary.
- Is CommerceHub Return Logistics Authority production-certifiable? Yes, for inspected runtime authority paths. Operational chaos proof and production monitoring remain maturity work.
