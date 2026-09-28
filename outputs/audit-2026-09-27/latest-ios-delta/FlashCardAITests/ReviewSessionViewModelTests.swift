import Combine
import XCTest
import SwiftUI
import UIKit
@testable import FlashCardAI

@MainActor
final class ReviewSessionViewModelTests: XCTestCase {
    private static let dayStart = Date(timeIntervalSince1970: 2_100_000_000)
    private static let now = dayStart.addingTimeInterval(43_200)
    private static let studyDay = StudyDay(
        start: dayStart,
        end: dayStart.addingTimeInterval(86_400)
    )

    func testPreviewIntervalFormatterUsesExactUnitBoundaries() {
        XCTAssertEqual(ReviewIntervalFormatter.string(seconds: 60), "1m")
        XCTAssertEqual(ReviewIntervalFormatter.string(seconds: 3_600), "1h")
        XCTAssertEqual(ReviewIntervalFormatter.string(seconds: 86_400), "1d")
        XCTAssertEqual(ReviewIntervalFormatter.string(seconds: 518_400), "6d")
    }

    func testRatingGradesAreHiddenUntilRevealThenShowAllFourInOrder() {
        XCTAssertEqual(
            ReviewGradePresentation.grades(isRevealed: false),
            []
        )
        XCTAssertEqual(
            ReviewGradePresentation.grades(isRevealed: true),
            [.again, .hard, .good, .easy]
        )
    }

    func testGlobalLoadBuildsQueueAndFocusedLoadResolvesOnlyRequestedCard() {
        let first = makeCandidate(id: "first", state: .learning)
        let second = makeCandidate(id: "second", state: .review)
        let globalRepository = RepositoryFake(
            snapshots: [snapshot([first, second])]
        )
        let global = makeViewModel(repository: globalRepository)

        global.viewModel.load(nativeLanguage: "en-us", learningLanguage: "es")

        XCTAssertEqual(global.viewModel.current?.card.id, "first")
        XCTAssertEqual(
            global.viewModel.queue.counts,
            ReviewQueueCounts(new: 0, learning: 1, review: 1)
        )
        XCTAssertEqual(globalRepository.createCalls.count, 1)

        let focusedRepository = RepositoryFake(
            snapshots: [snapshot([first, second])]
        )
        let focused = makeViewModel(repository: focusedRepository)

        focused.viewModel.load(
            nativeLanguage: "en-us",
            learningLanguage: "es",
            cardID: "second"
        )

        XCTAssertEqual(focused.viewModel.current?.card.id, "second")
        XCTAssertEqual(
            focused.viewModel.queue.counts,
            ReviewQueueCounts(new: 0, learning: 0, review: 1)
        )
        XCTAssertTrue(focusedRepository.createCalls.isEmpty)
        XCTAssertEqual(focusedRepository.fetchCalls.count, 1)
    }

    func testFocusedLoadRejectsUnseenNewAtExhaustedQuotaAndAdmitsDueCard() {
        let unseen = makeCandidate(id: "unseen", state: .new)
        let exhaustedRepository = RepositoryFake(
            snapshots: [
                snapshot(
                    [unseen],
                    introducedPrimaryWordIDs: ["already-introduced"]
                )
            ]
        )
        let exhausted = makeViewModel(
            repository: exhaustedRepository,
            newWordsPerDay: 1
        )

        exhausted.viewModel.load(
            nativeLanguage: "en-us",
            learningLanguage: "es",
            cardID: "unseen"
        )

        XCTAssertNil(exhausted.viewModel.current)
        XCTAssertEqual(exhausted.viewModel.queue, .empty)
        XCTAssertNil(exhausted.viewModel.loadError)

        let due = makeCandidate(
            id: "due",
            state: .review,
            due: Self.studyDay.end.addingTimeInterval(-1)
        )
        let dueRepository = RepositoryFake(snapshots: [snapshot([due])])
        let admitted = makeViewModel(
            repository: dueRepository,
            newWordsPerDay: 0
        )

        admitted.viewModel.load(
            nativeLanguage: "en-us",
            learningLanguage: "es",
            cardID: "due"
        )

        XCTAssertEqual(admitted.viewModel.current?.card.id, "due")
        XCTAssertEqual(admitted.viewModel.queue.counts.review, 1)
    }

    func testFocusedReviewOpensFutureCardAndFinishesAfterOneGrade() {
        let future = Self.studyDay.end.addingTimeInterval(86_400)
        let target = makeCandidate(id: "target", state: .review, due: future)
        let unrelated = makeCandidate(id: "unrelated", state: .review)
        let candidates = snapshot([target, unrelated])
        let repository = RepositoryFake(snapshots: [candidates, candidates, candidates])
        let sut = makeViewModel(repository: repository)

        sut.viewModel.load(nativeLanguage: "en-us", learningLanguage: "es", cardID: "target")
        XCTAssertEqual(sut.viewModel.current?.card.id, "target")
        XCTAssertEqual(sut.viewModel.queue.items.map(\.card.id), ["target"])
        XCTAssertTrue(repository.applyCalls.isEmpty)
        XCTAssertTrue(repository.markCalls.isEmpty)

        sut.viewModel.revealAnswer()
        sut.viewModel.rate(.good)
        XCTAssertEqual(repository.applyCalls.count, 1)
        XCTAssertEqual(sut.viewModel.completedCount, 1)
        XCTAssertNil(sut.viewModel.current)

        sut.viewModel.refresh(nativeLanguage: "en-us", learningLanguage: "es", cardID: "target")
        XCTAssertNil(sut.viewModel.current, "A background refresh must not reopen a completed focused card.")
        XCTAssertEqual(sut.viewModel.completedCount, 1)
    }

    func testFocusedReviewOpensFutureLearningCardWithoutResettingProgress() {
        let target = makeCandidate(id: "target", state: .learning,
                                   due: Self.now.addingTimeInterval(600))
        let repository = RepositoryFake(snapshots: [snapshot([target])])
        let sut = makeViewModel(repository: repository)
        sut.viewModel.load(nativeLanguage: "en-us", learningLanguage: "es", cardID: "target")
        XCTAssertEqual(sut.viewModel.current?.card, target.card)
        XCTAssertTrue(repository.applyCalls.isEmpty)
        XCTAssertTrue(repository.markCalls.isEmpty)
    }

    func testNewCardIsMarkedIntroducedBeforePreviewAndPublication() {
        let unseen = makeCandidate(id: "new-card", state: .new)
        let repository = RepositoryFake(snapshots: [snapshot([unseen])])
        let scheduler = SchedulerFake()
        scheduler.onPreviews = { card in
            XCTAssertEqual(repository.events, ["create", "fetch", "mark:new-card"])
            XCTAssertEqual(card.introducedAt, Self.now)
        }
        let sut = makeViewModel(
            repository: repository,
            scheduler: scheduler
        )

        sut.viewModel.load(nativeLanguage: "en-us", learningLanguage: "es")

        XCTAssertEqual(repository.markCalls, [.init(cardID: "new-card", now: Self.now)])
        XCTAssertEqual(sut.viewModel.current?.card.introducedAt, Self.now)
        XCTAssertEqual(sut.viewModel.queue.items.first?.card.introducedAt, Self.now)
    }

