# MAIN Product Image Upload Differential Final Certification

## 1. Executive Summary

Final decision: **INCONCLUSIVE**.

The root cause is proven. The Windows and Dell machines have the same tracked upload source at commit `13c7afd2c9d8161e51205b657f3a996514220c6b`, but not the same effective runtime. Windows runs `Development`; because R2 is absent, dependency injection selects writable `DevelopmentFileStorageService`. Dell runs `QA`; non-Development environments select `R2StorageService`, but every required R2 setting is absent. Authorized JPEG and WEBP uploads therefore reach the API and fail in `R2StorageService.EnsureConfigured()` before any S3 client, network call, object write, or product-media persistence.

The approved remediation is environment-only: provision the canonical R2 settings through an approved Dell-side secret source and restart only the API. It was not applied because no credential-bearing R2 source or values are accessible. Application code was intentionally not changed. Dell browser upload, real R2 connectivity, persistence, and cleanup certification remain blocked.

## 2. Incident Description

- Windows development reportedly accepts product image uploads and shows them in the gallery.
- Dell Firefox showed `POST http://localhost:5173/api/upload` returning HTTP 500 in about 50 ms.
- The Admin supports JPG, PNG, and WEBP images. MP4 and YouTube are separate video workflows and were not conflated with this image incident.
- The Dell failure is reproducible in structured API logs for JPEG and WEBP, including a repeated WEBP attempt.

## 3. Known Screenshot Evidence

The screenshots themselves were not available as files in this workspace. The user-provided observations establish the UI state, request URL, status, and approximate latency only. They do not establish the response body or failing subsystem. The API logs supply that missing evidence.

## 4. Repository And Build Identity

| Fact | Windows development | Dell QA |
|---|---|---|
| Repository | `C:/Users/fadya/source/repos/PointOfSale` | `/home/fady/ShopixyQA/PointOfSale` |
| Branch / HEAD | `main` / `13c7afd2c9d8161e51205b657f3a996514220c6b` | same |
| Upload source blobs | `UploadController` `0345000d...`; `Program.cs` `59432142...`; lockfile `2c7419cd...` | same |
| Worktree baseline | one pre-existing untracked development-upload PNG | one pre-existing untracked `PointOfSale.API/appsettings.QA.json` |
| Runtime assembly | Debug SHA-256 `bace1fd9...` | Release SHA-256 `e826a07d...` |
| Node / npm | `v22.20.0` / `10.9.3` | `v20.20.2` / `10.8.2` |
| Vite / Axios | `5.4.21` / `1.20.0` | same |

Worktree lockfile byte hashes differ because the Windows checkout uses different line endings; the Git blob id is identical. Unrelated untracked files were preserved.

## 5. Upload Request Call Chain

```text
ProductForm.jsx
  -> ProductMediaManager.handleFilesSelected
  -> client MIME / extension / 10 MB image validation
  -> FormData(file, entityType=product, entityId=productId)
  -> Axios POST /api/upload (baseURL /api)
  -> Vite localhost:5173 proxy to localhost:5010
  -> authentication, host resolution, tenant resolution, authorization
  -> UploadController.Upload
  -> ASP.NET Core multipart binding to UploadRequest.File
  -> FileValidationService signature + MIME + extension validation
  -> IR2StorageService.UploadFileAsync
     -> Windows Development without R2: DevelopmentFileStorageService
     -> Dell QA: R2StorageService -> EnsureConfigured -> PutObjectAsync
  -> UploadResponse(url, fileName, canonical contentType, size)
  -> separate POST /api/admin/products/{productId}/media with Idempotency-Key
  -> ProductMediaController.AddMedia
  -> ProductMediaService tenant-scoped product validation
  -> ProductMedia row + projection/outbox events
  -> local gallery append; GET media on reload/remount
```

This is a two-request workflow. `POST /api/upload` does not itself create a `ProductMedia` row. The Dell fails during the first request, so the second request is not issued by the frontend.

## 6. Runtime Configuration Comparison

