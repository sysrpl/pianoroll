#!/bin/bash
set -e

TARGET="$HOME/.local/share/pianoroll"
APPLICATIONS="$HOME/.local/share/applications"
ICON_DIR="$HOME/.local/share/icons/hicolor/512x512/apps"

# Build into a staging folder first, then sync it across. Publishing straight into the target
# would leave behind stale files from earlier builds, and syncing keeps samples/ and music/
# (which live inside the target) from being deleted along with them.
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

dotnet publish -c Release -r linux-x64 --self-contained -o "$STAGE"

mkdir -p "$TARGET"
rsync -a --delete --exclude 'samples/' --exclude 'music/' "$STAGE/" "$TARGET/"

# Recorded instruments and the MIDI files that come with the program: both are kept out of the
# build, because they are far larger than it is, and copied in here.
if [ -d samples ]; then
  rsync -a --delete samples/ "$TARGET/samples/"
fi

if [ -d music ]; then
  rsync -a --delete music/ "$TARGET/music/"
fi

# App icon for the Mint menu and other launchers.
mkdir -p "$ICON_DIR"
cp images/icon.png "$ICON_DIR/pianoroll.png"

mkdir -p "$APPLICATIONS"
cat > "$APPLICATIONS/pianoroll.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=Piano Roll
GenericName=MIDI Player
Comment=Watch MIDI files fall onto a piano keyboard and play
Keywords=midi;piano;music;keyboard;player;
Exec=$TARGET/pianoroll
Path=$TARGET
Icon=pianoroll
Terminal=false
Categories=AudioVideo;Audio;Music;Player;
StartupNotify=true
DESKTOP

chmod +x "$TARGET/pianoroll"
chmod +x "$APPLICATIONS/pianoroll.desktop"
update-desktop-database "$APPLICATIONS"
gtk-update-icon-cache -q -t "$HOME/.local/share/icons/hicolor" 2>/dev/null || true

echo "Installed to $TARGET ($(du -sh "$TARGET" | cut -f1)), menu entry added."
