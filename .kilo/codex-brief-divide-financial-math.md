Task: Add or validate a scoped `divide(a, b)` helper in the existing frontend financial math utility, with focused tests.

Repository: C:\Users\fadya\source\repos\PointOfSale

Current state observed by Codex:
- The working tree already has many unrelated modified and untracked files. Do not touch them.
- The intended scope appears to be:
  - `PointOfSale.Web/src/utils/financialMath.js`
  - `PointOfSale.Web/src/utils/financialMath.test.ts`
- A candidate `divide` export may already exist in `financialMath.js`.
- Candidate tests may already exist in `financialMath.test.ts`.

Required behavior:
- `divide(a, b)` should use the existing `toSafeNumber` helper for both inputs.
- It should return `0` when the divisor is zero or sanitizes to zero.
- Otherwise, it should return the sanitized dividend divided by the sanitized divisor.
- Keep this a general numeric helper. Do not apply currency rounding unless an existing local pattern clearly requires it.

Required tests:
- Add focused Vitest coverage in `PointOfSale.Web/src/utils/financialMath.test.ts`.
- Cover normal numeric division.
- Cover numeric strings accepted by `toSafeNumber`.
- Cover zero divisor.
- Cover invalid/null divisor sanitizing to zero.
- Keep tests narrow and consistent with the existing test style.

Scope rules:
- Edit only the two files listed above if changes are needed.
- Do not reformat unrelated code.
- Do not modify package files, snapshots, generated output, backend code, or unrelated frontend files.
- Do not stage or commit anything.

Verification command:
- From `PointOfSale.Web`, run:
  `npm test -- src/utils/financialMath.test.ts --run`

Final report requirements:
- State which files you changed, if any.
- State the verification command and result.
- Mention any relevant caveats.
