import Foundation
import Network

final class BridgeAPI {
    private let engine: BridgeEngine
    private var listener: NWListener?
    private let queue = DispatchQueue(label: "LV1Bridge.API")

    init(engine: BridgeEngine) { self.engine = engine }

    func start() throws {
        let listener = try NWListener(using: .tcp, on: 49631)
        listener.newConnectionHandler = { [weak self] connection in self?.accept(connection) }
        listener.start(queue: queue)
        self.listener = listener
    }

    private func accept(_ connection: NWConnection) {
        connection.start(queue: queue)
        connection.receive(minimumIncompleteLength: 1, maximumLength: 8192) { [weak self] data, _, _, _ in
            guard let self, let data, let text = String(data: data, encoding: .utf8) else { connection.cancel(); return }
            let isHTTP = text.hasPrefix("GET ") || text.hasPrefix("POST ") || text.hasPrefix("OPTIONS ")
            let command: String
            if isHTTP {
                let first = text.components(separatedBy: "\r\n").first ?? ""
                let target = first.split(separator: " ").dropFirst().first.map(String.init) ?? "/PING"
                command = target.dropFirst().removingPercentEncoding?.replacingOccurrences(of: "/", with: " ") ?? "PING"
            } else { command = text.trimmingCharacters(in: .whitespacesAndNewlines) }
            // MIDI and session metadata publish on the main queue. Read a
            // complete snapshot there so Deck labels, levels and mono flags
            // belong to the same update.
            let reply = DispatchQueue.main.sync { self.handle(command.uppercased()) }
            let payload = isHTTP
                ? "HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nAccess-Control-Allow-Origin: *\r\nContent-Length: \(reply.utf8.count)\r\nConnection: close\r\n\r\n\(reply)"
                : reply + "\n"
            connection.send(content: payload.data(using: .utf8), completion: .contentProcessed { _ in connection.cancel() })
        }
    }

    private func handle(_ command: String) -> String {
        if command == "PING" { return "PONG" }
        if command == "STATE" {
            return (["STATE", String(engine.activeAux)] + engine.auxNames.map(pack) + engine.userNames.map(pack) + engine.fxNames.map(pack)).joined(separator: "|")
        }
        if command == "METERS" {
            var fields = ["METERS", engine.receivedFromLV1 ? "1" : "0"]
            for index in 0..<16 {
                fields += [pack(engine.names[index]), String(engine.meters.left[index]), String(engine.meters.right[index])]
            }
            fields += engine.mono.map { $0 ? "1" : "0" }
            return fields.joined(separator: "|")
        }
        let parts = command.split(separator: " ")
        if parts.count == 2, let value = Int(parts[1]), parts[0] == "AUX", (1...24).contains(value) {
            DispatchQueue.main.async { self.engine.selectAux(value - 1) }; return "OK"
        }
        if command == "MIX" { DispatchQueue.main.async { self.engine.returnToMix() }; return "OK" }
        if parts.count == 2, let value = Int(parts[1]), parts[0] == "FX", (1...8).contains(value) {
            DispatchQueue.main.async { self.engine.selectFX(value - 1) }; return "OK"
        }
        if parts.count == 2, let value = Int(parts[1]), parts[0] == "USER", (1...16).contains(value) {
            DispatchQueue.main.async { self.engine.triggerUser(value) }; return "OK"
        }
        return "ERR"
    }

    private func pack(_ value: String) -> String { Data(value.utf8).base64EncodedString() }
}