    func testFourPreviewsUseOneCapturedNowAndImmutableSettingsSnapshot() {
        let card = makeCandidate(id: "card", state: .learning)
        let repository = RepositoryFake(snapshots: [snapshot([card])])
        let scheduler = SchedulerFake()
        let clock = Clock([Self.now, Self.now.addingTimeInterval(999)])
        let settings = SettingsFake(snapshot: Self.settingsSnapshot())
        let sut = makeViewModel(
            repository: repository,
            scheduler: scheduler,
            settings: settings,
            clock: clock
        )

        sut.viewModel.load(nativeLanguage: "en-us", learningLanguage: "es")

        XCTAssertEqual(Set(sut.viewModel.previews.keys), Set(ReviewGrade.allCases))
        XCTAssertEqual(clock.callCount, 1)
        XCTAssertEqual(settings.snapshotDates, [Self.now])
        XCTAssertEqual(scheduler.previewCalls.count, 1)
        XCTAssertEqual(scheduler.previewCalls.first?.now, Self.now)
        XCTAssertEqual(
            scheduler.previewCalls.first?.configuration,
            Self.settingsSnapshot().configuration
        )
    }

    func testRatingSchedulesOnceThenAppliesOneTransaction() {
        let card = makeCandidate(id: "card", state: .learning)
        let repository = RepositoryFake(
            snapshots: [snapshot([card]), snapshot([])],
            applyOutcomes: [.applied]
        )
        let scheduler = SchedulerFake()
        let sut = makeViewModel(
            repository: repository,
            scheduler: scheduler,
            attemptIDs: ["attempt-1"]
        )
        sut.viewModel.load(nativeLanguage: "en-us", learningLanguage: "es")
        sut.viewModel.revealAnswer()

        sut.viewModel.rate(.hard, durationMS: 321)

        XCTAssertEqual(scheduler.scheduleCalls.count, 1)
        XCTAssertEqual(scheduler.scheduleCalls.first?.grade, .hard)
        XCTAssertEqual(repository.applyCalls.count, 1)
        XCTAssertEqual(repository.applyCalls.first?.attemptID, "attempt-1")
        XCTAssertEqual(repository.applyCalls.first?.durationMS, 321)
        XCTAssertEqual(repository.applyCalls.first?.transition.grade, .hard)
        XCTAssertEqual(
            repository.applyCalls.first?.wordSnapshot,
            ReviewWordSnapshot(
                displayWord: "Word card",
                translations: ["Translation card"],
                learningLanguage: "es",
                nativeLanguage: "en-us"
            )
        )
    }

    func testAppliedAndDuplicateAdvanceAndRefreshQueue() {
        for outcome in [RepositoryFake.ApplyOutcome.applied, .duplicate] {
            let first = makeCandidate(id: "first", state: .learning)
            let second = makeCandidate(id: "second", state: .review)
            let repository = RepositoryFake(
                snapshots: [snapshot([first]), snapshot([second])],
                applyOutcomes: [outcome]
            )
            let sut = makeViewModel(repository: repository)
            sut.viewModel.load(nativeLanguage: "en-us", learningLanguage: "es")
            sut.viewModel.revealAnswer()

            sut.viewModel.rate(.good)

            XCTAssertEqual(sut.viewModel.current?.card.id, "second")
            XCTAssertEqual(sut.viewModel.completedCount, 1)
            XCTAssertFalse(sut.viewModel.showAnswer)
            XCTAssertNil(sut.viewModel.saveError)
            XCTAssertEqual(repository.fetchCalls.count, 2)
            XCTAssertEqual(repository.createCalls.count, 2)
        }
    }

    func testSaveFailurePreservesEntireVisibleReviewStateAndRevealedAnswer() {
        let card = makeCandidate(id: "card", state: .learning)
        let repository = RepositoryFake(
            snapshots: [snapshot([card])],
            applyOutcomes: [.failure]
        )
        let sut = makeViewModel(repository: repository)
        sut.viewModel.load(nativeLanguage: "en-us", learningLanguage: "es")
        sut.viewModel.revealAnswer()
        let queueBefore = sut.viewModel.queue
        let currentBefore = sut.viewModel.current
        let previewsBefore = sut.viewModel.previews
        let completedBefore = sut.viewModel.completedCount

        sut.viewModel.rate(.again)

        XCTAssertEqual(sut.viewModel.queue, queueBefore)
        XCTAssertEqual(sut.viewModel.current, currentBefore)
        XCTAssertEqual(sut.viewModel.previews, previewsBefore)
        XCTAssertEqual(sut.viewModel.completedCount, completedBefore)
        XCTAssertTrue(sut.viewModel.showAnswer)
        XCTAssertEqual(sut.viewModel.saveError, RepositoryFake.Failure.apply.localizedDescription)
    }

    func testRestartReplaysCompletedSessionFromBeginningAndFinishesOnePass() {
        let first = makeCandidate(id: "first", state: .learning)
        let second = makeCandidate(id: "second", state: .review)
        let tomorrow = Self.studyDay.end.addingTimeInterval(86_400)
        let reviewedFirst = makeCandidate(id: "first", state: .review, due: tomorrow)
        let reviewedSecond = makeCandidate(id: "second", state: .review, due: tomorrow)
        let unrelated = makeCandidate(id: "unrelated", state: .review, due: tomorrow)
        let caughtUp = snapshot([reviewedFirst, reviewedSecond, unrelated])
        let repository = RepositoryFake(snapshots: [
            snapshot([first, second]), snapshot([second]), caughtUp,
            caughtUp, caughtUp, caughtUp, caughtUp
        ])
        let sut = makeViewModel(repository: repository,
                                attemptIDs: ["first", "second", "repeat-first", "repeat-second"])
        sut.viewModel.load(nativeLanguage: "en-us", learningLanguage: "es")
        sut.viewModel.revealAnswer()
        sut.viewModel.rate(.good)
        sut.viewModel.revealAnswer()
        sut.viewModel.rate(.good)
        XCTAssertNil(sut.viewModel.current)
        XCTAssertEqual(sut.viewModel.completedCount, 2)

        sut.viewModel.restart()

        XCTAssertEqual(sut.viewModel.queue.items.map(\.card.id), ["first", "second"])
        XCTAssertEqual(sut.viewModel.current?.card, reviewedFirst.card)
        XCTAssertEqual(sut.viewModel.completedCount, 0)
        XCTAssertFalse(sut.viewModel.showAnswer)
        XCTAssertEqual(repository.applyCalls.count, 2, "Restart must not rewrite saved progress")
        sut.viewModel.revealAnswer()
        sut.viewModel.rate(.good)
        XCTAssertEqual(sut.viewModel.current?.card.id, "second")
        sut.viewModel.revealAnswer()
        sut.viewModel.rate(.good)
        XCTAssertNil(sut.viewModel.current)
        XCTAssertEqual(sut.viewModel.completedCount, 2)
        XCTAssertEqual(repository.applyCalls.map(\.attemptID), ["first", "second", "repeat-first", "repeat-second"])

        sut.viewModel.restart()
        XCTAssertEqual(sut.viewModel.current?.card.id, "first")
    }

