"""Read-only workspace manifest; never restores or rewrites existing work."""
import hashlib
import json
import pathlib
import subprocess
import sys

root = pathlib.Path(__file__).resolve().parents[2]
evidence = root / '.ai/evidence/storefront-theme-images-20261020'
evidence.mkdir(parents=True, exist_ok=True)
manifest = evidence / 'workspace-before.json'

def digest(path):
    return hashlib.file_digest(path.open('rb'), 'sha256').hexdigest()

if '--verify' not in sys.argv:
    if manifest.exists():
        raise SystemExit('Baseline already exists; refusing to overwrite')
    status = subprocess.check_output(['git', 'status', '--porcelain=v1', '-z', '-uall'], cwd=root).decode().split('\0')
    paths = subprocess.check_output(['git', 'ls-files', '-z', '--modified', '--others', '--exclude-standard'], cwd=root).decode().split('\0')
    hashes = {p: digest(root / p) for p in paths if p and (root / p).is_file()
              and not p.startswith(('.ai/evidence/storefront-theme-images-20261020/', '.playwright-cli/'))}
    manifest.write_text(json.dumps({'status': status, 'sha256': hashes}, indent=2), encoding='utf-8')
    print(f'Recorded {len(status)} status entries and {len(hashes)} existing-file hashes')
else:
    before = json.loads(manifest.read_text(encoding='utf-8'))
    changed = [p for p, h in before['sha256'].items() if not (root / p).is_file() or digest(root / p) != h]
    (evidence / 'workspace-preservation.json').write_text(json.dumps({'changedExistingFiles': changed}, indent=2), encoding='utf-8')
    print(json.dumps({'changedExistingFiles': changed}))
