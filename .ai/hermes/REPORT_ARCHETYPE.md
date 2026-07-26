# Report Archetype — Hermes Browser Testing Canonical Contract

**Document Type:** Canonical archetype — inheritance layer for Browser Knowledge
**Status:** Canonical — stable for years
**Layer:** One of the core inheritance layers beneath future `*KNOWLEDGE.yaml` files. This document expands ONLY the Report archetype first identified in `PAGE_ARCHETYPE_DISCOVERY.md`. It does not redefine that discovery, nor does it redefine the CRUD, Transaction, or Workflow archetypes. Where the four overlap conceptually (loading/empty/error states, URL/history/refresh behavior, deep-link and session-restoration behavior), Report inherits the *behavioral* expectations and extends them with the observation-and-exploration model that is unique to report pages.
**Companion Artifact:** The Browser Knowledge generation skill consumes this archetype by name and layers page-specific deltas on top.
**Authority Note:** This document defines *browser interaction behavior only*. It is deliberately independent of any business domain. No entity, field, rule, endpoint, or persistence mechanism is asserted here. A future Browser Knowledge file supplies those; everything behavioral is inherited from this document. A Report page exists to observe information: it loads, filters, searches, groups, sorts, aggregates, compares, visualizes, and exports loaded data, but it does not build, mutate, or transition state. Where this document states that a presented value is derived (for example, an aggregated summary), it states a browser-evidence expectation (the browser must observe the derived value consistently with the underlying loaded dataset), not an implementation detail. A Report page may issue a request carrying filter parameters to fetch the presented dataset, but it MUST NOT mutate any observed record or state. No monetary, quantity, stock, ledger, or domain semantics are implied anywhere in this document.

---

## 1. Purpose

### 1.1 Why Report exists

A large class of application surfaces are not collections of records the user edits (CRUD), not a working session the user assembles and commits (Transaction), and not a lifecycle item the user drives through states (Workflow). They are dedicated **observation surfaces**: the browser loads a dataset, presents it for inspection through filters, grouping, sorting, aggregation, comparison, and visualization, and lets the user export or refresh what they see.

The defining property is that the page is **read-only by intent**. Nothing on the page changes the underlying data; the page's entire value is in how well the browser can explore what has already been loaded. The interaction contract is therefore different in kind from the mutating archetypes.

### 1.2 Why it differs from CRUD

- **Mutation intent.** A CRUD page can create, edit, and delete records; those sub-flows are first-class. A Report page has no create/edit/delete; any write-style affordance (save filter preset, export) acts on the *view*, not on the observed records.
- **Default state.** A CRUD page defaults to a *view of a collection* the user may act on. A Report page defaults to a *loaded observation* the user explores; the resting state is "data is shown and inspectable," not "a record is about to be changed."
- **Source of structure.** CRUD structure is a table of uniform independent records. Report structure is a presented dataset shaped by filters, grouping, aggregation, and visualization — the page is organized around *what is being observed and how it is arranged*, not around row-level add/change/remove.
- **Side-effect observation.** CRUD observes row-level add/change/remove as the page's purpose. Report observes only that the loaded dataset and its derived presentation stay consistent with the controls the user applied; it produces no durable change to the data it shows.

### 1.3 Why it differs from Transaction

- **Who builds the object.** Transaction assembles a disposable working session from nothing and commits it. Report loads an already-existing dataset; the browser never builds it and never commits it.
- **Default state.** A Transaction page defaults to an empty (or recalled) *workspace* awaiting assembly. A Report page defaults to a *loaded observation* — filters and a presented dataset — immediately available for inspection.
- **The defining act.** In Transaction the defining act is commit (the working session is consumed and a result appears). In Report the defining act is *exploration* — applying a filter, regrouping, re-sorting, re-aggregating, switching visualization, or exporting — none of which mutates the observed data.
- **Side-effect observation.** Transaction observes a committed result and the clearing of the workspace. Report observes only that the presented view updates consistently with the controls and that an export/refresh produces the expected browser-observable artifact.
- **Object ownership.** The browser in Transaction owns the working session until commit. The browser in Report never owns the dataset; it only observes and reshapes its presentation.

