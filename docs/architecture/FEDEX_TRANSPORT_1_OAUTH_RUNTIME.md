# FEDEX-TRANSPORT-1 - OAuth Runtime

Date: 2026-07-08
Scope: FedEx OAuth transport authentication only
Status: IMPLEMENTED

## Executive Summary

FEDEX-TRANSPORT-1 implements only FedEx OAuth runtime authentication.

Implemented:

- token acquisition through official `POST /oauth/token`;
- server-side token cache;
- cache key scoped by carrier account, environment, and client ID;
- expiration-aware reuse;
- explicit 401-triggered refresh;
- bearer authorization header generation;
- OAuth-only transient retry policy;
- fail-closed OAuth response validation;
- secret-safe operational logging.

Not implemented:

- shipment creation;
- tracking;
- labels;
- pickup;
- service availability;
- address validation execution;
- response admission;
- business logic;
- geography, customs, carrier mapping, order, shipment lifecycle, frontend, or POS Admin changes.

## OAuth Flow

Official FedEx Authorization documentation defines OAuth token generation at
`POST /oauth/token` using form fields including `grant_type`, `client_id`, and
`client_secret`, with child credentials required for child credential grant
types. The runtime posts `application/x-www-form-urlencoded` to the official
sandbox or production base URL selected by environment.

Official sources:

- https://developer.fedex.com/api/en-us/catalog/authorization/v1/docs.html
- https://developer.fedex.com/api/en-us/guides/ratelimits.html
- https://developer.fedex.com/api/en-us/guides/best-practices.html

## Token Lifecycle

FedEx OAuth responses are accepted only when they contain:

- `access_token`;
- `token_type` equal to bearer;
- positive `expires_in`.

The token is stored server-side only with an absolute UTC expiration timestamp.
The runtime applies a small expiration skew before reuse so requests do not
start with a nearly expired token.

## Cache Strategy

Token cache key:

- carrier account number;
- environment: sandbox or production;
- client ID.

Tokens are not cached globally and cannot be shared between different accounts,
environments, or client IDs. The cache is in the singleton OAuth runtime and is
guarded by a per-key semaphore to avoid duplicate token storms.

## Refresh Strategy

The runtime refreshes only when:

- the cached token is expired or inside the expiration skew;
- caller invokes the explicit unauthorized refresh path after FedEx returns
  `401 Unauthorized`.

The normal authorization-header path reuses valid cached tokens and does not
request a token per carrier operation.

## Authority Matrix

| Concern | OAuth Runtime owns | OAuth Runtime does not own |
|---|---|---|
| Access token | Yes | Shipment identity |
| Token expiration | Yes | Shipment lifecycle |
| Refresh decision | Yes | Tracking truth |
| Authorization header | Yes | Labels |
| Transport authentication | Yes | Pickup |
| Client secret validation | Fail-closed input gate | Geography |
| OAuth response validation | Fail-closed token gate | Carrier Mapping |
| OAuth retry | Transient token request only | Orders, Checkout, Customs, SQL business truth |

## Failure Handling

Fails closed for:

- missing client ID;
- missing client secret;
- missing account number;
- invalid grant type;
- child grant type without child credentials;
- child credentials on `client_credentials`;
- malformed JSON;
- missing `access_token`;
- unknown `token_type`;
- missing, zero, or negative `expires_in`;
- rejected invalid credentials;
- final transient token request failure.

Invalid OAuth responses are not cached. Invalid credentials and unauthorized
configuration are not retried.

## Retry Policy

Retries are limited to OAuth token acquisition and only for transient failures:

- request timeout;
- HTTP `429`;
- HTTP `5xx`;
- transport timeout/request exception.

No retry occurs for invalid credentials, invalid client, unauthorized
configuration, malformed OAuth response, missing credentials, or missing account
scope.

## Logging Policy

Logs include only:

- correlation ID;
- request duration;
- HTTP status;
- retry count.

Logs never include:

- client secret;
- child secret;
- access token;
- bearer token;
- Authorization header.

## Regression Coverage

Updated:

- `PointOfSale.Tests/Architecture/FedExOfficialContractCompatibilityTests.cs`

Coverage freezes:

- token acquisition via official OAuth form POST;
- token cache reuse;
- cache isolation by account, environment, and client ID;
- expiration refresh;
- 401-triggered refresh;
- authorization header generation;
- invalid OAuth response fail-closed behavior;
- missing credential/account fail-closed behavior;
- transient-only retry;
- secret-safe logging;
- no shipment/tracking/label/pickup transport in `FedExAdapter`;
- no business logic, SQL, geography, carrier mapping, customs, POS Admin, or
  frontend behavior inside OAuth runtime.

## Verification

Command:

```powershell
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~FedExOfficialContractCompatibilityTests -nologo -v minimal
```

Result:

- PASS
- 31 passed
- 0 failed

Existing unrelated compiler/analyzer warnings remain.

## Final Recommendation

Approve FEDEX-TRANSPORT-1 as the FedEx OAuth Runtime implementation.

Recommended next phase: FedEx transport request execution and response
admission planning, still excluding production activation until credentialed
sandbox proof, production approval, mapping/origin/destination/customs coverage,
monitoring, logging, audit, rate-limit controls, rollback, and smoke tests are
complete.

Do not implement shipment creation, tracking, labels, pickup, or business logic
as part of this OAuth phase.

## Explicit Success Answers

- Reviewed ONLY OAuth Runtime? **Yes.**
- Runtime modified? **Yes, OAuth runtime only.**
- Backend modified? **Yes, OAuth client/runtime and DI HttpClient registration only.**
- Frontend modified? **No.**
- OAuth implemented? **Yes.**
- Token cache implemented? **Yes.**
- Refresh strategy implemented? **Yes, expiration and explicit 401 refresh only.**
- Authorization header generated? **Yes.**
- Business logic inside OAuth? **No.**
- Shipment Runtime modified? **No.**
- Carrier Adapter modified? **No shipment/tracking/label/pickup behavior changed; existing FedEx adapter operations remain fail-closed.**
- Architecture preserved? **Yes.**
- Backward compatible? **Yes.**
- Production-certifiable? **OAuth runtime is certifiable; FedEx production activation remains blocked by later transport, response-admission, operational, credential, and rollout gates.**
- Recommend next phase: **FedEx transport request execution and response admission planning, followed by credentialed sandbox certification.**

## AI Context Update Decision

No `.ai-context` files were updated. This phase implemented provider-specific
FedEx OAuth authentication and did not discover new permanent cross-provider
architectural truth.