    func testRestartCaughtUpSessionIncludesStudiedCardsButRespectsLocksSetsAndNewWordQuota() {
        let future = Self.studyDay.end.addingTimeInterval(86_400)
        let learning = makeCandidate(id: "learning", state: .learning, due: future)
        let reviewed = makeCandidate(id: "reviewed", state: .review, due: future)
        let unseen = makeCandidate(id: "unseen", state: .new)
        let locked = makeCandidate(id: "locked", state: .review, due: future, isLocked: true)
        let inactive = makeCandidate(id: "inactive", state: .review, due: future, activeSetIDs: [])
        let candidates = snapshot([learning, reviewed, unseen, locked, inactive])
        let repository = RepositoryFake(snapshots: [candidates, candidates])
        let sut = makeViewModel(repository: repository, newWordsPerDay: 0)
        sut.viewModel.load(nativeLanguage: "en-us", learningLanguage: "es")
        XCTAssertNil(sut.viewModel.current)

        sut.viewModel.restart()

        XCTAssertEqual(Set(sut.viewModel.queue.items.map(\.card.id)), ["learning", "reviewed"])
        XCTAssertEqual(sut.viewModel.queue.counts, ReviewQueueCounts(new: 0, learning: 1, review: 1))
        XCTAssertEqual(repository.createCalls.count, 1)
        XCTAssertTrue(repository.markCalls.isEmpty)
        XCTAssertTrue(repository.applyCalls.isEmpty)
    }

    func testRestartFocusedSessionKeepsOnlyRequestedCardEvenWhenNotDue() {
        let future = Self.studyDay.end.addingTimeInterval(86_400)
        let first = makeCandidate(id: "first", state: .review, due: future)
        let second = makeCandidate(id: "second", state: .review, due: future)
        let candidates = snapshot([first, second])
        let repository = RepositoryFake(snapshots: [candidates, candidates, candidates])
        let sut = makeViewModel(repository: repository)
        sut.viewModel.load(nativeLanguage: "en-us", learningLanguage: "es", cardID: "second")
        XCTAssertEqual(sut.viewModel.current?.card.id, "second")
        sut.viewModel.revealAnswer()
        sut.viewModel.rate(.good)
        XCTAssertNil(sut.viewModel.current)

        sut.viewModel.restart()

        XCTAssertEqual(sut.viewModel.queue.items.map(\.card.id), ["second"])
    }

    func testRestartSaveFailureKeepsCardAndRetriesOriginalAttempt() {
        let card = makeCandidate(id: "card", state: .review,
                                 due: Self.studyDay.end.addingTimeInterval(86_400))
        let candidates = snapshot([card])
        let repository = RepositoryFake(snapshots: [candidates, candidates, candidates],
                                        applyOutcomes: [.failure, .applied])
        let sut = makeViewModel(repository: repository)
        sut.viewModel.load(nativeLanguage: "en-us", learningLanguage: "es")
        sut.viewModel.restart()
        sut.viewModel.revealAnswer()

        sut.viewModel.rate(.hard)

        XCTAssertEqual(sut.viewModel.current?.card.id, "card")
        XCTAssertEqual(sut.viewModel.completedCount, 0)
        XCTAssertNotNil(sut.viewModel.saveError)
        sut.viewModel.rate(.good)
        XCTAssertNil(sut.viewModel.current)
        XCTAssertEqual(sut.viewModel.completedCount, 1)
        XCTAssertEqual(repository.applyCalls.map(\.attemptID), ["attempt-1", "attempt-1"])
        XCTAssertEqual(repository.applyCalls.map(\.transition.grade), [.hard, .hard])
    }

    func testRetryLoadPreservesRestartAfterReadFailure() {
        let card = makeCandidate(id: "card", state: .review,
                                 due: Self.studyDay.end.addingTimeInterval(86_400))
        let repository = RepositoryFake(snapshots: [snapshot([card]), snapshot([card])],
                                        fetchFailures: [nil, .fetch, nil])
        let sut = makeViewModel(repository: repository)
        sut.viewModel.load(nativeLanguage: "en-us", learningLanguage: "es")

        sut.viewModel.restart()
        XCTAssertNotNil(sut.viewModel.loadError)
        sut.viewModel.retryLoad()

        XCTAssertEqual(sut.viewModel.current?.card.id, "card")
        XCTAssertNil(sut.viewModel.loadError)
        XCTAssertFalse(sut.viewModel.showAnswer)
    }

    func testReplayRefreshRetryKeepsProgressAndDoesNotIncludeNewlyArrivedCards() {
        let future = Self.studyDay.end.addingTimeInterval(86_400)
        let first = makeCandidate(id: "first", state: .review, due: future)
        let second = makeCandidate(id: "second", state: .review, due: future)
        let arrived = makeCandidate(id: "arrived", state: .review, due: future)
        let original = snapshot([first, second])
        let updated = snapshot([first, second, arrived])
        let repository = RepositoryFake(
            snapshots: [original, original, updated, updated],
            fetchFailures: [nil, nil, .fetch, nil, nil]
        )
        let sut = makeViewModel(repository: repository, attemptIDs: ["first", "second"])
        sut.viewModel.load(nativeLanguage: "en-us", learningLanguage: "es")
        sut.viewModel.restart()
        sut.viewModel.revealAnswer()
        sut.viewModel.rate(.good)
        XCTAssertNotNil(sut.viewModel.loadError)
        XCTAssertEqual(sut.viewModel.completedCount, 1)

        sut.viewModel.retryLoad()

        XCTAssertNil(sut.viewModel.loadError)
        XCTAssertEqual(sut.viewModel.queue.items.map(\.card.id), ["second"])
        XCTAssertEqual(sut.viewModel.completedCount, 1)
        sut.viewModel.revealAnswer()
        sut.viewModel.rate(.good)
        XCTAssertNil(sut.viewModel.current)
        XCTAssertEqual(sut.viewModel.completedCount, 2)
        XCTAssertEqual(repository.applyCalls.count, 2)
    }

