"""Assemble the shareable package from the common builds and reviewed V2.6 data."""

import argparse
import hashlib
import json
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile


HERE = Path(__file__).resolve().parent
VENDOR_ROOT = "BepInEx/plugins/V81TestChn/"
RUNTIME_ARCHIVE_SHA256 = "5d96a6f6a041261fc03c65caa0ad658650e5892ba6a8a6e408ecdfcbdac7be93"
RUNTIME_FILES = (
    "V81TestChn.dll",
    "fonts/NotoSansSC-VF.ttf",
    "fonts/zh-cn-tmp-font",
    "fonts/zh-cn-charset.txt",
    "fonts/OFL-1.1.txt",
    "textures/StartupFlashingLightsWarningLocalized.png",
    "translations-clean/cfg/zh-CN/clean-draft-runtime.cfg",
    "translations-clean/zh-CN.runtime.json",
)
CUSTOM_CFGS = (
    "Wesley/wesley-01.cfg",
    "Wesley/wesley-02.cfg",
    "Wesley/wesley-03.cfg",
    "Wesley-interactions.cfg",
)
TERMINAL_CFGS = ("wesley-terminal-01.cfg", "wesley-terminal-02.cfg")


def collect(args):
    files = {name: (HERE / name).read_bytes() for name in ("README.md", "manifest.json", "icon.png")}
    for component in ("WesleyDisplayZh.dll", "WesleyImageReader.dll"):
        files[f"plugins/{component}"] = (args.binaries / component).read_bytes()
    files["patchers/WesleyV81Interiors.dll"] = (args.binaries / "WesleyV81Interiors.dll").read_bytes()

    with args.runtime.open("rb") as archive:
        if hashlib.file_digest(archive, "sha256").hexdigest() != RUNTIME_ARCHIVE_SHA256:
            raise ValueError("Expected the official LC Chinese Project 3.3.2 archive")

    vendor = "plugins/V81TestChn/"
    with ZipFile(args.runtime) as upstream, ZipFile(args.candidate) as candidate:
        for name in RUNTIME_FILES:
            files[vendor + name] = upstream.read(VENDOR_ROOT + name)
        files[vendor + "LICENSE.txt"] = upstream.read("LICENSE")
        files[vendor + "THIRD_PARTY_LICENSES.md"] = upstream.read("THIRD_PARTY_LICENSES.md")
        for name in CUSTOM_CFGS:
            path = "custom-localization/" + name
            files[vendor + path] = candidate.read("BepInEx/config/V81TestChn/" + path)
        for name in TERMINAL_CFGS:
            path = "translations-clean/cfg/zh-CN/" + name
            files[vendor + path] = candidate.read(VENDOR_ROOT + path)
        files["plugins/Reader.wri"] = candidate.read("BepInEx/plugins/NT1Ho-WesleyImageReader/Reader.wri")

    if not all(files.values()):
        raise ValueError("Empty runtime file in package input")
    return files


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--candidate", type=Path, required=True, help="complete V2.6 candidate with reviewed text and Reader.wri")
    parser.add_argument("--runtime", type=Path, required=True, help="official Aueser LC Chinese Project 3.3.2 zip")
    parser.add_argument("--binaries", type=Path, required=True, help="directory with the compiled Wesley plugins and V81 patcher")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    files = collect(args)
    temporary = args.output.with_suffix(".tmp")
    with ZipFile(temporary, "w", ZIP_DEFLATED, compresslevel=6) as archive:
        for name, data in sorted(files.items()):
            # BepInEx keys its plugin type cache by bundle file write time.
            archive.writestr(name, data)
    with ZipFile(temporary) as archive:
        if set(archive.namelist()) != set(files) or archive.testzip() is not None:
            raise ValueError("Package failed archive verification")
    temporary.replace(args.output)

    with args.output.open("rb") as archive:
        digest = hashlib.file_digest(archive, "sha256").hexdigest()
    print(digest, args.output.name)
    inventory = {name: {"size": len(data), "sha256": hashlib.sha256(data).hexdigest()} for name, data in sorted(files.items())}
    args.output.with_suffix(".files.json").write_text(json.dumps(inventory, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
