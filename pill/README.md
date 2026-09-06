# Tymos floating pill

Companion always-on-top overlay for Windows. Web Tymos owns the timer. The pill is a dark glass HUD at **bottom-center**: depleting ring + countdown, plus the focused task title when one is set.

> **Capsule / transparency status:** the pill body is a capsule (mock's
> `border-radius: 999px` + 1px hairline), but true see-through corners are
> blocked by a WinUI 3 compositor limitation — a dark band still surrounds the
> pill. Full investigation, dead ends, and next steps:
> [TRANSPARENCY-NOTES.md](TRANSPARENCY-NOTES.md).

## Quick start (Windows)

**Need once**
1. [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
2. Python 3 (already common on Windows; used only for the local web server)

**Run everything**

From the repo root in PowerShell:

```powershell
.\pill\run-windows.ps1
```

That script:
1. Starts Tymos on `http://localhost:8080` in a new window
2. Builds and launches the WinUI pill (listens on `http://127.0.0.1:17865`)
3. Opens Chrome to the app

Then click **Start** in Tymos. The pill should appear bottom-center with the countdown and focused task.

**Pill UI only (no bridge / no web)**

```powershell
.\pill\run-windows.ps1 -Demo
```

**Stop**

Close the pill window. Close the “Tymos web” PowerShell window (or Ctrl+C there).

## If `dotnet` is missing

```powershell
winget install Microsoft.DotNet.SDK.8
```

Restart the terminal, then rerun `.\pill\run-windows.ps1`.

## Manual steps (same thing, no script)

```powershell
# terminal 1 — web app
python -m http.server 8080

# terminal 2 — pill
cd pill\winui\TymosPill
dotnet restore
dotnet run -c Release -p:Platform=x64 --no-launch-profile
```

Open `http://localhost:8080`, focus a task, press Start.

## Linux / CI bridge only

WinUI will not build here. Use the same HTTP contract:

```bash
python3 -m http.server 8080
python3 pill/bridge-dev/server.py
# Start a session in the browser, then:
curl -s http://127.0.0.1:17865/v1/state
```

## LiveSessionState

`POST` / `GET` `http://127.0.0.1:17865/v1/state`

```json
{
  "running": true,
  "remainingSecs": 1472,
  "totalSecs": 1500,
  "isBreak": false,
  "taskTitle": "Ship floating pill",
  "updatedAt": 1736000000000
}
```

CORS allows `http://localhost:8080` and `http://127.0.0.1:8080` only.

## Layout

- `mock/` — visual spec (orb-first glass HUD, Placement A)
- `winui/TymosPill/` — WinUI 3 shell + state server
- `bridge-dev/server.py` — non-Windows stand-in for the state server
- `findings/` — evidence screenshots for the transparency investigation
- `TRANSPARENCY-NOTES.md` — what works / what's inert / what crashes, and next steps
- `run-windows.ps1` — one-command local setup

## Troubleshooting (Windows)

- `ExpandPriContent` / missing `AppxPackage` DLL: keep `<EnableMsixTooling>true</EnableMsixTooling>` in the csproj (unpackaged apps still need it for `dotnet build`).
- `app.manifest` must use root element `<assembly>`, not `<manifest>`.
- Prefer `dotnet run -c Release -p:Platform=x64` (the helper script does this). A bare `-r win-x64` without Platform can fail the XAML pass on some SDKs.
- Bridge not listening on `:17865`? Check `%TEMP%\tymos-pill.log` — the state server logs startup there. (Older builds used `HttpListener`, which needs a `netsh http add urlacl` reservation when non-elevated; the current `TcpListener` build needs nothing.)
- Pill process dies right after launch: check `%TEMP%\tymos-pill.log` — and make sure you did not pass `--alpha` (known crash, see TRANSPARENCY-NOTES.md).

## Agent path

See `.cursor/skills/tymos/SKILL.md`. Ensure the pill (or bridge-dev) is listening on `:17865` before Start. After Start, `GET /v1/state` should show `running: true`.
