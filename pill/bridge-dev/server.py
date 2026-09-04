#!/usr/bin/env python3
"""Dev stand-in for the WinUI StateServer (same contract on :17865).

Use on non-Windows CI/dev hosts. On Windows, prefer the WinUI pill process.
"""
from __future__ import annotations

import json
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any
from urllib.parse import urlparse

PORT = 17865
ALLOWED_ORIGINS = {
    "http://localhost:8080",
    "http://127.0.0.1:8080",
}

_lock = threading.Lock()
_state: dict[str, Any] = {
    "running": False,
    "remainingSecs": 0,
    "totalSecs": 1500,
    "isBreak": False,
    "taskTitle": "",
    "updatedAt": 0,
}


def _cors(handler: BaseHTTPRequestHandler) -> None:
    origin = handler.headers.get("Origin", "")
    if origin in ALLOWED_ORIGINS:
        handler.send_header("Access-Control-Allow-Origin", origin)
        handler.send_header("Vary", "Origin")
        handler.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        handler.send_header("Access-Control-Allow-Headers", "Content-Type")


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt: str, *args: Any) -> None:
        print(f"[pill-bridge] {self.address_string()} {fmt % args}")

    def do_OPTIONS(self) -> None:  # noqa: N802
        path = urlparse(self.path).path.rstrip("/")
        if path != "/v1/state":
            self.send_error(404)
            return
        self.send_response(204)
        _cors(self)
        self.end_headers()

    def do_GET(self) -> None:  # noqa: N802
        path = urlparse(self.path).path.rstrip("/")
        if path != "/v1/state":
            self.send_error(404)
            return
        with _lock:
            body = json.dumps(_state).encode("utf-8")
        self.send_response(200)
        _cors(self)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_POST(self) -> None:  # noqa: N802
        path = urlparse(self.path).path.rstrip("/")
        if path != "/v1/state":
            self.send_error(404)
            return
        length = int(self.headers.get("Content-Length", "0"))
        raw = self.rfile.read(length) if length else b"{}"
        try:
            parsed = json.loads(raw.decode("utf-8"))
        except json.JSONDecodeError:
            self.send_response(400)
            _cors(self)
            self.end_headers()
            self.wfile.write(b"invalid json")
            return
        if not isinstance(parsed, dict):
            self.send_response(400)
            _cors(self)
            self.end_headers()
            self.wfile.write(b"expected object")
            return

        next_state = {
            "running": bool(parsed.get("running", False)),
            "remainingSecs": max(0, int(parsed.get("remainingSecs", 0))),
            "totalSecs": max(0, int(parsed.get("totalSecs", 0))),
            "isBreak": bool(parsed.get("isBreak", False)),
            "taskTitle": str(parsed.get("taskTitle") or ""),
            "updatedAt": int(parsed.get("updatedAt") or 0),
        }
        with _lock:
            _state.clear()
            _state.update(next_state)
        self.send_response(204)
        _cors(self)
        self.end_headers()


def main() -> None:
    server = ThreadingHTTPServer(("127.0.0.1", PORT), Handler)
    print(f"[pill-bridge] listening on http://127.0.0.1:{PORT}/v1/state")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\n[pill-bridge] stopped")


if __name__ == "__main__":
    main()
