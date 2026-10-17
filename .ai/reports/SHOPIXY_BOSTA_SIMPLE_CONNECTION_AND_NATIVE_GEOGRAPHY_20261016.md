# Bosta simple connection and native geography — 2026-10-16

**Implemented and installed. Credentials/connectivity and Bosta-native destination preparation no longer require a national canonical publication. Production booking remains blocked by explicit Shopixy disablement and unverified provider-account/service eligibility. No live shipment or business transaction was created.**

Evidence: `.ai/evidence/phase8-20261016/`. Architecture: `docs/architecture/BOSTA_NATIVE_DESTINATION_WORKFLOW.md`.

## 1. Decision and provider evidence

**Genuine Bosta destination IDs are sufficient for Bosta's address contract. Shopixy canonical UUIDs are not a provider requirement.**

Fresh official documentation reads on October 16:

- https://docs.bosta.co/docs/how-to/format-bosta-address specifies a city name, independent address `firstLine` longer than five characters, and either genuine `districtId` or `cityId` together with `districtName`. It identifies `/cities?countryId=...`, `/cities/{cityId}/districts`, and district `pickupAvailability` / `dropOffAvailability`.
- https://docs.bosta.co/docs/how-to/create-your-first-delivery specifies delivery type **10**, required drop-off address, package details/item count and correct goods information. It explicitly describes default pickup behavior when `businessLocationId` is omitted. Shopixy retains the safer existing concrete verified-default resolution rather than relying on an untested omission.
- Retained official OpenAPI `.ai/evidence/phase6-20261015/bosta-api.yaml`, `AddressCreateDelivery` lines 999–1033 and `BusinessadddeliveryRequest`, agrees about provider identity. The YAML and prose differ in package-type terminology; this phase retains the existing tested `Parcel` wire contract rather than silently changing that separate contract.

Fresh HTML copies and SHA256 provenance are `official-native-address.html`, `official-create-delivery.html`, and `official-contract-sources.json`. PowerShell's initial documentation download failed on a 308; successful retained copies used redirect-following GETs. No provider mutation was used to establish the decision.

Actual saved-production-account reference reads returned **28 cities / 592 Cairo districts**. Selected and successfully validated:

| Provider identity | Genuine provider ID |
|---|---|
| Egypt | `60e4482c7cb7d4bc4849c4d5` |
| Cairo | `FceDyHXwpSYYF9zGW` |
| Zahraa Nasr City district | `VPRW3NEl5WQ` |
| Account-default pickup | `PZ_CVuhF4R` |

These strings are **not Shopixy canonical UUIDs**. No national place, canonical ID, global mapping, geographic approval or service entitlement was fabricated.

## 2. Implemented boundaries

### Credentials and connection

- Existing encrypted credential save and saved-account authentication are independent of geographic publication. Actual Admin save/re-read and test both returned **200** with no national catalog.
- Authentication validates the authenticated pickup-list response, not whether a configured pickup is owned or serviceable. An origin-selection problem can no longer masquerade as an invalid API key. Explicit pickup changes are still independently ownership-validated.
- A no-op save retains the existing key, environment, credential-reference identity, unrelated settings and current connection evidence. Material credential/environment changes invalidate version-bound evidence. Existing tenant/account and permission checks remain.
- Latest test result, environment and timestamp remain separate from integration lifecycle and booking eligibility. Neither page rendering, discovery nor connection testing activates the integration or publishes geography.

### Explicit integration activation

- Bosta's explicit activation action requires a current successful saved-account connection, but not a national catalog, warehouse-address publication or destination selection.
- It enables Shopixy's **carrier-specific** configuration and account lifecycle. It does not certify Bosta's provider-side operational activation or a shipment. Caller-supplied readiness Booleans cannot assert that eligibility.
- Explicit disable disables both carrier configuration and lifecycle, including setup-stage accounts. Activation/disable remain audited actions. No merchant-wide shipping switch was introduced or conflated with these carrier-specific flags.
- The real account was **not activated or disabled during browser verification**; activation/disable behavior is covered by isolated backend tests.

### Origin and destination

