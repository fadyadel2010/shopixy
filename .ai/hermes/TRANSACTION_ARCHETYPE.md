# Transaction Archetype — Hermes Browser Testing Canonical Contract

**Document Type:** Canonical archetype — inheritance layer for Browser Knowledge
**Status:** Canonical — stable for years
**Layer:** One of the core inheritance layers beneath future `*KNOWLEDGE.yaml` files. This document expands ONLY the Transaction archetype first identified in `PAGE_ARCHETYPE_DISCOVERY.md`. It does not redefine that discovery, nor does it redefine the CRUD archetype. Where the two overlap conceptually (validation posture, loading/error states), Transaction inherits the *behavioral* expectations and extends them with the working-session lifecycle that is unique to transactional pages.
**Companion Artifact:** The Browser Knowledge generation skill consumes this archetype by name and layers page-specific deltas on top.
**Authority Note:** This document defines *browser interaction behavior only*. It is deliberately independent of any business domain. No entity, field, rule, endpoint, or persistence mechanism is asserted here. A future Browser Knowledge file supplies those; everything behavioral is inherited from this document. Where this document states that validation is server-authoritative, or that a committed result is server-derived, it states a browser-evidence expectation (the browser must observe the server's verdict and the resulting committed state), not an implementation detail. No monetary, quantity, stock, ledger, or posting semantics are implied anywhere in this document.

---

## 1. Purpose

### 1.1 Why Transaction exists

A large class of application surfaces do not manage persistent independent records the way a list does. Instead they assemble a **temporary working session** — a set of entries the user builds interactively — and then commit that session as a single, indivisible result. The working session has no independent existence until it is committed: it is not a row in a collection, it is not searchable, and it does not persist unless the commit succeeds.

The interaction contract for such a surface is therefore different from CRUD in kind, not in degree. CRUD is *read-first, act-second* over a durable collection. Transaction is *build-first, commit-second* over a disposable working state that eventually produces a committed result.

### 1.2 Why it differs from CRUD

- **Subject of manipulation.** CRUD pages manipulate independent records that already exist in a collection. Transaction pages manipulate a working session that exists only within the current browser context until commit.
- **Default state.** A CRUD page defaults to a populated (or empty) *view* of a collection. A Transaction page defaults to an empty (or recalled) *workspace* awaiting assembly.
- **Atomicity of the result.** In CRUD, each create/edit/delete is a small independent mutation. In Transaction, the whole working session is committed together; the meaningful outcome is the single committed result, not the individual entries.
- **Point of no automatic return.** CRUD mutations are individually reversible-ish (edit again, delete). A Transaction commit is the irreversible-ish boundary: after it, the working session is consumed and a committed result exists. The browser must treat commit as the defining, guarded step.
- **Side-effect observation.** A successful Transaction commit must be observable as a durable committed result and as the clearing (or defined reset) of the working session. CRUD observes row-level add/change/remove instead.

### 1.3 Why Hermes should inherit Transaction behavior instead of learning every page individually

- **Transfer.** A new transactional surface shipped next quarter reuses the same contract. Hermes recognizes it on first encounter from the inherited layer instead of requiring a new knowledge file.
- **Single point of maintenance.** A change to the generic working-session/summary/confirm/commit/idempotency contract is made once, here, and benefits every Transaction page at once.
- **Reasoning consistency.** Generic QA expectations (server-authoritative validation, commit idempotency, no duplicate committed result, fail-closed on ambiguous state, working-state persistence correctness) are applied uniformly because they live in one place.
- **Reduced knowledge weight.** A page-specific file declares only its deltas (route, input structure, validation rules, confirmation behavior, exceptions). The bulk of behavioral knowledge is inherited, not repeated.

---

## 2. Browser Identity

### 2.1 Typical characteristics

A Transaction page exists to assemble a working session, review it, validate it, and commit it as a single result. The working session is temporary and exists only within the session (or a versioned browser-storage draft) until commit. There is no persistent collection of "in-progress" sessions to browse; there is the live workspace and the committed result.

### 2.2 Typical interaction model

The page is **build-first, commit-second**. The user lands in an empty (or recalled) workspace, adds entries to the working set, reviews a live summary, validates, confirms, and commits. The page is write-biased: its resting state is an assembling workspace, not a read view. The commit is guarded by confirmation because it produces a result that is not trivially undone by the page itself.

### 2.3 Typical browser layout

- An application shell (sidebar/nav) is assumed present; Transaction is the content surface inside it.
- A **header** identifying the transaction type and overall state.
- A **toolbar** for workspace-level actions (clear, hold/save draft, recall, undo/redo where supported).
- A **working area** — the surface where entries are added and edited. The definitional element of the archetype.
- A **summary panel** — a live aggregate of the working set (counts, totals in whatever units the page uses). Always visible alongside the working area.
- An optional **side panel** for selection/lookup helpers that feed entries into the working area.
- An optional **detail list** / line view enumerating the current entries.
- **Input controls** for each entry's fields.
- A **review section** that reflects the assembled working set before confirm.
- A **validation message surface** (inline per field and/or a summary-level banner).
- A **confirmation dialog** shown before commit.
- A **success screen** shown after a committed result.
- **Loading**, **empty**, and **error** states for the relevant regions.

### 2.4 Typical navigation

- Reached via a direct route entry (a dedicated transaction route), not by drilling from a list.
- The working session may persist across reload via versioned browser storage (TTL-capped, size-capped). On reload the page either restores the draft or presents a clean workspace, depending on the page contract.
- The committed result typically leads to a success screen, from which the user starts a new session or navigates to a surface that displays the result.

### 2.5 Typical user expectations

- The workspace is stable: entries stay where the user put them; adding, editing, and removing behave predictably.
- The summary panel always reflects the current working set; it is the user's trust anchor for "what will be committed".
- Validation is clear and blocks commit rather than silently discarding input.
- A confirmation step precedes the irreversible commit, showing what is about to be committed.
- A success screen proves the commit happened and shows the committed result.
- The session survives an accidental refresh (draft recall) or, where the page chooses not to persist, clearly resets to a clean workspace.
- Double-clicking or retrying commit does not produce two committed results.

---

## 3. Expected Page Structure

A Transaction page is composed of a set of UI components. Not every page contains every component. The contract distinguishes **required** from **optional**.

### 3.1 Required components

- **Workspace** — the surface where the working session is assembled (entries added, edited, removed). This is the definitional element of the archetype. If there is no buildable working state, the page is not Transaction.
- **Summary panel** — a live aggregate of the working set. It is the browser-side source of truth for "what will be committed". A Transaction page without a continuously-updated summary is incomplete.
- **Commit control** — the affordance that submits the working session and produces the committed result. Its availability is driven by the working set's validity and non-emptiness.
- **Validation message surface** — where invalid or missing input is surfaced to block commit.
- **Loading state** — an explicit indicator while the working session is being initialized, recalled, or committed.
- **Error state** — an explicit, recoverable presentation when a commit or recall fails.

### 3.2 Optional components

- **Header** — transaction-type title and high-level state (e.g. "draft", "committing").
- **Toolbar** — workspace-level actions: clear all, hold/save draft, recall draft, undo, redo.
- **Working-area sub-sections** — grouped regions within the workspace for distinct parts of the entry set.
- **Side panel** — a lookup/selection helper that feeds entries into the working area.
- **Detail list** — an enumerated, often editable, view of current entries.
- **Input controls** — per-entry fields; may be inline in the detail list or in a side editor.
- **Review section** — a consolidated read-back of the working set shown before confirmation.
- **Confirmation dialog** — modal shown before commit, summarizing the pending result.
- **Success screen** — post-commit confirmation showing the committed result and next actions.
- **Empty state** — an explicit presentation of a workspace with zero entries (distinct from error).
- **Hold / save-draft and recall** — persistence of the working session to browser storage and its later restoration.
- **Undo / redo** — stepwise reversal / re-application of working-session mutations.
- **Keyboard hint affordances** — visible hints for shortcuts (add, commit, clear).

### 3.3 Component contracts (browser-observable only)

- The **workspace** accepts additions, edits, and removals of working entries; every such mutation updates the **summary panel** and (where present) the **detail list** consistently.
- The **summary panel** reflects the working set at all times. It is the browser's source of truth for the pending commit; Hermes MUST be able to assert that the summary equals the working set.
- The **commit control** is **disabled** while the working set is empty or invalid, and **enabled** only when the working set is non-empty and valid.
- The **confirmation dialog** traps focus, presents a summary of the pending commit, and returns the browser to the workspace on cancel — without having committed anything.
- The **success screen** confirms the committed result and offers a path to begin a new working session; it does not re-show the committed working set as an editable workspace.
- **Loading / empty / error** are mutually exclusive visible states for the relevant region at any moment. A committing state is a form of loading for the workspace and commit control.

---

## 4. Browser Navigation Contract

This contract describes the canonical lifecycle a browser session takes through a Transaction surface. It is descriptive of expected behavior, not a script.

```
Landing
   ↓
Build working state (assemble entries into the workspace)
   ↓
Modify working state (add / edit / remove / clear entries; undo / redo)
   ↓
Review (inspect the working set and its summary)
   ↓
Validate (the browser observes validation verdicts; invalid state blocks commit)
   ↓
Confirm (a confirmation dialog summarizes the pending commit)
   ↓
Commit (submit the working session; produce the committed result)
   ↓
Observe result (success screen / committed-result evidence)
   ↓
Refresh (browser reload or in-page refresh)
   ↓
Verify persistence (the committed result is observable after refresh;
                     the workspace is in its defined post-commit state)
```

### 4.1 Notes on the navigation path

- **Build / Modify / Review / Validate** may iterate repeatedly before Confirm. The path above is the *complete* set, not a forced single pass.
- **Confirm** may be rejected (cancel) and re-entered any number of times before Commit; cancellation never commits.
- **Commit** is the boundary of no automatic return within the page: after it, the working session is consumed and a committed result exists.
- **Refresh / Verify persistence** is the closure that proves the commit was durable, not merely optimistic. After refresh, Hermes MUST observe that the committed result is still present (in whatever surface displays it) and that the workspace is in a defined state — either a clean workspace for a new session, or a recalled draft if the page persists drafts and the commit had not yet occurred.

---

## 5. Browser Interaction Contract

The expected interaction model for every Transaction page. These are behaviors Hermes should be able to exercise and observe.

### 5.1 Building the working state

- **Adding information** — an action appends a new entry to the working set; the summary and detail list update.
- **Removing information** — an action removes an entry from the working set; the summary and detail list update, and totals/aggregation decrease accordingly.
- **Changing values** — editing a field on an entry recomputes dependent summary values in the browser.
- **Editing entries** — an entry may be opened for editing inline, in a side editor, or in a dialog; saving returns the browser to the workspace with the updated entry.
- **Clearing entries** — a clear action empties the working set; the summary resets to its empty representation and the workspace shows its empty state (without committing).

### 5.2 Working-state control

- **Undo** — reverses the most recent working-session mutation; the summary and detail list reflect the reversal.
- **Redo** — re-applies a mutation undone by Undo.
- **Holding / saving a draft** — persists the working session to browser storage (versioned, TTL- and size-capped per the page contract).
- **Recalling a draft** — restores a previously held working session from browser storage on a later visit.
- **Expanding sections** — collapses/expands grouped regions of the workspace without losing entries.
- **Collapsing sections** — same; state is visual only and must not discard working-set data.

### 5.3 Review and validation

- **Reviewing** — a review section (or the summary panel + detail list) reflects the assembled working set; Hermes MUST be able to reconcile it against the workspace.
- **Validation feedback** — invalid or missing input is surfaced inline on the relevant field and/or at summary level; the commit control becomes (or remains) disabled.
- **Validation posture** — the browser observes validation as the authoritative verdict on whether commit is permitted. Client-side checks are a convenience; the server's verdict is what ultimately gates the committed result. Hermes MUST NOT assume a client-only check is sufficient.

### 5.4 Confirmation and commit

- **Confirmation** — triggering commit first presents a confirmation dialog summarizing the pending result; the actual commit does not occur until the dialog is accepted.
- **Cancellation** — dismissing the confirmation dialog (cancel, escape, backdrop) returns the browser to the workspace with the working set intact and nothing committed.
- **Commit** — accepting confirmation submits the working session and produces the committed result; a loading state covers the workspace/commit control until resolution.
- **Idempotent commit** — a committed result is produced exactly once per intentional commit. Double-clicking the commit control, pressing it twice, or retrying after a transient network error MUST NOT create two committed results. Hermes MUST observe and assert idempotency behavior.
- **Failure rollback** — if commit fails, the working session is preserved (not silently consumed) so the user can correct and retry, unless the page contract defines a different failure posture.

### 5.5 Viewport, reload, and history

- **Scrolling** — the working area and summary scroll independently of fixed chrome where the layout divides them.
- **Keyboard navigation** — tab order reaches toolbar, workspace inputs, summary, and commit/confirm controls; focus is visible and logical; escape closes overlays and returns to the workspace.
- **Focus management** — after adding an entry, focus lands on a sensible next input; after commit, focus moves to the success screen's primary action.
- **Browser refresh** — a full reload either restores a held draft or presents a clean workspace (per page contract); a committed session does not re-appear as an editable workspace.
- **Browser history** — commit may or may not push a history entry; the page's contract defines which, and Hermes should record the actual behavior rather than assume.
- **Deep links** — a transaction route is typically entered directly; deep-linking into a specific in-progress session is page-defined and, where supported, must render the correct state.
- **URL synchronization** — some transaction state (e.g. a recalled draft id) may be reflected in the URL; the URL, where synced, is the source of truth for restoring that state on reload.
- **Session restoration** — on reload, the working state is restored from browser storage (draft recall) or reset to a clean workspace; Hermes MUST assert the post-reload state is consistent and not silently corrupted.

---

## 6. Browser Assertions

What Hermes should automatically verify on a Transaction page. These are browser-level observations.

- **Workspace rendered** — the surface mounts with a usable workspace, not a blank screen or frozen shell.
- **Summary updated** — after every add/edit/remove/clear, the summary panel reflects the current working set.
- **Review reflects changes** — the review section (or detail list) matches the workspace entries.
- **Validation visible** — invalid or missing required input is surfaced and visible.
- **Commit available** — the commit control is enabled when the working set is non-empty and valid.
- **Commit disabled when invalid** — the commit control is disabled when the working set is empty or invalid; it does not commit on click while disabled.
- **Confirmation shown** — triggering commit presents a confirmation dialog summarizing the pending result.
- **Success state visible** — after a successful commit, a success screen or committed-result evidence is shown.
- **Failure state visible** — on commit failure, an error state is shown and the working session is preserved for retry.
- **Loading state** — a loading indicator shows during init, recall, or commit and clears on completion.
- **Error state** — a recoverable error presentation appears on failure, with a path to retry.
- **No duplicated actions** — a single intentional commit produces exactly one committed result; double-click/retry does not create duplicates.
- **No frozen interface** — the page remains interactive throughout; no permanent spinner after resolution.
- **No unexpected navigation** — commit/confirm/cancel do not navigate away from the transaction surface contrary to the page contract.
- **No JavaScript errors** — the browser console shows no uncaught errors during the flow.
- **No console errors** — no error-level console output unhandled by the page.
- **Browser responsive** — inputs, clicks, and navigation respond within expected bounds.
- **Visual consistency** — the workspace, summary, and controls are coherent at the tested viewport; no overlapping, collapsed, or unstyled regions.
- **State consistency** — the summary equals the working set at all observed moments; no drift between what is shown and what would be committed.

---

## 7. Failure Scenarios

Generic browser failures Hermes should be able to detect on a Transaction page. These are interaction/rendering failures, not domain errors.

- **Lost working state** — the in-progress working session disappears without commit (e.g. on a minor re-render, route blip, or storage cap breach).
- **Broken confirmation dialog** — the confirm dialog fails to open, fails to close, or traps focus with no escape.
- **Summary not updated** — the summary panel does not reflect the current working set after a mutation.
- **Validation missing** — invalid input is not surfaced, or the commit control stays enabled despite invalid state.
- **Commit never enabled** — the commit control remains disabled even when the working set is non-empty and valid.
- **Commit enabled too early** — the commit control is enabled while the working set is invalid or empty, allowing an invalid commit attempt.
- **Duplicate submission** — double-click, rapid re-press, or retry creates two committed results from one intentional commit.
- **Double click** — two commits fired from a single user action due to missing guard/disabled-state.
- **Browser freeze** — the page becomes unresponsive during build or commit (long synchronous render, runaway loop).
- **Unexpected redirect** — commit/confirm/cancel navigates away from the transaction surface without user intent.
- **Stale UI** — the workspace or summary shows pre-mutation data after a successful commit or edit.
- **Broken navigation** — a route link or deep link lands on the wrong route, a blank page, or an error.
- **Refresh inconsistency** — a full reload shows a different working state than the in-page view claimed, or loses a held draft unexpectedly.
- **History inconsistency** — back/forward navigates to an unexpected state, or confirm open/close desyncs with history.
- **Timeout** — a commit or recall request exceeds its bound and the UI has no defined recovery.
- **Retry failure** — a failed commit is not retried correctly, or retrying produces a duplicate result.
- **Layout corruption** — workspace, summary, or dialog render misaligned, overflow, or clip controls.
- **Rendering issues** — missing headers, collapsed cells, unstyled regions, or truncated controls.
- **Focus issues** — focus lost, trapped, or mis-directed after add/edit/commit/cancel.
- **Race conditions** — overlapping add/edit/remove or commit requests resolve out of order, painting an inconsistent working set or summary.

---

## 8. Browser Evidence Collection

What Hermes should collect when exercising a Transaction page. Browser evidence only — no backend debugging.

- **Screenshots** — the workspace (empty and populated), review/confirm states, success and error screens, and any corruption.
- **Console logs** — all console output, especially errors, warnings, and unhandled rejections during build, validate, confirm, and commit.
- **Network requests** — the commit (and recall) requests with their URLs, methods, params, statuses, headers of interest (e.g. an idempotency/attempt identifier if the page sends one), and timings — observed from the browser's network surface, not from server internals.
- **DOM state** — the rendered structure of the workspace, summary, detail list, dialogs, and banners at the moment of interest.
- **Current URL** — the address, including any query/state parameters reflecting draft or session, to prove deep-link and URL-sync behavior.
- **Browser storage** — relevant local/session storage entries holding the held draft (version, TTL, size), read from the browser, to verify persistence and recall correctness.
- **Visible notifications** — toasts, banners, and inline messages shown to the user (success, error, validation).
- **Validation messages** — the exact text and location of any surfaced validation.
- **Summary state** — the rendered aggregate values at commit time, for reconciliation against the working set.
- **Workspace state** — the actual entries present, for count, value, and duplication checks.
- **Confirmation dialog** — whether it is open, its title, its summarized pending result, and its action controls.
- **Loading indicators** — which spinner/skeleton was active and when it cleared.
- **Timing** — durations for build, validate, commit, and navigation, to detect timeouts and freezes.
- **Browser events** — focus changes, history pushes/pops, storage writes, and visibility/responsiveness signals.

---

## 9. Browser Recovery Behavior

How Hermes should recover from the failures in Section 7, using browser-level actions only.

- **Retry action** — re-trigger the failed commit or recall via the page's retry control, and assert the result is produced exactly once (idempotency preserved).
- **Refresh page** — perform an in-page refresh or a full browser reload to clear a stuck state; then assert the post-reload working state is consistent (draft recalled or clean workspace).
- **Rebuild working state** — if the working session was lost, re-add the entries through the normal build interactions and verify the summary recovers.
- **Dismiss dialog** — close a stuck confirmation dialog via cancel/escape/backdrop to return to the workspace intact.
- **Reopen page** — navigate away and back to the transaction route to rebuild a clean session state.
- **Navigate back** — use browser back to escape an unexpected redirect or broken confirm flow.
- **Navigate forward** — use browser forward to re-apply a state lost by an unintended back.
- **Repeat action** — re-perform an add/edit/remove to confirm whether an inconsistency was transient.
- **Recover after timeout** — after a commit/recall exceeds its bound, reload or retry and confirm the UI returns to a stable state and the result is not duplicated.
- **Recover after failed request** — observe the error state, then retry or reload and verify the working session is preserved and the committed result is eventually consistent.
- **Recover after browser reload** — after a reload, verify the intended working state (draft recalled or clean workspace) is restored and the summary matches the working set.
- **Reset working state** — clear the working session (via the page's clear control) or clear corrupting browser-storage draft, then reload to a known-good baseline.
- **Restore browser session** — where the page persists drafts, verify a held draft restores correctly after a full browser restart, within its TTL and size limits.

---

## 10. Inheritance Contract

This is the most important section. It defines the inheritance model that makes Transaction a reusable layer.

### 10.1 The rule

**Every Transaction page automatically inherits the entire behavior defined in this document.** A future Browser Knowledge file does not re-describe the working-session lifecycle, summary behavior, validation posture, confirmation dialog, commit/idempotency contract, loading/empty/error states, assertions, failure scenarios, evidence collection, or recovery. Those are inherited by referencing the Transaction archetype by name.

### 10.2 What a page-specific Browser Knowledge file must declare

A page-specific file supplies **only the delta** — what makes this one transactional surface different from every other. It declares:

- **Route** — the direct entry route for this transaction page.
- **Unique input structure** — the specific fields and entry types that appear in *this* workspace and detail list, beyond the generic contract.
- **Unique validation rules** — validations specific to this page's working set that the generic contract does not cover.
- **Unique business rules** — any rule that governs *this* page's behavior in the browser (e.g. a field conditionally required, a constraint surfaced by the server).
- **Unique confirmation behavior** — how *this* page's confirmation dialog summarizes the pending commit, and any page-specific confirm steps.
- **Unique page exceptions** — any deviation from this archetype (e.g. this page does not persist drafts, or commits without a confirmation dialog, or structures its workspace differently).

### 10.3 What a page-specific file MUST NOT declare

**Hermes MUST NOT duplicate inherited behavior inside page-specific Browser Knowledge.** Everything behavioral already in this document — the build/modify/review/validate/confirm/commit lifecycle, the summary-panel contract, the validation posture, the confirmation-dialog close behavior, the commit idempotency expectation, the loading/empty/error states, the assertion set, the failure catalog, the evidence set, and the recovery set — is inherited by reference. Re-declaring these in a page file is a violation of the inheritance model and causes drift. A page file references the Transaction archetype by name and adds only its delta.

### 10.4 Inheritance model summary

```
Page-specific Browser Knowledge file
   └─ declares: route, unique input structure,
                unique validation rules,
                unique business rules,
                unique confirmation behavior,
                unique page exceptions
   └─ inherits (by archetype reference): ALL of TRANSACTION_ARCHETYPE.md
                build · modify · review · validate
                confirm · commit (idempotent) · summary contract
                validation posture · states (loading/empty/error/success)
                assertions · failures · evidence · recovery
```

The class of change *"the confirmation dialog now shows a line-by-line summary"* is made once, in this archetype, and every Transaction page gains it. The class of change *"this page adds a conditionally-required field"* is made only in that page's delta.

---

## 11. Scope Boundaries

This document defines **browser interaction behavior**. It explicitly does NOT define the following; those belong to page-specific Browser Knowledge files (or to layers beneath them):

- **Business rules** — domain logic that decides what a valid working set is; this document only expects that such rules are *observable* as validation in the browser.
- **Permissions** — what a given user may see or do; the archetype expects gated controls to be disabled/hidden but does not define who is gated.
- **Approval workflows** — any multi-stage approval or state-machine transition on the committed result; those are the Workflow archetype's concern, not Transaction's.
- **Financial calculations** — any monetary, balancing, or settlement math; out of scope for a generic Transaction contract.
- **Inventory logic** — any stock or quantity constraints; out of scope.
- **Database behavior** — how a committed result is stored or migrated; this document references only the browser-visible effect (a result appears, the workspace clears).
- **API implementation** — how requests are formed, routed, or authenticated on the server; this document references only the browser-observable request (URL, params, status) as evidence.
- **Server processing** — what the server does with the committed working session; this document observes only the resulting browser state.
- **Domain validation** — the specific content rules for a field; the archetype expects server-authoritative validation to be *surfaced*, but the rules themselves are page-specific.
- **Entity definitions** — the shape and meaning of the entries and the committed result; those are declared per page.

If a page's dominant interaction is a stateful lifecycle *after* commit (approvals, transitions, side effects on the result), it is a Workflow page that may *inherit Transaction* as a secondary layer — but the lifecycle rules themselves are not defined here.

---

## 12. Stability

This document is intended to remain stable for years and to be extended rarely.

- **Defined by interaction shape, not domain.** Transaction is expressed entirely in browser behavior (build a working session, review it, validate, confirm, commit, observe the result, recover). Domains come and go; the interaction contract does not. A future transactional surface — whatever it assembles — will still build a working set, summarize it, validate it, confirm it, and commit it as a single result.
- **New transaction pages inherit without modification.** When a new transactional surface is added to the application, its Browser Knowledge file references this archetype and supplies only its delta. This document is not edited for the new page.
- **Only new browser patterns justify change.** This archetype is updated only when a genuinely new transactional interaction pattern appears that the current contract does not cover (for example, a new universal working-session primitive, a new standard confirmation lifecycle, or a new standard failure class such as a new idempotency mechanism). Incremental UI polish, new fields, and new transaction pages do NOT justify editing this document.
- **Drift protection.** Because behavioral knowledge lives here and not in page files, a fix or clarification to the generic Transaction contract propagates to every page at once. The stable center is this document; the churn happens in the page-specific deltas beneath it.

*End of Transaction Archetype. Canonical inheritance layer for Hermes Browser Testing.*

---

# Browser Execution Checklist

□ Workspace rendered
□ Working state created
□ Working state modified (add / edit / remove)
□ Summary updated
□ Review reflects changes
□ Validation verified
□ Confirmation verified
□ Commit verified (enabled only when valid + non-empty)
□ Idempotency verified (no duplicate committed result)
□ Success verified
□ Failure verified
□ Loading verified
□ Error verified
□ Empty state verified
□ Refresh verified
□ Browser history verified
□ Session restoration verified
□ Console verified
□ Network verified
□ Evidence collected
□ Recovery verified
