// Sequential headed playwright-cli certification, no delegated agents or browser mocks.
const { execFileSync } = require('node:child_process');
const pause = milliseconds => Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, milliseconds);
const run = file => {
    const output = execFileSync('cmd.exe', ['/d', '/s', '/c', `playwright-cli -s=opencode-ui-test --raw run-code --filename=tests/browser/${file}`], { encoding: 'utf8', timeout: 180000 });
    if (output.includes('### Error') || !output.trim()) throw new Error(output || `No result from ${file}`);
    const result = JSON.parse(output.trim());
    console.log(JSON.stringify(result));
    return result;
};
if (process.argv.includes('--reset')) run('homepageGapReset.cli.js');
const status = () => {
    for (let retry = 0; retry < 10; retry++) {
        try {
            return JSON.parse(execFileSync('cmd.exe', ['/d', '/s', '/c', 'playwright-cli -s=opencode-ui-test --raw run-code --filename=tests/browser/homepageGapCertificationStatus.cli.js'], { encoding: 'utf8', timeout: 30000 }));
        } catch (error) {
            if (!String(error.stdout).includes('Execution context was destroyed')) throw error;
            pause(2000);
        }
    }
    throw new Error('Browser did not settle after navigation');
};
let start = status().index;
run('homepageGapRestore.cli.js');
for (let index = start; index < 10; index++) {
    let result;
    const output = execFileSync('cmd.exe', ['/d', '/s', '/c', 'playwright-cli -s=opencode-ui-test --raw run-code --filename=tests/browser/homepageThemeDraftParity.cli.js'], { encoding: 'utf8', timeout: 180000 });
    if (output.includes('### Error')) throw new Error(output);
    if (output.trim()) result = JSON.parse(output.trim());
    else {
        // CLI modal bookkeeping can return while the accepted beforeunload
        // continuation is still executing. Wait for its actual recorded outcome.
        const deadline = Date.now() + 180000;
        while (Date.now() < deadline) {
            pause(2000);
            const state = status();
            if (state.error) throw new Error(state.error);
            if (state.index === index + 1) {
                result = { theme: state.result.theme, cells: state.result.cells.length,
                    maxTopDifference: Math.max(...state.result.cells.map(c => c.topPixels.ratio)),
                    maxFooterDifference: Math.max(...state.result.cells.map(c => c.footerPixels.ratio)) };
                break;
            }
        }
    }
    console.log(JSON.stringify(result));
    if (!result.theme || result.cells !== 4) throw new Error('Incomplete theme certification');
    run('homepageGapRestore.cli.js');
}
console.log('Completed all ten headed theme journeys with restoration after each comparison.');
