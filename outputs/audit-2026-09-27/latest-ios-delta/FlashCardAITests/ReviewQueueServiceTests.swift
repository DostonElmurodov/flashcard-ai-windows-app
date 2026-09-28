import XCTest
import UserNotifications
@testable import FlashCardAI

final class ReviewQueueServiceTests: XCTestCase {
    private struct NotificationStudySettings: Equatable {
        let remindersEnabled: Bool
        let reminderMinutesFromMidnight: [Int]
        let reminderFromMinutes: Int
        let notificationCountPerDay: Int
        let nativeLanguageCode: String
        let learningLanguageCode: String
        let newWordsPerDay: Int
        let reviewDirection: ReviewDirection
    }

    private static let dayStart = Date(timeIntervalSince1970: 2_000_000_000)
    private static let dayEnd = dayStart.addingTimeInterval(86_400)
    private static let now = dayStart.addingTimeInterval(43_200)
    private static let studyDay = StudyDay(start: dayStart, end: dayEnd)

    @MainActor
    func testServerTestModeIncludesNewWordsAfterAllowanceAndFalseRestoresLimit() throws {
        let suite = "ReviewTestMode.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        let settings = AppSettingsStore(defaults: defaults)
        settings.newWordsPerDay = 1
        let store = FSRSSettingsStore(settings: settings, configurationProvider: StaticFSRSConfigurationProvider())
        let flags = ServerFeatureFlags.shared
        defer { flags.disable() }
        let repository = RepositoryStub(snapshot: snapshot([
            makeCandidate(id: "swap", state: .new),
            makeCandidate(id: "other-word", state: .new),
            makeCandidate(id: "reverse", direction: .nativeToLearning, state: .new),
            makeCandidate(id: "inactive", state: .new, activeSetIDs: []),
            makeCandidate(id: "future", state: .review, due: Self.dayEnd.addingTimeInterval(86_400))
        ], introducedPrimaryWordIDs: ["already-studied"], introducedReverseCount: 1))
        let service = ReviewQueueService(repository: repository)

        try flags.apply(responseData: Data(#"{"test_mode":true}"#.utf8))
        let testRequest = request(newWordsPerDay: try store.snapshot(now: Self.now).newWordsPerDay)
        XCTAssertEqual(Set(try service.build(testRequest).items.map(\.card.id)), ["swap", "other-word", "reverse"])
        XCTAssertEqual(try service.project(testRequest).queue.counts.new, 3)
        XCTAssertEqual(try service.focused(FocusedReviewRequest(cardID: "swap", queueRequest: testRequest))?.card.id, "swap")

        try flags.apply(responseData: Data(#"{"test_mode":false}"#.utf8))
        let normalRequest = request(newWordsPerDay: try store.snapshot(now: Self.now).newWordsPerDay)
        XCTAssertTrue(try service.build(normalRequest).items.isEmpty)
        XCTAssertEqual(try service.project(normalRequest).queue.counts.new, 0)
        XCTAssertNil(try service.focused(FocusedReviewRequest(cardID: "swap", queueRequest: normalRequest)))
        XCTAssertEqual(settings.newWordsPerDay, 1)
    }

    func testBuildUsesStrictPartitionsAndHalfOpenStudyDay() throws {
        let candidates = [
            makeCandidate(
                id: "learning-due",
                state: .learning,
                due: Self.now
            ),
            makeCandidate(
                id: "relearning-due",
                state: .relearning,
                due: Self.now.addingTimeInterval(-2)
            ),
            makeCandidate(
                id: "learning-future",
                state: .learning,
                due: Self.now.addingTimeInterval(1)
            ),
            makeCandidate(
                id: "review-overdue",
                state: .review,
                due: Self.dayStart.addingTimeInterval(-1)
            ),
            makeCandidate(
                id: "review-at-start",
                state: .review,
                due: Self.dayStart
            ),
            makeCandidate(
                id: "review-before-end",
                state: .review,
                due: Self.dayEnd.addingTimeInterval(-1)
            ),
            makeCandidate(
                id: "review-at-end",
                state: .review,
                due: Self.dayEnd
            ),
            makeCandidate(
                id: "new-primary",
                direction: .learningToNative,
                state: .new,
                createdOffset: 1
            ),
            makeCandidate(
                id: "new-reverse",
                direction: .nativeToLearning,
                state: .new,
                createdOffset: 2
            ),
        ]
        let repository = RepositoryStub(snapshot: snapshot(candidates))

        let queue = try ReviewQueueService(repository: repository).build(
            request(newWordsPerDay: 5)
        )

        XCTAssertEqual(
            queue.items.map(\.card.id),
            [
                "relearning-due",
                "learning-due",
                "review-overdue",
                "review-at-start",
                "review-before-end",
                "new-primary",
                "new-reverse",
            ]
        )
        XCTAssertEqual(queue.counts, ReviewQueueCounts(new: 2, learning: 2, review: 3))
    }

    func testBuildDeduplicatesCardIdentityAndCarriesSortedActiveSetUnion() throws {
        let first = makeCandidate(
            id: "card",
            wordID: "word",
            direction: .learningToNative,
            state: .learning,
            activeSetIDs: ["set-c", "set-a"]
        )
        let duplicateIdentity = makeCandidate(
            id: "card-duplicate",
            wordID: "word",
            direction: .learningToNative,
            state: .learning,
            activeSetIDs: ["set-b", "set-a"]
        )
        let repository = RepositoryStub(snapshot: snapshot([duplicateIdentity, first]))

        let queue = try ReviewQueueService(repository: repository).build(request())

        XCTAssertEqual(queue.items.map(\.card.id), ["card"])
        XCTAssertEqual(queue.items.first?.activeSetIDs, ["set-a", "set-b", "set-c"])
    }

    func testBuildExcludesInactiveOnlyAndLockedCandidates() throws {
        struct Case {
            let name: String
            let candidate: ReviewQueueCandidate
            let isIncluded: Bool
        }
        let cases = [
            Case(
                name: "active",
                candidate: makeCandidate(id: "active", state: .learning),
                isIncluded: true
            ),
            Case(
                name: "inactive-only",
                candidate: makeCandidate(
                    id: "inactive-only",
                    state: .learning,
                    activeSetIDs: []
                ),
                isIncluded: false
            ),
            Case(
                name: "locked",
                candidate: makeCandidate(id: "locked", state: .learning, isLocked: true),
                isIncluded: false
            ),
        ]

        for testCase in cases {
            let repository = RepositoryStub(snapshot: snapshot([testCase.candidate]))
            let queue = try ReviewQueueService(repository: repository).build(request())
            XCTAssertEqual(
                !queue.items.isEmpty,
                testCase.isIncluded,
                testCase.name
            )
        }
    }

    func testZeroNewWordsKeepsDueAndIntroducedNewButHidesUnseenNew() throws {
        let candidates = [
            makeCandidate(
                id: "due",
                state: .review,
                due: Self.dayEnd.addingTimeInterval(-1)
            ),
            makeCandidate(
                id: "introduced-primary",
                direction: .learningToNative,
                state: .new,
                introducedAt: Self.dayStart.addingTimeInterval(-10)
            ),
            makeCandidate(
                id: "introduced-reverse",
                direction: .nativeToLearning,
                state: .new,
                introducedAt: Self.dayStart.addingTimeInterval(-10)
            ),
            makeCandidate(
                id: "unseen-primary",
                direction: .learningToNative,
                state: .new
            ),
            makeCandidate(
                id: "unseen-reverse",
                direction: .nativeToLearning,
                state: .new
            ),
        ]
        let repository = RepositoryStub(snapshot: snapshot(candidates))

        let queue = try ReviewQueueService(repository: repository).build(
            request(newWordsPerDay: 0)
        )

        XCTAssertEqual(
            queue.items.map(\.card.id),
            ["due", "introduced-primary", "introduced-reverse"]
        )
        XCTAssertEqual(queue.counts, ReviewQueueCounts(new: 2, learning: 0, review: 1))
    }

    func testPrimaryQuotaCountsDistinctIntroducedWordsAndReverseUsesSeparateCap() throws {
        let candidates = [
            makeCandidate(
                id: "introduced",
                wordID: "introduced-a",
                direction: .learningToNative,
                state: .new,
                introducedAt: Self.dayStart.addingTimeInterval(10)
            ),
            makeCandidate(
                id: "primary-1",
                direction: .learningToNative,
                state: .new,
                createdOffset: 1
            ),
            makeCandidate(
                id: "primary-2",
                direction: .learningToNative,
                state: .new,
                createdOffset: 2
            ),
            makeCandidate(
                id: "reverse-1",
                direction: .nativeToLearning,
                state: .new,
                createdOffset: 1
            ),
            makeCandidate(
                id: "reverse-2",
                direction: .nativeToLearning,
                state: .new,
                createdOffset: 2
            ),
            makeCandidate(
                id: "reverse-3",
                direction: .nativeToLearning,
                state: .new,
                createdOffset: 3
            ),
        ]
        let repository = RepositoryStub(
            snapshot: snapshot(
                candidates,
                introducedPrimaryWordIDs: ["introduced-a", "introduced-b"],
                introducedReverseCount: 1
            )
        )

        let queue = try ReviewQueueService(repository: repository).build(
            request(newWordsPerDay: 3)
        )

        XCTAssertEqual(
            queue.items.map(\.card.id),
            ["introduced", "primary-1", "reverse-1", "reverse-2"]
        )
        XCTAssertEqual(repository.events, ["create:3", "fetch"])
    }

    func testUnusedQuotaNeverRollsBetweenDirections() throws {
        struct Case {
            let name: String
            let introducedPrimary: Set<String>
            let introducedReverse: Int
            let expectedIDs: [String]
        }
        let cases = [
            Case(
                name: "unused reverse quota does not increase primary quota",
                introducedPrimary: [],
                introducedReverse: 2,
                expectedIDs: ["primary-1", "primary-2"]
            ),
            Case(
                name: "unused primary quota does not increase reverse quota",
                introducedPrimary: ["used-a", "used-b"],
                introducedReverse: 0,
                expectedIDs: ["reverse-1", "reverse-2"]
            ),
        ]
        let allCandidates = [
            makeCandidate(
                id: "primary-1",
                direction: .learningToNative,
                state: .new,
                createdOffset: 1
            ),
            makeCandidate(
                id: "primary-2",
                direction: .learningToNative,
                state: .new,
                createdOffset: 2
            ),
            makeCandidate(
                id: "primary-3",
                direction: .learningToNative,
                state: .new,
                createdOffset: 3
            ),
            makeCandidate(
                id: "reverse-1",
                direction: .nativeToLearning,
                state: .new,
                createdOffset: 1
            ),
            makeCandidate(
                id: "reverse-2",
                direction: .nativeToLearning,
                state: .new,
                createdOffset: 2
            ),
            makeCandidate(
                id: "reverse-3",
                direction: .nativeToLearning,
                state: .new,
                createdOffset: 3
            ),
        ]

        for testCase in cases {
            let repository = RepositoryStub(
                snapshot: snapshot(
                    allCandidates,
                    introducedPrimaryWordIDs: testCase.introducedPrimary,
                    introducedReverseCount: testCase.introducedReverse
                )
            )
            let queue = try ReviewQueueService(repository: repository).build(
                request(newWordsPerDay: 2)
            )
            XCTAssertEqual(queue.items.map(\.card.id), testCase.expectedIDs, testCase.name)
        }
    }

    func testUnseenPrimaryCardsUseStableSetRoundRobin() throws {
        let candidates = [
            makeCandidate(
                id: "set-b-2",
                direction: .learningToNative,
                state: .new,
                createdOffset: 4,
                activeSetIDs: ["set-b"]
            ),
            makeCandidate(
                id: "set-a-2",
                direction: .learningToNative,
                state: .new,
                createdOffset: 3,
                activeSetIDs: ["set-a"]
            ),
            makeCandidate(
                id: "set-b-1",
                direction: .learningToNative,
                state: .new,
                createdOffset: 2,
                activeSetIDs: ["set-b"]
            ),
            makeCandidate(
                id: "set-a-1",
                direction: .learningToNative,
                state: .new,
                createdOffset: 1,
                activeSetIDs: ["set-a"]
            ),
        ]
        let repository = RepositoryStub(snapshot: snapshot(candidates))

        let queue = try ReviewQueueService(repository: repository).build(
            request(newWordsPerDay: 4)
        )

        XCTAssertEqual(
            queue.items.map(\.card.id),
            ["set-a-1", "set-b-1", "set-a-2", "set-b-2"]
        )
    }

    func testRoundRobinConsumesAMultiSetWordOnlyOnce() throws {
        let candidates = [
            makeCandidate(
                id: "multi",
                direction: .learningToNative,
                state: .new,
                createdOffset: 0,
                activeSetIDs: ["set-b", "set-a"]
            ),
            makeCandidate(
                id: "only-a",
                direction: .learningToNative,
                state: .new,
                createdOffset: 1,
                activeSetIDs: ["set-a"]
            ),
            makeCandidate(
                id: "only-b",
                direction: .learningToNative,
                state: .new,
                createdOffset: 2,
                activeSetIDs: ["set-b"]
            ),
        ]
        let repository = RepositoryStub(snapshot: snapshot(candidates))

        let queue = try ReviewQueueService(repository: repository).build(
            request(newWordsPerDay: 3)
        )

        XCTAssertEqual(queue.items.map(\.card.id), ["multi", "only-b", "only-a"])
        XCTAssertEqual(queue.items.filter { $0.card.id == "multi" }.count, 1)
        XCTAssertEqual(queue.items.first?.activeSetIDs, ["set-a", "set-b"])
    }

    func testSiblingSeparationHasStableExactOrderWithoutDroppingCards() throws {
        struct Case {
            let input: [String]
            let expected: [String]
        }
        let cases = [
            Case(input: ["a-primary", "a-reverse", "b"], expected: ["a-primary", "b", "a-reverse"]),
            Case(input: ["b", "a-primary", "a-reverse"], expected: ["a-primary", "b", "a-reverse"]),
            Case(input: ["b", "a-primary", "a-reverse", "c"], expected: ["b", "a-primary", "c", "a-reverse"]),
            Case(input: ["a-primary", "a-reverse"], expected: ["a-primary", "a-reverse"]),
        ]

        for testCase in cases {
            let candidates = testCase.input.enumerated().map { index, id in
                makeCandidate(
                    id: id,
                    wordID: id.hasPrefix("a-") ? "word-a" : "word-\(id)",
                    direction: id == "a-reverse" ? .nativeToLearning : .learningToNative,
                    state: .learning,
                    due: Self.dayStart.addingTimeInterval(Double(index))
                )
            }
            let repository = RepositoryStub(snapshot: snapshot(candidates))
            let queue = try ReviewQueueService(repository: repository).build(request())
            let actual = queue.items.map(\.card.id)

            XCTAssertEqual(actual, testCase.expected, "\(testCase.input)")
            XCTAssertEqual(actual.sorted(), testCase.input.sorted(), "\(testCase.input)")
        }
    }

    func testReverseEligibilityTracksTheRepositoryActiveMembershipPolicy() throws {
        let reverse = makeCandidate(
            id: "reverse",
            direction: .nativeToLearning,
            state: .new
        )
        let cases: [(name: String, candidates: [ReviewQueueCandidate], expected: [String])] = [
            (
                name: "at least one active membership has reverse enabled",
                candidates: [reverse],
                expected: ["reverse"]
            ),
            (
                name: "last reverse-enabled membership is disabled",
                candidates: [],
                expected: []
            ),
        ]

        for testCase in cases {
            let repository = RepositoryStub(snapshot: snapshot(testCase.candidates))
            let queue = try ReviewQueueService(repository: repository).build(
                request(newWordsPerDay: 1)
            )
            XCTAssertEqual(queue.items.map(\.card.id), testCase.expected, testCase.name)
        }
    }

    func testFocusedUsesGlobalEligibilityAndQuotaRulesForExplicitCardID() throws {
        struct Case {
            let name: String
            let candidate: ReviewQueueCandidate?
            let introducedPrimary: Set<String>
            let introducedReverse: Int
            let limit: Int
            let expectedID: String?
        }
        let cases = [
            Case(
                name: "due review bypasses zero quota",
                candidate: makeCandidate(
                    id: "target",
                    state: .review,
                    due: Self.dayStart.addingTimeInterval(-1)
                ),
                introducedPrimary: [],
                introducedReverse: 0,
                limit: 0,
                expectedID: "target"
            ),
            Case(
                name: "introduced new bypasses zero quota",
                candidate: makeCandidate(
                    id: "target",
                    state: .new,
                    introducedAt: Self.dayStart.addingTimeInterval(-1)
                ),
                introducedPrimary: [],
                introducedReverse: 0,
                limit: 0,
                expectedID: "target"
            ),
            Case(
                name: "unseen primary is rejected when quota exhausted",
                candidate: makeCandidate(id: "target", state: .new),
                introducedPrimary: ["already-used"],
                introducedReverse: 0,
                limit: 1,
                expectedID: nil
            ),
            Case(
                name: "unseen primary is admitted when quota remains",
                candidate: makeCandidate(id: "target", state: .new),
                introducedPrimary: [],
                introducedReverse: 0,
                limit: 1,
                expectedID: "target"
            ),
            Case(
                name: "unseen reverse is rejected when separate cap exhausted",
                candidate: makeCandidate(
                    id: "target",
                    direction: .nativeToLearning,
                    state: .new
                ),
                introducedPrimary: [],
                introducedReverse: 1,
                limit: 1,
                expectedID: nil
            ),
            Case(
                name: "explicitly selected future review is admitted",
                candidate: makeCandidate(
                    id: "target",
                    state: .review,
                    due: Self.dayEnd
                ),
                introducedPrimary: [],
                introducedReverse: 0,
                limit: 1,
                expectedID: "target"
            ),
            Case(
                name: "inactive-only target is rejected",
                candidate: makeCandidate(
                    id: "target",
                    state: .learning,
                    activeSetIDs: []
                ),
                introducedPrimary: [],
                introducedReverse: 0,
                limit: 1,
                expectedID: nil
            ),
            Case(
                name: "locked target is rejected",
                candidate: makeCandidate(
                    id: "target",
                    state: .learning,
                    isLocked: true
                ),
                introducedPrimary: [],
                introducedReverse: 0,
                limit: 1,
                expectedID: nil
            ),
            Case(
                name: "reverse target excluded by membership policy is unresolved",
                candidate: nil,
                introducedPrimary: [],
                introducedReverse: 0,
                limit: 1,
                expectedID: nil
            ),
        ]

        for testCase in cases {
            let unrelated = makeCandidate(
                id: "unrelated",
                wordID: "unrelated-word",
                direction: .nativeToLearning,
                state: .new,
                createdOffset: 100,
                activeSetIDs: ["unrelated-set"]
            )
            let repository = RepositoryStub(
                snapshot: snapshot(
                    (testCase.candidate.map { [$0] } ?? []) + [unrelated],
                    introducedPrimaryWordIDs: testCase.introducedPrimary,
                    introducedReverseCount: testCase.introducedReverse
                )
            )
            let focused = try ReviewQueueService(repository: repository).focused(
                FocusedReviewRequest(
                    cardID: "target",
                    queueRequest: request(newWordsPerDay: testCase.limit)
                )
            )
            XCTAssertEqual(focused?.card.id, testCase.expectedID, testCase.name)
            XCTAssertEqual(repository.events, ["fetch"], testCase.name)
            XCTAssertTrue(repository.creations.isEmpty, testCase.name)
        }
    }

    func testBuildForwardsEveryRequestFieldAndClampsCreationLimit() throws {
        let now = Date(timeIntervalSince1970: 2_100_000_000)
        let studyDay = StudyDay(
            start: now.addingTimeInterval(-1_000),
            end: now.addingTimeInterval(2_000)
        )
        let cases = [
            (name: "positive", requested: 7, expected: 7),
            (name: "zero", requested: 0, expected: 0),
            (name: "negative", requested: -3, expected: 0),
        ]

        for testCase in cases {
            let repository = RepositoryStub(snapshot: snapshot([]))
            let request = ReviewQueueRequest(
                nativeLanguage: "uz",
                learningLanguage: "de",
                now: now,
                studyDay: studyDay,
                primaryDirection: .nativeToLearning,
                newWordsPerDay: testCase.requested
            )

            _ = try ReviewQueueService(repository: repository).build(request)

            XCTAssertEqual(
                repository.creations,
                [
                    RepositoryStub.Creation(
                        nativeLanguage: "uz",
                        learningLanguage: "de",
                        primaryDirection: .nativeToLearning,
                        creationLimit: testCase.expected,
                        studyDay: studyDay,
                        now: now
                    )
                ],
                testCase.name
            )
            XCTAssertEqual(
                repository.fetches,
                [
                    RepositoryStub.Fetch(
                        nativeLanguage: "uz",
                        learningLanguage: "de",
                        studyDay: studyDay,
                        primaryDirection: .nativeToLearning
                    )
                ],
                testCase.name
            )
        }
    }

    func testBuildPropagatesCreateErrorWithoutFetching() {
        let repository = RepositoryStub(
            snapshot: snapshot([]),
            createError: .create
        )

        XCTAssertThrowsError(
            try ReviewQueueService(repository: repository).build(
                request(newWordsPerDay: 2)
            )
        ) { error in
            XCTAssertEqual(error as? RepositoryStub.Failure, .create)
        }
        XCTAssertEqual(repository.events, ["create:2"])
        XCTAssertTrue(repository.fetches.isEmpty)
    }

    func testBuildPropagatesFetchErrorAfterCreation() {
        let repository = RepositoryStub(
            snapshot: snapshot([]),
            fetchError: .fetch
        )

        XCTAssertThrowsError(
            try ReviewQueueService(repository: repository).build(
                request(newWordsPerDay: 2)
            )
        ) { error in
            XCTAssertEqual(error as? RepositoryStub.Failure, .fetch)
        }
        XCTAssertEqual(repository.events, ["create:2", "fetch"])
        XCTAssertEqual(repository.creations.count, 1)
        XCTAssertEqual(repository.fetches.count, 1)
    }

    func testCancellableBuildStopsBeforeFetchWhenCancelledAfterCreation() throws {
        let repository = RepositoryStub(
            snapshot: snapshot([makeCandidate(id: "stale", state: .learning)])
        )
        var checkpointCount = 0

        let queue = try ReviewQueueService(repository: repository).build(
            request()
        ) {
            checkpointCount += 1
            return false
        }

        XCTAssertNil(queue)
        XCTAssertEqual(checkpointCount, 1)
        XCTAssertEqual(repository.events, ["create:10"])
        XCTAssertEqual(repository.creations.count, 1)
        XCTAssertTrue(repository.fetches.isEmpty)
    }

    func testCancellableBuildStopsBeforeProcessingWhenCancelledAfterFetch() throws {
        let repository = RepositoryStub(
            snapshot: snapshot([makeCandidate(id: "stale", state: .learning)])
        )
        var checkpointCount = 0

        let queue = try ReviewQueueService(repository: repository).build(
            request()
        ) {
            checkpointCount += 1
            return checkpointCount == 1
        }

        XCTAssertNil(queue)
        XCTAssertEqual(checkpointCount, 2)
        XCTAssertEqual(repository.events, ["create:10", "fetch"])
        XCTAssertEqual(repository.creations.count, 1)
        XCTAssertEqual(repository.fetches.count, 1)
    }

    func testFocusedForwardsOnlyFetchFieldsAndPropagatesFetchError() {
        let now = Date(timeIntervalSince1970: 2_200_000_000)
        let studyDay = StudyDay(
            start: now.addingTimeInterval(-500),
            end: now.addingTimeInterval(500)
        )
        let repository = RepositoryStub(
            snapshot: snapshot([]),
            fetchError: .fetch
        )
        let request = ReviewQueueRequest(
            nativeLanguage: "fr",
            learningLanguage: "it",
            now: now,
            studyDay: studyDay,
            primaryDirection: .nativeToLearning,
            newWordsPerDay: 9
        )

        XCTAssertThrowsError(
            try ReviewQueueService(repository: repository).focused(
                FocusedReviewRequest(cardID: "missing", queueRequest: request)
            )
        ) { error in
            XCTAssertEqual(error as? RepositoryStub.Failure, .fetch)
        }
        XCTAssertTrue(repository.creations.isEmpty)
        XCTAssertEqual(repository.events, ["fetch"])
        XCTAssertEqual(
            repository.fetches,
            [
                RepositoryStub.Fetch(
                    nativeLanguage: "fr",
                    learningLanguage: "it",
                    studyDay: studyDay,
                    primaryDirection: .nativeToLearning
                )
            ]
        )
    }

    func testOrderingAndCountersAreDeterministicFromFinalQueue() throws {
        let candidates = [
            makeCandidate(id: "new", state: .new, createdOffset: 3),
            makeCandidate(
                id: "review",
                state: .review,
                due: Self.dayStart.addingTimeInterval(1)
            ),
            makeCandidate(
                id: "learning",
                state: .learning,
                due: Self.now.addingTimeInterval(-1)
            ),
        ]
        let first = try ReviewQueueService(
            repository: RepositoryStub(snapshot: snapshot(candidates))
        ).build(request(newWordsPerDay: 1))
        let second = try ReviewQueueService(
            repository: RepositoryStub(snapshot: snapshot(candidates.reversed()))
        ).build(request(newWordsPerDay: 1))

        XCTAssertEqual(first, second)
        XCTAssertEqual(first.items.map(\.card.id), ["learning", "review", "new"])
        XCTAssertEqual(first.counts, ReviewQueueCounts(new: 1, learning: 1, review: 1))
    }

    @MainActor
    func testNotificationSchedulerUsesQueuePriorityQuotaLockedFilteringAndExistingDistribution() async throws {
        let candidates = [
            makeCandidate(
                id: "learning",
                state: .learning,
                due: Self.now.addingTimeInterval(-1)
            ),
            makeCandidate(
                id: "review",
                state: .review,
                due: Self.dayStart.addingTimeInterval(-1)
            ),
            makeCandidate(
                id: "new-first",
                state: .new,
                createdOffset: 1
            ),
            makeCandidate(
                id: "new-over-quota",
                state: .new,
                createdOffset: 2
            ),
            makeCandidate(
                id: "locked-due",
                state: .learning,
                due: Self.now.addingTimeInterval(-100),
                isLocked: true
            ),
        ]
        let repository = RepositoryStub(
            snapshot: snapshot(
                candidates,
                introducedPrimaryWordIDs: ["introduced-earlier"]
            )
        )
        let center = FakeStudyNotificationCenter(
            authorizationStatus: .authorized
        )
        let scheduler = StudyReminderScheduler(notificationCenter: center)
        scheduler.register(
            queue: ReviewQueueService(repository: repository),
            settingsSnapshot: { _, _, _, _ in
                ReviewSettingsSnapshot(
                    configuration: .initial,
                    newWordsPerDay: 2,
                    primaryDirection: .learningToNative,
                    studyDay: Self.studyDay
                )
            },
            now: { Self.now },
            hasVocabulary: { true }
        )
        let defaults = notificationDefaults(
            remindersEnabled: true,
            from: 7 * 60 + 30,
            until: 19 * 60 + 30,
            count: 3,
            newWordsPerDay: 2
        )
        let settings = AppSettingsStore(defaults: defaults)

        await scheduler.apply(using: settings)

        XCTAssertEqual(
            center.added.map(\.content.title),
            ["word-learning", "word-review", "word-new-first"]
        )
        XCTAssertFalse(
            center.added.contains {
                $0.content.title == "word-locked-due"
                    || $0.content.title == "word-new-over-quota"
            }
        )
        XCTAssertEqual(
            center.added.compactMap {
                ($0.trigger as? UNCalendarNotificationTrigger)?
                    .dateComponents
            }.map { [$0.hour, $0.minute] },
            [
                [7, 30],
                [13, 30],
                [19, 30],
            ]
        )
    }

    @MainActor
    func testNotificationSchedulerNeverFallsBackToLegacyDashboardSelection() async throws {
        let repository = RepositoryStub(snapshot: snapshot([]))
        let center = FakeStudyNotificationCenter(
            authorizationStatus: .authorized
        )
        let scheduler = StudyReminderScheduler(notificationCenter: center)
        scheduler.register(
            queue: ReviewQueueService(repository: repository),
            settingsSnapshot: { _, _, _, _ in
                ReviewSettingsSnapshot(
                    configuration: .initial,
                    newWordsPerDay: 0,
                    primaryDirection: .learningToNative,
                    studyDay: Self.studyDay
                )
            },
            now: { Self.now },
            hasVocabulary: { true }
        )
        let defaults = notificationDefaults(
            remindersEnabled: true,
            from: 8 * 60,
            until: 20 * 60,
            count: 3,
            newWordsPerDay: 0
        )
        let settings = AppSettingsStore(defaults: defaults)

        await scheduler.apply(using: settings)

        XCTAssertEqual(center.added.count, 3)
        XCTAssertEqual(
            Set(center.added.map(\.content.title)),
            ["Time to review"]
        )
        XCTAssertEqual(
            Set(center.added.map(\.content.body)),
            ["Open Owl AI for a quick session."]
        )
    }

    @MainActor
    func testDeniedNotificationsDoNotReadQueueOrChangeLocalFromStudyDay() async throws {
        let repository = RepositoryStub(snapshot: snapshot([]))
        let center = FakeStudyNotificationCenter(
            authorizationStatus: .denied
        )
        let scheduler = StudyReminderScheduler(notificationCenter: center)
        var snapshotCalls = 0
        scheduler.register(
            queue: ReviewQueueService(repository: repository),
            settingsSnapshot: { _, _, _, _ in
                snapshotCalls += 1
                return ReviewSettingsSnapshot(
                    configuration: .initial,
                    newWordsPerDay: 5,
                    primaryDirection: .learningToNative,
                    studyDay: Self.studyDay
                )
            },
            now: { Self.now },
            hasVocabulary: { true }
        )
        let defaults = notificationDefaults(
            remindersEnabled: true,
            from: 7 * 60 + 30,
            until: 19 * 60,
            count: 3,
            newWordsPerDay: 5
        )
        let settings = AppSettingsStore(defaults: defaults)
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = try XCTUnwrap(
            TimeZone(identifier: "America/Chicago")
        )
        let fsrsSettings = FSRSSettingsStore(
            settings: settings,
            configurationProvider: StaticFSRSConfigurationProvider(),
            calendar: calendar
        )
        let settingsBefore = notificationStudySettings(settings)
        let snapshotBefore = try fsrsSettings.snapshot(now: Self.now)

        await scheduler.apply(using: settings)

        let settingsAfter = notificationStudySettings(settings)
        let snapshotAfter = try fsrsSettings.snapshot(now: Self.now)
        XCTAssertEqual(snapshotCalls, 0)
        XCTAssertTrue(center.added.isEmpty)
        XCTAssertEqual(settingsAfter, settingsBefore)
        XCTAssertEqual(settingsAfter.reminderFromMinutes, 7 * 60 + 30)
        XCTAssertEqual(snapshotAfter.configuration, snapshotBefore.configuration)
        XCTAssertEqual(snapshotAfter.newWordsPerDay, snapshotBefore.newWordsPerDay)
        XCTAssertEqual(snapshotAfter.primaryDirection, snapshotBefore.primaryDirection)
        XCTAssertEqual(snapshotAfter.studyDay, snapshotBefore.studyDay)
        XCTAssertEqual(
            calendar.dateComponents(
                [.hour, .minute],
                from: snapshotAfter.studyDay.start
            ),
            DateComponents(hour: 7, minute: 30)
        )
    }

    @MainActor
    func testLatestDisabledWinsWhenOlderEnabledIsPausedInAuthorization() async {
        let repository = RepositoryStub(snapshot: snapshot([]))
        let center = FakeStudyNotificationCenter(
            authorizationStatus: .authorized
        )
        center.pauseNextAuthorization()
        let scheduler = StudyReminderScheduler(notificationCenter: center)
        scheduler.register(
            queue: ReviewQueueService(repository: repository),
            settingsSnapshot: { _, _, _, _ in
                ReviewSettingsSnapshot(
                    configuration: .initial,
                    newWordsPerDay: 0,
                    primaryDirection: .learningToNative,
                    studyDay: Self.studyDay
                )
            },
            now: { Self.now },
            hasVocabulary: { true }
        )
        let enabled = AppSettingsStore(
            defaults: notificationDefaults(
                remindersEnabled: true,
                from: 7 * 60 + 30,
                until: 19 * 60 + 30,
                count: 3,
                newWordsPerDay: 0
            )
        )
        let disabled = AppSettingsStore(
            defaults: notificationDefaults(
                remindersEnabled: false,
                from: 9 * 60,
                until: 21 * 60,
                count: 2,
                newWordsPerDay: 0
            )
        )

        let older = Task { @MainActor in
            await scheduler.apply(using: enabled)
        }
        await center.waitUntilAuthorizationPaused()
        let newer = await beginApply(
            scheduler: scheduler,
            settings: disabled
        )
        center.resumeAuthorization()
        await older.value
        await newer.value

        XCTAssertTrue(center.pending.isEmpty)
    }

    @MainActor
    func testLatestDisabledWinsWhenOlderEnabledIsPausedInsideAdd() async {
        let repository = RepositoryStub(snapshot: snapshot([]))
        let center = FakeStudyNotificationCenter(
            authorizationStatus: .authorized
        )
        center.pauseNextAdd()
        let scheduler = StudyReminderScheduler(notificationCenter: center)
        scheduler.register(
            queue: ReviewQueueService(repository: repository),
            settingsSnapshot: { _, _, _, _ in
                ReviewSettingsSnapshot(
                    configuration: .initial,
                    newWordsPerDay: 0,
                    primaryDirection: .learningToNative,
                    studyDay: Self.studyDay
                )
            },
            now: { Self.now },
            hasVocabulary: { true }
        )
        let enabled = AppSettingsStore(
            defaults: notificationDefaults(
                remindersEnabled: true,
                from: 7 * 60 + 30,
                until: 19 * 60 + 30,
                count: 3,
                newWordsPerDay: 0
            )
        )
        let disabled = AppSettingsStore(
            defaults: notificationDefaults(
                remindersEnabled: false,
                from: 9 * 60,
                until: 21 * 60,
                count: 2,
                newWordsPerDay: 0
            )
        )

        let older = Task { @MainActor in
            await scheduler.apply(using: enabled)
        }
        await center.waitUntilAddPaused()
        let newer = await beginApply(
            scheduler: scheduler,
            settings: disabled
        )
        center.resumeAdd()
        await older.value
        await newer.value

        XCTAssertTrue(center.pending.isEmpty)
    }

    @MainActor
    func testLatestEnabledWindowAndContentReplaceOlderPausedScheduleWithoutDuplicates() async {
        let repository = RepositoryStub(snapshot: snapshot([]))
        let center = FakeStudyNotificationCenter(
            authorizationStatus: .authorized
        )
        center.pauseNextAdd()
        let scheduler = StudyReminderScheduler(notificationCenter: center)
        scheduler.register(
            queue: ReviewQueueService(repository: repository),
            settingsSnapshot: { _, _, _, _ in
                ReviewSettingsSnapshot(
                    configuration: .initial,
                    newWordsPerDay: 0,
                    primaryDirection: .learningToNative,
                    studyDay: Self.studyDay
                )
            },
            now: { Self.now },
            hasVocabulary: { false }
        )
        let olderSettings = AppSettingsStore(
            defaults: notificationDefaults(
                remindersEnabled: true,
                from: 7 * 60,
                until: 19 * 60,
                count: 3,
                newWordsPerDay: 0,
                nativeLanguage: "en",
                learningLanguage: "es"
            )
        )
        let newerSettings = AppSettingsStore(
            defaults: notificationDefaults(
                remindersEnabled: true,
                from: 9 * 60,
                until: 21 * 60,
                count: 3,
                newWordsPerDay: 0,
                nativeLanguage: "de",
                learningLanguage: "it"
            )
        )

        let older = Task { @MainActor in
            await scheduler.apply(using: olderSettings)
        }
        await center.waitUntilAddPaused()
        let newer = await beginApply(
            scheduler: scheduler,
            settings: newerSettings
        )
        newerSettings.nativeLanguageCode = "fr"
        newerSettings.learningLanguageCode = "tr"
        center.resumeAdd()
        await older.value
        await newer.value

        XCTAssertEqual(
            center.pending.map(\.identifier).sorted(),
            [
                "com.flashcardai.study.empty-learning",
                "com.flashcardai.study.empty-native",
            ]
        )
        XCTAssertEqual(
            Set(center.pending.map(\.content.title)),
            [
                "Aggiungi la tua prima parola 🦉",
                "Füge dein erstes Wort hinzu 🦉",
            ]
        )
        XCTAssertEqual(
            center.pending.compactMap {
                ($0.trigger as? UNCalendarNotificationTrigger)?
                    .dateComponents.hour
            }.sorted(),
            [9, 21]
        )
    }

    @MainActor
    func testBurstCoalescesToActiveAndLatestExternalTransactionsOnly() async {
        let repository = RepositoryStub(snapshot: snapshot([]))
        let center = FakeStudyNotificationCenter(
            authorizationStatus: .authorized
        )
        center.pauseNextAuthorization()
        let scheduler = StudyReminderScheduler(notificationCenter: center)
        var settingsSnapshotCalls = 0
        scheduler.register(
            queue: ReviewQueueService(repository: repository),
            settingsSnapshot: { _, _, _, _ in
                settingsSnapshotCalls += 1
                return ReviewSettingsSnapshot(
                    configuration: .initial,
                    newWordsPerDay: 0,
                    primaryDirection: .learningToNative,
                    studyDay: Self.studyDay
                )
            },
            now: { Self.now },
            hasVocabulary: { false }
        )
        let activeSettings = AppSettingsStore(
            defaults: notificationDefaults(
                remindersEnabled: true,
                from: 7 * 60,
                until: 19 * 60,
                count: 3,
                newWordsPerDay: 0,
                nativeLanguage: "en",
                learningLanguage: "es"
            )
        )

        let active = Task { @MainActor in
            await scheduler.apply(using: activeSettings)
        }
        await center.waitUntilAuthorizationPaused()

        var submitted: [Task<Void, Never>] = []
        for index in 0..<15 {
            let isLatest = index == 14
            let settings = AppSettingsStore(
                defaults: notificationDefaults(
                    remindersEnabled:
                        isLatest || index.isMultiple(of: 2),
                    from: isLatest ? 9 * 60 : (8 + index % 2) * 60,
                    until: isLatest ? 21 * 60 : (18 + index % 2) * 60,
                    count: 3,
                    newWordsPerDay: index,
                    nativeLanguage: isLatest ? "de" : "en",
                    learningLanguage: isLatest ? "it" : "es"
                )
            )
            submitted.append(
                await beginApply(
                    scheduler: scheduler,
                    settings: settings
                )
            )
        }

        center.resumeAuthorization()
        await active.value
        for task in submitted {
            await task.value
        }

        XCTAssertEqual(center.pendingRequestCallCount, 2)
        XCTAssertEqual(center.authorizationCallCount, 2)
        XCTAssertEqual(settingsSnapshotCalls, 2)
        XCTAssertEqual(center.added.count, 4)
        XCTAssertEqual(
            center.pending.map(\.identifier).sorted(),
            [
                "com.flashcardai.study.empty-learning",
                "com.flashcardai.study.empty-native",
            ]
        )
        XCTAssertEqual(
            Set(center.pending.map(\.content.title)),
            [
                "Aggiungi la tua prima parola 🦉",
                "Füge dein erstes Wort hinzu 🦉",
            ]
        )
        XCTAssertEqual(
            center.pending.compactMap {
                ($0.trigger as? UNCalendarNotificationTrigger)?
                    .dateComponents.hour
            }.sorted(),
            [9, 21]
        )
    }

    @MainActor
    func testNonSingletonSchedulerReleasesAfterPausedActiveAndPendingSettle() async {
        let repository = RepositoryStub(snapshot: snapshot([]))
        let center = FakeStudyNotificationCenter(
            authorizationStatus: .authorized
        )
        center.pauseNextAuthorization()
        var scheduler: StudyReminderScheduler? = StudyReminderScheduler(
            notificationCenter: center
        )
        scheduler?.register(
            queue: ReviewQueueService(repository: repository),
            settingsSnapshot: { _, _, _, _ in
                ReviewSettingsSnapshot(
                    configuration: .initial,
                    newWordsPerDay: 0,
                    primaryDirection: .learningToNative,
                    studyDay: Self.studyDay
                )
            },
            now: { Self.now },
            hasVocabulary: { true }
        )
        weak let releasedScheduler = scheduler
        let activeFinished = AsyncTestCompletion()
        let pendingFinished = AsyncTestCompletion()
        let activeSettings = AppSettingsStore(
            defaults: notificationDefaults(
                remindersEnabled: true,
                from: 7 * 60,
                until: 19 * 60,
                count: 3,
                newWordsPerDay: 0
            )
        )
        let pendingSettings = AppSettingsStore(
            defaults: notificationDefaults(
                remindersEnabled: false,
                from: 9 * 60,
                until: 21 * 60,
                count: 2,
                newWordsPerDay: 0
            )
        )

        Task { @MainActor [weak scheduler] in
            guard let scheduler else {
                activeFinished.signal()
                return
            }
            await scheduler.apply(using: activeSettings)
            activeFinished.signal()
        }
        await center.waitUntilAuthorizationPaused()
        await withCheckedContinuation {
            (started: CheckedContinuation<Void, Never>) in
            Task { @MainActor [weak scheduler] in
                guard let scheduler else {
                    started.resume()
                    pendingFinished.signal()
                    return
                }
                started.resume()
                await scheduler.apply(using: pendingSettings)
                pendingFinished.signal()
            }
        }

        scheduler = nil
        XCTAssertNotNil(releasedScheduler)
        center.resumeAuthorization()
        await activeFinished.wait()
        await pendingFinished.wait()

        XCTAssertNil(releasedScheduler)
        XCTAssertTrue(center.pending.isEmpty)
    }

    @MainActor
    private func beginApply(
        scheduler: StudyReminderScheduler,
        settings: AppSettingsStore
    ) async -> Task<Void, Never> {
        var task: Task<Void, Never>!
        await withCheckedContinuation {
            (started: CheckedContinuation<Void, Never>) in
            task = Task { @MainActor in
                started.resume()
                await scheduler.apply(using: settings)
            }
        }
        return task
    }

    private func notificationDefaults(
        remindersEnabled: Bool,
        from: Int,
        until: Int,
        count: Int,
        newWordsPerDay: Int,
        nativeLanguage: String = "en",
        learningLanguage: String = "es"
    ) -> UserDefaults {
        let suiteName = "StudyReminderSchedulerTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suiteName)!
        defaults.set(
            remindersEnabled,
            forKey: "FlashCardAI.settings.remindersEnabled"
        )
        defaults.set(
            [from, until],
            forKey: "FlashCardAI.settings.reminderMinutes"
        )
        defaults.set(
            count,
            forKey: "FlashCardAI.settings.notificationCount"
        )
        defaults.set(
            newWordsPerDay,
            forKey: "FlashCardAI.settings.dailyReviewGoal"
        )
        defaults.set(
            nativeLanguage,
            forKey: "FlashCardAI.settings.nativeLanguage"
        )
        defaults.set(
            learningLanguage,
            forKey: "FlashCardAI.settings.learningLanguage"
        )
        addTeardownBlock {
            defaults.removePersistentDomain(forName: suiteName)
        }
        return defaults
    }

    private func request(newWordsPerDay: Int = 10) -> ReviewQueueRequest {
        ReviewQueueRequest(
            nativeLanguage: "en",
            learningLanguage: "es",
            now: Self.now,
            studyDay: Self.studyDay,
            primaryDirection: .learningToNative,
            newWordsPerDay: newWordsPerDay
        )
    }

    @MainActor
    private func notificationStudySettings(
        _ settings: AppSettingsStore
    ) -> NotificationStudySettings {
        NotificationStudySettings(
            remindersEnabled: settings.remindersEnabled,
            reminderMinutesFromMidnight:
                settings.reminderMinutesFromMidnight,
            reminderFromMinutes: settings.reminderFromMinutes,
            notificationCountPerDay: settings.notificationCountPerDay,
            nativeLanguageCode: settings.nativeLanguageCode,
            learningLanguageCode: settings.learningLanguageCode,
            newWordsPerDay: settings.newWordsPerDay,
            reviewDirection: settings.reviewDirection
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
        wordID: String? = nil,
        direction: ReviewDirection = .learningToNative,
        state: ReviewCardState,
        due: Date = ReviewQueueServiceTests.now,
        introducedAt: Date? = nil,
        createdOffset: TimeInterval = 0,
        activeSetIDs: [String] = ["set-a"],
        isLocked: Bool = false
    ) -> ReviewQueueCandidate {
        let resolvedWordID = wordID ?? "word-\(id)"
        let createdAt = Self.dayStart.addingTimeInterval(createdOffset)
        let card = ReviewCardRecord(
            id: id,
            wordID: resolvedWordID,
            direction: direction,
            state: state,
            due: due,
            stability: 0,
            difficulty: 0,
            elapsedDays: 0,
            scheduledDays: 0,
            learningStep: 0,
            reps: 0,
            lapses: 0,
            lastReview: nil,
            introducedAt: introducedAt,
            sourceTemplateID: nil,
            rowVersion: 0,
            createdAt: createdAt,
            updatedAt: createdAt
        )
        let word = WordDashboardRow(
            id: resolvedWordID,
            normalizedWord: resolvedWordID,
            displayWord: resolvedWordID,
            translation: nil,
            translations: [],
            pronunciation: nil,
            partOfSpeech: nil,
            learningLanguageBcp47: "es",
            nativeLanguageBcp47: "en",
            flashcardSetIDs: Set(activeSetIDs),
            flashcardSetLabel: activeSetIDs.first,
            exampleLines: [],
            userNotes: nil,
            isLocked: isLocked
        )
        return ReviewQueueCandidate(
            card: card,
            word: word,
            activeSetIDs: activeSetIDs,
            isLocked: isLocked
        )
    }
}

private struct StaticFSRSConfigurationProvider: FSRSConfigurationProviding {
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
}

@MainActor
private final class FakeStudyNotificationCenter: StudyNotificationCenter {
    let authorizationStatusValue: UNAuthorizationStatus
    private(set) var added: [UNNotificationRequest] = []
    private(set) var removedIdentifiers: [[String]] = []
    var pending: [UNNotificationRequest] = []
    private(set) var authorizationCallCount = 0
    private(set) var pendingRequestCallCount = 0
    private var shouldPauseAuthorization = false
    private var authorizationIsPaused = false
    private var authorizationPauseWaiters:
        [CheckedContinuation<Void, Never>] = []
    private var authorizationResume:
        CheckedContinuation<Void, Never>?
    private var shouldPauseAdd = false
    private var addIsPaused = false
    private var addPauseWaiters:
        [CheckedContinuation<Void, Never>] = []
    private var addResume: CheckedContinuation<Void, Never>?

    init(authorizationStatus: UNAuthorizationStatus) {
        authorizationStatusValue = authorizationStatus
    }

    func authorizationStatus() async -> UNAuthorizationStatus {
        authorizationCallCount += 1
        if shouldPauseAuthorization {
            shouldPauseAuthorization = false
            await withCheckedContinuation {
                (continuation: CheckedContinuation<Void, Never>) in
                authorizationResume = continuation
                authorizationIsPaused = true
                authorizationPauseWaiters.forEach { $0.resume() }
                authorizationPauseWaiters.removeAll()
            }
            authorizationIsPaused = false
        }
        return authorizationStatusValue
    }

    func pendingNotificationRequests() async -> [UNNotificationRequest] {
        pendingRequestCallCount += 1
        return pending
    }

    func removePendingNotificationRequests(
        withIdentifiers identifiers: [String]
    ) {
        removedIdentifiers.append(identifiers)
        pending.removeAll { identifiers.contains($0.identifier) }
    }

    func add(_ request: UNNotificationRequest) async throws {
        if shouldPauseAdd {
            shouldPauseAdd = false
            await withCheckedContinuation {
                (continuation: CheckedContinuation<Void, Never>) in
                addResume = continuation
                addIsPaused = true
                addPauseWaiters.forEach { $0.resume() }
                addPauseWaiters.removeAll()
            }
            addIsPaused = false
        }
        added.append(request)
        pending.removeAll { $0.identifier == request.identifier }
        pending.append(request)
    }

    func pauseNextAuthorization() {
        shouldPauseAuthorization = true
    }

    func waitUntilAuthorizationPaused() async {
        guard !authorizationIsPaused else { return }
        await withCheckedContinuation {
            authorizationPauseWaiters.append($0)
        }
    }

    func resumeAuthorization() {
        authorizationResume?.resume()
        authorizationResume = nil
    }

    func pauseNextAdd() {
        shouldPauseAdd = true
    }

    func waitUntilAddPaused() async {
        guard !addIsPaused else { return }
        await withCheckedContinuation {
            addPauseWaiters.append($0)
        }
    }

    func resumeAdd() {
        addResume?.resume()
        addResume = nil
    }
}

@MainActor
private final class AsyncTestCompletion {
    private var isComplete = false
    private var waiters: [CheckedContinuation<Void, Never>] = []

    func signal() {
        guard !isComplete else { return }
        isComplete = true
        waiters.forEach { $0.resume() }
        waiters.removeAll()
    }

    func wait() async {
        guard !isComplete else { return }
        await withCheckedContinuation {
            waiters.append($0)
        }
    }
}

private final class RepositoryStub: ReviewQueueRepository {
    enum Failure: Error, Equatable {
        case create
        case fetch
    }

    struct Creation: Equatable {
        let nativeLanguage: String
        let learningLanguage: String
        let primaryDirection: ReviewDirection
        let creationLimit: Int
        let studyDay: StudyDay
        let now: Date
    }

    struct Fetch: Equatable {
        let nativeLanguage: String
        let learningLanguage: String
        let studyDay: StudyDay
        let primaryDirection: ReviewDirection
    }

    let snapshot: ReviewCandidateSnapshot
    let createError: Failure?
    let fetchError: Failure?
    private(set) var creations: [Creation] = []
    private(set) var fetches: [Fetch] = []
    private(set) var events: [String] = []

    init(
        snapshot: ReviewCandidateSnapshot,
        createError: Failure? = nil,
        fetchError: Failure? = nil
    ) {
        self.snapshot = snapshot
        self.createError = createError
        self.fetchError = fetchError
    }

    func createMissingReverseCards(
        nativeLanguage: String,
        learningLanguage: String,
        primaryDirection: ReviewDirection,
        creationLimit: Int,
        studyDay: StudyDay,
        now: Date
    ) throws {
        creations.append(
            Creation(
                nativeLanguage: nativeLanguage,
                learningLanguage: learningLanguage,
                primaryDirection: primaryDirection,
                creationLimit: creationLimit,
                studyDay: studyDay,
                now: now
            )
        )
        events.append("create:\(creationLimit)")
        if let createError {
            throw createError
        }
    }

    func fetchCandidateSnapshot(
        nativeLanguage: String,
        learningLanguage: String,
        studyDay: StudyDay,
        primaryDirection: ReviewDirection
    ) throws -> ReviewCandidateSnapshot {
        fetches.append(
            Fetch(
                nativeLanguage: nativeLanguage,
                learningLanguage: learningLanguage,
                studyDay: studyDay,
                primaryDirection: primaryDirection
            )
        )
        events.append("fetch")
        if let fetchError {
            throw fetchError
        }
        return snapshot
    }
}