| Setting | Windows development | Dell QA |
|---|---|---|
| Effective environment | `Development` | `QA` |
| Storage selection | Development filesystem fallback | R2 required |
| R2 account/service endpoint | absent | absent |
| R2 access key id | absent | absent |
| R2 secret access key | absent | absent |
| R2 bucket | absent | absent |
| R2 public URL | absent | absent |
| Image limit | 10 MB | 10 MB |
| Multipart/request limit | 100 MB | 100 MB |
| Accepted images | JPEG, PNG, WEBP by MIME, extension, and signature | same |
| Admin proxy | `/api` -> `http://localhost:5010` | same |

No secret values were read into evidence. The Dell process environment and `appsettings.QA.json` were checked for presence only. No alternate Dell credential source was found. The untracked QA JSON has mode `664`, so it must not be used to store secrets without correcting the secret-management design and permissions.

## 7. Request And Response Evidence

Correlated authorized Dell API entries include:

| UTC | Canonical type | Result | API latency | Stage reached |
|---|---:|---:|---:|---|
| `2026-09-27T10:14:55.603Z` | `image/jpeg` | 500 | 14.116 ms | `R2StorageService.EnsureConfigured` |
| `2026-09-27T10:15:15.190Z` | `image/webp` | 500 | 8.9276 ms | same |
| `2026-09-27T10:15:54.183Z` | `image/webp` | 500 | 16.9229 ms | repeated failure, same |

The exact original filenames and sizes were not logged, so they are not claimed. `UploadController` maps this configuration exception to the safe body `{"error":"An unexpected error occurred"}`. The browser's original response export was not available.

A controlled generated 9-byte PNG with SHA-256 `843ac23b...` was submitted unauthenticated through both Vite and direct API URLs. Both returned controlled `400 TENANT_REQUIRED`; Vite took 15.301 ms and direct API took 39.093 ms. This proves the proxy target and upstream behavior, but is not an authorized upload test. A forged `X-Tenant-Id` was not accepted as authority and also returned `TENANT_REQUIRED`.

## 8. Backend Exception

Sanitized source location:

```text
System.InvalidOperationException: Cloudflare R2 is not configured.
  at R2StorageService.EnsureConfigured() in R2StorageService.cs:441
  at R2StorageService.UploadFileAsync(...) in R2StorageService.cs:107
  at UploadController.Upload(...) in UploadController.cs:105
```

Full sanitized excerpts are in `.opencode-tmp/product-image-upload-cert/evidence/sanitized-log-excerpts.json`. Correlation, tenant, user, connection, and request identifiers were removed from preserved evidence.

## 9. Storage Provider Investigation

`Program.cs:549-562` selects development filesystem storage only when the environment is Development and R2 is incomplete. QA always selects `R2StorageService`. `R2Options.IsConfigured` requires access key id, secret key, bucket, and a service URL resolved from account id, explicit service URL, or a compatible public URL.

On Dell, `EnsureConfigured()` throws at the first line of `UploadFileAsync` before filename generation, client creation, DNS, TLS, signing, or `PutObjectAsync`. Therefore DNS, TLS, bucket authorization, object-key behavior, and outbound Cloudflare connectivity were not involved in the observed failures and cannot yet be certified.

On Windows, `/health/ready` reports the R2 check Healthy in `development-file` mode and confirms the resolved development storage root is writable. The overall ready endpoint is still Unhealthy because of unrelated dependencies.

## 10. Root Cause Determination

1. The Dell API's R2 configuration admission check failed.
2. The ASP.NET Core API generated the 500, not Vite or a reverse proxy.
3. The exact condition was an incomplete `R2Options`: all canonical settings were absent.
4. Windows works because `Development` intentionally falls back to private local filesystem storage.
5. Dell fails because `QA` requires R2 and does not have its required runtime secrets/configuration.
6. This is an environment/configuration defect, not a differing tracked source defect.
7. It is reproducible across JPEG and repeated WEBP requests.
8. Validated MIME types and successful controller binding rule out client-side validation, multipart boundary, route, and request parsing for those attempts. The exception before S3 client creation rules out R2 DNS/TLS/network as the observed cause.

## 11. Confirmed Cause And Ruled-Out Hypotheses

Confirmed: missing Dell QA R2 runtime configuration.

Ruled out for the logged attempts:

- Vite proxy connection failure: the API logged and handled each request.
- API route mismatch: `UploadController.Upload` executed.
- Authentication or tenant resolution failure: logs contain resolved authenticated request context.
- Multipart field/boundary defect: `UploadRequest.File` was bound and validated.
- JPEG/WEBP validation defect: canonical content types were logged by storage.
- Request size rejection: the requests reached storage; no 413 occurred.
- R2 DNS, TLS, or provider rejection: no S3 client or network request was created.
- Local filesystem permission failure on Dell: QA did not select local storage.

