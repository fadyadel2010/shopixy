# Purpose

Cross-System Certification exists to verify that the Shopixy Knowledge Base describes one coherent architecture across all authoritative guides.

A Core Guide may be correct in isolation. A System Guide may also be correct in isolation. Certification is required because two individually correct documents can still contradict each other when read together. Those contradictions can mislead QA Engineers, developers, product owners, and AI Agents about authority, ownership, lifecycle, business actors, terminology, and system boundaries.

The purpose of certification is to eliminate architectural contradictions across the Knowledge Base.

This document defines how certification is performed. It does not certify any guide, does not record review results, and does not decide whether any document is currently compliant.

Cross-System Certification answers one question:

> When are two guides considered architecturally consistent?

Two guides are architecturally consistent when they describe the same business reality, respect the same authority boundaries, use compatible terminology, preserve the same ownership model, and do not create competing sources of truth.

# Scope

This standard certifies consistency across the Shopixy Knowledge Base.

It applies to:

- Core Guides
- System Guides
- Cross-System Relationships
- Authority Boundaries
- Terminology
- Ownership
- Cross References
- Business Rules
- Architectural Consistency

Certification reviews whether guides align with each other at the architectural level. It asks whether the documents describe the same durable system model from compatible perspectives.

Certification includes the following concerns:

- Whether a System Guide extends the relevant Core Guides without replacing them.
- Whether a Core Guide and a System Guide assign authority to the same owner.
- Whether related System Guides describe their relationship consistently from both sides.
- Whether business actors are named and bounded consistently.
- Whether shared terminology carries the same meaning across documents.
- Whether lifecycle ownership is preserved across all guides that mention the lifecycle.
- Whether cross references point readers toward the correct authoritative guide.
- Whether presentation surfaces, read models, dashboards, browser state, provider callbacks, notifications, analytics, and operational tools remain evidence or orchestration surfaces rather than authority.

Certification is out of scope for:

- Code quality
- Runtime performance
- UI design
- API design
- Endpoint behavior
- Database schema review
- Implementation correctness
- Temporary project status
- Delivery progress
- Sprint state
- Bug backlogs
- Test execution results
- Current production readiness
- Feature completeness
- Developer workflow
- Refactoring recommendations

Certification may identify that documentation contradicts the authoritative architecture, but it does not inspect code to prove implementation compliance.

# Authority Precedence

Certification follows the Shopixy authority philosophy.

Authoritative precedence is:

```text
.ai-context
↓
Core Guides
↓
System Guides
↓
Certification
↓
Implementation
```

`.ai-context` is the highest documentation authority for durable platform truth, cross-system architecture, source-of-truth rules, lifecycle boundaries, tenant isolation, financial authority, reconciliation rules, and fail-closed principles.

Core Guides define reusable architectural truth for shared domains such as platform architecture, inventory, payments, orders, security, and QA philosophy.

System Guides describe how a business system operates within those Core boundaries. A System Guide may explain workflows, responsibilities, operational surfaces, and cross-system relationships, but it may not redefine Core authority.

Certification defines the method for comparing guides. It is a standard for review, not a source of business rules. Certification cannot override `.ai-context`, a Core Guide, or a System Guide.

Implementation is below the Knowledge Base authority chain. Code may reveal that implementation diverges from documentation, but implementation does not become architectural truth merely because it exists.

When conflicts are found:

1. Resolve toward `.ai-context` when `.ai-context` contains the relevant durable rule.
2. If `.ai-context` is silent, resolve toward the applicable Core Guide.
3. If Core Guides are silent, compare the relevant System Guides and determine which document is introducing authority beyond its scope.
4. If the conflict cannot be resolved from existing authoritative documents, the finding must be recorded as an unresolved documentation authority gap for future correction.
5. Certification must never silently merge conflicting statements into a new invented rule.

# Certification Philosophy

Cross-System Certification is guided by the following principles.

## Consistency Over Completeness

A guide does not need to describe every detail of every related system. It must, however, avoid contradicting the guides that own those details.

A short but consistent guide is preferable to a complete-looking guide that assigns authority incorrectly.

## One Architectural Truth

