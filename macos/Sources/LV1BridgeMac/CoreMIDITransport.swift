import Foundation
import CoreMIDI

struct MIDIDevice: Hashable {
    let endpoint: MIDIEndpointRef
    let uniqueID: MIDIUniqueID
    let name: String
}

final class MIDIReadContext {
    var handler: ([UInt8]) -> Void
    private var sysex: [UInt8] = []

    init(handler: @escaping ([UInt8]) -> Void) { self.handler = handler }

    func receive(_ list: UnsafePointer<MIDIPacketList>) {
        var packet = list.pointee.packet
        for _ in 0..<list.pointee.numPackets {
            let bytes = withUnsafeBytes(of: packet.data) { Array($0.prefix(Int(packet.length))) }
            parse(bytes)
            packet = MIDIPacketNext(&packet).pointee
        }
    }

    private func parse(_ bytes: [UInt8]) {
        var short: [UInt8] = []
        for byte in bytes {
            if byte == 0xF0 {
                if !short.isEmpty { handler(short); short.removeAll(keepingCapacity: true) }
                sysex = [byte]
            } else if !sysex.isEmpty {
                sysex.append(byte)
                if byte == 0xF7 { handler(sysex); sysex.removeAll(keepingCapacity: true) }
            } else {
                short.append(byte)
            }
        }
        if !short.isEmpty { handler(short) }
    }
}

final class CoreMIDITransport {
    private var client = MIDIClientRef()
    private var outputPort = MIDIPortRef()
    private var controllerPorts: [MIDIPortRef] = []
    private var controllerContexts: [MIDIReadContext] = []
    private let lv1Context: MIDIReadContext
    private(set) var virtualToLV1 = MIDIEndpointRef()
    private(set) var virtualFromLV1 = MIDIEndpointRef()

    var onLV1Message: (([UInt8]) -> Void)?
    var onControllerMessage: ((Int, [UInt8]) -> Void)?

    init() throws {
        lv1Context = MIDIReadContext { _ in }
        guard MIDIClientCreateWithBlock("LV1Bridge" as CFString, &client, { _ in }) == noErr else {
            throw NSError(domain: "LV1Bridge.CoreMIDI", code: 1)
        }
        guard MIDIOutputPortCreate(client, "Controller Output" as CFString, &outputPort) == noErr else {
            throw NSError(domain: "LV1Bridge.CoreMIDI", code: 2)
        }
        guard MIDISourceCreate(client, "FaderBank In" as CFString, &virtualToLV1) == noErr else {
            throw NSError(domain: "LV1Bridge.CoreMIDI", code: 3)
        }
        let read: MIDIReadProc = { list, refCon, _ in
            guard let refCon else { return }
            Unmanaged<MIDIReadContext>.fromOpaque(refCon).takeUnretainedValue().receive(list)
        }
        guard MIDIDestinationCreate(client, "FaderBank Out" as CFString, read,
                                    Unmanaged.passUnretained(lv1Context).toOpaque(), &virtualFromLV1) == noErr else {
            throw NSError(domain: "LV1Bridge.CoreMIDI", code: 4)
        }
        lv1Context.handler = { [weak self] bytes in self?.onLV1Message?(bytes) }
    }

    deinit {
        disconnectControllers()
        if outputPort != 0 { MIDIPortDispose(outputPort) }
        if virtualFromLV1 != 0 { MIDIEndpointDispose(virtualFromLV1) }
        if virtualToLV1 != 0 { MIDIEndpointDispose(virtualToLV1) }
        if client != 0 { MIDIClientDispose(client) }
    }

    func connectControllerSources(_ devices: [MIDIDevice]) throws {
        disconnectControllers()
        let read: MIDIReadProc = { list, refCon, _ in
            guard let refCon else { return }
            Unmanaged<MIDIReadContext>.fromOpaque(refCon).takeUnretainedValue().receive(list)
        }
        for (index, device) in devices.enumerated() {
            let context = MIDIReadContext { [weak self] bytes in self?.onControllerMessage?(index, bytes) }
            var port = MIDIPortRef()
            let rc = MIDIInputPortCreate(client, "Controller Input \(index + 1)" as CFString, read,
                                         Unmanaged.passUnretained(context).toOpaque(), &port)
            guard rc == noErr else { throw NSError(domain: "LV1Bridge.CoreMIDI", code: Int(rc)) }
            let connect = MIDIPortConnectSource(port, device.endpoint, nil)
            guard connect == noErr else {
                MIDIPortDispose(port)
                throw NSError(domain: "LV1Bridge.CoreMIDI", code: Int(connect))
            }
            controllerContexts.append(context)
            controllerPorts.append(port)
        }
    }

    func disconnectControllers() {
        controllerPorts.forEach { MIDIPortDispose($0) }
        controllerPorts.removeAll()
        controllerContexts.removeAll()
    }

    func sendToLV1(_ bytes: [UInt8]) { inject(bytes, source: virtualToLV1) }

    func sendToController(_ bytes: [UInt8], endpoint: MIDIEndpointRef) {
        guard endpoint != 0 else { return }
        withPacketList(bytes) { list in _ = MIDISend(outputPort, endpoint, list) }
    }

    private func inject(_ bytes: [UInt8], source: MIDIEndpointRef) {
        withPacketList(bytes) { list in _ = MIDIReceived(source, list) }
    }

    private func withPacketList(_ bytes: [UInt8], _ body: (UnsafePointer<MIDIPacketList>) -> Void) {
        guard !bytes.isEmpty else { return }
        let capacity = max(1024, MemoryLayout<MIDIPacketList>.size + bytes.count + 64)
        let raw = UnsafeMutableRawPointer.allocate(byteCount: capacity, alignment: 8)
        defer { raw.deallocate() }
        let list = raw.bindMemory(to: MIDIPacketList.self, capacity: 1)
        let packet = MIDIPacketListInit(list)
        bytes.withUnsafeBufferPointer { data in
            guard let base = data.baseAddress else { return }
            _ = MIDIPacketListAdd(list, capacity, packet, 0, bytes.count, base)
            body(UnsafePointer(list))
        }
    }

    static func sources() -> [MIDIDevice] { devices(count: MIDIGetNumberOfSources, get: MIDIGetSource) }
    static func destinations() -> [MIDIDevice] { devices(count: MIDIGetNumberOfDestinations, get: MIDIGetDestination) }

    private static func devices(count: () -> Int, get: (Int) -> MIDIEndpointRef) -> [MIDIDevice] {
        (0..<count()).compactMap { index in
            let endpoint = get(index)
            let device = MIDIDevice(endpoint: endpoint, uniqueID: uniqueID(endpoint), name: name(endpoint))
            return device.name.hasPrefix("FaderBank ") ? nil : device
        }
    }

    private static func uniqueID(_ object: MIDIObjectRef) -> MIDIUniqueID {
        var value = MIDIUniqueID()
        MIDIObjectGetIntegerProperty(object, kMIDIPropertyUniqueID, &value)
        return value
    }

    private static func name(_ object: MIDIObjectRef) -> String {
        var value: Unmanaged<CFString>?
        guard MIDIObjectGetStringProperty(object, kMIDIPropertyDisplayName, &value) == noErr else { return "MIDI \(object)" }
        return value?.takeRetainedValue() as String? ?? "MIDI \(object)"
    }
}