Not tested or ruled out after configuration: credential validity, endpoint format, bucket permissions, DNS, TLS, and outbound access.

## 12. Code And Configuration Changes

No application code or runtime configuration was changed. This follows the environment-only remediation policy.

Approved but blocked remediation:

- Securely inject `R2_ACCOUNT_ID` or `R2_SERVICE_URL`.
- Securely inject `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY`, and `R2_BUCKET`.
- Optionally inject `R2_PUBLIC_URL`; root-relative application serving remains supported when it is absent.
- Grant only intended bucket permissions. The current readiness check also requires the ability to list at most one object.
- Restart only the Dell API, confirm its new PID/environment, and require the `r2` readiness entry to be Healthy before browser testing.

Do not commit these values, place them in this report, copy them from Windows, disable TLS, or switch QA to a different provider merely to make the test pass.

## 13. Files Changed

Application source/configuration: none.

Audit artifacts created:

- `docs/reports/MAIN_PRODUCT_IMAGE_UPLOAD_DIFFERENTIAL_FINAL_CERTIFICATION_2026-09-27.md`
- `.opencode-tmp/product-image-upload-cert/evidence/baseline-environment-comparison.json`
- `.opencode-tmp/product-image-upload-cert/evidence/request-response-metadata.json`
- `.opencode-tmp/product-image-upload-cert/evidence/sanitized-log-excerpts.json`
- `.opencode-tmp/product-image-upload-cert/evidence/remediation-decision.json`
- `.opencode-tmp/product-image-upload-cert/evidence/test-results.json`
- `.opencode-tmp/product-image-upload-cert/evidence/browser-results.json`
- `.opencode-tmp/product-image-upload-cert/evidence/final-audit.json`

## 14. Regression Tests

| Machine | Command | Result |
|---|---|---|
| Windows | `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~ProductMediaUploadContractTests" --nologo` | 7 passed, 0 failed, 0 skipped; pre-existing warnings |
| Windows | `npm test -- --run src/components/ProductMediaManager.test.tsx` | 7 passed, 0 failed on isolated rerun |
| Dell | same Vitest command | 7 passed, 0 failed |
| Dell | focused .NET command | compilation exceeded 8 minutes and was stopped; stale `--no-build` assembly contained no matching current test |

The first Windows Vitest attempt ran zero tests because a fork worker timed out while other builds ran. The isolated rerun passed and is classified as a test-harness failure, not an upload defect.

Existing tests verify JPEG/PNG/WEBP contracts, invalid MIME/extension/signature cases, gallery mock persistence, multiple images, and safe server validation display. They do not prove real R2 connectivity. No new tests were added because the proven defect is external configuration.

## 15. Browser Certification

Dell: not certified. Firefox was running and historical browser attempts correlate with API logs, but no post-remediation workflow could be run because R2 was not provisioned. No Playwright/Chromium package is installed in the Admin project, and private Firefox cookies were not extracted.

Windows: not certified in this audit. Success is user-reported and a pre-existing local object is present, but no controlled same-file browser workflow was executed.

## 16. Supported Format Matrix

| Format | Frontend test | Backend contract test | Dell real pre-fix | Dell post-fix | Windows controlled |
|---|---:|---:|---:|---:|---:|
| JPG | pass | pass | reaches storage, then config 500 | blocked | not run |
| PNG | pass | pass | not captured with authorized context | blocked | not run |
| WEBP | pass | pass | repeated config 500 | blocked | not run |
| Invalid | controlled frontend/backend rejection tests pass | pass | not run authenticated | blocked | not run |

## 17. Security And Tenant Isolation

| Check | Result |
|---|---|
| Authentication | Global fallback policy requires authentication; live unauthenticated requests were rejected before endpoint authorization with controlled `400 TENANT_REQUIRED`, not the documented 401 |
| Forged tenant header | Not accepted as authority in controlled unauthenticated probe |
| Tenant object-key prefix | Static pass: server uses resolved `RequiredTenantId` |
| Product association ownership | Static pass: `ProductMediaService` requires product id and resolved tenant id |
| Cross-tenant content read | Static pass: route requires tenant-owned product/media predicates |
| Insufficient-role user | Not live-tested; current upload/media endpoints rely on authenticated fallback policy and do not declare a narrower owner/admin permission policy |
| Unknown tenant | Controlled rejection observed |
| Cross-tenant authenticated operation | Not live-tested |

