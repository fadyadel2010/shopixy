"""Read-only workspace comparison; writes only Phase 6 evidence artifacts."""
from pathlib import Path
import difflib
import hashlib
import json
import subprocess

ROOT = Path.cwd()
E = ROOT / '.ai/evidence/storefront-phase6-20261018'
sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest().upper()
baseline = json.loads((E / 'baseline-originals.json').read_text(encoding='utf-8-sig'))
comparisons, diff, changed = [], [], []
for item in baseline:
    relative = item['path'].replace('\\', '/')
    path = ROOT / relative
    original = E / 'originals' / relative
    state = 'missing' if not path.exists() else 'unchanged' if sha(path) == item['sha256'] else 'changed'
    comparisons.append(dict(path=relative, state=state))
    if state == 'changed':
        changed.append(relative)
        diff.extend(difflib.unified_diff(original.read_text(encoding='utf-8-sig').splitlines(True), path.read_text(encoding='utf-8-sig').splitlines(True), fromfile='baseline/' + relative, tofile='current/' + relative))
extras = {
    'PointOfSale.Storefront/src/components/products/ProductCardRuntime.tsx': 'product-card-original.tsx',
    'PointOfSale.Storefront/src/features/checkout/checkoutTelemetry.ts': 'checkout-telemetry-original.ts',
    'PointOfSale.Tests/Security/PosCloseoutHttpAuthorizationTests.cs': 'authorization-host-original.cs',
}
for relative, name in extras.items():
    path, original = ROOT / relative, E / name
    if sha(path) != sha(original):
        changed.append(relative)
        diff.extend(difflib.unified_diff(original.read_text(encoding='utf-8-sig').splitlines(True), path.read_text(encoding='utf-8-sig').splitlines(True), fromfile='baseline/' + relative, tofile='current/' + relative))
new = [
    'PointOfSale.Tests/Analytics/Phase6MarketingTests.cs', 'PointOfSale.Tests/Analytics/Phase6ConfiguredTests.cs',
    'PointOfSale.Tests/Security/Phase6HttpAuthorizationTests.cs',
    'PointOfSale.Storefront/src/analytics/hooks/usePageViewTracking.ts',
    'PointOfSale.Storefront/src/analytics/marketing.phase6.test.tsx',
    'PointOfSale.Storefront/src/analytics/AnalyticsQueue.phase6.test.ts',
    'PointOfSale.Storefront/src/features/checkout/checkoutTelemetry.phase6.test.ts',
    'PointOfSale.Web/src/modules/analytics/marketing.phase6.test.tsx',
    'PointOfSale.Web/src/modules/analytics/attribution/pages/CampaignCostsPage.d.ts',
    'PointOfSale.Web/src/modules/storefront/config/PixelTrackingConfigPage.d.ts',
    'PointOfSale.Web/src/modules/analytics/attribution/components/RoiSummaryCards.d.ts',
    'PointOfSale.Web/src/modules/analytics/attribution/hooks/useCampaignCost.d.ts',
    'tests/browser/storefront-phase6-baseline.js', 'tests/browser/storefront-phase6-marketing.js',
    'tests/browser/storefront-phase6-tracking.js', 'tests/browser/storefront-phase6-readonly.js',
    'tests/browser/storefront-phase6-report-readback.js',
    'tests/browser/storefront-phase6-controls.js',
    'tests/quality-closeout/phase6-baseline.ps1', 'tests/quality-closeout/phase6-install.ps1',
    'tests/quality-closeout/phase6-closeout.py',
    '.ai/reports/SHOPIXY_STOREFRONT_MANAGEMENT_PHASE6_MARKETING_ATTRIBUTION_ROI_20261018.md',
]
# Existing test was read in full before its narrow behavioral extension; no separate pre-edit copy.
changed.append('PointOfSale.Storefront/src/components/products/ProductCardRuntime.quality.test.tsx')
files = sorted(set(changed + new))
(E / 'task-files.txt').write_text('\n'.join(files) + '\n', encoding='utf-8')
(E / 'task-files-manifest.json').write_text(json.dumps([dict(path=p, sha256=sha(ROOT / p)) for p in files], indent=2), encoding='utf-8')
(E / 'task-local.diff').write_text(''.join(diff), encoding='utf-8')
(E / 'originals-comparison.json').write_text(json.dumps(comparisons, indent=2), encoding='utf-8')
git = lambda *args: subprocess.check_output(['git', *args], text=True, encoding='utf-8').strip()
deleted = (E / 'baseline-deleted.txt').read_text(encoding='utf-8-sig').splitlines()
untracked = (E / 'baseline-untracked.txt').read_text(encoding='utf-8-sig').splitlines()
preservation = dict(
    headUnchanged=git('rev-parse', 'HEAD') == (E / 'baseline-head.txt').read_text(encoding='utf-8-sig').strip(),
    indexEmpty=not git('diff', '--cached', '--raw'),
    baselineDeleted=len(deleted), recreatedDeleted=[p for p in deleted if (ROOT / p).exists()],
    baselineUntracked=len(untracked), missingUntracked=[p for p in untracked if not (ROOT / p).exists()],
    originalsChanged=sum(c['state'] == 'changed' for c in comparisons),
    originalsUnchanged=sum(c['state'] == 'unchanged' for c in comparisons),
    originalsMissing=sum(c['state'] == 'missing' for c in comparisons),
    taskFiles=len(files), byteLevelWholeRepositoryProof=False,
)
(E / 'preservation-final.json').write_text(json.dumps(preservation, indent=2), encoding='utf-8')
(E / 'final-git-status.txt').write_text(git('status', '--porcelain=v2', '--untracked-files=all') + '\n', encoding='utf-8')
print(json.dumps(preservation, indent=2))
