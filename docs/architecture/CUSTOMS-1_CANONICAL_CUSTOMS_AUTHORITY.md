# CUSTOMS-1 - Canonical Customs Authority

Status: Architecture authority freeze
Date: 2026-07-06
Scope: Carrier-neutral Customs Authority only
Runtime impact: none
Backend impact: none
Frontend impact: none
Carrier adapter impact: none
International shipment implementation impact: none

## Executive Summary

CUSTOMS-1 establishes Customs as a carrier-neutral platform authority for all
international carrier integrations. It is not a DHL phase, not an
international-shipping implementation phase, and not an adapter-contract phase.

The Customs Platform owns customs evidence admission, commodity/customs
classification evidence, declared customs value admission, commercial invoice
evidence admission, export evidence where modeled, and the shipment customs
snapshot boundary. Product, Catalog, Order, Shipment, Geographic, Payment, and
Carrier domains remain separate authorities with explicit handoffs.

The required pipeline is:

```text
Product
  -> Customs Evidence
  -> Shipment Customs Snapshot
  -> Carrier Neutral Customs DTO
  -> Carrier Adapter
  -> Provider
```

Carrier adapters consume an admitted carrier-neutral Customs DTO and serialize
it to provider wire contracts. They never invent HS Code, commodity
description, country of origin, declared customs value, quantity, weight, units,
commercial invoice evidence, export evidence, or customs snapshots.

No runtime behavior, EF model, SQL migration, Product redesign, Checkout
redesign, Shipment redesign, carrier adapter, DHL exportDeclaration, FedEx
customs, UPS customs, or international shipment creation is implemented in
CUSTOMS-1.

## Authority Matrix

| Concern | Authoritative owner | Consumer / writer boundary | Forbidden authority |
|---|---|---|---|
| HS Code | Customs Evidence, sourced from governed product customs classification evidence | Shipment snapshots admitted HS Code before carrier registration | Carrier adapters, provider responses, Checkout, browser, cache, free text |
| Commodity Description | Customs Evidence, sourced from product customs commodity evidence | Shipment snapshots admitted commodity text | Carrier adapters, provider templates, product display name fallback without admission |
| Country Of Manufacture | Customs Evidence where modeled | Shipment snapshots admitted manufacture country evidence | Carrier adapters, provider defaults, destination/origin geography inference |
| Country Of Origin | Customs Evidence, admitted through Customs and Geographic country evidence | Shipment snapshots admitted origin country evidence | Carrier adapters, provider defaults, warehouse country, destination country, browser |
| Declared Customs Value | Customs Evidence, derived from order/product financial evidence under Customs admission | Shipment snapshots admitted value, currency, quantity basis, and source evidence | Carrier adapters, provider minimums, frontend totals, stale pricing |
| Quantity | Order line / shipment line quantity admitted into Customs Snapshot | Shipment snapshot line item | Carrier adapters, provider package count, free text |
| Net Weight | Product/package measurement evidence admitted into Customs Snapshot | Shipment snapshot line item | Carrier adapters, provider defaults, volumetric calculation unless admitted |
| Gross Weight | Shipment/package measurement evidence admitted into Customs Snapshot | Shipment package/customs snapshot | Carrier adapters, provider defaults, tracking payloads |
| Units | Customs Evidence unit policy and order line unit evidence | Shipment snapshot line item | Carrier adapters, provider code guesses |
| Export Classification | Customs Evidence where modeled and governed by export/compliance policy | Shipment snapshot export evidence | Carrier adapters, provider-specific field defaults |
| Dangerous Goods flag | Product/compliance evidence where modeled, admitted by Customs | Shipment snapshot line item/package evidence | Carrier adapters, provider defaults, UI checkbox alone |
| Commercial Invoice Evidence | Customs Platform evidence boundary | Shipment customs snapshot and audit | Carrier adapters, PDF generator output alone, provider payload |
| Customs Snapshot | Shipment domain owns immutable historical snapshot after Customs admission | Carrier-neutral DTO is emitted from snapshot only | Product edits, Checkout recovery, carrier adapters, providers, cache |
| SQL Truth | PostgreSQL customs evidence and shipment customs snapshot tables in the target model | Recovery and audit re-enter SQL snapshots/evidence | Mongo, Redis, Elasticsearch, provider APIs, browser state |
| Runtime Authority | Customs admission plus Shipment-owned immutable snapshot | Carrier registration reads snapshot only | Carrier adapters, provider responses, frontend, projections |

## Ownership Matrix

