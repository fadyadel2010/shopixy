// Use the repository's export-wrapper adapter for Playwright CLI expressions.
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

const root = process.cwd();
const files = fs.readdirSync('tests/browser').filter(name => /^registration-phone-20261020-.*\.js$/.test(name));
const results = [], commands = [];
for (const name of files) {
  const file = path.join(root, 'tests/browser', name);
  const source = fs.readFileSync(file, 'utf8').replace('__REGISTRATION_PRIVATE_INPUT__', '({password:"runtime-placeholder",verificationUrls:{}})');
  const run = spawnSync(process.execPath, [path.join(root, 'PointOfSale.Web/node_modules/eslint/bin/eslint.js'),
    '--stdin', '--stdin-filename', file, '--no-ignore', '--config', path.join(root, 'PointOfSale.Storefront/.eslintrc.cjs'),
    '--resolve-plugins-relative-to', path.join(root, 'PointOfSale.Web'), '--format', 'json'],
  {cwd:path.join(root, 'PointOfSale.Storefront'),encoding:'utf8',env:{...process.env,ESLINT_USE_FLAT_CONFIG:'false'},input:`export default (\n${source}\n);\n`});
  commands.push({file,exitCode:run.status,stderr:run.stderr});
  results.push(...JSON.parse(run.stdout));
}
const errors = results.reduce((sum, file) => sum + file.errorCount, 0);
const warnings = results.reduce((sum, file) => sum + file.warningCount, 0);
fs.writeFileSync('.ai/evidence/registration-phone-20261020/browser-lint.json', JSON.stringify({commands,results,errors,warnings},null,2));
console.log(JSON.stringify({files:files.length,errors,warnings}));
process.exitCode = errors || commands.some(c => c.exitCode !== 0) ? 1 : 0;
