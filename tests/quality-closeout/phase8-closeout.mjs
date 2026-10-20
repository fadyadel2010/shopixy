// Narrow Phase8 summary: named evidence/task files only, never ignored snapshots.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { execFileSync } from 'node:child_process';

const evidence = '.ai/evidence/storefront-phase8-20261018';
const report = '.ai/reports/SHOPIXY_STOREFRONT_MANAGEMENT_PHASE8_FINAL_CROSS_DOMAIN_INTEGRATION_CERTIFICATION_20261018.md';
const read = file => JSON.parse(fs.readFileSync(path.join(evidence, file), 'utf8').replace(/^\uFEFF/, ''));
const hash = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const before = read('api-baseline.json');
const active = read('api-active-closeout.json');
const built = read('api-built-normal-equivalence.json');
const runtime = {
  pid: active.pid, startUtc: active.startUtc,
  environmentUnchanged: active.environmentFingerprint === before.environmentFingerprint,
  configurationUnchanged: JSON.stringify(active.configurationFiles) === JSON.stringify(before.configurationFiles),
  activeAssemblyMatches: active.assemblies.map(file => ({ path: file.path, mvid: file.mvid, sha256: file.sha256,
    matches: built.artifacts.some(candidate => path.basename(candidate.path) === path.basename(file.path) && candidate.sha256 === file.sha256 && candidate.mvid === file.mvid) })),
  note: 'Normal-layout rebuilt source-equivalence, not equality to the artifacts-layout install. Process module/disk identity, not managed-memory metadata proof.',
};
const frontend = ['admin-final.json', 'storefront-verified-final.json'].map(file => {
  const data = read(file);
  return { artifact: file, total: data.numTotalTests, passed: data.numPassedTests, failed: data.numFailedTests, pending: data.numPendingTests, success: data.success };
});
const browser = ['browser-readonly-final.json', 'browser-quote-current.json', 'browser-settlement.json', 'browser-marketing-cleanup.json',
  'browser-negative-submit.json', 'browser-ai-auth-controlled-current.json', 'browser-final-readback-terminal.json'].map(file => {
  const data = read(file);
  return { artifact: file, passed: data.checks.filter(x => x.passed).length, failed: data.checks.filter(x => !x.passed).length, failures: data.failures ?? [], errors: data.errors ?? [] };
});
const readiness = ['readiness-baseline.json', 'readiness-mid.json', 'readiness-final-1.json', 'readiness-final-2.json', 'readiness-final-3.json'].map(file => {
  const data = read(file);
  return { artifact: file, observedUtc: data.timestampUtc, status: data.status,
    dependencies: Object.fromEntries(Object.entries(data.entries).filter(([key]) => ['postgresql', 'mongodb', 'redis', 'rabbitmq', 'r2', 'certificate_provider', 'caddy_edge'].includes(key)).map(([key, entry]) => [key, { status: entry.status, description: entry.description }])) };
});
const financialBefore = read('merchant-observer-baseline.json');
const financialAfter = read('merchant-observer-final.json');
const paths = [report,
  'PointOfSale.API/Controllers/StorefrontChatController.cs', 'PointOfSale.Storefront/src/services/api/httpClient.ts',
  'PointOfSale.Storefront/src/services/api/httpClient.privacy.test.ts',
  'PointOfSale.Tests/AbandonedCart/AbandonedCartNativeSchedulerTests.cs', 'PointOfSale.Tests/AbandonedCart/NativeSchedulerConfiguredScope.cs',
  'PointOfSale.Tests/Admin/Phase8IntegrationConfiguredTests.cs', 'PointOfSale.Tests/Analytics/ProviderDeliveryContractTests.cs',
  'PointOfSale.Tests/Architecture/PosAdministrativeAuthorityCertificationTests.cs', 'PointOfSale.Tests/Architecture/StorefrontAnalyticsAuthorityCertificationTests.cs',
  'PointOfSale.Tests/CouponProjectionConsumerConsistencyTests.cs', 'PointOfSale.Tests/Infrastructure/Phase5HttpCacheTests.cs',
  'PointOfSale.Tests/Integration/ProductVariantPriceDistributedReliabilityIntegrationTests.cs',
  ...fs.readdirSync('tests/browser').filter(name => /^storefront-phase8-.*\.js$/.test(name)).map(name => 'tests/browser/' + name),
  ...fs.readdirSync('tests/quality-closeout').filter(name => /^phase8-/.test(name)).map(name => 'tests/quality-closeout/' + name),
];
fs.writeFileSync(path.join(evidence, 'task-files-manifest.json'), JSON.stringify(paths.map(file => ({ path: file, sha256: hash(file) })), null, 2));
fs.writeFileSync(path.join(evidence, 'runtime-equivalence.json'), JSON.stringify(runtime, null, 2));
const git = args => execFileSync('git', args, { encoding: 'utf8', maxBuffer: 16 * 1024 * 1024 });
const preservation = read('workspace-final.json');
const summary = { observedUtc: new Date().toISOString(), verdict: 'CONDITIONAL — UNRESOLVED INTERNAL FINDINGS', frontend, browser, readiness, runtime,
  finalBackend: read('backend-results.json').map(run => ({ artifact: run.artifact, counters: run.counters })),
  historical: { originalIssues: 42, passed: 5, failed: 34, notExecutedDistributed: 3, table: 'failure-reconciliation.json' },
  protectedHistory: { fingerprintBefore: financialBefore.fingerprint, fingerprintAfter: financialAfter.fingerprint,
    equal: financialBefore.fingerprint === financialAfter.fingerprint, phase6CostsEqual: JSON.stringify(financialBefore.costs) === JSON.stringify(financialAfter.costs),
    pixelEqual: JSON.stringify(financialBefore.config) === JSON.stringify(financialAfter.config), whatsappDisabled: financialAfter.whatsapp.Enabled === false },
  preservation: { headUnchanged: preservation.headUnchanged, indexUnchanged: preservation.indexUnchanged, deletedUnchanged: preservation.deletedUnchanged,
    missingBaselineUntracked: preservation.missing.length, ignoredSnapshotExceptions: 30, exactRestorations: 43,
    userAuthorizedRetainAndDocument: true, blanketBytePreservationClaim: false },
  git: { head: git(['rev-parse', 'HEAD']).trim(), index: git(['diff', '--cached', '--name-status']) },
  noExternalCertification: { dnsTlsCaddy: true, paymentCarrier: true, advertising: true, genuineOAuth: true, modelInferenceAndBilling: true },
  oldRemoteAiExecutionAndCost: 'UNKNOWN; local evidence only; no counters reset; no new provider execution to investigate',
  fullBackendSuite: false,
};
fs.writeFileSync(path.join(evidence, 'verification-summary.json'), JSON.stringify(summary, null, 2));
console.log(JSON.stringify({ frontend, browser: browser.map(x => ({ artifact: x.artifact, passed: x.passed, failed: x.failed })), runtimeMatches: runtime.activeAssemblyMatches.every(x => x.matches),
  protectedHistoryEqual: summary.protectedHistory.equal, taskFiles: paths.length, verdict: summary.verdict }, null, 2));
if (!runtime.activeAssemblyMatches.every(x => x.matches) || !runtime.configurationUnchanged || !runtime.environmentUnchanged ||
    frontend.some(x => x.failed || x.pending) || browser.some(x => x.failed || x.failures.length || x.errors.length) || !summary.protectedHistory.equal) process.exitCode = 1;
