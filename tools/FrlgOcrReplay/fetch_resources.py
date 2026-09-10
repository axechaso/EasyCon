"""Fetch the pinned FRLG digit resources and public replay fixtures."""
from __future__ import annotations

import argparse
import concurrent.futures
import hashlib
import json
from pathlib import Path
import urllib.request

ROOT = Path(__file__).resolve().parents[2]
REVISIONS = {
    "EasyConNS/EasyCon": "1aed001c0e2d3a32d211c39bec26546741626bd6",
    "PokemonAutomation/Arduino-Source": "a772133ebc497aed05439f464a6222d3d83e0c10",
    "PokemonAutomation/Packages": "e8cc29cdc9e9c16faf406a1d154d70ad687b375c",
    "PokemonAutomation/CommandLineTests": "46b892bd7f2106a1f34de11aa300b492aee06b83",
}
LOCK = ROOT / "docs/frlg-ocr-resources.lock.json"


def entries():
    for family in ("Digits", "LevelDigits", "DialogDigits"):
        for digit in range(10):
            yield "PokemonAutomation/Packages", f"Resources/PokemonFRLG/{family}/{digit}.png", f"src/EasyCon.Capture/Ocr/Frlg/Resources/{family}/{digit}.png"
    yield "PokemonAutomation/Arduino-Source", "LICENSE", "docs/licenses/PokemonAutomation-MIT.txt"
    for name in ("nyash_jpn_45345.png", "tom_eng_60895.jpg"):
        yield "PokemonAutomation/CommandLineTests", f"PokemonFRLG/TrainerIdReader/{name}", f"test/EasyCon.Tests/TestData/Frlg/{name}"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify", action="store_true", help="Verify existing files without network access")
    args = parser.parse_args()
    existing = json.loads(LOCK.read_text(encoding="utf-8")) if LOCK.exists() else None
    expected = {item["path"]: item["sha256"] for item in existing["files"]} if existing else {}

    def fetch(entry):
        repo, source, destination = entry
        url = f"https://raw.githubusercontent.com/{repo}/{REVISIONS[repo]}/{source}"
        target = ROOT / destination
        if args.verify:
            if destination not in expected:
                raise ValueError(f"Missing resource lock: {destination}")
            data = target.read_bytes()
        else:
            with urllib.request.urlopen(url, timeout=45) as response:
                data = response.read()
        digest = hashlib.sha256(data).hexdigest()
        if destination in expected and expected[destination] != digest:
            raise ValueError(f"Resource hash mismatch: {destination}")
        if not args.verify:
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
        return {"path": destination, "source": url, "sha256": digest, "bytes": len(data)}

    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
        files = list(pool.map(fetch, entries()))
    if not args.verify:
        LOCK.parent.mkdir(parents=True, exist_ok=True)
        LOCK.write_text(json.dumps({"revisions": REVISIONS, "files": files}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Verified {len(files)} pinned resources.")


if __name__ == "__main__":
    main()
