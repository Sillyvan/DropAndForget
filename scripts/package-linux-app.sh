#!/usr/bin/env bash

set -euo pipefail

if [ "$#" -ne 5 ]; then
  echo "usage: $0 <app-name> <version> <publish-dir> <output-dir> <package-name>"
  exit 1
fi

app_name="$1"
version="$2"
publish_dir="$3"
output_dir="$4"
package_name="$5"

publish_dir_abs="$(cd "$publish_dir" && pwd -P)"
output_dir_abs="$(cd "$output_dir" && pwd -P)"

package_root="$output_dir/package-root"
lib_dir="$package_root/usr/lib/$package_name"
bin_dir="$package_root/usr/bin"
applications_dir="$package_root/usr/share/applications"
icons_dir="$package_root/usr/share/icons/hicolor/scalable/apps"
pixmaps_dir="$package_root/usr/share/pixmaps"
launcher_path="$bin_dir/$package_name"
desktop_path="$applications_dir/$package_name.desktop"
source_icon_path="$publish_dir/Assets/logo.svg"
icon_name="$package_name"

rm -rf "$package_root"
mkdir -p "$lib_dir" "$bin_dir" "$applications_dir" "$icons_dir" "$pixmaps_dir"

# When package_root lives under publish_dir, skip it to avoid copying a dir into itself.
if [ "$publish_dir_abs" = "$output_dir_abs" ]; then
  tar -C "$publish_dir" --exclude="./package-root" -cf - . | tar -C "$lib_dir" -xf -
else
  cp -R "$publish_dir"/. "$lib_dir"
fi

chmod +x "$lib_dir/$app_name"

cat > "$launcher_path" <<EOF
#!/usr/bin/env bash
set -euo pipefail
exec "/usr/lib/$package_name/$app_name" "\$@"
EOF
chmod +x "$launcher_path"

cat > "$desktop_path" <<EOF
[Desktop Entry]
Version=${version}
Type=Application
Name=${app_name}
Exec=${package_name}
Icon=${icon_name}
Terminal=false
Categories=Utility;
StartupNotify=true
EOF

if [ -f "$source_icon_path" ]; then
  cp "$source_icon_path" "$icons_dir/$icon_name.svg"
  cp "$source_icon_path" "$pixmaps_dir/$icon_name.svg"
fi

echo "$package_root"