Authorization was not weakened.

## 18. Storage And Database Integrity

For the observed Dell failures, `EnsureConfigured()` executes before generated filename/object key and before `PutObjectAsync`; no object could be created. Because the frontend awaits upload success before the association request, no `ProductMedia` row was created by those failed attempts. No test product or media row was created by this audit.

Residual design risks, not causes of this incident:

- Upload does not validate product ownership/existence before object creation; association validates it later, so a successful upload followed by failed association can orphan an object.
- Product media deletion removes the row but does not delete the stored object.
- Upload itself has no idempotency key; only media association is idempotent, and retry uses a new key.

## 19. Build Results

| Command | Result |
|---|---|
| `dotnet build PointOfSale.API\PointOfSale.API.csproj -c Release --no-restore --nologo --verbosity minimal` | pass, 187 warnings, 0 errors, 49.96 s |
| `npm run build` in `PointOfSale.Web` | pass, 53.02 s, chunk-size warning |
| `npm run build` in `PointOfSale.Storefront` | pass, 43.48 s, Browserslist/import/chunk warnings |
| `git diff --check` | pass before report creation; final rerun recorded after artifact creation |

These are passes with warnings, not clean-warning builds.

## 20. Runtime Process And Health Details

Windows:

- Admin: PID `8708`, `[::1]:5173`, `PointOfSale.Web` Vite.
- API: PID `5956`, parent `dotnet run` PID `11316`, loopback port `5010`, Development.
- `/health/live`: HTTP 200 Healthy.
- `/health/ready`: HTTP 503 overall due unrelated checks; R2 entry Healthy in development-file mode.

Dell:

- Admin: PID `7078`, `127.0.0.1:5173`, `/home/fady/ShopixyQA/PointOfSale/PointOfSale.Web`.
- API: PID `34387`, parent PID `1`, `0.0.0.0:5010`, QA, detached Release assembly.
- API log: `/home/fady/.deploy-qa/logs/qa-api-20260927-023734.log`.
- `/health/live`: HTTP 200.
- `/health/ready`: HTTP 503; `r2` Unhealthy, `Cloudflare R2 is not fully configured.`
- `/health`: HTTP 503 with the same R2 result plus other dependency checks.

`localhost:5173` is conclusively the Admin Vite server, not the API.

## 21. Cleanup And Restoration

- Temporary unauthenticated probe files were removed immediately.
- No R2 object, local media object, product, or database row was created by this audit.
- No cleanup of existing media was attempted or required.
- The Dell API/Admin and Windows API/Admin processes were not restarted.
- The hung Dell test process started by this audit was stopped; live runtime processes were untouched.
- Pre-existing untracked files remain present.

## 22. Known Limitations

- No authorized disposable product/account was made available to a non-interactive harness.
- Existing Firefox session data was deliberately not extracted.
- No real R2 credentials, endpoint, bucket, DNS, TLS, or permissions could be tested.
- Original tester filenames, sizes, response exports, and exact Firefox version string were unavailable.
- Zero-byte, over-limit, storage timeout, cancellation, role, and authenticated cross-tenant live matrices were not run.
- The frontend clears file selection after failure (`MediaUploadButton.jsx:27`), so the selected file is not retained; retry requires re-selection.

## 23. Remaining Blockers

1. Provision the approved canonical R2 settings through a protected Dell-side runtime secret source.
2. Restart only the Dell API and verify process identity and `r2` readiness.
3. Use the existing authorized Firefox session or an approved local test-account secret to run JPG, PNG, WEBP, invalid-file, retry, reload, ordering, primary-image, console, network, storage, DB, duplicate, and cleanup checks.
4. Repeat a controlled Windows browser workflow if both-machine PASS is required.

## 24. Final Certification Decision

**INCONCLUSIVE - evidence is sufficient to prove the root cause, but insufficient to prove remediation or certify either machine in a controlled browser workflow.**

No global production-readiness claim is made.
