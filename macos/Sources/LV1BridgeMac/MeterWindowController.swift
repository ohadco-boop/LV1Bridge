import AppKit

final class MeterView: NSView {
    var names = (1...16).map { "Channel \($0)" }
    var levels = MeterFrame()
    var mono = Array(repeating: true, count: 16)
    var gap: CGFloat = 6
    var stripWidth: CGFloat = 72
    var autoFit = true
    var alignRight = true

    override var isFlipped: Bool { true }

    override func draw(_ dirtyRect: NSRect) {
        NSColor(calibratedRed: 0.03, green: 0.06, blue: 0.09, alpha: 1).setFill()
        bounds.fill()
        let inset: CGFloat = 8
        let width = autoFit ? max(32, (bounds.width - inset * 2 - gap * 15) / 16) : stripWidth
        let total = width * 16 + gap * 15
        let start = autoFit ? inset : (alignRight ? max(inset, bounds.width - total - inset) : inset)
        let labelHeight: CGFloat = 30, footerHeight: CGFloat = 18
        let meterTop = labelHeight + 3, meterBottom = bounds.height - footerHeight - 4
        let meterHeight = max(20, meterBottom - meterTop)
        let labelFont = NSFont.boldSystemFont(ofSize: max(8, min(13, width / 6)))
        let footerFont = NSFont.systemFont(ofSize: 8)

        for strip in 0..<16 {
            let x = start + CGFloat(strip) * (width + gap)
            let rect = NSRect(x: x, y: 0, width: width, height: bounds.height)
            NSColor(calibratedWhite: strip.isMultiple(of: 2) ? 0.09 : 0.075, alpha: 1).setFill(); rect.fill()
            let paragraph = NSMutableParagraphStyle(); paragraph.alignment = .center; paragraph.lineBreakMode = .byTruncatingTail
            let name = NSAttributedString(string: names[strip], attributes: [.font: labelFont, .foregroundColor: NSColor.white, .paragraphStyle: paragraph])
            name.draw(in: NSRect(x: x + 2, y: 5, width: width - 4, height: 20))

            if names[strip] == "—" { continue }

            let lanes = mono[strip] ? 1 : 2
            for lane in 0..<lanes {
                let value = lane == 0 ? levels.left[strip] : levels.right[strip]
                let laneWidth = mono[strip] ? width * 0.62 : width * 0.28
                let laneX = x + (mono[strip] ? (width - laneWidth) / 2 : width * 0.18 + CGFloat(lane) * width * 0.34)
                for segment in 0..<24 {
                    let lit = segment < (Int(value) * 24 + 126) / 127
                    let color: NSColor = !lit ? NSColor(calibratedRed: 0.08, green: 0.16, blue: 0.18, alpha: 1)
                        : segment >= 22 ? .systemRed : segment >= 18 ? .systemYellow : .systemGreen
                    color.setFill()
                    let h = max(2, meterHeight / 24 - 1)
                    NSRect(x: laneX, y: meterBottom - CGFloat(segment + 1) * meterHeight / 24, width: laneWidth, height: h).fill()
                }
            }
            let footer = mono[strip] ? "MONO" : "L        R"
            NSAttributedString(string: footer, attributes: [.font: footerFont, .foregroundColor: NSColor.lightGray, .paragraphStyle: paragraph])
                .draw(in: NSRect(x: x, y: meterBottom + 2, width: width, height: footerHeight))
        }
    }
}

final class MeterWindowController: NSWindowController {
    private let meterView = MeterView()
    private let gapSlider = NSSlider(value: 6, minValue: 0, maxValue: 40, target: nil, action: nil)
    private let widthSlider = NSSlider(value: 72, minValue: 36, maxValue: 140, target: nil, action: nil)
    private let fit = NSButton(checkboxWithTitle: "Fit 16", target: nil, action: nil)
    private let right = NSButton(checkboxWithTitle: "Align right", target: nil, action: nil)
    private let defaults = UserDefaults.standard

    init() {
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 1500, height: 230),
                              styleMask: [.titled, .closable, .miniaturizable, .resizable],
                              backing: .buffered, defer: false)
        super.init(window: window)
        window.title = "LV1 — 16 Channel VU METERS"
        window.level = .floating
        window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        window.minSize = NSSize(width: 640, height: 120)
        window.setFrameAutosaveName("LV1BridgeMeterWindow")

        let root = NSStackView(); root.orientation = .vertical; root.spacing = 0
        let controls = NSStackView(); controls.orientation = .horizontal; controls.spacing = 8
        controls.edgeInsets = NSEdgeInsets(top: 4, left: 8, bottom: 4, right: 8)
        controls.addArrangedSubview(NSTextField(labelWithString: "Gap")); controls.addArrangedSubview(gapSlider)
        controls.addArrangedSubview(NSTextField(labelWithString: "Width")); controls.addArrangedSubview(widthSlider)
        controls.addArrangedSubview(fit); controls.addArrangedSubview(right)
        gapSlider.widthAnchor.constraint(equalToConstant: 120).isActive = true
        widthSlider.widthAnchor.constraint(equalToConstant: 120).isActive = true
        root.addArrangedSubview(controls); root.addArrangedSubview(meterView)
        meterView.widthAnchor.constraint(equalTo: root.widthAnchor).isActive = true
        window.contentView = root

        gapSlider.target = self; gapSlider.action = #selector(layoutChanged)
        widthSlider.target = self; widthSlider.action = #selector(layoutChanged)
        fit.target = self; fit.action = #selector(layoutChanged)
        right.target = self; right.action = #selector(layoutChanged)
        loadSettings()
        if !defaults.bool(forKey: "LV1MeterWindowPositioned") {
            let screen = NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1600, height: 900)
            window.setFrame(NSRect(x: screen.minX + 10, y: screen.maxY - 250, width: screen.width - 20, height: 230), display: false)
            defaults.set(true, forKey: "LV1MeterWindowPositioned")
        }
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    func update(names: [String], frame: MeterFrame, mono: [Bool]) {
        meterView.names = names; meterView.levels = frame; meterView.mono = mono; meterView.needsDisplay = true
    }

    @objc private func layoutChanged() {
        meterView.gap = CGFloat(gapSlider.doubleValue)
        meterView.stripWidth = CGFloat(widthSlider.doubleValue)
        meterView.autoFit = fit.state == .on
        meterView.alignRight = right.state == .on
        widthSlider.isEnabled = !meterView.autoFit
        defaults.set(gapSlider.doubleValue, forKey: "LV1MeterGap")
        defaults.set(widthSlider.doubleValue, forKey: "LV1MeterWidth")
        defaults.set(meterView.autoFit, forKey: "LV1MeterAutoFit")
        defaults.set(meterView.alignRight, forKey: "LV1MeterAlignRight")
        meterView.needsDisplay = true
    }

    private func loadSettings() {
        gapSlider.doubleValue = defaults.object(forKey: "LV1MeterGap") == nil ? 6 : defaults.double(forKey: "LV1MeterGap")
        widthSlider.doubleValue = defaults.object(forKey: "LV1MeterWidth") == nil ? 72 : defaults.double(forKey: "LV1MeterWidth")
        fit.state = defaults.object(forKey: "LV1MeterAutoFit") == nil || defaults.bool(forKey: "LV1MeterAutoFit") ? .on : .off
        right.state = defaults.object(forKey: "LV1MeterAlignRight") == nil || defaults.bool(forKey: "LV1MeterAlignRight") ? .on : .off
        layoutChanged()
    }
}
