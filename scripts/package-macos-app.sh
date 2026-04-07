#!/usr/bin/env bash

set -euo pipefail

if [ "$#" -ne 5 ]; then
  echo "usage: $0 <app-name> <version> <publish-dir> <output-dir> <bundle-id>"
  exit 1
fi

app_name="$1"
version="$2"
publish_dir="$3"
output_dir="$4"
bundle_id="$5"

app_dir="$output_dir/${app_name}.app"
contents_dir="$app_dir/Contents"
macos_dir="$contents_dir/MacOS"
frameworks_dir="$contents_dir/Frameworks"
resources_dir="$contents_dir/Resources"
sign_identity="${MACOS_SIGN_IDENTITY:--}"
source_icon_file="logo.icns"
bundle_icon_file="${app_name}.icns"

rm -rf "$app_dir"
mkdir -p "$macos_dir" "$frameworks_dir" "$resources_dir"

while IFS= read -r -d '' source_path; do
  file_name="$(basename "$source_path")"

  case "$file_name" in
    "$app_name")
      cp "$source_path" "$macos_dir/$file_name"
      chmod +x "$macos_dir/$file_name"
      ;;
    *.dylib|*.so)
      cp "$source_path" "$frameworks_dir/$file_name"
      ln -sf "../Frameworks/$file_name" "$macos_dir/$file_name"
      ;;
    *.dSYM)
      ;;
    *)
      cp -R "$source_path" "$resources_dir/$file_name"
      ;;
  esac
done < <(find "$publish_dir" -mindepth 1 -maxdepth 1 -print0)

if [ -f "$resources_dir/Assets/$source_icon_file" ]; then
  cp "$resources_dir/Assets/$source_icon_file" "$resources_dir/$bundle_icon_file"
fi

cat > "$contents_dir/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>en</string>
  <key>CFBundleExecutable</key>
  <string>${app_name}</string>
  <key>CFBundleIdentifier</key>
  <string>${bundle_id}</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundleName</key>
  <string>${app_name}</string>
  <key>CFBundleDisplayName</key>
  <string>${app_name}</string>
  <key>CFBundleIconFile</key>
  <string>${app_name}</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>${version}</string>
  <key>CFBundleVersion</key>
  <string>${version}</string>
  <key>LSMinimumSystemVersion</key>
  <string>12.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
EOF

sign_file() {
  local path="$1"

  codesign \
    --force \
    --sign "$sign_identity" \
    --timestamp=none \
    "$path"
}

while IFS= read -r -d '' dylib_path; do
  sign_file "$dylib_path"
done < <(find "$frameworks_dir" -type f \( -name '*.dylib' -o -name '*.so' \) -print0)

if [ -f "$macos_dir/$app_name" ]; then
  sign_file "$macos_dir/$app_name"
fi

sign_file "$app_dir"
codesign --verify --deep --strict --verbose=2 "$app_dir"

echo "$app_dir"
