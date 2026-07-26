# PASS 16.5F-B CommerceHub Publication Currency Policy Enforcement

Date: 2026-06-02

## Verdict

CommerceHub publication policy is enforced as EGP-only at inspected publication boundaries.

POS Admin and storefront remain multi-currency through tenant-scoped catalog price authority. CommerceHub is currently an Egypt marketplace, so products whose authoritative price currency is not EGP cannot be published, republished, bulk-published, or listing-updated into CommerceHub visibility.

## Authority

| Domain | Currency Policy |
|---|---|
| POS | Any Currency |
| Storefront | Any Currency |
| CommerceHub Publish | EGP Only |
| CommerceHub Listing | EGP Only |
| CommerceHub Cart | EGP Only |
| CommerceHub Checkout | EGP Only |
| CommerceHub Settlement | EGP Only |
| CommerceHub Refund | EGP Only |
| CommerceHub Ledger | EGP Only |

Current catalog price currency authority is `Tenant.Currency`; there is no separate `Product.Currency` or `ProductVariant.Currency` field in the inspected catalog model.

## Findings

- Publish and republish enter CommerceHub through `PublishToCommerceHubHandler`.
- Listing updates enter through `UpdateCommerceHubListingHandler`.
- Merchant bulk publish/update/remove-override enters through `AdminCommerceHubController.BulkListingOperation`.
- Replay, recovery, auto-healing, catch-up reindex, tenant reindex, and global reindex converge through `CommerceHubSearchSqlAuthorityOverlay`.
- Search/live recovery now counts only SQL-visible listings whose tenant price currency is EGP.
- Unpublish remains allowed because it only reduces visibility and must be available to withdraw legacy or stale listings.

## Runtime Hardening

- Added `CommerceHubPublicationCurrencyPolicy` with one EGP-only fail-closed message.
- Publish/republish/update now reject non-EGP product price authority before listing creation, republish, or update mutation.
- Bulk visibility-increasing and listing-update operations now validate currency per row and return per-row failures.
- Admin publication UI receives currency eligibility and shows: `CommerceHub currently supports EGP products only. This product uses USD and cannot be published.`
- Reindex expected visibility now excludes non-EGP listings to match overlay deletion behavior.

## Regression Freeze

- `CommerceHubPublicationCurrencyPolicyCertificationTests`
- `CommerceHubPublicationCurrencyValidationTests`
- `CommerceHubBulkPublicationCurrencyTests`

## Remaining Debt

- No DB-level EGP check constraint exists for every CommerceHub financial/listing currency surface.
- No production SQL/Mongo/Elasticsearch currency parity scanner exists.
- No PostgreSQL-backed mixed-currency chaos harness exists for publish, replay, recovery, reindex, cart, checkout, refund, payout, and ledger flows.
