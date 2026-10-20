"""Capture task evidence without overwriting previous QA evidence."""
import json
import re
import subprocess
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding='utf-8')
root = Path(__file__).resolve().parents[1]
out = root / '.ai/evidence/manual-wallet-no-sandbox-20261020'
out.mkdir(parents=True, exist_ok=True)
stage = sys.argv[1]
label = sys.argv[2] if len(sys.argv) > 2 else stage
if stage.startswith('sql-'):
    source = (root / 'tests/manual-wallet-visibility-20261020-observer.py').read_text(encoding='utf-8')
    source = source.replace("manual-wallet-visibility-20261020", "manual-wallet-no-sandbox-20261020")
    if stage != 'sql-history':
        source = source.replace('MWVIS20261020%', 'MWNOSB20261020%')
    sys.argv = [str(root / 'tests/manual-wallet-visibility-20261020-observer.py'), stage[4:]]
    exec(compile(source, sys.argv[0], 'exec'), {'__file__': sys.argv[0], '__name__': '__main__'})
elif stage == 'mongo':
    source = (root / 'tests/manual-wallet-visibility-20261020-mongo.py').read_text(encoding='utf-8')
    source = source.replace('manual-wallet-visibility-20261020', 'manual-wallet-no-sandbox-20261020')
    exec(compile(source, str(root / 'tests/manual-wallet-visibility-20261020-mongo.py'), 'exec'), {'__file__': str(root / 'tests/manual-wallet-visibility-20261020-mongo.py'), '__name__': '__main__'})
else:
    script = root / f'tests/browser/manual-wallet-no-sandbox-20261020-{stage}.js'
    run = subprocess.run(['playwright-cli.cmd', '-s=opencode-ui-test', 'run-code', '--filename=' + str(script)], cwd=root, capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=180)
    (out / f'browser-{label}.txt').write_text(run.stdout + run.stderr, encoding='utf-8')
    match = re.search(r'### Result\s*\n([^\r\n]+)', run.stdout)
    if not match:
        print(run.stdout + run.stderr)
        sys.exit(1)
    result = json.loads(match[1])
    (out / f'browser-{label}.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(result, ensure_ascii=False, indent=2))
    sys.exit(run.returncode or (1 if result.get('failed') else 0))
