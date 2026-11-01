#!/bin/bash
# Builds a shareable HaloNotch zip (no Apple Developer ID needed – ad-hoc signed).
# Output: dist/HaloNotch-<version>.zip  (contains HaloNotch.app + Install-HaloNotch.txt)
set -euo pipefail
cd "$(dirname "$0")"

if [ -z "${DEVELOPER_DIR:-}" ] && [ -d /Applications/Xcode.app/Contents/Developer ]; then
    export DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer
fi

VERSION=$(/usr/libexec/PlistBuddy -c "Print :CFBundleShortVersionString" Resources/Info.plist)
OUT="dist/HaloNotch-$VERSION"
APP="$OUT/HaloNotch.app"
rm -rf "$OUT" "dist/HaloNotch-$VERSION.zip"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

# Universal binary (Apple Silicon + Intel); falls back to this Mac's architecture if Intel fails.
BINARIES=()
for ARCH in arm64 x86_64; do
    # Separate scratch path per arch: newer SwiftPM shares one bin path across --arch builds.
    if swift build -c release --arch "$ARCH" --scratch-path ".build/$ARCH"; then
        BINARIES+=("$(swift build -c release --arch "$ARCH" --scratch-path ".build/$ARCH" --show-bin-path)/HaloNotch")
    else
        echo "⚠️  Build for $ARCH failed – skipping this architecture."
    fi
done
[ ${#BINARIES[@]} -gt 0 ] || { echo "❌ No architecture built"; exit 1; }
lipo -create "${BINARIES[@]}" -output "$APP/Contents/MacOS/HaloNotch"

cp Resources/Info.plist "$APP/Contents/Info.plist"
cp -R Resources/*.lproj "$APP/Contents/Resources/"
cp Resources/AppIcon.icns "$APP/Contents/Resources/"
xattr -cr "$APP"
if [ -n "${SIGN_ID:-}" ]; then
    # Developer ID signature with hardened runtime (used by notarize.sh)
    codesign --force --options runtime --timestamp --entitlements HaloNotch.entitlements --sign "$SIGN_ID" "$APP"
else
    codesign --force --deep --sign - "$APP"
fi

cp Install-HaloNotch.txt "$OUT/"
ditto -c -k --keepParent "$OUT" "dist/HaloNotch-$VERSION.zip"

echo "✅ dist/HaloNotch-$VERSION.zip  ($(lipo -archs "$APP/Contents/MacOS/HaloNotch"))"
