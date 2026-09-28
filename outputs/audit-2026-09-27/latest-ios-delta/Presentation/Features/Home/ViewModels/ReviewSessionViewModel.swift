import Foundation
import SwiftUI

protocol ReviewSessionRepository:
    ReviewQueueRepository,
    FSRSConfigurationProviding
{
    func markIntroducedIfNeeded(
        cardID: String,
        now: Date
    ) throws -> ReviewCardRecord

    func apply(
        transition: ReviewTransition,
        attemptID: String,
        durationMS: Int?,
        wordSnapshot: ReviewWordSnapshot,
        configuration: FSRSConfiguration
    ) throws -> ApplyReviewResult
}

protocol ReviewSettingsProviding {
    func snapshot(now: Date) throws -> ReviewSettingsSnapshot
}

struct ReviewSessionDependencies {
    let repository: ReviewSessionRepository
    let scheduler: ReviewScheduling
    let queue: ReviewQueueService
    let settings: any ReviewSettingsProviding
    let now: () -> Date
    let makeAttemptID: () -> String
    var secondaryReview: SecondaryReviewService? = nil
}

struct PendingReviewAttempt {
    let id: String
    let transition: ReviewTransition
    let durationMS: Int?
    let wordSnapshot: ReviewWordSnapshot
    let configuration: FSRSConfiguration
}

extension ReviewCardRepository: ReviewSessionRepository {}
extension FSRSSettingsStore: @MainActor ReviewSettingsProviding {}

@MainActor
final class ReviewSessionViewModel: ObservableObject {
    @Published private(set) var queue: ReviewQueue = .empty
    @Published private(set) var current: ReviewQueueCandidate?
    @Published private(set) var previews: [ReviewGrade: ReviewTransition] = [:]
    @Published private(set) var showAnswer = false
    @Published private(set) var isSubmitting = false
    @Published private(set) var loadError: String?
    @Published private(set) var saveError: String?
    @Published private(set) var completedCount = 0
    @Published var sessionFlip = false

    private struct SessionRequest {
        let nativeLanguage: String
        let learningLanguage: String
        let cardID: String?
        var selection: Selection
    }

    private enum Selection {
        case scheduled
        case focused
        case replay([String])
    }

    private struct Presentation {
        let queue: ReviewQueue
        let current: ReviewQueueCandidate?
        let previews: [ReviewGrade: ReviewTransition]
    }

    private let dependencies: ReviewSessionDependencies
    private var sessionGeneration: UInt64 = 0
    private var submissionInFlight = false
    private var request: SessionRequest?
    private var settingsSnapshot: ReviewSettingsSnapshot?
    private var pendingAttempt: PendingReviewAttempt?
    private var sessionCardIDs: [String] = []
    private var reviewedCardIDs: Set<String> = []

    init(dependencies: ReviewSessionDependencies) {
        self.dependencies = dependencies
    }

    func load(
        nativeLanguage: String,
        learningLanguage: String,
        cardID: String? = nil
    ) {
        loadSession(SessionRequest(
            nativeLanguage: nativeLanguage,
            learningLanguage: learningLanguage,
            cardID: cardID,
            selection: cardID == nil ? .scheduled : .focused
        ))
    }

    private func loadSession(_ request: SessionRequest, preservingProgress: Bool = false) {
        sessionGeneration += 1
        let generation = sessionGeneration
        resetForLoad(preservingProgress: preservingProgress)
        let capturedNow = dependencies.now()
        guard isCurrent(generation) else { return }
        self.request = request

        do {
            let snapshot = try dependencies.settings.snapshot(now: capturedNow)
            guard isCurrent(generation) else { return }
            guard let loaded = try loadQueue(
                request: request,
                snapshot: snapshot,
                now: capturedNow,
                generation: generation
            ) else {
                return
            }
            guard let presentation = try preparePresentation(
                loaded,
                snapshot: snapshot,
                now: capturedNow,
                generation: generation
            ) else {
                return
            }
            guard isCurrent(generation) else { return }
            // Freeze the fallback cohort so newly synced cards do not extend a replay.
            if case .replay(let cardIDs) = request.selection, cardIDs.isEmpty {
                self.request?.selection = .replay(loaded.items.map(\.card.id))
            }
            settingsSnapshot = snapshot
            publish(presentation)
        } catch {
            guard isCurrent(generation) else { return }
            settingsSnapshot = nil
            loadError = error.localizedDescription
        }
    }

    func revealAnswer() {
        guard current != nil else { return }
        showAnswer = true
    }