    func testBackgroundRefreshPreservesReplayCohortAndCompletedCards() {
        let future = Self.studyDay.end.addingTimeInterval(86_400)
        let first = makeCandidate(id: "first", state: .review, due: future)
        let second = makeCandidate(id: "second", state: .review, due: future)
        let arrived = makeCandidate(id: "arrived", state: .review, due: future)
        let original = snapshot([first, second])
        let updated = snapshot([first, second, arrived])
        let repository = RepositoryFake(snapshots: [original, original, original, updated, updated])
        let sut = makeViewModel(repository: repository, attemptIDs: ["first", "second"])
        sut.viewModel.load(nativeLanguage: "en-us", learningLanguage: "es")
        sut.viewModel.restart()
        sut.viewModel.revealAnswer()
        sut.viewModel.rate(.good)

        sut.viewModel.refresh(nativeLanguage: "en-us", learningLanguage: "es")

        XCTAssertEqual(sut.viewModel.queue.items.map(\.card.id), ["second"])
        XCTAssertEqual(sut.viewModel.completedCount, 1)
        sut.viewModel.revealAnswer()
        sut.viewModel.rate(.good)
        XCTAssertNil(sut.viewModel.current)
        XCTAssertEqual(sut.viewModel.completedCount, 2)
        XCTAssertEqual(repository.applyCalls.count, 2)
    }

    func testRetryReusesExactPendingAttemptWithoutSchedulingOrTakingNewSettings() {
        let card = makeCandidate(id: "card", state: .learning)
        let repository = RepositoryFake(
            snapshots: [snapshot([card]), snapshot([])],
            applyOutcomes: [.failure, .applied]
        )
        let scheduler = SchedulerFake()
        let settings = SettingsFake(snapshot: Self.settingsSnapshot())
        let sut = makeViewModel(
            repository: repository,
            scheduler: scheduler,
            settings: settings,
            attemptIDs: ["attempt-stable", "attempt-must-not-be-used"]
        )
        sut.viewModel.load(nativeLanguage: "en-us", learningLanguage: "es")
        sut.viewModel.revealAnswer()

        sut.viewModel.rate(.hard, durationMS: 400)
        settings.value = Self.settingsSnapshot(configurationVersion: 99)
        sut.viewModel.rate(.easy, durationMS: 999)

        XCTAssertEqual(repository.applyCalls.count, 2)
        XCTAssertEqual(repository.applyCalls[0], repository.applyCalls[1])
        XCTAssertEqual(repository.applyCalls[1].attemptID, "attempt-stable")
        XCTAssertEqual(repository.applyCalls[1].transition.grade, .hard)
        XCTAssertEqual(repository.applyCalls[1].durationMS, 400)
        XCTAssertEqual(repository.applyCalls[1].configuration.version, 1)
        XCTAssertEqual(scheduler.scheduleCalls.count, 1)
        XCTAssertEqual(sut.attemptIDs.callCount, 1)
        XCTAssertEqual(settings.snapshotDates, [Self.now])
        XCTAssertEqual(sut.viewModel.completedCount, 1)
    }

    func testRepeatedTapDuringApplyInvokesApplyOnlyOnce() {
        let card = makeCandidate(id: "card", state: .learning)
        let repository = RepositoryFake(
            snapshots: [snapshot([card]), snapshot([])],
            applyOutcomes: [.applied]
        )
        let sut = makeViewModel(repository: repository)
        sut.viewModel.load(nativeLanguage: "en-us", learningLanguage: "es")
        sut.viewModel.revealAnswer()
        repository.duringApply = { [weak viewModel = sut.viewModel] in
            viewModel?.rate(.easy)
        }

        sut.viewModel.rate(.good)

        XCTAssertEqual(repository.applyCalls.count, 1)
        XCTAssertEqual(sut.scheduler.scheduleCalls.count, 1)
        XCTAssertEqual(sut.viewModel.completedCount, 1)
    }

    func testReentrantLoadDuringSuccessfulApplyDiscardsEveryOldSessionMutation() {
        assertReentrantLoadDuringApply(outcome: .applied)
    }

    func testReentrantLoadDuringFailedApplyDiscardsEveryOldSessionMutation() {
        assertReentrantLoadDuringApply(outcome: .failure)
    }

    func testAppliedAndDuplicateRefreshFailuresAreTerminalButExplicitLoadRecovers() {
        struct Case {
            let name: String
            let outcome: RepositoryFake.ApplyOutcome
            let createFailures: [RepositoryFake.Failure?]
            let fetchFailures: [RepositoryFake.Failure?]
            let expectedError: RepositoryFake.Failure
        }
        let cases = [
            Case(
                name: "applied-create",
                outcome: .applied,
                createFailures: [nil, .create],
                fetchFailures: [],
                expectedError: .create
            ),
            Case(
                name: "duplicate-fetch",
                outcome: .duplicate,
                createFailures: [],
                fetchFailures: [nil, .fetch],
                expectedError: .fetch
            ),
        ]

        for testCase in cases {
            let first = makeCandidate(id: "first", state: .learning)
            let recovered = makeCandidate(id: "recovered", state: .review)
            let repository = RepositoryFake(
                snapshots: [snapshot([first]), snapshot([recovered]), snapshot([])],
                applyOutcomes: [testCase.outcome, .applied],
                createFailures: testCase.createFailures,
                fetchFailures: testCase.fetchFailures
            )
            let settings = SettingsFake(snapshot: Self.settingsSnapshot())
            let sut = makeViewModel(
                repository: repository,
                settings: settings,
                attemptIDs: ["attempt-old", "attempt-recovered"]
            )
            sut.viewModel.load(nativeLanguage: "en-us", learningLanguage: "es")
            sut.viewModel.revealAnswer()
            settings.value = Self.settingsSnapshot(configurationVersion: 99)

            sut.viewModel.rate(.good)

            XCTAssertEqual(repository.applyCalls.count, 1, testCase.name)
            XCTAssertEqual(sut.viewModel.completedCount, 1, testCase.name)
            XCTAssertEqual(sut.viewModel.queue, .empty, testCase.name)
            XCTAssertNil(sut.viewModel.current, testCase.name)
            XCTAssertTrue(sut.viewModel.previews.isEmpty, testCase.name)
            XCTAssertFalse(sut.viewModel.showAnswer, testCase.name)
            XCTAssertEqual(
                sut.viewModel.loadError,
                testCase.expectedError.localizedDescription,
                testCase.name
            )
            XCTAssertNil(sut.viewModel.saveError, testCase.name)
            XCTAssertEqual(settings.snapshotDates, [Self.now], testCase.name)
            XCTAssertEqual(
                repository.applyCalls.first?.configuration.version,
                1,
                testCase.name
            )

            sut.viewModel.revealAnswer()
            sut.viewModel.rate(.easy)

            XCTAssertEqual(repository.applyCalls.count, 1, testCase.name)
            XCTAssertEqual(sut.scheduler.scheduleCalls.count, 1, testCase.name)
            XCTAssertEqual(sut.attemptIDs.callCount, 1, testCase.name)

            sut.viewModel.load(nativeLanguage: "en-us", learningLanguage: "es")

            XCTAssertEqual(sut.viewModel.current?.card.id, "recovered", testCase.name)
            XCTAssertEqual(sut.viewModel.completedCount, 0, testCase.name)
            XCTAssertNil(sut.viewModel.loadError, testCase.name)
            XCTAssertNil(sut.viewModel.saveError, testCase.name)
            XCTAssertEqual(settings.snapshotDates.count, 2, testCase.name)
            XCTAssertEqual(
                sut.scheduler.previewCalls.last?.configuration.version,
                99,
                testCase.name
            )

            sut.viewModel.revealAnswer()
            sut.viewModel.rate(.hard)

            XCTAssertEqual(repository.applyCalls.count, 2, testCase.name)
            XCTAssertEqual(
                repository.applyCalls.last?.attemptID,
                "attempt-recovered",
                testCase.name
            )
            XCTAssertEqual(
                repository.applyCalls.last?.configuration.version,
                99,
                testCase.name
            )
        }
    }