### 1.4 Why it differs from Workflow

- **Subject of observation.** Workflow renders a single existing item whose nature is its lifecycle state and the states it can move to. Report renders a *dataset* — many observations arranged and summarized — whose nature is the current filter/scope and the chosen presentation.
- **Mutation shape.** In Workflow, the meaningful browser act is the **transition** that moves an item to a new state. In Report, there is no transition; the meaningful browser act is **reshaping the view** (filter, group, sort, aggregate, visualize, export).
- **Source of structure.** Workflow structure is a status indicator, a timeline/history, and a set of transition controls. Report structure is a filter panel, a presented data region (grid and/or chart), summary/aggregation surfaces, and export controls — organized around *observation*, not *state*.
- **State authority.** Workflow observes a current lifecycle state plus a recorded history that must agree with it. Report observes a loaded dataset plus a derived presentation (grouping, aggregation, visualization) that must agree with the loaded data and the applied controls. There is no lifecycle to advance.

### 1.5 Why Hermes should inherit Report behavior instead of learning every page individually

- **Transfer.** A new observation surface shipped next quarter reuses the same contract. Hermes recognizes it on first encounter from the inherited layer instead of requiring a new knowledge file.
- **Single point of maintenance.** A change to the generic load/filter/search/group/sort/aggregate/compare/visualize/export/refresh contract is made once, here, and benefits every Report page at once.
- **Reasoning consistency.** Generic QA expectations (loading/empty/error states, no duplicated rows, no stale UI, URL/history/refresh consistency, fail-closed on ambiguous state) are applied uniformly because they live in one place.
- **Reduced knowledge weight.** A page-specific file declares only its delta (route, available filters, groupings, aggregations, visualizations, exports, exceptions). The bulk of behavioral knowledge is inherited, not repeated.

---

## 2. Browser Identity

### 2.1 Typical characteristics

A Report page exists to let the browser **observe information**. It loads a dataset, presents it for inspection, and supports exploration through filtering, search, grouping, sorting, aggregation, comparison, and visualization. The page is observation-first: its resting state is a populated (or empty, or error) presented dataset, and every control on the page reshapes the *view* of that data, never the data itself.

### 2.2 Typical interaction model

The page is **observe-first, explore-second**. The user lands on a loaded observation, narrows it with filters and search, reorganizes it with grouping and sorting, derives higher-level views with aggregation and comparison, switches the visualization, inspects details, exports the result, and refreshes to re-synchronize with the source. The page is read-biased: there is no create/edit/delete and no commit; the only outputs are a reshaped on-screen view and a derived export artifact.

### 2.3 Typical browser layout

- An application shell (sidebar/nav) is assumed present; Report is the content surface inside it.
- A **report header** establishing the report's identity (a title and/or scope label).
- A **filter panel** — controls that constrain the observed dataset (scopes such as date range, category, type, status, and any other facets the page exposes).
- A **search** input that narrows the observed dataset by free text.
- A **comparison selector** — controls that place the current observation alongside a comparison baseline (e.g. a prior period or a different scope).
- A **grouping** control that arranges the dataset into groups.
- An **aggregation** control that derives summary values across the dataset or across groups.
- A **sorting** control that reorders rows or groups.
- A **primary presentation surface** — a data grid and/or one or more charts. The definitional element of the archetype.
- **Summary cards** — derived headline values distilled from the loaded dataset.
- A **pivot area** — an optional cross-tabular arrangement of the loaded dataset.
- **Export controls** — affordances that produce a derived file or printout of the current view.
- A **refresh control** — re-loads the dataset and re-synchronizes the presentation.
- **Loading**, **empty**, and **error** states for the relevant regions.

### 2.4 Typical navigation