    func restart() {
        guard let request, current == nil, !submissionInFlight else { return }
        let cardIDs = request.cardID.map { [$0] } ?? sessionCardIDs
        loadSession(SessionRequest(
            nativeLanguage: request.nativeLanguage,
            learningLanguage: request.learningLanguage,
            cardID: request.cardID,
            selection: .replay(cardIDs)
        ))
    }

    func refresh(nativeLanguage: String, learningLanguage: String, cardID: String? = nil) {
        guard let request,
              request.nativeLanguage == nativeLanguage,
              request.learningLanguage == learningLanguage,
              request.cardID == cardID else {
            load(nativeLanguage: nativeLanguage, learningLanguage: learningLanguage, cardID: cardID)
            return
        }
        switch request.selection {
        case .scheduled:
            load(nativeLanguage: nativeLanguage, learningLanguage: learningLanguage, cardID: cardID)
        case .focused, .replay:
            loadSession(request, preservingProgress: true)
        }
    }

    func retryLoad() {
        guard let request, !submissionInFlight else { return }
        if settingsSnapshot != nil {
            refreshAfterSuccessfulApply(generation: sessionGeneration)
        } else {
            loadSession(request, preservingProgress: true)
        }
    }

    func rate(_ grade: ReviewGrade, durationMS: Int? = nil) {
        let generation = sessionGeneration
        guard
            showAnswer,
            let current,
            let settingsSnapshot,
            !submissionInFlight
        else {
            return
        }

        submissionInFlight = true
        isSubmitting = true
        defer {
            submissionInFlight = false
            if isCurrent(generation) {
                isSubmitting = false
            }
        }

        do {
            let attempt: PendingReviewAttempt
            if let pendingAttempt {
                attempt = pendingAttempt
            } else {
                let reviewedAt = dependencies.now()
                guard isCurrent(generation) else { return }
                let transition = try dependencies.scheduler.schedule(
                    card: current.card,
                    grade: grade,
                    now: reviewedAt,
                    configuration: settingsSnapshot.configuration
                )
                guard isCurrent(generation) else { return }
                let attemptID = dependencies.makeAttemptID()
                guard isCurrent(generation) else { return }
                attempt = PendingReviewAttempt(
                    id: attemptID,
                    transition: transition,
                    durationMS: durationMS,
                    wordSnapshot: wordSnapshot(for: current.word),
                    configuration: settingsSnapshot.configuration
                )
                pendingAttempt = attempt
            }

            _ = try dependencies.repository.apply(
                transition: attempt.transition,
                attemptID: attempt.id,
                durationMS: attempt.durationMS,
                wordSnapshot: attempt.wordSnapshot,
                configuration: attempt.configuration
            )
            guard isCurrent(generation) else { return }
            pendingAttempt = nil
            saveError = nil
            completedCount += 1
            reviewedCardIDs.insert(current.card.id)
            refreshAfterSuccessfulApply(generation: generation)
        } catch {
            guard isCurrent(generation) else { return }
            saveError = error.localizedDescription
        }
    }

    func swapReviewSide() {
        sessionFlip.toggle()
    }

    private func resetForLoad(preservingProgress: Bool = false) {
        queue = .empty
        current = nil
        previews = [:]
        showAnswer = false
        isSubmitting = false
        loadError = nil
        saveError = nil
        request = nil
        settingsSnapshot = nil
        pendingAttempt = nil
        if !preservingProgress {
            completedCount = 0
            sessionCardIDs = []
            reviewedCardIDs = []
        }
    }

    private func refreshAfterSuccessfulApply(generation: UInt64) {
        guard isCurrent(generation) else { return }
        guard let request, let settingsSnapshot else {
            queue = .empty
            current = nil
            previews = [:]
            showAnswer = false
            return
        }

        let capturedNow = dependencies.now()
        guard isCurrent(generation) else { return }
        do {
            guard let refreshed = try loadQueue(
                request: request,
                snapshot: settingsSnapshot,
                now: capturedNow,
                generation: generation
            ) else {
                return
            }
            guard let presentation = try preparePresentation(
                refreshed,
                snapshot: settingsSnapshot,
                now: capturedNow,
                generation: generation
            ) else {
                return
            }
            guard isCurrent(generation) else { return }
            loadError = nil
            publish(presentation)
        } catch {
            guard isCurrent(generation) else { return }
            queue = .empty
            current = nil
            previews = [:]
            showAnswer = false
            loadError = error.localizedDescription
        }
    }

