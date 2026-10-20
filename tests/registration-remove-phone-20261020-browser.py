"""Use the registered headed browser; retain sanitized results, never credentials."""
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
PRIVATE = Path(os.environ['LOCALAPPDATA']) / 'Temp/opencode/registration-remove-phone-20261020'
OLD_PRIVATE = PRIVATE.parent / 'registration-phone-20261020'
OUT = ROOT / '.ai/evidence/registration-remove-phone-20261020'
PRIVATE.mkdir(parents=True, exist_ok=True)
OUT.mkdir(parents=True, exist_ok=True)
stage = sys.argv[1]
template_stage = stage.removesuffix('-final')
private_input = PRIVATE / 'input.json'
if private_input.exists():
    values = json.loads(private_input.read_text())
else:
    values = {'password': 'Qa1!' + secrets.token_urlsafe(24), 'runId': secrets.token_hex(4)}
    private_input.write_text(json.dumps(values))
values['existingPassword'] = json.loads((OLD_PRIVATE / 'input.json').read_text())['password']
values['verificationUrls'] = {}
for path in (OLD_PRIVATE / 'mail').glob('regphone20261020-remove-*.eml'):
    message = email.message_from_bytes(path.read_bytes(), policy=email.policy.default)
    body = message.get_body(preferencelist=('plain', 'html')).get_content()
    match = re.search(r'https?://[^\s<>"\']+/verify-email\?[^\s<>"\']+', body)
    if match:
        url = html.unescape(match[0])
        if urlparse(url).hostname != 'ahramstore.localhost':
            raise RuntimeError('Refusing a verification link outside AhramStore')
        # Only substitute the isolated local frontend port; token and tenant stay intact.
        values['verificationUrls'][path.stem] = url.replace(':5174/', ':5175/')
template = ROOT / 'tests/browser' / ('registration-remove-phone-20261020-' + template_stage + '.js')
script = PRIVATE / ('browser-' + stage + '.js')
source = template.read_text(encoding='utf-8')
if template_stage != 'primary':
    source = source.replace(':5174', ':5175')
script.write_text(source.replace('__REGISTRATION_PRIVATE_INPUT__', json.dumps(values)), encoding='utf-8')
run = subprocess.run(['playwright-cli.cmd', '-s=opencode-ui-test', 'run-code', '--filename=' + str(script)], cwd=ROOT, capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=180)
(PRIVATE / ('browser-' + stage + '.raw.txt')).write_text(run.stdout + '\n' + run.stderr, encoding='utf-8')
match = re.search(r'### Result\s*\n([^\r\n]+)', run.stdout)
if not match:
    # Raw CLI output includes materialized source and is intentionally private.
    print('Browser stage failed; inspect private output without exposing credentials: ' + stage)
    sys.exit(1)
result = json.loads(match[1])
serialized = json.dumps(result, ensure_ascii=False, indent=2)
assert values['password'] not in serialized
assert values['existingPassword'] not in serialized
assert not re.search(r'[?&]token=', serialized)
(OUT / ('browser-' + stage + '.json')).write_text(serialized, encoding='utf-8')
print(serialized)
sys.exit(run.returncode or (1 if result.get('failed') else 0))
