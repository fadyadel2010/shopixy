# VAR-CON-01 TestSprite Report

Date: 2026-05-18

## Result

- TestSprite case: TC045 `var_con_01_concurrent_same_variant_attribute_combination`
- Status: Passed
- TestSprite test id: `14a60cb0-cff0-4cfb-9b72-91f27f049fa1`
- Project id: `060761b4-255b-4016-a560-cf890c790989`
- Local runner: `POST /api/test-auth/var-con-01/run` then `GET /api/test-auth/var-con-01/run/{JobId}`
- Dashboard: https://www.testsprite.com/dashboard/mcp/tests/060761b4-255b-4016-a560-cf890c790989/14a60cb0-cff0-4cfb-9b72-91f27f049fa1

## Validation Boundary

TC045 used only the dedicated local runner endpoint. The remote TestSprite code did not synthesize concurrent variant creation, call product/variant/inventory/pricing/projection endpoints directly, or replace the deterministic PostgreSQL overlap with sleeps. The local runner executed the authoritative VAR-CON-01 xUnit scenario on the Windows machine against the real integration stack.

The TestSprite node process completed TC045 and wrote a passed repo-local result. It logged one tunnel probe timeout before retrying successfully.

## Coverage Confirmed

- concurrent creation of the same variant attribute combination
- SQL `AttributeCombinationKey` uniqueness as variant identity authority
- exactly one authoritative variant identity
- deterministic fail-closed rejection of the losing concurrent creation
- exactly one SKU owner
- exactly one barcode owner
- exactly one inventory ownership linkage
- pricing ownership remains on the surviving authoritative variant row
- no orphan variant attributes, stocks, or stock movements
- fresh-reader no-uncommitted variant/SKU/barcode/stock/idempotency visibility during overlap
- no duplicate processed-event completion for the new variant path
- post-commit idempotency replay does not mint another variant
- storefront, affiliate, CommerceHub, cache, and search timing remain non-authoritative and cannot select variant identity
