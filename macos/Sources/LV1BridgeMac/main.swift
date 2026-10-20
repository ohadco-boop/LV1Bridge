import AppKit
import Foundation
import CoreMIDI

final class AppDelegate: NSObject, NSApplicationDelegate {
    private var transport: CoreMIDITransport?
    private var engine: BridgeEngine?
    private var api: BridgeAPI?
    private var window: NSWindow!
    private var meterWindow: MeterWindowController?
    private let status = NSTextField(labelWithString: "Starting…")
    private let profile = NSPopUpButton()
    private let source1 = NSPopUpButton(), destination1 = NSPopUpButton()
    private let source2 = NSPopUpButton(), destination2 = NSPopUpButton()
    private var sources: [MIDIDevice] = [], destinations: [MIDIDevice] = []

    func applicationDidFinishLaunching(_ notification: Notification) {
        buildWindow()
        do {
            let midi = try CoreMIDITransport()
            let bridge = BridgeEngine(transport: midi)
            let server = BridgeAPI(engine: bridge)
            try server.start()
            transport = midi; engine = bridge; api = server
            let meters = MeterWindowController(); meterWindow = meters; meters.showWindow(nil)
            bridge.onStateChanged = { [weak self] in DispatchQueue.main.async { self?.renderState() } }
            renderState() // Session metadata may already be loaded before the callback is installed.
            refreshPorts(nil)
            bridge.startIdentityHeartbeat()
            status.stringValue = "FaderBank In/Out are active — choose controllers and click Connect."
        } catch { status.stringValue = "Startup error: \(error.localizedDescription)" }
    }

    private func buildWindow() {
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 850, height: 280),
                          styleMask: [.titled, .closable, .miniaturizable, .resizable],
                          backing: .buffered, defer: false)
        window.title = "LV1 Bridge — Settings"
        let root = NSStackView(); root.orientation = .vertical; root.alignment = .leading; root.spacing = 12
        root.edgeInsets = NSEdgeInsets(top: 20, left: 20, bottom: 20, right: 20)
        let title = NSTextField(labelWithString: "Waves eMotion LV1 · Surface Control Bridge")
        title.font = .boldSystemFont(ofSize: 20); root.addArrangedSubview(title)
        status.textColor = .systemOrange; root.addArrangedSubview(status)

        profile.addItems(withTitles: ControllerProfile.allCases.map(\.rawValue))
        root.addArrangedSubview(row("Controller profile", profile))
        root.addArrangedSubview(row("Bank 1 input", source1, "Bank 1 output", destination1))
        root.addArrangedSubview(row("Bank 2 input", source2, "Bank 2 output", destination2))
        let buttons = NSStackView(); buttons.orientation = .horizontal; buttons.spacing = 8
        buttons.addArrangedSubview(button("Refresh MIDI", #selector(refreshPorts(_:))))
        buttons.addArrangedSubview(button("Connect", #selector(connectControllers(_:))))
        buttons.addArrangedSubview(button("Send identity", #selector(sendIdentity(_:))))
        buttons.addArrangedSubview(button("Show VU Meters", #selector(showMeters(_:))))
        root.addArrangedSubview(buttons)
        let note = NSTextField(wrappingLabelWithString: "LV1 must use the LV1 Bridge / Fader Bank component, not Mackie Control. Stream Deck API: 127.0.0.1:49631.")
        note.textColor = .secondaryLabelColor; root.addArrangedSubview(note)
        window.contentView = root; window.center(); window.makeKeyAndOrderFront(nil)
    }

    private func row(_ leftTitle: String, _ left: NSView, _ rightTitle: String? = nil, _ right: NSView? = nil) -> NSView {
        let stack = NSStackView(); stack.orientation = .horizontal; stack.spacing = 8
        let label = NSTextField(labelWithString: leftTitle); label.widthAnchor.constraint(equalToConstant: 120).isActive = true
        stack.addArrangedSubview(label); stack.addArrangedSubview(left); left.widthAnchor.constraint(equalToConstant: right == nil ? 400 : 245).isActive = true
        if let rightTitle, let right {
            let label2 = NSTextField(labelWithString: rightTitle); label2.widthAnchor.constraint(equalToConstant: 110).isActive = true
            stack.addArrangedSubview(label2); stack.addArrangedSubview(right); right.widthAnchor.constraint(equalToConstant: 245).isActive = true
        }
        return stack
    }

    private func button(_ title: String, _ action: Selector) -> NSButton {
        let value = NSButton(title: title, target: self, action: action); value.bezelStyle = .rounded; return value
    }

    @objc private func refreshPorts(_ sender: Any?) {
        sources = CoreMIDITransport.sources(); destinations = CoreMIDITransport.destinations()
        populate(source1, devices: sources, preferred: ["X-Touch Compact", "P1-M"])
        populate(source2, devices: sources, preferred: ["X-Touch Extender", "P1-X"])
        populate(destination1, devices: destinations, preferred: ["X-Touch Compact", "P1-M"])
        populate(destination2, devices: destinations, preferred: ["X-Touch Extender", "P1-X"])
    }

    private func populate(_ popup: NSPopUpButton, devices: [MIDIDevice], preferred: [String]) {
        popup.removeAllItems(); popup.addItem(withTitle: "None"); devices.forEach { popup.addItem(withTitle: $0.name) }
        if let index = devices.firstIndex(where: { device in preferred.contains { device.name.localizedCaseInsensitiveContains($0) } }) {
            popup.selectItem(at: index + 1)
        }
    }

    @objc private func connectControllers(_ sender: Any?) {
        guard let transport, let engine else { return }
        let selectedSources = [source1, source2].compactMap { popup -> MIDIDevice? in
            popup.indexOfSelectedItem > 0 ? sources[popup.indexOfSelectedItem - 1] : nil
        }
        let selectedOutputs = [destination1, destination2].compactMap { popup -> MIDIEndpointRef? in
            popup.indexOfSelectedItem > 0 ? destinations[popup.indexOfSelectedItem - 1].endpoint : nil
        }
        do {
            try transport.connectControllerSources(selectedSources)
            engine.controllerOutputs = selectedOutputs
            engine.profile = ControllerProfile.allCases[profile.indexOfSelectedItem]
            status.stringValue = "Connected · waiting for LV1 names and VU meters"
            status.textColor = .systemGreen
        } catch { status.stringValue = "MIDI connection failed: \(error.localizedDescription)"; status.textColor = .systemRed }
    }

    @objc private func sendIdentity(_ sender: Any?) { engine?.advertiseIdentity() }
    @objc private func showMeters(_ sender: Any?) { meterWindow?.showWindow(nil); meterWindow?.window?.orderFrontRegardless() }

    private func renderState() {
        guard let engine else { return }
        meterWindow?.update(names: engine.names, frame: engine.meters, mono: engine.mono)
        if engine.receivedFromLV1 {
            status.stringValue = "Receiving Surface Control data from LV1"
            status.textColor = .systemGreen
        }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        if !flag { window.makeKeyAndOrderFront(nil); meterWindow?.showWindow(nil) }
        return true
    }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.regular)
app.activate(ignoringOtherApps: true)
app.run()
