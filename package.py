"""Package the Release DLL for Thunderstore local import."""
import json
import struct
from pathlib import Path
from tempfile import TemporaryDirectory
from xml.etree import ElementTree
from zipfile import ZIP_DEFLATED, ZipFile

root = Path(__file__).resolve().parent
listing = root / "thunderstore"
manifest = json.loads((listing / "manifest.json").read_text(encoding="utf-8"))
project = ElementTree.parse(root / "KingdomCharters.csproj")
name, version = manifest["name"], manifest["version_number"]
if name != "KingdomCharters" or version != project.findtext(".//Version"):
    raise SystemExit("Manifest name or version differs from the project.")
dll = root / "bin" / "Release" / project.findtext(".//TargetFramework") / f"{name}.dll"
files = {
    "manifest.json": listing / "manifest.json",
    "README.md": root / "README.md",
    "CHANGELOG.md": root / "CHANGELOG.md",
    "icon.png": listing / "icon.png",
    f"plugins/{name}.dll": dll,
}
icon = files["icon.png"].read_bytes()
if icon[:8] != b"\x89PNG\r\n\x1a\n" or struct.unpack(">II", icon[16:24]) != (256, 256):
    raise SystemExit("Expected a 256 × 256 PNG icon.")
output = root / "dist" / f"{name}-{version}.zip"
output.parent.mkdir(exist_ok=True)
with TemporaryDirectory(dir=output.parent) as scratch:
    staged = Path(scratch) / output.name
    with ZipFile(staged, "w", ZIP_DEFLATED) as archive:
        for entry, path in files.items():
            archive.write(path, entry)
    with ZipFile(staged) as archive:
        if archive.testzip() or set(archive.namelist()) != set(files):
            raise SystemExit("ZIP structure or CRC check failed.")
        for entry, path in files.items():
            if archive.read(entry) != path.read_bytes():
                raise SystemExit(f"Packaged bytes differ for {entry}.")
    staged.replace(output)
print(output)
