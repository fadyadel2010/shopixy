# Settings Archetype — Hermes Browser Testing Canonical Contract

**Document Type:** Canonical archetype — inheritance layer for Browser Knowledge
**Status:** Canonical — stable for years
**Layer:** One of the core inheritance layers beneath future `*KNOWLEDGE.yaml` files. This document expands ONLY the Settings archetype first identified in `PAGE_ARCHETYPE_DISCOVERY.md`. It does not redefine that discovery, nor does it redefine the CRUD, Transaction, Workflow, or Report archetypes. Where the five overlap conceptually (loading/empty/error states, validation surfacing, URL/history/refresh behavior, confirmation posture, session-restoration behavior), Settings inherits the *behavioral* expectations and extends them with the load-edit-validate-save-apply lifecycle that is unique to configuration pages.
**Companion Artifact:** The Browser Knowledge generation skill consumes this archetype by name and layers page-specific deltas on top.
**Authority Note:** This document defines *browser interaction behavior only*. It is deliberately independent of any business domain. No entity, field, rule, endpoint, or persistence mechanism is asserted here. A future Browser Knowledge file supplies those; everything behavioral is inherited from this document. Where this document states that a saved value is server-authoritative, or that a configuration drives downstream behavior, it states a browser-evidence expectation (the browser must observe the server's acceptance of the save and the resulting persisted state), not an implementation detail. No monetary, quantity, stock, ledger, or domain semantics are implied anywhere in this document.

---

## 1. Purpose

### 1.1 Why Settings exists

A large class of application surfaces do not manage collections (CRUD), do not assemble a working session to commit (Transaction), do not observe a lifecycle item (Workflow), and do not primarily observe a loaded dataset (Report). They are **configuration surfaces**: the browser loads a persistent set of values, lets the user edit them, validates them, and persists them so that the saved configuration becomes the active state for the rest of the application.

The defining property is that the page's purpose is to change *persistent configuration*, not to change a record, commit a result, move a lifecycle, or explore data. The interaction contract is therefore different in kind from the other archetypes.

### 1.2 Why it differs from CRUD

- **Mutation intent.** CRUD mutates independent records inside a collection (create/edit/delete rows). Settings mutates a *single persistent configuration document* as a whole — individual controls are fields of one cohesive state, not standalone records.
- **Default state.** A CRUD page defaults to a *view of a collection*. A Settings page defaults to a *loaded configuration* whose values are already populated from persistence.
- **Source of structure.** CRUD structure is a table/cards of uniform records. Settings structure is a form organized into sections/groups/tabs of controls — the page is organized around *what is being configured*, not around row-level add/change/remove.
- **Side-effect observation.** CRUD observes row-level add/change/remove as its purpose. Settings observes that a save persisted and that the saved configuration is reflected after reload; it produces one cohesive persisted state, not a collection mutation.

### 1.3 Why it differs from Transaction

- **Who builds the object.** Transaction assembles a disposable working session from nothing and commits it as a result. Settings loads an already-existing persistent configuration and edits it in place.
- **Default state.** A Transaction page defaults to an empty (or recalled) *workspace* awaiting assembly. A Settings page defaults to a *loaded configuration* already populated.
- **The defining act.** In Transaction the defining act is commit (the working session is consumed and a result appears). In Settings the defining act is **save/persist** (the edited configuration becomes the active persisted state); there is no consumed working session and no generated result document.
- **Side-effect observation.** Transaction observes a committed result and the clearing of the workspace. Settings observes the persisted configuration value and its reflection after reload (and, where the page previews, the preview of the pending change).
- **Object ownership.** The browser in Transaction owns the working session until commit. The browser in Settings holds an editable copy of a persistent configuration; saving replaces the persisted state.

### 1.4 Why it differs from Workflow

- **Subject of configuration.** Workflow renders a single existing item whose nature is its lifecycle state and the states it can move to. Settings renders a single persistent configuration whose nature is the set of current values.
- **Mutation shape.** In Workflow, the meaningful browser act is the **transition** that moves an item to a new state. In Settings, the meaningful browser act is **editing and saving a configuration** — there is no lifecycle to advance.
- **Source of structure.** Workflow structure is a status indicator, a timeline/history, and transition controls. Settings structure is a form of controls organized into sections/groups/tabs, with save/apply/reset/restore controls.
- **State authority.** Workflow observes a current lifecycle state plus a recorded history that must agree with it. Settings observes a current persisted configuration value plus the unsaved-edit state of each control; the two (persisted value and edited value) MUST be distinguishable and reconcilable.

### 1.5 Why it differs from Report

- **Mutation intent.** Report is observation-only: it loads and explores a dataset and never changes what it observes. Settings is configuration-first: its entire purpose is to change persistent state.
- **Default state.** A Report page defaults to a *loaded observation* presented for inspection. A Settings page defaults to a *loaded configuration* presented for editing.
- **The defining act.** In Report the defining act is *exploration* (filter/group/sort/aggregate/export) that reshapes the view without mutating data. In Settings the defining act is **save/persist**, which changes the persisted configuration and thereby downstream behavior.
- **Output.** Report's only outputs are a reshaped on-screen view and a derived export artifact. Settings' output is a persisted configuration state that survives reload and drives the application.

### 1.6 Why Hermes should inherit Settings behavior instead of learning every page individually

- **Transfer.** A new configuration surface shipped next quarter reuses the same contract. Hermes recognizes it on first encounter from the inherited layer instead of requiring a new knowledge file.
- **Single point of maintenance.** A change to the generic load-edit-validate-save-apply-reset-restore lifecycle is made once, here, and benefits every Settings page at once.
- **Reasoning consistency.** Generic QA expectations (server-authoritative save, validation surfacing, unsaved-changes tracking, fail-closed on ambiguous state, reload/persist consistency) are applied uniformly because they live in one place.
- **Reduced knowledge weight.** A page-specific file declares only its delta (route, sections, controls, validation, defaults, preview/persistence behavior, exceptions). The bulk of behavioral knowledge is inherited, not repeated.

---

## 2. Browser Identity

### 2.1 Typical characteristics

A Settings page exists to let the browser **configure persistent behavior**. It loads a configuration, presents it as editable controls, validates edits, and persists them so the saved configuration becomes active. The page is configuration-first: its resting state is a loaded configuration with controls reflecting the persisted values, and the dominant user action is editing and saving rather than browsing or observing.

### 2.2 Typical interaction model

The page is **view-first, edit-second, persist-third**. The user lands on a loaded configuration, reads the current values, modifies controls, sees validation and (where supported) a preview of the pending change, saves (and where the page separates the two, applies), and then observes that the saved configuration persists across reload. The save is the guarded act: it replaces the persisted state, so validation blocks it and (where the page defines one) a confirmation precedes it.

### 2.3 Typical browser layout

- An application shell (sidebar/nav) is assumed present; Settings is the content surface inside it.
- A **settings header** establishing the configuration's identity (a title and/or scope label).
- A **navigation panel** (sections/groups/tabs) that organizes the configuration into reachable regions.
- **Form controls** — the editable surface: text inputs, selectors, toggles, sliders, and any other control type the page exposes.
- An **unsaved-changes indicator** — a marker that the current edits differ from the persisted configuration.
- A **preview area** — where supported, a live rendering of the effect of the pending edits.
- A **validation message surface** — where invalid or incomplete input is surfaced.
- **Action controls** — save, apply, reset, and restore-defaults, the definitional actions of the archetype.
- A **confirmation dialog** — shown before a guarded save/apply where the page defines one.
- A **success notification** — transient confirmation that the save/apply persisted.
- **Loading**, **empty**, and **error** states for the relevant regions.

### 2.4 Typical navigation

- Reached via a sidebar/menu link or a dashboard/entry route to a settings route.
- The configuration may be organized into sub-routes or tabs addressed by URL; opening one directly renders the correct section.
- Some settings are reached through redirect routes that resolve to a canonical configuration surface; the browser lands there and the Settings contract applies.
- Save/apply typically keeps the browser on the settings surface, updating the persisted state in place rather than navigating away.

### 2.5 Typical user expectations

- The page loads and shows the current persisted configuration — not a blank or ambiguous screen.
- Controls reflect the persisted values; editing a control changes only that control's pending value, not others.
- Invalid input is surfaced clearly and blocks save rather than silently discarding.
- An unsaved-changes indicator appears when edits diverge from the persisted state, and clears on save or reset.
- A preview (where present) reflects the pending edits live, without persisting them.
- Save/apply persists the configuration; after reload, the saved values are restored and reflected across the application.
- Reset returns the edited controls to the persisted state; restore-defaults returns them to the documented defaults.
- Accidental refresh does not silently discard edits without warning, and does not re-trigger a save.
- A guarded save asks for confirmation and shows what will change.

---

## 3. Expected Page Structure

A Settings page is composed of a set of UI components. Not every page contains every component. The contract distinguishes **required** from **optional**.

### 3.1 Required components

- **Form controls** — the editable surface presenting the configuration as controls (text inputs, selectors, toggles, sliders, or other control types). This is the definitional element of the archetype. If there are no editable controls, the page is not Settings.
- **Save control** — the affordance that persists the edited configuration. Its availability is driven by edit validity and (where the page separates the two) by the apply posture.
- **Unsaved-changes indicator** — a marker distinguishing the edited state from the persisted configuration. A Settings page without a way to tell edits from persisted state is incomplete.
- **Loading state** — an explicit indicator while the configuration is being loaded or saved.
- **Error state** — an explicit, recoverable presentation when the configuration cannot be loaded or the save fails.

### 3.2 Optional components

- **Settings header** — the configuration's title and/or scope label.
- **Navigation panel** — sections/groups/tabs that organize the configuration into reachable regions.
- **Categories / sections / groups** — nested organization of controls within the surface.
- **Tabs** — switch between distinct sub-surfaces of the same configuration.
- **Text inputs** — free-text or numeric entry controls.
- **Selectors** — dropdown / radio / list controls that choose one of a set of values.
- **Toggles** — on/off switches.
- **Sliders** — range controls with a draggable handle.
- **Preview area** — a live rendering of the effect of the pending edits, without persisting them.
- **Validation messages** — surfaced when a control is invalid or incomplete.
- **Apply control** — where the page separates "persist" (save) from "activate" (apply); present only when the page defines the distinction.
- **Reset control** — returns edited controls to the persisted state.
- **Restore-defaults control** — returns controls to the documented defaults.
- **Confirmation dialog** — modal shown before a guarded save/apply; required whenever save/apply is guarded, and otherwise optional.
- **Success notification** — transient confirmation that the save/apply persisted.
- **Empty state** — an explicit presentation when a region (e.g. a search-filtered section list) has no content, distinct from error.
- **Reload control** — re-loads the configuration from persistence without a full navigation.

### 3.3 Component contracts (browser-observable only)

- The **form controls** render the persisted configuration on load; each control's displayed value MUST equal the persisted value until the user edits it.
- The **unsaved-changes indicator** appears when any control's pending value differs from its persisted value, and clears when edits are saved, applied, reset, or restored to the persisted state. The browser MUST be able to read it and assert edit-vs-persisted divergence.
- The **save control** is **disabled** while there are no edits or while any control is invalid, and **enabled** only when there are valid edits to persist. (Where the page separates apply, save persists and apply activates; the contract binds both to validity and edit state.)
- The **preview area** reflects the pending edits live and MUST NOT itself persist anything; Hermes MUST be able to assert that the preview matches the current control values without the persisted state having changed.
- The **confirmation dialog** traps focus, presents a summary of the pending change, and returns the browser to the configuration on cancel — without having persisted anything.
- **Loading / empty / error** are mutually exclusive visible states for the relevant region at any moment.
- **Expand / collapse** and **scrolling** are visual only and MUST NOT discard pending edits or the unsaved-changes indicator.

---

## 4. Settings Lifecycle

This contract describes the canonical lifecycle a browser session takes through a Settings surface. It is descriptive of expected behavior, not a script.

```
Landing
   ↓
Load configuration (fetch and present the persisted configuration)
   ↓
Modify values (edit controls; pending edits diverge from persisted state)
   ↓
Validate (the browser observes validation verdicts; invalid state blocks save)
   ↓
Preview (where supported, view the effect of pending edits without persisting)
   ↓
Save (persist the edited configuration)
   ↓
Apply (where the page separates the two, activate the persisted configuration)
   ↓
Observe persisted state (success notification; controls reflect persisted values)
   ↓
Refresh (browser reload or in-page reload)
   ↓
Verify persistence (the saved values are restored after refresh)
```

### 4.1 Notes on the lifecycle

- **Load / Modify / Validate / Preview** may iterate any number of times before Save. The path above is the *complete* set, not a forced single pass.
- **Save** may be rejected (cancel) at the confirmation step and re-entered any number of times; cancellation never persists.
- **Save** is the boundary of intent: the persisted state changes only when save is accepted. The browser MUST NOT observe a persisted change before save is accepted.
- **Apply** (where the page defines it) activates the already-persisted configuration; it does not by itself alter the persisted values. Hermes MUST observe which of save/apply the page uses and assert the correct one persisted/activated.
- **Refresh / Verify persistence** is the closure that proves the save was durable, not merely optimistic. After refresh, Hermes MUST observe that the controls reflect the saved values and that the unsaved-changes indicator is clear.

---

## 5. Browser Interaction Contract

The expected interaction model for every Settings page. These are behaviors Hermes should be able to exercise and observe.

### 5.1 Editing values

- **Editing values** — changing a control updates its pending value; the unsaved-changes indicator appears and the persisted configuration is unchanged until save.
- **Typing** — entering text into a text input updates that control's pending value; the edit is reflected without navigating away.
- **Selecting** — choosing an option in a selector updates the control's pending value.
- **Toggling** — switching a toggle flips its pending on/off state.
- **Dragging sliders** — moving a slider handle sets its pending value; the change is continuous and reflected live.
- **Expanding sections** — collapsing/expanding a section/group reveals/hides controls without discarding pending edits or the unsaved-changes indicator.
- **Collapsing sections** — same; the action is visual only and MUST NOT reset any control.

### 5.2 Arranging and previewing

- **Switching tabs** — changing between sub-surfaces of the configuration loads the relevant controls; tab state may persist and MUST restore the correct surface on reload. Pending edits on other tabs MUST be retained, not discarded.
- **Searching settings** — where the page supports it, a free-text query filters the visible sections/controls without altering any value.
- **Filtering settings** — where the page supports it, a facet control narrows the visible sections/controls without altering any value.
- **Previewing changes** — the preview area reflects the pending edits live; Hermes MUST be able to assert the preview matches the current control values and that the persisted state has not changed.

### 5.3 Persisting and restoring

- **Saving** — invoking save persists the edited configuration; a loading state covers the surface until resolution, then a success notification confirms and the persisted values are reflected.
- **Applying** — where the page separates apply, invoking apply activates the persisted configuration without re-persisting values; Hermes MUST observe the activation effect.
- **Resetting** — invoking reset returns edited controls to the persisted state and clears the unsaved-changes indicator, without persisting a change.
- **Restoring defaults** — invoking restore-defaults returns controls to the documented defaults; the browser MUST distinguish this from reset (persisted state vs. defaults) and reflect the changed pending values.
- **Confirmation** — a guarded save/apply first presents a confirmation dialog summarizing the pending change; the persist/activate does not occur until the dialog is accepted.
- **Cancellation** — dismissing the confirmation (cancel, escape, backdrop) returns the browser to the configuration with edits intact and nothing persisted/activated.

### 5.4 Viewport, reload, and history

- **Scrolling** — the configuration and any fixed chrome scroll independently where the layout divides them; scrolling MUST NOT trigger a save or discard edits.
- **Keyboard navigation** — tab order reaches the navigation panel, controls, validation surface, and save/apply/reset/restore controls; focus is visible and logical; escape closes overlays and returns to the configuration.
- **Focus management** — after editing a control, focus lands on a sensible next control; after save, focus moves to the success notification's primary action or back to the surface.
- **Browser refresh** — a full reload re-renders the configuration from persistence; pending edits not yet saved are either warned about, discarded per the page contract, or restored from browser storage where the page persists drafts. Hermes MUST assert the post-reload state is consistent with the page's declared unsaved-changes contract.
- **Browser history** — switching a tab or section may push a history entry; the page's contract defines which interactions are recorded, and Hermes should record the actual behavior rather than assume.
- **Deep links** — a section/tab may be addressable by URL; opening it directly renders the correct region with the correct controls.
- **URL synchronization** — the active section/tab (and, where supported, a pending-edit draft id) may be reflected in the URL; the URL is the source of truth for restoring that region on reload.
- **Unsaved changes handling** — where the page defines a guard, navigating away or reloading with unsaved edits prompts the user (or persists a draft); the browser MUST NOT silently lose edits nor silently persist them.
- **Session restoration** — on reload, the loaded configuration is restored from persistence; where the page persists drafts, the pending edits are restored consistently. Hermes MUST assert the post-reload state is consistent and not silently corrupted.

---

## 6. Browser Assertions

What Hermes should automatically verify on a Settings page. These are browser-level observations.

- **Configuration loaded** — the surface mounts with the persisted configuration populated into controls, not a blank screen or frozen shell.
- **Controls rendered** — the expected controls (text inputs, selectors, toggles, sliders, and others) are present and reachable.
- **Values displayed** — each control shows the persisted value on load; the displayed value equals the persisted value until edited.
- **Validation visible** — invalid or incomplete input is surfaced and visible; the browser observes the authoritative verdict on whether save is permitted.
- **Preview updated** — where a preview exists, it reflects the pending edits live and matches the current control values without the persisted state having changed.
- **Unsaved changes detected** — editing a control raises the unsaved-changes indicator; the browser MUST be able to assert edit-vs-persisted divergence.
- **Save enabled only when appropriate** — the save control is enabled only when there are valid edits to persist; it is disabled while there are no edits or while any control is invalid, and does not persist on click while disabled.
- **Apply behavior correct** — where the page separates apply, invoking it activates the persisted configuration without re-persisting values, and the activation effect is observable.
- **Persisted values restored** — after save and refresh, the controls reflect the saved values and the unsaved-changes indicator is clear.
- **Reset verified** — invoking reset returns edited controls to the persisted state and clears the unsaved-changes indicator without persisting a change.
- **Restore defaults verified** — invoking restore-defaults returns controls to the documented defaults and reflects the changed pending values (distinct from reset).
- **Confirmation shown** — triggering a guarded save/apply presents a confirmation dialog summarizing the pending change.
- **Confirmation cancel safe** — dismissing confirmation returns to the configuration with edits intact and nothing persisted/activated.
- **Success state visible** — after a successful save, a success notification is shown and the controls reflect the persisted values.
- **Failure state visible** — on save failure, an error state is shown and the edited configuration is preserved for retry.
- **Loading state** — a loading indicator shows during load and save and clears on completion.
- **Error state** — a recoverable error presentation appears on load or save failure, with a path to retry.
- **No stale UI** — controls show the persisted (or pending) value after save/reset/restore, with no pre-edit residue.
- **No console errors** — the browser console shows no uncaught errors during the flow.
- **No JavaScript errors** — no unhandled rejection leaves the page frozen or the configuration uneditable.
- **Browser responsive** — edits, save, apply, reset, and restore respond within expected bounds; no permanent spinner.
- **Visual consistency** — controls, navigation, preview, and action controls are coherent at the tested viewport; no overlapping, collapsed, or unstyled regions.
- **Configuration consistency** — the persisted value and the displayed value agree at all observed moments (except where a deliberate pending edit diverges, which the unsaved-changes indicator MUST reflect); no drift between what is shown and what is persisted.

---

## 7. Failure Scenarios

Generic browser failures Hermes should be able to detect on a Settings page. These are interaction/rendering failures, not domain errors.

- **Lost configuration** — the loaded configuration disappears or fails to populate, leaving controls blank or stale (e.g. on a minor re-render, route blip, or storage cap breach).
- **Broken validation** — invalid input is not surfaced, or the save control stays enabled despite invalid state; or valid input is blocked.
- **Preview mismatch** — the preview area does not reflect the pending edits, or reflects them after the persisted state has already changed.
- **Save unavailable** — the save control remains disabled even when there are valid edits to persist.
- **Save too early** — the save control is enabled while there are no edits or while a control is invalid, allowing an invalid or empty save attempt.
- **Apply failure** — where the page separates apply, invoking it does not activate the persisted configuration or activates the wrong state.
- **Reset failure** — reset does not return edited controls to the persisted state, or clears the unsaved-changes indicator incorrectly.
- **Restore defaults failure** — restore-defaults does not return controls to the documented defaults, or persists the defaults unintentionally.
- **Unsaved changes lost** — pending edits disappear without a save (on a re-render, tab switch, or reload contrary to the page's unsaved-changes contract).
- **Refresh inconsistency** — a full reload shows different values than the in-page view claimed, or loses pending edits/section state contrary to the persistence contract.
- **History inconsistency** — back/forward navigates to an unexpected configuration region, or a tab/section change desyncs with browser history.
- **Layout corruption** — the navigation panel, controls, preview, or action controls render misaligned, overflow, or clip content.
- **Rendering issues** — missing labels, collapsed controls, unstyled regions, or truncated inputs.
- **Broken navigation** — a section/tab link or deep link lands on the wrong region, a blank page, or an error.
- **Race conditions** — overlapping load/save or multi-control requests resolve out of order, painting a stale or inconsistent configuration.
- **Timeout** — a load or save request exceeds its bound and the UI has no defined recovery.
- **Retry failure** — a failed save is not retried correctly, or retrying produces a duplicated or conflicting persisted state.
- **Browser freeze** — the page becomes unresponsive during load or save (long synchronous render, runaway loop).
- **Unexpected redirect** — saving, applying, or reloading navigates away from the settings surface without user intent.
- **Stale interface** — controls show pre-save or pre-reset data after a successful save/reset/restore.

---

## 8. Browser Evidence Collection

What Hermes should collect when exercising a Settings page. Browser evidence only — no backend debugging.

- **Screenshots** — the loaded configuration, each edit/preview state, the confirmation dialog, the success/error states, and any corruption.
- **Console logs** — all console output, especially errors, warnings, and unhandled rejections during load, edit, validate, preview, save, and apply.
- **Network requests** — the load and save (and apply) requests with their URLs, methods, params, statuses, and timings — observed from the browser's network surface, not from server internals.
- **DOM state** — the rendered structure of the navigation panel, controls, preview area, validation surface, action controls, and banners at the moment of interest.
- **Current URL** — the address, including any query/section parameters reflecting the active region or draft, to prove deep-link and URL-sync behavior.
- **Browser storage** — relevant local/session storage entries holding the configuration or a persisted draft, read from the browser, to verify persistence and draft correctness.
- **Configuration state** — the actual control values present (persisted and pending), for divergence and duplication checks.
- **Preview state** — the rendered preview content at the moment of interest, for reconciliation against the current control values.
- **Validation messages** — the exact text and location of any surfaced validation.
- **Dialogs** — whether the confirmation dialog is open, its title, its summarized pending change, and its action controls.
- **Notifications** — toasts, banners, and inline messages shown about save/apply outcomes.
- **Loading indicators** — which spinner/skeleton was active and when it cleared.
- **Timing** — durations for load, save, apply, and navigation, to detect timeouts and freezes.
- **Browser events** — focus changes, history pushes/pops, storage writes, and visibility/responsiveness signals.

---

## 9. Browser Recovery Behavior

How Hermes should recover from the failures in Section 7, using browser-level actions only.

- **Retry save** — re-trigger the failed save via the page's retry control and assert the persisted state becomes consistent and is not duplicated/conflicting.
- **Retry apply** — re-trigger a failed apply and assert the persisted configuration is activated correctly.
- **Reload configuration** — re-load the configuration from persistence (via an in-page reload control) to recover from a stale or partially-loaded view.
- **Refresh** — perform an in-page refresh or a full browser reload to clear a stuck state; then assert the post-reload configuration is consistent with the persistence contract.
- **Reset controls** — invoke reset to return edited controls to the persisted state and clear the unsaved-changes indicator.
- **Restore defaults** — invoke restore-defaults to return controls to the documented defaults, then re-edit deliberately if needed.
- **Navigate back** — use browser back to escape an unexpected redirect or broken save/apply flow.
- **Navigate forward** — use browser forward to re-apply a configuration region lost by an unintended back.
- **Recover after timeout** — after a load or save exceeds its bound, reload or retry and confirm the UI returns to a stable, consistent state and the persisted configuration is not duplicated/conflicting.
- **Recover after failed request** — observe the error state, then retry or reload and verify the configuration returns to a consistent state (persisted values reflect the last accepted save).
- **Recover after browser reload** — after a reload, verify the persisted configuration is restored and (where the page persists drafts) that pending edits are restored consistently without silent loss or silent persistence.
- **Reset browser state** — clear relevant browser storage that is corrupting the configuration view (e.g. a stale draft), then reload to a known-good baseline.
- **Restore browser session** — where the page persists drafts or section state, verify they restore correctly after a full browser restart without altering the authoritative persisted configuration.

---

## 10. Inheritance Contract

This is the most important section. It defines the inheritance model that makes Settings a reusable layer.

### 10.1 The rule

**Every Settings page automatically inherits the entire behavior defined in this document.** A future Browser Knowledge file does not re-describe loading configuration, editing controls, validation surfacing, preview, save/apply, reset, restore-defaults, the unsaved-changes contract, the load-edit-validate-save-apply lifecycle, loading/empty/error states, assertions, failure scenarios, evidence collection, or recovery. Those are inherited by referencing the Settings archetype by name.

### 10.2 What a page-specific Browser Knowledge file must declare

A page-specific file supplies **only the delta** — what makes this one configuration surface different from every other. It declares:

- **Route** — the settings route (and any section/tab sub-routes) for this page.
- **Available sections** — the specific sections/groups/tabs this configuration surface organizes its controls into.
- **Available controls** — the specific controls this page exposes (text inputs, selectors, toggles, sliders, and others), beyond the generic form-control contract.
- **Validation rules** — validations specific to this page's controls that the generic contract does not cover (e.g. a field that is conditionally required, a format constraint surfaced by the server).
- **Default values** — the documented default for each control, so restore-defaults behavior can be asserted.
- **Preview behavior** — how (and whether) this page previews pending edits, and what the preview renders.
- **Persistence behavior** — how this page persists (save), whether it separates apply, whether it persists drafts across reload, and its declared unsaved-changes contract.
- **Unique business rules** — any rule that governs *this* page's browser behavior beyond the generic configuration contract (e.g. a control surfaced only under a given scope).
- **Unique page exceptions** — any deviation from this archetype (e.g. this page has no preview, or persists drafts differently, or requires confirmation on every save).

### 10.3 What a page-specific file MUST NOT declare

**Hermes MUST NOT duplicate inherited behavior inside page-specific Browser Knowledge.** Everything behavioral already in this document — the load/edit/validate/preview/save/apply/reset/restore lifecycle, the unsaved-changes and persisted-vs-displayed contracts, the preview/validation posture, the confirmation-dialog close behavior, the loading/empty/error states, the assertion set, the failure catalog, the evidence set, and the recovery set — is inherited by reference. Re-declaring these in a page file is a violation of the inheritance model and causes drift. A page file references the Settings archetype by name and adds only its delta.

### 10.4 Inheritance model summary

```
Page-specific Browser Knowledge file
   └─ declares: route,
                available sections,
                available controls,
                validation rules,
                default values,
                preview behavior,
                persistence behavior,
                unique business rules,
                unique page exceptions
   └─ inherits (by archetype reference): ALL of SETTINGS_ARCHETYPE.md
                load · edit · validate · preview
                save (persist) · apply (activate)
                reset · restore-defaults
                unsaved-changes contract · persisted-vs-displayed contract
                states (loading/empty/error)
                assertions · failures · evidence · recovery
```

The class of change *"the save control now shows a confirm-on-leave guard"* is made once, in this archetype, and every Settings page gains it. The class of change *"this page adds a conditionally-required field"* is made only in that page's delta.

---

## 11. Scope Boundaries

This document defines **browser interaction behavior**. It explicitly does NOT define the following; those belong to page-specific Browser Knowledge files (or to layers beneath them):

- **Business rules** — domain logic that decides what a valid configuration is; this document only expects that such rules are *observable* as validation in the browser.
- **Permissions** — what a given user may see or configure; the archetype expects gated controls to be disabled/hidden but does not define who is gated.
- **Configuration semantics** — the meaning or effect of a saved value on the rest of the application; out of scope for a generic Settings contract (the browser observes only that the value persisted and is reflected after reload).
- **Default value calculations** — how a default is computed or sourced; out of scope. The archetype expects restore-defaults to return *a* documented default, but the calculation is page-specific.
- **Database behavior** — how a configuration is stored or migrated; this document references only the browser-visible effect (values load, save persists, reload restores).
- **API implementation** — how requests are formed, routed, or authenticated on the server; this document references only the browser-observable request (URL, params, status) as evidence.
- **Server processing** — what the server does with a saved configuration; this document observes only the resulting browser state (controls reflect the persisted value).
- **Entity definitions** — the shape and meaning of the configured records; those are declared per page.
- **Validation logic** — the specific content rules for a control; the archetype expects server-authoritative validation to be *surfaced*, but the rules themselves are page-specific.

If a page's dominant interaction is managing a collection, it is a CRUD page. If it assembles and commits a working session, it is a Transaction page. If it observes a lifecycle, it is a Workflow page. If it primarily observes a loaded dataset, it is a Report page. A Settings page may sit downstream of or feed another archetype — but the load-edit-validate-save-apply configuration behavior itself is defined only here.

---

## 12. Stability

This document is intended to remain stable for years and to be extended rarely.

- **Defined by interaction shape, not domain.** Settings is expressed entirely in browser behavior (load a configuration, edit controls, validate, preview, save/apply, reset, restore defaults, verify persistence). Domains come and go; the interaction contract does not. A future configuration surface — whatever it configures — will still load values, let the user edit them, validate, and persist them.
- **New settings pages inherit without modification.** When a new configuration surface is added to the application, its Browser Knowledge file references this archetype and supplies only its delta (sections, controls, validation, defaults, preview/persistence behavior, exceptions). This document is not edited for the new page.
- **Only new browser patterns justify change.** This archetype is updated only when a genuinely new settings interaction pattern appears that the current contract does not cover (for example, a new universal configuration primitive, a new standard save/apply lifecycle, a new standard unsaved-changes guard, or a new standard failure class such as a new draft-persistence mechanism). Incremental UI polish, new controls, and new settings pages do NOT justify editing this document.
- **Drift protection.** Because behavioral knowledge lives here and not in page files, a fix or clarification to the generic Settings contract propagates to every page at once. The stable center is this document; the churn happens in the page-specific deltas beneath it.

*End of Settings Archetype. Canonical inheritance layer for Hermes Browser Testing.*

---

# Browser Execution Checklist

□ Configuration loaded
□ Controls rendered
□ Values verified
□ Validation verified
□ Preview verified
□ Unsaved changes verified
□ Save verified
□ Apply verified
□ Reset verified
□ Restore defaults verified
□ Refresh verified
□ Browser history verified
□ Console verified
□ Network verified
□ Evidence collected
□ Recovery verified
