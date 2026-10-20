# Storefront registration optional-phone HTTP 500 remediation

Date: **2026-10-20**. Initial execution was shortly after midnight Windows-local time (2026-10-19 UTC); final tests ran on 2026-10-20 UTC.

## A. Executive verdict

**CONDITIONAL — the reproduced registration defect is fixed and the final headed-browser registration, verification, login, persistence, and recovery tests pass. Two source/documentation assertions remain failing; the screenshot's original phone value was unavailable.**

- Omitted, cleared, and whitespace-only optional phones succeed and persist SQL `NULL`.
- Supported formatted international phones succeed and persist canonical E.164. Valid phone registration also worked in the baseline; an unconditional “every supplied phone fails” claim was **not** reproduced.
- Invalid supplied phones now return **400 / `INVALID_PHONE` / `data.field = phone`**, not 500. Form values remain intact.
- Shared phone numbers are **permitted** by the existing customer domain. Distinct email identities and equivalent spellings succeed without merging identities.
- A directly dependent verification defect was also reproduced and fixed: customer verification, token consumption, and the authoritative outbox event now save together.
- Final automated results: **114/114 backend**, **35/35 Storefront**, **20/20 Admin authentication**, and **111/111 independent browser-to-SQL integrity checks**. Backend and Storefront builds, Storefront typecheck, and focused lint/format checks pass.
- **24 explicitly synthetic QA accounts** are retained under the existing no-hard-delete policy; all **9 QA login sessions are revoked through logout**. Five pre-existing customer rows, provider links, saved configuration, and unrelated workspace files remain unchanged.

No tester machine, tester browser, alternate tester database, real recipient, SMS, or paid communication action was used. Nothing was staged or committed.

## B. Environment and reproduction

Evidence paths below are relative to **`.ai/evidence/registration-phone-20261020/`**.

### Environment/build identity

| Item | Verified identity |
|---|---|
| Workspace | `C:\Users\fadya\source\repos\PointOfSale`, primary `main`; HEAD `5bd9bfc0e57c23116cd1c4fba456bf0c9f7ccad4` |
| Preservation | `workspace-before.txt`, `existing-work-hashes.json`, `preservation.json`; substantial unrelated changes/untracked files and historical artifact deletions preserved |
| Storefront | **`http://ahramstore.localhost:5174/`**, `/register`, `/login`, `/verify-email`, `/account`; never switched to Nissan |
| Admin | `http://localhost:5173/`; existing separate Vite application, no production Admin source changed |
| Tenant | **AhramStore**, slug `ahramstore`, ID **`c9c5c1ae-949e-498e-bc63-07242daeb49d`**, default country `EG` |
| Tenant resolution | Existing hostname/domain resolution → trusted tenant context/header. Vite `/api` proxy uses `changeOrigin: false`, preserving Storefront Host. Forged tenant headers are rejected |
| API | Development, `http://localhost:5010`, normal `PointOfSale.API/bin/Debug/net9.0` output |
| SQL | Existing configured PostgreSQL **`192.168.1.5:5432 / PosEcommerceDb_Baseline`**. Real browser-created IDs independently observed there |
| Initial API | Original PID17124; baseline capture relaunched the same binary as PID2704 with local SMTP only. DLL SHA256 **`785D27CC1DE6247E0B4FF0078ADCBCD97BE3359146DEF0D268EC4D0F5F6BDC31`** |
| Final tested API | PID**23096**, DLL SHA256 **`BC3B5399B235A96ADB3B9F3705C8CFC0B6AD3295BC11175B9033C5F18D780918`**, written `2026-10-20T11:11:46.1147209Z`, process started afterward |
| Restored runtime | Same final binary, PID**24536**, normal configured SMTP restored after all registration/verification tests; `api-launch-restored.json`, `runtime-restored.json`, `runtime-restored-ready.json` |
| Final frontend | Storefront Vite PID24196, Admin Vite PID16656. `browser-build.json` records actual served module ETags/SHA256 and presence of the form-mount, phone-validation, submission, and stale-response fixes |
| Browser | Registered **`opencode-ui-test`**, isolated **headed Microsoft Edge**. Reused initially; recreated with the repository's headed Edge command after a session/server restart ended the previous browser |

