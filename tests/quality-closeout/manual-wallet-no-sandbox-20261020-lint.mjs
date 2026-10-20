import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

const root = process.cwd();
const files = fs.readdirSync('tests/browser').filter(n => /^manual-wallet-no-sandbox-20261020-.*\.js$/.test(n)).map(n => `tests/browser/${n}`);
const results = [], commands = [];
for (const file of files) {
    const run = spawnSync(process.execPath, [path.join(root, 'PointOfSale.Web/node_modules/eslint/bin/eslint.js'), '--stdin', '--stdin-filename', path.join(root, file), '--no-ignore', '--config', path.join(root, 'PointOfSale.Storefront/.eslintrc.cjs'), '--resolve-plugins-relative-to', path.join(root, 'PointOfSale.Web'), '--format', 'json'], {
        cwd: path.join(root, 'PointOfSale.Storefront'), encoding: 'utf8', env: { ...process.env, ESLINT_USE_FLAT_CONFIG: 'false' },
        input: `export default (\n${fs.readFileSync(file, 'utf8')}\n);\n`,
    });
    commands.push({file,exitCode:run.status,stderr:run.stderr});
    try { results.push(...JSON.parse(run.stdout)); } catch { commands.at(-1).output = run.stdout; }
}
const errors = results.reduce((sum, item) => sum + item.errorCount, 0);
const warnings = results.reduce((sum, item) => sum + item.warningCount, 0);
fs.writeFileSync('.ai/evidence/manual-wallet-no-sandbox-20261020/browser-lint.json', JSON.stringify({commands,results,errors,warnings}, null, 2));
console.log(JSON.stringify({files:files.length,errors,warnings}));
process.exitCode = errors || commands.some(c => c.exitCode !== 0) ? 1 : 0;
