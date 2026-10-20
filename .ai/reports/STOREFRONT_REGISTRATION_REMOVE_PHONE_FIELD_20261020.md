# Storefront registration — remove Phone

Date: **2026-10-20**. Workspace: **`C:\Users\fadya\source\repos\PointOfSale`**, primary development workspace.

## Outcome

**Implemented and verified in real headed Microsoft Edge.** Registration contains only Full Name, Email, Password, and Confirm Password. There is no Phone label, textbox, placeholder, optional marker, hidden phone input, phone state, field error, or phone payload property. No dialing prefix is requested.

New synthetic accounts were created against the configured development PostgreSQL database, verified through actual locally captured verification emails, and successfully logged in. No registration/verification/login service failure occurred in the final UI tests. **Two pre-existing backend source/documentation assertions still fail** and are reported separately below.

Evidence below is relative to **`.ai/evidence/registration-remove-phone-20261020/`**. Historical reports/evidence were not edited.

## Changed files and rationale

| File | Change |
|---|---|
| `PointOfSale.Storefront/src/auth/components/RegisterForm.tsx` | Removed the entire Phone group, form state/type/error members, phone-specific error mapping, and submission value. Remaining validation, password confirmation, password visibility controls, loading, mount protection, double-submit protection, and request-error handling remain. Normal request errors are still displayed. |
| `PointOfSale.Storefront/src/auth/types/auth.types.ts` | Removed `phone` from `RegisterRequest` only. `OnlineCustomer.phone` and shared backend error types remain supported. |
| `PointOfSale.Storefront/src/auth/api/authApi.ts` | Registration explicitly maps only `displayName`, `email`, and `password`. Even stale runtime callers cannot forward phone or confirmation values. Credentials behavior is unchanged. |
| `PointOfSale.Storefront/src/auth/api/authApi.registration.test.ts` | Tests exact phone-free request shape, exclusion of stale phone/confirmation values, and no automatic retry after a potentially committed POST. |
| `PointOfSale.Storefront/src/auth/pages/RegisterPage.registration.test.tsx` | Updates directly dependent form assertions: exactly four controls, no phone UI/hidden value/error, retained password hint, submission mounting, initial auth guard, and visible request failures. |

Inspected **`RegisterPage.tsx`**; no production page change was needed. Its existing verification success state, guest guard, and failure/value-preservation behavior remain intact. The existing `space-y-5` layout closes the removed group naturally.

New task-only automation:

- `tests/browser/registration-remove-phone-20261020-{form,loss,retry,verify,existing,primary}.js`: headed browser scenarios.
- `tests/registration-remove-phone-20261020-{browser,observer,integrity}.py`: private credential materialization/sanitized evidence, read-only SQL snapshots, browser-to-SQL assertions.
- `tests/quality-closeout/registration-remove-phone-20261020-{api,storefront,checks,runtime}.ps1` and `registration-remove-phone-20261020-browser-lint.mjs`: isolated services, verification commands, runtime identity, and focused browser-helper lint.
- This report and new evidence under the task-specific directory.

**No backend source, customer-profile source, shared phone utility, checkout/shipping source, translation dictionary, schema, or migration was changed.**

## Reported symptom and existing contract

Reported symptom: `/register` displayed Phone as Optional; `POST /api/storefront/auth/register?lang=en` returned 500 with a local-format supplied number and a generic form error. The user reported success without phone and apparent success after adding an international prefix. This is recorded as reported behavior, not reasserted as a newly reproduced failure for an unavailable original number.

The current backend `StorefrontRegisterRequest.Phone` is nullable. Existing `StorefrontAuthService.RegisterAsync` leaves omitted/blank phone as `null`; customer creation already supports it. The controller still uses the same registration, verification, tenant-resolution, and security contracts. **No normalization redesign or backend weakening was necessary.** Actual phone-free HTTP 200 responses and SQL `NULL` readback establish that removing the field does not hide a separate registration failure.

## Environment and safe email handling

