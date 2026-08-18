# SHOPIXY Paymob P6.2 API Key Provisioning

Date: 2026-08-14

Project: SHOPIXY / PointOfSale

Certification: BLOCKED

## 1. Configuration Contract

Canonical runtime authority:

```text
TenantPaymentMethod.ConfigEncrypted
DataProtection purpose: PaymentMethodSecrets
TenantId: 83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5
Provider code: PAYMOB
```

Findings:

| Question | Result |
| --- | --- |
| Is `ApiKey` already a supported runtime property? | YES |
| Exact normalized property name | `apiKey` |
| Runtime deserializer preserves it? | YES |
| Stored with `PaymentMethodSecrets` when present in config? | YES |
| Older admin configure endpoint can merge/persist it? | YES, through `PUT /api/admin/payment-methods/PAYMOB/configure` |
| Existing Payment Methods UI exposes it for Paymob? | NO |
| Super-admin provider UI exposes it for Paymob? | NO |
| Super-admin protected secret replacement allows it? | NO; currently `secretKey` and `hmacSecret` only |
| Core validator requires it for modern Paymob checkout? | NO |

Relevant symbols:

| File/symbol | Evidence |
| --- | --- |
| `PointOfSale.Core.Payments.PaymentMethodConfigurationValidator.NormalizeConfiguration` | Preserves arbitrary non-empty keys including `apiKey`. |
| `PointOfSale.API.Controllers.PaymentMethodsAdminController.ApplyConfigurationMutation` | Merges incoming config and protects `validation.NormalizedConfiguration` into `ConfigEncrypted`. |
| `PointOfSale.API.Services.Payments.Paymob.PaymobTenantConfigurationResolver.DecryptConfiguration` | Reads `ApiKey = validation.NormalizedConfiguration.GetValueOrDefault("apiKey")`. |
| `PointOfSale.API.Services.Payments.Paymob.TenantPaymobConfiguration.ApiKey` | Optional legacy API key property exists. |
| `PointOfSale.Web/src/pages/PaymentMethods.jsx` | Paymob form fields omit `apiKey`. |
| `PointOfSale.Web/src/super-admin/payment-providers/components/PaymobConfigurationForm.tsx` | Paymob protected secret inputs are `secretKey` and `hmacSecret` only. |

Conclusion:

`apiKey` already has a runtime-compatible encrypted storage shape, but the currently visible admin forms do not expose a safe Paymob API Key field.

## 2. Secure Provisioning

Provisioning was not performed.

Reason:

The user supplied Paymob Public Key, Secret Key, HMAC, MID, iframe ID, and Integration ID, but did not supply the separate Paymob Dashboard API Key required by Paymob's auth-token endpoint.

The pasted Secret/Public/HMAC values were not echoed into this report, not written to source, not written to plaintext configuration, and not used to replace the existing tenant credentials.

Current secure config check:

```text
public_key_configured YES
secret_key_configured YES
secret_key_decryptable YES
hmac_configured YES
hmac_decryptable YES
api_key_configured NO
configured_mid 645875
configured_integration_id 3153090
tenant_is_sandbox True
public_secret_country_match PASS
public_secret_mode_match PASS
secret_implied_region Egypt
secret_implied_environment Test
```

## 3. Paymob Authentication

Required Paymob auth endpoint:

```text
POST https://accept.paymob.com/api/auth/tokens
```

Result:

| Field | Result |
| --- | --- |
| API Key configured | NO |
| API Key decryptable | NO |
| Auth request sent | NO |
| HTTP status | N/A |
| Token created | NO |

No API Key or token was logged.

## 4. Integration Discovery

Required official/supported integration-list endpoint:

```text
GET https://accept.paymob.com/api/ecommerce/integrations?is_plugin=true&is_next=yes&page_size=500&is_deprecated=false&is_standalone=false
```

Result:

```text
endpoint_source Paymob official PHP library flow
base_url https://accept.paymob.com/
auth_endpoint POST https://accept.paymob.com/api/auth/tokens
integrations_endpoint GET https://accept.paymob.com/api/ecommerce/integrations?is_plugin=true&is_next=yes&page_size=500&is_deprecated=false&is_standalone=false
api_key_configured NO
public_secret_country_match PASS
public_secret_mode_match PASS
requested_mode Test
requested_region Egypt
discovery_status BLOCKED_API_KEY_NOT_CONFIGURED
```

No integration metadata was returned.

## 5. 3153090 Result

| Question | Result |
| --- | --- |
| Visible in Dashboard | YES, per user-provided dashboard evidence |
| Configured in tenant | YES |
| Visible via API integration list | UNPROVEN |
| API accepts Intention | NO, per prior/current P6 evidence |
| Same MID via integration list | UNPROVEN |
| Same Test account via integration list | UNPROVEN |

Current classification:

```text
UNPROVEN / API-REJECTED
```

`3153090` must remain configured until API-key discovery proves either that it is valid or that a fully matched same-account replacement exists.

## 6. Intention Result

No new Intention test was run in P6.2 because the missing API Key prevents the required account/integration reconciliation step.

Existing control evidence remains:

```text
POST https://accept.paymob.com/v1/intention/
payment_methods: [3153090]
amount: 41000
currency: EGP

HTTP 404
Integration ID/Name does not exist in our system
```

## 7. Security Verification

| Control | Result |
| --- | --- |
| API Key printed | NO |
| API Key written to source | NO |
| API Key written to appsettings | NO |
| API Key written to plaintext config | NO |
| Secret Key changed | NO |
| HMAC changed | NO |
| Public Key changed | NO |
| Integration ID changed | NO |
| Iframe changed | NO |
| Payment flow changed | NO |
| Adapter changed | NO |
| Frontend bundle received API Key | NO |

Security note:

Sensitive Paymob credentials were pasted into the conversation during P6.2. They were not repeated in this report. Those pasted credentials should be considered exposed outside the application secret store and rotated before any production or long-lived use.

## 8. Regression

No production source or tenant configuration change was made in P6.2. Therefore no COD, Manual Wallet, Paymob adapter, Intention, Unified Checkout, HMAC, or payment lifecycle regression was introduced by this step.

## 9. Build/EF

P6.2 stopped before provisioning because the required API Key was not provided/configured.

No production source change was made. Build/EF verification is not required to certify a no-op blocked configuration step, but `git diff --check` should be run after the final report update.

## 10. Worktree Safety

Before P6.2:

```text
git status --short: dirty tree with existing P4/P5/P6/C5 changes
git diff --stat: 181 files changed, 6762 insertions(+), 2801 deletions(-)
```

No reset, clean, restore, stash, commit, or push was performed.

## 11. Final Certification

BLOCKED

Reason:

The Paymob Dashboard API Key was not provided/configured. The supplied credential set did not include the separate API Key required by `POST /api/auth/tokens`, so Paymob integration discovery could not proceed.

Next safe action:

Provide the Paymob TEST Dashboard API Key for the same MID `645875` through a secure local input path, not chat, then rerun P6.2 provisioning and discovery.