| Question | Answer |
|---|---|
| Who owns HS Code? | Customs Evidence. Product may provide source classification data, but HS Code becomes carrier-usable only after Customs admission. |
| Who owns Country Of Origin? | Customs Evidence, with country identity admitted against Geographic country evidence where canonical geography is available. |
| Who owns Customs Value? | Customs Evidence, derived from order/product financial evidence and admitted with currency, quantity basis, and source proof. |
| Who owns Commodity? | Customs Evidence owns carrier-usable commodity description. Product display names are candidates only until admitted. |
| Who owns Customs Snapshot? | Shipment owns the immutable shipment customs snapshot after Customs admission. |
| Who owns Export Evidence? | Customs Platform owns export evidence where modeled; no carrier adapter owns it. |
| Who owns SQL Truth? | PostgreSQL Customs Evidence and Shipment Customs Snapshot persistence in the target model. |
| Who owns Runtime Authority? | Customs admission before shipment registration, then the immutable Shipment Customs Snapshot. |
| Carrier owns Customs? | No. Carriers consume admitted snapshot DTOs and return provider evidence only. |

## Pipeline

CUSTOMS-1 freezes the carrier-neutral handoff:

```text
Product customs source data
  -> Customs Evidence admission
  -> order/shipment line quantity and financial evidence binding
  -> Shipment Customs Snapshot creation
  -> carrier-neutral Customs DTO emission
  -> Carrier Adapter serialization
  -> Provider request
```

Rules:

- Product source fields are candidates until admitted by Customs.
- Order line quantity and financial evidence are consumed as source evidence,
  not overwritten by Customs.
- Shipment registration for international carrier-managed shipments must
  acquire a complete Customs Snapshot before adapter invocation.
- Carrier-neutral Customs DTOs are generated only from the immutable snapshot.
- Provider-specific request fields, including DHL `exportDeclaration`, FedEx
  customs shapes, UPS customs shapes, and Aramex international customs shapes,
  belong to later carrier adapter phases.

## Snapshot Model

The target Shipment Customs Snapshot is a shipment-owned immutable record with
line-level and document-level evidence.

Required line-level evidence for dutiable international goods:

- shipment ID, tenant ID, order ID, order line ID or shipment line ID;
- product ID and variant ID where applicable;
- customs evidence ID and evidence version;
- HS Code;
- commodity description;
- country of origin;
- country of manufacture where modeled;
- declared customs value, currency, and value source;
- quantity and unit;
- net weight and unit;
- gross weight and unit where line-level gross weight is modeled;
- dangerous goods flag where modeled;
- export classification where modeled;
- admission timestamp, admitted-by actor/system, and snapshot version.

Required document/package-level evidence:

- commercial invoice evidence ID or explicit commercial invoice pending policy;
- customs snapshot version;
- shipment/package gross weight and unit;
- carrier-neutral DTO version;
- source order/pricing/currency evidence;
- source product customs evidence fingerprint;
- audit correlation ID;
- rejection/failure reason when admission fails.

Snapshot version `1` is the architecture baseline. Later implementation may
choose normalized tables plus JSON audit payloads, but JSON must not be the only
SQL authority for queryable Customs Evidence or snapshot admission.

## Immutability

Once shipment registration begins, the Shipment Customs Snapshot is immutable.

Immutability rules:

- Product edits cannot rewrite historical shipment customs.
- Product classification corrections create new Customs Evidence versions for
  future shipments.
- Order price corrections do not rewrite an already acquired shipment customs
  snapshot; they require explicit shipment correction/re-registration policy in
  a later approved phase.
- Carrier retry and recovery reuse the existing snapshot.
- Provider responses, labels, tracking, invoice PDFs, webhooks, and adapter
  metadata cannot mutate the snapshot.
- Snapshot mutation after registration start is forbidden except by an explicit
  future correction workflow that preserves original evidence and creates a new
  auditable snapshot/version.

## Recovery

Recovery authority is:

```text
Shipment Customs Snapshot
  -> Customs Evidence SQL
  -> SQL Truth
```

Recovery must reuse the Shipment Customs Snapshot when it exists. It must not
rebuild customs from current Product rows, current catalog names, current
prices, carrier metadata, provider responses, regenerated invoices, Redis,
Mongo, Elasticsearch, browser state, or adapter defaults.

If the snapshot is missing, malformed, partial, wrong-tenant, wrong-shipment,
wrong-version, or inconsistent with indexed shipment linkage after registration
has begun, recovery fails closed and requires governed operational repair. It
does not call a carrier and does not attempt to infer customs data.

## Audit

CUSTOMS-1 requires append-only audit for:

- Customs Evidence creation, update, retirement, and versioning;
- HS Code admission and correction;
- commodity description admission and correction;
- country of origin/manufacture admission;
- declared customs value admission;
- dangerous goods and export classification admission where modeled;
- commercial invoice evidence admission;
- shipment customs snapshot acquisition;
- snapshot admission failure;
- carrier-neutral DTO emission;
- recovery reuse;
- recovery rejection;
- attempted post-registration mutation.

Audit records must include tenant, shipment, order, product/variant where
applicable, customs evidence ID/version, snapshot ID/version, actor/system,
timestamp, correlation ID, source evidence fingerprint, and non-PII failure
reason.

## Fail-Closed Rules

Shipment registration for dutiable international carrier-managed shipments must
fail before carrier network I/O when:

