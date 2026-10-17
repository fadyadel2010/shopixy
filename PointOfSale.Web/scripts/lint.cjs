// ESLint 9 still supports this project's maintained legacy configuration.
// Keep this explicit and cross-platform rather than introducing a flat-config migration.
const { spawnSync } = require('node:child_process')
const path = require('node:path')

const cli = path.join(path.dirname(require.resolve('eslint/package.json')), 'bin/eslint.js')
const targets = process.argv.slice(2)
// Options-only invocations still lint the maintained project scope. For a focused
// run, put the file/directory target first, just as in the normal npm command.
const args = targets.length && !targets[0].startsWith('-') ? targets : ['.', ...targets]
const result = spawnSync(process.execPath, [cli, ...args, '--ext', '.js,.jsx,.ts,.tsx'], {
  stdio: 'inherit',
  env: { ...process.env, ESLINT_USE_FLAT_CONFIG: 'false' },
})
if (result.error) throw result.error
process.exit(result.status ?? 1)