Runtime manifests: `runtime-baseline-live.json`, `runtime-corrected.json`, `runtime-final-tests.json`, `runtime-tested-closeout.json`. Final API source hashes and the tested DLL remain unchanged through closeout. The intermediate API hash `656DB5…` is not labeled the final verification build.

SQL observations use **REPEATABLE READ, READ ONLY**. New relational regression tests use the **same configured database**, explicit outer transactions and rollback, no external message transport, and no schema creation/reset/migration. Existing unit-test conventions were inspected before implementation.

### Safe communication handling

Saved development SMTP points to Gmail. **The API was stopped before the first registration submission**, then run with process-only SMTP host/port/SSL overrides targeting **`127.0.0.1:2526`**. The existing real `SmtpEmailSender` delivered to a local SMTP capture that accepts only `regphone20261020-…@example.test`, cannot relay, and rejects other recipients. No saved configuration was changed. Verification links came from these actual locally captured messages, not fabricated tokens or SQL edits.

Credential-bearing SMTP messages, generated QA passwords, materialized browser scripts and raw CLI output remain only in approved private temporary storage, not report/network evidence. Report captures omit passwords, hashes, cookies, access/refresh credentials and verification tokens. Supplied existing-user credentials were not needed for synthetic registration and were not put in scripts or reports.

After testing, the task API and SMTP sink were stopped, and the **same corrected API binary** was relaunched with the original configured SMTP settings. No registration/resend/send action was performed after this restoration.

### Initial database state

`sql-before.json`: resolved AhramStore/EG; **zero** task-owned accounts and verification/session rows; zero collisions for the reserved QA canonical phone values; five pre-existing AhramStore customers fingerprinted without exposing their data. Relevant live columns, indexes and constraints were recorded. Configuration files were fingerprinted, not copied with secrets.

### Actual baseline request/response and persistence

All submissions used the **real headed registration form** and `POST /api/storefront/auth/register?lang=en`.

| Controlled input | HTTP | Independently persisted result |
|---|---:|---|
| Phone untouched / property omitted |200| `d5ca3e26-ada7-4e6c-8fce-356d481dd4f3`, phone `NULL`, one local unverified customer |
| Phone `+1 (202) 555-0101` |200| `fabaebf2-6964-4bd6-bfcd-d33c56479ebf`, **raw formatted phone**, not canonical |
| Phone `12345` |**500**| No customer, verification or login identity created |

Failed payload, excluding password:

```json
{"displayName":"QA Optional Phone baseline-invalid","email":"regphone20261020-baseline-invalid@example.test","phone":"12345"}
```

Response:

```json
{"code":"INTERNAL_ERROR","message":"An unexpected error occurred.","traceId":"2a1f4dcd-320a-46f6-b1ff-0ba5f18eae80","data":null}
```

Correlation header **`2a1f4dcd-320a-46f6-b1ff-0ba5f18eae80`**; backend activity **`00-2291825e253deb516080a4111f0786f8-2bee73b577fc7347-01`**; exception timestamp **`2026-10-19T21:56:01.182Z`**. `browser-baseline.json`, `registration-exceptions.json`, `sql-baseline.json`, and `baseline-invalid.png` correlate request, response, backend stack and absent identity. The baseline error screenshot also shows that the old page discarded entered form values.

This is the exact reproduced **500 path/signature**, not proof about the unavailable phone value in the original screenshot. Valid supported international input did not fail in the controlled baseline. No tester access was used to fill that evidence gap.

## C. Root cause

### Registration: invalid phone exception escaped before customer creation

`StorefrontAuthService.RegisterAsync` called `IsBlockedPhoneAsync(tenantId, request.Phone, …)` **outside** its registration exception boundary. `PhoneRiskProfileService.IsBlockedAsync` first calls the established tenant-aware normalization service. For the invalid number, that service rejected normalization and raised:

> `System.ArgumentException: Enter a valid phone number for the configured country. (Parameter 'phoneNumber')`

Recorded stack: **`PhoneRiskProfileService.NormalizePhoneAsync` → `IsBlockedAsync` → `StorefrontAuthService.IsBlockedPhoneAsync` → `RegisterAsync` → controller → global exception middleware**.

