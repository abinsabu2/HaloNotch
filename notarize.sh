#!/bin/bash
# Builds HaloNotch signed with Developer ID, notarizes and staples zip + dmg.
# Needs: "Developer ID Application" certificate in the keychain and a notarytool profile:
#   xcrun notarytool store-credentials halonotch-notary --apple-id <id> --team-id <team>
# Output: dist/HaloNotch-<version>.zip and dist/HaloNotch-<version>.dmg
set -euo pipefail
cd "$(dirname "$0")"

PROFILE="${NOTARY_PROFILE:-halonotch-notary}"
SIGN_ID="${SIGN_ID:-$(security find-identity -v -p codesigning | awk -F'"' '/Developer ID Application/ {print $2; exit}')}"
[ -n "$SIGN_ID" ] || { echo "❌ No 'Developer ID Application' certificate in keychain"; exit 1; }
echo "Signing as: $SIGN_ID"
export SIGN_ID

./package.sh

VERSION=$(/usr/libexec/PlistBuddy -c "Print :CFBundleShortVersionString" Resources/Info.plist)
OUT="dist/HaloNotch-$VERSION"
APP="$OUT/HaloNotch.app"
ZIP="dist/HaloNotch-$VERSION.zip"
DMG="dist/HaloNotch-$VERSION.dmg"

notarize() {
    local log
    log=$(xcrun notarytool submit "$1" --keychain-profile "$PROFILE" --wait 2>&1) || true
    echo "$log"
    grep -q "status: Accepted" <<<"$log" || {
        echo "❌ Notarization failed for $1 – details: xcrun notarytool log <id> --keychain-profile $PROFILE"
        exit 1
    }
}

# App: notarize the zip, staple the ticket into the app, re-zip with the ticket
notarize "$ZIP"
xcrun stapler staple "$APP"
rm -f "$ZIP"
ditto -c -k --keepParent "$OUT" "$ZIP"

# DMG with the stapled app
rm -rf dist/dmg && mkdir -p dist/dmg
ditto "$APP" dist/dmg/HaloNotch.app
cp Install-HaloNotch.txt dist/dmg/
ln -s /Applications dist/dmg/Applications
hdiutil create -volname HaloNotch -srcfolder dist/dmg -ov -format UDZO "$DMG"
codesign --sign "$SIGN_ID" --timestamp "$DMG"
notarize "$DMG"
xcrun stapler staple "$DMG"

spctl -a -vv -t exec "$APP"
spctl -a -vv -t open --context context:primary-signature "$DMG"
echo "✅ $ZIP and $DMG notarized"
