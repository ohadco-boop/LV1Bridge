## macOS 2.1.4

MON and USER names now update on Stream Deck from the active LV1 session. MON renames no longer depend on the visible VU / mixer bank, and unsaved names are included. Empty USER names fall back to USER 1–16.

Includes the 2.1.3 correction for MON selection: the command sequence and waiting intervals match the supplied Windows implementation.

### Downloads

- **LV1Bridge-macOS-Installer-2.1.4.pkg** — full installation or upgrade.
- **LV1-Bridge-XL.streamDeckProfile** — 32-key Stream Deck XL profile; import once.
- App and Stream Deck ZIPs — separate components for an existing installation.
- Source ZIP, Hebrew instructions and SHA-256 checksums are also included.

Save your Show and quit LV1, Bridge and Stream Deck before installing. Reopen them after installation. The installer preserves existing Stream Deck profiles and backs up a previous LV1 Bridge module.

Requires macOS 13+, a licensed LV1 installation, and Stream Deck software for Deck controls. Universal Apple Silicon / Intel build, validated with LV1 17.3.50. App and module use ad-hoc signatures; the package is not notarized.

### Validation

- 9 Swift tests passed.
- Stream Deck plugin tests passed, including live label rendering and one command per press.
- Universal build and packaged app/module signatures verified.

This release is for macOS. The current Windows installer is not included.
