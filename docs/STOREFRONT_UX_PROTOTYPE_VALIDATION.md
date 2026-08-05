# Storefront UX Prototype Validation

- **Project:** PointOfSale / Shopixy tenant Storefront Administration
- **Date:** 2026-08-04
- **Validation type:** Role and task simulation against the constitutional UX prototype
- **Implementation validation:** Out of scope
- **Application source changes:** None
- **Final certification:** **READY WITH OBSERVATIONS**

---

## Executive Summary

The proposed Storefront UX is usable by a real merchant and is sufficiently defined for migration phase M1 to begin. It presents a coherent top-level mental model: understand the Storefront, operate its availability, compose bounded pages, manage content and navigation, control presentation, and recover StoreConfig history. The proposed information architecture is materially easier to predict than the current menu because it exposes Storefront Status, exposes Product Template, separates Themes from Colors & Typography, names StoreConfig history precisely, and removes Category Template from the authoring model. [BP §2.1–2.3, §6.1–6.7; UXA Executive Summary, Findings F-02/F-05/F-06/F-09]

The target is not uniformly easy. Four workflows remain cognitively hard even when their click counts are modest:

1. **Navigation publication** because Header and Footer Links are separate views of one complete draft and one publish transition.
2. **Footer Content** because it is immediate live, has no persisted draft/preview/history, and remains adjacent to transitional legacy footer-link ownership until M4.
3. **StoreConfig rollback** because one action can restore several StoreConfig-owned live domains while excluding CMS pages, Footer SQL, and operational status.
4. **Storefront seed** because one action creates missing defaults across CMS, Navigation, Homepage, and Footer, requires an existing StoreConfig, and deliberately refuses to overwrite customized state.

[BP §3 C07/C08/C11, §5.3–5.4, §5.10–5.11, §7.2; SEED §Execution Pipeline/Idempotency Strategy; NAV-RUNTIME §Complete Runtime Call Graph]

The target has strong failure and recovery principles. Dirty candidates are protected; successful responses replace client assumptions; backend-issued versions are mandatory; structured errors govern recovery; Navigation can load a broken link for repair while save/publish remain strict; CMS publish conflicts adopt authoritative detail state; and Footer conflicts retain the draft and offer deliberate recovery. [BP §1.7–1.8, §1.17, §7.3–7.6, Laws 8–10/32; HARDENING §Authoring UX Recovery; NAV-RESILIENCY §Call Graph; CMS-PUBLISH §Source-Backed Call Graph]

### Certification decision

**READY WITH OBSERVATIONS**

- **M1 may begin:** yes, after the M1 prerequisites in this report are accepted.
- **Architecture blocker to starting M1:** none. M1 is a menu, naming, and reachability change with no permitted runtime ownership change. [BP §9.1 M1]
- **Blocker to claiming the whole UX migration complete:** yes. M2 workflow alignment, M3 theme-activation lifecycle alignment, and M4 footer-link ownership cutover remain separately gated. [BP §9.1 M2–M4, §13.3]
- **Role-model observation:** Merchant, Store Manager, Content Editor, Marketing Manager, Operations Admin, and Support Agent are validation personas, not six certified backend roles. Current tenant authoring mutations share the `Owner,Admin` backend boundary. The prototype must not imply finer permissions until those permissions are separately certified. [BP §3 introduction, Law 31]
- **Recovery observation:** an absent StoreConfig is not merchant-recoverable through Seed. Seed correctly fails with `CMS_SEED_STORE_CONFIG_REQUIRED`; no certified ordinary UX creates StoreConfig as a side effect. [SEED §StoreConfig Contract, §Error Contract; BP C05/C11]

---

## Validation Basis and Method

### Constitutional basis

The Blueprint is treated as constitutional. Earlier `FAIL` certifications describe historical defects; certified recovery records and post-recovery alignments supersede them. The final architecture certification freezes Runtime, Theme, Builder, Projection, Cache, and data-authority ownership. [BP §0.2–0.4; FAC Executive Verdict/Frozen Inputs]

Source abbreviations used in this report:

| Abbreviation | Source |
|---|---|
| BP | [`STOREFRONT_CANONICAL_UX_BLUEPRINT.md`](STOREFRONT_CANONICAL_UX_BLUEPRINT.md) |
| UXA | [`STOREFRONT_UX_ARCHITECTURE_AUDIT.md`](STOREFRONT_UX_ARCHITECTURE_AUDIT.md) |
| FAC | [`FINAL_STOREFRONT_ARCHITECTURE_CERTIFICATION.md`](FINAL_STOREFRONT_ARCHITECTURE_CERTIFICATION.md) |
| RUNTIME-CERT | [`RUNTIME_OWNERSHIP_CERTIFICATION.md`](RUNTIME_OWNERSHIP_CERTIFICATION.md) |
| THEME-CERT | [`THEME_RUNTIME_CERTIFICATION.md`](THEME_RUNTIME_CERTIFICATION.md) |
| BUILDER-CERT | [`BUILDER_RUNTIME_CERTIFICATION.md`](BUILDER_RUNTIME_CERTIFICATION.md) |
| CACHE-CERT | [`CACHE_RUNTIME_CERTIFICATION.md`](CACHE_RUNTIME_CERTIFICATION.md) |
| CMS-CERT | [`STOREFRONT_CMS_AUTHORING_CERTIFICATION.md`](STOREFRONT_CMS_AUTHORING_CERTIFICATION.md) |
| RUNTIME-R | [`RECOVERY-1_RUNTIME_OWNERSHIP_IMPLEMENTATION.md`](RECOVERY-1_RUNTIME_OWNERSHIP_IMPLEMENTATION.md) |
| THEME-R | [`RECOVERY-2_THEME_RUNTIME_IMPLEMENTATION.md`](RECOVERY-2_THEME_RUNTIME_IMPLEMENTATION.md) |
| BUILDER-R | [`RECOVERY-3_BUILDER_RUNTIME_IMPLEMENTATION.md`](RECOVERY-3_BUILDER_RUNTIME_IMPLEMENTATION.md) |
| BROWSER-CACHE-R | [`RECOVERY-4_BROWSER_RUNTIME_CACHE_IMPLEMENTATION.md`](RECOVERY-4_BROWSER_RUNTIME_CACHE_IMPLEMENTATION.md) |
| CACHE-R | [`RECOVERY-5_CACHE_CORE_AND_CLEANUP_IMPLEMENTATION.md`](RECOVERY-5_CACHE_CORE_AND_CLEANUP_IMPLEMENTATION.md) |
| CMS-VERSION | [`RECOVERY-0_MARKETPLACE_CMS_VERSION_CONTRACT.md`](RECOVERY-0_MARKETPLACE_CMS_VERSION_CONTRACT.md) |
| HOME-R | [`RECOVERY-1_HOMEPAGE_AUTHORING_IMPLEMENTATION.md`](RECOVERY-1_HOMEPAGE_AUTHORING_IMPLEMENTATION.md) |
| NAV-R | [`RECOVERY-2_NAVIGATION_AUTHORING_IMPLEMENTATION.md`](RECOVERY-2_NAVIGATION_AUTHORING_IMPLEMENTATION.md) |
| SEED | [`RECOVERY-3_CMS_SEED_RUNTIME_IMPLEMENTATION.md`](RECOVERY-3_CMS_SEED_RUNTIME_IMPLEMENTATION.md) |
| HARDENING | [`RECOVERY-4_5_AUTHORING_FINAL_HARDENING.md`](RECOVERY-4_5_AUTHORING_FINAL_HARDENING.md) |
| CONTRACT | [`POST_RECOVERY_CONTRACT_ALIGNMENT.md`](POST_RECOVERY_CONTRACT_ALIGNMENT.md) |
| NAV-RUNTIME | [`POST_CERTIFICATION_NAVIGATION_RUNTIME_ALIGNMENT.md`](POST_CERTIFICATION_NAVIGATION_RUNTIME_ALIGNMENT.md) |
| NAV-RESILIENCY | [`POST_AUTHORING_NAVIGATION_RESILIENCY_RECOVERY.md`](POST_AUTHORING_NAVIGATION_RESILIENCY_RECOVERY.md) |
| CMS-PUBLISH | [`POST_RECOVERY_CMS_EDITOR_PUBLISH_ALIGNMENT.md`](POST_RECOVERY_CMS_EDITOR_PUBLISH_ALIGNMENT.md) |

