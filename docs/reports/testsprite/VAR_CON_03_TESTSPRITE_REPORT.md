# VAR-CON-03 TestSprite Report

Date: 2026-05-18

Result: Passed

Dashboard: https://www.testsprite.com/dashboard/mcp/tests/04921cc6-2128-418f-9a48-1086b8bd8ff9/4aa99758-7804-4ee6-936e-1e21ea25de30

TestSprite executed TC047 through the dedicated local runner only:
- `POST /api/test-auth/var-con-03/run` with `{"repetitions":1}`
- `GET /api/test-auth/var-con-03/run/{JobId}`

The runner completed with `AllPassed=true`, one iteration, and `ExitCode=0`. The generated remote test did not call product, variant, inventory, pricing, storefront, affiliate, CommerceHub, debug outbox, rebuild, reconciliation, dashboard, BI, analytics, or projection endpoints directly, and did not run `dotnet test` inside remote Python.
