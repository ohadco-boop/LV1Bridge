import Foundation
import CoreMIDI

enum ControllerProfile: String, CaseIterable {
    case behringer = "X-Touch Compact + Extender"
    case icon = "iCON P1-M + P1-X"
}

final class BridgeEngine {
    let transport: CoreMIDITransport
    let waves = WavesProtocol()
    var profile: ControllerProfile = .behringer
    var controllerOutputs: [MIDIEndpointRef] = []
    var onStateChanged: (() -> Void)?
    var onLog: ((String) -> Void)?

    private(set) var names = (1...16).map { "CH \($0)" }
    private(set) var auxNames = (1...24).map { "MON \($0)" }
    private(set) var userNames = (1...16).map { "USER \($0)" }
    private(set) var meters = MeterFrame()
    private(set) var mono = Array(repeating: true, count: 16)
    private(set) var receivedFromLV1 = false
    var activeAux: Int { deckCommands.activeAux }
    private lazy var deckCommands = DeckCommandSequence(
        pulse: { [weak self] note in self?.pulse(note) },
        changed: { [weak self] in self?.onStateChanged?() }
    )
    private var identityTimer: Timer?
    private var metadata: SessionMetadata?
    private var metadataLoaded = false

    init(transport: CoreMIDITransport) {
        self.transport = transport
        transport.onLV1Message = { [weak self] bytes in
            DispatchQueue.main.async { self?.receiveFromLV1(bytes) }
        }
        transport.onControllerMessage = { [weak self] index, bytes in
            DispatchQueue.main.async { self?.receiveFromController(index, bytes) }
        }
        waves.onMeters = { [weak self] frame in
            guard let self else { return }
            meters = frame; receivedFromLV1 = true
            forwardMeters(frame); onStateChanged?()
        }
        waves.onNames = { [weak self] values in
            guard let self else { return }
            if !metadataLoaded { names = values }
            receivedFromLV1 = true
            forwardNames(names); onStateChanged?()
        }
        metadata = SessionMetadata(controlsUpdate: { [weak self] aux, users in
            guard let self else { return }
            let changed = self.auxNames != aux || self.userNames != users
            self.auxNames = aux
            self.userNames = users
            if changed { self.onStateChanged?() }
        }) { [weak self] values, mono in
            guard let self else { return }
            let namesChanged = names != values
            let changed = namesChanged || self.mono != mono || !metadataLoaded
            names = values; self.mono = mono; metadataLoaded = true
            if namesChanged { forwardNames(values) }
            if changed { onStateChanged?() }
        }
        metadata?.start()
    }

    func startIdentityHeartbeat() {
        identityTimer?.invalidate()
        advertiseIdentity()
        identityTimer = Timer.scheduledTimer(withTimeInterval: 2, repeats: true) { [weak self] _ in self?.advertiseIdentity() }
    }

    func advertiseIdentity() { transport.sendToLV1(WavesProtocol.identityReply) }

    func receiveFromLV1(_ bytes: [UInt8]) {
        if bytes.first == 0xF0 {
            waves.consume(bytes) { [weak self] reply in self?.transport.sendToLV1(reply) }
            return
        }
        var index = 0
        while index < bytes.count {
            let status = bytes[index]
            let length = (status & 0xF0) == 0xC0 || (status & 0xF0) == 0xD0 ? 2 : 3
            guard index + length <= bytes.count else { break }
            let d1 = bytes[index + 1], d2 = length == 3 ? bytes[index + 2] : 0
            receivedFromLV1 = true
            forwardShortFromLV1(status, d1, d2)
            index += length
        }
        onStateChanged?()
    }

    func receiveFromController(_ device: Int, _ bytes: [UInt8]) {
        guard bytes.first != 0xF0 else { return }
        var index = 0
        while index + 2 < bytes.count {
            mapController(device: device, status: bytes[index], d1: bytes[index + 1], d2: bytes[index + 2])
            index += 3
        }
    }

    private func mapController(device: Int, status: UInt8, d1: UInt8, d2: UInt8) {
        if profile == .icon { mapMCU(device: device, status: status, d1: d1, d2: d2); return }
        let type = status & 0xF0
        if device == 0 {
            if type == 0xB0, (1...8).contains(d1) { sendPitch(Int(d1 - 1), value7: d2) }
            else if type == 0xB0, (10...17).contains(d1) { send([0xB0, 16 + d1 - 10, d2]) }
            else if type == 0x90 || type == 0x80 {
                let down: UInt8 = type == 0x90 && d2 > 0 ? 127 : 0
                if d1 <= 7 { send([0x90, 16 + d1, down]) }
                else if (16...23).contains(d1) { send([0x90, 32 + d1 - 16, down]) }
                else if (24...31).contains(d1) { send([0x90, 48 + d1 - 24, down]) }
                else if (32...39).contains(d1) { send([0x90, d1 - 32, down]) }
            }
        } else {
            if type == 0xB0, (70...77).contains(d1) { sendPitch(Int(8 + d1 - 70), value7: d2) }
            else if type == 0xB0, (80...87).contains(d1) { send([0xB0, 24 + d1 - 80, d2]) }
            else if type == 0x90 || type == 0x80 {
                let down: UInt8 = type == 0x90 && d2 > 0 ? 127 : 0
                if d1 <= 7 { send([0x90, 24 + d1, down]) }
                else if (16...23).contains(d1) { send([0x90, 40 + d1 - 16, down]) }
                else if (24...31).contains(d1) { send([0x90, 56 + d1 - 24, down]) }
                else if (32...39).contains(d1) { send([0x90, 8 + d1 - 32, down]) }
            }
        }
    }

