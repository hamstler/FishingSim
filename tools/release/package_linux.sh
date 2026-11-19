#!/usr/bin/env bash
# Linux packages from the exported game (build/linux): a .tar.gz and a .deb (installs to /opt/fishingsim with a
# launcher in /usr/bin and a menu entry).   tools/release/package_linux.sh VERSION [SRC] [OUT]
set -euo pipefail
VER=$1; SRC=${2:-build/linux}; OUT=${3:-build/dist}
HERE=$(cd "$(dirname "$0")" && pwd)
mkdir -p "$OUT"
OUT=$(cd "$OUT" && pwd)
NAME=FishingSim-$VER-linux-x86_64

stage=$(mktemp -d)
cp -a "$SRC" "$stage/$NAME"
cp "$HERE/icon.png" "$stage/$NAME/icon.png"
chmod +x "$stage/$NAME/FishingSim.x86_64"
tar -C "$stage" -czf "$OUT/$NAME.tar.gz" "$NAME"

deb=$stage/deb
mkdir -p "$deb/DEBIAN" "$deb/opt/fishingsim" "$deb/usr/bin" "$deb/usr/share/applications" \
  "$deb/usr/share/icons/hicolor/256x256/apps"
cp -a "$SRC/." "$deb/opt/fishingsim/"
chmod +x "$deb/opt/fishingsim/FishingSim.x86_64"
cp "$HERE/icon.png" "$deb/usr/share/icons/hicolor/256x256/apps/fishingsim.png"
cat > "$deb/usr/bin/fishingsim" <<'SH'
#!/bin/sh
exec /opt/fishingsim/FishingSim.x86_64 "$@"
SH
chmod 755 "$deb/usr/bin/fishingsim"
cat > "$deb/usr/share/applications/fishingsim.desktop" <<DESK
[Desktop Entry]
Type=Application
Name=Fishing Sim
Comment=Symulator wędkarstwa
Exec=fishingsim
Icon=fishingsim
Terminal=false
Categories=Game;Simulation;
DESK
size=$(du -sk "$deb" | cut -f1)
cat > "$deb/DEBIAN/control" <<CTRL
Package: fishingsim
Version: $VER
Section: games
Priority: optional
Architecture: amd64
Installed-Size: $size
Depends: libc6, libgl1, libvulkan1
Maintainer: Krystian Naskręski <noreply@users.noreply.github.com>
Description: Fishing simulator (Godot 4.3)
 Nordic-styled fishing simulator with lakes, villages and 66 fish species.
CTRL
mkdir -p "$deb/usr/share/doc/fishingsim"
cp "$HERE/../../LICENSE" "$deb/usr/share/doc/fishingsim/copyright"
dpkg-deb --root-owner-group -Zxz --build "$deb" "$OUT/fishingsim_${VER}_amd64.deb"
rm -rf "$stage"
ls -la "$OUT"