The exception was neither a field validation result nor inside the service's existing infrastructure-failure mapping. The global middleware therefore emitted **500 / INTERNAL_ERROR**. No insert had occurred. The frontend sent an ordinary string; proxying, DTO shape, optional nullability, column width and phone uniqueness were not the cause of this reproduction.

Separately, successful registration trimmed but persisted the **raw spelling**, while risk lookup normalized it. This disagreement is fixed using the existing normalizer, not a new country/provider restriction.

### Directly dependent verification: publish after commit

The first real email-verification UI test exposed another existing defect. `VerifyEmailAsync` saved `EmailVerified = true` and consumed the token, **then** published `OnlineCustomerEmailVerifiedEvent`. The real publisher rejected that authoritative event:

> `System.InvalidOperationException: Authoritative integration events must be published before the source mutation is committed so the MassTransit outbox row is atomic with the business state. EventType=OnlineCustomerEmailVerifiedEvent`

Activity **`00-108448e6b0878752319d81ad1406b7a2-517694585e0968b9-01`**, correlation **`4cb45914-e99b-4a82-83a4-53dd81cb1b4a`**, `2026-10-20T11:05:34.627Z`. The browser showed **Invalid Link**, while SQL and real `/auth/me` showed verified. `browser-verification-diagnostic.json`, `verification-before-fix-failed-ui.png`, `sql-verification-interrupted.json`, and the second entry of `registration-exceptions.json` preserve this discrepancy.

This is **not** relabeled as the original registration 500. It is a directly dependent verification lifecycle defect found by the mandatory UI tests.

## D. Implementation

| Changed file | Necessary change |
|---|---|
| `PointOfSale.API/Services/Storefront/StorefrontAuthService.cs` | Canonical optional phone handling inside the registration boundary: blank→`null`, supported value→existing tenant-aware normalization, narrowly identified invalid phone→`INVALID_PHONE`; risk check and customer persistence use the canonical value. Dependency/configuration failures retain controlled unavailable behavior |
| `PointOfSale.API/Controllers/StorefrontAuthController.cs` | Attach structured `data.field = "phone"` to controlled registration phone validation responses |
| `PointOfSale.API/Services/Storefront/StorefrontEmailVerificationService.cs` | Publish verified event **before SaveChanges**; save customer, consumed token and outbox together; use the same persisted verification timestamp for customer/token/event, including existing `EmailVerifiedAtUtc` column |
| `PointOfSale.Storefront/src/auth/api/authApi.ts` | Consistently omit optional blank phones at the transport boundary; retain valid formatting for backend normalization |
| `PointOfSale.Storefront/src/auth/components/RegisterForm.tsx` | Accessible field-specific error, values retained, synchronous submission guard, no stale success callback after unmount |
| `PointOfSale.Storefront/src/auth/pages/RegisterPage.tsx` | Keep form mounted during submission; initial auth resolution still guards the route |
| `PointOfSale.Storefront/src/auth/store/authStore.ts` | Reject superseded registration completion instead of handing stale success back to the page; existing revision protection remains |
| `PointOfSale.Storefront/src/auth/types/auth.types.ts` | Type `INVALID_PHONE` |
| `PointOfSale.Storefront/src/i18n/locales/en.json`, `ar.json` | Meaningful localized phone validation text |
| `PointOfSale.Tests/Security/StorefrontRegistrationPhoneConfiguredTests.cs` | Opt-in real configured-PostgreSQL rollback tests: optional/valid/invalid inputs, canonical duplicate allowance, tenant-scoped blocking, retry/dispatch safety, SQL/outbox atomicity, and verification failure boundaries |
| `PointOfSale.Tests/Security/StorefrontAuthenticationLifecycleTests.cs` | Controller field-specific400/no-cookie regression; existing password and authentication regressions retained |
| `PointOfSale.Storefront/src/auth/api/authApi.registration.test.ts` | Optional transport normalization, valid spelling preservation, no automatic retry of a potentially committed POST |
| `PointOfSale.Storefront/src/auth/pages/RegisterPage.registration.test.tsx` | Form remains mounted during submission; initial auth guard and field-level rendering |
| `PointOfSale.Storefront/src/auth/store/authStore.test.ts` | Registration remains unauthenticated; stale success/failure cannot replace a newer authentication transition |

