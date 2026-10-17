#!/usr/bin/env bash
# Builds a Debian package: a Native AOT build of Markdownia plus a desktop entry, icons, and a registration for
# Markdown files (text/markdown), so Markdownia appears in the launcher and under "Open With" for .md files.
#
# Usage: packaging/linux/build-deb.sh [linux-x64|linux-arm64]
# Output: artifacts/deb/markdownia_<version>_<arch>.deb
# The maintainer field comes from git config (user.name / user.email); set DEB_MAINTAINER to override it.
set -euo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(cd "$here/../.." && pwd)
rid=${1:-linux-x64}
case "$rid" in
  linux-x64) arch=amd64 ;;
  linux-arm64) arch=arm64 ;;
  *) echo "Unsupported runtime identifier: $rid (use linux-x64 or linux-arm64)" >&2; exit 1 ;;
esac

version=$(sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p' "$root/src/Markdownia/Markdownia.csproj" | head -n1)
if [[ -z "${DEB_MAINTAINER:-}" ]]; then
  name=$(git -C "$root" config user.name 2>/dev/null || true)
  email=$(git -C "$root" config user.email 2>/dev/null || true)
  if [[ -z "$name" || -z "$email" ]]; then
    echo "Set DEB_MAINTAINER (\"Name <email>\") or git config user.name/user.email." >&2
    exit 1
  fi
  DEB_MAINTAINER="$name <$email>"
fi

out="$root/artifacts/deb"
publish="$out/publish-$rid"
stage="$out/markdownia_${version}_${arch}"
rm -rf "$publish" "$stage"

echo "Publishing Markdownia $version for $rid..."
dotnet publish "$root/src/Markdownia" -c Release -r "$rid" -o "$publish" -nologo

# Program files. /usr/bin/markdownia is a symlink; the app finds its native libraries next to the real executable.
install -Dm755 "$publish/Markdownia" "$stage/usr/lib/markdownia/Markdownia"
install -m644 "$publish"/*.so "$stage/usr/lib/markdownia/"
mkdir -p "$stage/usr/bin"
ln -s ../lib/markdownia/Markdownia "$stage/usr/bin/markdownia"

# Desktop entry (with MimeType=text/markdown) and icons. Debian's desktop-file-utils and icon cache triggers
# refresh the MIME handler and icon caches after installation, so no maintainer scripts are needed.
install -Dm644 "$here/markdownia.desktop" "$stage/usr/share/applications/markdownia.desktop"
(cd "$here/icons" && find hicolor -type f) | while read -r icon; do
  install -Dm644 "$here/icons/$icon" "$stage/usr/share/icons/$icon"
done

# Documentation and licenses, including the licenses of the fonts embedded in the executable.
doc="$stage/usr/share/doc/markdownia"
install -Dm644 "$root/README.md" "$doc/README.md"
install -Dm644 "$root/THIRD-PARTY-NOTICES.md" "$doc/THIRD-PARTY-NOTICES.md"
for license in "$root"/src/Markdownia/Assets/Fonts/*/{OFL.txt,LICENSE*}; do
  [[ -f "$license" ]] || continue
  install -Dm644 "$license" "$doc/fonts/$(basename "$(dirname "$license")")/$(basename "$license")"
done
{
  echo "Markdownia"
  echo "Copyright $(date +%Y) ${DEB_MAINTAINER%% <*}"
  if [[ -f "$root/LICENSE" ]]; then echo; cat "$root/LICENSE"; fi
  echo
  echo "Markdownia includes third-party fonts, icons and libraries; see"
  echo "/usr/share/doc/markdownia/THIRD-PARTY-NOTICES.md and the font licenses in"
  echo "/usr/share/doc/markdownia/fonts/."
} > "$doc/copyright"
chmod 644 "$doc/copyright"

# The executable needs glibc 2.38+ (Ubuntu 24.04, Debian 13 or newer). X11, input and cursor libraries are loaded
# at runtime by Avalonia, fontconfig/freetype by Skia, and OpenSSL for images loaded over HTTPS.
installed_size=$(du -sk --exclude=DEBIAN "$stage" | cut -f1)
mkdir -p "$stage/DEBIAN"
cat > "$stage/DEBIAN/control" <<CONTROL
Package: markdownia
Version: $version
Architecture: $arch
Maintainer: $DEB_MAINTAINER
Installed-Size: $installed_size
Depends: libc6 (>= 2.38), libfontconfig1, libfreetype6, libx11-6, libice6, libsm6, libxi6, libxrandr2, libxcursor1, libxext6, libxfixes3, libssl3t64 | libssl3
Recommends: libgl1, libdbus-1-3, libgtk-3-0t64 | libgtk-3-0
Section: editors
Priority: optional
Description: Markdown editor and viewer with a typeset preview
 Markdownia is a two-pane Markdown editor and reader built with Avalonia and
 compiled ahead of time. The preview is rendered natively (no web view), with
 bundled reading fonts, nine light and dark themes, syntax-highlighted code,
 typeset math, tables, footnotes and other Pandoc and GitHub extensions.
 .
 The editor pane can be collapsed to use Markdownia as a reader, and Markdown
 files can be opened from the file manager.
CONTROL

find "$stage" -type d -exec chmod 755 {} +
deb="$out/markdownia_${version}_${arch}.deb"
dpkg-deb --root-owner-group -Zxz --build "$stage" "$deb"
echo "Built $deb"
