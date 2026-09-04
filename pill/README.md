# Tymos floating pill

Companion always-on-top overlay for Windows. Web Tymos owns the timer. The pill mirrors countdown + task title.

## Layout

- `mock/` — HTML visual mock for look and placement approval
- `winui/TymosPill/` — WinUI 3 + C# always-on-top shell + localhost state server
- `bridge-dev/server.py` — same HTTP contract for non-Windows verification

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

## Run (Windows)

```powershell
cd pill/winui/TymosPill
dotnet build
dotnet run
```

Demo chrome with sample state (no bridge):

```powershell
dotnet run -- --demo
```

Default placement is bottom-center, always on top. Drag the pill to move it. When `running` is false the pill hides.

## Run bridge on Linux / CI

```bash
python3 pill/bridge-dev/server.py
```

Serve the web app on port 8080, start a focus session, then:

```bash
curl -s http://127.0.0.1:17865/v1/state
```

## Agent path

See `.cursor/skills/tymos/SKILL.md`. Before Start, ensure the pill (or `bridge-dev/server.py`) is listening on `:17865`. After Start, `GET /v1/state` should show `running: true` and the task title.