Task-only tooling: `tests/browser/registration-phone-20261020-*.js`, `tests/registration-phone-20261020-*.py`, and `tests/quality-closeout/registration-phone-20261020-*`. These scope synthetic identities, local SMTP, private materialization, read-only SQL, build/test commands, preservation and evidence. They do not alter production email provider registration or schema.

### Contract preserved

1. Optional missing/null/empty/whitespace phone becomes SQL **NULL**. The UI consistently omits blanks in JSON; backend handles null/blank callers independently.
2. Existing `PhoneRiskProfileService` / `PhoneNumberNormalizer` rules remain authoritative: international E.164/formatting and tenant-country handling, including supported `00` spelling. No country/provider hardcoding was added.
3. `OnlineCustomers.Phone` is live nullable **varchar(50)**. Unique customer key is **`IX_OnlineCustomers_TenantId_Email`**. **No phone uniqueness rule exists**; the tenant+phone unique index belongs to **PhoneRiskProfiles**, not customer identities. No migration or schema change is required.
4. Local customer identity remains `OnlineCustomer`, separate from POS `Users` and `Customers`. Phone is not an account-linking key. New customer, tenant, verification and refresh-token relationships are checked in SQL.
5. Registration still issues **no authenticated session**, and verification/login/activation rules remain unchanged. Existing application behavior permits login before email verification and exposes unverified status; registration does not mark either phone or email verified.
6. Customer creation and its registration outbox save remain atomic. A later email-dispatch outage honestly returns account-created/dispatch-failed, not a false total registration failure. Same-email retries retain the established **400 / EMAIL_EXISTS** recovery contract, not invented success or a second account.
7. Verification publication and state now share the persistence boundary. Publish/SQL failure tests confirm neither a consumed token nor a verified customer is left behind in the corrected path.

## E. Test evidence

### Passed — final real headed Edge matrix

**All rows below ran after both source fixes and final API build**, using PID23096 and the DLL identified above. There were no mocked registration or verification successes. Matrix request bodies intentionally exclude passwords in retained evidence.

`browser-matrix-final.json`: **11/11 cases**, nine real successful accounts, two controlled validation failures, one POST per case, zero page exceptions.

| Scenario / QA suffix¹ | HTTP / visible UI | SQL outcome / customer ID | Screenshot |
|---|---|---|---|
| A untouched `optional-untouched-final-v2` |200, account created| `dcce20fb-4c32-4129-b39e-2738eae0c252`, phoneNULL | `optional-untouched-final-v2.png` |
| A entered then cleared `optional-cleared-final-v2` |200| `60538fd3-0e5c-48c6-81e8-1123be1677d2`, phoneNULL | matching suffix `.png` |
| A whitespace `optional-whitespace-final-v2` |200, phone omitted in JSON| `51eb34bd-c7d8-4cae-b6b0-9b58abdb942e`, phoneNULL | matching suffix `.png` |
| B `valid-formatted-v2`, `+1 (202) 555-0102` |200, account created| `efa41aeb-74c8-45ed-adc2-194210c5cd20`, **`+12025550102`** | `valid-formatted-v2.png` |
| C `duplicate-exact-v2` |200, permitted by established policy| `ede67b38-cbea-4cd4-accb-f99da766887a`, same canonical phone, independent identity | matching suffix `.png` |
| C `duplicate-equivalent-v2`, `+1 202-555-0102` |200| `65790582-87f0-435f-86dc-1e3a8871770d`, same canonical phone, independent identity | matching suffix `.png` |
| C `invalid-number-v2`, `12345` |**400 INVALID_PHONE**, field message, values retained| **No customer/verification/session** | `invalid-number-v2.png` |
| C `invalid-format-v2`, `not-a-phone` |**400 INVALID_PHONE**, field message, values retained| **No customer/verification/session** | `invalid-format-v2.png` |
| D `repeat-email-only-v2` |200| `5087653d-765a-40ec-8aac-44695cffaba8`, phoneNULL | matching suffix `.png` |
| D `repeat-valid-v2`, two synchronous `requestSubmit()` calls |200, **one POST**| `a021af49-6fb9-408a-b990-43f56f155a81`, one identity, `+12025550103` | matching suffix `.png` |
| D `refreshed-valid-v2`, refresh form then ordinary submission |200| `c1d25268-4a92-4d72-8c45-2f98f1e62d07`, `+12025550104` | matching suffix `.png` |

