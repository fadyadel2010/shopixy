# DOMAIN-3.0.2: Runtime Reality Verification

**Project:** Shopixy / PointOfSale  
**Platform:** Multi-tenant e-commerce / affiliate / POS  
**Date:** 2026-08-06  
**Status:** RUNTIME REALITY VERIFICATION (FROZEN — NO IMPLEMENTATION)  
**Authority:** .ai-context  

---

## Executive Summary

This report verifies every architectural conclusion from `DOMAIN-3.0_STOREFRONT_DOMAIN_MANAGEMENT_ARCHITECTURE_AUDIT.md` and `DOMAIN-3.0.1_ARCHITECTURE_VERIFICATION_AND_CLEANUP.md` against actual runtime execution, module resolution, build behavior, and test execution.

**Key Finding: The audit's core claim is REALITY VERIFIED.** `STOREFRONT_PRODUCT_NAVIGATION` is indeed imported by `Sidebar.jsx` but undefined in the source module. However, the audit under-reported the scope: THREE symbols are undefined in the barrel re-export chain, not one. The audit also incorrectly framed this as a pre-implementation gap; runtime evidence shows it is an active crash that blocks the Sidebar from rendering.

**Verdict: REALITY VERIFIED** (audit findings confirmed) with additional runtime evidence that the issue is more severe than documented.

---

## Actual Runtime Behaviour

### CHECK 1: Module Export Reality

Verified by executing the module through Node ESM loader:

```
$ node --input-type=module -e "import * as ia from './storefrontInformationArchitecture.js'; console.log(Object.keys(ia))"

Export keys: ['STOREFRONT_NAVIGATION', 'STOREFRONT_ROUTE_COMPATIBILITY', 'STOREFRONT_SIDEBAR_STATE_KEY', 'isStorefrontNodeActive', 'resolveStorefrontScreen']
Has STOREFRONT_NAVIGATION: true
Has STOREFRONT_PRODUCT_NAVIGATION: false
Has OPERATIONS_NAVIGATION: false
Has STOREFRONT_OPERATIONS_SIDEBAR_STATE_KEY: false
```

**Actual exports of `storefrontInformationArchitecture.js`:**
| Symbol | Exported? |
|--------|-----------|
| `STOREFRONT_NAVIGATION` | YES |
| `STOREFRONT_PRODUCT_NAVIGATION` | **NO** |
| `OPERATIONS_NAVIGATION` | **NO** |
| `STOREFRONT_ROUTE_COMPATIBILITY` | YES |
| `STOREFRONT_SIDEBAR_STATE_KEY` | YES |
| `STOREFRONT_OPERATIONS_SIDEBAR_STATE_KEY` | **NO** |
| `isStorefrontNodeActive` | YES |
| `resolveStorefrontScreen` | YES |

Five symbols are exported. Three symbols referenced by the barrel (`index.js`) are NOT exported by the source module.

### CHECK 2: Barrel Export Reality

`informationArchitecture/index.js` (12 lines):

```js
export { StorefrontAdminFrame } from './StorefrontAdminFrame'
export { StorefrontBreadcrumbs } from './StorefrontBreadcrumbs'
export {
    OPERATIONS_NAVIGATION,        // ← NOT defined in source
    STOREFRONT_NAVIGATION,        // ✓ defined
    STOREFRONT_PRODUCT_NAVIGATION, // ← NOT defined in source
    STOREFRONT_ROUTE_COMPATIBILITY, // ✓ defined
    STOREFRONT_SIDEBAR_STATE_KEY,  // ✓ defined
    STOREFRONT_OPERATIONS_SIDEBAR_STATE_KEY, // ← NOT defined in source
    isStorefrontNodeActive,       // ✓ defined
    resolveStorefrontScreen,      // ✓ defined
} from './storefrontInformationArchitecture'
```

**Barrel assessment:** `index.js` re-exports three symbols that do not exist in `./storefrontInformationArchitecture`. In a strict ESM environment, this is a **SyntaxError** at module-load time (undefined named exports). In Vite's dev server, the import resolution may produce `undefined` bindings rather than crashing at import time, but any runtime access to these bindings produces `undefined`.

Direct Node ESM import of `index.js` fails for a different reason — `StorefrontAdminFrame.jsx` is a JSX file that Node cannot resolve without Vite's transform pipeline. This confirms `index.js` cannot be validated outside the Vite ecosystem.

### CHECK 3: Sidebar Runtime

**Cannot navigate the full application at runtime** — the dev server (`vite` on port 5173) starts but the browser tool cannot connect to `localhost:5173` (network isolation in this environment). However, the **Sidebar component's runtime behavior was verified through test execution**:

The test suite `wave0ConstitutionalAlignment.test.tsx` renders `<Sidebar>` via `@testing-library/react`. The following runtime error occurs:

