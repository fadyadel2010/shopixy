"""Focused command runner with exact native exit codes and UTF-8 evidence."""
import json
import os
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / '.ai/evidence/registration-phone-20261020'
STORE = ROOT / 'PointOfSale.Storefront'
mode = sys.argv[1]
commands = []
if mode == 'frontend':
    commands = [
        ('storefront-tests-final', ['npm.cmd', 'exec', '--', 'vitest', 'run', 'src/auth', 'src/services/api/httpClient.privacy.test.ts'], STORE),
        ('storefront-typecheck-final', ['npm.cmd', 'exec', '--', 'tsc', '--noEmit'], STORE),
        ('storefront-build-final', ['npm.cmd', 'run', 'build'], STORE),
    ]
elif mode == 'test-build':
    commands = [('backend-test-build-final', ['dotnet', 'build', 'PointOfSale.Tests/PointOfSale.Tests.csproj', '-c', 'Release', '--artifacts-path', '.artifacts/registration-phone-20261020'], ROOT)]
elif mode == 'backend-dev-final':
    commands = [('backend-final-development-build-completed', ['dotnet', 'build', 'PointOfSale.API/PointOfSale.API.csproj', '--no-restore'], ROOT)]
elif mode == 'admin-auth':
    commands = [('admin-auth-regression', ['npm.cmd', 'exec', '--', 'vitest', 'run', 'src/components/AuthBootstrap.test.tsx', 'src/services/api.authReplay.test.ts'], ROOT / 'PointOfSale.Web')]
elif mode == 'source-contracts':
    commands = [('customer-source-contracts-final', ['dotnet', 'vstest', '.artifacts/registration-phone-20261020/bin/PointOfSale.Tests/release/PointOfSale.Tests.dll',
        '/TestCaseFilter:FullyQualifiedName~StorefrontCustomerAuthorityCertificationTests',
        '/ResultsDirectory:' + str(OUT / 'trx'), '/Logger:trx;LogFileName=customer-source-contracts-final.trx'], ROOT)]
elif mode == 'lint':
    files = [
        'src/auth/api/authApi.ts', 'src/auth/api/authApi.registration.test.ts', 'src/auth/components/RegisterForm.tsx',
        'src/auth/pages/RegisterPage.tsx', 'src/auth/pages/RegisterPage.registration.test.tsx',
        'src/auth/store/authStore.ts', 'src/auth/store/authStore.test.ts', 'src/auth/types/auth.types.ts',
    ]
    commands = [('storefront-lint-final', ['node', str(ROOT / 'PointOfSale.Web/node_modules/eslint/bin/eslint.js'),
        '--config', '.eslintrc.cjs', '--resolve-plugins-relative-to', str(ROOT / 'PointOfSale.Web'), '--format', 'json', *files], STORE)]
results = []
for name, args, directory in commands:
    started = datetime.now(timezone.utc).isoformat()
    run = subprocess.run(args, cwd=directory, env={**os.environ, 'ESLINT_USE_FLAT_CONFIG': 'false'}, capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=900)
    (OUT / (name + '.log')).write_text(run.stdout + '\n' + run.stderr, encoding='utf-8')
    results.append({'name': name, 'command': args, 'cwd': str(directory), 'startedUtc': started, 'finishedUtc': datetime.now(timezone.utc).isoformat(), 'exitCode': run.returncode})
    print(json.dumps(results[-1]), flush=True)
(OUT / (mode + '-commands.json')).write_text(json.dumps(results, indent=2), encoding='utf-8')
sys.exit(1 if any(r['exitCode'] for r in results) else 0)
