#!/usr/bin/env bash
# Builds a universal (Apple silicon + Intel) Release of TransmissionRemote and packs it into a
# disk image: open it, drag the app onto the Applications folder next to it.
#
# The app is ad-hoc signed rather than signed with a developer certificate, so anyone can build
# the same thing; people approve the first launch in System Settings. Styling the disk image
# window goes through Finder, so macOS asks once to let your terminal control Finder.
#
# Usage: Scripts/build-release.sh
# Output: build/release/TransmissionRemote-<version>.dmg
set -euo pipefail

cd "$(dirname "$0")/.."

project=TransmissionRemote.xcodeproj
scheme=TransmissionRemote
app_name=TransmissionRemote.app
volume_name="Transmission Remote"
output_dir=build/release
derived_data="$output_dir/DerivedData"
staging="$output_dir/dmg-staging"
writable_image="$output_dir/writable.dmg"
attached_device=""

cleanup() {
  if [[ -n "$attached_device" ]]; then
    hdiutil detach "$attached_device" -force -quiet || true
  fi
  rm -rf "$staging" "$writable_image"
}
trap cleanup EXIT

read_version() {
  xcodebuild -project "$project" -scheme "$scheme" -configuration Release -showBuildSettings 2>/dev/null \
    | awk -F' = ' '/ MARKETING_VERSION /{ if (!found) { print $2; found = 1 } }'
}

build_app() {
  xcodebuild -project "$project" -scheme "$scheme" -configuration Release \
    -destination 'generic/platform=macOS' -derivedDataPath "$derived_data" \
    -skipPackagePluginValidation -quiet \
    CODE_SIGN_IDENTITY=- CODE_SIGN_STYLE=Manual DEVELOPMENT_TEAM= \
    build
}

# The folder that becomes the disk image: the app, a link to /Applications, and the window
# background in a hidden folder.
prepare_staging() {
  local app="$1"
  rm -rf "$staging"
  mkdir -p "$staging/.background"
  ditto "$app" "$staging/$app_name"
  ln -s /Applications "$staging/Applications"

  swift Scripts/dmg/make-background.swift "$staging/.background/background.png" 1
  swift Scripts/dmg/make-background.swift "$staging/.background/background@2x.png" 2
  tiffutil -cathidpicheck "$staging/.background/background.png" "$staging/.background/background@2x.png" \
    -out "$staging/.background/background.tiff" 2>/dev/null
  rm "$staging/.background/"*.png
}

detach_with_retries() {
  for _ in 1 2 3; do
    if hdiutil detach "$attached_device" -quiet; then
      attached_device=""
      return
    fi
    sleep 2
  done
  hdiutil detach "$attached_device" -force -quiet
  attached_device=""
}

make_disk_image() {
  local destination="$1"
  if [[ -e "/Volumes/$volume_name" ]]; then
    echo "Eject the disk named \"$volume_name\" first; Finder can't tell two of them apart." >&2
    exit 1
  fi

  # Room for the files plus Finder's .DS_Store; the final conversion drops the free space.
  local size_mb=$(( $(du -sm "$staging" | cut -f1) + 20 ))
  rm -f "$writable_image"
  hdiutil create -quiet -volname "$volume_name" -srcfolder "$staging" -fs HFS+ \
    -format UDRW -size "${size_mb}m" "$writable_image"

  # The first line of the output is the whole disk, which detaching ejects with its volume.
  attached_device=$(diskutil image attach "$writable_image" | awk 'NR == 1 { print $1 }')

  if ! osascript Scripts/dmg/style-window.applescript "$volume_name" "$app_name"; then
    echo "Couldn't lay out the disk image window. Allow your terminal to control Finder in" >&2
    echo "System Settings → Privacy & Security → Automation, then run this again." >&2
    exit 1
  fi
  rm -rf "/Volumes/$volume_name/.fseventsd"
  sync
  detach_with_retries

  rm -f "$destination"
  hdiutil convert -quiet "$writable_image" -format ULFO -o "$destination"
  hdiutil verify -quiet "$destination"
}

version=$(read_version)
if [[ -z "$version" ]]; then
  echo "Couldn't read MARKETING_VERSION from the project." >&2
  exit 1
fi

build_app
app="$derived_data/Build/Products/Release/$app_name"
codesign --verify --deep --strict "$app"

prepare_staging "$app"
disk_image="$output_dir/TransmissionRemote-$version.dmg"
make_disk_image "$disk_image"
echo "$disk_image"
