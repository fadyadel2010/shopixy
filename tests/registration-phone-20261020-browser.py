"""Run headed CLI templates; preserve only explicit sanitized result JSON."""
import email
import email.policy
import html
import json
import os
import re
import secrets
import subprocess
import sys
from pathlib import Path
from urllib.parse import urlparse

sys.stdout.reconfigure(encoding='utf-8')
ROOT = Path(__file__).resolve().parents[1]
PRIVATE = Path(os.environ['LOCALAPPDATA']) / 'Temp/opencode/registration-phone-20261020'
OUT = ROOT / '.ai/evidence/registration-phone-20261020'
stage = sys.argv[1]
template_stage = stage.removesuffix('-final') if stage in ('matrix-final', 'verification-final') else stage
private_input = PRIVATE / 'input.json'
if private_input.exists():
    values = json.loads(private_input.read_text())
else:
    values = {'password': 'Qa1!' + secrets.token_urlsafe(24)}
    private_input.write_text(json.dumps(values))
values['verificationUrls'] = {}
values['suffix'] = '-v2' if stage in ('matrix-final', 'verification-final') else ''
for p in (PRIVATE / 'mail').glob('regphone20261020-*.eml'):
    message = email.message_from_bytes(p.read_bytes(), policy=email.policy.default)
    body = message.get_body(preferencelist=('plain', 'html')).get_content()
    match = re.search(r'https?://[^\s<>"\']+/verify-email\?[^\s<>"\']+', body)
    if match:
        url = html.unescape(match[0])
        if urlparse(url).hostname not in ('ahramstore.localhost', 'localhost'):
            raise RuntimeError('Refusing nonlocal verification link')
        values['verificationUrls'][p.stem.replace('regphone20261020-', '')] = url
template = ROOT / 'tests/browser' / ('registration-phone-20261020-' + template_stage + '.js')
script = PRIVATE / ('browser-' + stage + '.js')
script.write_text(template.read_text().replace('__REGISTRATION_PRIVATE_INPUT__', json.dumps(values)), encoding='utf-8')
run = subprocess.run(['playwright-cli.cmd', '-s=opencode-ui-test', 'run-code', '--filename=' + str(script)], cwd=ROOT, capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=600)
(PRIVATE / ('browser-' + stage + '.raw.txt')).write_text(run.stdout + '\n' + run.stderr, encoding='utf-8')
match = re.search(r'### Result\s*\n([^\r\n]+)', run.stdout)
if not match:
    # Do not echo CLI source because it contains the private test credential.
    error = re.search(r'### Error\s*\n(.+?)(?=###|$)', run.stdout, re.S)
    print('Browser script failed: ' + (error[1].splitlines()[0] if error else 'see private output'))
    sys.exit(1)
result = json.loads(match[1])
serialized = json.dumps(result, ensure_ascii=False, indent=2)
assert values['password'] not in serialized
assert not re.search(r'[?&]token=', serialized)
(OUT / ('browser-' + stage + '.json')).write_text(serialized, encoding='utf-8')
print(serialized)
sys.exit(run.returncode or (1 if result.get('failed') else 0))
