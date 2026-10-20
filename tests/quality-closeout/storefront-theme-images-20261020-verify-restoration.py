import json
import pathlib

root = pathlib.Path(__file__).resolve().parents[2]
evidence = root / '.ai/evidence/storefront-theme-images-20261020'
exact = json.dumps(
    {
        'colors': {
            'primary': '#2563eb',
            'secondary': '#0f172a',
            'background': '#ffffff',
            'text': '#020617',
            'muted': '#64748b',
        },
        'font': 'Inter',
        'borderRadius': 8,
    },
    separators=(',', ':'),
)
data = json.loads((evidence / 'draft-restored-after-outage.json').read_text(encoding='utf-8-sig'))
print(json.dumps(data, indent=2))
print('draft exact match:', data['draft'] == exact)
print('live exact match:', data['live'] == exact)
