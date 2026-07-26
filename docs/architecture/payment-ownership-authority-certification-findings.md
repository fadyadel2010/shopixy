# Payment Ownership Authority Certification (PASS M3-C)

## Authority

The backend owns payment ownership and every related customer, vendor, order,
refund, recovery, tenant, provider, settlement, and authorization decision.
The frontend presents only current backend DTOs, filters/sorts them, and sends
backend-issued lookup identifiers back to backend-authorized endpoints.

## Implemented runtime paths

- CommerceHub customer checkout/payment initiation and provider handoff.
- CommerceHub customer order list, order detail, payment retry/recovery, and
  refund history/request views.
- POS vendor finance, earnings, payment-request, payment-proof, statement,
  payout-method, and settlement presentation.
- POS customer/supplier statement payments and manual-wallet review/confirm/
  reject flows; these retain only backend-issued party or payment-intent lookup
  IDs and re-enter endpoint authorization for every read or mutation.
- Super Admin vendor manual-payment monitoring, proof access, approval/reject,
  payment-method management, refund and payout monitoring.
- Storefront checkout pending status, Stripe/Paymob return handling, order
  confirmation, and tracking lookup presentation.

## Findings

- **Customer:** order and recovery route values are lookup candidates only.
  Exact backend order identity and current `canRetryPayment` are required
  before any retry. Customer queries are purged on authority loss or actor
  replacement, and recovery context intentionally has no durable storage.
- **Vendor:** vendor financial dashboards require current tenant/vendor scope
  and backend-issued rows. Browser selection state is constrained to the
  current validated method list; manual payment IDs remain endpoint-authorized.
- **Admin:** monitoring is visibility, not ownership. Payment monitor rows,
  methods, proofs, and mutation responses now pass a strict DTO boundary.
  Selected approve/reject state holds only a request ID and is re-derived from
  the latest validated query; it is closed if refresh/failure replaces the row.
- **Order/refund consistency:** customer order adapters require coherent
  backend parent/split totals, sub-order evidence, lifecycle/payment status,
  and refund financial amount snapshots. The frontend never reconstructs a
  payment owner from split rows or refund state.
- **Recovery:** signed recovery token/link use is limited to a backend lookup;
  the token is removed from the visible URL and neither it nor a payment
  session is persisted. A retry re-fetches backend authority immediately before
  initiation.
- **Bootstrap/cache/failure:** customer bootstrap revalidates on initial load,
  focus, visibility, reconnect, history navigation, and BFCache restore, then
  purges customer query state on loss/replacement. Super Admin authority loss
  clears its query client. 401/403 redirects/purges; malformed, 404, 409, and
  422 responses do not supply replacement ownership.
- **Adversarial:** local/session storage, route/history/search parameters,
  React state/context, React Query, mutation results, cross-tab signals, and
  provider redirects do not independently establish ownership. Replaying them
  only causes a backend-authorized re-fetch or endpoint call.

## Runtime hardening performed

Super Admin payment monitoring previously trusted unvalidated monitoring DTOs
and retained the selected request object in React state. A strict payment
ownership presentation boundary now rejects malformed/duplicate rows and
proof/mutation DTOs. The modal retains only a backend-issued request ID and
resolves it against the current validated result; refresh, failure, or removal
cannot preserve the old selected row as action authority.

## Regression freezes

- `PointOfSale.Web/src/super-admin/payments/paymentOwnershipAuthority.test.ts`
- `PointOfSale.Tests/Architecture/PaymentOwnershipAuthorityCertificationTests.cs`

## Verification

- `npm test -- --run src/super-admin/payments/paymentOwnershipAuthority.test.ts`
- `npm run build` in `PointOfSale.Web`
- `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~PaymentOwnershipAuthorityCertificationTests`

## Certification answers

Backend owns payment ownership authority. The frontend cannot fabricate it.
Customer A cannot access Customer B's payments and Vendor A cannot access
Vendor B's payments without the backend returning and authorizing that data.
Monitoring cannot become ownership. Recovery links/tokens cannot grant a
frontend-created ownership grant. Split ownership cannot drift through frontend
reconstruction. Stale ownership cannot survive authority refresh, cache cannot
restore it, and replay cannot restore it. Tenant leakage is not introduced by
the frontend paths. Runtime changes were required and applied. Payment
Ownership Authority is production-certifiable, contingent on the existing
backend authorization boundaries remaining in force.