¹ Email format is **`regphone20261020-<suffix>@example.test`**. Phones are from the reserved North American fictional **202-555-0100–0199** range, not real customer numbers.

Correlations for representative final cases: optional `f2b270d1-d2be-4060-9bbd-cf2f85e91fce`; formatted valid `adb3f9fe-0ee8-46a9-8186-82f5ce0ee4f7`; invalid number `0d356ac2-3b09-424b-baa3-101b003c3f91`, activity `00-30847f8672eac9a780c8d76605117411-0a8313896fdf0b18-01`. Full request/response/correlation details are in the JSON.

### Passed — verification, subsequent login, visible refresh and recovery

- **Optional and valid identities:** `browser-verification-final.json` records real pre-verification login200/unverified, real captured-link verification200, subsequent login200/verified, and refreshed `/auth/me`200 with the **same customer ID**. Registration itself returned empty tokens and unverified status. SQL confirms consumed, tenant-owned verification records and matching `EmailVerifiedAtUtc`/`UsedAtUtc` timestamps.
- Optional verified timestamp: `2026-10-20T11:18:52.205200Z`. Valid-phone verified timestamp: `2026-10-20T11:19:06.125419Z`. Verification correlations: **`d7795556-8582-41f3-9898-36d53457c3c9`** and **`378f7f5f-9d5e-471d-938f-1b844345685a`**.
- Screenshots: `optional-untouched-final-v2-verified.png`, `valid-formatted-v2-verified.png`; final fully rendered profile evidence is **`*-persisted-account-final.png`**, with `browser-readback.json`. Earlier `*-verified-login-refresh.png` captures caught the loading frame and are **not** cited as completed profile screenshots.
- **Transient response loss:** `browser-response-loss.json` forwards the real UI POST to the actual application, observes real200 and committed customer **`a70db497-ee43-46cc-930b-a8f32c1eb722`**, then aborts browser delivery only. UI shows network failure and retains values. **`sql-response-loss-before-retry.json` was recorded before retry**. The ordinary UI retry returned400/EMAIL_EXISTS; sign-in200 recovered the **same ID**. One customer and one verification record remain. `browser-recovery.json`, `response-loss.png`, `response-loss-retry.png`, `response-loss-persisted-account-final.png`.
- **Tenant spoofing through the UI request:** modified only the outgoing tenant header, not the response. Actual backend **403 / TENANT_FORBIDDEN**; no account. `browser-tenant.json`, `tenant-spoof-rejected.png`. The configured SQL regression additionally creates a rollback-only blocked risk profile in another tenant and proves canonical same-number registration is unaffected in AhramStore but blocked in that tenant. No second-tenant browser login is claimed.
- **Independent SQL correlation:** `sql-post-final-matrix.json`, `sql-response-loss-before-retry.json`, `sql-final.json`, and **`integrity-checks.json`:111passed,0failed**. These check actual customer IDs, canonical/null phones, single verification records, invalid/spoof/rollback absence, tenant ownership, local identity semantics, verification timestamps, retry identity, visible refresh results, and revoked QA sessions.

### Passed — automated tests/build/typecheck/lint

| Check | Final result / evidence |
|---|---|
| Backend registration, password/auth lifecycle, configured registration+verification rollback, phone normalization/risk, tenant isolation, phone-verification reliability, provider-admin outbox, cookie security | **114passed,0failed,0skipped**; `backend-regression-final.log`, exact filter/assembly-hash manifest, `trx/backend-regression-final.trx` |
| New configured registration/verification cases within that total | **18** cases on current PostgreSQL; no replacement database, external message send or committed test fixture |
| Storefront auth, registration page/API/state, auth bootstrap/navigation/verification presentation, HTTP privacy | **35passed,8files**; `storefront-tests-final.log`, `frontend-commands.json` |
| Admin auth bootstrap and auth replay | **20passed,2files**; `admin-auth-regression.log`, `admin-auth-commands.json` |
| Final normal development backend build | **PASS,0errors**; `backend-final-development-build-completed.log`, command manifest; final binary actually loaded for UI tests |
| Release backend/test build | **PASS,0errors**; `backend-test-build-final.log`, `test-build-commands.json`; existing warnings retained |
| Storefront typecheck | **PASS**; `storefront-typecheck-final.log` and exact exit0 manifest |
| Storefront production `tsc && vite build` | **PASS**; `storefront-build-final.log`; existing tooling/chunk warnings retained |
| Changed Storefront source/tests lint | **PASS,8files,0errors/0warnings**; `storefront-lint-final.log`, `lint-commands.json`; existing shared plugin resolution used, no dependencies installed |
| Task browser-helper lint | **PASS,9files,0errors/0warnings**; `browser-lint.json`; existing export-wrapper workflow |
| Changed C# production and test whitespace verification | **PASS**, verify-only absolute includes; `backend-format-final.log`, `backend-test-format.log` |
| Changed-source Git whitespace / task Python syntax | **PASS**; actual `git diff --check` and AST parsing ran; ordinary LF/CRLF notices are not failures |