    private func loadQueue(
        request: SessionRequest,
        snapshot: ReviewSettingsSnapshot,
        now: Date,
        generation: UInt64
    ) throws -> ReviewQueue? {
        let queueRequest = ReviewQueueRequest(
            nativeLanguage: request.nativeLanguage,
            learningLanguage: request.learningLanguage,
            now: now,
            studyDay: snapshot.studyDay,
            primaryDirection: snapshot.primaryDirection,
            newWordsPerDay: snapshot.newWordsPerDay
        )
        if case .replay(let cardIDs) = request.selection {
            let eligible = try dependencies.queue.project(queueRequest).eligibleCandidates
            guard isCurrent(generation) else { return nil }
            let byID = Dictionary(uniqueKeysWithValues: eligible.map { ($0.card.id, $0) })
            let selected = cardIDs.isEmpty ? eligible : cardIDs.compactMap { byID[$0] }
            let items = selected.filter {
                !reviewedCardIDs.contains($0.card.id)
                    && ($0.card.state != .new || $0.card.introducedAt != nil)
            }
            return ReviewQueue(items: items, counts: ReviewQueueCounts(
                new: items.filter { $0.card.state == .new }.count,
                learning: items.filter { $0.card.state == .learning || $0.card.state == .relearning }.count,
                review: items.filter { $0.card.state == .review }.count
            ))
        }
        guard let cardID = request.cardID else {
            return try dependencies.queue.build(queueRequest) {
                isCurrent(generation)
            }
        }
        guard !reviewedCardIDs.contains(cardID) else { return .empty }
        let candidate = try dependencies.queue.focused(
            FocusedReviewRequest(cardID: cardID, queueRequest: queueRequest)
        )
        guard isCurrent(generation) else { return nil }
        guard let candidate else {
            return .empty
        }
        return ReviewQueue(
            items: [candidate],
            counts: counts(for: candidate.card.state)
        )
    }

    private func preparePresentation(
        _ loaded: ReviewQueue,
        snapshot: ReviewSettingsSnapshot,
        now: Date,
        generation: UInt64
    ) throws -> Presentation? {
        guard isCurrent(generation) else { return nil }
        guard var candidate = loaded.items.first else {
            return Presentation(queue: loaded, current: nil, previews: [:])
        }

        var preparedQueue = loaded
        if candidate.card.state == .new && candidate.card.introducedAt == nil {
            let introduced = try dependencies.repository.markIntroducedIfNeeded(
                cardID: candidate.card.id,
                now: now
            )
            guard isCurrent(generation) else { return nil }
            candidate = ReviewQueueCandidate(
                card: introduced,
                word: candidate.word,
                activeSetIDs: candidate.activeSetIDs,
                isLocked: candidate.isLocked
            )
            var items = loaded.items
            items[0] = candidate
            preparedQueue = ReviewQueue(items: items, counts: loaded.counts)
        }

        let previews = try dependencies.scheduler.previews(
            for: candidate.card,
            now: now,
            configuration: snapshot.configuration
        )
        guard isCurrent(generation) else { return nil }
        return Presentation(
            queue: preparedQueue,
            current: candidate,
            previews: previews
        )
    }

    private func isCurrent(_ generation: UInt64) -> Bool {
        generation == sessionGeneration
    }

    private func publish(_ presentation: Presentation) {
        let known = Set(sessionCardIDs)
        sessionCardIDs.append(contentsOf: presentation.queue.items.map(\.card.id).filter { !known.contains($0) })
        queue = presentation.queue
        current = presentation.current
        previews = presentation.previews
        showAnswer = false
        loadError = nil
    }

    private func counts(for state: ReviewCardState) -> ReviewQueueCounts {
        switch state {
        case .new:
            ReviewQueueCounts(new: 1, learning: 0, review: 0)
        case .learning, .relearning:
            ReviewQueueCounts(new: 0, learning: 1, review: 0)
        case .review:
            ReviewQueueCounts(new: 0, learning: 0, review: 1)
        }
    }

    private func wordSnapshot(for word: WordDashboardRow) -> ReviewWordSnapshot {
        ReviewWordSnapshot(
            displayWord: word.displayWord,
            translations: word.displayTranslations,
            learningLanguage: word.learningLanguageBcp47,
            nativeLanguage: word.nativeLanguageBcp47
        )
    }
}


/// A local exercise, deliberately independent of grading and scheduling.
struct SpellingPractice {
    var answer = "" {
        didSet { isCorrect = nil }
    }
    private(set) var isCorrect: Bool?

    var canCheck: Bool { !Self.normalized(answer).isEmpty }

    mutating func check(expected: String) {
        isCorrect = canCheck ? Self.matches(answer: answer, expected: expected) : nil
    }

    static func matches(answer: String, expected: String) -> Bool {
        let answer = normalized(answer)
        return !answer.isEmpty && answer == normalized(expected)
    }

    private static func normalized(_ value: String) -> String {
        value.lowercased().precomposedStringWithCanonicalMapping
            .split(whereSeparator: \.isWhitespace).joined(separator: " ")
    }
}