- Reached via a sidebar/menu link or a dashboard drill-down to a report route.
- The report may be addressable by URL, with the active scope/filter/group/sort/comparison reflected in the query so the exact observed view can be restored on direct entry or reload.
- Some reports are reached through redirect routes that resolve to a canonical viewer; the browser lands on the canonical surface and the Report contract applies there.
- Export typically opens a dialog or triggers a download; it does not navigate away from the report.

### 2.5 Typical user expectations

- The report loads and shows a populated observation (or a clear empty/error state) without a blank or ambiguous screen.
- Filters, search, grouping, sorting, aggregation, and comparison all visibly reshape the presented data and stay consistent with one another.
- Derived values (summary cards, aggregations, chart totals) agree with the rows/groups the browser can count.
- Changing a control does not lose the user's place irrecoverably; clearing filters returns to the full loaded dataset.
- Export reflects the currently observed scope and arrangement; print opens a sensible preview.
- Refresh re-synchronizes the view with the source without changing the user's chosen controls.
- A browser reload restores the same observed view (via URL/query or persisted scope) rather than dropping the user to a default.
- No interaction silently mutates the underlying data or performs an unexpected navigation.

---

## 3. Expected Page Structure

A Report page is composed of a set of UI components. Not every page contains every component. The contract distinguishes **required** from **optional**.

### 3.1 Required components

- **Primary presentation surface** — the data grid and/or visualization that presents the loaded dataset for observation. This is the definitional element of the archetype. If there is no presented dataset (no grid and no chart), the page is not Report.
- **Filter panel** — the controls that constrain the observed dataset by scope/facet. A Report page that presents loaded data almost always offers at least one scope control; without a way to constrain the observation, the page cannot fulfill its exploration purpose.
- **Loading state** — an explicit indicator while the dataset (or a reshaped slice of it) is being fetched or recomputed.
- **Empty state** — an explicit, non-error presentation when the current scope yields no observations, with no crash.
- **Error state** — an explicit, recoverable presentation when the dataset cannot be loaded or recomputed.

### 3.2 Optional components

- **Report header** — the report's title and/or scope label.
- **Search** — a free-text input that narrows the observed dataset. Often debounced.
- **Date selector** — a scope control constraining the observation to a time window (range, single point, or preset).
- **Comparison selector** — controls placing the current observation alongside a comparison baseline.
- **Grouping controls** — arrange the dataset into groups (by a field or facet).
- **Aggregation controls** — derive summary values across the dataset or across groups.
- **Sorting controls** — reorder rows or groups.
- **Data grid** — the tabular presentation of the loaded dataset (columns, rows, pagination).
- **Charts** — the visual presentation(s) of the loaded dataset (the optional visual complement to the grid).
- **Summary cards** — derived headline values distilled from the loaded dataset.
- **Pivot area** — a cross-tabular arrangement of the loaded dataset.
- **Export controls** — produce a derived file (e.g. spreadsheet/document) or printout of the current view.
- **Refresh control** — re-loads the dataset and re-synchronizes the presentation.
- **Tabs** — switch between distinct presentations or sub-views of the same report.
- **Detail drawer / drill-down** — reveal finer detail for a row or group without leaving the report.
- **Loading / empty / error** for sub-regions (e.g. a chart that fails independently of the grid).

### 3.3 Component contracts (browser-observable only)

- The **primary presentation surface** paints the loaded dataset in whatever arrangement the applied controls dictate; it is the browser's source of truth for "what is being observed." Hermes MUST be able to read it and reconcile it against the applied filters, grouping, sorting, and aggregation.
- The **filter panel** applies its constraints to the observed dataset; multiple filters combine (intersection) unless the page documents otherwise. Applying or clearing a filter reshapes the presentation and the derived values together.
- **Summary cards / aggregation** reflect the loaded (and filtered) dataset at all times. They are derived, browser-observable values; Hermes MUST be able to assert that an aggregated value is consistent with the rows/groups the browser can count.
- **Pagination** shows current page, total pages (or count), and page controls; changing page preserves the active filter/search/sort/group scope unless the page explicitly resets it.
- **Loading / empty / error** are mutually exclusive visible states for the relevant region at any moment.
- **Export** opens a dialog or triggers a download that captures the *currently observed* scope and arrangement; it MUST NOT navigate away from the report or mutate the underlying data.
- **Expand / collapse** and **scrolling** are visual only and MUST NOT discard applied filters, search, grouping, sorting, or aggregation state.

