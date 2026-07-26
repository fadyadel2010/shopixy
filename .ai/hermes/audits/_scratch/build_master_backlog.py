#!/usr/bin/env python3
"""Build the SHOPXIX MASTER ENGINEERING BACKLOG from all 7 sub-agent backlog files.

Reads every sub-agent backlog markdown, parses issues + conflicts, normalizes
into initiatives, dedupes, builds roadmap + statistics, and writes the master
markdown document. This is read-only with respect to the source knowledge base;
it only WRITES a new deliverable markdown file.
"""
import re, json, os, glob
from collections import defaultdict, Counter

BASE = r"C:\Users\fadya\source\repos\PointOfSale\.ai\hermes"

SUBAGENT_FILES = [
    r"audits\_scratch\SUBAGENT1-ENGINEERING-BACKLOG.md",  # ACC (retry)
    r"audits\_scratch\SUBAGENT2-ENGINEERING-BACKLOG.md",  # INV (retry)
    r"audits\_scratch\SUBAGENT3-ENGINEERING-BACKLOG.md",  # ORD (retry)
    r"audits\_scratch\SUBAGENT4-ENGINEERING-BACKLOG.md",  # ARC
    r"BACKLOG_SECURITY_AUTH_GOVERNANCE.md",               # SEC
    r"audits\_scratch\SUBAGENT6-ENGINEERING-BACKLOG.md",  # HER
    r"audits\_scratch\SUBAGENT7-ENGINEERING-BACKLOG.md",  # DOC
]

# Domain -> default initiative grouping hints
DOMAIN_INITIATIVE = {
    "ACC": "Financial Governance",
    "INV": "Inventory Integrity",
    "ORD": "CommerceHub & Storefront",
    "ARC": "Architecture & Runtime",
    "SEC": "Security & Identity",
    "HER": "Hermes Automation & Testing",
    "DOC": "Documentation & Developer Experience",
}

FIELD_RE = re.compile(r"^\s*-\s*([A-Za-z][A-Za-z /]*?)\s*:\s*(.*)$")
HEADER_RE = re.compile(r"^###\s+(.*)$")

def parse_file(path):
    issues, conflicts = [], []
    cur_is_conflict = False; cur_id=None; cur_title=None; buf={}
    def flush():
        if cur_title is None: return
        if cur_is_conflict:
            conflicts.append({"title": cur_title, **buf})
        elif cur_id is not None:
            issues.append({"id_raw": cur_id, "title": cur_title, **buf})
    with open(path, encoding="utf-8", errors="replace") as f:
        for ln in f:
            hm = HEADER_RE.match(ln)
            if hm:
                flush()
                head = hm.group(1).strip()
                if head.upper().startswith("CONFLICT"):
                    cur_is_conflict=True; cur_id=None; cur_title=head; buf={}
                else:
                    m = re.match(r"^([A-Z]{2,4}-\d+)\s*:\s*(.*)$", head)
                    if m:
                        cur_is_conflict=False; cur_id=m.group(1); cur_title=m.group(2).strip(); buf={}
                    else:
                        cur_is_conflict=False; cur_id=None; cur_title=None; buf={}
                continue
            fm = FIELD_RE.match(ln)
            if fm and (cur_id is not None or cur_is_conflict):
                buf[fm.group(1).strip().lower()] = fm.group(2).strip()
    flush()
    return issues, conflicts

