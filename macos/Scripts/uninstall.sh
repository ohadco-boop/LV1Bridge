#!/bin/zsh
set -euo pipefail
APP='/Applications/LV1 Bridge.app'
MODULE='/Applications/Waves/eMotion LV1/Modules/LV1Bridge_Control.bundle'
PLUGIN="$HOME/Library/Application Support/com.elgato.StreamDeck/Plugins/com.lv1.faderbank.sdPlugin"
LAUNCH_AGENT='/Library/LaunchAgents/com.lv1tools.LV1BridgeMac.plist'
echo "Removing LV1 Bridge app, LV1 module and Stream Deck plugin…"
sudo rm -rf "$APP" "$MODULE" "$LAUNCH_AGENT"
rm -rf "$PLUGIN"
echo "Removed. Waves' original FaderBank_Control.bundle was not changed. Backups, if any, remain in /Library/Application Support/LV1Bridge/Backups."