The Knowledge Base must describe one coherent platform architecture. Different guides may describe different perspectives, but they must not create different truths.

System Guides describe the same reality from different operational viewpoints. Core Guides describe the shared rules that those systems depend on.

## No Duplicated Authority

Only one guide should own a durable architectural rule. Other guides may reference that rule, summarize its implication, or explain how a system depends on it, but they must not become a second owner of the same authority.

Duplication becomes dangerous when copied language drifts, narrows, expands, or contradicts the original rule.

## No Contradictory Ownership

A business object, lifecycle, state transition, financial event, inventory movement, identity boundary, or tenant-scoped authority cannot have competing owners across guides.

If one guide says a Core domain owns a lifecycle and another guide says a System surface owns the same lifecycle, the Knowledge Base is inconsistent.

## Bidirectional Relationships

When two systems have a business relationship, both participating guides must describe that relationship compatibly.

The relationship does not need to be described with identical wording, but the same actors, responsibilities, dependencies, boundaries, and authority direction must be preserved.

## Fail Closed

When documentation describes uncertainty, ambiguity, stale evidence, missing proof, conflicting source data, or unresolved external signals, the architecture must preserve fail-closed behavior.

No guide may imply that uncertainty authorizes completion, outward money movement, inventory mutation, lifecycle finality, cross-tenant access, or recovery by assumption.

## SQL Remains Authoritative

PostgreSQL remains the authoritative system of record for durable Shopixy business truth unless an authoritative guide explicitly defines a narrower exception.

MongoDB, Redis, Elasticsearch, browser state, analytics events, provider callbacks, notification delivery, dashboards, and UI state are derivative evidence or presentation surfaces. They may support workflows, but they do not become authority.

## Systems Describe the Same Reality From Different Perspectives

A Core Guide may describe a domain rule. A System Guide may describe how an operational surface uses that rule. Another System Guide may describe how it depends on that same rule.

These perspectives are consistent only when they preserve the same ownership, authority, lifecycle, actors, terminology, and business boundaries.

# Certification Principles

Certification reviews must follow these fundamental principles.

These principles define the reviewer's mindset. They do not define the step-by-step review process and they do not assign certification outcomes.

## Certification Is Evidence-Driven

Certification depends on explicit documentation evidence.

A reviewer must ground each consistency claim in authoritative documentation, not memory, expectation, implementation behavior, or inferred intent.

## Certification Never Invents Architecture

Certification may identify contradictions, gaps, or ambiguity, but it must not create new architecture to close them.

When the existing Knowledge Base does not define an authority, relationship, lifecycle, or boundary, the correct output is a finding or unresolved gap, not an invented rule.

## Certification Never Resolves Ambiguity By Assumption

Ambiguity must remain visible until corrected by an authoritative document.

A reviewer must not assume that two statements are compatible merely because they could be interpreted compatibly. Certification requires clear alignment, not charitable reconstruction.

## Certification Compares Documentation, Not Implementation

Certification is a Knowledge Base consistency activity.

It compares `.ai-context`, Core Guides, System Guides, cross references, terminology, ownership statements, and business rules. It does not certify code, API behavior, UI behavior, database design, or production state.

## Certification Preserves Architectural Intent

Certification should protect the durable architectural model behind the Knowledge Base.

Corrections should strengthen the intended authority model rather than flattening system differences, removing necessary boundaries, or turning every guide into a duplicate of every other guide.

## Certification Values Consistency Over Wording

Guides do not need identical phrasing to be consistent.

Certification should compare meaning, authority, boundaries, actors, lifecycle, and ownership. Wording differences matter only when they create ambiguity, contradiction, or misunderstanding.

## Certification Resolves Conflicts Toward Higher Authority

When documents conflict, certification must follow the authority precedence chain.

Lower-authority documents must be corrected toward higher-authority documents. Certification must not average conflicting statements or choose the more convenient wording.

## Certification Never Creates New Business Rules

Certification defines whether existing documents agree.

It does not define payment rules, inventory rules, order rules, affiliate rules, tenant rules, marketplace rules, accounting rules, security rules, or operational rules.

## Certification Validates Relationships, Not Isolated Documents

The purpose of certification is cross-document consistency.

