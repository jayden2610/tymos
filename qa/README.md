# Tymos QA Workflow

Self-contained QA harness for the Tymos static app. No app dependencies — it loads
`../index.html` directly and exercises the real functions.

## Run it

```bash
cd qa
npm install
npx playwright install chromium   # one-time, downloads the headless browser
npm run qa                        # static + runtime
```

Individual stages:

```bash
npm run qa:static     # fast, no browser — wiring + syntax + dead code
npm run qa:runtime    # Playwright — drives the live DOM
```

## What it checks

### Stage 1 — `static-check.mjs` (no browser)
- Inline `<script>` **compiles** (syntax errors caught via `node:vm`).
- Every inline HTML handler (`onclick=...`, `oninput=...`, etc.) resolves to a **defined function** — catches typo'd / removed handlers.
- **Dead-code scan**: functions defined but never referenced anywhere.
- Every `getElementById('x')` has a matching element (static or runtime-injected).

### Stage 2 — `runtime-check.mjs` (Playwright/Chromium)
External hosts (Supabase, fonts, CDNs) are **blocked** so the run is
hermetic and offline — the app must degrade gracefully.
- No uncaught exceptions / non-network console errors on load.
- Core functions are wired (`typeof === 'function'`).
- **Timer state machine**: empty Start opens the overlay, Start anyway stamps
  `timerStartTs`, `tick()` decrements, reset restores `sessionUntouched`.
- **Break/work** phase toggle + `body.break-mode` class.
- **Task lifecycle**: idle Enter commits, overlay closes, then Start runs;
  expanded quick-add → render card → mark done → confirm-delete.
- **Settings/stats** persistence round-trip (localStorage).
- **Candle shelf** renders without throwing.
- **Focus duration** spinbutton updates `workSecs`.

## CI
`.github/workflows/qa.yml` runs both stages on every push to `master`, every PR,
and on manual dispatch.

## Extending
Add a new functional assertion in `runtime-check.mjs` using the `eq(label, got, want)`
helper and the `G(expr)` shortcut to read app globals in the page context.