All listed sources were read in full. Findings below cite the constitutional target or a certified architecture/recovery record; no feature is inferred from an unused DTO, dormant compatibility field, or historical implementation path. [BP §0.2–0.3, Law 30]

### Measurement rules

The prototype has no interactive click telemetry, so the quantitative results are deterministic path counts derived from the canonical screen and journey contracts.

- **Click:** one deliberate activation of a navigation item, tab/view, primary action, row/version, or confirmation. Field focus, typing, drag distance, and repeated content-entry clicks are excluded.
- **Sidebar baseline:** the Storefront group is available and the required organizational parent is already expanded. Add one click when `Catalog`, `Content`, `Navigation`, or `Appearance` is collapsed. A stable deep link removes the initial navigation click.
- **Screen:** a distinct canonical route-level responsibility C01–C11. A dialog or a Header/Footer Navigation view is not counted as another screen. A signed real-Storefront preview is counted as a second viewing surface.
- **Decision:** a user choice that changes scope, lifecycle, target, version, or live impact. Routine field entry is not counted.
- **Confusion opportunity:** a source-backed point where terminology, lifecycle, scope, ownership, or expected location is plausibly misread. It is not a defect count.
- **Context switch:** a transition to another authority or viewing context, including a signed Storefront preview or a command whose disclosed impact crosses several domain concepts.
- **Difficulty:** `Easy` means one owner and one obvious state transition; `Medium` means the path is short but requires lifecycle or scope knowledge; `Hard` means multiple authorities, versions, or irreversible/immediate-live implications must be understood.

[BP §1.1–1.18, §3, §5–§7]

---

## Role Analysis

The six roles below are personas used to test intent and comprehension. They do not grant permissions. Counts use one representative high-value task per persona so the numbers remain comparable and auditable.

| Persona | What they are trying to accomplish | Representative canonical journey | Clicks | Screens | Decisions | Confusion opportunities | Ownership/permission result | Source |
|---|---|---:|---:|---:|---:|---:|---|---|
| Merchant | Make the public Homepage ready and publish the visible composition | Publish Homepage from an accepted draft | 3 | 1 | 3 | 2 | Clear Builder/StoreConfig ownership if the action says `Publish Homepage`; mutation requires Owner/Admin | BP C03, §5.1, §7.2; BUILDER-R Final Certification |
| Store Manager | Temporarily stop ordinary public use while communicating maintenance | Enable Maintenance Mode | 3 | 1 | 2 | 1 | Clear StoreConfig operational owner; immediate-live effect must be stated; mutation requires Owner/Admin | BP C02, §7.2, Laws 12/18 |
| Content Editor | Create, save, and publish one informational CMS page | Create + Publish CMS Page | 5 | 2 | 4 | 2 | CMS content ownership is clear; Navigation placement is intentionally separate; named role has no certified distinct permission | BP C05/C06, §5.2; HARDENING §CMS pages |
| Marketing Manager | Change the Storefront visual system without changing runtime behavior | Edit and publish Colors & Typography | 5 | 1 | 4 | 3 | Theme-token owner is clear after `Appearance` grouping; users must distinguish tokens from theme identity; named role has no certified distinct permission | BP C09/C10, §5.5–5.6; THEME-R Ownership Matrix |
| Operations Admin | Restore a known StoreConfig live snapshot after an unsafe publication | Recover StoreConfig | 5 | 1 | 5 | Scope is explicit but mentally heavy; CMS, Footer, and status exclusions must be visible before confirmation | BP C11, §5.10–5.11, §7.2 |
| Support Agent | Diagnose and repair a link whose CMS target no longer exists | Repair Broken Link | 6 | 1 | 4 | Link-level repair is clear; content restoration remains with CMS; named role has no certified mutation permission | BP C07, §5.3, §7.6; NAV-RESILIENCY Executive Summary/Call Graph |

### Role findings

1. **Merchant:** The Homepage task is discoverable and bounded, but “Create Homepage” is not a real entity lifecycle. The merchant edits the one Homepage workspace; they create sections, not a second Homepage. Calling the action “Create Homepage” would introduce an unsupported concept. [BP C03, §6.2, Law 35]
2. **Store Manager:** Storefront Status is the clearest operational task in the target because it is directly reachable, one-screen, and immediate live. It becomes confusing only if it is hidden under generic Settings or if Save does not state public impact. [BP C02, §2.2, Laws 12/18; UXA F-02]
3. **Content Editor:** Content ownership is predictable: Pages owns page identity/content/SEO/history; Navigation owns placement. This adds a deliberate second journey when the editor also wants a menu link, but avoids duplicate ownership. [BP §1.12–1.13, C05–C07, Law 24]
4. **Marketing Manager:** The separation between Themes and Colors & Typography is conceptually defensible but unlike Shopify’s monolithic theme-editor expectation. Grouping both under Appearance makes the difference learnable; lifecycle copy must still distinguish immediate activation from draft/publish. [BP C09/C10, §11.2/§11.6; UXA F-04/F-10]
5. **Operations Admin:** StoreConfig recovery is powerful and safe only when exact inclusion/exclusion scope is shown. The technical term `StoreConfig` improves accuracy but raises novice cognitive load. [BP C11, §5.10–5.11, Laws 15–16]
6. **Support Agent:** Broken Navigation is the best-defined diagnostic recovery: load remains available, one broken row is annotated, and invalid content cannot be republished. The prototype does not establish a Support Agent backend permission; a support-only mutation role must not be inferred. [NAV-RESILIENCY Executive Summary/Ownership Graph; BP Law 31]

### Cross-role conclusion

The target supports the six mental models, but only two backend permission categories are currently certified for mutation: Owner and Admin. M1 role-visibility tests should therefore test authorized Owner/Admin and unauthorized direct access. They must not encode the six personas as security roles until a separate permission contract exists. [BP §3 introduction, §9.1 M1 testing strategy, Law 31]

---

## Journey Validation

### Journey scorecard

| ID | Workflow | Canonical expected path | Minimum clicks | Screens | Decisions | Confusion opportunities | Difficulty | Source |
|---|---|---|---:|---:|---:|---:|---|---|
| J01 | Create Homepage | Homepage → add section → choose admitted type → Save draft | 4 | 1 | 3 | 1 | Medium | BP C03, §5.1 |
| J02 | Edit Homepage | Homepage → select section → edit → Save draft | 3 | 1 | 2 | 1 | Easy | BP C03, §5.1, §7.3 |
| J03 | Publish Homepage | Homepage → Publish Homepage → confirm scope/history effect | 3 | 1 | 3 | 2 | Medium | BP §5.1/§5.7, C03 |
| J04 | Create CMS Page | Pages → Create → choose content representation → Save draft | 4 | 2 | 3 | 2 | Medium | BP C05/C06, §5.2 |
| J05 | Publish CMS Page | Pages → open page → Publish Page; save-first when dirty | 3 | 2 | 2 | 2 | Medium | BP C05/C06, §5.2/§7.4 |
| J06 | Rollback CMS Page | Pages → page → History → version → load/rollback → confirm scope | 6 | 2 | 4 | 3 | Hard | BP C06, §5.10–5.11 |
| J07 | Create Navigation Link | Navigation view → Add → type → target → Save draft → Publish → confirm Header+Footer scope | 7 | 1 | 5 | 3 | Hard | BP C07, §5.3, §7.2 |
| J08 | Repair Broken Link | Navigation → invalid row → replace/remove → Save → Publish → confirm | 6 | 1 | 4 | 2 | Medium | BP §7.6; NAV-RESILIENCY |
| J09 | Change Theme | Themes → select installed theme → signed Preview → close | 4 | 1 + preview | 2 | 2 | Medium | BP C09, §5.5/§5.9 |
| J10 | Edit Colors | Colors & Typography → edit → scoped/local Preview → Save draft | 3 | 1 | 3 | 2 | Medium | BP C10, §5.6 |
| J11 | Activate Theme | Themes → Activate theme now → confirm immediate-live identity change | 3 | 1 | 3 | 2 | Medium | BP C09, §5.5, Law 12 |
| J12 | Edit Footer | Footer Content → edit complete graph → Save live Footer → confirm | 3 | 1 | 4 | 4 | Hard | BP C08, §5.4, §7.2; HARDENING §Footer editor |
| J13 | Enable Maintenance Mode | Storefront Status → choose Maintenance → Save immediate live | 3 | 1 | 2 | 1 | Easy | BP C02, §7.2 |
| J14 | Recover StoreConfig | StoreConfig History → snapshot → inspect scope → Roll back → confirm | 5 | 1 | 5 | 4 | Hard | BP C11, §5.10–5.11 |
| J15 | Seed Storefront | Pages → Seed defaults → confirm multi-domain scope | 3 | 1 | 4 | 4 | Hard | BP C05; SEED §Execution Pipeline/Idempotency |