A document may be clear and internally coherent while still contradicting another guide. Certification therefore validates relationships between documents, systems, domains, and authorities rather than treating each guide as an isolated artifact.

# Certification Rules

## Rule 1: Every Business Relationship Must Be Described Consistently By Both Participating Systems

If two systems interact, both System Guides must describe the relationship without contradiction.

The guides must agree on:

- Which system initiates the business action.
- Which system owns durable authority.
- Which system presents, orchestrates, or consumes evidence.
- Which Core Guide owns the underlying business rule.
- What the relationship does not allow.

A one-sided relationship description is incomplete. A contradictory relationship description is a certification finding.

## Rule 2: Authority Ownership Must Never Contradict Another Guide

A guide must not assign ownership of a business rule, lifecycle, mutation, or final state to a system that another authoritative guide says is not the owner.

System surfaces may submit intent, display state, collect evidence, request actions, or operate workflows. They do not become authority unless the authoritative architecture explicitly grants that role.

## Rule 3: Terminology Must Remain Consistent

The same term must mean the same thing across guides.

If a term is used differently in different guides, the distinction must be explicit. If the distinction is not explicit, the terminology is inconsistent.

Terminology consistency is required for:

- Business actors
- Lifecycle states
- Financial states
- Inventory concepts
- Tenant boundaries
- System names
- Operational surfaces
- Authority labels
- Evidence labels
- Recovery language

## Rule 4: Business Actors Must Remain Consistent

Guides must describe the same actors with compatible roles and boundaries.

A customer, vendor, tenant operator, affiliate, platform administrator, supplier, carrier, provider, or marketplace participant must not gain different powers merely because a different guide is describing them.

Actor consistency includes:

- Who the actor is.
- Which system the actor belongs to.
- What the actor can request.
- What the actor cannot decide.
- Which authority validates the actor's action.

## Rule 5: Lifecycle Ownership Must Remain Consistent

Lifecycle transitions must remain owned by the same authoritative domain across all guides.

A System Guide may describe that a user requests a transition through an operational surface, but the guide must not imply that the surface owns admission, finality, recovery, reconciliation, or state mutation if a Core lifecycle owner owns those responsibilities.

Lifecycle consistency applies to:

- Orders
- Payments
- Refunds
- Fulfillment
- Inventory movements
- Payout readiness
- Settlements
- Returns
- Identity/session admission
- Tenant-scoped operational permissions

## Rule 6: Shared Core Domains Must Never Have Competing Owners

When multiple systems depend on a shared Core domain, no System Guide may claim ownership of that Core domain.

Shared Core domains provide common authority across system surfaces. System Guides may document how they consume or operationalize the Core domain, but they must not redefine the Core domain's rules.

## Rule 7: Presentation Never Becomes Authority

Presentation surfaces may display state, collect intent, expose controls, and help users operate workflows.

Presentation does not decide durable truth.

Examples of non-authoritative presentation include:

- UI screens
- Dashboards
- Product cards
- Search results
- Customer account pages
- Admin panels
- Browser route state
- Local storage
- Session storage
- Notification views
- Analytics dashboards

A guide is inconsistent if it implies that presentation alone can prove truth, mutate authority, or finalize a lifecycle.

## Rule 8: Evidence Never Becomes Authority

Evidence can support authority, but evidence does not become authority by itself.

Examples of evidence include:

- Provider callbacks
- Webhook payloads
- Payment gateway redirects
- Uploaded proof
- Notification delivery receipts
- Analytics events
- Search documents
- Mongo projections
- Redis cache entries
- Browser state
- Logs
- External status messages

Authority may admit, reject, reconcile, quarantine, or repair evidence. Evidence alone must not finalize business truth.

## Rule 9: Recovery Never Invents Authority

Recovery processes may repair projections, rebuild derivative state, replay events, reconcile uncertainty, or restore consistency from authoritative records.

Recovery must not invent missing authority.

A guide is inconsistent if it implies that recovery may create finality, settle ambiguity, complete outward money movement, restore inventory, validate identity, or confirm lifecycle state without authoritative proof.

## Rule 10: System Guides Extend Core Guides; They Never Replace Them