- HS Code is missing;
- country of origin is missing;
- commodity description is missing;
- declared customs value is missing;
- declared customs value currency is missing or invalid;
- quantity is missing, zero, negative, or not representable in admitted units;
- net weight is missing, zero, negative, or unitless;
- gross weight is required but missing, zero, negative, or unitless;
- unit code is missing or not admitted;
- commercial invoice evidence is required but missing;
- dangerous goods evidence is required by policy but missing;
- export classification is required by policy but missing;
- country evidence is unsupported or not admitted;
- snapshot is incomplete, malformed, mismatched, or wrong version;
- authority is not admitted by Customs;
- a carrier adapter attempts to supply or overwrite customs authority.

Fail closed means no provider call, no label request, no successful carrier
registration, and no provider-specific request construction from guessed data.

## Regression Strategy

Architecture-only certification tests must freeze:

- CUSTOMS-1 remains architecture-only.
- Customs owns carrier-usable HS Code, commodity, country of origin, declared
  value, commercial invoice evidence, and export evidence where modeled.
- Shipment owns the immutable Customs Snapshot after admission.
- Product source data is candidate/source evidence, not historical shipment
  authority after registration begins.
- Carriers and adapters cannot invent or rewrite customs data.
- Recovery reuses the snapshot and never rebuilds from live Product or carrier
  payloads.
- DHL `exportDeclaration`, FedEx customs, UPS customs, carrier adapters, and
  international shipment creation remain out of scope.
- The fail-closed matrix includes missing HS Code, origin country, commodity,
  declared value, invalid quantity, incomplete snapshot, and non-admitted
  authority.

## Future Carrier Integration

Future carrier phases consume CUSTOMS-1 as their source of truth:

- DHL may map the carrier-neutral DTO into MyDHL `exportDeclaration` only after
  CUSTOMS-1 runtime implementation and DHL-specific contract certification.
- FedEx, UPS, Aramex International, and future providers must consume the same
  carrier-neutral DTO and may not introduce provider-owned customs authority.
- Carrier-specific required fields must be declared as adapter contract
  requirements and mapped from the snapshot or fail closed.
- Provider-specific documents, labels, and customs payloads are output evidence,
  not Customs authority.
- A provider may reject or request correction, but that creates operational
  evidence; it does not mutate the snapshot automatically.

## Risks

| Risk | Status | Mitigation |
|---|---|---|
| Product fields are mistaken for shipment history | Mitigated by architecture | Customs Evidence is source/admission; Shipment Customs Snapshot is immutable history. |
| Carrier adapter invents missing HS Code or value | Mitigated by architecture | Adapter is serializer only; missing required customs data fails closed before I/O. |
| DHL exportDeclaration pressure smuggles DHL authority into platform model | Mitigated by architecture | CUSTOMS-1 forbids DHL-specific implementation and freezes carrier-neutral DTO first. |
| Legacy products lack HS Code or origin country | Accepted | International carrier registration fails closed until governed customs evidence exists. |
| Commercial invoice generation becomes authority | Mitigated by architecture | Invoice artifact is evidence/output; admitted commercial invoice evidence and snapshot own authority. |
| Product correction after shipment registration rewrites history | Mitigated by architecture | Corrections version future evidence and preserve historical snapshots. |
| Customs value conflicts with order financial authority | Residual | Runtime implementation must bind value admission to order/product financial evidence and currency policy. |
| Dangerous goods/export classification not yet modeled | Accepted | Where not modeled, no carrier adapter may invent it; future compliance phases must add governed evidence. |

## Final Recommendation

Approve CUSTOMS-1 as the canonical Customs Authority architecture freeze.

The next phase should be **CUSTOMS-2 - Customs Evidence SQL Model and Admission
Runtime**, limited to carrier-neutral Customs Evidence, shipment customs
snapshot persistence, fail-closed admission, audit, and architecture/runtime
tests. It should not implement DHL `exportDeclaration`, FedEx customs, UPS
customs, carrier adapters, or international shipment creation.

## Explicit Success Answers

- Reviewed ONLY Customs Authority? **Yes.**
- Runtime modified? **No.**
- Backend modified? **No runtime backend files modified.**
- Frontend modified? **No.**
- Architecture files added? **Yes, `docs/architecture/CUSTOMS-1_CANONICAL_CUSTOMS_AUTHORITY.md`.**
- Who owns HS Code? **Customs Evidence after Customs admission; Product source data is candidate/source evidence only.**
- Who owns Commodity? **Customs Evidence after Customs admission.**
- Who owns Customs Snapshot? **Shipment owns the immutable Shipment Customs Snapshot after Customs admission.**
- Carrier owns Customs? **NO.**
- Snapshot immutable? **Yes, once shipment registration begins.**
- Backward compatible? **Yes as architecture; no runtime or schema changed. Future international carrier registration should fail closed until evidence exists.**
- Production foundation ready? **Yes as architecture; runtime is not implemented yet.**
- Recommend next phase: **CUSTOMS-2 - Customs Evidence SQL Model and Admission Runtime.**
