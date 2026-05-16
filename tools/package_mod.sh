#!/usr/bin/env bash
# Packages the BeamNG mod into an installable .zip
# Output: out/BeamQuestBridge.zip
# Install: drag into BeamNG mod manager, or copy to %AppData%/BeamNG.drive/mods/

set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

OUT="out"
ZIP="$OUT/BeamQuestBridge.zip"

mkdir -p "$OUT"
rm -f "$ZIP"

echo "Packaging BeamNG mod → $ZIP"

cd beamng-mod
zip -r "../$ZIP" . \
    --exclude "*.DS_Store" \
    --exclude "__pycache__/*"
cd ..

echo "Done: $ZIP"
echo ""
echo "Install options:"
echo "  1. BeamNG mod manager: drag $ZIP into the window"
echo "  2. Manual: copy $ZIP to %APPDATA%/BeamNG.drive/mods/"
echo "  3. Linux: copy to ~/.local/share/BeamNG.drive/mods/"
echo ""
echo "After installing, enable in-game:"
echo "  Main Menu → Mods → BeamQuest Bridge → Enable"
echo ""
echo "Configuration (set before launching BeamNG):"
echo "  set BQ_QUEST_IP=192.168.x.x  (your Quest 3 IP)"
echo "  set BQ_STATE_PORT=37420"
echo "  set BQ_POS_PORT=37421"