    private func mapMCU(device: Int, status: UInt8, d1: UInt8, d2: UInt8) {
        let type = status & 0xF0, channel = Int(status & 0x0F), offset = device * 8
        if type == 0xE0, channel < 8 { send([UInt8(0xE0 | (offset + channel)), d1, d2]); return }
        if type == 0xB0, (16...23).contains(d1) { send([0xB0, UInt8(16 + offset) + d1 - 16, d2]); return }
        guard type == 0x90 || type == 0x80 else { return }
        let down: UInt8 = type == 0x90 && d2 > 0 ? 127 : 0
        var local = -1, group = -1
        if d1 <= 7 { local = Int(d1); group = 16 }
        else if d1 <= 15 { local = Int(d1 - 8); group = 32 }
        else if d1 <= 23 { local = Int(d1 - 16); group = 48 }
        else if d1 <= 31 { local = Int(d1 - 24); group = 0 }
        else if (104...111).contains(d1) { local = Int(d1 - 104); group = 104 }
        if local >= 0 { send([0x90, UInt8(group + offset + local), down]) }
    }

    private func forwardShortFromLV1(_ status: UInt8, _ d1: UInt8, _ d2: UInt8) {
        let type = status & 0xF0, channel = Int(status & 0x0F)
        if type == 0xE0 { setFader(channel, UInt8((Int(d1) | Int(d2) << 7) >> 7)) }
        else if type == 0xA0, d1 < 16 { setFader(Int(d1), d2) }
        else if type == 0xB0, (16...31).contains(d1) { setRing(Int(d1 - 16), d2) }
        else if type == 0x90 || type == 0x80 { setButton(Int(d1), type == 0x90 && d2 > 0) }
    }

    private func setFader(_ strip: Int, _ value: UInt8) {
        guard strip < 16 else { return }
        if profile == .icon {
            let v = Int(value) << 7 | Int(value)
            output(strip / 8, [UInt8(0xE0 | (strip & 7)), UInt8(v & 127), UInt8(v >> 7)])
        } else if strip < 8 { output(0, [0xB0, UInt8(1 + strip), value]) }
        else { output(1, [0xB0, UInt8(70 + strip - 8), value]) }
    }

    private func setRing(_ strip: Int, _ value: UInt8) {
        guard strip < 16 else { return }
        if profile == .icon {
            let position = UInt8(max(1, min(11, 1 + Int(value) * 10 / 127)))
            output(strip / 8, [0xB0, UInt8(48 + (strip & 7)), 0x10 | position])
        } else if strip < 8 { output(0, [0xB0, UInt8(26 + strip), value]) }
        else { output(1, [0xB0, UInt8(80 + strip - 8), value]) }
    }

    private func setButton(_ note: Int, _ on: Bool) {
        let value: UInt8 = on ? 127 : 0
        var strip = 0, mapped = 0
        if note < 16 { strip = note; mapped = profile == .icon ? 24 + (strip & 7) : 32 + (strip & 7) }
        else if note < 32 { strip = note - 16; mapped = strip & 7 }
        else if note < 48 { strip = note - 32; mapped = 8 + (strip & 7) }
        else if note < 64 { strip = note - 48; mapped = 16 + (strip & 7) }
        else { return }
        output(strip / 8, [0x90, UInt8(mapped), value])
    }

    private func forwardMeters(_ frame: MeterFrame) {
        for strip in 0..<16 {
            let level = frame.clip[strip] ? 127 : max(frame.left[strip], frame.right[strip])
            if profile == .icon {
                let meter = level == 0 ? 0 : min(13, 1 + Int(level) * 12 / 127)
                output(strip / 8, [0xD0, UInt8(((strip & 7) << 4) | meter)])
            } else if strip < 8 {
                output(0, [0xB0, UInt8(26 + strip), UInt8(level == 0 ? 0 : 1 + Int(level) * 12 / 127)])
            } else { output(1, [0xB0, UInt8(90 + strip - 8), level]) }
        }
    }

    private func forwardNames(_ values: [String]) {
        guard profile == .icon else { return }
        for strip in 0..<min(16, values.count) {
            let padded = Array(values[strip].padding(toLength: 14, withPad: " ", startingAt: 0).utf8.prefix(14))
            let device: UInt8 = strip < 8 ? 0x14 : 0x15, local = strip & 7
            output(strip / 8, [0xF0,0,0,0x66,device,0x12,UInt8(local * 7)] + Array(padded.prefix(7)) + [0xF7])
            output(strip / 8, [0xF0,0,0,0x66,device,0x12,UInt8(56 + local * 7)] + Array(padded.dropFirst(7)) + [0xF7])
        }
    }

    func selectAux(_ aux: Int) {
        guard (0..<24).contains(aux) else { return }
        deckCommands.enqueue(.aux(aux))
    }

    func returnToMix() { deckCommands.enqueue(.mix) }
    func triggerUser(_ index: Int) {
        guard (1...16).contains(index) else { return }
        deckCommands.enqueue(.user(index))
    }

    private func pulse(_ note: UInt8) { send([0x90, note, 127]); send([0x90, note, 0]) }
    private func sendPitch(_ channel: Int, value7: UInt8) {
        let value = Int(value7) << 7 | Int(value7)
        send([UInt8(0xE0 | channel), UInt8(value & 127), UInt8(value >> 7)])
    }
    private func send(_ bytes: [UInt8]) { transport.sendToLV1(bytes) }
    private func output(_ index: Int, _ bytes: [UInt8]) {
        guard controllerOutputs.indices.contains(index) else { return }
        transport.sendToController(bytes, endpoint: controllerOutputs[index])
    }
}