Counts above are final suite counts, not sums of overlapping intermediate runs.

### Failed — retained separately

`customer-source-contracts-final.log` / TRX: **3passed,2failed** in `StorefrontCustomerAuthorityCertificationTests`:

1. `StorefrontSessionIssuance_MustRequireCurrentSqlCustomerAndBlockedPhoneAuthority`, line30, requires the literal **`await IsBlockedPhoneAsync(tenantId, request.Phone`**. Corrected registration deliberately checks **`normalizedPhone`**, not raw input. This source-literal assertion is now obsolete; actual canonical tenant-scoped risk checks pass. It was **not rewritten merely to obtain green certification**.
2. `StorefrontCustomerAuthority_DocumentationMustBeFrozen`, line71, cannot find a historical **`PASS P2-E Storefront Customer Authority`** marker in `.ai-context/PROJECT_CONTEXT.md`. That documentation was not changed. No old certification report/source assertion was edited to repair historical markers.

Earlier resolved/interrupted runs are not passing final evidence: the first new configured fixture captured a disposed DbContext in deferred DI options (**15 fixture failures**); fixed by capturing its connection string before disposal. An initial stale-registration frontend test used an internal state setter rather than a revision-bearing login action (**1 test failure**); corrected to exercise the actual action. Their logs remain. Initial plugin-path invocation and several finite-timeout preparation/build runs were retried with the proper existing plugin path or completed native-command capture. Final exact native exit codes are recorded in manifests.

The initial rapid UI batch hit the **existing3/minute429** after creating three optional QA accounts. SQL was inspected before proceeding; the rejected valid identity was absent. Later matrices were paced, not run with weakened security. A response-loss harness attempt initially failed in Node DNS resolution of `*.localhost`, before any server insert; SQL confirmed absence. Its corrected forward connects to loopback while preserving actual Storefront Host/tenant. Neither tooling issue is claimed to be an application registration regression.

### Blocked / unexecuted / not applicable

- **Blocked:** reproducing the screenshot's exact phone value/identity; neither was available. No tester access or real-person test input was substituted.
- **Unexecuted by design:** external email/SMS/provider delivery, original real-customer registrations, full repository test suite, and second-tenant browser account creation. These are not claimed passing.
- **Not applicable, not executed:** Admin production build/typecheck; no POS/shared frontend authentication module changed. Focused Admin auth regressions and backend provider-administration outbox tests did run.
- Two historical/source assertions remain failed as listed above. No whole-repository formatter/lint certification is claimed.

## F. Data integrity and retention

`sql-final.json` contains the full safe QA identifier/readback inventory: **24 customer rows,24 verification rows,9 revoked refresh sessions**, correct tenant, no POS users/customers, no new social-provider identity links. Twelve QA phones areNULL. All failed invalid/spoof requests and all rollback-only fixture identities are absent.

### Retained account inventory

All emails use `regphone20261020-<suffix>@example.test`; all belong to AhramStore. Final case IDs appear in the matrix above. Additional retained historical/task IDs:

