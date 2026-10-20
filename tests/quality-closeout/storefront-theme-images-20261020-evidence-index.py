import json
import pathlib

root = pathlib.Path(__file__).resolve().parents[2] / '.ai/evidence/storefront-theme-images-20261020'
names = [
    'final-default-matrix.json',
    'final-dark-matrix.json',
    'final-fixtures.json',
    'content-preview-final.json',
    'contrast-default-final.json',
    'contrast-dark-final.json',
    'commerce-regression-final.json',
    'final-readback.json',
    'editor-preview-isolation.json',
    'workflow-and-restoration.json',
    'configuration-integrity-final.json',
    'draft-restored-after-outage.json',
]
for name in names:
    path = root / name
    if not path.exists():
        print(name, 'MISSING')
        continue
    data = json.loads(path.read_text(encoding='utf-8-sig'))
    if 'passed' in data:
        count = len(data.get('results') or data.get('stages') or [])
        print(f'{name}: records={count} passed={data["passed"]}')
    else:
        print(f'{name}: {json.dumps(data)[:200]}')
