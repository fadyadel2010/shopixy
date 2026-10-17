// Read-only workspace/runtime reconciliation. Never recapture the baseline.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { execFileSync } from 'node:child_process';

const root = process.cwd();
const evidence = path.join(root, '.ai/evidence/storefront-phase7-20261018');
const readJson = name => JSON.parse(fs.readFileSync(path.join(evidence, name), 'utf8').replace(/^\uFEFF/, ''));
const hash = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const git = args => execFileSync('git', args, { maxBuffer: 64 * 1024 * 1024, stdio: ['ignore', 'pipe', 'pipe'] });
const checks = [];
const check = (name, passed, details) => checks.push({ name, passed, ...(details ? { details } : {}) });
const entries = {
    head: ['rev-parse', 'HEAD'], index: ['diff', '--cached', '--name-status'],
    deleted: ['ls-files', '--deleted'], untracked: ['ls-files', '--others', '--exclude-standard'],
};
for (const [name, args] of Object.entries(entries)) {
    const current = git(args);
    fs.writeFileSync(path.join(evidence, `final-${name}.txt`), current);
    if (name !== 'untracked') check(`${name} unchanged from captured baseline`, current.equals(fs.readFileSync(path.join(evidence, `baseline-${name}.txt`))));
}
const baselineUntracked = fs.readFileSync(path.join(evidence, 'baseline-untracked.txt'), 'utf8').trim().split(/\r?\n/).filter(Boolean);
const finalUntracked = git(entries.untracked).toString().trim().split(/\r?\n/).filter(Boolean);
const missing = baselineUntracked.filter(file => !fs.existsSync(path.join(root, file)));
check('All baseline untracked paths still exist', missing.length === 0, { baselineCount: baselineUntracked.length, missing });
const newlyUntracked = finalUntracked.filter(file => !baselineUntracked.includes(file));
fs.writeFileSync(path.join(evidence, 'task-new-untracked.json'), JSON.stringify(newlyUntracked, null, 2));
const originals = readJson('baseline-originals.json');
const sourceChanges = [];
let diff = '';
for (const entry of originals) {
    const original = path.join(evidence, 'originals', entry.path);
    if (hash(original) !== entry.hash) throw new Error(`Original snapshot integrity failure: ${entry.path}`);
    if (hash(path.join(root, entry.path)) === entry.hash) continue;
    sourceChanges.push({ path: entry.path, before: entry.hash, after: hash(path.join(root, entry.path)) });
    try { git(['diff', '--no-index', '--', original, path.join(root, entry.path)]); }
    catch (e) { if (e.status !== 1) throw e; diff += e.stdout.toString(); }
}
check('Captured source originals retain their recorded SHA256', true, { count: originals.length });
fs.writeFileSync(path.join(evidence, 'task-captured-source.diff'), diff);
fs.writeFileSync(path.join(evidence, 'task-captured-source-changes.json'), JSON.stringify(sourceChanges, null, 2));
const supplemental = ['PointOfSale.Storefront/src/services/api/httpClient.ts', 'PointOfSale.Storefront/src/i18n/locales/en.json',
    'PointOfSale.Storefront/src/i18n/locales/ar.json', 'PointOfSale.Tests/Security/PosCloseoutHttpAuthorizationTests.cs'];
let supplementalDiff = '';
for (const file of supplemental) {
    const original = path.join(evidence, 'additional-originals', file);
    try { git(['diff', '--no-index', '--', original, path.join(root, file)]); }
    catch (e) { if (e.status !== 1) throw e; supplementalDiff += e.stdout.toString(); }
}
fs.writeFileSync(path.join(evidence, 'task-supplemental-source.diff'), supplementalDiff);
const taskPaths = [...new Set([...sourceChanges.map(x => x.path), ...supplemental, 'PointOfSale.API/Program.cs',
    'PointOfSale.Storefront/src/types/chat.ts', ...newlyUntracked.filter(file => /^(PointOfSale\.|tests\/)/.test(file))])];
