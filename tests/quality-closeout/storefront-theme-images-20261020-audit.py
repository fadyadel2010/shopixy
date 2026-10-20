"""Read-only source/config inventory and evidence summary for this task."""
import json
import pathlib
import re

root = pathlib.Path(__file__).resolve().parents[2]
evidence = root / '.ai/evidence/storefront-theme-images-20261020'
authority = json.loads((root / 'theme-authority/shopixy-theme-authority.json').read_text(encoding='utf-8'))
baseline = json.loads((evidence / 'original-configuration.json').read_text(encoding='utf-8'))
supported = [t for t in authority['themes'] if t['officialStatus'] == 'official' and t['activationStatus'] == 'active' and t['runtimeAvailability']]
renderers = []
for path in (root / 'PointOfSale.Storefront/src').rglob('*'):
    if path.suffix not in ('.tsx', '.css') or '.test.' in path.name:
        continue
    lines = path.read_text(encoding='utf-8').splitlines()
    hits = [{'line': i, 'source': line.strip()} for i, line in enumerate(lines, 1)
            if re.search(r'<img|<OptimizedImage|<MediaFrame|backgroundImage:|height:\s*auto|h-auto|aspect-ratio', line)]
    if hits:
        renderers.append({'file': path.relative_to(root).as_posix(), 'hits': hits})
data = {
    'supportedRuntimeThemes': [{'id': t['id'], 'name': t['displayName'], 'defaults': t['defaultThemeConfig']} for t in supported],
    'excludedRuntimeThemes': [t['id'] for t in authority['themes'] if not t['runtimeAvailability']],
    'colorPresets': baseline['presets'],
    'tenantId': baseline['config']['tenantId'], 'storeName': baseline['config']['storeName'],
    'baselineRowVersion': baseline['config']['rowVersion'], 'baselineLiveVersion': baseline['history']['currentVersion'],
    'imageRenderers': renderers,
    'workflow': {
        'appearanceFields': ['primary', 'secondary', 'background', 'text', 'muted'],
        'appearanceScope': 'One tenant Appearance draft/live branch shared across runtime themes; not per-theme palette storage.',
        'projectionOrder': ['runtime theme canonical defaults', 'non-default appearance preset', 'customized legacy tenant theme values', 'draft or live overrides'],
        'preview': 'Authenticated materialized draft through nonce/origin/tenant-admitted runtime iframe; signed read-only theme links use draft values.',
        'save': 'Appearance overrides PUT with If-Match; preserves newer local edit generation.',
        'discard': 'Local candidate returns to last backend-accepted draft; does not replace saved draft with live.',
        'publish': 'Appearance-only promotion and StoreConfig history version; does not activate theme or publish content.',
        'restore': 'Republish original Appearance through the same supported concurrency/versioned boundary, not database manipulation.',
    },
}
(evidence / 'source-inventory.json').write_text(json.dumps(data, indent=2), encoding='utf-8')
print(f"{len(supported)} runtime themes; {len(baseline['presets'])} presets; {len(renderers)} media-related source files")
