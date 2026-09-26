# Main Storefront Colors & Typography Final Certification

Date: 2026-09-25

## Verdict

PASS for the Storefront Colors & Typography scope on MAIN/PRIMARY.

The supported Appearance authority now works end to end through Admin draft authoring, optimistic concurrency, signed candidate preview, publication, public live projection, Storefront CSS variables, all 10 active official theme shells, English/LTR desktop, Arabic/RTL mobile, and exact post-certification restoration.

## Scope

Certified values:

- Primary, secondary, background, text, and muted colors
- Font family and base font size
- UI border radius

No unsupported typography or color authority was invented. Heading weights, line height, letter spacing, button typography, hover/focus tokens, and separate border/link/button colors remain outside this contract.

## Authority Chain

The verified chain is:

1. PostgreSQL `StoreConfigs` owns legacy Appearance columns plus `ThemeDraftOverridesJson` and `ThemeLiveOverridesJson`.
2. `PUT /api/admin/store-config/theme/overrides` writes the draft only and requires accepted row-version evidence through `If-Match`.
3. Signed theme previews project the draft preset and draft overrides while retaining the requested theme shell.
4. `POST /api/admin/store-config/theme/publish` promotes draft Appearance to live and requires current row-version evidence.
5. `GET /api/storefront/store-config` remains anonymous and live-only.
6. Storefront runtime maps accepted values to CSS custom properties on the document root.
7. Theme-shell identity remains a separate authority from Colors & Typography.

## Remediation Completed

### Base Font Size

- Added `baseSize` to the Admin theme view contract.
- Mapped the backend theme font base size into draft and live Admin responses.
- Added Admin base-size authoring from `12px` through `24px`.
- Included base size in draft persistence, response hydration, preset selection, and the local candidate preview.

### Validation And Accessibility

- Matched Admin client validation to backend-supported hex, font, and base-size formats.
- Blocked malformed color candidates before any mutation.
- Added explicit label associations, stable control IDs, `aria-invalid`, and accessible validation messages.
- Backend validation rejects unsupported root keys, color keys and formats, fonts, typography keys, base sizes, and radius values.

### Session Stability

- Added edit-generation tracking around save and publish operations.
- A response from an in-flight save no longer overwrites a newer local edit.
- Publication pauses when a newer edit occurs during the prerequisite draft save.
- Newer edits made while publication is in flight remain local and unsaved after the accepted draft is published.
- Focus, blur, visibility, and copy events do not reload StoreConfig or discard local edits.

### Storefront Runtime Tokens

- Added `--color-primary-rgb` and `--color-secondary-rgb` derivation for runtime alpha styles.
- Added `--color-text-muted` as the accepted muted-color alias.
- Added matching fallback declarations in the Storefront stylesheet.

### Signed Draft Preview

- Fixed signed theme preview to use `ThemePresetKey` and `ThemeDraftOverridesJson` rather than the live preset and live overrides.
- Preserved the requested signed theme-shell override while applying the draft Appearance candidate.
- Added a backend regression test proving draft Appearance is returned without falling back to live values.

## Admin Workflow

The real Chromium workflow verified:

- Successful authenticated navigation to Colors & Typography
- Correct hydration of all eight baseline controls
- Accessible malformed-color feedback and zero mutation attempts for invalid input
- Local preview updates for color, font, base size, and radius
- Delayed draft-save response with a newer local edit retained
- Final draft save with HTTP `200` and accepted row version
- Live public StoreConfig remaining unchanged before publication
- Explicit publication with HTTP `200`
- Published values visible through public StoreConfig and live Storefront CSS variables
- Zero console errors, failed requests, or HTTP errors after the authenticated authoring surface became ready

Candidate used for certification:

| Value | Candidate |
| --- | --- |
| Primary | `#7c3aed` |
| Secondary | `#0f766e` |
| Background | `#fff7ed` |
| Text | `#1c1917` |
| Muted | `#78716c` |
| Font | `Cairo` |
| Base size | `18px` |
| Border radius | `14px` |

## Browser Matrix

The signed draft candidate was checked in Chromium for every active official theme:

- `modern`
- `minimal`
- `fashion`
- `pearl`
- `phoenix`
- `hype`
- `alizon`
- `sharp`
- `cosmetics`
- `seven`

The disabled experimental `electronics` theme was excluded.

Each theme was checked at:

- English desktop: 1440 x 1000, LTR
- Arabic mobile: 390 x 844, RTL

Results across all 20 combinations:

- Signed preview config HTTP `200`: 20/20
- Exact candidate CSS variables: 20/20
- Correct theme-specific product-card renderer: 20/20
- Header and footer rendered exactly once: 20/20
- No horizontal overflow: 20/20
- Correct language direction: 20/20
- Auth requests from signed preview: 0
- Console errors: 0
- Failed requests: 0
- HTTP errors: 0

