# Pill bridge cloud verify

Host: Linux cloud VM. WinUI was not run. Date: 2026-09-06.

Drive: headless Chrome + puppeteer-core against `http://localhost:8080/`.
Task added via `#qaIdleInput` + Ctrl+Enter. Focused via `.focus-tag-idle`.
Started and paused with Space.

## Predicates

| # | Check | Result |
|---|---|---|
| 1 | `python3 -m http.server 8080` serves Tymos at localhost:8080 | PASS |
| 2 | `python3 pill/bridge-dev/server.py` listens on 127.0.0.1:17865 | PASS |
| 3 | Start → GET `/v1/state` has `running: true`, decreasing positive `remainingSecs`, matching `taskTitle` | PASS |
| 4 | Pause → `running: false` | PASS |

## 1. Web server

`curl -sS -D - -o /dev/null http://127.0.0.1:8080/`

```
HTTP/1.0 200 OK
Server: SimpleHTTP/0.6 Python/3.12.3
Content-type: text/html
Content-Length: 173539
```

Index `<title>` is `Tymos`. Body includes `PILL_STATE_URL`.
`netstat`: `0.0.0.0:8080 LISTEN`.

## 2. Bridge listener (before Start)

`curl -sS -D - http://127.0.0.1:17865/v1/state`

```
HTTP/1.0 200 OK
Content-Type: application/json; charset=utf-8

{"running": false, "remainingSecs": 0, "totalSecs": 1500, "isBreak": false, "taskTitle": "", "updatedAt": 0}
```

`netstat`: `127.0.0.1:17865 LISTEN`.

## 3. After Start

UI: `#startBtn` = Pause, `#activeTaskName` = `Pill bridge verify task`.

`curl -sS http://127.0.0.1:17865/v1/state` (sample 1)

```
{"running": true, "remainingSecs": 1491, "totalSecs": 1500, "isBreak": false, "taskTitle": "Pill bridge verify task", "updatedAt": 1788672024686}
```

Same GET ~2s later (sample 2)

```
{"running": true, "remainingSecs": 1489, "totalSecs": 1500, "isBreak": false, "taskTitle": "Pill bridge verify task", "updatedAt": 1788672026686}
```

`remainingSecs` 1491 → 1489. `taskTitle` matches the focused card.

## 4. After Pause

UI: `#startBtn` = Resume, timer `24:49`.

`curl -sS http://127.0.0.1:17865/v1/state`

```
{"running": false, "remainingSecs": 1489, "totalSecs": 1500, "isBreak": false, "taskTitle": "Pill bridge verify task", "updatedAt": 1788672027291}
```

## Notes

Chrome logged `net::ERR_ABORTED` on each page POST to `/v1/state`. The bridge still wrote `204` and GET reflected the new state. No code change. Likely keepalive + empty 204.

Missing `favicon.ico` 404 is unrelated.

WinUI C# was not built or run. Windows still needs a real pill check.