- Both explicit and provider-default pickup modes require bounded authenticated ownership evidence for booking readiness. Missing/expired proof is an origin blocker, not a credential failure. The existing unique-default discovery, frozen concrete origin, warehouse inventory binding and before-create revalidation are retained.
- New `BostaNativeDestination` and `BostaNativeDestinationService` admit only the exact provider-observed Egypt city/district relationship with drop-off availability. Wrong account/environment, changed credentials, stale discovery, unknown/foreign-parent and unavailable references fail closed.
- Cities/districts load only through explicit authenticated saved-account actions. City discovery now supplies the genuine observed country ID to the documented endpoint.
- Account-page destination validation is a preview: **serviceable does not mean booking-ready**. A shipment-specific save independently discovers and admits the selected references.
- Checkout keeps its existing domestic delivery address and supported standard service; it does not demand national mapping publication. Provider geography is mandatory at shipment preparation/registration, through either the new native snapshot or the retained published-mapping path. Missing native evidence does not enable a delivery POST.
- Other carriers' canonical address/mapping policies are unchanged. Aramex still requires its published mapping. Existing governed-bootstrap/import/four-eyes publication remains available under optional advanced Admin controls.

### Persistence — no schema migration

The existing shipment-only `CarrierGeographicMappingSnapshotJson` jsonb column stores a tagged/versioned **native provider destination**, with existing environment/account-scope/timestamp companion columns. Canonical mapping-release ID/version/key remain **null**; no fabricated release or country UUID is inserted.

The snapshot contains tenant/original-order/shipment, carrier account/configuration/credential-reference/environment, credential-material hash, unchanged address/recipient fingerprint, real provider references/hierarchy/serviceability and observation time. Credential plaintext and credential hashes are not returned in the new Admin readback DTO. Timestamp precision is aligned with PostgreSQL microseconds.

Persistence guards separately restore native versus canonical evidence and retain immutable snapshots across ordinary, detached and delete/clear attempts. Original order/customer address and canonical catalog records are untouched. Existing canonical snapshot formats and historical recovery remain supported.

New API routes, with no-store and management permission (shipment save additionally requires fulfillment permission):

```
GET  /api/admin/shipping/carrier-accounts/{id}/bosta/native-geography?cityId=...
POST /api/admin/shipping/carrier-accounts/{id}/bosta/destinations/validate
GET  /api/admin/shipments/{id}/bosta-destination
PUT  /api/admin/shipping/carrier-accounts/{id}/bosta/destinations/{shipmentId}
```

The validation POST performs provider GETs only. Shipment details expose explicit destination read/setup/save; that endpoint rejects a non-Bosta original order and cannot convert Manual COD fulfillment.

### Create/cancel protections

The durable intent includes the native destination alongside the existing frozen origin and measured parcel. Current account/configuration/credential-reference ownership is checked before the create claim. Before delivery POST, the adapter independently reloads genuine provider geography and rejects changed hierarchy or unavailable serviceability. No provider HTTP was added inside the create-claim transaction.

Measured parcel dimensions/weight, actual declared goods value, physical item count, recipient/address, supported standard service, EGP/COD constraints, stable unique business reference, single durable create claim, original tenant/order/account/environment binding, no blind repost after ambiguity, and original-identity cancellation remain enforced. Existing cancellation eligibility, at-most-one mutation and exact independent terminal-state read are retained.

## 3. Actual Admin and hosted API evidence

`admin-browser-workflow4.txt` exercises the actual running Admin and installed API, with a page-scoped guard permitting only connection save/test and read-only destination validation mutations:

| User action / check | Observed result |
|---|---|
| Save retained credential/configuration | **200**; configuration, unrelated account values and current test identity preserved |
| Test saved production connection | **200 / Succeeded**, `2026-10-16T13:47:49.870467Z` |
| Load pickup locations | **200**; unique provider default verified; selector remains empty, no default saved |
| Load Bosta cities / Cairo districts | **200 / 200**, 28 / 592 genuine references |
| Select and validate genuine district | **200**, validated/serviceable; `bookingReady=false` |
| Submit a genuine unavailable district candidate | **409 `BOSTA_DESTINATION_NOT_SERVICEABLE`**; connection remains current/successful |
| Unavailable options | Disabled; zero enabled unavailable options |
| Render setup | Zero provider-reference requests before explicit actions |
| Configuration / protected order comparison | All compared values unchanged |
| Unexpected shipping mutations | Zero; only two connection POSTs and two provider-read validation POSTs |