Counts exclude field-entry clicks and add one click when the relevant organizational parent is collapsed. Direct deep links remove the first click. [BP §6.4–6.6]

### J01 — Create Homepage

- **Expected path:** Open Homepage, add an admitted Home section, choose/configure its bounded content, and Save draft. The result is a persisted Home draft, not a second Homepage entity and not a live change. [BP C03, §5.1, §7.3]
- **Alternative path:** Enter from the Overview Homepage card or use the stable Home deep link. Both must load the same authority independently. [BP C01, §5.12, §6.6]
- **Error path:** Missing concurrency evidence disables mutation; malformed or semantically invalid section data is rejected; the candidate remains local. [BP §7.3/§7.6, Laws 9–10/32]
- **Recovery path:** Correct validation errors and retry with the retained candidate. On conflict, load current accepted evidence and deliberately choose Load Latest or a supported retry of the retained draft; never overwrite automatically. [BP §5.8, §7.6]
- **Validation:** Usable, but the task should be understood as creating Homepage content/sections, not creating a Homepage object. **Medium.** [BP §6.2, Law 35]

### J02 — Edit Homepage

- **Expected path:** Open Homepage, select an existing section, edit bounded settings/content, and Save draft. [BP C03, §5.1]
- **Alternative path:** Reorder, enable, or configure an admitted section in the same workspace; Preview may be used after the canonical save owner persists a dirty candidate. [BP C03, §5.9]
- **Error path:** A dirty candidate must not be replaced by focus, refresh, route, or incoming state; rejection leaves it intact. [BP §1.8, §5.8, Law 10]
- **Recovery path:** Cancel with confirmation to restore the accepted baseline, or save again after fixing the structured error. [BP §5.8, §7.3]
- **Validation:** One owner and one screen make routine editing easy. The only non-obvious fact is that Preview may save first. **Easy.** [BP §1.9, C03]

### J03 — Publish Homepage

- **Expected path:** Select `Publish Homepage`; if dirty, persist through the one Home draft owner; confirm Home scope and the StoreConfig history side effect; publish; adopt the complete response. [BP §5.1, §5.7, §7.4]
- **Alternative path:** Save draft and leave it unpublished. There is no global Publish Storefront alternative. [BP §5.8, Law 5]
- **Error path:** Publish-time re-admission or version conflict prevents promotion. No automatic publish retry is allowed. [BP §5.7, §7.4]
- **Recovery path:** Retain the candidate, load the latest accepted StoreConfig evidence, resolve the conflict, and explicitly publish again. [BP §7.6, Laws 8–10]
- **Validation:** Short path, but the merchant must understand both Home-only live scope and StoreConfig-wide snapshot history. **Medium.** [BP C03/C11]

### J04 — Create CMS Page

- **Expected path:** Open Content / Pages, create a draft page, enter identity/content/SEO, choose a supported rich-text or block representation, and Save. [BP C05/C06, §5.2]
- **Alternative path:** Use Seed only for the canonical default pages; Seed is not an alternative arbitrary-page creator and must not overwrite customized content. [BP C05; SEED §Idempotency Strategy]
- **Error path:** Invalid/duplicate slug, invalid content, unsafe media URL, or missing evidence is rejected by CMS authority. The editor stays on the candidate. [BP C06, §7.3; HARDENING §CMS pages]
- **Recovery path:** Correct the field or choose a unique slug, then save again. A conflict reloads/adopts authoritative version evidence without silently publishing. [BP §7.6; HARDENING §CMS pages]
- **Validation:** The two-screen list/detail model is conventional. Users must learn that page creation creates a draft and that menu placement is separate. **Medium.** [BP §1.12–1.13]

### J05 — Publish CMS Page

- **Expected path:** Open the page editor and select its page-scoped Publish action. Dirty visible content is saved first; the accepted draft version is then published; the full response is adopted and Publish disables when no draft remains. [BP C06, §5.2, §7.4; CMS-PUBLISH §Source-Backed Call Graph]
- **Alternative path:** Save draft and stop. Preview is available only for a clean persisted draft. The Pages list is navigation/collection lifecycle, not the canonical target publish initiator. [BP C05/C06, §2.2, §5.9]
- **Error path:** Validation, absence, or `409` version conflict blocks publication. The backend may supply the latest authoritative detail snapshot. [BP §7.4/§7.6; CMS-PUBLISH §Verified Root Cause]
- **Recovery path:** Adopt the supplied latest detail state; if it has no draft, Publish remains disabled; otherwise make deliberate edits/save and publish again. No automatic retry. [CMS-PUBLISH §Implementation/Certification]
- **Validation:** Workflow is coherent after M2 removes list-row publication. Until then, the duplicate list/editor initiator remains a migration observation. **Medium.** [BP §9.1 M2; UXA F-12]

### J06 — Rollback CMS Page

- **Expected path:** Open one CMS page, open page History, inspect an immutable page version, select the page-owned load/rollback action, and confirm page-only scope. [BP C06, §5.10–5.11]
- **Alternative path:** Inspect history and return without mutation, or load a historical snapshot as a candidate under CMS rules rather than treating it as global restoration. [BP §5.11]
- **Error path:** Stale semantic evidence or a changed page group blocks the transition. Cross-page or StoreConfig rollback is forbidden from this surface. [BP §7.2, Laws 15–16]
- **Recovery path:** Reload that page’s current history/evidence, reselect the intended version, and reconfirm. The current draft must not be silently discarded. [BP §5.8, §5.10]
- **Validation:** Safe but cognitively hard because `History`, `Version`, `Load snapshot`, and `Rollback` describe related but different states. Exact action copy is mandatory. **Hard.** [BP §1.16, §6.2]

### J07 — Create Navigation Link

- **Expected path:** Open Header or Footer Links, add a link, select its canonical type, select a backend-issued target or admitted path, Save the complete Header+Footer draft, and Publish the complete Navigation scope. [BP C07, §5.3; NAV-R §Canonical Navigation Contract]
- **Alternative path:** Use the other Header/Footer view without leaving the shared dirty workspace; choose external, custom, collection, product, category, or CMS target according to the same admission pipeline. [BP §6.4; NAV-R §DTO Contract]
- **Error path:** Invalid target, wrong tenant, deleted/hidden/unpublished dependency, unsafe path/URL, duplicate target, or stale RowVersion returns structured rejection. [NAV-R §Error Contract; BP §7.6]
- **Recovery path:** Retain the complete draft; fix the indicated link/field; save again; publish only after strict publication admission succeeds. [NAV-R §Admission Pipeline; BP §5.3]
- **Validation:** The target is safe and predictable once users understand that Header and Footer Links are views of one aggregate. That shared scope is the main cognitive burden. **Hard.** [BP C07, Laws 7/25/32]

### J08 — Repair Broken Link

- **Expected path:** Open Navigation, read the load warning, select the highlighted invalid row, replace or remove the target, Save, and Publish. [NAV-RESILIENCY Executive Summary/Call Graph]
- **Alternative path:** Remove the invalid link rather than replacing it. Editing the target page itself belongs to Pages, not Navigation. [BP §1.12–1.13, C07]
- **Error path:** Load remains available and preserves valid and invalid links, but save/publish continue to reject any unresolved invalid target. [NAV-RESILIENCY §Regression Tests/Certification]
- **Recovery path:** Resolve every warning through the one admitted target contract, then save and publish with current RowVersion evidence. [NAV-RESILIENCY §Ownership Graph; NAV-R §Concurrency]
- **Validation:** Recovery is obvious and unusually strong because the broken object remains visible and editable without weakening publication admission. **Medium.** [NAV-RESILIENCY Certification]

