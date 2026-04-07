#!/usr/bin/env bash

set -euo pipefail

if [ "$#" -ne 6 ]; then
  echo "usage: $0 <app-name> <version> <package-root> <output-dir> <package-name> <rid>"
  exit 1
fi

app_name="$1"
version="$2"
package_root="$3"
output_dir="$4"
package_name="$5"
rid="$6"

package_root="$(cd "$package_root" && pwd -P)"
output_dir="$(mkdir -p "$output_dir" && cd "$output_dir" && pwd -P)"

case "$rid" in
  linux-x64)
    deb_arch="amd64"
    rpm_arch="x86_64"
    rpm_strip="$(command -v strip)"
    ;;
  linux-arm64)
    deb_arch="arm64"
    rpm_arch="aarch64"
    rpm_strip="$(command -v aarch64-linux-gnu-strip)"
    ;;
  *)
    echo "unsupported rid: $rid"
    exit 1
    ;;
esac

tmp_dir="$output_dir/package-build"
rm -rf "$tmp_dir"
mkdir -p "$tmp_dir"

deb_root="$tmp_dir/deb-root"
cp -R "$package_root" "$deb_root"
mkdir -p "$deb_root/DEBIAN"

cat > "$deb_root/DEBIAN/control" <<EOF
Package: $package_name
Version: $version
Section: utils
Priority: optional
Architecture: $deb_arch
Maintainer: DropAndForget
Description: Desktop app for simple Cloudflare R2 file drops and sync.
EOF

chmod 0755 "$deb_root/DEBIAN"
chmod 0644 "$deb_root/DEBIAN/control"

dpkg-deb --root-owner-group --build "$deb_root" "$output_dir/$app_name-$version-$rid.deb"

rpm_topdir="$tmp_dir/rpmbuild"
mkdir -p "$rpm_topdir/BUILD" "$rpm_topdir/RPMS" "$rpm_topdir/SOURCES" "$rpm_topdir/SPECS" "$rpm_topdir/SRPMS"
cp -R "$package_root" "$rpm_topdir/SOURCES/package-root"

cat > "$rpm_topdir/SPECS/$package_name.spec" <<EOF
Name: $package_name
Version: $version
Release: 1
Summary: Desktop app for simple Cloudflare R2 file drops and sync.
License: MIT

%description
Desktop app for simple Cloudflare R2 file drops and sync.

%prep

%build

%install
rm -rf %{buildroot}
cp -a %{_sourcedir}/package-root/. %{buildroot}/

%files
/usr/bin/$package_name
/usr/lib/$package_name
/usr/share/applications/$package_name.desktop
/usr/share/icons/hicolor/scalable/apps/$package_name.svg
/usr/share/pixmaps/$package_name.svg
EOF

rpmbuild --target "$rpm_arch" --define "_topdir $rpm_topdir" --define "__strip $rpm_strip" -bb "$rpm_topdir/SPECS/$package_name.spec"

rpm_path="$(printf '%s\n' "$rpm_topdir/RPMS/$rpm_arch"/*.rpm)"
mv "$rpm_path" "$output_dir/$app_name-$version-$rid.rpm"

rm -rf "$tmp_dir"
