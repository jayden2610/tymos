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
- [ ] [P2] polish: Tiny a11y pass on timer controls (focus rings, aria)
- [ ] [P3] idea: Weekly focus report export (if requested)

## Milestones
- [x] Live on GitHub Pages | 2026-07-01
- [x] Candle + stats shipped | 2026-07-15
- [ ] A11y polish | 2026-09-20

## Notes / Log
- Run locally: python -m http.server 8080. Push to master auto-deploys.
- Shipped — in maintenance mode. No active build pressure.