### J09 — Change Theme

- **Expected path:** Open Appearance / Themes, choose an installed presentation identity, open the signed read-only Storefront preview, evaluate it, and close. No state is changed. [BP C09, §5.5, §5.9]
- **Alternative path:** Leave the active theme unchanged and move to Colors & Typography for token-only changes. [BP C09/C10]
- **Error path:** Missing/unauthorized preview or unsupported theme fails closed; Preview cannot mutate runtime StoreConfig. [BP §1.9, C09, §7.5]
- **Recovery path:** Return to the installed theme list and retry preview with valid authority, or select another installed theme. [BP C09]
- **Validation:** Discoverable, but users may treat preview selection as a draft theme. The UI must state that the preview is read-only and not activated. **Medium.** [BP Law 13–14]

### J10 — Edit Colors

- **Expected path:** Open Appearance / Colors & Typography, edit admitted presentation tokens, inspect the local/scoped candidate preview, and Save draft. [BP C10, §5.6]
- **Alternative path:** Reset the candidate to the accepted/published token state without changing the installed theme identity. [BP C10, §7.2]
- **Error path:** Invalid tokens or stale StoreConfig evidence blocks save; Runtime behavior cannot be changed from this screen. [BP C10, Laws 9/20]
- **Recovery path:** Fix validation, reset to accepted state, or deliberately recover from conflict while preserving the candidate. [BP §5.8, §7.6]
- **Validation:** The fields are conceptually simple, but users must distinguish token draft, token preview, token publication, and theme activation. **Medium.** [BP §11.2/§11.6]

### J11 — Activate Theme

- **Expected path:** Open Themes, choose `Activate theme now`, and confirm that the installed theme identity changes the public Storefront immediately. [BP C09, §5.5, Law 12]
- **Alternative path:** Preview and stop without activation. Editing Colors & Typography is not an activation alternative. [BP C09/C10]
- **Error path:** Concurrency or activation failure leaves the previously accepted theme live. The UI must not assume the requested identity was accepted. [BP Laws 8–9/17]
- **Recovery path:** Reload current StoreConfig identity/evidence, verify the currently active theme, and explicitly retry activation. [BP §7.6]
- **Validation:** Usable if the button says `Activate theme now`. History/snapshot coverage remains an M3 observation and prevents a fully uniform recovery story. **Medium.** [BP §9.1 M3, §13.3 observation 3]

### J12 — Edit Footer

- **Expected path:** Open Footer Content, edit the complete footer-specific content graph, and confirm `Save live Footer`. The accepted response supplies a successor revision. [BP C08, §5.4, §7.2; HARDENING §Footer Recovery]
- **Alternative path:** Cancel or refresh with dirty-state confirmation. Footer navigation belongs in Navigation / Footer Links, not Footer Content after M4. [BP C08, §2.2; HARDENING §Footer editor]
- **Error path:** Validation retains the candidate. A stale revision creates a structured conflict. Load failure is non-editable/fail-closed. [HARDENING §Footer editor/§Error Mapping]
- **Recovery path:** Choose `Load Latest` or explicitly confirm `Retry My Draft` against a separately fetched latest revision. [HARDENING §Concurrency/§Footer editor; BP §7.6]
- **Validation:** Recovery is excellent, but the lifecycle is hard: immediate live, complete-graph replacement, no persisted draft, no preview, no rollback history, and a temporary legacy-link transition. **Hard.** [BP C08, §9.1 M4]

### J13 — Enable Maintenance Mode

- **Expected path:** Open Storefront Status, choose Maintenance, enter the message, and Save with visible immediate-live impact. [BP C02, §7.2]
- **Alternative path:** Choose Live or Disabled in the same operational owner, or cancel the unsaved candidate. [BP C02]
- **Error path:** Missing/stale StoreConfig evidence or validation blocks mutation; the current public state remains authoritative. [BP §7.3/§7.6]
- **Recovery path:** Retain the candidate, refresh current status/evidence deliberately, and save again. There is no certified status rollback history. [BP C02/C11, §5.10]
- **Validation:** Direct and predictable once exposed. **Easy.** [BP Law 18]

### J14 — Recover StoreConfig

- **Expected path:** Open StoreConfig Version History, select and inspect a snapshot, review all covered live domains and exclusions, choose `Roll back StoreConfig`, and confirm. [BP C11, §5.10–5.11]
- **Alternative path:** Inspect without applying, then return to the owning authoring screen. There is no universal Storefront rollback. [BP C11, Law 15]
- **Error path:** Stale RowVersion or changed live state blocks rollback. CMS page versions, Footer revision, and status cannot be rolled back here. [BP §7.2, Laws 15–16/27]
- **Recovery path:** Reload current history/evidence, re-evaluate the chosen snapshot against current live state, and reconfirm the full StoreConfig scope. [BP §5.10]
- **Validation:** Safe only with exact disclosure. `StoreConfig` is precise but technical, and the mismatch between one history screen and multiple excluded authorities is cognitively hard. **Hard.** [BP §6.2, UXA F-03/F-06]

### J15 — Seed Storefront

- **Expected path:** Open Pages, invoke the canonical default seed, confirm its CMS/Navigation/Homepage/Footer scope, and accept the structured result. The backend creates only missing defaults in one atomic operation. [BP C05; SEED §Execution Pipeline/Transaction Boundary]
- **Alternative path:** Manually create individual pages and author other domains through their own screens. That is not the same atomic default-seed workflow. [BP C03/C05/C07/C08]
- **Error path:** Invalid tenant, missing StoreConfig, already completed seed, duplicate canonical content, draft-only conflict, concurrency, or invalid stored state returns the structured 404/409/422 contract without partial commit. [SEED §Error Contract]
- **Recovery path:** For `ALREADY_COMPLETED`, no mutation is needed. For content conflicts, resolve the owning page/domain without overwriting customization. For missing StoreConfig, stop and escalate to the StoreConfig authority; Seed must not create it. [SEED §Idempotency Strategy/StoreConfig Contract]
- **Validation:** Very low click count hides high conceptual scope. The term `Seed` is technical, the command spans four authoring domains, and one prerequisite has no merchant self-service recovery. **Hard.** [BP Laws 2–3/7; SEED Certification]

---

## Cognitive Load Analysis

### Measured task load

`Concepts` counts the distinct state/ownership ideas a user must hold during the task, not the number of fields.

| Journey | Concepts | Menu levels | Clicks | Context switches | Screen switches | Duplicate/unexpected terminology | Complexity |
|---|---:|---:|---:|---:|---:|---|---|
| Create Homepage | 4 | 1 | 4 | 0 | 0 | `Create Homepage` suggests a nonexistent entity lifecycle | Medium |
| Edit Homepage | 4 | 1 | 3 | 0 | 0 | Preview may save first | Easy |
| Publish Homepage | 6 | 1 | 3 | 1 | 0 | Home-scoped Publish also creates StoreConfig history evidence | Medium |
| Create CMS Page | 6 | 2 | 4 | 0 | 1 | CMS blocks resemble Builder sections but are not Storefront Builder | Medium |
| Publish CMS Page | 5 | 2 | 3 | 0 | 1 | Current list/editor publish duplication must disappear in M2 | Medium |
| Rollback CMS Page | 7 | 2 | 6 | 0 | 1 | History, version, load snapshot, and rollback require exact distinctions | Hard |
| Create Navigation Link | 8 | 1 | 7 | 1 | 0 | Header/Footer views look separate but share one draft/publish | Hard |
| Repair Broken Link | 6 | 1 | 6 | 1 | 0 | Navigation repairs reference; it does not repair target content | Medium |
| Change Theme | 4 | 2 | 4 | 1 | 1 | Theme preview differs from token preview and Builder preview | Medium |
| Edit Colors | 6 | 2 | 3 | 0 | 0 | Theme identity, Theme tokens, Appearance, and Preview overlap linguistically | Medium |
| Activate Theme | 5 | 2 | 3 | 0 | 0 | Activate is immediate live, unlike token Publish | Medium |
| Edit Footer | 7 | 1 | 3 | 1 | 0 | Footer Links, Footer Content, and Legacy footer links are adjacent terms | Hard |
| Enable Maintenance | 4 | 1 | 3 | 0 | 0 | Save means immediate live on this screen | Easy |
| Recover StoreConfig | 8 | 1 | 5 | 1 | 0 | StoreConfig, version, history, rollback, coverage, exclusions | Hard |
| Seed Storefront | 9 | 2 | 3 | 3 | 0 | `Seed` and `Storefront` obscure a multi-domain default operation | Hard |

