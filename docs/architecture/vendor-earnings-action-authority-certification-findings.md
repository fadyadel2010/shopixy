# Vendor Earnings Action Authority Certification Findings

Date: 2026-06-23

PASS M6-E

## Verdict

Vendor Earnings Action Authority is production-certifiable for the inspected CommerceHub vendor earnings, finance, statement, export, payout-history, payout-method, bootstrap, cache, refresh, reconnect, history, and BFCache runtime paths. Backend endpoint authorization, current actor/tenant context, SQL workflow, and payout execution controls own all action authority. Frontend emits only payout-method destination intent and presents backend evidence.

## Actual Runtime Action Vocabulary

- Vendor earnings and finance: read dashboard, history, statement, and CSV export. These are backend-authorized reads, not earnings actions.
- Vendor payout methods: create one payout-method destination (`BankTransfer`, `VodafoneCash`, or `InstaPay`), optionally as default. The backend alone derives vendor identity, validates the method/destination, records the actor, and sets verification/default state.
- Vendor payout request, release, processing, completion, failure, recovery, retry, reversal, settlement, and approval: no frontend runtime action exists.
- Direct payout processing, completion, and failure endpoints deliberately return `409 PAYOUT_EXECUTION_WORKFLOW_REQUIRED`; the Super Admin payout workflow is the only execution authority surface.
- Vendor admin actions: none. The payout-method endpoint is restricted to current `Owner,Admin` actors and trusted tenant context; the browser does not choose actor, tenant, earning, payout, amount, or approval state.

## Authority, Eligibility, and Payout Findings

- Backend owns action authority, action availability, revocation, execution, approval, payout eligibility/release/failure/retry, and reversal. The frontend has no action DTO, action token, optimistic action cache, selected earning, route action, dialog action, or response patch that can grant any of these.
- `canViewFinance` is a backend-issued session capability used only to withhold the payout-method form. It is not sent to the server and cannot authorize the request. The POST endpoint now independently requires `Owner,Admin`, trusted current tenant context, and current authenticated actor attribution.
- Earnings dashboard eligibility amounts, readiness flags, payout state, verification, default status, and statement/export filters are display evidence only. They do not enable a payout execution path.
- Payout-method mutation invalidates/refetches scoped payout-method and earnings queries; it cannot create a payout, choose earning IDs, release funds, change payout status, or patch financial evidence.

## Replay, Cache, Tenant, Actor, and Failure Findings

- React Query keys include backend-hydrated tenant and actor. Actor/tenant/role replacement clears the query client and runtime cache; scoped authority loss removes the vendor earnings/finance/payout-method subtrees.
- Initial load, focus, visibility, online reconnect, popstate, periodic revalidation, and persisted BFCache pageshow re-enter `/auth/session`; protected presentation is withheld during validation.
- Route/search state, browser history, dialogs, selected rows, local/session storage, mutation responses, malformed DTOs, and forged action/eligibility fields cannot create an executable action. There is no persisted vendor earnings action state.
- 401/403 cause authority-loss handling and revalidation; 404/409/422 are backend outcomes. Direct execution attempts receive the backend workflow-required conflict. A stale client capability can at most submit intent; current endpoint authorization decides and rejects it after revocation.
- Tenant and actor leakage does not occur on inspected paths: tenant comes from trusted backend context, actor from current authentication, and the client submits neither selector.

## Runtime Hardening Performed

- Restricted `VendorPayoutMethodsController` from broad authentication to backend `Owner,Admin` action authorization, matching the backend-issued vendor finance capability cohort.
- Added an action-authority regression freeze covering the sole vendor mutation, disabled direct payout execution, and scoped cache recovery/authority-loss paths.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/VendorPayoutMethodsController.cs`
- `PointOfSale.Tests/Architecture/CommerceHubPayoutIdentityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/CommerceHubVendorEarningsActionAuthorityCertificationTests.cs`

## Explicit Certification Answers

- Who owns Vendor Earnings Action Authority? Backend.
- What actions actually exist? Vendor reads and CSV export; vendor payout-method creation; no vendor payout/retry/recovery/reversal/approval/admin earnings action.
- Can frontend fabricate action authority or eligibility? No.
- Can action authority emerge, replay, or survive revocation? No. Cached/frontend state is never executable authority; backend reauthorizes every request.
- Can eligibility become authoritative or payout authority become earnings authority? No.
- Can vendor gain admin actions? No.
- Can cached action authority become authoritative? No.
- Can tenant leakage occur? No on inspected paths.
- Were runtime changes required? Yes: backend payout-method action authorization was made explicit.
- Is Vendor Earnings Action Authority production-certifiable? Yes.
