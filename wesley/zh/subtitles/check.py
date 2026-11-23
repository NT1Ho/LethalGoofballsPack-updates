"""Check timed subtitles and compare clip identities from the existing Wesley extractor."""

import argparse
from collections import defaultdict
import json
from pathlib import Path
import re
import unicodedata


root = Path(__file__).resolve().parent


def fields(path):
    return [line.split("\t") for line in path.read_text(encoding="utf-8-sig").splitlines()
            if line and not line.startswith("#")]


def snapshot(folder):
    result = defaultdict(set)
    for source in folder.glob("*.json"):
        bundle = json.loads(source.read_text(encoding="utf-8"))
        for kind, name, identity, duration, digest in bundle.get("clips", []):
            result[kind, name, identity].add(digest)
    if not result:
        raise ValueError(f"No clip fingerprints in {folder}. Use extract.py --media-fingerprints.")
    return result


def width(text):
    return sum(2 if unicodedata.east_asian_width(char) in ("F", "W") else 1 for char in text)


def verify():
    media = {}
    for kind, name, identity, duration, digest, status, bundle in fields(root / "media-index.tsv"):
        key = (kind, name, int(identity))
        if key in media and status == "字幕" and media[key][1] not in (digest, ""):
            raise ValueError(f"Ambiguous runtime media: {key}")
        if key not in media or status == "字幕":
            media[key] = (int(duration), digest, status)
    texts = defaultdict(list)
    last = {}
    for kind, name, identity, start, end, lane, value in fields(root / "tracks.zh-CN.tsv"):
        key = (kind, name, int(identity))
        if key not in media or media[key][2] not in ("字幕", "部分字幕"):
            raise ValueError(f"Track lacks audited media: {key}")
        assert kind in ("A", "V") and (lane == "D" or kind == "V" and lane == "W"), key
        assert 0 <= last.get((*key, lane), 0) <= int(start) < int(end) <= media[key][0], (key, start, end)
        last[(*key, lane)] = int(end)
        lines = value.split(r"\n")
        assert 1 <= len(lines) <= 2 and 0 < width(value) <= 137 and not re.search("[<>]", value), (key, value)
        texts[name].append(value)

    terms = dict(fields(root.parent / "terms.zh-CN.tsv")[1:])
    for name, approved in {
        "WM_Infernis": ("Cunningham", "Infernis"),
        "RadioLog1": ("Across The Stars", "Calist"),
        "secret_log": ("Cosmocos", "Gratar"),
        "babob hakw": ("Decimator",),
        "AsteroidTapeNew": ("Unified Dominion",),
    }.items():
        for entry in approved:
            assert terms[entry] in " ".join(texts[name]), (name, entry)
    for name, literals in {
        "secret_log": ("bit.ly/3", "大写 H"),
        "AsteroidTapeNew": ("56-Vow", "BIO-HXNV7", "BIO-JHCF3", "BIO-HNEX5", "BIO-GZHX4"),
        "Cunningham Log 3": ("第 6 个字母", "小写 z"),
        "Cunningham Log 4": ("大写 P",),
        "HarringtonResearch2": ("GYN-H", "LYN-H"),
    }.items():
        for literal in literals:
            assert literal in " ".join(texts[name]), (name, literal)
    found = {key[:3] for key in last}
    print(f"PASS {sum(len(lines) for lines in texts.values())} cues; {len(found)} media identities and shared terminology")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--compare", nargs=2, type=Path, metavar=("BEFORE_CACHE", "AFTER_CACHE"))
    args = parser.parse_args()
    verify()
    if args.compare:
        before, after = map(snapshot, args.compare)
        for key in sorted(before.keys() | after.keys()):
            if before.get(key) != after.get(key):
                action = "更新" if key in before and key in after else "新增" if key in after else "移除"
                print(action, *key, sep="\t")