System Guides must be read as extensions of Core Guides.

A System Guide may explain:

- How a system participates in a Core domain.
- Which user journeys depend on the Core domain.
- Which operational surface submits intent.
- Which projections or dashboards expose Core-owned state.
- Which cross-system dependencies matter to that system.

A System Guide must not:

- Redefine Core ownership.
- Narrow Core fail-closed rules.
- Create alternate lifecycle states.
- Introduce competing authority.
- Treat implementation convenience as architectural truth.
- Replace a Core Guide with system-specific wording.

# Consistency Dimensions

Certification reviews the following dimensions.

## Relationship Consistency

Relationship consistency verifies that connected systems describe their interaction in compatible ways.

Review questions:

- Do both guides describe the same relationship?
- Do they agree on direction of dependency?
- Do they agree on which system owns the business outcome?
- Do they agree on what each system may and may not do?
- Do they reference the same Core authority when relevant?

## Authority Consistency

Authority consistency verifies that durable truth is assigned to the same owner everywhere.

Review questions:

- Which guide owns the authoritative rule?
- Does another guide duplicate or contradict that rule?
- Does a System Guide accidentally convert orchestration into authority?
- Does any guide treat derivative state as authoritative?

## Actor Consistency

Actor consistency verifies that business participants have the same role across guides.

Review questions:

- Are the same actors named consistently?
- Are actor permissions and limitations preserved?
- Does any guide give an actor powers that another guide denies?
- Are platform actors separated from tenant actors?
- Are customer, vendor, affiliate, supplier, carrier, and administrator roles kept distinct?

## Terminology Consistency

Terminology consistency verifies that words carry stable meaning across the Knowledge Base.

Review questions:

- Is the same term used for the same concept?
- Are different terms used only when they represent different concepts?
- Are similarly named but distinct concepts clearly separated?
- Would a QA Engineer reasonably misunderstand the term after reading multiple guides?

## Lifecycle Consistency

Lifecycle consistency verifies that state transitions, finality, recovery, and reconciliation are described compatibly.

Review questions:

- Which domain owns the lifecycle?
- Which system may request a transition?
- Which authority admits the transition?
- What evidence is required?
- What happens under ambiguity?
- Does the guide preserve fail-closed behavior?

## Business Rule Consistency

Business rule consistency verifies that durable business constraints do not change across guides.

Review questions:

- Are pricing, payment, inventory, order, tenant, identity, accounting, settlement, payout, and fulfillment rules preserved?
- Does a System Guide narrow or expand a Core rule?
- Does a guide create a special case without authoritative support?
- Does the same business scenario produce the same architectural outcome across documents?

## Cross Reference Consistency

Cross reference consistency verifies that guides point readers toward the correct authority.

Review questions:

- Does the guide reference the correct Core Guide?
- Does a System Guide point to related System Guides when relationships matter?
- Are cross references used to avoid duplicating authority?
- Do references direct readers to the owner of the rule rather than a consumer of the rule?

## Ownership Consistency

Ownership consistency verifies that each system owns only what it is architecturally allowed to own.

Review questions:

- Does the guide distinguish ownership from operation?
- Does it distinguish decision authority from evidence collection?
- Does it distinguish lifecycle ownership from workflow participation?
- Does it distinguish business authority from UI responsibility?

## Presentation vs Authority

This dimension verifies that guides preserve the boundary between what users see and what the platform knows.

Review questions:

- Is UI state described as presentation?
- Are dashboards described as views over authority?
- Are projections described as derivative?
- Are stale or missing projections treated as evidence problems rather than truth changes?

## Evidence vs Authority

This dimension verifies that external signals, derived records, and operational traces are not promoted into durable truth.

Review questions:

- Is evidence admitted by the correct authority?
- Does uncertainty fail closed?
- Does reconciliation preserve authority?
- Does recovery repair from authoritative records rather than inventing state?

# Severity Levels

Certification findings use four severity levels.

## Critical

A Critical finding is an architectural contradiction.

Critical findings indicate that two or more guides cannot all be true at the same time.

Typical Critical issues include:

