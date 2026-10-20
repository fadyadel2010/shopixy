// Use the repository's existing export-wrapper adapter for CLI function expressions.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { spawnSync } from 'node:child_process';

const root = process.cwd();
const files = fs.readdirSync('tests/browser').filter(name => /^coupon-checkout-20261019-.*\.js$/.test(name));
const results = [];
const commands = [];
for (const name of files) {
  const file = path.join(root, 'tests/browser', name);
  const source = fs.readFileSync(file, 'utf8');
  const args = [path.join(root, 'PointOfSale.Web/node_modules/eslint/bin/eslint.js'), '--stdin', '--stdin-filename', file,
    '--no-ignore', '--config', path.join(root, 'PointOfSale.Storefront/.eslintrc.cjs'),
    '--resolve-plugins-relative-to', path.join(root, 'PointOfSale.Web'), '--format', 'json'];
  const run = spawnSync(process.execPath, args, { cwd: path.join(root, 'PointOfSale.Storefront'), encoding: 'utf8',
    env: { ...process.env, ESLINT_USE_FLAT_CONFIG: 'false' }, input: `export default (\n${source}\n);\n`, maxBuffer: 8 * 1024 * 1024 });
  commands.push({ file: path.relative(root, file), sourceSha256: crypto.createHash('sha256').update(source).digest('hex'), exitCode: run.status, stderr: run.stderr });
  results.push(...JSON.parse(run.stdout));
}
const evidence = '.ai/evidence/coupon-checkout-20261019';
fs.writeFileSync(path.join(evidence, 'browser-lint.json'), JSON.stringify(results, null, 2));
const errors = results.reduce((sum, file) => sum + file.errorCount, 0);
const warnings = results.reduce((sum, file) => sum + file.warningCount, 0);
fs.writeFileSync(path.join(evidence, 'browser-lint-commands.json'), JSON.stringify({ commands, errors, warnings }, null, 2));
console.log(JSON.stringify({ files: files.length, errors, warnings }));
if (errors || commands.some(run => run.exitCode !== 0)) process.exitCode = 1;
