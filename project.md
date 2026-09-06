---
name: tymos
status: shipped
deadline: null
links:
  repo: https://github.com/jayden2610/tymos
  deployed: https://jayden2610.github.io/tymos/
---

## One-liner
Minimal focus timer — Pomodoro + tasks + burning candle that earns its shelf.

## Direction / Goals
- Keep it boringly reliable: vanilla JS, no build, GitHub Pages deploys in 30s
- Distraction-free ritual: timer presets (25/50/90) + candle burn = tangible focus
- Maintain, don't expand — small polish only unless usage signals a new need

## Tasks
- [x] [P1] core: Timer presets + task cards (priority, subtasks, notes, est. sessions)
- [x] [P1] candle: Burn animation + shelf persistence (localStorage + Supabase sync)
- [x] [P1] polish: Keyboard-first (Space to start/pause), mobile responsive, stats/streaks
- [x] [P1] pill: bridge actually listens (TcpListener, no URL ACL) + capsule body — 2026-09-07
- [x] [P2] pill: see-through capsule corners (WinUI 3 blur-behind recipe — see pill/TRANSPARENCY-NOTES.md)
- [ ] [P2] polish: Tiny a11y pass on timer controls (focus rings, aria)
- [ ] [P3] idea: Weekly focus report export (if requested)

## Constraints
- Pill is a WinUI 3 / DComp window, not HTML — no CSS. See-through window
  corners need the blur-behind per-pixel alpha path (a `SystemBackdrop` subclass
  replicating WinUIEx's `TransparentTintBackdrop` recipe — `PillTransparentBackdrop`);
  the naive version crashes WASDK 1.6. Full matrix in `pill/TRANSPARENCY-NOTES.md`.
- Loopback servers in this repo must bind raw sockets (`TcpListener`), never
  `HttpListener` — http.sys needs a URL ACL for non-elevated processes.
- Keep the web app vanilla/static (no build); the pill is the only compiled piece.

## Milestones
- [x] Live on GitHub Pages | 2026-07-01
- [x] Candle + stats shipped | 2026-07-15
- [ ] A11y polish | 2026-09-20
- [x] Pill capsule corners (see-through, blur-behind) | 2026-09-07

## Notes / Log
- Run locally: python -m http.server 8080. Push to master auto-deploys.
- Shipped — in maintenance mode. No active build pressure.
- 2026-09-07: pill session — killed frozen-pill bug (bridge never listened:
  HttpListener URL ACL), rewrote StateServer on TcpListener (verified e2e with
  a real session via Playwright), capsule body + hairline shipped. Transparency
  (see-through corners) investigated to a hard constraint; findings + next
  steps in pill/TRANSPARENCY-NOTES.md.
- 2026-09-07: pill transparency shipped — see-through capsule corners via
  `PillTransparentBackdrop` (WinUIEx blur-behind recipe). The crash was ordering
  (missing WM_ERASEBKGND hook + DWM re-apply), not fundamental. Verified over
  white: corners transparent, body ~(50,45,40), no crash, survives refits.
