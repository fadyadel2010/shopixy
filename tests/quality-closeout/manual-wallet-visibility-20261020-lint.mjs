import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

const root = process.cwd();
const out = '.ai/evidence/manual-wallet-visibility-20261020';
const browserFiles = fs.readdirSync('tests/browser').filter(name => /^manual-wallet-visibility-20261020-.*\.js$/.test(name));
const files = ['PointOfSale.Storefront/src/store/checkoutStore.validation.test.ts', ...browserFiles.map(name => `tests/browser/${name}`)];
const commands = [], results = [];
for (const file of files) {
    const source = fs.readFileSync(file, 'utf8');
    const run = spawnSync(process.execPath, [path.join(root, 'PointOfSale.Web/node_modules/eslint/bin/eslint.js'), '--stdin', '--stdin-filename', path.join(root, file), '--no-ignore', '--config', path.join(root, 'PointOfSale.Storefront/.eslintrc.cjs'), '--resolve-plugins-relative-to', path.join(root, 'PointOfSale.Web'), '--format', 'json'], {
        cwd: path.join(root, 'PointOfSale.Storefront'), encoding: 'utf8', env: {...process.env, ESLINT_USE_FLAT_CONFIG: 'false'},
        input: file.endsWith('.js') ? `export default (\n${source}\n);\n` : source,
    });
    commands.push({file, exitCode: run.status, stderr: run.stderr});
    try { results.push(...JSON.parse(run.stdout)); } catch { commands.at(-1).output = run.stdout; }
}
const errors = results.reduce((sum, item) => sum + item.errorCount, 0);
const warnings = results.reduce((sum, item) => sum + item.warningCount, 0);
fs.writeFileSync(`${out}/focused-lint.json`, JSON.stringify({commands, results, errors, warnings}, null, 2));
console.log(JSON.stringify({files: files.length, errors, warnings}));
process.exitCode = errors || commands.some(command => command.exitCode !== 0) ? 1 : 0;