- Competing authority owners for the same lifecycle or business rule.
- A System Guide overriding a Core Guide.
- Presentation, projection, evidence, or browser state being treated as authority.
- A guide implying that uncertainty can finalize money, inventory, tenant access, identity, fulfillment, or lifecycle state.
- Cross-tenant or cross-system authority boundaries being contradicted.
- Shared Core domains having competing owners.

Critical findings must be resolved before guides can be considered architecturally consistent.

## High

A High finding is a business misunderstanding risk.

High findings may not directly assign competing authority, but they can cause QA Engineers, AI Agents, developers, or operators to misunderstand how the business system works.

Typical High issues include:

- Missing reciprocal relationship descriptions between related systems.
- Ambiguous actor boundaries.
- Lifecycle wording that could imply the wrong owner.
- Business rules described with incomplete fail-closed behavior.
- Cross references that lead readers away from the authoritative source.
- System responsibilities stated so broadly that they could be read as authority.

High findings should be corrected before relying on the guide for certification-driven QA.

## Medium

A Medium finding is a documentation inconsistency.

Medium findings reduce clarity or alignment but do not by themselves create a direct architectural contradiction.

Typical Medium issues include:

- Inconsistent section naming.
- Incomplete cross references.
- Missing relationship context where the authority is otherwise clear.
- Minor differences in wording that could be clarified.
- Duplicated summaries that should be replaced with references.

Medium findings should be corrected to improve maintainability and reduce future drift.

## Low

A Low finding is a terminology, readability, or editorial improvement.

Low findings do not change architecture, authority, ownership, or business meaning.

Typical Low issues include:

- Readability improvements.
- Minor terminology tightening.
- Formatting consistency.
- Reducing repetition.
- Clarifying a sentence that is already architecturally safe.

Low findings may be addressed opportunistically.

# Evidence Requirements

Two guides may be declared consistent only when there is sufficient documentation evidence.

Required evidence includes:

- Explicit statements of the relevant relationship.
- Matching ownership assignments.
- Matching terminology or explicitly documented terminology differences.
- Matching business actor boundaries.
- Matching authority boundaries.
- Matching lifecycle ownership.
- Matching fail-closed behavior where uncertainty exists.
- Matching distinction between presentation, evidence, and authority.
- Cross references to the correct authoritative guides.
- No contradictory statements in either guide.

Certification evidence must come from authoritative documentation.

Acceptable evidence includes:

- `.ai-context` authority files.
- Core Guides.
- System Guides.
- The Knowledge Base index when it identifies authoritative document relationships.

Evidence is insufficient when:

- A relationship is only implied.
- A guide is silent about a boundary that another guide depends on.
- The same term is used with different meanings without explanation.
- A System Guide summarizes a Core rule but changes its meaning.
- A guide relies on implementation behavior rather than Knowledge Base authority.
- A guide describes a business actor without defining what the actor cannot decide.

Absence of contradiction is not enough. Certification requires affirmative evidence that the guides describe the same architecture.

# Review Methodology

Certification review follows a repeatable methodology.

```text
Select two related guides
↓
Read both independently
↓
Compare terminology
↓
Compare actors
↓
Compare authority
↓
Compare ownership
↓
Compare lifecycle
↓
Compare cross references
↓
Record findings
↓
Resolve contradictions
↓
Repeat
```

The reviewer must not begin by assuming that one guide is correct because it is longer, newer, more detailed, or closer to implementation.

The reviewer should perform the comparison in this order:

1. Identify the relationship being certified.
2. Identify the authoritative Core Guide or `.ai-context` rule that governs the relationship.
3. Read each related guide independently.
4. List each guide's terminology for the relationship.
5. List each guide's business actors.
6. List each guide's claimed owner for the relevant authority.
7. List each guide's lifecycle or state model if applicable.
8. List each guide's evidence, presentation, projection, and recovery boundaries.
9. Compare cross references.
10. Classify differences using the severity levels.
11. Correct the documents that contradict higher authority.
12. Re-read the corrected sections.
13. Repeat the comparison until no contradiction remains.

The review must not include actual certification results in this standard. Results belong in future certification rounds or dedicated review reports.

# Certification Workflow

Future certification rounds should follow this workflow:

