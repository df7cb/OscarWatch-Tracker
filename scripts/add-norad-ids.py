#!/usr/bin/env python3
"""Add norad_id fields to satellite_database.json from tle.oscarwatch.org."""

from __future__ import annotations

import json
import re
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DB_PATHS = [
    ROOT / "satellite_database.json",
    ROOT / "OscarWatch" / "Assets" / "satellite_database.json",
]
TLE_URL = "https://tle.oscarwatch.org/"
TLE_SEED = ROOT / "OscarWatch" / "Assets" / "tle-seed.txt"

# Name in database -> TLE catalogue name (when they differ)
NAME_ALIASES = {
    "RADFXSAT (FOX-1B)": "AO-91",
}

# Satellites not yet in the published TLE bundle (verified elsewhere)
MANUAL_NORAD = {
    "PARUS-T2": "64560",    # TASA / SpaceX Transporter-14
    "BOTAN": "65942",       # Chiba Institute of Technology (SatNOGS)
}


def parse_tle_text(text: str) -> dict[str, str]:
    lines = text.splitlines()
    mapping: dict[str, str] = {}
    i = 0
    while i < len(lines):
        name = lines[i].strip()
        if (
            name
            and not name.startswith("1 ")
            and not name.startswith("2 ")
            and i + 1 < len(lines)
            and lines[i + 1].startswith("1 ")
        ):
            norad = lines[i + 1][2:7].strip()
            mapping[name] = norad
            i += 3
            continue
        i += 1
    return mapping


def load_tle_mapping() -> dict[str, str]:
    with urllib.request.urlopen(TLE_URL, timeout=20) as resp:
        live = parse_tle_text(resp.read().decode("utf-8", errors="replace"))

    seed = parse_tle_text(TLE_SEED.read_text(encoding="utf-8")) if TLE_SEED.exists() else {}
    merged = {**seed, **live, **MANUAL_NORAD}
    return merged


def resolve_norad(name: str, tle: dict[str, str]) -> str | None:
    lookup = NAME_ALIASES.get(name, name)
    if lookup in tle:
        return tle[lookup]
    if name in tle:
        return tle[name]
    return MANUAL_NORAD.get(name)


def entry_to_ordered_dict(entry: dict) -> dict:
    ordered: dict = {"name": entry["name"]}
    if entry.get("norad_id"):
        ordered["norad_id"] = entry["norad_id"]
    ordered["modes"] = entry["modes"]
    return ordered


def update_database(path: Path, tle: dict[str, str]) -> tuple[list[str], list[str]]:
    entries = json.loads(path.read_text(encoding="utf-8"))
    matched: list[str] = []
    missing: list[str] = []

    for entry in entries:
        name = entry["name"]
        norad = resolve_norad(name, tle)
        if norad:
            entry["norad_id"] = norad
            matched.append(f"{name} -> {norad}")
        else:
            entry.pop("norad_id", None)
            missing.append(name)

    ordered = [entry_to_ordered_dict(entry) for entry in entries]
    path.write_text(json.dumps(ordered, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return matched, missing


def main() -> None:
    tle = load_tle_mapping()
    print(f"Loaded {len(tle)} TLE name mappings")

    for path in DB_PATHS:
        if not path.exists():
            print(f"SKIP missing: {path}")
            continue
        matched, missing = update_database(path, tle)
        print(f"\nUpdated {path}")
        print(f"  Matched: {len(matched)}")
        for line in matched:
            print(f"    {line}")
        if missing:
            print(f"  Missing NORAD ({len(missing)}):")
            for name in missing:
                print(f"    {name}")


if __name__ == "__main__":
    main()