    func testReentrantLoadDuringGlobalCreationCancelsStaleFetchAndPublication() {
        let reloaded = makeCandidate(id: "reloaded", state: .review)
        let staleFetch = makeCandidate(id: "stale-fetch", state: .review)
        let repository = RepositoryFake(
            snapshots: [snapshot([reloaded]), snapshot([staleFetch])]
        )
        let sut = makeViewModel(repository: repository)
        var reloadedQueue: ReviewQueue?
        var reloadedPreviews: [ReviewGrade: ReviewTransition]?
        repository.duringCreate = { [weak viewModel = sut.viewModel] in
            repository.duringCreate = nil
            viewModel?.load(nativeLanguage: "en-us", learningLanguage: "es")
            reloadedQueue = viewModel?.queue
            reloadedPreviews = viewModel?.previews
        }

        sut.viewModel.load(nativeLanguage: "en-us", learningLanguage: "es")

        XCTAssertEqual(sut.viewModel.current?.card.id, "reloaded")
        XCTAssertEqual(sut.viewModel.queue, reloadedQueue)
        XCTAssertEqual(sut.viewModel.previews, reloadedPreviews)
        XCTAssertEqual(sut.viewModel.previews.count, 4)
        XCTAssertEqual(sut.viewModel.completedCount, 0)
        XCTAssertNil(sut.viewModel.loadError)
        XCTAssertNil(sut.viewModel.saveError)
        XCTAssertEqual(repository.createCalls.count, 2)
        XCTAssertEqual(repository.fetchCalls.count, 1)
        XCTAssertEqual(repository.remainingSnapshotCount, 1)
    }

    func testSideSwapIsDisplayOnlyAndDoesNotChangeSchedulingOrProgress() {
        let card = makeCandidate(id: "card", state: .learning)
        let repository = RepositoryFake(snapshots: [snapshot([card])])
        let sut = makeViewModel(repository: repository)
        sut.viewModel.load(nativeLanguage: "en-us", learningLanguage: "es")
        let queueBefore = sut.viewModel.queue
        let currentBefore = sut.viewModel.current
        let previewsBefore = sut.viewModel.previews
        let previewCallCount = sut.scheduler.previewCalls.count

        sut.viewModel.swapReviewSide()

        XCTAssertTrue(sut.viewModel.sessionFlip)
        XCTAssertEqual(sut.viewModel.queue, queueBefore)
        XCTAssertEqual(sut.viewModel.current, currentBefore)
        XCTAssertEqual(sut.viewModel.previews, previewsBefore)
        XCTAssertEqual(sut.viewModel.completedCount, 0)
        XCTAssertEqual(sut.scheduler.previewCalls.count, previewCallCount)
        XCTAssertTrue(sut.scheduler.scheduleCalls.isEmpty)
        XCTAssertTrue(repository.applyCalls.isEmpty)
    }

    func testLoadFailurePublishesErrorWithNoPartialCardState() {
        let unseen = makeCandidate(id: "new-card", state: .new)
        let repository = RepositoryFake(
            snapshots: [snapshot([unseen])],
            markFailure: .mark
        )
        let sut = makeViewModel(repository: repository)

        sut.viewModel.load(nativeLanguage: "en-us", learningLanguage: "es")

        XCTAssertEqual(sut.viewModel.loadError, RepositoryFake.Failure.mark.localizedDescription)
        XCTAssertEqual(sut.viewModel.queue, .empty)
        XCTAssertNil(sut.viewModel.current)
        XCTAssertTrue(sut.viewModel.previews.isEmpty)
        XCTAssertFalse(sut.viewModel.showAnswer)
    }

    private func assertReentrantLoadDuringApply(
        outcome: RepositoryFake.ApplyOutcome,
        file: StaticString = #filePath,
        line: UInt = #line
    ) {
        let old = makeCandidate(id: "old", state: .learning)
        let reloaded = makeCandidate(id: "reloaded", state: .review)
        let staleRefresh = makeCandidate(id: "stale-refresh", state: .review)
        let repository = RepositoryFake(
            snapshots: [
                snapshot([old]),
                snapshot([reloaded]),
                snapshot([staleRefresh]),
            ],
            applyOutcomes: [outcome]
        )
        let sut = makeViewModel(
            repository: repository,
            attemptIDs: ["attempt-old", "attempt-must-not-be-used"]
        )
        sut.viewModel.load(nativeLanguage: "en-us", learningLanguage: "es")
        sut.viewModel.revealAnswer()
        var isSubmittingPublications: [Bool] = []
        let isSubmittingObservation = sut.viewModel.$isSubmitting
            .dropFirst()
            .sink { isSubmittingPublications.append($0) }
        defer { isSubmittingObservation.cancel() }
        var reloadedQueue: ReviewQueue?
        var reloadedCurrent: ReviewQueueCandidate?
        var reloadedPreviews: [ReviewGrade: ReviewTransition]?

        repository.duringApply = { [weak viewModel = sut.viewModel] in
            repository.duringApply = nil
            viewModel?.load(nativeLanguage: "en-us", learningLanguage: "es")
            reloadedQueue = viewModel?.queue
            reloadedCurrent = viewModel?.current
            reloadedPreviews = viewModel?.previews
            viewModel?.revealAnswer()
            viewModel?.rate(.easy)
        }

        sut.viewModel.rate(.good)

        XCTAssertEqual(sut.viewModel.queue, reloadedQueue, file: file, line: line)
        XCTAssertEqual(sut.viewModel.current, reloadedCurrent, file: file, line: line)
        XCTAssertEqual(sut.viewModel.current?.card.id, "reloaded", file: file, line: line)
        XCTAssertEqual(sut.viewModel.previews, reloadedPreviews, file: file, line: line)
        XCTAssertEqual(sut.viewModel.completedCount, 0, file: file, line: line)
        XCTAssertTrue(sut.viewModel.showAnswer, file: file, line: line)
        XCTAssertFalse(sut.viewModel.isSubmitting, file: file, line: line)
        XCTAssertNil(sut.viewModel.loadError, file: file, line: line)
        XCTAssertNil(sut.viewModel.saveError, file: file, line: line)
        XCTAssertEqual(repository.fetchCalls.count, 2, file: file, line: line)
        XCTAssertEqual(repository.applyCalls.count, 1, file: file, line: line)
        XCTAssertEqual(sut.scheduler.scheduleCalls.count, 1, file: file, line: line)
        XCTAssertEqual(sut.attemptIDs.callCount, 1, file: file, line: line)
        XCTAssertEqual(
            isSubmittingPublications,
            [true, false],
            file: file,
            line: line
        )

        sut.viewModel.rate(.hard)

        XCTAssertEqual(repository.applyCalls.count, 2, file: file, line: line)
        XCTAssertEqual(
            repository.applyCalls.last?.attemptID,
            "attempt-must-not-be-used",
            file: file,
            line: line
        )
        XCTAssertEqual(sut.scheduler.scheduleCalls.count, 2, file: file, line: line)
        XCTAssertEqual(sut.attemptIDs.callCount, 2, file: file, line: line)
        XCTAssertEqual(
            isSubmittingPublications,
            [true, false, true, false],
            file: file,
            line: line
        )
    }