- Store/tenant: **AhramStore**, slug `ahramstore`, ID `c9c5c1ae-949e-498e-bc63-07242daeb49d`; **never Nissan**.
- Primary frontend: **`http://ahramstore.localhost:5174/register`**. Real EN/LTR and AR/RTL form/validation checks ran here in isolated guest browser contexts.
- An independently started development API occupied port 5010 before the task's mail-safe launch. That launch failed to bind and submitted **no registration**. Rather than stop or reconfigure that process, lifecycle tests used **`http://ahramstore.localhost:5175`**, serving the same current Storefront source through Vite, with its real `/api` proxy targeting the task API at **`http://localhost:5011`**. Both frontends' registration form/API modules match after excluding only Vite HMR timestamp query strings (`browser-primary-final.json`).
- Database: existing configured **`192.168.1.5 / PosEcommerceDb_Baseline`**. No substitute database, reset, restore, migration, or SQL account activation was used. Observations ran **REPEATABLE READ, READ ONLY**. Existing opt-in configured backend tests used rollback-only transactions.
- Browser workflow: `playwright-cli list` confirmed the existing **`opencode-ui-test`** session is **headed Microsoft Edge**. Tests reused its browser with isolated contexts; existing user cookies were not cleared and existing sessions were not logged out.
- API used process-only SMTP overrides to **`127.0.0.1:2526`**, using the repository's existing `registration-phone-20261020-smtp.py` local capture. It accepts only reserved synthetic `regphone20261020-…@example.test` recipients and cannot relay. **No external email or SMS was sent by these tests.** Saved configuration hashes match before/after.
- Verification used the tokens from the actual captured messages. Only the local frontend port was changed from 5174 to 5175 in the captured link; token/tenant contents were not fabricated or changed. Credential-bearing mail, materialized scripts, and raw CLI output remain in approved private temporary storage, not this report/evidence.
- `runtime-tested.json` records source hashes and tested API DLL SHA256 **`867126D2BBCF4612528DA5348242082C00EE199C4C5A6739CA0AFDFF69EC6E37`**. The API was built by the backend test command before launch; the Storefront production build/typecheck completed before lifecycle UI testing.
- After testing, only task-owned API PID21932, frontend PID6348, and SMTP PID6256 were stopped, after checking executable paths and process creation times. Their background exit notifications reflect deliberate shutdown, not test failures. `task-service-cleanup.json` and `runtime-closeout.json` retain this evidence. Original API PID21300, Storefront PID16332, and Admin PID16656 remain running; primary API health returned 200 afterward.

## Actual request and response

Normal headed form submission:

```http
POST http://ahramstore.localhost:5175/api/storefront/auth/register?lang=en
Content-Type: application/json
```

```json
{
  "displayName": "QA Remove Phone",
  "email": "regphone20261020-remove-main-adbe5771@example.test",
  "password": "[REDACTED]"
}
```

**Actual request keys were exactly `displayName`, `email`, `password`.** Neither `phone` nor `confirmPassword` was sent. Two synchronous form submissions produced **one POST**.

Actual response: **HTTP 200**, correlation **`38ac4f20-28c2-411b-afbc-525aca4fb3e1`**:

```json
{
  "customer": {
    "id": "4b75b153-8c2e-4c7d-ae63-fe3497b3ec0c",
    "email": "regphone20261020-remove-main-adbe5771@example.test",
    "displayName": "QA Remove Phone",
    "provider": "local",
    "emailVerified": false,
    "createdAtUtc": "2026-10-20T17:18:22.0634716Z"
  },
  "accessToken": "",
  "refreshToken": "",
  "expiresAt": "2026-10-20T17:18:22.656401Z",
  "verificationEmailSent": true
}
```

No authentication credential was issued by registration. The response omits null phone; independent SQL readback records `Phone = NULL`. Evidence: `browser-form.json`, `sql-before-retry.json`, `sql-final.json`.

## Browser UI results — passed

