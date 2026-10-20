# LV1 Bridge

Bridge between Waves eMotion LV1, MIDI controllers and Stream Deck, with a separate 16-channel VU display.

| Platform | Status |
| --- | --- |
| macOS | [2.1.5 release and installer](https://github.com/ohadco-boop/LV1Bridge/releases/tag/macos-v2.1.5) — universal Apple Silicon / Intel |
| Windows | Reference source is included. The current working Windows installer is on another computer and has not been published here yet. |

## macOS installation

Requires macOS 13 or later, a licensed Waves eMotion LV1 installation, and Stream Deck software for Deck integration. This build was validated with LV1 17.3.50 and a Stream Deck XL layout.

1. Save your Show and quit LV1, LV1 Bridge and Stream Deck.
2. Download and run `LV1Bridge-macOS-Installer-2.1.5.pkg` from the release.
3. Open Bridge and configure its MIDI pairs. Add **LV1 Bridge** from LV1's Inventory.
4. Open Stream Deck 7.1 or later. Import `LV1-Bridge-XL.streamDeckProfile` once and select **LV1 Bridge XL** for the XL device.

For an existing XL profile, drag **LV1 Smart Slot** onto the empty sixth key in the bottom row to add FX; existing profiles are preserved.

The installer includes the Bridge app, its LV1 module, the Stream Deck plugin and XL profile. The app-only ZIP is for existing installations; it does not replace a full first installation. The app and module are ad-hoc signed, not notarized.

See [the Hebrew guide](macos/README-HE.md) for configuration and removal.

## Changes in 2.1.5

- Stream Deck now runs in its Node.js runtime, so polling continues while its editor is in the background and selection highlights update after command completion. Requires Stream Deck 7.1+.
- The new FX page button (sixth key in the bottom row) switches the first eight keys to FX 1–8, with live names and selection highlighting.
- Rapid command requests preserve key press order; unchanged key images are not retransmitted.
- MON labels follow the active LV1 snapshot, including unsaved renames.
- USER labels follow the session's User Keys names; empty names use numbered defaults.
- Labels update independently of the visible mixer bank.
- Includes the 2.1.3 Windows-matched MON selection sequence and the 2.1.2 VU name / mono-stereo updates.

## Source and builds

- `macos/`: current Swift app, LV1 module wrapper, Stream Deck plugin, tests and installer scripts.
- `windows/reference/`: Windows handoff sources used for comparison. These are not a confirmed current Windows release.
- `release-assets/macos-v2.1.5/`: validated downloads with SHA-256 checksums.

On a Mac with Xcode Command Line Tools and LV1's `FaderBank_Control.bundle` installed:

```sh
cd macos
swift test
node --test Tests/StreamDeck/plugin.test.cjs
zsh Scripts/build-release.sh
python3 Scripts/package-release.py --dist .dist --output outputs
```

The build uses the locally installed Waves Fader Bank backend. Rebuild and revalidate after updating LV1. This project is an independent integration, not a Waves or Elgato product.