---

## 4. Report Lifecycle

This contract describes the canonical lifecycle a browser session takes through a Report surface. It is descriptive of expected behavior, not a script.

```
Landing
   ↓
Load report (fetch and present the dataset)
   ↓
Apply filters (constrain the observed dataset by scope/facet)
   ↓
Search (narrow the observed dataset by free text)
   ↓
Group (arrange the dataset into groups)
   ↓
Sort (reorder rows or groups)
   ↓
Aggregate (derive summary values across the dataset or groups)
   ↓
Compare (place the current observation alongside a comparison baseline)
   ↓
Inspect results (read the grid, charts, summary cards, drill into detail)
   ↓
Export (produce a derived file or printout of the current view)
   ↓
Refresh (re-load the dataset and re-synchronize the presentation)
   ↓
Verify consistency (the view agrees with the loaded data and the applied controls)
```

### 4.1 Notes on the lifecycle

- **Load / Apply filters / Search / Group / Sort / Aggregate / Compare / Inspect** may iterate any number of times in any order; the path above is the *complete* set, not a forced single pass.
- **Export** and **Refresh** are terminal-ish observation actions, not mutations: each produces a browser-observable artifact (a file/preview, or a re-synchronized view) without changing the underlying data.
- **Verify consistency** is the closure that proves the presented view is coherent: the grid, charts, summary cards, grouping, sorting, aggregation, and comparison all agree with the loaded dataset and with the controls the user applied. After refresh, Hermes MUST observe that the re-synchronized view is consistent and that the user's chosen controls are still in effect (or were intentionally reset by the page).

---

## 5. Browser Interaction Contract

The expected interaction model for every Report page. These are behaviors Hermes should be able to exercise and observe.

### 5.1 Loading and constraining

- **Loading data** — entering the report (or activating its load/refresh) fetches and presents the dataset; a loading state covers the presentation until resolution.
- **Filtering** — selecting a filter value constrains the observed dataset; multiple filters combine (intersection) unless the page documents otherwise. Changing a filter reshapes the grid, charts, and derived values together.
- **Searching** — typing into the search input narrows the observed dataset. Behavior may be debounced; a new keystroke may cancel the prior in-flight request. Search combines with active filters.
- **Date selection** — a date scope control constrains the observation to a time window; changing it reshapes the presented dataset and derived values.
- **Comparison selection** — choosing a comparison baseline places the current observation alongside it; the comparison surface updates with the current scope.

### 5.2 Reshaping the view

- **Grouping** — activating a grouping control arranges the dataset into groups; toggling or changing the group field re-partitions the presentation and the derived per-group values.
- **Sorting** — activating a sort control reorders rows or groups; toggling direction is expected where sort is bidirectional.
- **Aggregating** — activating an aggregation control derives summary values across the dataset or across groups; the summary cards / aggregation surface update to match.
- **Expanding sections** — collapsing/expanding grouped regions or detail panels reveals/hides content without discarding applied filters, search, grouping, sorting, or aggregation.
- **Collapsing sections** — same; the action is visual only and MUST NOT reset the observation.
- **Showing details** — opening a detail drawer or drill-down reveals finer detail for a row or group without leaving the report and without changing the dataset.
- **Hiding details** — closing the detail surface returns the browser to the report with all applied controls intact.

### 5.3 Visualization and arrangement

