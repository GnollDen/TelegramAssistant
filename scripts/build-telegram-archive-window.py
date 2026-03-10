#!/usr/bin/env python3
import argparse
import json
from pathlib import Path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Build a Telegram Desktop export subset that keeps only the last N importable messages."
    )
    parser.add_argument("input", type=Path, help="Path to the source Telegram Desktop result.json")
    parser.add_argument("output", type=Path, help="Path to the subset result.json to write")
    parser.add_argument(
        "--count",
        type=int,
        default=1000,
        help="How many importable messages to keep from the tail (default: 1000)",
    )
    return parser.parse_args()


def is_importable_message(item: object) -> bool:
    if not isinstance(item, dict):
        return False

    item_type = item.get("type")
    item_id = item.get("id")
    return item_type == "message" and isinstance(item_id, int) and item_id > 0


def main() -> int:
    args = parse_args()
    if args.count <= 0:
        raise SystemExit("--count must be > 0")

    with args.input.open("r", encoding="utf-8-sig") as fh:
        root = json.load(fh)

    if not isinstance(root, dict):
        raise SystemExit("Expected root object in Telegram Desktop export JSON")

    raw_messages = root.get("messages")
    if not isinstance(raw_messages, list):
        raise SystemExit("Expected root.messages array in Telegram Desktop export JSON")

    importable = [item for item in raw_messages if is_importable_message(item)]
    selected_ids = {item["id"] for item in importable[-args.count:]}
    subset_messages = [
        item
        for item in raw_messages
        if isinstance(item, dict)
        and item.get("type") == "message"
        and isinstance(item.get("id"), int)
        and item["id"] in selected_ids
    ]

    subset = dict(root)
    subset["messages"] = subset_messages

    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("w", encoding="utf-8", newline="\n") as fh:
        json.dump(subset, fh, ensure_ascii=False, indent=2)
        fh.write("\n")

    first_id = subset_messages[0]["id"] if subset_messages else None
    last_id = subset_messages[-1]["id"] if subset_messages else None
    print(
        f"built subset: total_importable={len(importable)} kept={len(subset_messages)} "
        f"first_id={first_id} last_id={last_id} output={args.output}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
