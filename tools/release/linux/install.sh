#!/usr/bin/env bash
# Fishing Sim for Linux: joins the release parts (FishingSim-<version>-Linux.tar.gz.part-*), unpacks the game into
# ~/.local/share/fishingsim (no root needed) and adds a menu entry and the `fishingsim` command.
#   chmod +x install.sh && ./install.sh          (run in the folder with the downloaded parts)
#   ./install.sh --uninstall
set -euo pipefail
DEST="${XDG_DATA_HOME:-$HOME/.local/share}/fishingsim"
BIN="$HOME/.local/bin"
APPS="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
if [ "${1:-}" = "--uninstall" ]; then
  rm -rf "$DEST" "$BIN/fishingsim" "$APPS/fishingsim.desktop"
  echo "Fishing Sim odinstalowany (zapisy gry zostają w ~/.config/unity3d)."
  exit 0
fi
cd "$(dirname "$0")"
parts=(FishingSim-*-Linux.tar.gz.part-*)
[ -e "${parts[0]}" ] || { echo "Nie znaleziono plików FishingSim-*-Linux.tar.gz.part-* w tym folderze."; exit 1; }
echo "Sprawdzam i rozpakowuję ${#parts[@]} części do $DEST ..."
if [ -f SHA256SUMS ]; then sha256sum -c SHA256SUMS --ignore-missing; fi
rm -rf "$DEST"
mkdir -p "$DEST" "$BIN" "$APPS"
cat "${parts[@]}" | tar -xz -C "$DEST" --strip-components=1
chmod +x "$DEST/FishingSim.x86_64"
cat > "$BIN/fishingsim" <<SH
#!/bin/sh
exec "$DEST/FishingSim.x86_64" "\$@"
SH
chmod +x "$BIN/fishingsim"
cat > "$APPS/fishingsim.desktop" <<DESK
[Desktop Entry]
Type=Application
Name=Fishing Sim
Comment=Symulator wędkarstwa
Exec=$DEST/FishingSim.x86_64
Icon=$DEST/icon.png
Terminal=false
Categories=Game;Simulation;
DESK
echo "Gotowe. Uruchom z menu (Fishing Sim) albo poleceniem: fishingsim"
