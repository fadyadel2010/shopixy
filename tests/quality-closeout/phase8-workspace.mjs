// Phase 8 read-only inventory. Never stage, restore, or delete workspace content.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { execFileSync } from 'node:child_process';

const root = process.cwd();
const evidence = path.join(root, '.ai/evidence/storefront-phase8-20261018');
const mode = process.argv[2];
const git = args => execFileSync('git', args, { maxBuffer: 128 * 1024 * 1024 });
const hash = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const list = buffer => buffer.toString().split('\0').filter(Boolean);
const write = (name, value) => fs.writeFileSync(path.join(evidence, name), JSON.stringify(value, null, 2));
fs.mkdirSync(evidence, { recursive: true });
if (mode === 'baseline') {
  if (fs.existsSync(path.join(evidence, 'workspace-baseline.json'))) throw new Error('Baseline already exists; refusing overwrite');
  const tracked = list(git(['ls-files', '-z']));
  const untracked = list(git(['ls-files', '--others', '--exclude-standard', '-z']));
  const files = tracked.filter(file => fs.existsSync(file)).map(file => ({ path: file, sha256: hash(file) }));
  write('workspace-baseline.json', { capturedUtc: new Date().toISOString(), head: git(['rev-parse', 'HEAD']).toString().trim(),
    index: git(['diff', '--cached', '--raw']).toString(), deleted: list(git(['ls-files', '--deleted', '-z'])), untracked, files });
  fs.writeFileSync(path.join(evidence, 'git-baseline.txt'), git(['status', '--porcelain=v2', '--branch', '--untracked-files=normal']));
  const phases = [];
  for (let phase = 1; phase <= 7; phase++) {
    const dir = `.ai/evidence/storefront-phase${phase}-${phase < 5 ? '20261016' : phase === 5 ? '20261017' : '20261018'}`;
    const candidates = ['task-files-manifest.json', 'final-task-files-manifest.json', 'task-source-paths.json'];
    for (const name of candidates) {
      const file = path.join(dir, name);
      if (fs.existsSync(file)) phases.push({ phase, file, sha256: hash(file) });
    }
  }
  write('prior-manifests.json', phases);
  console.log(JSON.stringify({ trackedHashed: files.length, baselineUntracked: untracked.length, phases }));
} else if (mode === 'final') {
  const before = JSON.parse(fs.readFileSync(path.join(evidence, 'workspace-baseline.json')));
  const changed = before.files.filter(file => !fs.existsSync(file.path) || hash(file.path) !== file.sha256);
  const missing = before.untracked.filter(file => !fs.existsSync(file));
  const headUnchanged = before.head === git(['rev-parse', 'HEAD']).toString().trim();
  const indexUnchanged = before.index === git(['diff', '--cached', '--raw']).toString();
  const deletedUnchanged = JSON.stringify(before.deleted) === JSON.stringify(list(git(['ls-files', '--deleted', '-z'])));
  const exception = JSON.parse(fs.readFileSync(path.join(evidence, 'privacy-scope-restoration.json'), 'utf8'));
  write('workspace-final.json', { observedUtc: new Date().toISOString(), headUnchanged, indexUnchanged, deletedUnchanged, changed, missing,
    baselineTrackedCount: before.files.length, baselineUntrackedCount: before.untracked.length,
    ignoredSnapshotException: { count: exception.olderSnapshotsStillContainingTaskRedactionMarker.length,
      paths: exception.olderSnapshotsStillContainingTaskRedactionMarker.map(file => '.playwright-cli/' + file),
      userAuthorizedRetainAndDocument: true, byteIdenticalPreservationProven: false, exactRestorations: exception.restored.length },
    ignoredFilesNotScannedAgain: true });
  fs.writeFileSync(path.join(evidence, 'git-final.txt'), git(['status', '--porcelain=v2', '--branch']));
  fs.writeFileSync(path.join(evidence, 'task-tracked.diff'), git(['diff', '--', ...changed.map(x => x.path)]));
  console.log(JSON.stringify({ headUnchanged, indexUnchanged, deletedUnchanged, changed: changed.map(x => x.path), missingCount: missing.length }));
} else throw new Error('Expected baseline or final');