let whitespace = { command: 'git diff --check -- <task source paths>', exitCode: 0, output: '' };
try { git(['diff', '--check', '--', ...taskPaths]); }
catch (e) { whitespace = { ...whitespace, exitCode: e.status, output: e.stdout.toString() }; }
fs.writeFileSync(path.join(evidence, 'task-diff-check.json'), JSON.stringify(whitespace, null, 2));
check('Task-path git diff whitespace check', whitespace.exitCode === 0);
fs.writeFileSync(path.join(evidence, 'task-source-paths.json'), JSON.stringify(taskPaths, null, 2));
const active = readJson('api-active-closeout.json'), built = readJson('api-built-closeout.json'), baseline = readJson('api-baseline.json');
check('API environment fingerprint unchanged', active.environmentFingerprint === baseline.environmentFingerprint);
check('All baseline configuration hashes unchanged', JSON.stringify(active.configurationFiles) === JSON.stringify(baseline.configurationFiles));
for (const assembly of active.assemblies) {
    const match = built.artifacts.find(a => path.basename(a.path) === path.basename(assembly.path));
    check(`Active SHA256/MVID equals final build: ${path.basename(assembly.path)}`, !!match && match.sha256 === assembly.sha256 && match.mvid === assembly.mvid);
}
check('All three expected application assemblies observed', active.assemblies.length === 3);
const before = readJson('merchant-observer-baseline.json'), after = readJson('merchant-observer-final.json');
check('Prior order/journal/WhatsApp fingerprint unchanged', before.fingerprint === after.fingerprint);
check('Prior campaign costs and pixel configuration unchanged', JSON.stringify(before.costs) === JSON.stringify(after.costs) && JSON.stringify(before.config) === JSON.stringify(after.config));
const browserFiles = ['browser-hosted-verified-final.json', 'browser-readonly-verified-final.json', 'browser-callback-verified-final.json',
    'browser-controlled-expanded-verified-final.json', 'browser-password-login-closed-final.json', 'browser-disabled-chat-final.json'];
const browser = browserFiles.map(file => {
    const data = readJson(file);
    return { file, passed: data.checks.filter(c => c.passed).length, failed: data.checks.filter(c => !c.passed).length,
        failures: data.failures ?? [], errors: data.errors ?? [] };
});
check('Final browser groups pass all 80 assertions', browser.reduce((sum, item) => sum + item.passed, 0) === 80 && browser.every(item => !item.failed && !item.failures.length && !item.errors.length));
const frontend = ['admin-closeout.json', 'storefront-closed-final.json'].map(file => {
    const data = readJson(file);
    return { file, passed: data.numPassedTests, failed: data.numFailedTests, pending: data.numPendingTests, total: data.numTotalTests };
});
check('Final full frontend suites have no failures/pending', frontend.every(item => !item.failed && !item.pending && item.passed === item.total));
const backend = ['phase7-backend-closeout.trx', 'phase7-browser-callback-verified-final.trx', 'phase7-preservation-final.trx'].map(file => {
    const text = fs.readFileSync(path.join(root, 'PointOfSale.Tests/TestResults', file), 'utf8');
    const attributes = text.match(/<Counters\s+([^>]+)\/>/)[1];
    return { file, counters: Object.fromEntries([...attributes.matchAll(/(\w+)="(\d+)"/g)].map(match => [match[1], Number(match[2])])) };
});
check('Final selected backend/callback/observer runs have no failed or unexecuted tests', backend.every(item => item.counters.failed === 0 && item.counters.notExecuted === 0 && item.counters.passed === item.counters.total));
const lint = ['admin-lint-closeout.json', 'storefront-lint-closed-final.json'].map(file => {
    const data = readJson(file);
    return { file, files: data.length, errors: data.reduce((sum, item) => sum + item.errorCount, 0), warnings: data.reduce((sum, item) => sum + item.warningCount, 0) };
});
check('Focused lint has zero errors/warnings', lint.every(item => !item.errors && !item.warnings));
check('Required Phase7 report exists', fs.existsSync(path.join(root, '.ai/reports/SHOPIXY_STOREFRONT_MANAGEMENT_PHASE7_AI_ASSISTANT_AUTHENTICATION_PROVIDERS_20261018.md')));
fs.writeFileSync(path.join(evidence, 'verification-summary.json'), JSON.stringify({ observedUtc: new Date().toISOString(), frontend, backend, browser,
    browserAssertionsPassed: browser.reduce((sum, item) => sum + item.passed, 0), lint,
    boundary: { fullBackendSuite: false, genuineExternalOAuthVerified: false, genuineModelSuccessVerified: false,
        noExternalExecutionOrCostAssurance: false, earlyChatInterceptionMissedQuerySuffix: true },
    overlap: 'Preservation run includes two Phase7 observer tests already in selected backend. Callback host and its 12 browser assertions are different evidence layers.' }, null, 2));
const result = { observedUtc: new Date().toISOString(), checks, sourceChangesCount: sourceChanges.length, newlyUntrackedCount: newlyUntracked.length,
    limitation: 'Only captured source paths have pre-edit content hashes. Program.cs and some later shared files lack pre-edit snapshots; chat.ts additional snapshot is post-change. Baseline untracked comparison proves path retention, not unchanged contents.' };
fs.writeFileSync(path.join(evidence, 'closeout-reconciliation.json'), JSON.stringify(result, null, 2));
console.log(JSON.stringify(result, null, 2));
if (checks.some(c => !c.passed)) process.exitCode = 1;
