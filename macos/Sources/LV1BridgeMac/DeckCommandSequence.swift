import Foundation

enum DeckCommand {
    case aux(Int) // zero-based monitor send, 0...23
    case fx(Int) // zero-based FX send, 0...7; feedback uses slots 24...31
    case mix
    case user(Int) // one-based user key, 1...16
}

enum DeckStep: Equatable {
    case pulse(UInt8)
    case wait(Int) // milliseconds between LV1 mode/page changes
}

struct DeckControlState {
    private(set) var activeAux = -1
    private var userMode = false

    // Match SelectAuxFromTouch / ReturnToMainMix / HandleDeckApi in the
    // working Windows bridge. 0x57 is AUX flip; 0x71 is not AUX flip.
    mutating func steps(for command: DeckCommand) -> [DeckStep] {
        var steps: [DeckStep] = []
        switch command {
        case .aux, .fx:
            let selection: Int, bank: UInt8, key: UInt8
            switch command {
            case .aux(let aux):
                guard (0..<24).contains(aux) else { return [] }
                selection = aux; bank = UInt8(10 + aux / 8); key = UInt8(aux % 8)
            case .fx(let fx):
                guard (0..<8).contains(fx) else { return [] }
                selection = 24 + fx; bank = 9; key = UInt8(fx)
            default: return []
            }
            if activeAux == selection { return self.steps(for: .mix) }
            if userMode {
                steps += [.pulse(0x70), .wait(120)]
                userMode = false
            }
            if activeAux < 0 { steps += [.pulse(0x57), .wait(220)] }
            steps += [.pulse(bank), .wait(120), .pulse(key)]
            activeAux = selection
        case .mix:
            if activeAux >= 0 { steps += [.pulse(0x57)] }
            activeAux = -1
        case .user(let index):
            guard (1...16).contains(index) else { return [] }
            if activeAux >= 0 {
                steps += self.steps(for: .mix)
                steps += [.wait(150)]
            }
            if !userMode {
                steps += [.pulse(0x70), .wait(150)]
                userMode = true
            }
            steps += [.pulse(UInt8(index - 1))]
        }
        return steps
    }
}

final class DeckCommandSequence {
    typealias Schedule = (Int, @escaping () -> Void) -> Void
    private let pulse: (UInt8) -> Void
    private let schedule: Schedule
    private let changed: () -> Void
    private var state = DeckControlState()
    private var pending: [DeckCommand] = []
    private var running = false
    var activeAux: Int { state.activeAux }

    init(pulse: @escaping (UInt8) -> Void,
         schedule: @escaping Schedule = { milliseconds, work in
             DispatchQueue.main.asyncAfter(deadline: .now() + .milliseconds(milliseconds), execute: work)
         },
         changed: @escaping () -> Void) {
        self.pulse = pulse
        self.schedule = schedule
        self.changed = changed
    }

    // All callers run on the main queue. Keep each complete mode/page/key
    // transaction together without blocking meters or the app's UI.
    func enqueue(_ command: DeckCommand) {
        pending.append(command)
        startNext()
    }

    private func startNext() {
        guard !running, !pending.isEmpty else { return }
        running = true
        var next = state
        let steps = next.steps(for: pending.removeFirst())
        execute(steps, from: 0) { [weak self] in
            guard let self else { return }
            self.state = next
            self.running = false
            self.changed()
            self.startNext()
        }
    }

    private func execute(_ steps: [DeckStep], from start: Int, completion: @escaping () -> Void) {
        var index = start
        while index < steps.count {
            let step = steps[index]
            index += 1
            switch step {
            case .pulse(let note): pulse(note)
            case .wait(let milliseconds):
                let resume = index
                schedule(milliseconds) { [weak self] in
                    self?.execute(steps, from: resume, completion: completion)
                }
                return
            }
        }
        completion()
    }
}