Sources: BP §3, §5–§7; UXA Workflow Inventory/Findings; HARDENING §Authoring consistency; SEED §Execution Pipeline.

### Mental model assessment

| Mental model | Assessment | Why | Source |
|---|---|---|---|
| One screen, one responsibility | Easy | C01–C11 provide distinct owners and prohibit catch-all editing | BP §1.1, §3, Law 1 |
| Candidate → draft → live | Medium | Strong and consistent where drafts exist, but Status/Footer/Theme activation are immediate live | BP §1.5, §7.2, Laws 6/11/12 |
| Publish | Medium | Consistent principle—named aggregate promotion—but deliberately not global | BP §1.4, §5.7, Law 5 |
| Preview | Hard for novices | CMS refuses dirty content; Builder saves first; installed Theme uses signed preview; token preview is local/scoped; some domains have none | BP §1.9, §5.9, §7.5 |
| Version/concurrency | Hard | StoreConfig RowVersion, CMS semantic version, Footer revision, and immutable history are different evidence types | BP C06–C08/C11, Laws 8–9/15 |
| History/rollback | Hard | StoreConfig and CMS have separate history; Footer/status do not; rollback coverage is not universal | BP §1.16, §5.10–5.11 |
| Theme/Appearance | Medium | Grouping is good, but installed identity and token lifecycle differ | BP C09/C10, §11.2 |
| Navigation/Footer | Hard until M4 | Final ownership is clear, but legacy footer links remain transitional and public presentation currently combines distinct inputs | BP C07/C08, §9.1 M4; UXA F-01 |
| CMS content vs placement | Medium | Familiar once learned; requires separate Pages and Navigation actions | BP §1.12–1.13, Law 24–25 |
| Runtime/Theme/Builder ownership | Easy for product behavior, mostly invisible to merchants | The UX can explain effects without exposing runtime/cache/projection controls | FAC Final Ownership Matrix; BP Laws 19–30 |

### Cognitive load verdict

- **Easy:** routine Homepage edits and Storefront Status.
- **Medium:** CMS create/publish, Theme evaluation/activation, and token editing.
- **Hard:** Navigation publication, CMS historical recovery, Footer immediate-live editing, StoreConfig rollback, and Seed.

The hard ratings do not mean the features should be merged. Merging would hide authority and increase failure impact. Their load should be controlled through exact scope, state, and recovery disclosure required by the Blueprint. [BP §1.1–1.18, §8]

---

## Discoverability Analysis

| Feature | Can a new merchant find it? | Can they predict where it belongs? | Would they search elsewhere first? | Would Shopify users understand it? | Verdict / source |
|---|---|---|---|---|---|
| Overview | Yes | Yes, at the top of Storefront | Some may expect a Dashboard label | Yes | Easy after replacing misleading Settings label. BP C01, §2.2; UXA F-02 |
| Storefront Status | Yes in target | Yes, operational status is explicit | Current users may search global Settings | Yes, though wording differs by platform | Strong target discoverability. BP C02, §2.2, Law 18 |
| Homepage | Yes | Yes | Shopify users might search under Themes/customize | Yes as a homepage task | Direct label is better than Homepage Builder. BP C03, §6.2 |
| Product Template | Yes under Catalog | Mostly; catalog relationship is clear | Shopify users may search Appearance/Themes | Yes conceptually | Medium; intentional separation from Theme must be learned. BP C04, §11.2/§11.6 |
| Pages | Yes under Content | Yes | Some may search Navigation when they want a linked page | Yes | Familiar and strong. BP C05, §11.2 |
| CMS Page Editor | Yes from Pages | Yes | No, once the page row is open | Yes | Strong; page-scoped history belongs here. BP C06 |
| Navigation / Header | Yes | Yes | Shopify users may search Content / Menus | Yes | Strong after canonical grouping. BP C07, §11.2 |
| Navigation / Footer Links | Yes | Yes after reading the full label | Many will search Footer Content first | Yes; Shopify treats footer menus as navigation | Medium; shared Header+Footer draft must be disclosed. BP C07, §11.2 |
| Footer Content | Yes, but hesitation likely | Partly | Users may search Navigation for links or Appearance for presentation | Partly | Hardest location distinction until M4; no move is justified. BP C08, §2.2, §9.1 M4 |
| Themes | Yes under Appearance | Yes | Shopify users expect one theme editor to contain more controls | Yes | Strong for browsing/activation. BP C09, §11.2 |
| Colors & Typography | Yes under Appearance | Yes | Shopify users may first open Themes | Yes after one learning step | Medium; separation is intentional. BP C10, §11.6 |
| StoreConfig Version History | Yes at bottom | Function is predictable; technical scope is not | Users may look inside Homepage, Navigation, or Themes for local history | Partly; terminology is Shopixy-specific | Discoverable but cognitively technical. BP C11, §6.1–6.2 |
| Seed Storefront | Only from Pages | Weak for a novice | Setup, Overview, or onboarding would be searched first | No; `Seed` is technical | Low discoverability is acceptable only if treated as first-run recovery/setup, not a daily primary feature. BP C05; SEED §Execution Pipeline |

### Discoverability findings

1. The target menu lets a new merchant predict the owner of most daily work without knowing Runtime, Theme, Builder, Cache, or StoreConfig internals. [BP §6.7, Laws 17/19–30]
2. `Footer Links` versus `Footer Content` is the largest remaining location hesitation. Moving either item would misstate ownership; the hesitation ends only when M4 completes the data/runtime-input cutover. [BP §9.1 M4]
3. Shopify users will understand Pages, menus, themes, previews, and templates, but may initially search Themes for Homepage/Product Template composition and may expect one theme editor. The Shopixy separation is intentional and architecture-backed. [BP §11.2/§11.6]
4. `StoreConfig` and `Seed` are accurate technical concepts but are the least merchant-native terms. The validation does not authorize renaming them beyond the Blueprint; it requires prototype comprehension testing and exact scope copy. [BP §6.2, Law 7]

---

## Consistency Analysis

### Terminology and action consistency