| Scenario | Actual result | Evidence |
|---|---|---|
| Phone fully absent | Exactly four inputs; no telephone/phone/hidden control, phone label, prefix hint, or phone validation text | `browser-form.json`; `browser-primary-final.json` |
| Desktop/mobile layout | 1440, 390, and 320px widths; four aligned groups; approximately 20px spacing; no empty group/gap or horizontal overflow; screenshots visually reviewed | `register-1440.png`, `register-390.png`, `register-320.png` |
| Primary page localization | EN/LTR and AR/RTL, correct localized required-field messages, no phone, no overflow, zero registration POSTs for invalid forms | `primary-5174-en.png`, `primary-5174-ar.png`; `browser-primary-final.json` |
| Required fields | Name, email, password, and confirmation each show their existing messages; no network submission | `browser-form.json` |
| Invalid inputs | Invalid email, password shorter than eight characters, missing uppercase/lowercase/digit, and confirmation mismatch rejected with existing appropriate messages | `validation.png`; `browser-form.json` |
| Normal registration | Real 200; one POST despite synchronous double submit; visible Account created/verification banner; unverified identity; `/auth/me` 401 before login | `account-created.png`; `browser-form.json` |
| Failed delivery/value preservation | Second real registration committed successfully, then only browser response delivery was deliberately aborted; actual server 200 recorded; all four entered values retained; network error displayed; no automatic retry | `response-loss.png`; `browser-loss.json` |
| Safe retry | **SQL inspected before retry**, confirming the committed identity. Ordinary user retry returned **400 / EMAIL_EXISTS**, preserving values; final SQL still has exactly one account/verification per email | `sql-before-retry.json`; `duplicate-retry.png`; `browser-retry.json`; `sql-final.json` |
| Email verification/activation | Both captured-message verification requests returned real 200/success and visible Email Verified; SQL records consumed tenant-owned verification rows, `EmailVerified = true`, timestamp equality, `IsActive = true` under unchanged existing policy | `main-email-verified.png`, `loss-email-verified.png`; `browser-verify.json`; `sql-final.json` |
| Verified login/refresh | Both real logins returned 200; refreshed `/auth/me` returned the same verified customer IDs; visible account page rendered; both sessions logged out | `main-verified-account.png`, `loss-verified-account.png`; `browser-verify.json` |
| Existing stored phone | Pre-existing synthetic account logged in with 200; profile still shows Phone and reserved fictional `+12025550102`; real `/auth/me` agrees; session logged out | `existing-phone-profile.png`; `browser-existing.json` |

The response-loss test is an explicitly controlled browser-network fault, **not** a mocked registration success or an application outage. Forwarding preserved the real AhramStore Host/tenant and real backend response. No uncertain POST was blindly repeated.

### Persisted QA account readback

| Synthetic email suffix (`regphone20261020-remove-…@example.test`) | SQL/browser customer ID | Final state |
|---|---|---|
| `main-adbe5771` | `4b75b153-8c2e-4c7d-ae63-fe3497b3ec0c` | Phone NULL, active, verified, verification consumed, successful login, session revoked |
| `loss-adbe5771` | `7a46ac29-3107-4a3c-8bf6-24980621be07` | Same final state; original committed identity recovered, no duplicate |

Both belong to AhramStore. `PhoneVerified` remains false; password hashes exist but were never exported. There are no task POS Users or POS Customers. **Two synthetic customer records are retained**; no hard delete or audit-history cleanup was performed. `integrity.json`: **23 passed, 0 failed** independent browser/SQL checks.

## Automated verification

### Passed

| Check | Result |
|---|---|
| `npx vitest run src/auth src/services/api/httpClient.privacy.test.ts src/utils/phone.test.ts src/utils/checkoutFieldRules.test.ts src/store/checkoutStore.validation.test.ts src/components/checkout/ShippingStep.localization.test.ts` | **79 passed, 12 files** |
| `npx tsc --noEmit` | PASS |
| `npm run build` (`tsc && vite build`) | PASS; existing large-chunk warning retained |
| Focused lint of all five changed Storefront files using the repository's lint wrapper/plugin resolution, `--max-warnings 0` | PASS; existing ESLint legacy-config deprecation notice only |
| Six browser-helper files, export-wrapper lint | **0 errors, 0 warnings**; `browser-lint.json` |
| Configured backend regression run | **117 passed**, including registration without phone, password/auth lifecycle, configured registration/verification rollback cases, tenant isolation, cookies, phone normalization/risk/verification, and provider outbox regressions |
| Backend dependency/build phase of `dotnet test --no-restore` | Completed successfully; no backend source changes requiring a separate production build |
| New Python helpers / Git whitespace | **3 syntax checks passed**; `git diff --check` exit 0 |

