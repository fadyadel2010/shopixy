# Kilo Delegate Smoke Implementation: Safe Feed URL Masking Helper

You are Laguna S 2.1 working inside `C:\Users\fadya\source\repos\PointOfSale`.

## Task

Add a small isolated frontend helper and a focused test to prove Kilo Delegate can make a bounded implementation change.

Implement a helper that safely masks product feed URLs before display so raw feed tokens are never shown after one-time disclosure.

## Scope

Allowed files only:

- `PointOfSale.Web/src/utils/feedUrlMasking.js` (new file)
- `PointOfSale.Web/src/utils/feedUrlMasking.test.ts` (new file)

Do not modify backend files, migrations, snapshots, package manifests, lockfiles, existing application components, i18n files, or unrelated tests.

## Helper Contract

Export a function named `maskFeedUrlForDisplay(value)`.

Behavior:

- Accepts any input.
- Returns an empty string for non-string, blank, or unparseable values.
- For absolute URLs, preserve origin and path up to the feed channel, then replace the token segment with `••••••••`.
- For relative URLs, preserve the relative path up to the feed channel, then replace the token segment with `••••••••`.
- Expected feed URL shape includes `/feeds/{channel}/{token}` somewhere in the pathname, for example:
  - `https://shop.example/api/public/feeds/meta/raw-token-123`
  - `/api/public/feeds/google/raw-token-456`
- Preserve query strings only if they existed before the token segment; do not preserve query strings attached to the token URL.
- Never include the original token in the return value.
- Keep implementation small and dependency-free.

Acceptable examples:

- `maskFeedUrlForDisplay('https://shop.example/api/public/feeds/meta/raw-token-123')` returns `https://shop.example/api/public/feeds/meta/••••••••`
- `maskFeedUrlForDisplay('/api/public/feeds/google/raw-token-456')` returns `/api/public/feeds/google/••••••••`
- `maskFeedUrlForDisplay('not a url')` returns `''`

## Focused Test Requirements

Create `PointOfSale.Web/src/utils/feedUrlMasking.test.ts` using Vitest.

Cover at least:

- absolute URL token is masked and not leaked
- relative URL token is masked and not leaked
- trailing path/query after token is not leaked
- malformed, blank, and non-string inputs return empty string

## Verification Commands

Run from `PointOfSale.Web`:

- `npm test -- --run src/utils/feedUrlMasking.test.ts`

If the command fails due to an environment/pre-existing issue, report the exact failure.

## Stage/Commit Rules

Do not stage or commit anything.

## Final Worker Report

Report:

- files changed
- verification command run and exact result
- any caveat or blocker
- explicit statement that no files were staged or committed