```text
Knowledge Base
↓
Relationship Selection
↓
Comparison
↓
Finding Classification
↓
Corrections
↓
Revalidation
↓
Certification
```

## Knowledge Base

Begin from the authoritative Knowledge Base corpus, including `.ai-context`, the index, Core Guides, and System Guides.

## Relationship Selection

Select a relationship that requires cross-system consistency.

Examples of relationship types include:

- A System Guide depending on a Core Guide.
- Two System Guides describing the same business interaction.
- A System Guide referencing a shared lifecycle.
- A System Guide presenting a domain owned by a Core Guide.
- A guide describing evidence produced by an external provider or derivative store.

## Comparison

Compare the selected documents across all consistency dimensions.

The comparison must be evidence-based and must not rely on assumptions about intended meaning.

## Finding Classification

Classify each inconsistency as Critical, High, Medium, or Low.

Severity must be based on architectural risk, not writing style preference.

## Corrections

Correct the document that conflicts with the higher authority.

Corrections should preserve the document's role. A System Guide should remain a System Guide. A Core Guide should remain a Core Guide. Certification should remain a method, not a source of business rules.

## Revalidation

After correction, re-read the relevant sections and repeat the comparison.

Revalidation confirms that the correction removed the contradiction without creating a new one.

## Certification

Only after revalidation may a future certification round record the relationship as certified according to the standard used at that time.

This document does not record such certification.

# Certification Outputs

Future certification rounds may produce standardized outcomes.

This document defines the possible outcome labels so future reports use consistent language. It does not assign any outcome to any guide, relationship, system, or certification round.

Possible outcomes include:

- PASS
- PASS WITH NOTES
- CORRECTIONS REQUIRED
- REVIEW BLOCKED
- NOT APPLICABLE

## PASS

The compared documents are architecturally consistent for the selected relationship under the standard used by the certification round.

## PASS WITH NOTES

The compared documents are architecturally consistent, but the reviewer recorded non-blocking notes, clarifications, or low-risk improvements.

## CORRECTIONS REQUIRED

The compared documents contain one or more inconsistencies that must be corrected before the selected relationship can be certified.

## REVIEW BLOCKED

The reviewer cannot complete certification because required evidence is missing, authoritative documents are unavailable, the relationship is insufficiently defined, or ambiguity cannot be resolved without higher-authority clarification.

## NOT APPLICABLE

The selected relationship does not require certification under this standard because the documents do not share a relevant architectural relationship, authority boundary, business rule, lifecycle, actor model, or cross-system dependency.

Actual certification results belong only to future certification reports.

# Future Certification Matrix

This section is a placeholder for future certification rounds.

Future reviewers may populate a certification matrix that tracks selected relationships, compared documents, finding severity, correction status, revalidation status, and certification status.

This version intentionally contains no matrix entries and no certification results.

# Future Certification Rounds

This document defines the certification standard only.

Future certification rounds will apply this standard to specific cross-system relationships. Each round should select a relationship, compare the relevant guides, record its own evidence, classify its own findings, apply corrections where required, revalidate the corrected documents, and reference this standard as the governing method.

Example future certification relationships include:

- POS Admin ↔ Storefront
- POS Admin ↔ CommerceHub
- POS Admin ↔ Affiliate
- POS Admin ↔ Service Marketplace
- POS Admin ↔ Super Admin
- Storefront ↔ CommerceHub
- CommerceHub ↔ Affiliate
- CommerceHub ↔ Super Admin
- Service Marketplace ↔ CommerceHub

Each certification round is independent.

Each round records its own findings.

Each round references this standard.

This document intentionally contains no review results.

# Source Documents

This standard depends on the authoritative Shopixy Knowledge Base corpus.

Primary source categories are:

- `INDEX.md`
- Core Guides
- System Guides
- `.ai-context`

This document depends on every authoritative guide but owns none of the business rules itself.

It defines the certification method only.

It does not define payment rules, inventory rules, order rules, security rules, tenant rules, accounting rules, affiliate rules, CommerceHub rules, Storefront rules, POS Admin rules, Service Marketplace rules, or Super Admin rules.

When this standard appears to conflict with an authoritative guide, the authoritative guide wins according to the Authority Precedence section.