- **Changing visualization** — switching between grid and chart (or between chart types) re-presents the same loaded dataset; the new visualization MUST agree with the grid and with the applied controls.
- **Switching tabs** — changing between sub-views/presentations of the report loads the relevant surface; tab state may persist and MUST restore the correct surface on reload.
- **Scrolling** — the data region and any fixed chrome scroll independently where the layout divides them; scrolling MUST NOT trigger a filter reset or a navigation.
- **Zooming charts** — where a chart supports zoom/pan, the control changes the visible window without altering the underlying dataset or the export scope (unless the page documents zoom-to-export).
- **Pagination** — moving to another page fetches/renders the corresponding slice; the active filter/search/sort/group scope is preserved across page changes.
- **Column resize** — resizing a grid column changes its rendered width without altering data or triggering a reload.
- **Column reorder** — reordering grid columns changes presentation order without altering data or triggering a navigation.

### 5.4 Output and history

- **Export** — invoking an export control opens a dialog or triggers a download capturing the currently observed scope and arrangement; the report remains mounted and unchanged.
- **Refresh** — an in-page refresh control (or a full browser reload) re-loads the dataset and re-synchronizes the presentation; applied controls are restored per the page's URL/query or persistence contract.
- **Browser history** — applying a filter/scope may push a history entry; the page's contract defines which interactions are recorded, and Hermes should record the actual behavior rather than assume.
- **Deep links** — a report (and, where supported, a specific scope/arrangement) may be addressable by URL; opening it directly renders the correct observed view.
- **URL synchronization** — filter/scope/search/group/sort/comparison may be reflected in the URL query; the URL is the source of truth for restoring the observed view on reload.
- **Keyboard navigation** — tab order reaches the filter panel, search, presentation surface, and export/refresh controls; focus is visible and logical; escape closes drawers/dialogs and returns to the report.
- **Focus management** — after changing a control, focus lands on a sensible next control; after closing a detail drawer or export dialog, focus returns to the triggering control.
- **Session restoration** — on reload, the observed view is restored from the URL/query or from persisted scope; Hermes MUST assert the post-reload view is consistent with the pre-reload controls and not silently corrupted or reset.

---

## 6. Browser Assertions

What Hermes should automatically verify on a Report page. These are browser-level observations.

- **Report rendered** — the surface mounts with a usable presentation, not a blank screen or frozen shell.
- **Grid rendered** — where a data grid is the presentation, it paints with the expected structure (headers/columns, rows).
- **Charts rendered** — where charts are the presentation, they paint with visible series/markings (not a blank spinner).
- **Summary rendered** — where summary cards/aggregation exist, they paint derived values consistent with the loaded dataset.
- **Filters applied** — applying a filter narrows the observed dataset; removing it widens it.
- **Search applied** — entering text changes the result set; clearing it restores the broader set.
- **Grouping updated** — activating grouping partitions the presentation; changing the group field re-partitions it.
- **Sorting updated** — activating sort reorders rows or groups in the indicated direction.
- **Aggregation updated** — activating aggregation updates the derived summary values to match the dataset/groups.
- **Comparison updated** — selecting a comparison baseline updates the comparison surface to match the current scope.
- **Export available** — an export control is present and, when invoked, produces a derived file/preview of the current view without navigating away.
- **Refresh updated data** — after refresh, the presentation re-synchronizes with the source and the applied controls remain in effect (or were intentionally reset).
- **Loading state** — a loading indicator shows during load/recompute and clears on completion.
- **Empty state** — a clear empty presentation appears when scope yields nothing, with no crash.
- **Error state** — a recoverable error presentation appears on failure, with a path to retry.
- **Visual consistency** — grid, charts, summary, and controls are coherent at the tested viewport; no overlapping, collapsed, or unstyled regions.
- **Result consistency** — the grid, charts, summary cards, grouping, sorting, aggregation, and comparison all agree with the loaded dataset and the applied controls; no drift between what is shown and what would be derived from the data.
- **No duplicated rows** — the same observation does not appear twice within a page/slice.
- **No stale UI** — the presentation shows post-refresh data after a refresh, with no pre-refresh residue.
- **No console errors** — the browser console shows no uncaught errors during the flow.
- **No JavaScript errors** — no unhandled rejection leaves the page frozen or the report unobservable.
- **Browser responsive** — filter/search/group/sort/aggregate/export/refresh respond within expected bounds; no permanent spinner.