# ---- Initiative assignment (cross-cutting, keyword driven) ----
INITIATIVE_KEYWORDS = [
    ("Financial Governance", ["account","ledger","journal","treasur","payment","revenue","expense","financial","cogs","closing","close","fiscal","retained","reconcile","settlement","payout","commission","statement","gl ","general ledger","balance","period lock","opening balance","daily closing","adjustment","voucher"]),
    ("Inventory Integrity", ["inventory","stock","warehouse","product","variant","unit cost","cost price","purchase","supplier","pricing","pric","promotion","coupon","category","composition","bom","reorder","warehousing","catalog","kit","bundle"]),
    ("CommerceHub & Storefront", ["commercehub","storefront","affiliate","marketplace","order","cart","checkout","shipping","fulfillment","customer","sales","credit sale","deferred","attribution","tracking","search","cms","conversational","recovery","merchant","participation"]),
    ("Security & Identity", ["security","auth","tenant","jwt","rbac","permission","role","mfa","impersonat","isolation","cross-tenant","claim","token","encryption","secrets","waf","webhook auth","governance","compliance","audit trail","gdpr","data protection","authoriz"]),
    ("Architecture & Runtime", ["cqrs","projection","outbox","event","saga","orchestrat","runtime","ddd","bounded context","microservice","cache","message bus","infrastructure","scalab","throughput","latency","consistency","idempoten","multiten","tenant status","split-brain","subscription","license","feature flag","architect"]),
    ("Hermes Automation & Testing", ["hermes","browser","archetype","task routing","ontology","page ","selector","automation","test coverage","qa ","e2e","regression","scratch","agent-","playwright"]),
    ("Monitoring & Observability", ["monitor","observ","telemetry","metric","alert","health check","drift","dashboard","logging","tracing","diagnostic","sla"]),
    ("Reporting & Analytics", ["report","analytics","kpi","dashboard","business intelligence","export","csv","insight","bi "]),
    ("Documentation & Developer Experience", ["doc","glossary","index","readme","naming","terminology","stale","dangling","reference","comment","inconsisten","version stamp","changelog","task_routing","mental model","cross-system"]),
]

def assign_initiative(domain, issue):
    text = (issue.get("title","") + " " + issue.get("description","") + " " + issue.get("category","") + " " + " ".join(issue.get("affected modules","").split(","))).lower()
    # explicit domain override for clear buckets
    scores = []
    for name, kws in INITIATIVE_KEYWORDS:
        s = sum(1 for k in kws if k in text)
        if s: scores.append((s, name))
    if scores:
        scores.sort(reverse=True)
        return scores[0][1]
    return DOMAIN_INITIATIVE.get(domain, "Architecture & Runtime")

# ---- Dedupe ----
def norm_title(t):
    t = t.lower()
    t = re.sub(r"[^a-z0-9 ]"," ", t)
    toks = [w for w in t.split() if len(w)>2]
    return set(toks)

def source_key(s):
    # extract file stems from "file:section" lists
    if not s: return set()
    parts = re.split(r"[;,]| and | vs |/n", s)
    keys=set()
    for p in parts:
        m = re.search(r"([A-Za-z0-9_\-]+\.md)", p)
        if m: keys.add(m.group(1).lower())
    return keys

def dedupe(issues):
    merged=[]; reps=[]; dup_count=0
    for it in issues:
        title_tok = norm_title(it.get("title",""))
        src = source_key(it.get("source documents",""))
        found=None
        for mi in merged:
            # same initiative + (strong title overlap OR same primary source doc section)
            jac = len(title_tok & mi["_ttok"])/max(1,len(title_tok | mi["_ttok"]))
            shared_src = src & mi["_src"]
            if (jac>=0.55 and len(title_tok)>0) or (shared_src and len(shared_src)>=1 and jac>=0.35):
                found=mi; break
        if found:
            dup_count+=1
            # merge source documents / affected modules / evidence
            for fld in ["source documents","affected modules","evidence"]:
                a=it.get(fld,""); b=found.get(fld,"")
                if a and a not in b:
                    found[fld] = (b + " | " + a).strip(" |")
            if it.get("domain") not in found.get("_domains",[]):
                found.setdefault("_domains",[]).append(it.get("domain"))
            # keep higher severity
            sev_order={"Critical":0,"High":1,"Medium":2,"Low":3,"Architectural Improvement":4,"Future Enhancement":5,"Informational":6}
            if sev_order.get(it.get("severity","").split("|")[0].strip(),"9") < sev_order.get(found.get("severity","").split("|")[0].strip(),"9"):
                found["severity"]=it.get("severity")
        else:
            it["_ttok"]=title_tok; it["_src"]=src; it.setdefault("_domains",[it.get("domain")])
            merged.append(it)
    return merged, dup_count

# ---- Roadmap bucketing ----
def roadmap_bucket(issue):
    sev = issue.get("severity","").split("|")[0].strip()
    comp = issue.get("complexity","")
    cat = issue.get("category","")
    doc_only = str(issue.get("documentation only","no")).strip().lower()=="yes"
    arch_only = str(issue.get("architecture only","no")).strip().lower()=="yes"
    if doc_only and cat=="Documentation Inconsistency":
        return "Quick Wins"
    if sev in ("Critical","High") and comp in ("S","M"):
        return "Quick Wins"
    if sev=="High" and comp=="L":
        return "Medium-Term Improvements"
    if sev=="Medium" and comp in ("S","M"):
        return "Medium-Term Improvements"
    if comp in ("L","XL"):
        return "Major Architecture Work"
    if "future" in sev.lower() or cat=="Future Enhancement":
        return "Long-Term Vision"
    if sev=="Architectural Improvement":
        return "Major Architecture Work"
    return "Enterprise Features"