| Area | Canonical consistency rule | Inconsistency or residual risk | Status | Source |
|---|---|---|---|---|
| Naming | Use task/domain names, not implementation names | Current `Storefront Settings` opens Overview; `Homepage Builder` exposes implementation; Product Template is hidden | Resolved by M1 target | BP §2.2, §6.2; UXA F-02/F-09 |
| Buttons | Action verb must name state and scope | Generic Save/Publish/Activate/Rollback can hide different lifecycles | Must be enforced in M2 | BP §1.6, §6.2, §7.2 |
| Publish | One visible initiator per aggregate; no global publish | Current CMS list and editor both publish; Builder contains a global rollback shortcut | M2 blocker for full conformance, not M1 start | BP §2.2, §9.1 M2; UXA F-03/F-12 |
| Save | Save is draft-only unless explicitly immediate live | Status and Footer save live; theme activation is not Save; current Footer `Save and Publish` conflates state | Target is consistent only with explicit `Save live Footer`/status wording | BP §7.3, Laws 11–12; UXA F-07 |
| Preview | Every preview declares source and whether it saves | Builder saves dirty candidate; CMS refuses dirty; Theme uses signed real Storefront; token preview is local/scoped; Status opens live Storefront | Intentional semantic variation; unqualified `Preview` is inconsistent | BP §5.9, §7.5; UXA F-10 |
| Rollback | Same persistence authority owns history and rollback | Current Builder makes StoreConfig rollback appear page-local | Remove action/link to C11 in M2 | BP §2.2, §5.10, Law 16 |
| History | Label exact scope and exclusions | Generic Version History can be mistaken for universal history | Rename to StoreConfig Version History; CMS history stays in editor | BP C06/C11, Laws 15–16; UXA F-06 |
| Draft | Visibly distinguish local candidate, persisted draft, and live | Footer/status/theme identity have no persisted draft; Pages list only displays draft state | Intentional, but state badge/copy is mandatory | BP §1.5, C02/C05/C08/C09 |
| Version | Backend evidence only | `Version` can mean StoreConfig RowVersion, CMS semantic version, Footer revision, or history snapshot | Use domain-qualified language; never client-generate | BP C06–C08/C11, Laws 8–9 |
| Theme | Reserve Theme for installed presentation identity | Current Themes and Theme Editor sound overlapping; activation and token publication differ | Group/rename in M1; lifecycle scope in M2/M3 | BP C09/C10, §2.2; UXA F-04/F-10 |
| Appearance | Organizational parent only | Risk of becoming a catch-all persistence owner | No catch-all screen/API permitted | BP §4.1 Appearance row, §6.4 |
| Navigation | Own Header and Footer link topology | Header/Footer views may imply independent drafts; current Footer also authors links | Shared draft must be explicit; M4 removes duplicate owner | BP C07, §6.4, Law 25 |
| Footer | Footer Content is data; Theme is presentation; Navigation owns links | `Footer`, `Footer Links`, `Footer Content`, and transitional `Legacy footer links` are easy to conflate | Highest remaining naming/ownership risk | BP C08, §2.2, Law 26, §9.1 M4 |
| Homepage | Name user task, not Builder engine | `Create Homepage` implies multiple Homepage entities; CMS Page is a different content model | Keep one Homepage workspace; do not merge with Pages | BP C03, §6.2, Law 24 |
| CMS | Page content/history only; Navigation placement separate | CMS blocks can be mistaken for Storefront Builder; list publication duplicates editor publication today | Target keeps child editors but one publish owner | BP C05/C06, §2.2, Law 24 |
| Exit/navigation controls | Exit should return to a safe owning destination | Current Navigation Exit routes to Pages without a domain reason | M1/M2 route/navigation acceptance issue | UXA Navigation Graph, Risk UX-12 |
| Product Template delete | Exact live effect must be explicit | Delete is an immediate publication of template removal, unlike ordinary draft edit | Must not use ordinary Delete wording without live scope | BP C04, §7.2; BUILDER-R V8 recovery |
| Seed | Exact multi-domain scope must be explicit | `Seed Storefront` sounds like a single Pages action but creates several missing defaults | Keep one owner; expose full scope/result | BP C05; SEED §Execution Pipeline |

### Consistency verdict

The target is **principle-consistent, not mechanically uniform**. Every owner follows the same constitutional rules—named state, named scope, backend evidence, response adoption, retained candidate—but individual authorities legitimately differ in draft, preview, publication, version, and history behavior. Attempting to make every screen use identical Save/Publish/Preview mechanics would violate the certified architecture. [BP §1.4–1.17, §7; FAC Final Ownership Matrix]

The migration must remove current duplicate or misleading affordances; merely relabeling them would leave the consistency defects intact. [BP §9.1 M2–M4]

---

## Error Recovery Analysis

| Scenario | Canonical response | User-visible recovery | Is recovery obvious? | Observation | Source |
|---|---|---|---|---|---|
| Generic conflict | Retain candidate; show structured reason; obtain latest evidence; no automatic overwrite/publish | `Load Latest` or explicitly confirmed retry where supported | Medium | Exact choices vary by owner, so generic conflict copy is insufficient | BP §7.6, Laws 9–10/32 |
| Validation | Reject before mutation; backend admission is authoritative | Correct indicated fields/links and retry with candidate retained | Medium | Field/link attribution must survive transport mapping | BP §7.3, Law 32; HARDENING §Validation Strategy |
| 404 / absent deep link | Fail closed at route level; never open an editable empty/default editor | Return to owning list or retry after authority becomes available | Medium | No universal recovery target is constitutional; owner-specific destination is required | BP §6.6, Law 33 |
| 422 semantic admission | Return structured domain code/message/context | Repair the exact semantic dependency; save/publish remains blocked | Medium–High | Navigation has the strongest link-level implementation pattern | NAV-R §Error Contract; BP Law 32 |
| Broken Navigation | Load mixed valid/invalid links with warnings; strict save/publish | Replace or remove invalid row, save, then publish | High | Recovery is explicit and candidate-preserving | NAV-RESILIENCY Certification |
| Deleted CMS Page referenced by Navigation | Mark link invalid; do not silently drop it or publish it | Replace/remove the link; CMS content remains owned by Pages | High for Navigation; Low for undelete | No certified CMS undelete feature exists and none is inferred | NAV-RESILIENCY §Regression Tests; BP C06/C07 |
| Theme activation failure | Keep current accepted theme live; do not assume request success | Reload active identity/evidence and explicitly retry | Medium | M3 must make history/audit coverage unambiguous | BP C09, §9.1 M3, Laws 8–9 |
| CMS version mismatch | `409` with latest authoritative detail where available | Adopt latest; Publish disables when no draft remains; edit/save again deliberately | High | No automatic publish retry | CMS-PUBLISH §Implementation/Certification |
| StoreConfig conflict | Reject stale RowVersion; retain candidate or rollback intent | Reload accepted StoreConfig/history, re-evaluate scope, retry explicitly | Medium | Large rollback scope makes recovery mentally hard even when technically safe | BP §5.10, §7.6 |
| Footer conflict | Reject stale revision; preserve visible full-graph draft | `Load Latest` or `Retry My Draft` after latest revision fetch | High | Best-defined overwrite decision in the target | HARDENING §Concurrency/§Footer editor |
| Navigation load with no positive RowVersion | Fail closed; do not open unsaveable editor | Retry after backend authority is available | High | Persisted fallback must preserve real RowVersion | CONTRACT Root Cause #1 |
| Seed already complete/content conflict | Return structured 409 with no mutation | Stop when complete; resolve owning content conflict without overwrite | High | Low click count should not suggest a blind retry | SEED §Idempotency/§Error Contract |
| Seed missing StoreConfig | Return `CMS_SEED_STORE_CONFIG_REQUIRED` before mutation | Escalate to StoreConfig authority; do not create it from Seed | Low for self-service | This is an intentional recovery boundary and an operations prerequisite | SEED §StoreConfig Contract |

### Recovery verdict

Recovery UX is **obvious for broken Navigation, CMS publish conflict, Footer conflict, and Seed conflicts**. It is **adequate but cognitively demanding for StoreConfig rollback and Theme activation**, and **not merchant self-service for an absent StoreConfig**. The latter is not a UX defect that this validation may redesign; it is an explicit ownership boundary that must be stated in operational guidance. [BP Laws 27/32; SEED §StoreConfig Contract]

---

## Menu Validation

The constitutional order is validated as natural: understand → operate → compose → manage content/navigation → style → recover. [BP §6.1]

| Position | Menu item/group | Natural order? | Grouping obvious? | Likely hesitation | Should it move? | Source-backed result |
|---:|---|---|---|---|---|---|
| 1 | Overview | Yes | Yes | Dashboard/Overview vocabulary only | No | Read-only orientation belongs first. BP C01, Law 17 |
| 2 | Storefront Status | Yes | Yes | Users trained on current Settings may look elsewhere | No | Operational availability should be visible near the top. BP C02, Law 18 |
| 3 | Homepage | Yes | Yes | Shopify users may look under Themes | No | It is the highest-frequency composition task. BP C03, §11.6 |
| 4 | Catalog / Product Template | Yes | Yes after learning | One-child parent and Shopify theme-template expectation | No | Catalog protects Product Runtime boundary; Category Template must remain absent. BP C04, §6.4, Law 23 |
| 5 | Content / Pages | Yes | Yes | User may expect Menus under Content | No | Pages owns content only. BP C05/C06, Law 24 |
| 6 | Navigation / Header / Footer Links | Yes | Mostly | Tabs look independent but publish together | No | One Navigation aggregate requires one grouped owner. BP C07, §6.4, Law 25 |
| 7 | Footer Content | Yes, immediately after Navigation | Partly | Highest hesitation: Footer Links versus Footer Content | No | Moving it under Navigation or Appearance would misstate content ownership. BP C08, Law 26 |
| 8 | Appearance / Themes / Colors & Typography | Yes | Yes | Users may expect one editor and one publish lifecycle | No | Separate owners are intentional; group makes relationship visible. BP C09/C10, §11.6 |
| 9 | StoreConfig Version History | Yes | Function yes; scope requires explanation | Technical name and non-universal coverage | No | Recovery belongs last and must state exclusions. BP C11, §6.1 |