---

## 7. Failure Scenarios

Generic browser failures Hermes should be able to detect on a Report page. These are interaction/rendering failures, not domain errors.

- **Wrong filters** — applying a filter does not constrain the dataset (or constrains it incorrectly), so the presented view does not match the chosen scope.
- **Broken grouping** — grouping fails to partition the presentation, partitions it on the wrong field, or leaves groups inconsistent with the data.
- **Broken sorting** — sorting fails to reorder, reorders in the wrong direction, or sorts on the wrong column.
- **Aggregation mismatch** — a derived summary value does not agree with the rows/groups the browser can count (e.g. a summary that ignores the active filter).
- **Chart not updated** — changing a filter/group/sort/aggregation updates the grid but leaves the chart stale, or vice versa.
- **Grid mismatch** — the grid and the derived values (summary/chart) disagree after a control change.
- **Export failure** — export produces no file, a corrupt file, a file with the wrong scope, or navigates away unexpectedly; or the export dialog fails to open/close.
- **Refresh inconsistency** — a refresh shows different data than the in-page view claimed, or loses the user's applied controls contrary to the page's persistence contract.
- **History inconsistency** — back/forward navigates to an unexpected observed view, or a filter/scope change desyncs with browser history.
- **Layout corruption** — the filter panel, grid, charts, summary, or export controls render misaligned, overflow, or clip content.
- **Rendering issues** — missing headers, collapsed cells, unstyled regions, or truncated controls in the presentation.
- **Broken visualization** — a chart fails to render, renders blank, or renders with missing/duplicated series after a control change.
- **Race conditions** — overlapping filter/search/group/sort/aggregate requests resolve out of order, painting a stale or inconsistent view.
- **Timeout** — a load/recompute request exceeds its bound and the UI has no defined recovery.
- **Retry failure** — a failed load/recompute is not retried, or retrying leaves the UI stuck or produces a duplicated/inconsistent view.
- **Browser freeze** — the page becomes unresponsive during load or recompute (long synchronous render, runaway loop).
- **Unexpected redirect** — applying a filter/scope, exporting, or refreshing navigates away from the report without user intent.
- **Lost browser state** — applied filters/search/group/sort/aggregation reset unexpectedly (e.g. on a minor re-render, tab switch, or reload contrary to the persistence contract).
- **Stale interface** — the presentation shows pre-change or pre-refresh data after a successful control change or refresh.

---

## 8. Browser Evidence Collection

What Hermes should collect when exercising a Report page. Browser evidence only — no backend debugging.