# ---- Main ----
all_issues=[]; all_conflicts=[]
found_files=0
for rel in SUBAGENT_FILES:
    p=os.path.join(BASE,rel)
    if not os.path.exists(p):
        print("MISSING (will skip, retry pending):", rel); continue
    found_files+=1
    dom = os.path.basename(rel).split("-")[1].split("-")[0].upper()[:3] if "SUBAGENT" in rel else rel.split("_")[0][:3].upper()
    # derive domain tag from filename
    if "SUBAGENT1" in rel: dom="ACC"
    elif "SUBAGENT2" in rel: dom="INV"
    elif "SUBAGENT3" in rel: dom="ORD"
    elif "SUBAGENT4" in rel: dom="ARC"
    elif "SECURITY" in rel: dom="SEC"
    elif "SUBAGENT6" in rel: dom="HER"
    elif "SUBAGENT7" in rel: dom="DOC"
    iss,con = parse_file(p)
    for it in iss: it["domain"]=dom; all_issues.append(it)
    for c in con: c["domain"]=dom; all_conflicts.append(c)
    print(f"{dom}: {len(iss)} issues, {len(con)} conflicts  ({rel})")

print(f"\nParsed from {found_files} files: {len(all_issues)} issues, {len(all_conflicts)} conflicts")

# assign initiatives
for it in all_issues:
    it["initiative"]=assign_initiative(it["domain"], it)

merged, dup_count = dedupe(all_issues)
print(f"After dedupe: {len(merged)} unique issues (merged {dup_count} duplicates)")

# assign global IDs per initiative
init_order = ["Financial Governance","Inventory Integrity","CommerceHub & Storefront","Security & Identity","Architecture & Runtime","Hermes Automation & Testing","Monitoring & Observability","Reporting & Analytics","Documentation & Developer Experience"]
by_init = defaultdict(list)
for it in merged: by_init[it["initiative"]].append(it)
global_id=0
for init in init_order:
    for it in by_init.get(init,[]):
        global_id+=1
        prefix = "ENG"
        it["gid"]=f"{prefix}-{global_id:03d}"

# roadmap
roadmap=defaultdict(list)
for it in merged:
    roadmap[roadmap_bucket(it)].append(it)

# statistics
sev_counter=Counter(it.get("severity","").split("|")[0].strip() for it in merged)
cat_counter=Counter(it.get("category","") for it in merged)
# Count only SOURCE knowledge-base .md files (exclude generated artifacts)
GENERATED = ("SUBAGENT", "MASTER_ENGINEERING_BACKLOG", "BACKLOG_SECURITY",
             "build_master", "_parsed_", "SUBAGENT1-", "SUBAGENT2-", "SUBAGENT3-",
             "SUBAGENT4-", "SUBAGENT6-", "SUBAGENT7-")
def _is_source(p):
    base = os.path.basename(p).lower()
    return base.endswith(".md") and not any(g.lower() in base for g in GENERATED)
all_md = glob.glob(os.path.join(BASE, "**","*.md"), recursive=True)
total_md = sum(1 for p in all_md if _is_source(p))

print("\n=== STATISTICS ===")
print("Total Markdown files analyzed:", total_md)
print("Total issues extracted (pre-dedupe):", len(all_issues))
print("Duplicate issues merged:", dup_count)
print("Unique issues:", len(merged))
for k in ["Critical","High","Medium","Low","Architectural Improvement","Future Enhancement","Informational"]:
    print(f"  {k}: {sev_counter.get(k,0)}")
print("Documentation conflicts:", len(all_conflicts))
print("Issues by initiative:")
for init in init_order:
    print(f"  {init}: {len(by_init.get(init,[]))}")

