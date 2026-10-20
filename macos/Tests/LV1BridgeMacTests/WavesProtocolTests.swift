import Testing
@testable import LV1BridgeMac

struct WavesProtocolTests {
    @Test func identityRequestProducesWavesReply() {
        let protocolDecoder = WavesProtocol()
        var reply: [UInt8] = []
        protocolDecoder.consume([0xF0, 0x7E, 0x7F, 0x06, 0x01, 0xF7]) { reply = $0 }
        #expect(reply == WavesProtocol.identityReply)
    }

    @Test func meterPacketDecodesSixteenStereoPairs() {
        let protocolDecoder = WavesProtocol()
        var decoded: MeterFrame?
        protocolDecoder.onMeters = { decoded = $0 }
        var packet = Array(repeating: UInt8(0), count: 47)
        packet[0...6] = [0xF0, 0, 0, 0x74, 0x3C, 0x1A, 4]
        packet[12] = 42; packet[13] = 64; packet[46] = 0xF7
        protocolDecoder.consume(packet) { _ in }
        #expect(decoded?.left[0] == 42)
        #expect(decoded?.right[0] == 64)
    }

    @Test func namePacketDecodesSixteenNames() {
        let protocolDecoder = WavesProtocol()
        var decoded: [String] = []
        protocolDecoder.onNames = { decoded = $0 }
        var packet = Array(repeating: UInt8(32), count: 202)
        packet[0...6] = [0xF0, 0, 0, 0x74, 0x3C, 0x1A, 3]
        packet.replaceSubrange(9...14, with: Array("VOCAL1".utf8))
        packet[201] = 0xF7
        protocolDecoder.consume(packet) { _ in }
        #expect(decoded.count == 16)
        #expect(decoded[0] == "VOCAL1")
    }
}