- **Screenshots** — the loaded report, each control state (filtered, grouped, sorted, aggregated, compared), the chart/grid, the empty/error/loading states, the export dialog, and any corruption.
- **Console logs** — all console output, especially errors, warnings, and unhandled rejections during load, recompute, export, and refresh.
- **Network requests** — the load/recompute/export requests with their URLs, params, methods, statuses, and timings (observed from the browser's network surface, not from server internals).
- **DOM state** — the rendered structure of the filter panel, search, presentation surface, summary cards, charts, export controls, and state banners at the moment of interest.
- **Current URL** — the address, including query parameters reflecting scope/filter/search/group/sort/comparison, to prove deep-link and URL-sync behavior.
- **Browser storage** — relevant local/session storage entries that influence the report view (e.g. persisted scope or tab), read from the browser.
- **Grid state** — the actual rows/cells painted, for row-count, duplication, and consistency checks against the derived values.
- **Chart state** — the rendered series/markings and their scope, for consistency checks against the grid and summary.
- **Summary state** — the rendered derived values at the moment of interest, for reconciliation against the grid/chart.
- **Filters** — the exact active filter/search/scope values, to prove they match the presented view.
- **Export dialogs** — whether an export dialog is open, its options, and the resulting file/preview artifact and its scope.
- **Loading indicators** — which spinner/skeleton was active and when it cleared.
- **Timing** — durations for load, recompute, export, and navigation, to detect timeouts and freezes.
- **Browser events** — focus changes, history pushes/pops, tab switches, and visibility/responsiveness signals.

---

## 9. Browser Recovery Behavior

How Hermes should recover from the failures in Section 7, using browser-level actions only.

- **Retry loading** — re-trigger the failed load/recompute via the page's retry control and confirm the view becomes consistent.
- **Refresh** — perform an in-page refresh or a full browser reload to clear a stuck or stale state; then assert the re-synchronized view is consistent and the applied controls are restored per the page contract.
- **Reapply filters** — re-apply the filter/scope values to confirm whether a wrong/missing constraint was transient.
- **Reset filters** — clear search/filter/scope/group/sort back to the full loaded dataset to escape a broken or over-narrow view.
- **Reset grouping** — clear or change the grouping to recover from a broken partition.
- **Reset sorting** — clear or change the sort to recover from a broken order.
- **Reload report** — navigate away and back to the report route to rebuild a clean observation session.
- **Navigate back** — use browser back to escape an unexpected redirect or a broken control flow.
- **Navigate forward** — use browser forward to re-apply an observed view lost by an unintended back.
- **Recover after timeout** — after a load/recompute exceeds its bound, reload or retry and confirm the UI returns to a stable, consistent view.
- **Recover after failed request** — observe the error state, then retry or reload and verify the report becomes consistent with the loaded data.
- **Recover after browser reload** — after a reload, verify the intended observed view (URL/query/persisted scope) is restored and the presentation matches the loaded dataset.
- **Reset browser state** — clear relevant browser storage scope that is corrupting the report view, then reload to a known-good baseline.
- **Restore browser session** — where the page persists scope or tab state, verify it restores correctly after a full browser restart without altering the underlying data or the authoritative observation.

---

## 10. Inheritance Contract

This is the most important section. It defines the inheritance model that makes Report a reusable layer.

### 10.1 The rule

**Every Report page automatically inherits the entire behavior defined in this document.** A future Browser Knowledge file does not re-describe loading, filtering, searching, grouping, sorting, aggregating, comparing, visualizing, exporting, refreshing, the loading/empty/error states, assertions, failure scenarios, evidence collection, or recovery. Those are inherited by referencing the Report archetype by name.

### 10.2 What a page-specific Browser Knowledge file must declare

A page-specific file supplies **only the delta** — what makes this one observation surface different from every other. It declares:

- **Route** — the report route (and any redirect that resolves to it, or sub-routes) for this page.
- **Available filters** — the specific scope/facet controls this report exposes (beyond the generic filter-panel contract), and how they combine.
- **Available groupings** — the specific group fields this report supports.
- **Available aggregations** — the specific derived summary values this report supports.
- **Available visualizations** — the specific presentations this report offers (grid, chart types, pivot, summary cards) and how they relate.
- **Available exports** — the specific export/print artifacts this report produces and their scope.
- **Unique validation rules** — any validation specific to this page's controls that the generic contract does not cover (e.g. a date range that must be bounded).
- **Unique business rules** — any rule that governs *this* page's browser behavior beyond the generic observation contract (e.g. a control surfaced only under a given scope).
- **Unique page exceptions** — any deviation from this archetype (e.g. this report has no grid and is chart-only, or no comparison selector, or persists scope differently).

### 10.3 What a page-specific file MUST NOT declare

**Hermes MUST NOT duplicate inherited behavior inside page-specific Browser Knowledge.** Everything behavioral already in this document — the load/filter/search/group/sort/aggregate/compare/inspect/export/refresh lifecycle, the presentation and derived-value consistency contracts, the loading/empty/error states, the assertion set, the failure catalog, the evidence set, and the recovery set — is inherited by reference. Re-declaring these in a page file is a violation of the inheritance model and causes drift. A page file references the Report archetype by name and adds only its delta.

### 10.4 Inheritance model summary

```
Page-specific Browser Knowledge file
   └─ declares: route,
                available filters,
                available groupings,
                available aggregations,
                available visualizations,
                available exports,
                unique validation rules,
                unique business rules,
                unique page exceptions
   └─ inherits (by archetype reference): ALL of REPORT_ARCHETYPE.md
                load · filter · search · group · sort
                aggregate · compare · visualize · export · refresh
                presentation/derived-value consistency
                states (loading/empty/error)
                assertions · failures · evidence · recovery
```

The class of change *"the filter panel now supports a saved preset"* is made once, in this archetype, and every Report page gains it. The class of change *"this report adds a new aggregation"* is made only in that page's delta.

---

## 11. Scope Boundaries

This document defines **browser interaction behavior**. It explicitly does NOT define the following; those belong to page-specific Browser Knowledge files (or to layers beneath them):

- **Business rules** — domain logic that decides what a valid observation is; this document only expects that such rules are *observable* as filtered/derived presentation in the browser.
- **Permissions** — what a given user may see or do; the archetype expects gated controls to be disabled/hidden but does not define who is gated.
- **Financial calculations** — any monetary, balancing, or settlement math; out of scope for a generic Report contract.
- **Analytics formulas** — any formula that computes a derived metric; out of scope. The archetype expects derived values to be *consistent with the loaded data*, but the formula itself is page-specific.
- **Visualization calculations** — any charting/aggregation math that turns data into a visual; out of scope. The archetype expects the chart to *agree with the grid and the data*, but the calculation is page-specific.
- **Database behavior** — how the dataset is stored or migrated; this document references only the browser-visible effect (data loads, reshapes, or empties).
- **API implementation** — how requests are formed, routed, or authenticated on the server; this document references only the browser-observable request (URL, params, status) as evidence.
- **Server processing** — what the server does to produce the dataset or the derived values; this document observes only the resulting browser presentation.
- **Domain validation** — the specific content rules for a control; the archetype expects observable validation to be *surfaced*, but the rules themselves are page-specific.
- **Entity definitions** — the shape and meaning of the observed records; those are declared per page.

If a page's dominant interaction is a stateful lifecycle (approvals, transitions, side effects), it is a Workflow page. If it is building and committing a working session, it is a Transaction page. If it is managing a collection, it is a CRUD page. A Report page may *inherit CRUD* (to reach a record list that feeds an observation) or sit downstream of another archetype's output — but the observation-and-exploration behavior itself is defined only here.

---

## 12. Stability

This document is intended to remain stable for years and to be extended rarely.

- **Defined by interaction shape, not domain.** Report is expressed entirely in browser behavior (load a dataset, filter, search, group, sort, aggregate, compare, visualize, export, refresh, verify consistency). Domains come and go; the interaction contract does not. A future observation surface — whatever it observes — will still load data, constrain it, reshape its presentation, and export the result.
- **New report pages inherit without modification.** When a new observation surface is added to the application, its Browser Knowledge file references this archetype and supplies only its delta (filters, groupings, aggregations, visualizations, exports, exceptions). This document is not edited for the new page.
- **Only new browser patterns justify change.** This archetype is updated only when a genuinely new report interaction pattern appears that the current contract does not cover (for example, a new universal observation primitive, a new standard comparison lifecycle, or a new standard failure class such as a new export-scope mechanism). Incremental UI polish, new filters, and new report pages do NOT justify editing this document.
- **Drift protection.** Because behavioral knowledge lives here and not in page files, a fix or clarification to the generic Report contract propagates to every page at once. The stable center is this document; the churn happens in the page-specific deltas beneath it.

*End of Report Archetype. Canonical inheritance layer for Hermes Browser Testing.*

---

# Browser Execution Checklist

□ Report rendered
□ Filters verified
□ Search verified
□ Grouping verified
□ Sorting verified
□ Aggregation verified
□ Comparison verified
□ Grid verified
□ Charts verified
□ Export verified
□ Refresh verified
□ Browser history verified
□ Console verified
□ Network verified
□ Evidence collected
□ Recovery verified
