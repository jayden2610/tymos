"""Sync Hermes HQ Top 3 -> Tymos pomodoro blocks.

Next time just ask the assistant: "sync Tymos to my Hermes top 3".
It pulls the Top 3 from the Hermes HQ Notion page (via Composio),
calls this script with three --block args, and opens Tymos in your
real browser at http://localhost:8080/?seed=1 -- where index.html's
seed hook applies the tasks inside your own browser session (so
Supabase sync keeps working when you're signed in).

Manual usage:
  python scripts/sync-hermes-to-tymos.py ^
    --source "Hermes HQ Thu 3/9/26" ^
    --block "INF1104 - finish homework | 40 | finish homework | submitted/complete | open problem set Q1 | saved messages, Etsy" ^
    --block "Saved messages - go through | 20 | triage saved | inbox zero | oldest first | Etsy pack" ^
    --block "Etsy pack - continue work | 25 | continue pack | one shippable increment | resume repo | everything else"

Block format: "Title | minutes | objective | outcome | next | notNow"
Only Title is required; minutes defaults to 25.
"""
import argparse
import datetime
import json
import socket
import subprocess
import sys
import time
import webbrowser
from pathlib import Path

TYMOS_DIR = Path(__file__).resolve().parent.parent
SEED_FILE = TYMOS_DIR / "tymos-seed.json"


def parse_block(raw):
    parts = [p.strip() for p in raw.split("|")]
    title = parts[0]
    if not title:
        raise ValueError(f"empty title in block: {raw!r}")
    minutes = 25
    if len(parts) > 1 and parts[1]:
        minutes = int(parts[1])
    keys = ("objective", "outcome", "next", "notNow")
    block = {"title": title, "minutes": minutes}
    for key, val in zip(keys, parts[2:]):
        if val:
            block[key] = val
    return block


def server_up(port):
    try:
        with socket.create_connection(("127.0.0.1", port), timeout=1):
            return True
    except OSError:
        return False


def ensure_server(port):
    if server_up(port):
        return "already running"
    subprocess.Popen(
        [sys.executable, "-m", "http.server", str(port)],
        cwd=str(TYMOS_DIR),
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    for _ in range(30):
        if server_up(port):
            return "started"
        time.sleep(0.5)
    raise RuntimeError(f"http.server did not come up on port {port}")


def main():
    ap = argparse.ArgumentParser(description="Write tymos-seed.json and open Tymos with ?seed=1")
    ap.add_argument("--block", action="append", required=True,
                    help='"Title | minutes | objective | outcome | next | notNow" (repeat per block, max 9)')
    ap.add_argument("--source", default="Hermes HQ", help="label recorded in each task's notes")
    ap.add_argument("--port", type=int, default=8080)
    ap.add_argument("--no-open", action="store_true", help="write seed only, do not open browser")
    args = ap.parse_args()

    if len(args.block) > 9:
        ap.error("max 9 blocks")
    blocks = [parse_block(b) for b in args.block]

    seed = {
        "updatedAt": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "source": args.source,
        "blocks": blocks,
    }
    SEED_FILE.write_text(json.dumps(seed, ensure_ascii=False, indent=2), encoding="utf-8")

    url = f"http://localhost:{args.port}/?seed=1"
    status = ensure_server(args.port)
    if not args.no_open:
        webbrowser.open(url)

    print(f"seed: {SEED_FILE} ({len(blocks)} blocks, updatedAt={seed['updatedAt']})")
    for b in blocks:
        print(f"  - {b['title']} [{b['minutes']}m]")
    print(f"server: {status} -> {url}")


if __name__ == "__main__":
    main()
