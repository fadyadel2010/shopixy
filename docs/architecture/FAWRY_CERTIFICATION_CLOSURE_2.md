# FAWRY-CERTIFICATION-CLOSURE-2 — Official Charge Contract Compliance

Date: 2026-07-15  
Finding: `FAWRY-FINAL-001` only  
Scope: Active POS Admin + Storefront PayAtFawry initialization  
Governing architecture: `PAYMENT_REFERENCE_FREEZE.md`  
Runtime posture: Preserved and unchanged

## Certification Decision

The Board finding was **valid**. The active initialization path did not supply
required customer mobile, customer email, or language values. It passed the
internal `PAY-...` payment reference through as `merchantRefNum`, emitted the
repository-normalized `PAYATFAWRY` value instead of the value shown by the
current official PayAtFawry contract, and allowed required official fields to
be omitted by the adapter.

A minimum bounded correction was implemented in the Application layer and
Fawry Provider Adapter only. No Runtime, Runtime Bridge, lifecycle, controller,
SQL, migration, entity, DTO, frontend, or business-rule source was changed by
this closure.

## Official Documentation Evidence

Evidence was re-read from the current official FawryPay documentation on
2026-07-15:

- [Create Payment Requests Using FawryPay Reference Number](https://developer.fawrystaging.com/docs/server-apis/create-payment-refno-apis)

The official charge table and request example establish the following current
contract:

| Concern | Current official contract |
|---|---|
| `merchantCode` | Required merchant account code. |
| `merchantRefNum` | Required integer; the unique charge reference in the merchant system. |
| `customerProfileId` | Optional integer merchant customer identity. |
| `paymentMethod` | Required PayAtFawry method; the current reference-charge page and examples use `PayAtFawry`. |
| `customerMobile` | Required merchant customer mobile; the documented form is an Egyptian `01xxxxxxxxx` number. |
| `customerEmail` | Required merchant customer email. |
| `amount` | Required decimal in two-decimal format. |
| `description` | Required charge description. |
| `language` | Required; allowed notification languages are `ar-eg` and `en-gb`. |
| `chargeItems` | Required item array with item id, description, price, and quantity. |
| `signature` | Required SHA-256 of merchant code, merchant reference, optional customer profile, payment method, two-decimal amount, and secure key. |

The response contract distinguishes the two reference authorities:

- `referenceNumber` is issued by FawryPay and is the customer-facing payment
  reference;
- `merchantRefNumber` is the merchant-issued reference echoed from the charge
  request.

The implementation must not substitute the merchant reference for the Fawry
reference returned to the customer.

## Root Cause

The original FAWRY-2 translation contract certified a generic repository-shaped
request in which customer mobile, customer email, description, and language
were optional metadata. The later application convergence supplied the local
payment reference but did not acquire customer contact fields from order
authority and did not select an official language value. The adapter preserved
the local alphanumeric reference and normalized the payment method to an
internal uppercase spelling.

Consequently, prior tests proved deterministic repository mapping but did not
prove the complete current official PayAtFawry serialization used by the active
application path.

## Current Implementation Review

### `PaymentService`

The active Fawry branch still delegates to
`IFawryRuntimeApplicationService`; no direct provider HTTP path was added.
Before delegation it now obtains customer contact data from authoritative
application data already attached to the order:

1. immutable checkout snapshots `Order.GuestName`, `GuestPhone`, and
   `GuestEmail`;
2. same-tenant `OnlineCustomer` identity as a fallback;
3. same-tenant POS `Customer` identity as a fallback.

Missing mobile or email fails closed with
`FAWRY_CUSTOMER_DETAILS_REQUIRED` before provider execution. The application
supplies `en-gb`, matching the storefront's existing English fallback, because
the active permitted boundary has no durable per-order locale field. This
closure does not add or infer a new locale authority.

### `FawryRuntimeApplicationService`

The application request now requires customer mobile, customer email, and
language. It maps those values into provider metadata, supplies the documented
`PayAtFawry` method value, and continues to supply the order description,
amount, currency, expiry, internal payment reference, and deterministic action
lineage. Invalid incomplete requests fail before tenant credential resolution
or provider execution.

### `FawryPaymentAdapter`

The adapter now:

- requires mobile, email, description, and language;
- normalizes Egyptian `+20...`/`20...` mobile input to documented
  `01xxxxxxxxx` form and rejects other shapes;
- validates and normalizes customer email;
- maps only supported notification languages to `ar-eg` or `en-gb`;
- emits the documented `PayAtFawry` value and signs that exact value;
- rejects a non-integer optional customer profile identity;
- projects the local merchant payment identity and canonical action lineage to
  a stable 18-digit `merchantRefNum`;
- continues to treat the Fawry-issued `referenceNumber` as the projected
  customer reference code.

The numeric merchant reference is deterministic for replay and changes when
the merchant payment identity changes. It is used consistently for request
serialization, signature material, and adapter idempotency projection.

## Implemented Correction

Modified in scope:

- `PointOfSale.API/Services/Payments/PaymentService.cs`
- `PointOfSale.API/Services/Payments/Fawry/FawryRuntimeApplicationService.cs`
- `PointOfSale.API/Services/Payments/Adapters/FawryPaymentAdapter.cs`
- focused Fawry certification tests

Explicitly unchanged by this closure:

- `PointOfSale.Core/Payments/Runtime/**`
- `FawryRuntimeBridge`
- payment lifecycle services
- SQL, migrations, EF entities, and schema
- controllers and public DTOs
- frontend and CommerceHub
- business rules

## Focused Certification Tests

`FawryCertificationClosure2Tests` proves:

- all current required official fields are serialized;
- merchant-reference output is numeric, deterministic for replay, bound to the
  local merchant payment identity, and used as the idempotency projection;
- customer mobile, customer email, and language are normalized to official
  forms;
- missing required fields fail before provider I/O;
- the correction remains in Application/Adapter source and Runtime stays
  provider-neutral.

Existing provider and application tests were corrected to use the current
official contract rather than the obsolete repository-shaped request.

## Regression

Executed in this closure session:

| Command / slice | Result |
|---|---:|
| API build | **Passed**, 0 warnings, 0 errors |
| `FawryCertificationClosure2Tests` | **8 passed**, 0 failed |
| `FawryProviderAdapterCertificationTests` + `FawryApplicationContractCertificationTests` | **22 passed**, 0 failed |
| Frozen Payment Runtime slice (`FullyQualifiedName~PaymentRuntime`) | **94 passed**, 0 failed |
| Complete Fawry slice (`FullyQualifiedName~Fawry`) | **99 passed**, 7 failed, 0 skipped |

The seven complete-slice failures are not caused by this finding or correction:

- four `FawryWebhookReliabilityTests` are blocked before webhook behavior by
  local PostgreSQL schema drift: `Orders.BillingAddressAdmissionEvidenceId`
  does not exist in the local test database;
- three `FawryPaymentAuthorityCertificationTests` retain historical source-text
  assertions for superseded implementation details and the absent historical
  `PASS SF-PAY7` context marker. These were already outside the current
  application/adapter contract and were not modified under this one-finding
  mission.

Therefore the focused charge-contract regression and frozen Runtime regression
pass. An unqualified whole-Fawry regression decision remains withheld for the
pre-existing database and historical-test conditions above.

## Final Certification Matrix

| Question | Decision |
|---|---|
| Was the Board finding valid? | **Yes.** |
| Official charge contract now satisfied? | **Yes, for the active source initialization path covered by this closure.** |
| Customer fields certified? | **Yes.** Required mobile/email acquisition, normalization, serialization, and fail-closed behavior are covered. |
| Merchant reference certified? | **Yes.** It is merchant-derived, numeric, stable for replay, distinct from the Fawry-issued customer reference, and included in signature/idempotency projection. |
| Regression passed? | **Focused contract and Runtime regression: yes. Whole-Fawry aggregate: withheld because of seven pre-existing unrelated failures documented above.** |
| Runtime preserved? | **Yes. 94/94 Runtime tests pass.** |
| Architecture preserved? | **Yes. Correction is limited to Application and Provider Adapter boundaries.** |
| Recommend Closure-3? | **Yes, as a separately bounded review of the next Board finding. This decision does not close or implement any finding other than `FAWRY-FINAL-001`.** |

## Closure Decision

`FAWRY-FINAL-001` is **CLOSED at source**. The active initialization path now
serializes the current official PayAtFawry charge contract and keeps merchant
reference authority distinct from the Fawry-issued customer payment reference.

This closure does not claim Sandbox validation, deployment readiness, live
provider acceptance, whole-provider software certification, or closure of
`FAWRY-FINAL-002` and later findings.
