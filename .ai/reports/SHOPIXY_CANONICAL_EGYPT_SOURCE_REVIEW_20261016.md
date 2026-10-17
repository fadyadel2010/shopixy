# Egypt canonical source and mapping review — 2026-10-16

**RESEARCH ONLY. No independent reviewer, import, mapping acceptance or publication approval.** No canonical UUIDs or postal/hierarchy policies have been invented.

## Recommended source candidate and provenance

Use the **CAPMAS-origin Egypt COD administrative boundaries distributed by OCHA/HDX** as a source candidate for human review, not as an automatically approved current catalog:

- Dataset: https://data.humdata.org/dataset/cod-ab-egy
- Metadata: https://data.humdata.org/api/3/action/package_show?id=cod-ab-egy
- Dataset ID: `b90d81ba-7c7a-4283-9899-827480d80a79`; XLSX resource ID: `81126a96-2991-48e1-93cb-24c164a4de88`.
- Original organization: **Central Agency for Public Mobilization and Statistics (CAPMAS)**; distributor/contributor: **OCHA Field Information Services Section**. These are not Bosta-derived labels.
- Both public downloads returned **200**. Raw metadata/XLSX and SHA-256 evidence are retained under `.ai/evidence/phase7-20261015/canonical-source-research/`; workbook SHA-256: `9d8ed34df98f328048b0c2e8be28f2fcab27484f00b3f6fd55aa8851850a0250`.
- Coverage: country; **27 ADM1**, **365 ADM2**, **5,716 ADM3** (partial coverage); another **770 populated-place** rows require separate semantic review. ADM3 includes towns, districts and neighborhoods—not a uniform Shopixy locality/district hierarchy.
- Identities: retain dataset ID + level + exact PCode + version, parent PCodes, bilingual source names and validity fields. PCodes are source identifiers, not preexisting Shopixy UUIDs and not Bosta IDs.

### Currency and usage constraints

Metadata says valid/review span **2017-04-21 through 2024-12-19**, resource modified **2026-01-26**, update frequency **365 days**, due date **2025-12-19**, status **needs_update**. A recent metadata/resource timestamp does not establish that all boundaries are current.

Metadata declares **CC BY-IGO**, linking https://creativecommons.org/licenses/by/3.0/igo/legalcode, but also has `isopen=false`. Review that discrepancy and applicable attribution, license/link, modification marking, no-endorsement and IGO terms before commercial redistribution. Do not infer that public download equals unrestricted ownership. This is not legal advice or a usage approval.

Official alternatives were checked: the CAPMAS GIS page yielded no extractable dataset through the available fetch; the [UN SALB Egypt page](https://salb.un.org/en/data/egy) identifies the Egyptian Survey Authority but lists **no datasets currently available**. Neither observation justifies inventing a replacement authoritative dataset.

### Refresh procedure

Re-fetch metadata and the identified resource by GET, retain separate dated bytes/checksums, compare actual record identities/parents/validity, and stage an independently reviewed revision. Treat deleted/split/renamed units as reconciliation, not silent identity reuse. Preserve old source and publication evidence. The existing importer deliberately does not overwrite an established canonical catalog as an initial-bootstrap shortcut.

## Comparison with the persisted genuine Bosta draft

Compared against run `e9d38578-2e31-458f-9df6-8ba600917f42`, source hash `b237dd4f6cd6ace6135e6dd6c1af355499a7f6cdae696316bf211e23fba08b1a`, **621** references. Exact provider/source identifiers and parent relationships are retained in `mapping-review-manifest.json` (SHA-256 recorded in `mapping-review-summary.json`).

| Assessment | Count | Meaning |
|---|---:|---|
| Confirmed country ISO identity only | 1 | Authenticated provider country ID explicitly reports `EG`; source `iso2` is `EG`. Not a published canonical record or an approved mapping. |
| Exact-name candidates, not identity confirmations | 18 | Candidate ADM1 units for human review; all remain unaccepted. |
| No exact scoped-name candidate | 599 | Unresolved; not proof an official entity is missing. Includes transliteration differences and carrier delivery areas. |
| Provider unserviceable | 3 | One city and two districts; cannot be admitted by matching a canonical name. |

No city/district crosswalk was confirmed. Matching is deliberately restricted to NFC/trim/case-insensitive **exact** names and candidate parent scope; no fuzzy matching, transliteration coalescing or auto-acceptance. Zero multi-hit exact scoped-name results does **not** resolve broader semantic ambiguity. Source caveats explicitly report 23 English names with multiple Arabic names and 19 duplicate ADM2 English names within an ADM1.

Examples: Cairo has source candidate `EG01`, but that alone does not turn a Bosta city into a reviewed canonical administrative area. Bosta's observed Aswan code is `EG-21`, while the CAPMAS/OCHA Aswan PCode is `EG28`; removing hyphens is **not** a valid crosswalk. Bosta has 28 city/service regions versus 27 source governorates; `North Coast` remains unresolved rather than becoming an invented governorate. The target `VPRW3NEl5WQ` / Zahraa Nasr City has no confirmed administrative identity or published canonical locality chain.

Source integrity check found **zero duplicate level/PCode identities and zero missing immediate parent PCodes**, but **all 5,716 ADM3 English-name cells are empty**. Do not fabricate translations to pass bilingual import validation. All 6,109 administrative source entities remain absent from Shopixy's published catalog, which is still empty; that is an operational bootstrap gap, not permission to import every source row or assert source completeness.

## Admin review handoff

1. Inspect the review manifest's `sourceEntities`, per-provider `reviews`, `sourceOptions`, parent PCodes, serviceability and blockers. `canonicalId=null`, `mappingAccepted=false`, `publicationApproved=false` are intentional.
2. Independently decide source freshness/license acceptance and semantic level correspondence. Supply authoritative translations, locality lineage and country postal policy where required. Do not derive them from carrier labels.
3. An authorized source owner supplies stable canonical UUIDs and a valid typed canonical import manifest. Preview in Admin, stage a draft only, then obtain independent source review before validation/publication. The research-review manifest is deliberately **not** a valid application import request.
4. Review provider mappings against the published canonical hierarchy with exact provider IDs/parents/account/environment/credential binding. Re-sync stale provider references. Use the existing separate four-eyes/revision/checksum publication action only after approval.

No reviewer has been designated. No stage/import/accept/publish request was submitted from this research. Existing Admin publication guards remain in force.