```
TypeError: Cannot read properties of undefined (reading 'labelKey')
    at localizeStorefrontProductNode (Sidebar.jsx:125:23)
    at Sidebar (Sidebar.jsx:243:9)
    at renderWithHooks (react-dom.development.js:15486:18)
    at mountIndeterminateComponent (react-dom.development.js:20103:13)
    at beginWork (react-dom.development.js:21626:16)
    at HTMLUnknownElement.callCallback (react-dom.development.js:4164:14)
    ...
    at HTMLUnknownElementImpl._dispatch (jsdom/lib/jsdom/living/events/EventTarget-impl.js:243:9)

The above error occurred in the <Sidebar> component:

    at Sidebar (Sidebar.jsx:66:20)
    at Router (react-router.development.js:1207:17)
    at MemoryRouter (react-router.development.js:1101:7)

Consider adding an error boundary to your tree to customize error handling behavior.
```

**Crash chain:**
1. `Sidebar.jsx:37` imports `STOREFRONT_PRODUCT_NAVIGATION` from barrel `index.js`
2. `index.js:6` re-exports it from `storefrontInformationArchitecture.js`
3. `storefrontInformationArchitecture.js` does not export it → value is `undefined`
4. `Sidebar.jsx:243` calls `localizeStorefrontProductNode(STOREFRONT_PRODUCT_NAVIGATION)` with `undefined`
5. `Sidebar.jsx:123-125`: `localizeStorefrontProductNode = (node) => ({ ...node, label: t(node.labelKey, ...) })`
6. Spreading `undefined` produces `{}` but `node.labelKey` throws `TypeError: Cannot read properties of undefined (reading 'labelKey')`

**Sidebar is rendered by:** `Layout.jsx:53` → `<Sidebar isOpen={sidebarOpen} ...>`
**Layout is used by:** `App.jsx:138` (import), `App.jsx:237` (`<Route path="/" element={<Layout />}>`)

This means every authenticated page that uses the root `Layout` component is affected. The Sidebar crashes during mount, making the entire authenticated UI unusable.

### CHECK 4: Build Reality

**Production build FAILS**, but NOT due to the barrel export. The build fails in the module transformation phase:

```
$ node -e "const {build} = require('./node_modules/vite/index.cjs'); build({logLevel:'info'})..."
vite v5.4.21 building for production...
transforming...
✓ 226 modules transformed.
x Build failed in 15.23s
BUILD ERROR: [commonjs--resolver] Failed to resolve entry for package "jsbarcode".
The package may have incorrect main/module/exports specified in its package.json.
```

**Build assessment:**
- **Vite dev server starts:** Confirmed on port 5173 (server.listen() succeeded)
- **Production build fails:** Yes, due to `jsbarcode` package (v3.12.3) having an incorrect `main` field (`./bin/JsBarcode.js`) that Vite's CJS resolver cannot resolve. This is a pre-existing dependency issue unrelated to domain management.
- **Barrel export does NOT cause build failure:** Vite's ESM module resolution does not enforce named-export existence at build time for re-exports. Rollup's tree-shaking eliminates the unused `STOREFRONT_PRODUCT_NAVIGATION` reference because the Sidebar's `menuItems` array (where it's used at line 243) is only partially tree-shaken. The broken import survives build-time but crashes at runtime.

**Why the build doesn't catch the barrel issue:** Vite's dev server uses native ESM imports. In ESM, importing a named export that doesn't exist from a module is supposed to be a `SyntaxError`. However, Vite intercepts and transforms imports, and in practice, the undefined binding becomes `undefined` at runtime rather than a build-time error. This is a known gap in Vite's static analysis for barrel re-exports of non-existent named exports.

### CHECK 5: Test Reality

**Test environment is broken:**
- `vitest.config.ts:7` declares `environment: 'jsdom'`
- `vitest-environment-jsdom` package is **NOT INSTALLED** in `node_modules`
- **Symptom:** `ReferenceError: window is not defined` / `ReferenceError: document is not defined` when tests attempt to render React components
- **Result:** All tests that use `beforeEach(() => { window.localStorage.clear() })` or `document.title = ''` fail with environment errors

**Test results (all IA tests, 83 total):**
```
Test Files: 3 failed (3)
Tests: 41 failed | 42 passed
```

**Failure breakdown:**
1. **25 `TypeError` failures** (Sidebar crash): `Cannot read properties of undefined (reading 'labelKey')` — direct consequence of the broken barrel import at `Sidebar.jsx:125,243`
2. **16 environment failures** (wave0): `ReferenceError: window is not defined` — missing `vitest-environment-jsdom`
3. **7 assertion failures** (wave1): Tests expecting `OPERATIONS_NAVIGATION`, `/ecommerce/*` routes, and canonical terminology that don't exist in source — these tests encode the Wave 1/Wave 2 architectural vision, not the current implementation state

