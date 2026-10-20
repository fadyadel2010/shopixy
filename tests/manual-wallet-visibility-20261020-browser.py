"""Run only task-owned scripts in the existing headed CLI session."""
import json
import re
import subprocess
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding='utf-8')
root = Path(__file__).resolve().parents[1]
stage = sys.argv[1]
script = root / f'tests/browser/manual-wallet-visibility-20261020-{stage}.js'
out = root / '.ai/evidence/manual-wallet-visibility-20261020'
run = subprocess.run(['playwright-cli.cmd', '-s=opencode-ui-test', 'run-code', '--filename=' + str(script)], cwd=root, capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=180)
(out / f'browser-{stage}.txt').write_text(run.stdout + run.stderr, encoding='utf-8')
match = re.search(r'### Result\s*\n([^\r\n]+)', run.stdout)
if not match:
    print(run.stdout + run.stderr)
    sys.exit(1)
result = json.loads(match[1])
(out / f'browser-{stage}.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(result, ensure_ascii=False, indent=2))
sys.exit(run.returncode or (1 if result.get('failed') else 0))