### Menu conclusions

- The order is natural and no item should move under the constitutional target.
- `Catalog` and `Content` each having one current child adds a possible expansion click, but preserves domain boundaries and future-proofs no unowned catch-all screen. [BP §6.4]
- `Navigation / Header` and `Navigation / Footer Links` must preserve one dirty workspace while switching views. [BP §6.4]
- The main hesitation is not order; it is the semantic boundary between Footer Links and Footer Content. That cannot be solved by menu movement and must remain an M4 migration concern. [BP §9.1 M4]
- Parent groups must not become clickable catch-all editors; when accessibility requires activation, they route to the first authorized child. [BP §6.4]
- Category Template, generic Settings, universal History, Theme Code, cache controls, projection controls, and operational repair tools remain absent. [BP §6.1, Law 36]

---

## Shopify Parity

This comparison is user-experience-only and uses the Blueprint’s dated conceptual parity review. It does not compare implementation or copy Shopify architecture. [BP §11.1]

### Where Shopixy is simpler

1. One bounded Builder experience covers Homepage and Product Template without exposing separate Builder engines. [BP §11.3; BUILDER-R Final Certification]
2. No Theme code editor, Category Template, arbitrary runtime/plugin editor, or template matrix for every content type appears in normal Storefront administration. [BP §11.3, Laws 23/34/36]
3. Header and Footer Links are views of one Navigation owner and one publication scope. [BP §11.3, C07]
4. Pages own content while Navigation owns placement; users are not asked to understand duplicate page/menu truth. [BP §11.2]

### Where Shopixy is more powerful for users

1. CMS pages provide immutable page versions, exact persisted-version preview, page history, and page-scoped rollback. [BP §11.4, C06]
2. Recovered authoring surfaces use backend-issued conflict evidence and deliberate stale-write recovery rather than silent last-writer-wins behavior. [BP §11.4, Laws 8–10]
3. Navigation publication verifies target eligibility and retains broken links for repair rather than silently losing them. [BP §11.4; NAV-RESILIENCY]
4. Product Template customization cannot remove commerce-critical Product Hero behavior, reducing the risk that a visual editor breaks selling. [BP §11.4, Law 22]
5. StoreConfig and CMS histories remain domain-specific and actionable rather than becoming a vague activity log. [BP §11.2/§11.4]

### Where Shopixy is more confusing

1. Theme identity activation and Colors & Typography publication are separate lifecycles under one Appearance concept. [BP §11.5, observation 6]
2. StoreConfig Version History is powerful but non-universal; users must remember CMS, Footer, and status exclusions. [BP §11.5, observation 5]
3. Footer Links and Footer Content are adjacent, and the current compatibility period still has legacy footer-link data. [BP §11.5, observation 1; §9.1 M4]
4. Product Template lives under Catalog rather than inside a theme editor, which is correct for Shopixy but initially surprising to Shopify users. [BP §11.2/§11.6]
5. Preview has several legitimate meanings and guarantees across Builder, CMS, Themes, and tokens. [BP §5.9, §7.5]
6. Per-domain publication is safer but requires more state literacy than a theme-centric “publish the design” model. [BP Law 5, §11.6]

### Intentional differences that are justified

- Keep Builder composition separate from Theme presentation tokens and identity. [BP §11.6; FAC Final Ownership Matrix]
- Keep Category Builder-free under the current certified runtime. [BP Law 23]
- Keep Product Runtime capabilities mandatory even when Product Template presentation is customized. [BP Law 22]
- Keep StoreConfig history and CMS Page history separate. [BP Laws 15–16]
- Keep backend-issued Navigation identities and target resolution rather than client-authored routing truth. [BP §11.6]
- Keep explicit per-domain publication; do not add a universal Publish Storefront action. [BP Law 5]

### Shopify parity verdict

The target has good conceptual parity where it helps merchant comprehension—Pages separate from menus, installed Themes separate from activation, and header/footer menus treated as Navigation. Its intentional differences are justified by clearer failure containment and ownership. The migration should not chase Shopify surface parity by adding Category Template, a monolithic Theme editor, universal history, or a global publish action. [BP §11.2–§11.6]

---

## Migration Readiness

### Can M1 safely begin?

**Yes. M1 can safely begin with observations.**

M1 changes menu hierarchy, naming, grouping, and reachability while preserving existing owners, routes, query identities, and runtime behavior. The Blueprint rates it Low–Medium risk and explicitly requires redirects, role visibility, keyboard navigation, breadcrumbs, direct-load/deep-link tests, and dirty-state guards. [BP §9.1 M1, §9.2]

### M1 start blockers

| Blocker | Status | Decision |
|---|---|---|
| Unclear target screen inventory | Closed | C01–C11 are constitutional. BP §3 |
| Runtime/Theme/Builder/Cache ownership uncertainty | Closed | Final architecture and recoveries freeze the boundaries. FAC; RUNTIME-R; THEME-R; BUILDER-R; BROWSER-CACHE-R; CACHE-R |
| Product Template authority | Closed | It is an existing canonical workspace under the one Builder engine. BP C04; BUILDER-R |
| Category Template proposal | Closed by exclusion | It must remain absent. BP Law 23 |
| Existing route compatibility | Open prerequisite, not architecture blocker | Preserve aliases/query identity and test direct load. BP §6.6, §9.4 |
| Role-specific authorization for six personas | Open observation | Do not invent roles; test Owner/Admin versus unauthorized until separately certified. BP Law 31 |

**Net M1 blocker count: 0 architecture blockers; 2 execution prerequisites remain open (route compatibility evidence and authorized/unauthorized visibility tests).**

### Blockers to full migration completion

| ID | Blocker | Blocks | Why | Source |
|---|---|---|---|---|
| B-01 | Duplicate/misplaced current workflow affordances | Full M2 conformance | CMS list publish and Builder global rollback must cease to look like canonical local actions | BP §9.1 M2; UXA F-03/F-12 |
| B-02 | Theme activation history/lifecycle ambiguity | M3 certification | Immediate activation needs explicit concurrency, audit, event, and snapshot semantics | BP §9.1 M3, §13.3 |
| B-03 | Footer-link dual ownership and data-shape mismatch | M4 certification | Grouping/localization/order/fallback/revision must migrate losslessly to one Navigation source | BP §9.1 M4; UXA F-01 |
| B-04 | No certified fine-grained persona permissions | Any claim of role-specific security | Current backend authorizes Owner/Admin; menu visibility cannot create permissions | BP §3 introduction, Law 31 |
| B-05 | Missing StoreConfig has no ordinary recovery screen | Self-service Seed recovery | Seed correctly requires StoreConfig and must not become its creator | SEED §StoreConfig Contract |

### Risks

| Risk | Rating | Control before/during M1 | Source |
|---|---:|---|---|
| Old `/storefront` and `/settings?tab=storefront` links break | Medium | Preserve aliases/redirects; direct-load test | BP MR-04, §9.4 |
| Product Template ID/query identity is lost | High | Preserve `page=product-template` and `templateId` | BP MR-05, §6.6 |
| Dirty work is discarded by new sidebar/breadcrumb navigation | High | Shared route/tab/unload guards | BP MR-10, Law 10 |
| Category Template is accidentally exposed during regrouping | High | Negative menu/route test | BP MR-06, Law 23 |
| Status remains hidden despite new label | High | Direct link and first-load authority test | BP M1 success criteria |
| Parent groups become catch-all routes | Medium | Route parents to first authorized child only; no new editor | BP §6.4 |
| Footer Links/Footer Content appear resolved before M4 | High | Preserve explicit transitional ownership and do not remove data/runtime readers in M1 | BP §9.1 M4 |
| M1 labels imply nonexistent persona permissions | High | Backend direct-URL authorization tests; no UI-only security claims | BP Law 31 |

