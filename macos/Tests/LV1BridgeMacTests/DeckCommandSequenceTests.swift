import Testing
@testable import LV1BridgeMac

struct DeckCommandSequenceTests {
    @Test func testWindowsSequenceForEveryMonitor() {
        for aux in 0..<24 {
            var state = DeckControlState()
            #expect(state.steps(for: .aux(aux)) == [
                .pulse(0x57), .wait(220),
                .pulse(UInt8(10 + aux / 8)), .wait(120), .pulse(UInt8(aux % 8))
            ], "Monitor \(aux + 1)")
            #expect(state.activeAux == aux)
            #expect(state.steps(for: .aux(aux)) == [.pulse(0x57)])
            #expect(state.activeAux == -1)
            #expect(state.steps(for: .mix) == [])
        }
    }

    @Test func testSwitchingMonitorDoesNotToggleFlipAgain() {
        var state = DeckControlState()
        _ = state.steps(for: .aux(0))
        #expect(state.steps(for: .aux(9)) == [.pulse(11), .wait(120), .pulse(1)])
        #expect(state.activeAux == 9)
    }

    @Test func testWindowsUserTransitions() {
        for key in 1...16 {
            var state = DeckControlState()
            _ = state.steps(for: .aux(2))
            #expect(state.steps(for: .user(key)) == [
                .pulse(0x57), .wait(150), .pulse(0x70), .wait(150), .pulse(UInt8(key - 1))
            ])
            #expect(state.activeAux == -1)
            #expect(state.steps(for: .user(key)) == [.pulse(UInt8(key - 1))])
            #expect(state.steps(for: .aux(0)) == [
                .pulse(0x70), .wait(120), .pulse(0x57), .wait(220),
                .pulse(10), .wait(120), .pulse(0)
            ])
        }
    }

    @Test func testRapidCommandsStayOrderedWithoutBlocking() {
        var notes: [UInt8] = []
        var waits: [Int] = []
        var work: [() -> Void] = []
        var completed: [Int] = []
        var sequence: DeckCommandSequence!
        sequence = DeckCommandSequence(pulse: { notes.append($0) }, schedule: { delay, callback in
            waits.append(delay)
            work.append(callback)
        }, changed: { completed.append(sequence.activeAux) })

        sequence.enqueue(.aux(0))
        sequence.enqueue(.aux(9))
        sequence.enqueue(.user(3))
        #expect(notes == [0x57])
        #expect(completed == [], "Do not report a completed selection during the mode change")
        while !work.isEmpty { work.removeFirst()() }
        #expect(notes == [0x57, 10, 0, 11, 1, 0x57, 0x70, 2])
        #expect(waits == [220, 120, 120, 150, 150])
        #expect(completed == [0, 9, -1])
    }

    @Test func testInvalidCommandsHaveNoEffects() {
        var state = DeckControlState()
        for command in [DeckCommand.aux(-1), .aux(24), .fx(-1), .fx(8), .user(0), .user(17)] {
            #expect(state.steps(for: command) == [])
            #expect(state.activeAux == -1)
        }
    }

    @Test func fxSelectionUsesSeparateBankAndFeedbackIndices() {
        for fx in 0..<8 {
            var state = DeckControlState()
            #expect(state.steps(for: .fx(fx)) == [
                .pulse(0x57), .wait(220), .pulse(9), .wait(120), .pulse(UInt8(fx))
            ])
            #expect(state.activeAux == 24 + fx)
            // MON 1 and FX 1 have the same local key, but are different selections.
            #expect(state.steps(for: .aux(0)) == [.pulse(10), .wait(120), .pulse(0)])
            #expect(state.steps(for: .fx(fx)) == [.pulse(9), .wait(120), .pulse(UInt8(fx))])
            #expect(state.steps(for: .fx(fx)) == [.pulse(0x57)])
            #expect(state.activeAux == -1)
        }
    }
}
