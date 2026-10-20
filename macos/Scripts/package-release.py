#!/usr/bin/env python3
"""Package an already-built universal release without changing the live installation."""
import argparse
import hashlib
import plistlib
from pathlib import Path
import shutil
import subprocess
import tempfile
from zipfile import ZipFile, ZIP_DEFLATED

root = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--dist", type=Path, default=root / ".dist")
parser.add_argument("--output", type=Path, default=root / "outputs")
args = parser.parse_args()
dist, output = args.dist.resolve(), args.output.resolve()
output.mkdir(parents=True, exist_ok=True)
version = plistlib.loads((dist / "LV1 Bridge.app/Contents/Info.plist").read_bytes())["CFBundleShortVersionString"]
plugin = root / "StreamDeck/com.lv1.faderbank.sdPlugin"
artifacts = []

def run(*arguments):
    subprocess.run([str(a) for a in arguments], check=True)

with tempfile.TemporaryDirectory(prefix="LV1Bridge-package-") as temporary:
    staging = Path(temporary)
    payload = staging / "root"
    for item, destination in [
        ("LV1 Bridge.app", "Applications/LV1 Bridge.app"),
        ("LV1Bridge_Control.bundle", "Applications/Waves/eMotion LV1/Modules/LV1Bridge_Control.bundle"),
    ]:
        run("codesign", "--verify", "--strict", dist / item)
        shutil.copytree(dist / item, payload / destination, symlinks=True)
    support = payload / "Library/Application Support/LV1Bridge"
    shutil.copytree(plugin, support / "StreamDeck" / plugin.name)
    shutil.copy2(root / "README-HE.md", support / "README-HE.md")
    shutil.copy2(root / "Scripts/uninstall.sh", support / "uninstall.sh")
    agents = payload / "Library/LaunchAgents"
    agents.mkdir(parents=True)
    shutil.copy2(root / "Scripts/com.lv1tools.LV1BridgeMac.plist", agents)
    scripts = staging / "scripts"
    scripts.mkdir()
    for name in ("preinstall", "postinstall"):
        shutil.copy2(root / "Scripts" / name, scripts / name)
        (scripts / name).chmod(0o755)
    components = [
        {"RootRelativeBundlePath": "Applications/Waves/eMotion LV1/Modules/LV1Bridge_Control.bundle",
         "BundleIsRelocatable": False, "BundleIsVersionChecked": True, "BundleOverwriteAction": "upgrade"},
        {"RootRelativeBundlePath": "Applications/LV1 Bridge.app",
         "BundleIsRelocatable": False, "BundleIsVersionChecked": True,
         "BundleOverwriteAction": "upgrade", "BundleHasStrictIdentifier": True},
    ]
    component_path = staging / "components.plist"
    component_path.write_bytes(plistlib.dumps(components))
    package = output / f"LV1Bridge-macOS-Installer-{version}.pkg"
    run("pkgbuild", "--root", payload, "--scripts", scripts,
        "--component-plist", component_path, "--identifier", "com.lv1tools.LV1BridgeMac",
        "--version", version, "--install-location", "/", package)
    artifacts.append(package)
    expanded = staging / "verified"
    run("pkgutil", "--expand-full", package, expanded)
    for destination in ["Applications/LV1 Bridge.app",
                        "Applications/Waves/eMotion LV1/Modules/LV1Bridge_Control.bundle"]:
        run("codesign", "--verify", "--strict", expanded / "Payload" / destination)

app_zip = output / f"LV1-Bridge-Mac-App-{version}.zip"
run("ditto", "-c", "-k", "--sequesterRsrc", "--keepParent", dist / "LV1 Bridge.app", app_zip)
artifacts.append(app_zip)
plugin_zip = output / f"LV1-Bridge-StreamDeck-Mac-{version}.zip"
with ZipFile(plugin_zip, "w", ZIP_DEFLATED) as archive:
    for path in sorted(plugin.rglob("*")):
        if path.is_file() and path.name != ".DS_Store":
            archive.write(path, path.relative_to(plugin.parent))
    archive.write(root / "README-HE.md", "README-HE.md")
artifacts.append(plugin_zip)
source_zip = output / f"LV1Bridge-macOS-Source-{version}.zip"
with ZipFile(source_zip, "w", ZIP_DEFLATED) as archive:
    for name in ["Sources", "Tests", "StreamDeck", "Scripts", "Module", "Package.swift", "README-HE.md"]:
        source = root / name
        for path in [source] if source.is_file() else sorted(source.rglob("*")):
            if path.is_file() and "__pycache__" not in path.parts and path.name != ".DS_Store":
                archive.write(path, Path("lv1bridge-mac") / path.relative_to(root))
artifacts.append(source_zip)
for source, name in [
    (root / "README-HE.md", f"README-HE-{version}.md"),
    (plugin / "Profiles/LV1 Bridge XL.streamDeckProfile", "LV1-Bridge-XL.streamDeckProfile"),
]:
    destination = output / name
    shutil.copy2(source, destination)
    artifacts.append(destination)
checksums = output / f"LV1Bridge-{version}-SHA256SUMS.txt"
checksums.write_text("".join(hashlib.sha256(path.read_bytes()).hexdigest() + "  " + path.name + "\n"
                             for path in artifacts))
print(f"Packaged and verified {version} in {output}")