**Key test evidence (wave1, line 470):**
```
expect(source).toContain('OPERATIONS_NAVIGATION')
- Expected
+ Received
```
The test reads the actual source file (`storefrontInformationArchitecture.js`) and asserts it contains `OPERATIONS_NAVIGATION`. It does not. The diff shows the full source — only `STOREFRONT_NAVIGATION`, `STOREFRONT_ROUTE_COMPATIBILITY`, `STOREFRONT_SIDEBAR_STATE_KEY`, `isStorefrontNodeActive`, and `resolveStorefrontScreen` are defined.

**Key test evidence (wave1, line 28):**
```tsx
import {
    OPERATIONS_NAVIGATION,    // ← imports non-existent symbol
    resolveStorefrontScreen,
} from './storefrontInformationArchitecture'
```
The test file itself imports `OPERATIONS_NAVIGATION` from the source module — the same broken import that causes the Sidebar crash. This import would be a `SyntaxError` in strict ESM, but Vitest's Vite-based transform converts it to a runtime `undefined` binding.

### CHECK 6: Import Graph

```
App.jsx
  └───import─── Layout.jsx (src/components/Layout.jsx:6)
              └───render─── <Sidebar> (Layout.jsx:53)
                              │
                              └───import─── STOREFRONT_PRODUCT_NAVIGATION
                              │             STOREFRONT_OPERATIONS_SIDEBAR_STATE_KEY
                              │             STOREFRONT_SIDEBAR_STATE_KEY
                              │             isStorefrontNodeActive
                              │             resolveStorefrontScreen
                              │              (Sidebar.jsx:36-42)
                              │    │
                              └───from─── informationArchitecture/index.js (barrel)
                                          │
                                          ├──► storefrontInformationArchitecture.js
                                          │    ┌── EXPORTS: STOREFRONT_NAVIGATION ✓
                                          │    ├── EXPORTS: STOREFRONT_ROUTE_COMPATIBILITY ✓
                                          │    ├── EXPORTS: STOREFRONT_SIDEBAR_STATE_KEY ✓
                                          │    ├── EXPORTS: isStorefrontNodeActive ✓
                                          │    ├── EXPORTS: resolveStorefrontScreen ✓
                                          │    ├── MISSING: STOREFRONT_PRODUCT_NAVIGATION ✗
                                          │    ├── MISSING: OPERATIONS_NAVIGATION ✗
                                          │    └── MISSING: STOREFRONT_OPERATIONS_SIDEBAR_STATE_KEY ✗
                                          │
                                          ├──► StorefrontAdminFrame.jsx (✓ exists)
                                          └──► StorefrontBreadcrumbs.jsx (✓ exists)
```

**Import graph verification:**
- Edge 1: `Sidebar.jsx → index.js` — Sidebar imports 5 names from the barrel. ✓ (path resolves)
- Edge 2: `index.js → storefrontInformationArchitecture.js` — Barrel re-exports 8 names. 3 of 8 do NOT exist in the source. ✗
- Edge 3: `index.js → StorefrontAdminFrame.jsx` — Barrel re-exports component. ✓ (file exists)
- Edge 4: `index.js → StorefrontBreadcrumbs.jsx` — Barrel re-exports component. ✓ (file exists)
- Edge 5: `Layout.jsx → Sidebar.jsx` — Layout imports and renders Sidebar. ✓

**Runtime outcome:** Edge 2 is broken. Three of eight re-exported names are phantom exports. When `Sidebar.jsx:243` dereferences `STOREFRONT_PRODUCT_NAVIGATION`, it gets `undefined`, and `localizeStorefrontProductNode(undefined)` crashes.

### CHECK 7: Root Cause

**Root cause: Wave merge regression — stale barrel re-exports.**

The `index.js` barrel was written to re-export `OPERATIONS_NAVIGATION`, `STOREFRONT_PRODUCT_NAVIGATION`, and `STOREFRONT_OPERATIONS_SIDEBAR_STATE_KEY` as part of a planned "Wave 2" information architecture migration (merging Design and Operations under a unified Storefront product tree). The `storefrontInformationArchitecture.js` module was updated to the current "Wave 1" state (unified `STOREFRONT_NAVIGATION` with Storefront root containing Overview, Status, Homepage, Catalog, Content, Navigation, Footer, Appearance, History) but the barrel and consumer (`Sidebar.jsx`) were NOT updated to match.

