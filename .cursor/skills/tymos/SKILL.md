---
name: tymos
description: Drive Tymos on localhost. Add and select tasks, start the timer, and ensure the Windows floating pill (or bridge-dev) mirrors the session. Use when Jayden says /tymos, run Tymos, add Tymos tasks, or work the local task list.
---

# Tymos

Operate the local Tymos app at `http://localhost:8080/`. Repo root is the workspace (vanilla static files. No build).

Do not use GitHub Pages unless Jayden asks. Do not create tasks on the live signed-in account.

## Browser

Use `user-browsermcp` only (real Chrome). Never `cursor-ide-browser`. Discover schemas with GetDynamicTools before calling.

If navigate fails with no extension connection: tell Jayden to click the Browser MCP toolbar icon, then Connect on that tab. Retry. Cursor MCP auth is not the same as the tab Connect.

## Server

If `http://localhost:8080/` is down, from the tymos repo:

```
python -m http.server 8080
```

Run it in the background. Confirm with a GET. Then `browser_navigate` to `http://localhost:8080/`.

Hard-refresh after local code changes.

## Floating pill bridge

The companion pill reads live session state from `http://127.0.0.1:17865/v1/state`.

**Before Start**, ensure a listener is up:

1. Prefer the WinUI pill on Windows (`pill/winui/TymosPill`, `dotnet run`).
2. On Linux/CI or when WinUI is unavailable: `python3 pill/bridge-dev/server.py` in the background.
3. Confirm with `curl -s http://127.0.0.1:17865/v1/state` (JSON body).

**After Start**, verify the bridge:

```
curl -s http://127.0.0.1:17865/v1/state
```

Expect `running: true`, `remainingSecs` counting down, and `taskTitle` matching the focused task (or empty if none focused). On Windows with the WinUI shell running, the always-on-top pill should be visible at the approved bottom-center placement.

If POST fails silently, the web timer still runs. Fix the listener, then Start or wait one tick.

## Tasks

The add field is `#qaIdleInput` (placeholder "Add a task…"). Task cards are `#task-{id}` in `#taskList`. Selected card has class `active`.

**Add.** Focus `#qaIdleInput`, type the title, then Ctrl+Enter (Cmd+Enter on Mac). That commits without expanding the form. Do not press plain Enter if you meant Ctrl+Enter only. Plain Enter on the idle field also commits if there is text.

**Navigate.** ArrowDown from the add field selects the first card. ArrowDown/ArrowUp move between cards. ArrowUp on the first card returns focus to `#qaIdleInput`. Do not send arrows while a duration `type=number` input or a notes textarea is focused.

**Focus a task for the pill title.** Click the focus control on the card (or use the existing focus affordance) so it appears in `#activeTaskStrip` / `#activeTaskName`. The pill mirrors that title.

**Start a session.** If there are no tasks, Start opens Before you start (`#noTasksOverlay`). Add there with Ctrl+Enter (`ntmAddTask`), then Start focus. If a task already exists, Start runs the timer. Space toggles start/pause when focus is not in an input, textarea, or button.

**Verify.** Snapshot after add: the new title is in a `.task-card`. After arrows: `selectedTaskId` matches the highlighted card, or the add field is focused. After Start: bridge GET shows `running: true`.

## Do not

- Type into `#qaIdleInput` and submit in a way that destroys the node mid-keystroke. Prefer Ctrl+Enter after the full title is in the field.
- Drive `https://jayden2610.github.io/tymos/` for task writes.
- Export cookies.
- Kill the background server unless asked.