Commands, native exit codes, and logs: `*-command.json`, `storefront-*.log`, `backend-filter.txt`, `backend-regression.log`, `trx/backend.trx`, `final-static-checks.json`. No overlapping run counts are added together.

### Failed — not concealed or changed

Backend overall run: **119 total, 117 passed, 2 failed, 0 skipped** (exit 1). Both failures are in `PointOfSale.Tests.Architecture.StorefrontCustomerAuthorityCertificationTests` and were already documented in the historical optional-phone remediation report:

1. `StorefrontSessionIssuance_MustRequireCurrentSqlCustomerAndBlockedPhoneAuthority`: source-literal assertion expects `await IsBlockedPhoneAsync(tenantId, request.Phone…`; current unchanged backend checks normalized phone instead.
2. `StorefrontCustomerAuthority_DocumentationMustBeFrozen`: historical `PASS P2-E Storefront Customer Authority` documentation marker is missing.

Neither assertion, backend source, nor historical documentation/report was modified to manufacture green results.

The original broad existing-AhramStore identity fingerprint comparison is **false** for the deliberately logged-in existing synthetic account only. Its login updates **both `LastLoginAtUtc` and `UpdatedAtUtc`** in the unchanged service; the baseline fingerprint excluded only `LastLoginAtUtc`. This mismatch is retained in `sql-final.json`/`integrity.json`, not rebased. All other pre-existing AhramStore identity fingerprints match, and **all 54 existing phone fingerprints match**, including that account's phone/activation/verification readback.

Resolved tooling-only attempts: initial API bind conflict; initial isolated frontend launch through the npm wrapper failed before serving; initial primary helper needed explicit UTF-8 reading; the first cross-server module comparison incorrectly compared Vite HMR timestamps literally (`browser-primary.json`, retained). Corrected `browser-primary-final.json` passes after excluding only `?t=<digits>`. No extra registration was submitted by these preparation attempts.

### Blocked / unexecuted

- **Blocked:** reproducing the screenshot's exact original phone value, which was not supplied. No tester-machine access or real-person registration was substituted.
- **Unexecuted by design:** external email/SMS delivery; full repository suite; Admin build/tests; unrelated profile phone edits, checkout/order submissions, or second-tenant account creation. These are not claimed as runtime-tested workflows.
- Account creation/verification/login POSTs through the independently running normal-provider API on port5010 were not attempted. Equivalent current-workspace application source and configured database were exercised through isolated mail-safe ports; primary5174 UI/validation was separately verified.

## Preservation and scope

- Backend phone contracts, stored data, profile display/edit code, checkout/shipping/order code, phone normalization and duplicate/risk rules, tenant isolation, account activation, email ownership, and login security were left unchanged. Existing regressions and the stored-phone profile test confirm the relevant preserved behavior; untouched workflows not executed above are not overstated as UI passes.
- Existing **54 customer phone values** and saved app configuration hashes match before/after. No existing phone was deleted, normalized, or backfilled.
- New account activation follows existing `IsActive = true` creation behavior; email verification starts false and changes only through the real token-verification endpoint. No SQL verification edits or security bypass were used.
- Initial working-tree status was clean. Only the five listed tracked Storefront files and new task-scoped tests/report/evidence were changed. Existing historical material was preserved. Nothing was staged or committed; no reset, clean, stash, destructive database operation, tester machine, or unrelated user process was used.
- Supplied real-user/Admin credentials were not required: synthetic-only account testing and the pre-existing synthetic stored-phone account were sufficient. No passwords, verification tokens, access credentials, or real customer contact details are included in this report.

**Remaining verification limitation:** the two pre-existing backend certification failures above. No unresolved registration, verification, login, value-preservation, duplicate-retry, or registration-layout failure remains in the executed UI scenarios.