The published candidate was also checked on English desktop and Arabic mobile. Both live checks had exact CSS variables, correct direction, no horizontal overflow, no failed requests, and no unexpected HTTP or console errors.

Fresh anonymous live sessions perform the existing customer-auth probes to `/api/storefront/auth/me` and `/api/storefront/auth/refresh`; both return expected `401` guest responses. These were recorded separately and were not treated as Colors & Typography failures. Signed previews made no auth requests.

## Security

| Check | Result |
| --- | --- |
| Anonymous mutation | Denied before write, HTTP `400` from trusted tenant resolution |
| Missing `If-Match` | HTTP `428` |
| Stale `If-Match` | HTTP `409` |
| Malformed backend payload | HTTP `400` |
| Tampered signed preview token | HTTP `401` |
| Public draft leakage before publish | None |
| Signed preview auth requests | None |

The anonymous `400` occurs before MVC action execution because unauthenticated input cannot establish trusted tenant authority and inbound tenant headers are stripped. The StoreConfig row remained unchanged.

## Restoration

The immutable baseline was captured before login or mutation at:

`.opencode-tmp/colors-typography-cert/baseline.json`

Baseline SHA-256:

`77424FA1F60FBB31FB60B54AB6980D2714346D444D695D26B28E1B6A0D845EEE`

The baseline file remained read-only and retained the same hash after certification.

Row-version, update timestamp, publication version, and version-history audit metadata advanced as a required consequence of accepted row-versioned save/publish/restoration operations. They were not rewound through direct database writes. Exact restoration refers to the Appearance authorities, public projection, theme identity, and protected cross-impact surfaces listed below.

Restored Appearance state:

| Value | Final value |
| --- | --- |
| Primary | `#2563eb` |
| Secondary | `#0f172a` |
| Background | `#ffffff` |
| Text | `#020617` |
| Muted | `#64748b` |
| Font | `Inter` |
| Base size | `16px` |
| Border radius | `8px` |
| Draft preset | `default` |
| Live preset | `default` |

Restoration verification passed for:

- Exact draft override JSON
- Exact live override JSON
- Exact legacy Appearance columns
- Exact public Appearance projection
- Theme identity fingerprint
- Homepage fingerprint
- Navigation fingerprint
- Footer fingerprint
- CMS Pages fingerprint
- Products fingerprint
- Other-tenant StoreConfig, Homepage, CMS Pages, and Products fingerprints

The active theme-shell identity remained `alizon`; Colors & Typography certification did not activate a theme.

## Automated Validation

| Validation | Result |
| --- | --- |
| Admin Appearance/service/authority Vitest | PASS, 4 files and 30 tests |
| Storefront token/preview-auth Vitest | PASS, 3 files and 6 tests |
| Backend Admin/preview/runtime tests | PASS, 48 tests |
| API Release build | PASS, 0 warnings and 0 errors |
| Storefront TypeScript and production build | PASS |
| Admin production build | PASS |
| Git whitespace validation | PASS |

Frontend builds retain pre-existing stale Browserslist, dynamic/static import, and bundle-size warnings. They are non-failing optimization notices and did not affect the certified behavior.

## Runtime Health

- API listener: port `5010`
- Liveness: HTTP `200`, Healthy
- Readiness: HTTP `503`, Unhealthy
- Sole unhealthy readiness entry: `payout_external_truth_readiness`
- PostgreSQL, Redis, MongoDB, Elasticsearch, RabbitMQ, MassTransit, and scoped Storefront requests were healthy during certification.

The readiness failure is an unrelated payout reconciliation mismatch and did not block this scope.

## Evidence

Machine-readable evidence and screenshots are outside the repository at:

`C:\Users\fadya\AppData\Local\Temp\opencode\colors-typography-final-evidence`

Primary evidence file:

`C:\Users\fadya\AppData\Local\Temp\opencode\colors-typography-final-evidence\final-audit.json`

Evidence includes:

- 20 signed-preview screenshots
- 2 published live screenshots
- 2 Admin screenshots
- Exact CSS-variable checks
- Language, direction, viewport, overflow, header, footer, and product-theme measurements
- Security response statuses
- Admin session-stability and draft/live-isolation outcomes
- Final baseline and cross-impact restoration results

Credentials were supplied only through temporary process environment variables. They were not written to repository files, evidence JSON, screenshots, or this report.

## Final State

- Tenant: `AhramStore`
- Storefront: `http://ahramstore.localhost:5174/`
- Active theme shell: `alizon`
- Appearance draft: restored to baseline
- Appearance live: restored to baseline
- Theme identity and all protected content authorities: preserved
- Active official themes certified: 10
- Experimental `electronics`: disabled and excluded
- Scoped certification: PASS