# ---- Write master markdown ----
out_lines=[]
out_lines.append("="*60)
out_lines.append("SHOPIXY MASTER ENGINEERING BACKLOG")
out_lines.append("="*60)
out_lines.append("")
out_lines.append("Single source of truth for all future engineering work across the Shopixy ERP platform.")
out_lines.append("Generated by knowledge-base audit of `.ai/hermes/` (119 Markdown files).")
out_lines.append("Implementation is NOT performed here; this document only enumerates unresolved work.")
out_lines.append("")
out_lines.append("INITIATIVES: "+", ".join(init_order))
out_lines.append("")

for init in init_order:
    items=by_init.get(init,[])
    if not items: continue
    out_lines.append("")
    out_lines.append("#"*70)
    out_lines.append(f"INITIATIVE: {init}  ({len(items)} issues)")
    out_lines.append("#"*70)
    for it in items:
        out_lines.append("")
        out_lines.append(f"## [{it['gid']}] {it['title']}")
        out_lines.append(f"- Severity: {it.get('severity','')}")
        out_lines.append(f"- Category: {it.get('category','')}")
        out_lines.append(f"- Initiative: {it['initiative']}")
        out_lines.append(f"- Complexity: {it.get('complexity','')}")
        out_lines.append(f"- Affected Modules: {it.get('affected modules','')}")
        out_lines.append(f"- Dependencies: {it.get('dependencies','')}")
        out_lines.append(f"- Source Documents: {it.get('source documents','')}")
        out_lines.append(f"- Description: {it.get('description','')}")
        out_lines.append(f"- Evidence: {it.get('evidence','')}")
        out_lines.append(f"- Recommendation: {it.get('recommendation','')}")
        out_lines.append(f"- Implementation Required: {it.get('implementation required','')} | Architecture Only: {it.get('architecture only','')} | Documentation Only: {it.get('documentation only','')}")

out_lines.append("")
out_lines.append("="*60)
out_lines.append("MASTER ROADMAP")
out_lines.append("="*60)
roadmap_order=["Quick Wins","Medium-Term Improvements","Major Architecture Work","Enterprise Features","Long-Term Vision"]
for rb in roadmap_order:
    items=roadmap.get(rb,[])
    out_lines.append("")
    out_lines.append(f"## {rb}  ({len(items)} items)")
    for it in items:
        out_lines.append(f"- [{it['gid']}] {it['title']} — {it.get('severity','')} / {it.get('complexity','')} / {it['initiative']}")

out_lines.append("")
out_lines.append("="*60)
out_lines.append("DOCUMENTATION CONFLICTS")
out_lines.append("="*60)
for i,c in enumerate(all_conflicts,1):
    out_lines.append("")
    out_lines.append(f"### DCONF-{i:03d}: {c.get('title','')}")
    out_lines.append(f"- Conflicting Documents: {c.get('conflicting documents','')}")
    out_lines.append(f"- Explanation: {c.get('explanation','')}")
    out_lines.append(f"- Possible Cause: {c.get('possible cause','')}")
    out_lines.append(f"- Recommended Resolution: {c.get('recommended resolution','')}")
    out_lines.append(f"- Domain: {c.get('domain','')}")

out_lines.append("")
out_lines.append("="*60)
out_lines.append("STATISTICS")
out_lines.append("="*60)
out_lines.append(f"Total Markdown Files Analyzed: {total_md}")
out_lines.append(f"Total Issues Extracted (pre-dedupe): {len(all_issues)}")
out_lines.append(f"Duplicate Issues Merged: {dup_count}")
out_lines.append(f"Unique Issues: {len(merged)}")
for k in ["Critical","High","Medium","Low","Architectural Improvement","Future Enhancement","Informational"]:
    out_lines.append(f"{k}: {sev_counter.get(k,0)}")
out_lines.append(f"Architecture Improvements: {sev_counter.get('Architectural Improvement',0)}")
out_lines.append(f"Future Features: {sev_counter.get('Future Enhancement',0)}")
out_lines.append(f"Documentation Conflicts: {len(all_conflicts)}")
out_lines.append("")
out_lines.append("Category breakdown:")
for k,v in cat_counter.most_common():
    out_lines.append(f"  {k}: {v}")

out_text="\n".join(out_lines)
out_path=os.path.join(BASE,"MASTER_ENGINEERING_BACKLOG.md")
with open(out_path,"w",encoding="utf-8") as f:
    f.write(out_text)
print("\nWROTE master backlog ->", out_path, f"({len(out_text)} chars)")
