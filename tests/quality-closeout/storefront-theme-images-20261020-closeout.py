"""Read-only evidence aggregation; does not access API, credentials, or merchant data stores."""
import hashlib
import json
import pathlib

root = pathlib.Path(__file__).resolve().parents[2]
evidence = root / '.ai/evidence/storefront-theme-images-20261020'

def load(name):
    return json.loads((evidence / name).read_text(encoding='utf-8-sig'))

before = load('original-configuration.json')
final = load('final-readback.json')
original, restored = before['config'], final['admin']['config']
excluded = {'rowVersion', 'updatedAt', 'updatedAtUtc'}
changes = [key for key in set(original) | set(restored)
           if key not in excluded and original.get(key) != restored.get(key)]
integrity = {
    'changedConfigurationFieldsExcludingConcurrencyAndTimestamp': sorted(changes),
    'originalRowVersion': original['rowVersion'], 'restoredRowVersion': restored['rowVersion'],
    'originalHistoryVersion': before['history'].get('currentVersion'),
    'restoredHistoryVersion': final['admin']['history'].get('currentVersion'),
    'exactDraftOverrideStringRestored': original['themeDraftOverridesJson'] == restored['themeDraftOverridesJson'],
    'exactLiveOverrideStringRestored': original['themeLiveOverridesJson'] == restored['themeLiveOverridesJson'],
    'activeThemeRestored': original['storefrontTheme'] == restored['storefrontTheme'],
}
(evidence / 'configuration-integrity-final.json').write_text(json.dumps(integrity, indent=2), encoding='utf-8')
matrices = {}
for name in ['final-default-matrix.json', 'final-dark-matrix.json', 'final-fixtures.json', 'content-preview-final.json']:
    if not (evidence / name).exists():
        matrices[name] = {'notExecuted': True}
        continue
    data = load(name)
    matrices[name] = {'records': len(data.get('results', [])), 'passed': data.get('passed'), 'failures': data.get('failures', [])}
contrast = {}
for name in ['contrast-default-final.json', 'contrast-dark-final.json']:
    if not (evidence / name).exists():
        contrast[name] = {'notExecuted': True}
        continue
    data = load(name)
    rows = [{**item, 'theme': record['theme'], 'path': record['path']} for record in data['results'] for item in record['exceptions']]
    contrast[name] = {'routes': len(data['results']), 'checked': sum(len(r['checked']) for r in data['results']),
                      'solidExceptions': [r for r in rows if not r['imageBackground']],
                      'manualImageGradientReviewCandidates': len([r for r in rows if r['imageBackground']])}
summary = {'configurationIntegrity': integrity, 'publicReadbackPassed': final.get('passed'),
           'matrices': matrices, 'contrast': contrast,
           'workspacePreservation': load('workspace-preservation.json')}
lint = load('focused-lint.json')
source_paths = lint['files'] + ['theme-authority/appearanceRoles.ts',
    'PointOfSale.Storefront/src/components/media/media.css',
    'PointOfSale.Storefront/src/components/products/VariantSelector.css',
    'PointOfSale.Storefront/src/styles/index.css', 'PointOfSale.Storefront/tailwind.config.js']
summary['verifiedSourceSha256'] = {name: hashlib.sha256((root / name).read_bytes()).hexdigest()
                                   for name in sorted(set(source_paths))}
(evidence / 'final-summary.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
print(json.dumps({'configurationIntegrity': integrity, 'publicReadbackPassed': final.get('passed'),
                  'matrices': {k: {field: v for field, v in value.items() if field != 'failures'} for k, value in matrices.items()},
                  'solidContrastExceptions': {k: len(v.get('solidExceptions', [])) for k, v in contrast.items()}}))
