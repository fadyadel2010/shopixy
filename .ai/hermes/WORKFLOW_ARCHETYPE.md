# Workflow Archetype — Hermes Browser Testing Canonical Contract

**Document Type:** Canonical archetype — inheritance layer for Browser Knowledge
**Status:** Canonical — stable for years
**Layer:** One of the core inheritance layers beneath future `*KNOWLEDGE.yaml` files. This document expands ONLY the Workflow archetype first identified in `PAGE_ARCHETYPE_DISCOVERY.md`. It does not redefine that discovery, nor does it redefine the CRUD or Transaction archetypes. Where the three overlap conceptually (loading/error states, confirmation posture, validation surfacing, history/refresh behavior), Workflow inherits the *behavioral* expectations and extends them with the lifecycle-observation-and-transition model that is unique to workflow pages.
**Companion Artifact:** The Browser Knowledge generation skill consumes this archetype by name and layers page-specific deltas on top.
**Authority Note:** This document defines *browser interaction behavior only*. It is deliberately independent of any business domain. No entity, field, rule, endpoint, or persistence mechanism is asserted here. A future Browser Knowledge file supplies those; everything behavioral is inherited from this document. Where this document states that the current state is authoritative, or that a transition is validated, it states a browser-evidence expectation (the browser must observe the item's verdict and the resulting state), not an implementation detail. No monetary, quantity, stock, ledger, approval, or domain semantics are implied anywhere in this document.

---

## 1. Purpose

### 1.1 Why Workflow exists

A large class of application surfaces are neither a *collection of independent records* (CRUD) nor a *disposable working session that is built and committed* (Transaction). They are the browser-facing observation and control surface for an item that **already exists** and whose nature is defined by the lifecycle state it currently holds and the states it can move to.

The item is not created here. It is not assembled here. It is not owned by this page. The page's reason to exist is to let the browser **observe** the item's current lifecycle position and, where permitted, **execute a transition** that moves it to a new state.

The interaction contract for such a surface is therefore different in kind from both CRUD and Transaction. CRUD is *read-first, act-second* over a durable collection of independent records. Transaction is *build-first, commit-second* over a disposable working state that eventually produces a committed result. Workflow is *observe-first, transition-second* over an existing item whose identity and lifecycle pre-date the page.

### 1.2 Why it differs from CRUD

- **Subject of observation.** CRUD pages render a collection the browser can search, page, and mutate. Workflow pages render a single existing item whose collection membership is incidental; the page is about *this one item's state*, not the list it came from.
- **Mutation shape.** In CRUD, the browser mutates a record's fields (create/edit/delete). In Workflow, the browser does not edit the item's intrinsic content so much as drive it through a lifecycle — the meaningful browser act is the **transition**, not a field patch.
- **Source of structure.** CRUD structure is a table/cards of uniform records. Workflow structure is a status indicator, a timeline/history of states, and a set of transition controls valid from the current state. The page is organized around *state*, not *rows*.
- **State authority.** CRUD observes row-level add/change/remove. Workflow observes a current lifecycle state plus the recorded history that explains how the item arrived there; the two (current status and latest history entry) MUST be mutually consistent.

### 1.3 Why it differs from Transaction

- **Who builds the object.** Transaction builds a temporary working session from nothing and commits it. Workflow observes an item that already exists; the browser never builds it.
- **Default state.** A Transaction page defaults to an empty (or recalled) *workspace* awaiting assembly. A Workflow page defaults to a *view of an existing item's current state* — status, history, and details already populated.
- **The defining act.** In Transaction the defining act is commit (the working session is consumed and a result appears). In Workflow the defining act is the **transition** (the item moves from one lifecycle state to another, recorded in its history).
- **Side-effect observation.** Transaction observes the committed result and the clearing of the workspace. Workflow observes the *new current state*, the *appended history entry*, and the *updated timeline*, all of which persist on the already-existing item.
- **Object ownership.** The browser in Transaction owns the working session until commit. The browser in Workflow never owns the item; it only observes and requests transitions against an object owned elsewhere.

### 1.4 Why Hermes should inherit Workflow behavior instead of learning every page individually

- **Transfer.** A new lifecycle surface shipped next quarter reuses the same contract. Hermes recognizes it on first encounter from the inherited layer instead of requiring a new knowledge file.
- **Single point of maintenance.** A change to the generic status/timeline/history/transition/confirmation contract is made once, here, and benefits every Workflow page at once.
- **Reasoning consistency.** Generic QA expectations (current state is authoritative, only valid transitions are presented, confirmation precedes a guarded transition, history and status stay consistent, fail-closed on ambiguous state) are applied uniformly because they live in one place.
- **Reduced knowledge weight.** A page-specific file declares only its delta (route, lifecycle states, allowed transitions, unique validations, exceptions). The bulk of behavioral knowledge is inherited, not repeated.

---

## 2. Browser Identity

### 2.1 Typical characteristics

A Workflow page exists to let the browser observe an existing item's lifecycle and, where the item's current state permits, execute a transition that moves it forward (or, where supported, laterally or backward). The item is a pre-existing entity addressed by an identifier (in the route or in the navigation that opened the page). The page does not create the item and is not the system of record for its lifecycle; it is the observation and control surface.

The page is read-biased in its resting state (it shows current status, history, and details) and becomes action-oriented only when the user invokes a transition control that the current state allows.

### 2.2 Typical interaction model

The page is **observe-first, transition-second**. The user lands on an existing item, reads its current status, inspects its history/timeline and details, and — only if the current state exposes a valid transition — executes that transition (typically through a confirmation), then observes the resulting new state. Creating or editing the item's intrinsic content is not the page's purpose; moving it through its lifecycle is.

The transition is the guarded act. Because it changes the item's lifecycle position (often irreversibly within the page), it is confirmed and is the focal point of the page's validation and assertion posture — in the same way commit is focal for Transaction, but operating on an existing object rather than a built working session.

### 2.3 Typical browser layout

- An application shell (sidebar/nav) is assumed present; Workflow is the content surface inside it.
- A **header** establishing the item's identity (an identifier and/or human label) and its current lifecycle state.
- A **status indicator** — the definitive rendering of the item's current state (badge, pill, banner). The definitional element of the archetype.
- A **timeline** — the progression of lifecycle states the item has moved through, in order.
- A **history / activity log** — the chronological record of events (state entries, notes, attachments) associated with the item.
- A **summary panel** — key facts about the item (optional but common).
- A **details panel** — the item's fuller content/attributes.
- A **metadata panel** — lifecycle bookkeeping (when the state last changed, by whom, identifiers).
- An **action bar** — the container for the item's transition controls.
- **Transition controls** — the affordances that move the item to another state; present only for transitions valid from the current state.
- A **confirmation dialog** — shown before a guarded transition executes.
- A **comments / notes thread** (optional) — collaboration or annotation attached to the item.
- An **attachments** region (optional) — files associated with the item or its lifecycle.
- A **notifications** surface (optional) — toasts/banners about transition outcomes.
- A **validation message surface** — where a transition is blocked by a condition the browser can observe.
- **Loading**, **empty**, and **error** states for the relevant regions.

### 2.4 Typical navigation

- Reached by opening an existing item: from a list/dashboard (a row or tile click) or by a deep link that addresses the item by identifier.
- The item identifier is part of the route or of the navigation payload that opened the page; the page loads the item's current state and history on entry.
- A transition typically keeps the browser on the same item view, updating status/timeline/history in place (or via a refresh), rather than navigating away.

### 2.5 Typical user expectations

- The page loads and immediately shows the item's current lifecycle state — not a blank or ambiguous screen.
- The status indicator, timeline, and history are mutually consistent: the latest history entry agrees with the current status.
- Only transitions valid from the current state are offered; invalid transitions are not presented as actionable (or, where shown, are visibly disabled and non-executing).
- Inspecting history, details, and metadata does not change the item's state.
- A guarded transition asks for confirmation and shows what the transition will do.
- After a transition, the new state is visible, the timeline and history reflect the move, and the change survives a browser refresh.
- Accidental refresh does not lose the observed state or silently re-execute a transition.
- Double-clicking or retrying a transition does not move the item twice.

---

## 3. Expected Page Structure

A Workflow page is composed of a set of UI components. Not every page contains every component. The contract distinguishes **required** from **optional**.

### 3.1 Required components

- **Item identity header** — establishes which existing item is being observed (an identifier and/or human label). Without it, the page is not a Workflow observation surface.
- **Current status indicator** — the rendering of the item's current lifecycle state. This is the definitional element: a Workflow page observes an existing item's state. If there is no current-state rendering, the page is not Workflow.
- **Transition controls** — the affordances that move the item to another lifecycle state. The defining action of the archetype. They MUST be present (even if currently offering no valid transition from the current state) or the page cannot fulfill its purpose.
- **History / timeline** — the record of how the item reached its current state, expressed as a chronological event log and/or an ordered state progression. This is the evidence that the current status is real and has a trail.
- **Loading state** — an explicit indicator while the item's current state and history are being fetched.
- **Error state** — an explicit, recoverable presentation when the item's state/history cannot be loaded or a transition fails.

### 3.2 Optional components

- **Summary panel** — key facts about the item, surfaced near the top.
- **Details panel** — the item's fuller content/attributes, often collapsible.
- **Metadata panel** — lifecycle bookkeeping (last-changed time, actor, identifiers, version).
- **Timeline** (distinct from History) — a visual progression of lifecycle states; may be combined with or separate from the event log.
- **Activity log** — a finer-grained, often append-only stream of events distinct from the state-level history/timeline.
- **Action bar** — a container region that groups the transition controls and any secondary actions.
- **Confirmation dialog** — modal shown before a guarded transition; required whenever a transition is guarded, and otherwise optional.
- **Comments / notes thread** — an annotation or collaboration surface attached to the item.
- **Attachments** — files associated with the item or a lifecycle step.
- **Notifications / toasts** — transient messages about transition outcomes or state changes.
- **Validation messages** — surfaced when a transition is blocked by an observable condition.
- **Empty state** — an explicit presentation when a region (e.g. history or comments) has no content yet, distinct from error.
- **Retry control** — a re-attempt affordance after a failed load or transition.
- **Undo control** — a reverse-transition affordance, present only where the item's lifecycle supports stepping back.
- **Expand / collapse sections** — controls that show/hide details, history, or metadata without altering the item's state.

### 3.3 Component contracts (browser-observable only)

- The **status indicator** renders exactly one current lifecycle state at any moment. It is the browser's source of truth for "where the item is." Hermes MUST be able to read it and assert it equals the latest history entry's resulting state.
- The **transition controls** present only the transitions valid from the current state. A control for an invalid transition MUST NOT be enabled (and, per page contract, is either hidden or visibly disabled and non-executing). Hermes MUST observe the current state to determine which transitions are offered.
- The **timeline / history** is append-oriented: a transition adds a new entry rather than overwriting prior entries. The most recent entry reflects the current status.
- The **confirmation dialog** traps focus, presents a summary of the pending transition, and returns the browser to the item view on cancel — without having changed the item's state.
- **Loading / empty / error** are mutually exclusive visible states for the state/history region at any moment.
- **Expand / collapse** and **scrolling** are visual only and MUST NOT discard observed state or pending input in a transition sub-flow.

---

## 4. Workflow Lifecycle

This contract describes the canonical lifecycle a browser session takes through a Workflow surface. It is descriptive of expected behavior, not a script.

```
Landing
   ↓
Load current state (fetch the item's current lifecycle state and history)
   ↓
Observe current status (read the status indicator)
   ↓
Inspect history (review the timeline / activity log of how the item arrived here)
   ↓
Review details (read the item's summary, details, and metadata)
   ↓
Execute transition (invoke a transition control valid from the current state)
   ↓
Confirm (a confirmation dialog summarizes the pending transition)
   ↓
Observe new state (the status indicator moves; the item is now in the new state)
   ↓
Refresh (browser reload or in-page refresh)
   ↓
Verify persistence (the new state and the appended history survive refresh)
```

### 4.1 Notes on the lifecycle

- **Load / Observe / Inspect / Review** may iterate any number of times before a transition is attempted. The path above is the *complete* set, not a forced single pass.
- **Execute transition** may be rejected (cancel) at the confirmation step and re-entered any number of times; cancellation never changes the item's state.
- **Confirm** is the boundary of intent: the actual state change occurs only when confirmation is accepted. The browser MUST NOT observe a state change before confirmation is accepted.
- **Observe new state** proves the transition took effect: the status indicator, timeline, and history all advance consistently.
- **Refresh / Verify persistence** is the closure that proves the transition was durable, not merely optimistic. After refresh, Hermes MUST observe that the item is still in the post-transition state and that the history still contains the transition entry.

---

## 5. Browser Interaction Contract

The expected interaction model for every Workflow page. These are behaviors Hermes should be able to exercise and observe.

### 5.1 Observing state

- **Viewing details** — opening/expanding the item's summary, details, and metadata surfaces reveals its content without altering state.
- **Reading status** — the current lifecycle state is visible and readable from the status indicator; Hermes MUST be able to assert which state the item is in.
- **Observing consistency** — the status indicator and the latest history/timeline entry agree; Hermes MUST be able to reconcile them.

### 5.2 Inspecting history

- **Viewing history** — the timeline / activity log renders the ordered record of lifecycle events.
- **Filtering timeline** — where the page supports it, filtering narrows the timeline/history by criterion (type, actor, range) without changing the item.
- **Searching history** — where the page supports it, a free-text query locates history entries; results are scoped to this item.
- **Expanding sections** — collapsing/expanding history, details, or metadata regions reveals/hides content without discarding observed state.
- **Collapsing sections** — same; the action is visual only and MUST NOT mutate the item or pending input.

### 5.3 Executing transitions

- **Executing transitions** — invoking a transition control valid from the current state initiates the move; the browser then confirms (if guarded) and observes the new state.
- **Cancelling transitions** — dismissing the confirmation (cancel, escape, backdrop) returns the browser to the item view with the item's state intact and nothing transitioned.
- **Confirmation** — a guarded transition first presents a confirmation dialog summarizing the pending move; the transition does not occur until the dialog is accepted.
- **Undo (if supported)** — where the lifecycle supports a reverse transition, an undo/reverse control returns the item to a prior state; the history records the reversal rather than erasing the forward entry.
- **Retry** — after a failed transition, a retry control re-attempts it; Hermes MUST observe that retry does not produce a duplicate transition (the item moves exactly once per accepted transition).
- **Transition availability** — a transition control is enabled only when the current state permits it and any observable precondition is satisfied; it is disabled (or absent) otherwise.

### 5.4 Viewport, reload, and history

- **Scrolling** — the status/timeline/details regions scroll independently of fixed chrome where the layout divides them; scrolling MUST NOT trigger or undo a transition.
- **Keyboard navigation** — tab order reaches the status indicator, history, details, and transition/confirmation controls; focus is visible and logical; escape closes overlays and returns to the item view.
- **Focus management** — after executing a transition, focus lands on a sensible next control (the new-state status or the next valid transition); after cancel, focus returns to the triggering control.
- **Browser refresh** — a full reload re-renders the item's current state and history from the server; the item is addressed by its identifier so the same state is restored.
- **Browser history** — opening the item via route may push a history entry; executing a transition may or may not push one. The page's contract defines which, and Hermes should record the actual behavior rather than assume.
- **History navigation** — browser back/forward return to the prior route or item state; they MUST NOT silently re-execute or undo a transition.
- **Deep links** — an item may be addressable by identifier in the URL; opening it directly renders the correct current state and history.
- **URL synchronization** — the item identifier (and, where supported, a focused history entry or section) may be reflected in the URL; the URL is the source of truth for restoring the item on reload.
- **Session restoration** — on reload, the observed item state and history are restored from the server by identifier; Hermes MUST assert the post-reload state is consistent with the pre-reload state and not silently corrupted or reset.

---

## 6. Browser Assertions

What Hermes should automatically verify on a Workflow page. These are browser-level observations.

- **State rendered** — the item's current lifecycle state is shown in the status indicator; the surface mounts without a blank screen or frozen shell.
- **Status visible** — the current status indicator is present, readable, and reflects exactly one state.
- **Timeline rendered** — the timeline (where the page defines one) paints the ordered state progression.
- **History rendered** — the history / activity log paints the chronological event record for this item.
- **Details verified** — the summary/details/metadata surfaces render the item's content coherently.
- **Transition controls visible** — the transition controls are present and reflect the transitions valid from the current state.
- **Transition enabled only when valid** — a transition control is enabled only from a state that permits it and when observable preconditions hold; it does not execute from a disallowed state.
- **Confirmation shown** — triggering a guarded transition presents a confirmation dialog summarizing the pending move.
- **Confirmation cancel safe** — dismissing confirmation returns to the item view with state unchanged.
- **New state visible** — after an accepted transition, the status indicator reflects the new state.
- **History updated** — after an accepted transition, the history/timeline contains a new entry reflecting the move, and the latest entry agrees with the new status.
- **Timeline updated** — where a timeline is defined, it advances to include the new state.
- **Loading state** — a loading indicator shows during state/history fetch and clears on completion.
- **Error state** — a recoverable error presentation appears on load or transition failure, with a path to retry.
- **No duplicate transitions** — a single accepted transition moves the item exactly once; double-click/retry does not move it twice.
- **No stale UI** — the status, timeline, and history reflect the current state after a transition, with no pre-transition residue.
- **No console errors** — the browser console shows no uncaught errors during the flow.
- **No JavaScript errors** — no unhandled rejection leaves the page frozen or the item unobservable.
- **Browser responsive** — status reads, history inspection, and transition execution respond within expected bounds; no permanent spinner.
- **Visual consistency** — status, timeline, history, and controls are coherent at the tested viewport; no overlapping, collapsed, or unstyled regions.
- **State consistency** — the status indicator and the latest history entry agree at all observed moments; no drift between what is shown and where the item actually is.

---

## 7. Failure Scenarios

Generic browser failures Hermes should be able to detect on a Workflow page. These are interaction/rendering failures, not domain errors.

- **Wrong status** — the status indicator shows a state that does not match the item's actual current state (e.g. a stale or mis-rendered badge).
- **Timeline not updated** — after a transition, the timeline does not advance to include the new state.
- **History missing** — the history / activity log fails to render, or a transition leaves no corresponding history entry.
- **Duplicate transition** — double-click, rapid re-press, or retry moves the item twice from one accepted transition.
- **Transition available too early** — a transition control is enabled (or presented) from a state that does not yet permit it, allowing a premature transition attempt.
- **Transition unavailable** — a transition that the current state should permit is missing, disabled contrary to state, or fails to execute.
- **Confirmation broken** — the confirmation dialog fails to open, fails to close, or traps focus with no escape; or accepting it does not perform the transition.
- **Browser freeze** — the page becomes unresponsive while loading state/history or executing a transition (long synchronous render, runaway loop).
- **Refresh inconsistency** — a full reload shows a different state than the in-page view claimed, or loses the item (wrong identifier, blank item view).
- **History inconsistency** — the history/timeline and the status indicator disagree (latest entry state ≠ current status).
- **Layout corruption** — status, timeline, history, or transition controls render misaligned, overflow, or clip content.
- **Rendering issues** — missing status badge, collapsed timeline nodes, unstyled history rows, or truncated controls.
- **Focus issues** — focus lost, trapped, or mis-directed after transition, cancellation, or section toggle.
- **Race conditions** — overlapping transition or history requests resolve out of order, painting a stale status or a stale history.
- **Timeout** — a state-load or transition request exceeds its bound and the UI has no defined recovery.
- **Retry failure** — a failed transition is not retried correctly, or retrying produces a duplicate transition.
- **Unexpected redirect** — executing or cancelling a transition navigates away from the item view without user intent.
- **Stale interface** — the status, timeline, or history shows pre-transition data after a successful transition.
- **Lost state** — the observed item state or pending transition input disappears without a transition (e.g. on a minor re-render, route blip, or section toggle).

---

## 8. Browser Evidence Collection

What Hermes should collect when exercising a Workflow page. Browser evidence only — no backend debugging.

- **Screenshots** — the item view (current state), the history/timeline, details/metadata panels, the confirmation dialog, the success/error states, and any corruption.
- **Console logs** — all console output, especially errors, warnings, and unhandled rejections during load, history inspection, and transition execution.
- **Network requests** — the state-load and transition requests with their URLs, methods, params, statuses, and timings — observed from the browser's network surface, not from server internals.
- **DOM state** — the rendered structure of the status indicator, timeline, history, details, transition controls, and banners at the moment of interest.
- **Current URL** — the address, including the item identifier and any query/section parameters, to prove deep-link and URL-sync behavior.
- **Browser storage** — relevant local/session storage entries that influence the item view (e.g. last-viewed section, draft note), read from the browser.
- **Timeline state** — the rendered ordered progression of states at the moment of interest, for order and completeness checks.
- **History state** — the rendered chronological entries, for count, order, and duplication checks.
- **Visible status** — the exact current-state rendering (badge text/pill value) observed in the status indicator.
- **Notifications** — toasts, banners, and inline messages shown about transition outcomes or state changes.
- **Dialogs** — whether the confirmation dialog is open, its title, its summarized pending transition, and its action controls.
- **Loading indicators** — which spinner/skeleton was active and when it cleared.
- **Timing** — durations for state load, history load, transition, and navigation, to detect timeouts and freezes.
- **Browser events** — focus changes, history pushes/pops, and visibility/responsiveness signals.

---

## 9. Browser Recovery Behavior

How Hermes should recover from the failures in Section 7, using browser-level actions only.

- **Retry transition** — re-trigger the failed transition via the page's retry control, and assert the item moves exactly once (no duplicate transition).
- **Refresh** — perform an in-page refresh or a full browser reload to clear a stuck state; then assert the post-reload item state and history are consistent.
- **Reload state** — re-fetch the item's current state and history (via an in-page refresh control) to recover from a stale or partially-loaded view.
- **Reopen page** — navigate away and back to the item route (by identifier) to rebuild a clean observation session.
- **Navigate back** — use browser back to escape an unexpected redirect or broken transition flow.
- **Navigate forward** — use browser forward to re-apply an item state lost by an unintended back.
- **Dismiss dialog** — close a stuck confirmation dialog via cancel/escape/backdrop to return to the item view intact.
- **Retry loading** — re-attempt a failed state/history load via the page's retry control.
- **Recover after timeout** — after a state-load or transition exceeds its bound, reload or retry and confirm the UI returns to a stable state and the item is not duplicated or double-transitioned.
- **Recover after failed request** — observe the error state, then retry or reload and verify the item returns to a consistent state (current status agrees with history).
- **Recover after browser reload** — after a reload, verify the item is restored by identifier to its correct current state and that the history still reflects prior transitions.
- **Reset browser state** — clear relevant browser storage that is corrupting the item view (e.g. a stale section/draft entry), then reload to a known-good baseline.
- **Restore browser session** — where the page persists view preferences or draft notes, verify they restore correctly after a full browser restart without altering the item's authoritative state.

---

## 10. Inheritance Contract

This is the most important section. It defines the inheritance model that makes Workflow a reusable layer.

### 10.1 The rule

**Every Workflow page automatically inherits the entire behavior defined in this document.** A future Browser Knowledge file does not re-describe status observation, timeline/history rendering, transition availability, confirmation dialogs, the load-observe-transition-verify lifecycle, loading/empty/error states, assertions, failure scenarios, evidence collection, or recovery. Those are inherited by referencing the Workflow archetype by name.

### 10.2 What a page-specific Browser Knowledge file must declare

A page-specific file supplies **only the delta** — what makes this one lifecycle surface different from every other. It declares:

- **Route** — the route (and any item-sub-routes) that addresses this item by identifier.
- **Lifecycle states** — the enumerated lifecycle states this item can hold, in the order the page presents them.
- **Allowed transitions** — for each state, which transitions are valid from it (the transition controls the page offers), and whether each is guarded by confirmation.
- **Unique validation rules** — validations specific to this page's transitions that the generic contract does not cover (e.g. a precondition that must hold before a given transition is permitted).
- **Unique business rules** — any rule that governs *this* page's browser behavior beyond the generic lifecycle (e.g. a transition that requires an attachment to be present first, surfaced as an observable condition).
- **Unique page exceptions** — any deviation from this archetype (e.g. this page renders history as a chat-style thread, or offers no timeline, or performs an immediate unconfirmed transition).

### 10.3 What a page-specific file MUST NOT declare

**Hermes MUST NOT duplicate inherited behavior inside page-specific Browser Knowledge.** Everything behavioral already in this document — the load/observe/inspect/review/transition/confirm/verify lifecycle, the status-indicator and history-consistency contracts, the transition-availability rule, the confirmation-dialog close behavior, the loading/empty/error states, the assertion set, the failure catalog, the evidence set, and the recovery set — is inherited by reference. Re-declaring these in a page file is a violation of the inheritance model and causes drift. A page file references the Workflow archetype by name and adds only its delta.

### 10.4 Inheritance model summary

```
Page-specific Browser Knowledge file
   └─ declares: route, lifecycle states,
                allowed transitions (per state),
                unique validation rules,
                unique business rules,
                unique page exceptions
   └─ inherits (by archetype reference): ALL of WORKFLOW_ARCHETYPE.md
                observe state · inspect history · review details
                transition availability · confirmation · verify persistence
                status/history consistency · states (loading/empty/error)
                assertions · failures · evidence · recovery
```

The class of change *"the confirmation dialog now shows the resulting state inline"* is made once, in this archetype, and every Workflow page gains it. The class of change *"this item adds a new lifecycle state and its allowed transitions"* is made only in that page's delta.

---

## 11. Scope Boundaries

This document defines **browser interaction behavior**. It explicitly does NOT define the following; those belong to page-specific Browser Knowledge files (or to layers beneath them):

- **Business rules** — domain logic that decides what a valid state or transition is; this document only expects that such rules are *observable* as validation or as transition availability in the browser.
- **Permissions** — what a given user may see or do; the archetype expects gated transition controls to be disabled/hidden but does not define who is gated.
- **Approval policies** — any multi-party or staged authorization around a transition; out of scope for a generic Workflow contract (the browser only observes whether a transition is offered and confirmed).
- **Transition logic** — the actual decision of *how* a state change is computed or what it entails; this document observes only the browser-visible effect (status moves, history appends).
- **Financial logic** — any monetary, balancing, or settlement constraints; out of scope.
- **Inventory logic** — any stock or quantity constraints; out of scope.
- **Database behavior** — how an item or its history is stored or migrated; this document references only the browser-visible effect (state appears, history grows).
- **API implementation** — how requests are formed, routed, or authenticated on the server; this document references only the browser-observable request (URL, params, status) as evidence.
- **Server processing** — what the server does when a transition is requested; this document observes only the resulting browser state and history.
- **Domain validation** — the specific content rules for a transition precondition; the archetype expects observable validation to be *surfaced*, but the rules themselves are page-specific.
- **Entity definitions** — the shape, identity, and meaning of the item being observed; those are declared per page.

If a page's dominant interaction is building a working session that is then committed, it is a Transaction page. If it is managing a collection, it is a CRUD page. A Workflow page may *inherit CRUD* (to reach the item from a list) or *inherit Transaction* (if a transition itself assembles a working session) as a secondary layer — but the lifecycle-observation-and-transition behavior itself is defined only here.

---

## 12. Stability

This document is intended to remain stable for years and to be extended rarely.

- **Defined by interaction shape, not domain.** Workflow is expressed entirely in browser behavior (observe an existing item's state, inspect its history, execute a permitted transition, confirm, verify the new state persists). Domains come and go; the interaction contract does not. A future lifecycle surface — whatever item it observes — will still show a current state, a history, and a set of permitted transitions.
- **New workflow pages inherit without modification.** When a new lifecycle surface is added to the application, its Browser Knowledge file references this archetype and supplies only its delta (states, transitions, validations, exceptions). This document is not edited for the new page.
- **Only new browser patterns justify change.** This archetype is updated only when a genuinely new workflow interaction pattern appears that the current contract does not cover (for example, a new universal state-observation primitive, a new standard confirmation lifecycle, or a new standard failure class such as a new transition-idempotency mechanism). Incremental UI polish, new lifecycle states, and new workflow pages do NOT justify editing this document.
- **Drift protection.** Because behavioral knowledge lives here and not in page files, a fix or clarification to the generic Workflow contract propagates to every page at once. The stable center is this document; the churn happens in the page-specific deltas beneath it.

*End of Workflow Archetype. Canonical inheritance layer for Hermes Browser Testing.*

---

# Browser Execution Checklist

□ State rendered
□ Status visible
□ Timeline rendered
□ History rendered
□ Details verified
□ Current status verified
□ Transition availability verified
□ Transition executed
□ Confirmation verified
□ Confirmation cancel safe
□ New state verified
□ Timeline updated
□ History updated
□ Loading verified
□ Error verified
□ Empty state verified (where applicable)
□ No duplicate transitions
□ No stale UI
□ Refresh verified
□ Browser history verified
□ Session restoration verified
□ Console verified
□ Network verified
□ Evidence collected
□ Recovery verified