`admin-browser-reload-localization1.txt` verifies actual reload, preserved saved/current connection and disabled integration, English/Arabic labels and incomplete-booking message, Arabic RTL, and explicit activation action. **Zero Bosta requests or shipping mutations on reload/language change.** No secret was re-entered; the existing session and encrypted credential were reused.

`admin-hosted-boundaries*.txt` verifies a non-Bosta account cannot use the native Bosta endpoint (**404**), and anonymous requests are rejected **400 at the existing tenant-context boundary**, not falsely claimed as an observed 401 policy test. Permission declarations and forbidden-role checks are separately exercised in backend tests. The existing governed draft still reads back **621 candidates**, same ID and SHA256 `b237dd4f6cd6ace6135e6dd6c1af355499a7f6cdae696316bf211e23fba08b1a`; no publication occurred.

Browser harness failures are retained rather than counted as application failures: invocation 1 used an unavailable sandbox `URL` global; its failed route guard was explicitly removed. Invocation 2 still encountered that failed guard; invocation 3's safe guard rejected the incorrectly singular `/connection-test` path. After using the actual plural `/connection-tests` contract, invocation 4 passed. No blocked create/activate/cancel request was sent to the API.

No actual shipment-specific destination save was performed on merchant data: there is no authorized Bosta order to mutate, and the existing Manual COD order is expressly protected. Shipment-specific service/create/cancel behavior and real SQL snapshot persistence are proven separately below; this is not labeled a live Admin-to-order booking certification.

## 4. Backend, persistence and final verification

All results are per invocation; overlapping totals are not added.

| Invocation | Result |
|---|---|
| `backend-regressions3` | **594/594**, zero skipped: connection/configuration/readiness, native destination, default/explicit pickup, Bosta contracts/create/cancel/recovery, shipping methods/country scope, authorization, geographic governance and existing selected regressions |
| Native-specific cases within that run | **23 passed**; exact results in `native-specific-backend-results.json`, not replaced by unrelated health/test totals |
| `backend-native-final` | **23/23**, zero skipped, separate final invocation of the new native/connection/permission cases |
| `backend-native-postgres1` | **1/1** actual configured PostgreSQL snapshot save/reload, null canonical IDs, detached-clear rejection and immediate constraints; rollback and independent zero-row observation |
| `backend-geographic-architecture1` | **50 passed / 3 historical failures of 53**; unchanged missing initial geographic migration and GEO-3/GEO-4 freeze markers |
| `admin-focused2` | **200/200** actual component/API-boundary regressions, including native selection, foreign/environment/stale response rejection, explicit snapshot save and bilingual contracts |
| `admin-full2` | **1,862/1,862**, full maintained Admin suite, one worker |
| `storefront-tests1` | **528/528** |
| `admin-typecheck2`, `storefront-typecheck1` | Passed |
| `admin-build1`, `storefront-build1` | Passed; isolated production output directories, live dist not replaced |
| `backend-build7`, `debug-build1` | Passed; final Release test build and source-attested Debug API build |
| `protected-order-final` | **1/1**, actual read-only SQL observation; all compared order/warehouse/physical-line/reservation/shipment fields equal the prior phase |
| `admin-lint1` | **7 errors / 119 warnings**, unchanged finding groups from prior latest result; new native files have zero findings. This inherited lint gate is not closed |

Controlled native tests use real captured reference IDs with an isolated no-network transport. They exercise actual shipment service orchestration, durable intent/request construction, declared value **75 EGP versus 150 EGP COD**, exact item count, changed serviceability with zero POSTs, rotated/foreign/environment/manual rejection, retained-address tampering, ambiguous create/no repost, exact-identity cancellation and replay with one controlled cancellation mutation. They create no live provider data. These process-local shipment tests are not PostgreSQL create/cancel concurrency certification.

The **separate real PostgreSQL test** uses the existing schema, a new UUID-scoped synthetic tenant/order/shipment inside one rollback-only transaction, and genuine captured provider reference strings. It proves tagged native JSON/scalar roundtrip and persistence guards, not provider/account entitlement. No migration, reset, schema creation or commit occurs; independent observation confirms no rows remain in the tested tenant/order/shipment/account/outbox/audit scopes.

