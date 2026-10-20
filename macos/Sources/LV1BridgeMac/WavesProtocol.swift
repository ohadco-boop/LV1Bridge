import Foundation

struct MeterFrame {
    var left = Array(repeating: UInt8(0), count: 16)
    var right = Array(repeating: UInt8(0), count: 16)
    var clip = Array(repeating: false, count: 16)
}

final class WavesProtocol {
    static let identityReply: [UInt8] = [0xF0,0x7E,0x00,0x06,0x02,0x00,0x00,0x74,0x01,0x00,0x01,0x00,0x01,0x00,0x00,0x00,0xF7]
    var onMeters: ((MeterFrame) -> Void)?
    var onNames: (([String]) -> Void)?

    func consume(_ p: [UInt8], reply: ([UInt8]) -> Void) {
        if p.count >= 5, p[0] == 0xF0, p[1] == 0x7E, p[3] == 0x06, p[4] == 0x01 {
            reply(Self.identityReply); return
        }
        guard p.count >= 8, Array(p.prefix(6)) == [0xF0,0,0,0x74,0x3C,0x1A], p.last == 0xF7 else { return }
        if p[6] == 4, p.count == 47 { decodeMeters(p) }
        else if p[6] == 3, p.count >= 202 { decodeNames(p) }
    }

    private func decodeMeters(_ p: [UInt8]) {
        var f = MeterFrame()
        for strip in 0..<16 {
            let li = strip * 2
            f.left[strip] = p[12 + li]
            f.right[strip] = p[13 + li]
            let lclip = (p[7 + li / 7] & UInt8(1 << (li % 7))) != 0
            let rclip = (p[7 + (li + 1) / 7] & UInt8(1 << ((li + 1) % 7))) != 0
            f.clip[strip] = lclip || rclip
        }
        onMeters?(f)
    }

    private func decodeNames(_ p: [UInt8]) {
        let folder = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Logs/LV1Bridge", isDirectory: true)
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        try? Data(p).write(to: folder.appendingPathComponent("names-packet.bin"), options: .atomic)
        let width = (p.count - 10) / 16
        guard (6...16).contains(width) else { return }
        var names = [String]()
        for strip in 0..<16 {
            let start = 9 + strip * width
            let end = min(start + width, p.count - 1)
            let bytes = p[start..<end].map { $0 == 0 ? 32 : $0 }
            names.append(String(bytes: bytes, encoding: .ascii)?.trimmingCharacters(in: .whitespaces) ?? "CH \(strip + 1)")
        }
        onNames?(names)
    }
}
