# CRUD Archetype — Hermes Browser Testing Canonical Contract

**Document Type:** Canonical archetype — inheritance layer for Browser Knowledge
**Status:** Canonical — stable for years
**Layer:** One of the core inheritance layers beneath future `*KNOWLEDGE.yaml` files. This document expands ONLY the CRUD archetype first identified in `PAGE_ARCHETYPE_DISCOVERY.md`. It does not redefine that discovery, nor any other archetype.
**Companion Artifact:** The Browser Knowledge generation skill consumes this archetype by name and layers page-specific deltas on top.
**Authority Note:** This document defines *browser interaction behavior only*. It is deliberately independent of any business domain. No entity, field, rule, endpoint, or persistence mechanism is asserted here. A future Browser Knowledge file supplies those; everything behavioral is inherited from this document. Where this document states that validation is server-authoritative, it states a browser-evidence expectation (the browser must observe the server's verdict), not an implementation detail.

---

## 1. Purpose

### 1.1 Why CRUD exists

The overwhelming majority of administrative and operational application surfaces are collections of uniform records that a user searches, reads, creates, edits, and deletes. Across every domain the *shape* of that interaction is identical: a toolbar, a data region, a way to locate a record, a way to open it, and a way to mutate it. The records differ; the browser contract does not.

### 1.2 Why it is reusable

A CRUD page is defined by its interaction pattern, not by what it stores. Once the pattern is captured — how search behaves, how a dialog opens and closes, how a create is submitted and confirmed, how a delete is guarded — that capture applies to any collection, present or future. Re-describing the pattern per collection produces duplicated, drifting knowledge.

### 1.3 Why Hermes should inherit CRUD behavior instead of learning every page individually

- **Transfer.** A new collection shipped next quarter reuses the same contract. Hermes recognizes it on first encounter from the inherited layer instead of requiring a new knowledge file.
- **Single point of maintenance.** A change to the generic list/modal/validation contract is made once, here, and benefits every CRUD page at once.
- **Reasoning consistency.** Generic QA expectations (server-authoritative validation, idempotency on create, response-shape validation, fail-closed on ambiguous state) are applied uniformly across all collections because they live in one place.
- **Reduced knowledge weight.** A page-specific file declares only its deltas (route, entity name, unique fields, special validations, exceptions). The bulk of behavioral knowledge is inherited, not repeated.

---

## 2. Browser Identity

### 2.1 Typical characteristics

A CRUD page presents a bounded collection of uniform records. The collection is the page's reason to exist. The page is read-biased: the default state is *view the collection*, and mutation is a deliberate, usually modal, sub-flow.

### 2.2 Typical interaction model

The page is **read-first, act-second**. The user lands on a populated (or empty) collection, narrows it with search/filter/sort/pagination, then performs a single-record or bulk action. Creation and editing happen inside an overlay (modal or drawer) or a dedicated record route, not by in-place mutation of the collection view. Deletion is a confirmed, reversible-in-intent destructive act.

### 2.3 Typical browser layout

- An application shell (sidebar/nav) is assumed present; CRUD is the content surface inside it.
- A **toolbar** region above the data region: search input, filter controls, and a primary create affordance.
- A **data region** rendering records as a table or as cards.
- Optional **pagination / load-more** beneath the data region.
- Optional **detail panel / drawer** beside or replacing the data region when a single record is opened.
- Optional **modal** layered over the page for create/edit.

### 2.4 Typical navigation

- Reached via a sidebar/menu link to a list route.
- A record may be opened in place (drawer/modal) or via a deep link to a record route (`:id` / `:id/edit`).
- The collection route is the canonical landing; record routes are sub-states of it.

### 2.5 Typical user expectations

- The collection loads quickly and shows a stable first page.
- Search and filter are **non-destructive**: applying them does not lose the user's place irrecoverably, and clearing them returns to the full collection.
- Actions are discoverable from the toolbar and from per-row controls.
- Destructive actions ask for confirmation.
- The view is consistent after a browser refresh: the same collection (and, where appropriate, the same filters) is restored.
- Validation errors are shown clearly and block submission rather than silently discarding input.

---

## 3. Expected Page Structure

A CRUD page is composed of a small set of UI components. Not every page contains every component. The contract distinguishes **required** from **optional**.

### 3.1 Required components

- **Data region** — the collection rendered as a table or as cards. This is the definitional element of the archetype. If there is no data region, the page is not CRUD.
- **Loading state** — an explicit indicator while the collection (or a page/filter of it) is being fetched.
- **Empty state** — an explicit, non-error presentation when the collection yields no records for the current scope.
- **Error state** — an explicit, recoverable presentation when the collection cannot be loaded.

### 3.2 Optional components

- **Toolbar** — a container for search, filters, and the primary create affordance. Absent only on minimal read-only list surfaces.
- **Search** — a free-text input that narrows the collection. Often debounced.
- **Filters** — one or more controls (dropdowns, toggles, date pickers) that constrain the collection by a field or facet.
- **Sort controls** — column headers or controls that reorder the collection.
- **Pagination / load-more** — divides a large collection into pages or incrementally appends.
- **Create button** — the primary affordance that opens the create sub-flow.
- **Row actions** — per-record controls (typically edit and delete) revealed on hover or in an overflow menu.
- **Bulk actions** — operate on a multi-record selection (delete, status change, export). Present only when selection is supported.
- **Selection checkboxes** — enable bulk actions; may be per-row, with a select-all-in-view control.
- **Detail panel / drawer** — shows a single record's full content without leaving the list; may be read-only or editable.
- **Modal** — overlays the page for create/edit, capturing a form and a submit/cancel pair.
- **Inline validation messages** — attached to form fields within a create/edit sub-flow.

### 3.3 Component contracts (browser-observable only)

- A **modal** traps focus, dims the underlying page, and is dismissable by cancel, backdrop click (if enabled), or escape — each restoring the prior collection state.
- A **drawer** slides in from an edge and leaves the collection visible; closing it returns focus to the triggering control.
- **Pagination** shows current page, total pages (or count), and page controls; changing page preserves the active search/filter scope unless the page explicitly resets it.
- **Empty / error / loading** are mutually exclusive visible states for the data region at any moment.

---

## 4. Browser Navigation Contract

This contract describes the canonical path a browser session takes through a CRUD surface. It is descriptive of expected behavior, not a script.

```
Landing
   ↓
Read page (collection renders, or empty/error state shows)
   ↓
Understand available actions (toolbar, row actions, bulk actions visible)
   ↓
Search (narrow the collection by free text)
   ↓
Filter (constrain by field/facet; combine with search)
   ↓
Open record (row → drawer/modal/record route)
   ↓
Create (open create sub-flow, fill, submit)
   ↓
Edit (open record, change fields, submit)
   ↓
Delete (confirm, removal reflected in collection)
   ↓
Refresh (browser reload or in-page refresh)
   ↓
Verify persistence (the earlier mutation is still present after refresh)
```

### 4.1 Notes on the navigation path

- Steps may be entered in any order; the path above is the *complete* set, not a forced sequence.
- **Search** and **Filter** are locating steps; they precede acting on a record but are not prerequisites for Create.
- **Open record** is the bridge between the collection and the edit/detail sub-flows.
- **Refresh / Verify persistence** is the closure that proves the mutation was durable, not merely optimistic.

---

## 5. Browser Interaction Contract

The expected interaction model for every CRUD page. These are behaviors Hermes should be able to exercise and observe.

### 5.1 Locating

- **Searching** — typing into the search input narrows the collection. Behavior may be debounced; a new keystroke may cancel the prior in-flight request.
- **Filtering** — selecting a filter value constrains results; multiple filters combine (intersection) unless the page documents otherwise.
- **Sorting** — activating a sort control reorders the collection; toggling direction is expected where sort is bidirectional.
- **Pagination** — moving to another page fetches/renders the corresponding slice; the active search/filter scope is preserved across page changes.

### 5.2 Acting on a record

- **Opening dialogs** — a create/edit/detail sub-flow opens as a modal or drawer, or navigates to a record route.
- **Closing dialogs** — cancel, escape, backdrop, or successful submit all return the browser to the collection in a defined state.
- **Keyboard navigation** — tab order reaches toolbar, data region, and dialog controls; focus is visible and logical; escape closes overlays.
- **Selection** — individual checkboxes select rows; a header control selects all in the current view.
- **Bulk selection** — a selected set enables bulk actions; the count of selected records is visible.

### 5.3 Viewport and reload

- **Scrolling** — the data region scrolls independently of the toolbar; infinite-scroll variants append rather than replace.
- **Refreshing** — an in-page refresh control reloads the collection without a full browser navigation.
- **Cancel** — abandoning a create/edit sub-flow discards unsaved input and restores the collection.
- **Retry** — after a failed load or submit, a retry control re-attempts the operation.
- **Back navigation** — browser back returns to the prior route or collection state.
- **Forward navigation** — browser forward re-applies the forward history entry.
- **Browser reload** — a full reload re-renders the collection from the server; persisted UI scope (e.g. filters in the URL) is restored.

### 5.4 State and history

- **Deep links** — a record or filtered view may be addressable by URL; opening it directly renders the correct state.
- **URL synchronization** — search/filter/page/sort may be reflected in the URL query; the URL is the source of truth for restoring state on reload.
- **History behavior** — opening a record route pushes history; closing it returns via back. Modal-only opens may or may not push history; the page's contract defines which, and Hermes should record the actual behavior rather than assume.

---

## 6. Browser Assertions

What Hermes should automatically verify on a CRUD page. These are browser-level observations.

- **Page rendered** — the surface mounts without a blank screen or frozen shell.
- **Toolbar visible** — when the page defines a toolbar, its controls are present and reachable.
- **Data region rendered** — the table/cards paint with the expected structure (headers/columns or card layout).
- **Search functional** — entering text changes the result set; clearing it restores the broader set.
- **Filters update results** — applying a filter narrows the collection; removing it widens it.
- **Sorting works** — activating sort reorders rows in the indicated direction.
- **Pagination works** — page controls move between slices; the slice matches the page indicator.
- **Create succeeded** — after submit, the new record appears in the collection (after the appropriate cache/refresh), and the sub-flow closes.
- **Edit persisted** — after submit, the changed fields are reflected in the rendered record.
- **Delete removed record** — after confirmation, the record is absent from the collection and the count decrements.
- **Validation enforced** — invalid input renders inline errors and blocks submit; the browser observes the server's authoritative verdict, not only client-side checks.
- **Loading state** — a loading indicator shows during fetch and clears on completion.
- **Empty state** — a clear empty presentation appears when scope yields nothing, with no crash.
- **Error state** — a recoverable error presentation appears on failure, with a path to retry.
- **No broken UI** — no overlapping, collapsed, or unstyled regions; layout is coherent at the tested viewport.
- **No duplicated rows** — the same record does not appear twice within a page/slice.
- **No unexpected dialogs** — no modal/drawer is open without a triggering action.
- **No console errors** — the browser console shows no uncaught errors during the flow.
- **No JavaScript crashes** — the page remains interactive; no unhandled rejection leaves it frozen.
- **Browser responsive** — inputs, clicks, and navigation respond within expected bounds; no permanent spinner.
- **No visual corruption** — rendered content matches structure (no missing columns, no truncated controls, no layout shift that hides data).

---

## 7. Failure Scenarios

Generic browser failures Hermes should be able to detect on a CRUD page. These are interaction/rendering failures, not domain errors.

- **Search returns wrong results** — the result set does not match the query (stale client filter, wrong parameter, or un-cancelled prior request).
- **Pagination broken** — page controls do not change the slice, or pagination state is lost when filters change.
- **Duplicate rows** — the same record renders more than once in a slice.
- **Broken modal** — a create/edit dialog fails to open, fails to close, or traps focus with no escape.
- **Disabled button** — a create/submit/action control is unexpectedly disabled or enabled contrary to state.
- **Unexpected validation** — validation blocks a valid submission, or accepts an invalid one and submits anyway.
- **Loading forever** — a spinner never resolves because the request was never answered or its completion was not handled.
- **Lost state** — search/filter/page/sort reset unexpectedly (e.g. on a sub-flow close or minor re-render).
- **Browser freeze** — the page becomes unresponsive due to a long synchronous render or runaway loop.
- **Stale UI** — the collection shows pre-mutation data after a successful create/edit/delete.
- **Refresh inconsistency** — a full reload shows different data than the in-page view claimed, or loses intended scope.
- **Broken routing** — a record link or deep link lands on the wrong route, a blank page, or an error.
- **History problems** — back/forward navigates to an unexpected state, or a modal open/close desyncs with history.
- **Layout shift** — content jumps or reflows in a way that hides controls or misaligns columns.
- **Rendering issues** — missing headers, collapsed cells, overflow clipping, or unstyled regions.
- **Race conditions** — overlapping search/filter/page requests resolve out of order, painting a stale slice.
- **Network retry** — a failed request is not retried or is retried excessively, leaving the UI stuck.
- **Timeouts** — a request exceeds its bound and the UI has no defined recovery.
- **Unexpected redirects** — a create/edit/delete navigates away from the CRUD surface without user intent.

---

## 8. Browser Evidence Collection

What Hermes should collect when exercising a CRUD page. Browser evidence only — no backend debugging.

- **Screenshots** — the collection, each sub-flow state, empty/error/loading states, and any corruption.
- **Console logs** — all console output, especially errors, warnings, and unhandled rejections.
- **Network requests** — the list/search/filter/page/sort/create/edit/delete requests with their URLs, params, methods, statuses, and timings (observed from the browser's network surface, not from server internals).
- **DOM state** — the rendered structure of the data region, toolbar, dialogs, and state banners at the moment of interest.
- **Current URL** — the address, including query parameters reflecting scope, to prove deep-link and URL-sync behavior.
- **Storage state** — relevant browser storage (local/session) entries that influence the CRUD view (e.g. persisted scope or draft), read from the browser.
- **Visible notifications** — toasts, banners, and inline messages shown to the user.
- **Rendered table** — the actual rows/cells painted, for row-count and duplication checks.
- **Dialog state** — whether a modal/drawer is open, its title, its fields, and its action controls.
- **Loading indicators** — which spinner/skeleton was active and when it cleared.
- **Error messages** — the exact text and location of any surfaced error.
- **Timing** — durations for load, submit, and navigation, to detect timeouts and freezes.
- **Browser events** — focus changes, history pushes/pops, and visibility/responsiveness signals.

---

## 9. Browser Recovery Behavior

How Hermes should recover from the failures in Section 7, using browser-level actions only.

- **Retry action** — re-trigger the failed load or submit via the page's retry control.
- **Refresh page** — perform an in-page refresh or a full browser reload to clear a stuck state.
- **Close modal** — dismiss an open dialog via cancel/escape/backdrop to return to the collection.
- **Reopen page** — navigate away and back to the CRUD route to rebuild a clean session state.
- **Repeat search** — re-enter the query to confirm whether a wrong result was transient.
- **Navigate back** — use browser back to escape an unexpected redirect or broken sub-flow.
- **Navigate forward** — use browser forward to re-apply a state lost by an unintended back.
- **Clear filters** — reset search/filter/sort to return to the full, unfiltered collection.
- **Reset state** — clear relevant browser storage scope that is corrupting the view, then reload.
- **Dismiss notifications** — close toasts/banners that may obscure controls.
- **Recover after timeout** — after a request exceeds its bound, reload or retry and confirm the UI returns to a stable state.
- **Recover after failed request** — observe the error state, then retry or reload and verify the collection becomes consistent.
- **Recover after browser refresh** — after a reload, verify the intended scope (URL/query) is restored and the data matches.

---

## 10. Inheritance Contract

This is the most important section. It defines the inheritance model that makes CRUD a reusable layer.

### 10.1 The rule

**Every CRUD page automatically inherits the entire behavior defined in this document.** A future Browser Knowledge file does not re-describe search, pagination, filtering, create/edit/delete, validation posture, loading/empty/error states, assertions, failure scenarios, evidence collection, or recovery. Those are inherited by referencing the CRUD archetype by name.

### 10.2 What a page-specific Browser Knowledge file must declare

A page-specific file supplies **only the delta** — what makes this one collection different from every other. It declares:

- **Route** — the list route (and any record sub-routes) for this page.
- **Entity name** — the human and system name of the record type this collection manages.
- **Unique fields** — the fields that appear in this page's create/edit forms and list columns, beyond the generic contract.
- **Unique business rules** — any rule that governs *this* collection's behavior in the browser (e.g. a field that is conditionally required, a constraint surfaced by the server).
- **Special validations** — validations specific to this page's forms that the generic contract does not cover.
- **Page-specific exceptions** — any deviation from this archetype (e.g. this page uses cards instead of a table, or opens records only via route and never via modal).

### 10.3 What a page-specific file must NOT declare

Everything behavioral already in this document: the search/filter/sort/pagination contracts, the create/edit/delete lifecycle, the modal/drawer close behavior, the loading/empty/error states, the assertion set, the failure catalog, the evidence set, and the recovery set. Re-declaring these in a page file is a violation of the inheritance model and causes drift.

### 10.4 Inheritance model summary

```
Page-specific Browser Knowledge file
   └─ declares: route, entity name, unique fields,
                unique business rules, special validations,
                page-specific exceptions
   └─ inherits (by archetype reference): ALL of CRUD_ARCHETYPE.md
                search · filter · sort · paginate · create
                edit · delete · validation posture · states
                assertions · failures · evidence · recovery
```

The class of change *"the list now supports column pinning"* is made once, in this archetype, and every CRUD page gains it. The class of change *"this collection adds a required field"* is made only in that page's delta.

---

## 11. Scope Boundaries

This document defines **browser interaction behavior**. It explicitly does NOT define the following; those belong to page-specific Browser Knowledge files (or to layers beneath them):

- **Business rules** — domain logic that decides what a valid record is; this document only expects that such rules are *observable* as validation in the browser.
- **Permissions** — what a given user may see or do; the archetype expects gated controls to be disabled/hidden but does not define who is gated.
- **Financial rules** — any monetary, balancing, or settlement constraints; out of scope for a generic CRUD contract.
- **Ledger or posting rules** — any ledger, posting, or reconciliation constraints; out of scope.
- **Stock or quantity rules** — any stock or quantity constraints; out of scope.
- **Workflow rules** — any state-machine or approval transitions; those are the Workflow archetype's concern, not CRUD's.
- **Approval rules** — who may approve what, and when; out of scope.
- **API implementation** — how requests are formed, routed, or authenticated on the server; this document references only the browser-observable request (URL, params, status) as evidence.
- **Database behavior** — how records are stored or migrated; this document references only the browser-visible effect (a row appears, changes, or disappears).
- **Domain validation** — the specific content rules for a field; the archetype expects server-authoritative validation to be *surfaced*, but the rules themselves are page-specific.

If a page's dominant interaction is a stateful lifecycle (approvals, transitions, side effects), it is a Workflow page that may *inherit CRUD* as a secondary layer — but the lifecycle rules themselves are not defined here.

---

## 12. Stability

This document is intended to remain stable for years and to be extended rarely.

- **Defined by interaction shape, not domain.** CRUD is expressed entirely in browser behavior (list, search, filter, paginate, create, edit, delete, validate). Domains come and go; the interaction contract does not. A future collection — whatever it manages — will still be a searchable, paginated, filterable list with create/edit/delete.
- **New CRUD pages inherit without modification.** When a new collection surface is added to the application, its Browser Knowledge file references this archetype and supplies only its delta. This document is not edited for the new page.
- **Only new browser patterns justify change.** This archetype is updated only when a genuinely new CRUD interaction pattern appears that the current contract does not cover (for example, a new universal list primitive, a new standard dialog lifecycle, or a new standard failure class). Incremental UI polish, new fields, and new collections do NOT justify editing this document.
- **Drift protection.** Because behavioral knowledge lives here and not in page files, a fix or clarification to the generic CRUD contract propagates to every page at once. The stable center is this document; the churn happens in the page-specific deltas beneath it.

*End of CRUD Archetype. Canonical inheritance layer for Hermes Browser Testing.*
