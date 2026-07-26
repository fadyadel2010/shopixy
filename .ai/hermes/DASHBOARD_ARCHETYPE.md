# Dashboard Archetype — Hermes Browser Testing Canonical Contract

**Document Type:** Canonical archetype — inheritance layer for Browser Knowledge
**Status:** Canonical — stable for years
**Layer:** One of the core inheritance layers beneath future `*KNOWLEDGE.yaml` files. This document expands ONLY the Dashboard archetype first identified in `PAGE_ARCHETYPE_DISCOVERY.md`. It does not redefine that discovery, nor does it redefine the CRUD, Transaction, Workflow, Report, or Settings archetypes. Where the six overlap conceptually (loading/empty/error states, URL/history/refresh behavior, deep-link and session-restoration behavior, confirmation posture), Dashboard inherits the *behavioral* expectations and extends them with the situational-awareness-and-navigation model that is unique to dashboard pages.
**Companion Artifact:** The Browser Knowledge generation skill consumes this archetype by name and layers page-specific deltas on top.
**Authority Note:** This document defines *browser interaction behavior only*. It is deliberately independent of any business domain. No entity, field, rule, endpoint, or persistence mechanism is asserted here. A future Browser Knowledge file supplies those; everything behavioral is inherited from this document. Where this document states that a widget presents a derived value or an alert reflects a condition, it states a browser-evidence expectation (the browser must observe the widget's rendered content and, where applicable, its consistency with the rest of the surface), not an implementation detail. A Dashboard may issue a request to fetch widget content, but it MUST NOT mutate any observed record or state. No monetary, quantity, stock, ledger, or domain semantics are implied anywhere in this document.

---

## 1. Purpose

### 1.1 Why Dashboard exists

A large class of application surfaces are not collections the user edits (CRUD), not a working session the user commits (Transaction), not a lifecycle item the user drives (Workflow), not a dataset the user explores (Report), and not a persistent configuration the user edits and saves (Settings). They are **situational-awareness surfaces**: the browser loads a set of independent widgets — summaries, indicators, status badges, charts, feeds, alerts — that together give immediate orientation, and it offers navigation into the deeper surfaces behind each widget.

The defining property is that the page is **monitoring-first**. Its value is in showing current state at a glance and in launching the user toward where action is needed. The interaction contract is therefore different in kind from the other archetypes.

### 1.2 Why it differs from CRUD

- **Mutation intent.** CRUD can create, edit, and delete records. A Dashboard issues no mutations; its widgets only present current state and link onward.
- **Default state.** A CRUD page defaults to a *view of a collection*. A Dashboard defaults to a *loaded grid of widgets* presenting current state.
- **Source of structure.** CRUD structure is a table/cards of uniform records. Dashboard structure is a heterogeneous widget grid — summaries, indicators, charts, feeds, alerts — organized around *what the user needs to see first*, not around row-level add/change/remove.
- **Side-effect observation.** CRUD observes row-level add/change/remove as its purpose. Dashboard observes only that each widget renders its current content and that drill-down links lead where expected.

### 1.3 Why it differs from Transaction

- **Who builds the object.** Transaction assembles a working session from nothing and commits it. A Dashboard loads already-existing widget content; the browser never builds or commits anything.
- **Default state.** A Transaction page defaults to an empty (or recalled) *workspace* awaiting assembly. A Dashboard defaults to a *populated widget grid* immediately on load.
- **The defining act.** In Transaction the defining act is commit. In Dashboard the defining act is *observation and navigation* — reading widgets and drilling down — neither of which mutates state.
- **Side-effect observation.** Transaction observes a committed result and the clearing of the workspace. Dashboard observes only that widgets render current content and that refresh/auto-refresh re-synchronizes them.

### 1.4 Why it differs from Workflow

- **Subject of observation.** Workflow renders a single existing item whose nature is its lifecycle state. A Dashboard renders *many independent widgets*, each a self-contained observation, with no single lifecycle to advance.
- **Mutation shape.** In Workflow, the meaningful browser act is the **transition** that moves an item. In Dashboard, there is no transition; the meaningful acts are *reading widgets* and *drilling down* into deeper surfaces.
- **Source of structure.** Workflow structure is a status indicator, a timeline/history, and transition controls. Dashboard structure is a widget grid of summaries, indicators, charts, feeds, and alerts — organized around *awareness*, not *state*.
- **State authority.** Workflow observes one current lifecycle state plus a history that must agree with it. Dashboard observes many widget contents that each present their own current state; there is no single lifecycle to reconcile.

### 1.5 Why it differs from Report

- **Mutation intent.** Report is observation-only over a single loaded dataset the user explores with filter/group/sort/aggregate/export. Dashboard is monitoring-first over *many independent widgets*, each with its own content and its own drill-down, and it does not offer dataset exploration primitives.
- **Default state.** A Report page defaults to a *loaded observation* presented for inspection and reshaping. A Dashboard defaults to a *loaded widget grid* for immediate orientation.
- **The defining act.** In Report the defining act is *exploration* that reshapes one dataset's view. In Dashboard the defining act is *reading widgets and navigating onward*; there is no single dataset to reshape and no export of the dashboard as a dataset.
- **Output.** Report's outputs are a reshaped on-screen view and a derived export artifact. Dashboard's output is current situational awareness plus navigation into deeper surfaces.

### 1.6 Why it differs from Settings

- **Mutation intent.** Settings is configuration-first: its purpose is to change persistent state via edit-and-save. Dashboard is monitoring-first: its widgets only present current state and never persist a configuration change.
- **Default state.** A Settings page defaults to a *loaded configuration* presented for editing. A Dashboard defaults to a *loaded widget grid* presented for reading.
- **The defining act.** In Settings the defining act is **save/persist**. In Dashboard there is no save; the defining acts are *observing widgets* and *drilling down*. A Dashboard may offer a filter or time-range control, but changing it reshapes the view, not a persisted configuration.
- **Output.** Settings' output is a persisted configuration that survives reload. Dashboard's widget content reflects current state and may be scoped by a transient filter/time range, but the page does not persist a configuration document.

### 1.7 Why Hermes should inherit Dashboard behavior instead of learning every page individually

- **Transfer.** A new situational-awareness surface shipped next quarter reuses the same contract. Hermes recognizes it on first encounter from the inherited layer instead of requiring a new knowledge file.
- **Single point of maintenance.** A change to the generic load-widgets/refresh/auto-refresh/drill-down/alert lifecycle is made once, here, and benefits every Dashboard page at once.
- **Reasoning consistency.** Generic QA expectations (widget loading/empty/error states, no stale widgets, drill-down correctness, auto-refresh consistency, fail-closed on ambiguous state) are applied uniformly because they live in one place.
- **Reduced knowledge weight.** A page-specific file declares only its delta (route, widget definitions, layout, refresh behavior, interactions, filters, drill-down targets, alert behavior, exceptions). The bulk of behavioral knowledge is inherited, not repeated.

---

## 2. Browser Identity

### 2.1 Typical characteristics

A Dashboard page exists to give the browser **immediate situational awareness**. It loads a set of independent widgets that each present a current-state observation, surfaces alerts/notifications, and offers drill-down navigation into the deeper surfaces behind each widget. The page is monitoring-first: its resting state is a populated widget grid, and every control on the page reshapes the *view* (time range, filter) or launches navigation, never the observed data.

### 2.2 Typical interaction model

The page is **observe-first, navigate-second**. The user lands on a loaded widget grid, reads summaries/indicators/status/charts/alerts, narrows the surface with a time range or filter where supported, refreshes (manually or via auto-refresh) to re-synchronize, and drills down from a widget into a deeper surface. The page is read-biased: there is no create/edit/save/commit/transition; the only outputs are current awareness and a navigation into another archetype's surface.

### 2.3 Typical browser layout

- An application shell (sidebar/nav) is assumed present; Dashboard is the content surface inside it.
- A **dashboard header** establishing the surface's identity (a title and/or scope label).
- A **widget grid** — the container arranging the widgets. The definitional element of the archetype.
- **Widget cards** — self-contained panels, each presenting one observation (summary, indicator, chart, feed, alert, status, or quick action).
- **Summary cards** — headline values distilled from current state.
- **Indicators** and **status badges** — at-a-glance signals of a condition.
- **Charts** — visual presentations within widget cards.
- An **activity feed** — a chronological stream of recent events.
- **Notifications** and **alerts** — transient or persistent signals requiring attention.
- **Quick actions** — affordances that launch a common action or navigation.
- A **refresh control** — re-loads widget content and re-synchronizes the surface.
- An **auto-refresh indicator** — shows that widgets refresh on an interval and when the last refresh occurred.
- A **time range selector** and **filter controls** — scope the observed widgets where the page supports them.
- **Drill-down links** — from a widget into a deeper surface (often another archetype's route).
- **Loading**, **empty**, and **error** states for the surface and for individual widgets.

### 2.4 Typical navigation

- Reached at the application index or via a sidebar/menu link to a dashboard route; it is often the landing hub that launches the other archetypes.
- A widget's drill-down link navigates to the deeper surface behind it (a list, a report, a workflow item, a settings surface, or a transaction entry).
- The dashboard may be addressable by URL, with the active time range/filter reflected in the query so the exact observed scope can be restored on direct entry or reload.
- Auto-refresh updates widget content in place without navigating away.

### 2.5 Typical user expectations

- The dashboard loads and shows a populated widget grid (or clear empty/error states) without a blank or ambiguous screen.
- Each widget renders its current content; a widget that cannot load shows its own error/empty state without taking down the whole surface.
- Summaries, indicators, status badges, charts, and alerts all reflect current state and stay consistent with one another where they overlap.
- A time range or filter, where present, reshapes the widgets and stays consistent across them.
- Refresh re-synchronizes widget content; auto-refresh does so on an interval and the indicator reflects the last refresh.
- A drill-down link navigates to the expected deeper surface; returning brings the user back to the dashboard.
- A browser reload restores the same observed scope (via URL/query or persisted scope) rather than dropping the user to a default.
- No interaction silently mutates the underlying data or performs an unexpected navigation.

---

## 3. Expected Page Structure

A Dashboard page is composed of a set of UI components. Not every page contains every component. The contract distinguishes **required** from **optional**.

### 3.1 Required components

- **Widget grid** — the container that arranges the widgets. This is the definitional element of the archetype. If there is no widget grid presenting independent observations, the page is not Dashboard.
- **Loading state** — an explicit indicator while the dashboard (or individual widgets) is being loaded.
- **Error state** — an explicit, recoverable presentation when the dashboard or a widget cannot be loaded.

### 3.2 Optional components

- **Dashboard header** — the surface's title and/or scope label.
- **Widget cards** — self-contained panels each presenting one observation.
- **Summary cards** — headline values distilled from current state.
- **Indicators** — at-a-glance numeric or qualitative signals.
- **Status badges** — signals of a condition's state.
- **Charts** — visual presentations within widget cards.
- **Activity feed** — a chronological stream of recent events.
- **Notifications** — transient or persistent messages shown on the surface.
- **Alerts** — signals requiring attention, often with a severity and a drill-down.
- **Quick actions** — affordances that launch a common action or navigation.
- **Refresh control** — re-loads widget content and re-synchronizes the surface.
- **Auto-refresh indicator** — shows that widgets refresh on an interval and when the last refresh occurred.
- **Time range selector** — scopes the observed widgets to a time window where the page supports it.
- **Filter controls** — constrain the observed widgets by scope/facet where the page supports them.
- **Drill-down links** — from a widget into a deeper surface.
- **Empty state** — an explicit presentation when a widget (or the surface) yields no content, distinct from error.
- **Widget detail / expansion** — reveal finer detail for a widget without leaving the dashboard.

### 3.3 Component contracts (browser-observable only)

- The **widget grid** paints the widgets in whatever arrangement the page defines; it is the browser's source of truth for "what is being observed." Hermes MUST be able to read each widget's rendered content and reconcile overlapping widgets with one another.
- Each **widget card** presents its own current-state observation independently; a widget that fails to load MUST show its own error/empty state without preventing the other widgets from rendering.
- **Summary cards / indicators / status badges** reflect current state at the moment of load/refresh; Hermes MUST be able to assert that overlapping widgets agree where they present the same observation.
- **Alerts** present a condition with a severity and (where supported) a drill-down; the browser MUST be able to read the alert's content and assert it is visible when the condition it reflects is present.
- **Refresh / auto-refresh** re-synchronizes widget content in place; auto-refresh MUST NOT navigate away or mutate underlying data, and the auto-refresh indicator MUST reflect the last refresh time.
- **Drill-down links** navigate to the deeper surface behind the widget; they MUST NOT mutate state and MUST leave a history entry the user can return from.
- **Loading / empty / error** are mutually exclusive visible states for a given widget (and for the surface) at any moment.
- **Expand / collapse** and **scrolling** are visual only and MUST NOT discard the observed scope (time range/filter) or pending drill-down context.

---

## 4. Dashboard Lifecycle

This contract describes the canonical lifecycle a browser session takes through a Dashboard surface. It is descriptive of expected behavior, not a script.

```
Landing
   ↓
Load dashboard (fetch and present the widget grid)
   ↓
Load widgets (each widget presents its current-state observation)
   ↓
Observe status (read status badges / indicators)
   ↓
Observe summaries (read summary cards / headline values)
   ↓
Observe indicators (read at-a-glance signals)
   ↓
Observe alerts (read visible alerts / notifications)
   ↓
Refresh (re-load widget content; manual or via auto-refresh)
   ↓
Auto-refresh (widgets re-synchronize on interval)
   ↓
Drill down (navigate from a widget into a deeper surface)
   ↓
Return (navigate back to the dashboard)
   ↓
Verify consistency (widgets agree; alerts reflect conditions; scope persists)
```

### 4.1 Notes on the lifecycle

- **Load / Observe status / summaries / indicators / alerts** may iterate any number of times as the user scans the surface; the path above is the *complete* set, not a forced single pass.
- **Refresh / Auto-refresh** are re-synchronization actions, not mutations: each updates widget content in place without changing the underlying data or navigating away.
- **Drill down** is a navigation into another archetype's surface (a list, report, workflow item, settings surface, or transaction entry); it MUST leave a history entry so the user can return.
- **Return** proves the dashboard is still mounted and that the observed scope (time range/filter) is intact after the navigation.
- **Verify consistency** is the closure that proves the surface is coherent: widgets agree where they overlap, alerts are visible when their conditions hold, and the observed scope persists across refresh and navigation.

---

## 5. Browser Interaction Contract

The expected interaction model for every Dashboard page. These are behaviors Hermes should be able to exercise and observe.

### 5.1 Loading and re-synchronizing

- **Loading widgets** — entering the dashboard (or activating its refresh) fetches and presents the widget content; a loading state covers each widget (or the surface) until resolution.
- **Refreshing** — an in-page refresh control re-loads widget content and re-synchronizes the surface; the observed scope (time range/filter) is preserved.
- **Auto-refresh** — where the page defines an interval, widgets re-synchronize automatically; the auto-refresh indicator MUST reflect the last refresh time and the surface MUST NOT navigate away or mutate data.
- **Changing time range** — a time range selector scopes the observed widgets to a window; changing it re-synchronizes the widgets and the scope persists across them.
- **Filtering** — where the page supports it, a filter control constrains the observed widgets by scope/facet; multiple filters combine (intersection) unless the page documents otherwise.

### 5.2 Arranging and inspecting

- **Expanding widgets** — expanding a widget reveals finer detail without leaving the dashboard and without discarding the observed scope.
- **Collapsing widgets** — collapsing hides detail; the action is visual only and MUST NOT reset the observed scope or pending drill-down context.
- **Opening widget details** — where a widget offers a detail surface, opening it reveals finer content without navigating away.
- **Widget resizing** — where supported, resizing a widget changes its rendered size without altering its content or triggering a navigation.
- **Widget rearranging** — where supported, rearranging widgets changes their order/position without altering content or navigating away.
- **Scrolling** — the widget grid scrolls independently of fixed chrome where the layout divides them; scrolling MUST NOT trigger a refresh or a navigation.

### 5.3 Navigating

- **Searching widgets** — where the page supports it, a free-text query filters the visible widgets without altering any widget's content.
- **Navigating** — reaching the dashboard via its route or the application index; the widget grid is the landing surface.
- **Drill-down** — invoking a widget's drill-down link navigates to the deeper surface behind it; the browser MUST observe the target route and that a history entry was left.
- **Returning** — browser back (or an in-page back control) returns the browser to the dashboard with the observed scope intact.
- **Keyboard navigation** — tab order reaches the header, widgets, refresh/auto-refresh controls, and drill-down links; focus is visible and logical; escape closes widget detail/expansion and returns to the grid.
- **Focus management** — after drilling down and returning, focus returns to a sensible control (the originating widget or the grid); after expanding a widget, focus lands in its detail surface.
- **Browser history** — a drill-down pushes a history entry; the page's contract defines which other interactions are recorded, and Hermes should record the actual behavior rather than assume.
- **Deep links** — the dashboard (and, where supported, a specific scope) may be addressable by URL; opening it directly renders the correct observed view.
- **URL synchronization** — the active time range/filter may be reflected in the URL query; the URL is the source of truth for restoring the observed scope on reload.
- **Session restoration** — on reload, the observed scope is restored from the URL/query or from persisted scope; Hermes MUST assert the post-reload scope is consistent with the pre-reload controls and the widget grid is intact.

---

## 6. Browser Assertions

What Hermes should automatically verify on a Dashboard page. These are browser-level observations.

- **Dashboard rendered** — the surface mounts with a usable widget grid, not a blank screen or frozen shell.
- **Widgets rendered** — each widget card paints its current-state observation with the expected structure.
- **Charts rendered** — where a widget contains a chart, it paints with visible series/markings (not a blank spinner).
- **Summary visible** — summary cards present headline values derived from current state.
- **Indicators visible** — indicators and status badges present their signals and are readable.
- **Alerts visible** — alerts/notifications that reflect a present condition are visible; the browser MUST be able to read their content and severity.
- **Notifications visible** — transient or persistent notifications render where the page presents them.
- **Widget loading** — a loading indicator shows for a widget (or the surface) during load/re-sync and clears on completion.
- **Widget error** — a widget that cannot load shows its own error/empty state without blocking the other widgets.
- **Refresh updated widgets** — after manual refresh, widget content re-synchronizes and the observed scope persists.
- **Auto-refresh verified** — where defined, widgets re-synchronize on interval; the auto-refresh indicator reflects the last refresh and no navigation/mutation occurred.
- **Drill-down verified** — a widget's drill-down link navigates to the expected deeper surface and leaves a returnable history entry.
- **Return verified** — returning from a drill-down lands back on the dashboard with the observed scope intact.
- **Loading state** — a loading indicator shows during load/re-sync and clears on completion.
- **Empty state** — a clear empty presentation appears for a widget (or the surface) that yields no content, with no crash.
- **Error state** — a recoverable error presentation appears on load failure, with a path to retry.
- **No stale widgets** — widgets show post-refresh content after a refresh, with no pre-refresh residue; auto-refresh does not leave partially-stale widgets.
- **No console errors** — the browser console shows no uncaught errors during the flow.
- **No JavaScript errors** — no unhandled rejection leaves the page frozen or the dashboard unobservable.
- **Browser responsive** — load, refresh, auto-refresh, and drill-down respond within expected bounds; no permanent spinner.
- **Visual consistency** — the widget grid, headers, alerts, and controls are coherent at the tested viewport; no overlapping, collapsed, or unstyled regions.
- **Dashboard consistency** — overlapping widgets agree where they present the same observation; alerts are visible when their conditions hold; the observed scope is consistent across widgets and across refresh/navigation.

---

## 7. Failure Scenarios

Generic browser failures Hermes should be able to detect on a Dashboard page. These are interaction/rendering failures, not domain errors.

- **Missing widgets** — a widget that should be present fails to render, leaving a gap in the grid.
- **Broken widget** — a widget renders but with missing/garbled content, a broken chart, or unstyled regions.
- **Broken refresh** — refresh does not re-synchronize widget content or loses the observed scope.
- **Broken auto-refresh** — auto-refresh does not fire, fires but leaves stale widgets, navigates away, or mutates underlying data; or the indicator does not update.
- **Wrong indicator** — an indicator or status badge shows a signal that does not match current state (e.g. a stale or mis-rendered badge).
- **Alert mismatch** — an alert is missing when its condition holds, or present when it does not; or its severity/drill-down is wrong.
- **Widget mismatch** — overlapping widgets disagree where they should present the same observation.
- **Broken drill-down** — a widget's drill-down link lands on the wrong route, a blank page, or an error, or fails to leave a returnable history entry.
- **Broken return** — returning from a drill-down does not land back on the dashboard, or loses the observed scope.
- **History inconsistency** — back/forward navigates to an unexpected surface, or a drill-down desyncs with browser history.
- **Layout corruption** — the widget grid, headers, alerts, or controls render misaligned, overflow, or clip content.
- **Rendering issues** — missing widget titles, collapsed cards, unstyled regions, or truncated controls.
- **Race conditions** — overlapping widget loads or refresh/auto-refresh cycles resolve out of order, painting a stale or inconsistent grid.
- **Timeout** — a widget or surface load exceeds its bound and the UI has no defined recovery.
- **Retry failure** — a failed widget/surface load is not retried, or retrying leaves the UI stuck or produces an inconsistent grid.
- **Browser freeze** — the page becomes unresponsive during load or auto-refresh (long synchronous render, runaway loop).
- **Unexpected redirect** — refresh, auto-refresh, or a widget interaction navigates away from the dashboard without user intent.
- **Stale dashboard** — the grid shows pre-refresh content after a refresh or auto-refresh cycle.
- **Lost widget state** — the observed scope (time range/filter) or widget arrangement resets unexpectedly (e.g. on a minor re-render, drill-down, or reload contrary to the persistence contract).

---

## 8. Browser Evidence Collection

What Hermes should collect when exercising a Dashboard page. Browser evidence only — no backend debugging.

- **Screenshots** — the loaded widget grid, each widget state (loaded, empty, error), the alerts/notifications, the drill-down target, the auto-refresh indicator, and any corruption.
- **Console logs** — all console output, especially errors, warnings, and unhandled rejections during load, refresh, auto-refresh, and drill-down.
- **Network requests** — the dashboard and per-widget load/refresh requests with their URLs, methods, params, statuses, and timings (observed from the browser's network surface, not from server internals).
- **DOM state** — the rendered structure of the widget grid, each widget card, alerts, notifications, refresh/auto-refresh controls, and state banners at the moment of interest.
- **Current URL** — the address, including query parameters reflecting time range/filter, to prove deep-link and URL-sync behavior.
- **Browser storage** — relevant local/session storage entries that influence the dashboard view (e.g. persisted scope or widget arrangement), read from the browser.
- **Widget state** — the rendered content of each widget at the moment of interest, for content and consistency checks.
- **Alert state** — the rendered alerts/notifications, their content, severity, and drill-down, for visibility and mismatch checks.
- **Notification state** — transient or persistent notifications shown on the surface.
- **Dashboard state** — the overall observed scope (time range/filter) and widget arrangement, for persistence and restoration checks.
- **Loading indicators** — which spinner/skeleton was active (per widget or surface) and when it cleared.
- **Timing** — durations for load, refresh, auto-refresh cycle, drill-down, and navigation, to detect timeouts and freezes.
- **Browser events** — focus changes, history pushes/pops, auto-refresh ticks, and visibility/responsiveness signals.

---

## 9. Browser Recovery Behavior

How Hermes should recover from the failures in Section 7, using browser-level actions only.

- **Retry widget** — re-trigger a failed widget load via its retry control; the widget MUST recover to a consistent state without blocking the others.
- **Retry dashboard** — re-trigger a failed surface load via the page's retry control and confirm the widget grid becomes consistent.
- **Refresh** — perform an in-page refresh or a full browser reload to clear a stuck or stale state; then assert the widgets re-synchronize and the observed scope is restored per the page contract.
- **Reload** — navigate away and back to the dashboard route to rebuild a clean observation session.
- **Recover after timeout** — after a widget or surface load exceeds its bound, reload or retry and confirm the UI returns to a stable, consistent grid.
- **Recover after failed request** — observe the error state, then retry or reload and verify the widget(s) return to a consistent state.
- **Reset widget state** — clear a corrupted widget arrangement or expand/collapse state, then reload to a known-good baseline.
- **Reset dashboard state** — clear relevant browser storage that is corrupting the dashboard view (e.g. a stale scope or arrangement), then reload.
- **Navigate back** — use browser back to escape an unexpected redirect or broken drill-down flow.
- **Navigate forward** — use browser forward to re-apply a dashboard state lost by an unintended back.
- **Restore browser session** — where the page persists scope or arrangement, verify they restore correctly after a full browser restart without altering the observed data.
- **Reset browser state** — clear relevant browser storage that is corrupting the view, then reload to a known-good baseline.

---

## 10. Inheritance Contract

This is the most important section. It defines the inheritance model that makes Dashboard a reusable layer.

### 10.1 The rule

**Every Dashboard page automatically inherits the entire behavior defined in this document.** A future Browser Knowledge file does not re-describe loading widgets, observing status/summaries/indicators/alerts, refresh, auto-refresh, drill-down/return, the widget loading/empty/error states, assertions, failure scenarios, evidence collection, or recovery. Those are inherited by referencing the Dashboard archetype by name.

### 10.2 What a page-specific Browser Knowledge file must declare

A page-specific file supplies **only the delta** — what makes this one situational-awareness surface different from every other. It declares:

- **Route** — the dashboard route (and any redirect that resolves to it) for this page.
- **Widget definitions** — the specific widgets this dashboard presents (summary, indicator, status, chart, feed, alert, quick action, and others), beyond the generic widget-grid contract.
- **Widget layout** — how the widgets are arranged in the grid (order, grouping, sizing).
- **Widget refresh behavior** — how each widget (and the surface) refreshes, whether auto-refresh is defined, and its interval.
- **Widget interactions** — the interactions each widget supports (expand, detail, resize, rearrange, drill-down).
- **Available filters** — the specific time range / scope / facet controls this dashboard exposes, and how they combine.
- **Available drill-down targets** — the deeper surfaces each widget's drill-down navigates to.
- **Alert behavior** — how alerts/notifications are presented, their severity model, and where each drills down.
- **Unique validation rules** — any validation specific to this page's controls (e.g. a time range that must be bounded) that the generic contract does not cover.
- **Unique business rules** — any rule that governs *this* page's browser behavior beyond the generic monitoring contract (e.g. a widget surfaced only under a given scope).
- **Unique page exceptions** — any deviation from this archetype (e.g. this dashboard has no auto-refresh, or no filters, or a fixed (non-rearrangeable) layout).

### 10.3 What a page-specific file MUST NOT declare

**Hermes MUST NOT duplicate inherited behavior inside page-specific Browser Knowledge.** Everything behavioral already in this document — the load/observe/refresh/auto-refresh/drill-down/return lifecycle, the widget and alert-visibility contracts, the loading/empty/error states, the assertion set, the failure catalog, the evidence set, and the recovery set — is inherited by reference. Re-declaring these in a page file is a violation of the inheritance model and causes drift. A page file references the Dashboard archetype by name and adds only its delta.

### 10.4 Inheritance model summary

```
Page-specific Browser Knowledge file
   └─ declares: route,
                widget definitions,
                widget layout,
                widget refresh behavior,
                widget interactions,
                available filters,
                available drill-down targets,
                alert behavior,
                unique validation rules,
                unique business rules,
                unique page exceptions
   └─ inherits (by archetype reference): ALL of DASHBOARD_ARCHETYPE.md
                load widgets · observe status/summaries
                indicators · alerts · notifications
                refresh · auto-refresh · drill-down · return
                widget/alert visibility contracts
                states (loading/empty/error)
                assertions · failures · evidence · recovery
```

The class of change *"widgets now support a sticky/expanded detail drawer"* is made once, in this archetype, and every Dashboard page gains it. The class of change *"this dashboard adds a new widget"* is made only in that page's delta.

---

## 11. Scope Boundaries

This document defines **browser interaction behavior**. It explicitly does NOT define the following; those belong to page-specific Browser Knowledge files (or to layers beneath them):

- **Business rules** — domain logic that decides what a valid observation is; this document only expects that such rules are *observable* as widget content in the browser.
- **Permissions** — what a given user may see or do; the archetype expects gated widgets/controls to be disabled/hidden but does not define who is gated.
- **Widget calculations** — any formula that computes a widget's value; out of scope. The archetype expects a widget to *present* its content consistently, but the calculation is page-specific.
- **Alert logic** — the decision of when a condition raises an alert; out of scope. The archetype expects an alert to be *visible* when its condition holds, but the logic is page-specific.
- **Metric calculations** — any metric/aggregation math behind an indicator or summary; out of scope.
- **Visualization calculations** — any charting math that turns data into a visual; out of scope. The archetype expects the chart to *render*, but the calculation is page-specific.
- **Database behavior** — how widget content is stored or migrated; this document references only the browser-visible effect (widgets load, re-synchronize, or error).
- **API implementation** — how requests are formed, routed, or authenticated on the server; this document references only the browser-observable request (URL, params, status) as evidence.
- **Server processing** — what the server does to produce widget content; this document observes only the resulting browser presentation.
- **Entity definitions** — the shape and meaning of the observed records; those are declared per page.

If a page's dominant interaction is managing a collection, it is a CRUD page. If it assembles and commits a working session, it is a Transaction page. If it observes a lifecycle, it is a Workflow page. If it primarily explores a loaded dataset, it is a Report page. If it configures persistent behavior, it is a Settings page. A Dashboard page may launch into any of those via drill-down — but the situational-awareness-and-navigation behavior itself is defined only here.

---

## 12. Stability

This document is intended to remain stable for years and to be extended rarely.

- **Defined by interaction shape, not domain.** Dashboard is expressed entirely in browser behavior (load a widget grid, observe status/summaries/indicators/alerts, refresh, auto-refresh, drill down, return, verify consistency). Domains come and go; the interaction contract does not. A future situational-awareness surface — whatever it monitors — will still load widgets, surface alerts, and offer drill-down into deeper surfaces.
- **New dashboard pages inherit without modification.** When a new situational-awareness surface is added to the application, its Browser Knowledge file references this archetype and supplies only its delta (widget definitions, layout, refresh behavior, interactions, filters, drill-down targets, alert behavior, exceptions). This document is not edited for the new page.
- **Only new browser patterns justify change.** This archetype is updated only when a genuinely new dashboard interaction pattern appears that the current contract does not cover (for example, a new universal widget primitive, a new standard auto-refresh lifecycle, a new standard alert model, or a new standard failure class such as a new widget-level retry mechanism). Incremental UI polish, new widgets, and new dashboard pages do NOT justify editing this document.
- **Drift protection.** Because behavioral knowledge lives here and not in page files, a fix or clarification to the generic Dashboard contract propagates to every page at once. The stable center is this document; the churn happens in the page-specific deltas beneath it.

*End of Dashboard Archetype. Canonical inheritance layer for Hermes Browser Testing.*

---

# Browser Execution Checklist

□ Dashboard rendered
□ Widgets rendered
□ Summary verified
□ Indicators verified
□ Alerts verified
□ Refresh verified
□ Auto-refresh verified
□ Drill-down verified
□ Return verified
□ Loading verified
□ Error verified
□ Browser history verified
□ Console verified
□ Network verified
□ Evidence collected
□ Recovery verified