    private func makeViewModel(
        repository: RepositoryFake,
        scheduler: SchedulerFake = SchedulerFake(),
        settings: SettingsFake? = nil,
        clock: Clock? = nil,
        attemptIDs: [String] = ["attempt-1"],
        newWordsPerDay: Int = 5
    ) -> (
        viewModel: ReviewSessionViewModel,
        scheduler: SchedulerFake,
        settings: SettingsFake,
        clock: Clock,
        attemptIDs: AttemptIDSequence
    ) {
        let resolvedSettings = settings ?? SettingsFake(
            snapshot: Self.settingsSnapshot(newWordsPerDay: newWordsPerDay)
        )
        let resolvedClock = clock ?? Clock(Array(repeating: Self.now, count: 20))
        let ids = AttemptIDSequence(attemptIDs)
        let dependencies = ReviewSessionDependencies(
            repository: repository,
            scheduler: scheduler,
            queue: ReviewQueueService(repository: repository),
            settings: resolvedSettings,
            now: { resolvedClock.next() },
            makeAttemptID: { ids.next() }
        )
        return (
            ReviewSessionViewModel(dependencies: dependencies),
            scheduler,
            resolvedSettings,
            resolvedClock,
            ids
        )
    }

    private static func settingsSnapshot(
        newWordsPerDay: Int = 5,
        configurationVersion: Int = 1
    ) -> ReviewSettingsSnapshot {
        let configuration = FSRSConfiguration(
            version: configurationVersion,
            weights: FSRSConfiguration.initial.weights,
            desiredRetention: 0.9,
            maximumInterval: 36_500,
            learningSteps: ["1m", "10m"],
            relearningSteps: ["10m"],
            enableFuzz: false,
            enableShortTerm: true,
            newWordsPerDaySnapshot: newWordsPerDay,
            reviewLimit: 0,
            primaryDirection: .learningToNative,
            packageRevision: FSRSConfiguration.initial.packageRevision
        )
        return ReviewSettingsSnapshot(
            configuration: configuration,
            newWordsPerDay: newWordsPerDay,
            primaryDirection: .learningToNative,
            studyDay: studyDay
        )
    }

    private func snapshot(
        _ candidates: [ReviewQueueCandidate],
        introducedPrimaryWordIDs: Set<String> = [],
        introducedReverseCount: Int = 0
    ) -> ReviewCandidateSnapshot {
        ReviewCandidateSnapshot(
            candidates: candidates,
            introducedPrimaryWordIDs: introducedPrimaryWordIDs,
            introducedReverseCount: introducedReverseCount
        )
    }

    private func makeCandidate(
        id: String,
        state: ReviewCardState,
        due: Date? = nil,
        direction: ReviewDirection = .learningToNative,
        introducedAt: Date? = nil,
        activeSetIDs: [String] = ["set-a"],
        isLocked: Bool = false
    ) -> ReviewQueueCandidate {
        let wordID = "word-\(id)"
        let resolvedDue = due ?? Self.now
        let card = ReviewCardRecord(
            id: id,
            wordID: wordID,
            direction: direction,
            state: state,
            due: resolvedDue,
            stability: 1,
            difficulty: 5,
            elapsedDays: 1,
            scheduledDays: 1,
            learningStep: 0,
            reps: 1,
            lapses: 0,
            lastReview: Self.now.addingTimeInterval(-86_400),
            introducedAt: introducedAt,
            sourceTemplateID: nil,
            rowVersion: 3,
            createdAt: Self.dayStart.addingTimeInterval(-86_400),
            updatedAt: Self.dayStart
        )
        let word = WordDashboardRow(
            id: wordID,
            normalizedWord: id,
            displayWord: "Word \(id)",
            translation: "Translation \(id)",
            translations: [],
            pronunciation: nil,
            partOfSpeech: nil,
            learningLanguageBcp47: "es",
            nativeLanguageBcp47: "en-us",
            flashcardSetIDs: ["set-a"],
            flashcardSetLabel: "Set A",
            exampleLines: [],
            userNotes: nil
        )
        return ReviewQueueCandidate(
            card: card,
            word: word,
            activeSetIDs: activeSetIDs,
            isLocked: isLocked
        )
    }
}

private final class SettingsFake: ReviewSettingsProviding {
    enum Failure: LocalizedError {
        case snapshot

        var errorDescription: String? { "settings failed" }
    }

    var value: ReviewSettingsSnapshot
    var failure: Failure?
    private(set) var snapshotDates: [Date] = []

    init(snapshot: ReviewSettingsSnapshot) {
        value = snapshot
    }

    func snapshot(now: Date) throws -> ReviewSettingsSnapshot {
        snapshotDates.append(now)
        if let failure {
            throw failure
        }
        return value
    }
}

private final class SchedulerFake: ReviewScheduling {
    struct PreviewCall: Equatable {
        let card: ReviewCardRecord
        let now: Date
        let configuration: FSRSConfiguration
    }

    struct ScheduleCall: Equatable {
        let card: ReviewCardRecord
        let grade: ReviewGrade
        let now: Date
        let configuration: FSRSConfiguration
    }

    var onPreviews: ((ReviewCardRecord) throws -> Void)?
    private(set) var previewCalls: [PreviewCall] = []
    private(set) var scheduleCalls: [ScheduleCall] = []

    func previews(
        for card: ReviewCardRecord,
        now: Date,
        configuration: FSRSConfiguration
    ) throws -> [ReviewGrade: ReviewTransition] {
        previewCalls.append(
            PreviewCall(card: card, now: now, configuration: configuration)
        )
        try onPreviews?(card)
        return Dictionary(
            uniqueKeysWithValues: ReviewGrade.allCases.map {
                ($0, transition(card: card, grade: $0, now: now))
            }
        )
    }