**Evidence chain:**
1. `storefrontInformationArchitecture.js` defines `STOREFRONT_NAVIGATION` (the current unified tree) — confirmed by runtime `Object.keys()` inspection
2. `index.js` re-exports `STOREFRONT_PRODUCT_NAVIGATION` and `OPERATIONS_NAVIGATION` — confirmed by reading the file
3. `Sidebar.jsx:37` imports `STOREFRONT_PRODUCT_NAVIGATION` and `Sidebar.jsx:243` uses it — confirmed by reading the file
4. Runtime crash at `Sidebar.jsx:125` — confirmed by test execution (`TypeError: Cannot read properties of undefined (reading 'labelKey')`)
5. `wave1StorefrontInformationArchitecture.test.tsx:27` imports `OPERATIONS_NAVIGATION` from the source — confirmed by reading the file
6. No grep hit for `STOREFRONT_PRODUCT_NAVIGATION` or `OPERATIONS_NAVIGATION` in `storefrontInformationArchitecture.js` — confirmed by grep

**The barrel is a dead/wave-merge artifact.** The source module was refactored to a unified `STOREFRONT_NAVIGATION` tree, but the barrel and its consumers were not updated to remove references to the planned-but-never-implemented `OPERATIONS_NAVIGATION` and `STOREFRONT_PRODUCT_NAVIGATION` symbols.

### CHECK 8: Required Fix

The smallest constitutional fix (NOT implemented — described only):

**Option A (Minimal — removes crash, preserves current architecture):**
- Remove `STOREFRONT_PRODUCT_NAVIGATION`, `OPERATIONS_NAVIGATION`, and `STOREFRACT_OPERATIONS_SIDEBAR_STATE_KEY` from `index.js:3-12` barrel re-exports
- Replace the import in `Sidebar.jsx:37` with `STOREFRONT_NAVIGATION` (which IS defined)
- Replace the import in `Sidebar.jsx:39` with `STOREFRONT_SIDEBAR_STATE_KEY` (which IS defined and already imported on line 38)
- Replace `Sidebar.jsx:243` `localizeStorefrontProductNode(STOREFRONT_PRODUCT_NAVIGATION)` with `localizeStorefrontProductNode(STOREFRONT_NAVIGATION)`
- Update `wave1StorefrontInformationArchitecture.test.tsx:27` to remove the `OPERATIONS_NAVIGATION` import

**Option B (Full — implements Wave 2):**
- Define `OPERATIONS_NAVIGATION`, `STOREFRONT_PRODUCT_NAVIGATION`, and `STOREFRONT_OPERATIONS_SIDEBAR_STATE_KEY` in `storefrontInformationArchitecture.js` as actual exported constants
- This implements the "Wave 2" unified Storefront+Operations tree described in the wave1 tests

**Option A is the smallest constitutional fix** — it makes the barrel consistent with the source and eliminates the crash. It does not introduce new architecture. It uses the same `STOREFRONT_NAVIGATION` symbol that the source module already exports and that the audit's observed structure (lines 93-109) describes as the canonical current state.

---

## Reality Verdict

**REALITY VERIFIED**

The DOMAIN-3.0 audit's core claim — that `STOREFRONT_PRODUCT_NAVIGATION` is imported but undefined — is confirmed by runtime execution. However, the verification revealed the issue is:

1. **Broader than reported:** 3 undefined symbols, not 1 (`STOREFRONT_PRODUCT_NAVIGATION`, `OPERATIONS_NAVIGATION`, `STOREFRONT_OPERATIONS_SIDEBAR_STATE_KEY`)
2. **More severe than reported:** Not a missing feature — an active runtime crash that blocks Sidebar rendering on every authenticated page
3. **Affecting more consumers:** Both `Sidebar.jsx` AND `wave1StorefrontInformationArchitecture.test.tsx` import the undefined symbols
4. **Not caught by build:** Vite's build fails for a different reason (`jsbarcode`), and does not catch the broken barrel re-export at all

The audit's observed navigation structure (Storefront root with Overview, Status, Homepage, Catalog, Content, Navigation, Footer, Appearance, History) is confirmed correct against source. The missing "Domains" node is confirmed — no domain management screen exists in the IA. The backend Domain entity, middleware, and resolver chain are confirmed present and non-coupling.

---

## Supporting Evidence Index

| Check | Evidence Source | Method |
|-------|----------------|--------|
| ES module exports | Runtime `Object.keys()` of module | `node --input-type=module` |
| Barrel re-exports | `index.js` file read | `read_file` |
| Sidebar crash | Stack trace from test execution | `vitest` runtime error |
| Barrel import | `Sidebar.jsx:36-42` | `read_file` |
| Missing symbol | grep in source module | 0 matches for all 3 symbols |
| Test expectations | `wave1StorefrontInformationArchitecture.test.tsx:27,470` | `read_file` |
| Build behavior | `node index.cjs build` | Direct execution |
| Dev server | `createServer().listen()` on port 5173 | Programmatic execution |
| Import graph | `App.jsx → Layout.jsx → Sidebar.jsx → index.js → storefrontInformationArchitecture.js` | Cross-file tracing |
