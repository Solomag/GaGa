#!/bin/sh

# Build GaGa.app and package it for handing to someone.
#
# Usage: ./package.sh
# Writes build/GaGa-<version>.dmg and build/GaGa-<version>.zip
#
# Note that the app is signed ad-hoc, not with a Developer ID, and is not
# notarized. That is fine for your own machines: a locally built app has no
# quarantine flag and opens normally. Anything downloaded from the internet
# does get flagged, and Gatekeeper will refuse an ad-hoc signature, so a
# recipient has to right click the app and pick Open the first time, or run
#   xattr -dr com.apple.quarantine /Applications/GaGa.app
# Shipping without that step needs a paid Developer ID and notarization.

set -e
cd "$(dirname "$0")"

NAME="GaGa"
APP="build/$NAME.app"
STAGE="build/stage"

./build.sh

VERSION=$(/usr/libexec/PlistBuddy -c "Print CFBundleShortVersionString" "$APP/Contents/Info.plist")
DMG="build/$NAME-$VERSION.dmg"
ZIP="build/$NAME-$VERSION.zip"

# a disk image holding the app next to a shortcut to /Applications, which is
# the drag-to-install layout people expect
rm -rf "$STAGE" "$DMG"
mkdir -p "$STAGE"
cp -R "$APP" "$STAGE/$NAME.app"
ln -s /Applications "$STAGE/Applications"

hdiutil create -volname "$NAME" -srcfolder "$STAGE" -ov -format UDZO -quiet "$DMG"
rm -rf "$STAGE"

# and a plain archive, for when a disk image is more ceremony than it is worth
rm -f "$ZIP"
ditto -c -k --keepParent "$APP" "$ZIP"

echo "Built $DMG ($(du -h "$DMG" | cut -f1))"
echo "Built $ZIP ($(du -h "$ZIP" | cut -f1))"