### Prerequisites

1. Freeze the C01–C11 route-to-owner matrix as the M1 acceptance baseline. [BP §3–§4]
2. Inventory and preserve `/storefront`, `/settings?tab=storefront`, `page=home`, `page=product-template`, and `templateId` behavior. [BP §6.6, §9.4]
3. Define M1 tests for sidebar order, expansion, canonical names/icons, breadcrumbs, keyboard access, direct deep links, and Category Template absence. [BP §6, §12.5]
4. Define Owner/Admin authorized behavior and unauthorized direct-route behavior without inventing the six personas as permissions. [BP Law 31]
5. Add dirty-state navigation acceptance cases for Homepage, Product Template, CMS Page, Navigation Header/Footer view switching, Footer, and Colors & Typography. [BP Law 10, §12.5]
6. Keep M1 documentation/route/menu work isolated from M2 actions, M3 Theme lifecycle, and M4 Footer data/runtime cutover. [BP §9.1 M1–M4]
7. Capture the current route/query/permission snapshot before implementation so rollback can restore labels/routes without changing product state. [BP M0/M1 rollback strategy]

### M1 exit criteria

- `Storefront Settings` no longer labels Overview.
- Storefront Status and Product Template are directly discoverable.
- Appearance groups Themes and Colors & Typography without merging them.
- StoreConfig Version History is exact and non-universal.
- Category Template is absent.
- All old deep links resolve safely with identity intact.
- Organizational parents do not create catch-all ownership.
- Navigation Header/Footer views preserve one dirty workspace.
- No Runtime, Theme, Builder, CMS, Navigation, Footer, StoreConfig, Projection, or Cache owner changes.

[BP §9.1 M1 success criteria, §12.4–§12.6]

---

## Open Risks

| ID | Open risk | Severity | Migration owner/exit condition | Source |
|---|---|---:|---|---|
| OR-01 | Functional personas may be mistaken for backend roles | High | Security/UX governance: certify a permission matrix or keep Owner/Admin-only language | BP Law 31 |
| OR-02 | Users may publish both Header and Footer Links while believing only the visible tab changes | High | M2 copy/test: exact complete Navigation scope | BP C07, §6.4 |
| OR-03 | Footer Content remains confused with Footer Links during legacy transition | Critical for M4 | M4 lossless one-source cutover certification | BP MR-01/MR-02, §9.1 M4 |
| OR-04 | Theme activation failure/history coverage remains unclear | High | M3 lifecycle and history certification | BP MR-03, §9.1 M3 |
| OR-05 | StoreConfig rollback is mistaken for CMS/Footer/status recovery | High | M2 scope/exclusion labels and confirmation tests | BP MR-08, C11 |
| OR-06 | Different Preview guarantees are presented with one unqualified label | Medium | M2 enforce §7.5 source disclosure | BP §7.5; UXA F-10 |
| OR-07 | Seed’s technical name and multi-domain effect are not understood | High | First-run task test and exact aggregate result/error copy | SEED §Execution Pipeline/§Error Contract |
| OR-08 | Missing StoreConfig leaves merchant unable to self-recover Seed | Medium operational | Support runbook/escalation to StoreConfig authority; no Seed side-effect creation | SEED §StoreConfig Contract |
| OR-09 | Parent groups with one child increase menu friction | Low | Keyboard/expansion test; retain boundary unless a new responsibility is certified | BP §6.4 |
| OR-10 | Current duplicate CMS publish or Builder rollback survives hidden in secondary UI | Medium | M2 initiator-count and negative action tests | BP §9.1 M2 |
| OR-11 | Product Template is sought under Themes by Shopify-trained users | Medium | M1 usability observation; validate Catalog naming/breadcrumb | BP §11.2/§11.6 |
| OR-12 | Deleted CMS target has link repair but no certified undelete workflow | Medium | Present replace/remove; do not imply undelete | NAV-RESILIENCY; BP Law 36 |

---

## Recommendations

These recommendations validate and guard the constitutional UX; they do not redesign it.

1. **Proceed with M1 as a strictly isolated menu/naming/reachability phase.** Do not include Footer data migration, Theme lifecycle changes, workflow-action removal, or runtime modifications in the M1 change set. [BP §9.1 M1–M4]
2. **Use the canonical action vocabulary exactly:** `Save draft`, `Publish Homepage`, `Publish Header and Footer Navigation`, `Activate theme now`, `Save live Footer`, and `Roll back StoreConfig`. Validate exact scope in confirmation and result copy. [BP §6.2, Law 7]
3. **Treat the six roles as usability personas only.** Before any role-specific visibility is shipped, either certify backend permissions or retain the current Owner/Admin authority and test direct URL denial. [BP Law 31]
4. **Prototype-test the four hard tasks before M2/M3/M4:** Navigation publication, Footer live save/conflict, StoreConfig rollback, and Seed. Success means the participant can state what becomes live, what history applies, and what is excluded without assistance. [BP §12.5]
5. **Require state/source disclosure beside every Preview action.** The user should be able to answer whether Preview saves first, whether it is a persisted draft, whether it is signed read-only, and whether it can change live state. [BP §7.5]
6. **Preserve dirty state across every new sidebar, breadcrumb, redirect, and Header/Footer Navigation view transition.** This is an M1 acceptance criterion, not a later polish item. [BP Law 10, MR-10]
7. **Do not represent M4 as a menu-only cleanup.** Keep legacy Footer links recoverable until checksummed, migrated, validated in public output, and covered by rollback rehearsal. [BP §9.1 M4]
8. **Do not promise missing-StoreConfig self-service through Seed.** Show the structured prerequisite failure and route the incident to operational support under the existing StoreConfig authority. [SEED §StoreConfig Contract]
9. **Measure actual post-M1 paths against this baseline.** Any task that adds a new screen, duplicate publish initiator, or extra owner requires explanation and constitutional review. [BP Laws 1–3/35/38]
10. **Recertify after each migration phase.** M1 should prove information architecture only; M2 action/state consistency; M3 Theme lifecycle; M4 one-source Footer Navigation; M6 full conformance. [BP §9, §12.6]

---

## Final Certification

### Result: **READY WITH OBSERVATIONS**

The proposed Storefront UX is validated as usable, discoverable, workflow-consistent at the constitutional level, recoverable under its certified authority contracts, and compatible with the frozen Storefront architecture.

The validation certifies that:

- the eleven-screen inventory gives each retained responsibility one clear home;
- the sidebar order is natural and no item needs to move;
- Homepage, CMS, Navigation, Footer, Theme, Appearance, Status, History, and Seed journeys can be completed without inventing a second owner;
- exact draft/live/preview/history differences are explainable and recoverable;
- broken links, conflicts, validation errors, and stale versions have safe paths that preserve candidate work;
- M1 can begin without reopening Runtime, Theme, Builder, CMS, Projection, Cache, or StoreConfig ownership;
- Shopify parity is sufficient where user concepts align, and intentional differences are justified.

The observations preventing an unqualified `READY FOR MIGRATION` are:

1. the six validation personas are not six certified permission roles;
2. Navigation publication, Footer editing, StoreConfig rollback, and Seed remain cognitively hard;
3. Theme activation lifecycle/history requires M3 alignment;
4. Footer-link ownership requires the separately gated M4 migration;
5. a missing StoreConfig has no ordinary merchant self-service recovery;
6. current duplicate/misleading workflow affordances remain until M2.

These observations do not block the start of M1. They do block any claim that the complete Storefront UX migration is already finished or that M2–M4 can be combined without their own authorization, testing, rollback, and certification. [BP §9.1–§9.4, §13.3]

**M1 decision: MAY BEGIN after the listed prerequisites are accepted.**

**Application code changed by this validation: none. Documentation only.**