    func schedule(
        card: ReviewCardRecord,
        grade: ReviewGrade,
        now: Date,
        configuration: FSRSConfiguration
    ) throws -> ReviewTransition {
        scheduleCalls.append(
            ScheduleCall(
                card: card,
                grade: grade,
                now: now,
                configuration: configuration
            )
        )
        return transition(card: card, grade: grade, now: now)
    }

    private func transition(
        card: ReviewCardRecord,
        grade: ReviewGrade,
        now: Date
    ) -> ReviewTransition {
        let previous = ReviewCardSnapshot(
            state: card.state,
            due: card.due,
            stability: card.stability,
            difficulty: card.difficulty,
            elapsedDays: card.elapsedDays,
            scheduledDays: card.scheduledDays,
            learningStep: card.learningStep,
            reps: card.reps,
            lapses: card.lapses,
            lastReview: card.lastReview,
            rowVersion: card.rowVersion
        )
        var next = card
        next.state = .review
        next.due = now.addingTimeInterval(Double(grade.rawValue) * 86_400)
        next.reps += 1
        next.lastReview = now
        next.updatedAt = now
        return ReviewTransition(
            grade: grade,
            reviewedAt: now,
            previous: previous,
            next: next,
            lastElapsedDays: card.elapsedDays
        )
    }
}

private final class RepositoryFake: ReviewSessionRepository {
    enum Failure: LocalizedError {
        case mark
        case apply
        case create
        case fetch

        var errorDescription: String? {
            switch self {
            case .mark: "mark failed"
            case .apply: "apply failed"
            case .create: "create failed"
            case .fetch: "fetch failed"
            }
        }
    }

    enum ApplyOutcome {
        case applied
        case duplicate
        case failure
    }

    struct CreateCall: Equatable {
        let nativeLanguage: String
        let learningLanguage: String
        let primaryDirection: ReviewDirection
        let creationLimit: Int
        let studyDay: StudyDay
        let now: Date
    }

    struct FetchCall: Equatable {
        let nativeLanguage: String
        let learningLanguage: String
        let studyDay: StudyDay
        let primaryDirection: ReviewDirection
    }

    struct MarkCall: Equatable {
        let cardID: String
        let now: Date
    }

    struct ApplyCall: Equatable {
        let transition: ReviewTransition
        let attemptID: String
        let durationMS: Int?
        let wordSnapshot: ReviewWordSnapshot
        let configuration: FSRSConfiguration
    }

    private var snapshots: [ReviewCandidateSnapshot]
    private var applyOutcomes: [ApplyOutcome]
    private var createFailures: [Failure?]
    private var fetchFailures: [Failure?]
    let markFailure: Failure?
    var duringCreate: (() -> Void)?
    var duringApply: (() -> Void)?
    private(set) var createCalls: [CreateCall] = []
    private(set) var fetchCalls: [FetchCall] = []
    private(set) var markCalls: [MarkCall] = []
    private(set) var applyCalls: [ApplyCall] = []
    private(set) var events: [String] = []
    var remainingSnapshotCount: Int { snapshots.count }

    init(
        snapshots: [ReviewCandidateSnapshot],
        applyOutcomes: [ApplyOutcome] = [],
        createFailures: [Failure?] = [],
        fetchFailures: [Failure?] = [],
        markFailure: Failure? = nil
    ) {
        self.snapshots = snapshots
        self.applyOutcomes = applyOutcomes
        self.createFailures = createFailures
        self.fetchFailures = fetchFailures
        self.markFailure = markFailure
    }

    func createMissingReverseCards(
        nativeLanguage: String,
        learningLanguage: String,
        primaryDirection: ReviewDirection,
        creationLimit: Int,
        studyDay: StudyDay,
        now: Date
    ) throws {
        createCalls.append(
            CreateCall(
                nativeLanguage: nativeLanguage,
                learningLanguage: learningLanguage,
                primaryDirection: primaryDirection,
                creationLimit: creationLimit,
                studyDay: studyDay,
                now: now
            )
        )
        events.append("create")
        if !createFailures.isEmpty, let failure = createFailures.removeFirst() {
            throw failure
        }
        duringCreate?()
    }

    func fetchCandidateSnapshot(
        nativeLanguage: String,
        learningLanguage: String,
        studyDay: StudyDay,
        primaryDirection: ReviewDirection
    ) throws -> ReviewCandidateSnapshot {
        fetchCalls.append(
            FetchCall(
                nativeLanguage: nativeLanguage,
                learningLanguage: learningLanguage,
                studyDay: studyDay,
                primaryDirection: primaryDirection
            )
        )
        events.append("fetch")
        if !fetchFailures.isEmpty, let failure = fetchFailures.removeFirst() {
            throw failure
        }
        guard !snapshots.isEmpty else {
            return ReviewCandidateSnapshot(
                candidates: [],
                introducedPrimaryWordIDs: [],
                introducedReverseCount: 0
            )
        }
        let snapshot = snapshots.removeFirst()
        for candidate in snapshot.candidates {
            fetchedCards[candidate.card.id] = candidate.card
        }
        return snapshot
    }

    func activeFSRSConfiguration() throws -> FSRSConfiguration {
        .initial
    }

    func synchronizeFSRSConfiguration(
        newWordsPerDay: Int,
        primaryDirection: ReviewDirection,
        now: Date
    ) throws -> FSRSConfiguration {
        .initial
    }

    func markIntroducedIfNeeded(
        cardID: String,
        now: Date
    ) throws -> ReviewCardRecord {
        markCalls.append(MarkCall(cardID: cardID, now: now))
        events.append("mark:\(cardID)")
        if let markFailure {
            throw markFailure
        }
        var introduced = lastFetchedCard(cardID: cardID)
        introduced.introducedAt = introduced.introducedAt ?? now
        introduced.updatedAt = now
        return introduced
    }

    func apply(
        transition: ReviewTransition,
        attemptID: String,
        durationMS: Int?,
        wordSnapshot: ReviewWordSnapshot,
        configuration: FSRSConfiguration
    ) throws -> ApplyReviewResult {
        applyCalls.append(
            ApplyCall(
                transition: transition,
                attemptID: attemptID,
                durationMS: durationMS,
                wordSnapshot: wordSnapshot,
                configuration: configuration
            )
        )
        duringApply?()
        let outcome = applyOutcomes.isEmpty ? .applied : applyOutcomes.removeFirst()
        if case .failure = outcome {
            throw Failure.apply
        }
        let persisted = PersistedReviewResult(
            attemptID: attemptID,
            reviewCardIDSnapshot: transition.next.id,
            wordIDSnapshot: transition.next.wordID,
            direction: transition.next.direction,
            next: ReviewCardSnapshot(
                state: transition.next.state,
                due: transition.next.due,
                stability: transition.next.stability,
                difficulty: transition.next.difficulty,
                elapsedDays: transition.next.elapsedDays,
                scheduledDays: transition.next.scheduledDays,
                learningStep: transition.next.learningStep,
                reps: transition.next.reps,
                lapses: transition.next.lapses,
                lastReview: transition.next.lastReview,
                rowVersion: transition.previous.rowVersion + 1
            ),
            reviewedAt: transition.reviewedAt,
            rating: transition.grade
        )
        switch outcome {
        case .applied:
            return .applied(persisted)
        case .duplicate:
            return .duplicate(persisted)
        case .failure:
            fatalError("Handled above")
        }
    }

