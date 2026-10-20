#!/usr/bin/env python3
"""Build the XL layout for the coordinate-based LV1 Smart Slot action."""
import json
from pathlib import Path
from uuid import NAMESPACE_URL, uuid5
from zipfile import ZIP_DEFLATED, ZipFile, ZipInfo

ROOT = Path(__file__).resolve().parent.parent
PLUGIN = ROOT / "StreamDeck/com.lv1.faderbank.sdPlugin"


def identifier(name):
    return str(uuid5(NAMESPACE_URL, "com.lv1.faderbank/xl/" + name))


def build():
    profile_id = identifier("profile")
    page_id = identifier("page")
    pinned_id = identifier("pinned")
    actions = {}
    # Slots 0–23 are MON sends; 24–29 are MIX, USER, font −/+, VU, and FX.
    # The remaining two keys are intentionally available to the user.
    for slot in range(30):
        column, row = slot % 8, slot // 8
        actions[f"{column},{row}"] = {
            "ActionID": identifier(f"key-{slot}"),
            "LinkedTitle": True,
            "Name": "LV1 Smart Slot",
            "Settings": {},
            "State": 0,
            "States": [{"FontSize": 16, "Title": "", "TitleAlignment": "middle",
                        "TitleColor": "#FFFFFF", "ShowTitle": False}],
            "UUID": "com.lv1.faderbank.control",
        }
    files = {
        "manifest.json": {
            "Device": {"Model": "20GAT9902", "UUID": ""},
            "Name": "LV1 Bridge XL",
            "Pages": {"Current": page_id, "Default": pinned_id, "Pages": [page_id]},
            "Version": "3.0",
        },
        f"Profiles/{page_id.upper()}/manifest.json": {
            "Controllers": [{"Actions": actions, "Type": "Keypad"}],
            "Icon": "", "Name": "",
        },
        f"Profiles/{pinned_id.upper()}/manifest.json": {
            "Controllers": [{"Actions": None, "Type": "Keypad"}],
            "Icon": "", "Name": "",
        },
    }
    destination = PLUGIN / "Profiles/LV1 Bridge XL.streamDeckProfile"
    destination.parent.mkdir(parents=True, exist_ok=True)
    with ZipFile(destination, "w", ZIP_DEFLATED) as archive:
        for relative, data in files.items():
            entry = ZipInfo(f"{profile_id.upper()}.sdProfile/{relative}",
                            date_time=(2026, 10, 20, 0, 0, 0))
            entry.compress_type = ZIP_DEFLATED
            entry.external_attr = 0o100644 << 16
            archive.writestr(entry, json.dumps(data, ensure_ascii=False, separators=(",", ":")))
    print(destination)


if __name__ == "__main__":
    build()