Earlier failed/interrupted invocations remain retained: `backend-native1` was **65 passed / 1 failed** because the new cancellation replay test changed its reason; identical replay intent fixed it. `backend-regressions1` had **5 obsolete Bosta geography/activation expectations**, updated to assert the requested independent states without changing other carriers' assertions. `backend-regressions2` passed 594 with the opt-in SQL case skipped; the SQL case was subsequently explicitly run, and `regressions3` excludes it to report zero skips. `backend-build2` had a test namespace qualification error. `admin-focused1` had **199 passed / 1 failed** due to a non-unique “No” query after adding the separate switch state. `admin-full1` was terminated by the shell's two-minute limit; the complete fresh `full2` run used sufficient terminal time, without increasing test timeouts or retries.

## 5. Installed runtime and current states

Debug build attestation: **3,611 unchanged captured backend inputs**, nine artifact hashes. The user stopped/restarted the API; exactly nine verified runtime artifacts were installed with original binaries backed up. Configuration was unchanged during installation. The restarted original Debug apphost is PID **12572**, start **2026-10-16T13:44:16.0079698Z**. API/Core/Infrastructure hashes match the attested build and disk writes precede process start (`runtime-loaded.json`). Actual new endpoint behavior independently verifies the installed source path; this is disk/process equivalence, not a managed-memory MVID assertion.

| State | Current evidence |
|---|---|
| Credentials/connectivity | Saved encrypted API key; **current Production authentication succeeded** at the timestamp above |
| Shopixy integration activation | **Disabled**, account `ConnectionTestPending`; no silent activation |
| Provider operational activation/service eligibility | **Not currently verified by the API-key connection contract**; retained October 15 profile reported `isActive=false` |
| Pickup | Genuine unique provider default independently discovered/verified, not selected or saved; bounded proof requires refresh after expiry/restart |
| Native destination | Genuine city/district validated and serviceable without canonical publication; not persisted to any real order/shipment |
| Shipment booking | **Not ready / `canExecute=false`**; no eligible real parcel/order booking was attempted |

After explicit default discovery, current account blockers are:

```
CARRIER_CONFIGURATION_DISABLED
BOSTA_INTEGRATION_ACTIVATION_REQUIRED
BOSTA_PROVIDER_ACCOUNT_ELIGIBILITY_UNVERIFIED
```

**National geography is no longer a Bosta setup or account-readiness blocker.** The remaining provider-account gate is real evidence/entitlement: the working pickup API key cannot certify operational business activation or requested delivery-service eligibility. Shopixy deliberately does not accept a caller Boolean, geographic publication or authentication success as that evidence. The retained inactive profile is not relabeled as a fresh provider observation or a proven service rejection; no production create trial was used to resolve it.

For any future separately authorized real Bosta order, explicit merchant activation, genuine provider operational eligibility, current pickup, accepted/revalidated native destination, actual recipient/parcel/contents/value and all existing financial/inventory/order gates still apply. The protected Manual COD order is never a candidate for conversion.

## 6. Preservation and disposition

- **No live Bosta delivery, cancellation, pickup, dispatch or label mutation. No real order, shipment, charge, inventory or business transaction was created.** Only permitted retained-settings save, connection-test/audit metadata and read-only reference/validation operations touched the running application.
- Protected Manual COD order remains Pending/unpaid, EGP 40, with zero shipments; actual SQL projection comparison is entirely unchanged (`protected-order-comparison.json`).
- No national catalog/mapping publication or Easy Orders setting/account change. Existing draft/reference evidence and canonical governance remain available.
- No credential, cookie, token or authentication-state export. No real credentials appear in this report or new test fixtures; synthetic test credentials cannot reach a network transport.
- No staging, commits, reset, clean, stash or unrelated cleanup. Git index is unchanged; zero missing original status entries; **142 scoped original source copies**, zero missing originals. The initial broader hash inventory timed out and is explicitly not claimed as a completed full-worktree attestation (`preservation.json`).
- The three inherited geographic historical-artifact failures and existing lint **7/119** remain open; no historical migration/freeze claims or rule suppressions were fabricated to erase them.

**Disposition:** the simplified connection/native-destination software path is implemented, installed and safely verified. Production booking authorization/eligibility is not established, and no production shipment effect was performed.
