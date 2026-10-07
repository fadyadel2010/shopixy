const { execFileSync } = require('node:child_process');
for (let index = 0; index < 10; index++) {
    const output = execFileSync('cmd.exe', ['/d', '/s', '/c', 'playwright-cli -s=opencode-ui-test --raw run-code --filename=tests/browser/homepageSharedRuntimeMiddle.cli.js'], { encoding: 'utf8', timeout: 90000 });
    if (output.includes('### Error') || !output.trim()) throw new Error(output || 'Incomplete CLI command');
    const result = JSON.parse(output.trim());
    console.log(JSON.stringify(result));
    if (result.done) break;
    if (result.cells !== 4) throw new Error('Incomplete middle screenshot certification');
    execFileSync('node', ['tests/browser/exportHomepageGapEvidence.cjs'], { timeout: 30000 });
}
