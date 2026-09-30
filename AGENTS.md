# Workspace Agent Notes

## Playwright CLI on Windows

- Run `playwright-cli list` before opening a browser and reuse `opencode-ui-test` when it exists.
- Use short, separate commands. Never chain `close` and `open` in one shell invocation.
- Do not use `--persistent`, `--profile`, `--config`, or `--idle-timeout=0` for routine UI verification. These options can relaunch Microsoft Edge against its default profile, leave an unregistered visible window, and make the CLI invocation appear stuck.
- Start the session with `playwright-cli -s=opencode-ui-test open "http://localhost:5173/" --browser=msedge --headed` and allow at least 60 seconds for the initial Windows/Edge launch. Then use separate `snapshot`, `goto`, `click`, and `fill` commands. This CLI defaults to headless mode when `--headed` is omitted.
- A visible Edge window is not proof that Playwright owns it. Trust `playwright-cli list`; if the named session is absent, start the isolated session above instead of trying to control the orphaned window.
- If the initial `open` command times out, run `playwright-cli list` once. Reuse `opencode-ui-test` if registered; otherwise rerun the same isolated `open` command with a 60-second timeout even when the first Edge window is visible.
- Keep each Playwright command on a finite terminal timeout. Do not terminate unrelated user-owned Edge processes.
