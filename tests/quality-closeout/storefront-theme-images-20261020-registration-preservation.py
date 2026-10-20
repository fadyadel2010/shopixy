"""Verify prior registration work by reversing ONLY this task's style substitutions in memory."""
import hashlib
import json
import pathlib

root = pathlib.Path(__file__).resolve().parents[2]
evidence = root / '.ai/evidence/storefront-theme-images-20261020'
baseline = json.loads((evidence / 'workspace-before.json').read_text(encoding='utf-8'))['sha256']
common = {
    'text-2xl font-bold text-text': 'text-2xl font-bold text-gray-900',
    'text-muted mt-2': 'text-gray-600 mt-2',
}
form = {
    **common,
    'block text-sm font-medium text-text mb-1.5': 'block text-sm font-medium text-gray-700 mb-1.5',
    'bg-background text-text placeholder:text-muted': 'text-gray-900 placeholder-gray-400',
    'disabled:bg-subtle disabled:text-muted': 'disabled:bg-gray-50 disabled:text-gray-500',
    'border-border hover:border-text': 'border-gray-200 hover:border-gray-300',
    'text-muted font-normal ms-1': 'text-gray-400 font-normal ms-1',
    'text-muted hover:text-text': 'text-gray-400 hover:text-gray-600',
    'mt-1.5 text-xs text-muted': 'mt-1.5 text-xs text-gray-500',
    'bg-primary text-on-primary font-medium': 'bg-[var(--color-primary)] text-white font-medium',
    'text-center text-sm text-muted mt-8': 'text-center text-sm text-gray-600 mt-8',
}
page = {
    **common,
    'min-h-screen flex items-center justify-center bg-background': 'min-h-screen flex items-center justify-center bg-gray-50',
    'min-h-screen flex flex-col bg-background text-text': 'min-h-screen flex flex-col bg-gray-50',
    'text-xl font-bold text-text hover:text-primary-text': 'text-xl font-bold text-gray-900 hover:text-[var(--color-primary)]',
    'bg-surface border border-border rounded-2xl shadow-xl': 'bg-white rounded-2xl shadow-xl',
    'text-muted': 'text-gray-500',
}
results = []
for name, substitutions in [('components/RegisterForm.tsx', form), ('pages/RegisterPage.tsx', page)]:
    path = f'PointOfSale.Storefront/src/auth/{name}'
    reconstructed = (root / path).read_text(encoding='utf-8')
    for new, old in substitutions.items():
        reconstructed = reconstructed.replace(new, old)
    hashes = [hashlib.sha256((bom + text).encode()).hexdigest()
              for text in [reconstructed, reconstructed.replace('\n', '\r\n')]
              for bom in ['', '\ufeff']]
    results.append({'file': path, 'inverseStyleByteHashMatches': baseline[path] in hashes,
                    'note': 'Non-gating byte reconstruction diagnostic, not a behavioral verdict. Mixed original line endings are not captured by a SHA-only baseline. Review scoped diff and registration regression results.'})
(evidence / 'registration-preservation.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
print(json.dumps(results))
