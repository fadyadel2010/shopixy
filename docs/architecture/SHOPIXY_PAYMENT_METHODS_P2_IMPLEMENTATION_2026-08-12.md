# SHOPIXY Payment Methods P2 Implementation - 2026-08-12

## 1. Executive Summary

P2 remediation implemented a tenant-safe provider catalog and admin provisioning UX for payment methods, plus non-secret Development runtime configuration for the Manual Wallet review workflow.

The implementation does not seed `TenantPaymentMethods`, does not add a migration, does not alter payment finality, and does not introduce browser authority over tenant, provider, amount, or payment state.

Certification status: **BLOCKED** for release certification because the broader existing payment finality regression filter is not green and authenticated live runtime probing could not be completed in this local shell session. The P2-focused implementation tests/builds passed.

## 2. Initial Problems

The Admin Payment Methods screen rendered `No payment methods available` for a tenant with zero `TenantPaymentMethods` rows because `GET /api/admin/payment-methods` correctly returned only tenant rows.

The Manual Wallet pending review endpoint existed, but runtime admission returned `MANUAL_PAYMENT_CONFIGURATION_INVALID` because `CommerceHub:Payments:ManualPayment` was disabled/incomplete in Development runtime configuration.

## 3. Provider Catalog

Added `GET /api/admin/payment-methods/catalog` in `PaymentMethodsAdminController`.

The endpoint returns the supported provider codes:

- `COD`
- `STRIPE`
- `PAYMOB`
- `FAWRY`
- `MANUALWALLET`

The catalog returns metadata only and does not create tenant rows.

## 4. Tenant Provisioning

The Admin UI now loads both the backend catalog and existing tenant rows, then merges them by provider code.

Missing tenant rows render as supported providers with `hasTenantPaymentMethod = false`, `isEnabled = false`, and `isConfigurationValid = false`.

Provider creation/configuration continues to use existing mutations:

- `PUT /api/admin/payment-methods/{code}`
- `PUT /api/admin/payment-methods/{code}/configure`
- `POST /api/admin/payment-methods/{code}/enable`
- `POST /api/admin/payment-methods/{code}/disable`

No parallel API was introduced.

## 5. Provider Configuration UI

The Admin UI now distinguishes provider existence from tenant configuration state:

- `Not configured`
- `Configured`
- `Enabled`
- `Disabled`
- `Configuration invalid`

COD can be configured without credentials via the existing upsert mutation. Required-configuration providers open the configuration modal and are not auto-enabled.

Manual Wallet configuration was narrowed to the existing validator-supported tenant field: `walletNumber`.

## 6. ManualPaymentOptions Configuration

Development runtime configuration now sets non-secret `CommerceHub:Payments:ManualPayment` options required for the review workflow:

- enabled sandbox runtime
- enabled feature flags for proof upload, review, approval, and rejection
- enabled structural capability/certification flags required by the existing validator
- generic default instructions
- proof upload limits/content types
- positive pending/proof TTLs
- enabled `VodafoneCash` channel under `MANUALWALLET`

No merchant wallet number or production secret was added to checked-in config. `appsettings.Test.json` was left unchanged to avoid globally changing unrelated test host behavior; tests use explicit valid options where needed.

## 7. ManualWallet Pending Workflow

The pending endpoint contract was preserved:

`GET /api/admin/payments/manual-wallet/pending?page=1&pageSize=25`

Focused controller coverage verifies that valid global `ManualPaymentOptions` plus an enabled tenant `MANUALWALLET` method with encrypted tenant wallet configuration returns `200` with an empty page when no pending payments exist.

The backend 404 configuration-invalid behavior was not changed because the frontend can safely distinguish it without expanding the API contract.

## 8. Storefront Availability

Storefront payment availability was not redesigned.

Existing storefront tests remain in the P2 focused slice to verify disabled/unconfigured providers stay hidden and enabled valid providers appear through the existing SQL/read-model authority path.

## 9. Security/Tenancy

Tenant scope remains derived from authenticated tenant context through `ITenantContext.RequiredTenantId`.

The browser does not submit or control `TenantId`.

Secrets remain write-only from the Admin UI perspective; decrypted secrets are not returned by GET responses. No provider credentials were committed.

Manual Wallet merchant wallet number remains tenant payment-method configuration, not global runtime configuration and not checkout browser authority.

## 10. API Contracts

Added:

- `GET /api/admin/payment-methods/catalog`

Preserved:

- `GET /api/admin/payment-methods`
- `PUT /api/admin/payment-methods/{code}`
- `PUT /api/admin/payment-methods/{code}/configure`
- `POST /api/admin/payment-methods/{code}/enable`
- `POST /api/admin/payment-methods/{code}/disable`
- `GET /api/admin/payments/manual-wallet/pending`

## 11. Tests

Added/updated focused backend tests for:

- zero tenant payment-method rows still exposing supported catalog
- tenant-scoped read isolation
- configure creating a disabled tenant row
- COD requiring no credentials
- missing Manual Wallet config not enabling provider
- provider alias normalization for Stripe/Fawry
- Manual Wallet requiring `walletNumber`
- valid ManualPaymentOptions Development overlay
- valid ManualWallet pending endpoint returning `200` empty page

Added/updated frontend tests for:

- catalog merge with zero tenant rows
- fresh-tenant provider rendering
- COD configure using existing mutation
- manual pending configuration errors not rendering as an empty queue

## 12. Builds

Passed:

- `dotnet build PointOfSale.API/PointOfSale.API.csproj --no-restore`
- `dotnet build PointOfSale.sln --no-restore`
- `npm run build` in `PointOfSale.Web`
- `npm run build` in `PointOfSale.Storefront`

Storefront build emitted existing Vite/Browserslist/chunk-size warnings, with no build failure.

## 13. EF/Migration Status

No migration was created.

`dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj --startup-project PointOfSale.API/PointOfSale.API.csproj` passed with no pending model changes.

## 14. Runtime Verification

Code-level runtime verification is covered by controller and UI tests.

Authenticated live API probing was not completed in this local shell session. Attempts to start the API through background PowerShell process helpers failed because the local process environment contains conflicting `Path`/`PATH` keys; foreground probing was not completed before timeout.

The current tenant still needs a real merchant `walletNumber` entered through Admin Payment Methods before Manual Wallet can be enabled for checkout. No wallet number was invented.

## 15. Worktree Safety

Initial `git status --short` contained only pre-existing untracked audit documents under `docs/architecture`.

Initial `git diff --stat` was empty.

No `git reset`, `git clean`, `git checkout`, `git restore`, `git stash`, `git commit`, or `git push` was run.

## 16. Remaining External Verification

Not verified:

- live Stripe provider authorization/webhooks
- live Paymob callback/webhook flow
- live Fawry callback/webhook flow
- authenticated browser walkthrough against the current tenant

Broader finality regression filter remains blocked by existing failures in COD lifecycle, Fawry webhook reliability, webhook security hardening, manual-wallet concurrency, and payment-intent guard tests. Representative failures include database `xmin` column errors, invalid-signature fixture failures, and missing fixture runtime services. These were not changed by the P2 implementation.

## 17. Final Certification

**BLOCKED**

P2 remediation is implemented and the focused P2 verification passed, but release certification cannot be marked `PASS WITH EXTERNAL VERIFICATION BLOCKED` while the broader payment finality regression filter remains red and authenticated live runtime verification is incomplete.