    private var fetchedCards: [String: ReviewCardRecord] = [:]

    private func lastFetchedCard(cardID: String) -> ReviewCardRecord {
        if let fetched = fetchedCards[cardID] {
            return fetched
        }
        fatalError("Missing fetched card \(cardID)")
    }
}

private final class Clock {
    private var dates: [Date]
    private(set) var callCount = 0

    init(_ dates: [Date]) {
        self.dates = dates
    }

    func next() -> Date {
        callCount += 1
        guard !dates.isEmpty else {
            fatalError("Clock exhausted")
        }
        return dates.removeFirst()
    }
}

private final class AttemptIDSequence {
    private var values: [String]
    private(set) var callCount = 0

    init(_ values: [String]) {
        self.values = values
    }

    func next() -> String {
        callCount += 1
        guard !values.isEmpty else {
            fatalError("Attempt IDs exhausted")
        }
        return values.removeFirst()
    }
}


@MainActor
final class SpellingPracticeTests: XCTestCase {
    func testComparisonIgnoresCaseAndWhitespaceButRequiresCorrectLettersAndAccents() {
        for (answer, expected, correct) in [
            (" CAR ", "Car", true), ("ice   cream", "ice cream", true),
            ("cafe\u{301}", "café", true), ("cafe", "café", false),
            ("машина", "Машина", true), ("машына", "машина", false),
            ("dont", "don't", false), (" ", "car", false), ("", "", false)
        ] {
            XCTAssertEqual(SpellingPractice.matches(answer: answer, expected: expected), correct,
                           "Answer: \(answer), expected: \(expected)")
        }
    }

    func testCheckingIsExplicitAndEditingClearsFeedback() {
        var practice = SpellingPractice()
        practice.answer = "carr"
        XCTAssertNil(practice.isCorrect)
        practice.check(expected: "car")
        XCTAssertEqual(practice.isCorrect, false)
        practice.answer = "car"
        XCTAssertNil(practice.isCorrect)
        practice.check(expected: "car")
        XCTAssertEqual(practice.isCorrect, true)
        practice.answer = " "
        practice.check(expected: "car")
        XCTAssertNil(practice.isCorrect)
    }

    func testSpellingInputRendersCorrectAndIncorrectInBothThemes() async throws {
        let suite = "SpellingRender.\(UUID())"
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        let settings = AppSettingsStore(defaults: defaults)
        for dark in [false, true] {
            settings.syncColorScheme(dark ? .dark : .light)
            for answer in ["carr", "car"] {
                var practice = SpellingPractice()
                practice.answer = answer
                practice.check(expected: "car")
                let host = UIHostingController(rootView:
                    SpellingPracticeInput(expectedWord: "car", practice: .constant(practice), answerRevealed: false)
                        .environmentObject(settings)
                        .environment(\.colorScheme, dark ? .dark : .light)
                        .padding(16).frame(width: 340).background(settings.currentBackground)
                )
                let scene = try XCTUnwrap(UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }.first)
                let previousKeyWindow = scene.windows.first(where: \.isKeyWindow)
                let window = UIWindow(windowScene: scene)
                window.frame = CGRect(x: 0, y: 0, width: 340, height: 240)
                window.rootViewController = host
                window.makeKeyAndVisible()
                defer { window.isHidden = true; previousKeyWindow?.makeKey() }
                host.view.frame = window.bounds
                host.view.setNeedsLayout()
                host.view.layoutIfNeeded()
                try await Task.sleep(nanoseconds: 100_000_000)
                // UIKit-backed TextField cannot be captured by SwiftUI ImageRenderer.
                let image = UIGraphicsImageRenderer(size: window.bounds.size).image { _ in
                    XCTAssertTrue(host.view.drawHierarchy(in: host.view.bounds, afterScreenUpdates: true))
                }
                let attachment = XCTAttachment(image: image)
                attachment.name = "spelling-\(dark ? "dark" : "light")-\(answer)"
                attachment.lifetime = .keepAlways
                add(attachment)
            }
        }
    }

    func testRememberedModeDoesNotChangeReviewDirection() {
        let suite = "SpellingPracticeTests.\(UUID())"
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        let settings = AppSettingsStore(defaults: defaults)
        XCTAssertFalse(settings.spellingPracticeEnabled)
        settings.reviewDirection = .learningToNative
        settings.spellingPracticeEnabled = true
        let restored = AppSettingsStore(defaults: defaults)
        XCTAssertTrue(restored.spellingPracticeEnabled)
        XCTAssertEqual(restored.reviewDirection, .learningToNative)
        restored.spellingPracticeEnabled = false
        XCTAssertFalse(AppSettingsStore(defaults: defaults).spellingPracticeEnabled)
    }
}

final class EnglishReviewVerbFormsTests: XCTestCase {
    func testEnglishDoShowsLowercasePastAndParticiple() {
        let forms = EnglishIrregularVerbForms.reviewForms(for: " Do ", language: "en-GB", partOfSpeech: "verb")
        XCTAssertEqual(forms.map(\.label), ["V2", "V3"])
        XCTAssertEqual(forms.map(\.word), ["did", "done"])
    }

    func testSameSpellingIsKeptForBothFormsAndBeIncludesBothPastForms() {
        XCTAssertEqual(EnglishIrregularVerbForms.reviewForms(for: "put", language: "en").map(\.word), ["put", "put"])
        XCTAssertEqual(EnglishIrregularVerbForms.reviewForms(for: "Be", language: "en_US").map(\.word), ["was / were", "been"])
    }

    func testOtherLanguagesKnownNounsAndUnknownWordsHaveNoGuessedForms() {
        XCTAssertTrue(EnglishIrregularVerbForms.reviewForms(for: "do", language: "es").isEmpty)
        XCTAssertTrue(EnglishIrregularVerbForms.reviewForms(for: "break", language: "en", partOfSpeech: "noun").isEmpty)
        XCTAssertTrue(EnglishIrregularVerbForms.reviewForms(for: "car", language: "en").isEmpty)
    }

    func testExistingAddWordFormsAreUnchanged() {
        XCTAssertEqual(EnglishIrregularVerbForms.otherForms(for: "Do")?.map(\.word), ["Did", "Done"])
    }
}
