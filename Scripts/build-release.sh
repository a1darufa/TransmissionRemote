#!/usr/bin/env bash
# Builds a universal (Apple silicon + Intel) Release of TransmissionRemote and zips it for a
# GitHub release. The app is ad-hoc signed rather than signed with a developer certificate,
# so anyone can build the same thing; people approve the first launch in System Settings.
#
# Usage: Scripts/build-release.sh
# Output: build/release/TransmissionRemote-<version>.zip
set -euo pipefail

cd "$(dirname "$0")/.."

project=TransmissionRemote.xcodeproj
scheme=TransmissionRemote
output_dir=build/release
derived_data="$output_dir/DerivedData"

version=$(xcodebuild -project "$project" -scheme "$scheme" -configuration Release -showBuildSettings 2>/dev/null \
  | awk -F' = ' '/ MARKETING_VERSION /{ if (!found) { print $2; found = 1 } }')
if [[ -z "$version" ]]; then
  echo "Couldn't read MARKETING_VERSION from the project." >&2
  exit 1
fi

xcodebuild -project "$project" -scheme "$scheme" -configuration Release \
  -destination 'generic/platform=macOS' -derivedDataPath "$derived_data" \
  -skipPackagePluginValidation -quiet \
  CODE_SIGN_IDENTITY=- CODE_SIGN_STYLE=Manual DEVELOPMENT_TEAM= \
  build

app="$derived_data/Build/Products/Release/TransmissionRemote.app"
codesign --verify --deep --strict "$app"

archive="$output_dir/TransmissionRemote-$version.zip"
rm -f "$archive"
ditto -c -k --sequesterRsrc --keepParent "$app" "$archive"
echo "$archive"
