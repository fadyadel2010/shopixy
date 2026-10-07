// Export evidence already produced by the registered headed playwright-cli session.
const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const artifacts = path.resolve('.artifacts');
if (!fs.existsSync(artifacts)) throw new Error('Expected workspace artifact directory does not exist');
const raw = execFileSync('cmd.exe', ['/d', '/s', '/c', 'playwright-cli -s=opencode-ui-test --raw run-code --filename=tests/browser/homepageGapEvidence.cli.js'], { encoding: 'utf8', timeout: 30000 });
let evidence = JSON.parse(raw.trim());
if (typeof evidence === 'string') evidence = JSON.parse(evidence);
const target = path.join(artifacts, 'homepage-final-two-gaps-browser-evidence.json');
const previous = fs.existsSync(target) ? JSON.parse(fs.readFileSync(target, 'utf8')) : {};
for (const key of Object.keys(previous)) {
    if (!evidence[key] || (Array.isArray(evidence[key]) ? evidence[key].length === 0 : Object.keys(evidence[key]).length === 0)) evidence[key] = previous[key];
}
fs.writeFileSync(target, JSON.stringify(evidence, null, 2));
console.log(JSON.stringify({ themes: evidence.themes.length, visualCells: evidence.themes.reduce((n, r) => n + r.cells.length, 0), signedMobileCells: evidence.signedMobile.length, middleCells: evidence.middle.length, security: evidence.security }));
