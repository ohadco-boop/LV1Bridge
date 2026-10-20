#!/bin/zsh
set -euo pipefail

ROOT="${0:A:h:h}"
DIST="${LV1BRIDGE_DIST:-$ROOT/.dist}"
ORIGINAL="/Applications/Waves/eMotion LV1/Modules/FaderBank_Control.bundle/Contents/MacOS/FaderBank_Control"
[[ -f "$ORIGINAL" ]] || { echo "eMotion LV1 FaderBank_Control was not found." >&2; exit 1; }

rm -rf "$DIST"
mkdir -p "$DIST/LV1 Bridge.app/Contents/MacOS" "$DIST/LV1 Bridge.app/Contents/Resources"

swift build --package-path "$ROOT" -c release --arch arm64 --scratch-path "$ROOT/.build-arm64"
swift build --package-path "$ROOT" -c release --arch x86_64 --scratch-path "$ROOT/.build-x86_64"
lipo -create "$ROOT/.build-arm64/arm64-apple-macosx/release/LV1BridgeMac" \
  "$ROOT/.build-x86_64/x86_64-apple-macosx/release/LV1BridgeMac" \
  -output "$DIST/LV1 Bridge.app/Contents/MacOS/LV1BridgeMac"
cp "$ROOT/Scripts/App-Info.plist" "$DIST/LV1 Bridge.app/Contents/Info.plist"

BUNDLE="$DIST/LV1Bridge_Control.bundle"
mkdir -p "$BUNDLE/Contents/MacOS"
cp "$ROOT/Module/Info.plist" "$BUNDLE/Contents/Info.plist"
ditto "$ROOT/Module/Resources" "$BUNDLE/Contents/Resources"
# Keep data in Resources so its signature survives pkg/ZIP extraction.
# Waves still reads the same Contents/manifest.yaml path through this link.
cp -X "$ROOT/Module/manifest.yaml" "$BUNDLE/Contents/Resources/manifest.yaml"
ln -s Resources/manifest.yaml "$BUNDLE/Contents/manifest.yaml"
cp "$ORIGINAL" "$BUNDLE/Contents/MacOS/LV1BridgeBackend"
ruby -e 'p=ARGV[0];s=File.binread(p);abort "Fader Bank identity not found" unless s.include?("Fader Bank");File.binwrite(p,s.gsub("Fader Bank","LV1 Bridge"))' "$BUNDLE/Contents/MacOS/LV1BridgeBackend"
xcrun clang++ -std=c++17 -Os -arch arm64 -arch x86_64 -mmacosx-version-min=12.0 -bundle -fvisibility=hidden \
  "$ROOT/Module/LV1BridgeModule.cpp" -o "$BUNDLE/Contents/MacOS/LV1Bridge_Control"

codesign --force --deep --sign - "$DIST/LV1 Bridge.app"
codesign --force --deep --sign - "$BUNDLE"
echo "Built universal release in $DIST"