| Suffix | Customer ID / purpose |
|---|---|
| `baseline-none` | `d5ca3e26-ada7-4e6c-8fce-356d481dd4f3`, original email-only control |
| `baseline-valid` | `fabaebf2-6964-4bd6-bfcd-d33c56479ebf`, original successful raw-phone storage evidence; deliberately not backfilled |
| `optional-untouched` | `1a6ee1be-d960-4db9-afbc-cbb3759ce7f6`, first rapid batch |
| `optional-cleared` | `966fd564-4615-4090-98dc-82a13f9de1d3`, first rapid batch |
| `optional-whitespace` | `1d4efca5-838b-4aa1-962e-6cf220ea4ce0`, first rapid batch |
| `optional-untouched-final` | `b9b60711-a35b-4d42-9481-540099774670`, verification-before-fix evidence |
| `optional-cleared-final` | `e0eb8ac1-e821-420f-9e00-e4ec12d12179`, first completed matrix |
| `optional-whitespace-final` | `79e83ff1-65d8-4183-916c-1146f86fc200`, first completed matrix |
| `valid-formatted` | `dafb04a9-0b51-40e4-8877-191bf0f57547`, first completed matrix |
| `duplicate-exact` | `8c8d8aa8-ae35-4d7f-85e7-67efe56047d1`, permitted shared-phone identity |
| `duplicate-equivalent` | `d37bc5ad-0f70-4399-a6a0-a07ff82fbc3f`, permitted equivalent spelling |
| `repeat-email-only` | `cc6bac13-9809-4d01-a366-6a754b936962`, email-only repeat |
| `repeat-valid` | `963d20a7-3663-49de-9b05-0cc2b24ff68e`, first double-submit control |
| `refreshed-valid` | `f2e7f13a-fc71-4187-ad4d-402ec8c0c3ac`, first refresh control |
| `response-loss` | `a70db497-ee43-46cc-930b-a8f32c1eb722`, committed-response-loss recovery |

`OnlineCustomer` explicitly has **no hard-delete operation** and retains identity/audit history. Accordingly, these accounts and verification history remain; no direct SQL deletion or “cleanup” rewrite occurred. Strong generated credentials/tokens are excluded from evidence. All task-owned actual login sessions were logged out.

The pre-fix verification probe `b9b60711-…` remains verified with its old consumed token and null historical verified timestamp. It was not reset, backfilled or re-registered to manufacture a passing test. Fresh final-build identities prove the corrected atomic behavior instead.

`preservation.json` verifies:

- **114 unrelated pre-existing modified/untracked work files unchanged**, no unexpected file changes under the recorded preservation set; task changes are explicitly identified.
- All **five pre-existing AhramStore customer row fingerprints unchanged**.
- Existing provider links unchanged; provider-settings row digest unchanged between its captured post-matrix and final observations. Provider settings were never edited.
- All saved application configuration file fingerprints unchanged.
- No task POS `User` or POS `Customer` created.

No reset, replacement, recreation, broad restore, migration, existing-customer edit, unrelated snapshot edit, staging or commit was performed. The task stayed in the primary Windows workspace and used only its configured services; no tester-machine operation occurred.

## G. Remaining limitations

1. The original screenshot's phone value is unknown. The concrete invalid-phone500 and inconsistent canonical persistence are verified/fixed; a separate failure for some other valid value is not asserted or concealed.
2. Two source/documentation assertions remain failed, with exact reasons above. Runtime regression success does not certify those historical source strings/reports.
3. Legacy raw phone spellings and the explicitly retained pre-fix QA verification artifact are not backfilled. No unrelated existing customer was modified.
4. External delivery is intentionally not certified. Real verification was proven through the existing SMTP path with a non-relaying local capture, followed by real browser/API/SQL verification and login.
5. Original configured SMTP has been restored for normal development. Re-running registration tooling requires the local-only sink and a deliberate fresh QA identity/readback plan; do not blindly resubmit these retained accounts or run tests against external SMTP.

**No unresolved runtime failure remains in the executed final optional-phone registration, canonical persistence, validation, allowed-duplicate, tenant, verification, login, or committed-response recovery cases.**

### Post-completion process notice

After final verification, the restored API launcher reported exit1. A subsequent process check found **no running API process**. Its captured log confirms successful Development startup/listening; the inspected last300 structured records contain no recorded Error/Critical event explaining the later exit. The cause of the stop is not established, and it is not attributed to registration. The API was **not automatically restarted**, to avoid undoing a possible deliberate stop. All recorded headed tests preceded this exit and retain their tested binary identity. The normal configured SMTP files remain unchanged; the local QA SMTP sink remains stopped. Start normally from the workspace with `dotnet run --project PointOfSale.API` when development should resume.
