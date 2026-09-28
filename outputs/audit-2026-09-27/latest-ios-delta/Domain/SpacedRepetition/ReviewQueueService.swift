import Foundation

struct ReviewQueueRequest {
    let nativeLanguage: String
    let learningLanguage: String
    let now: Date
    let studyDay: StudyDay
    let primaryDirection: ReviewDirection
    let newWordsPerDay: Int
}

struct FocusedReviewRequest {
    let cardID: String
    let queueRequest: ReviewQueueRequest
}

struct ReviewQueueCounts: Equatable {
    let new: Int
    let learning: Int
    let review: Int
}

struct ReviewQueue: Equatable {
    let items: [ReviewQueueCandidate]
    let counts: ReviewQueueCounts

    static let empty = ReviewQueue(
        items: [],
        counts: ReviewQueueCounts(new: 0, learning: 0, review: 0)
    )
}

struct ReviewQueueProjection {
    let queue: ReviewQueue
    let eligibleCandidates: [ReviewQueueCandidate]
}

protocol ReviewQueueRepository {
    func createMissingReverseCards(
        nativeLanguage: String,
        learningLanguage: String,
        primaryDirection: ReviewDirection,
        creationLimit: Int,
        studyDay: StudyDay,
        now: Date
    ) throws

    func fetchCandidateSnapshot(
        nativeLanguage: String,
        learningLanguage: String,
        studyDay: StudyDay,
        primaryDirection: ReviewDirection
    ) throws -> ReviewCandidateSnapshot
}

struct CardIdentity: Hashable {
    let wordID: String
    let direction: ReviewDirection
}

struct ReviewQueueService {
    let repository: ReviewQueueRepository

    func build(_ request: ReviewQueueRequest) throws -> ReviewQueue {
        try build(request, shouldContinue: { true }) ?? .empty
    }

    /// Applies the same eligibility, quota, and due partitions as `build`
    /// without creating reverse cards. Read-only surfaces such as dashboard
    /// counts use this projection so inspecting them cannot mutate scheduling.
    func project(
        _ request: ReviewQueueRequest,
        flashcardSetID: String? = nil
    ) throws -> ReviewQueueProjection {
        let snapshot = try fetchedSnapshot(for: request)
        let allEligible = eligibleCandidates(snapshot.candidates)
        let allItems = queueItems(from: snapshot, request: request)
        let eligible = filter(
            allEligible,
            flashcardSetID: flashcardSetID
        )
        let items = filter(allItems, flashcardSetID: flashcardSetID)
        return ReviewQueueProjection(
            queue: ReviewQueue(items: items, counts: counts(for: items)),
            eligibleCandidates: eligible
        )
    }

    func build(
        _ request: ReviewQueueRequest,
        shouldContinue: () -> Bool
    ) throws -> ReviewQueue? {
        try repository.createMissingReverseCards(
            nativeLanguage: request.nativeLanguage,
            learningLanguage: request.learningLanguage,
            primaryDirection: request.primaryDirection,
            creationLimit: max(0, request.newWordsPerDay),
            studyDay: request.studyDay,
            now: request.now
        )
        guard shouldContinue() else { return nil }
        let snapshot = try fetchedSnapshot(for: request)
        guard shouldContinue() else { return nil }
        let items = queueItems(from: snapshot, request: request)
        return ReviewQueue(items: items, counts: counts(for: items))
    }

    func focused(_ request: FocusedReviewRequest) throws -> ReviewQueueCandidate? {
        let queueRequest = request.queueRequest
        let snapshot = try fetchedSnapshot(for: queueRequest)
        let eligible = eligibleCandidates(
            snapshot.candidates,
            preferredCardID: request.cardID
        )
        guard let candidate = eligible.first(where: { $0.card.id == request.cardID }) else {
            return nil
        }

        switch candidate.card.state {
        case .learning, .relearning, .review:
            // An explicitly selected word can be practiced before its due date.
            return candidate
        case .new:
            if candidate.card.introducedAt != nil {
                return candidate
            }
            let limit = max(0, queueRequest.newWordsPerDay)
            if candidate.card.direction == queueRequest.primaryDirection {
                let remaining = max(
                    0,
                    limit - snapshot.introducedPrimaryWordIDs.count
                )
                return remaining > 0 ? candidate : nil
            }
            if candidate.card.direction == queueRequest.primaryDirection.opposite {
                let remaining = max(0, limit - snapshot.introducedReverseCount)
                return remaining > 0 ? candidate : nil
            }
            return nil
        }
    }

    private func fetchedSnapshot(
        for request: ReviewQueueRequest
    ) throws -> ReviewCandidateSnapshot {
        try repository.fetchCandidateSnapshot(
            nativeLanguage: request.nativeLanguage,
            learningLanguage: request.learningLanguage,
            studyDay: request.studyDay,
            primaryDirection: request.primaryDirection
        )
    }

    private func queueItems(
        from snapshot: ReviewCandidateSnapshot,
        request: ReviewQueueRequest
    ) -> [ReviewQueueCandidate] {
        let eligible = eligibleCandidates(snapshot.candidates)
        let dueLearning = eligible.filter {
            ($0.card.state == .learning || $0.card.state == .relearning)
                && $0.card.due <= request.now
        }.sorted(by: dueOrder)
        let overdueReview = eligible.filter {
            $0.card.state == .review && $0.card.due < request.studyDay.start
        }.sorted(by: dueOrder)
        let todayReview = eligible.filter {
            $0.card.state == .review
                && $0.card.due >= request.studyDay.start
                && $0.card.due < request.studyDay.end
        }.sorted(by: dueOrder)
        let introducedNew = eligible.filter {
            $0.card.state == .new && $0.card.introducedAt != nil
        }.sorted(by: creationOrder)
        let unseenPrimary = eligible.filter {
            $0.card.state == .new
                && $0.card.introducedAt == nil
                && $0.card.direction == request.primaryDirection
        }
        let unseenReverse = eligible.filter {
            $0.card.state == .new
                && $0.card.introducedAt == nil
                && $0.card.direction == request.primaryDirection.opposite
        }.sorted(by: creationOrder)

        let limit = max(0, request.newWordsPerDay)
        let primaryRemaining = max(
            0,
            limit - snapshot.introducedPrimaryWordIDs.count
        )
        let reverseRemaining = max(0, limit - snapshot.introducedReverseCount)
        let selectedPrimary = Array(
            roundRobin(unseenPrimary).prefix(primaryRemaining)
        )
        let selectedReverse = Array(unseenReverse.prefix(reverseRemaining))

        return separateSiblings(
            dueLearning
                + overdueReview
                + todayReview
                + introducedNew
                + selectedPrimary
                + selectedReverse
        )
    }

    private func eligibleCandidates(
        _ candidates: [ReviewQueueCandidate],
        preferredCardID: String? = nil
    ) -> [ReviewQueueCandidate] {
        deduplicated(candidates, preferredCardID: preferredCardID).filter {
            !$0.isLocked
                && !$0.word.isLocked
                && !$0.activeSetIDs.isEmpty
        }
    }

    private func filter(
        _ candidates: [ReviewQueueCandidate],
        flashcardSetID: String?
    ) -> [ReviewQueueCandidate] {
        guard let flashcardSetID else { return candidates }
        return candidates.filter {
            $0.activeSetIDs.contains(flashcardSetID)
        }
    }

    private func deduplicated(
        _ candidates: [ReviewQueueCandidate],
        preferredCardID: String?
    ) -> [ReviewQueueCandidate] {
        var byIdentity: [CardIdentity: ReviewQueueCandidate] = [:]
        for candidate in candidates.sorted(by: creationOrder) {
            let identity = CardIdentity(
                wordID: candidate.card.wordID,
                direction: candidate.card.direction
            )
            guard let existing = byIdentity[identity] else {
                byIdentity[identity] = normalized(candidate)
                continue
            }

            let preferred: ReviewQueueCandidate
            if candidate.card.id == preferredCardID {
                preferred = candidate
            } else {
                preferred = existing
            }
            let isLocked = existing.isLocked || candidate.isLocked
                || existing.word.isLocked || candidate.word.isLocked
            var word = preferred.word
            word.isLocked = isLocked
            byIdentity[identity] = ReviewQueueCandidate(
                card: preferred.card,
                word: word,
                activeSetIDs: Array(
                    Set(existing.activeSetIDs + candidate.activeSetIDs)
                ).sorted(),
                isLocked: isLocked
            )
        }
        return byIdentity.values.sorted(by: creationOrder)
    }

    private func normalized(
        _ candidate: ReviewQueueCandidate
    ) -> ReviewQueueCandidate {
        let isLocked = candidate.isLocked || candidate.word.isLocked
        var word = candidate.word
        word.isLocked = isLocked
        return ReviewQueueCandidate(
            card: candidate.card,
            word: word,
            activeSetIDs: Array(Set(candidate.activeSetIDs)).sorted(),
            isLocked: isLocked
        )
    }

    private func roundRobin(
        _ candidates: [ReviewQueueCandidate]
    ) -> [ReviewQueueCandidate] {
        let setIDs = Set(candidates.flatMap(\.activeSetIDs)).sorted()
        var buckets: [String: [ReviewQueueCandidate]] = [:]
        for setID in setIDs {
            buckets[setID] = candidates
                .filter { $0.activeSetIDs.contains(setID) }
                .sorted(by: creationOrder)
        }

        var consumed = Set<CardIdentity>()
        var result: [ReviewQueueCandidate] = []
        while result.count < candidates.count {
            var madeProgress = false
            for setID in setIDs {
                while let first = buckets[setID]?.first {
                    let identity = CardIdentity(
                        wordID: first.card.wordID,
                        direction: first.card.direction
                    )
                    if consumed.contains(identity) {
                        buckets[setID]?.removeFirst()
                        continue
                    }
                    buckets[setID]?.removeFirst()
                    consumed.insert(identity)
                    result.append(first)
                    madeProgress = true
                    break
                }
            }
            if !madeProgress {
                break
            }
        }

        let remaining = candidates
            .filter {
                !consumed.contains(
                    CardIdentity(
                        wordID: $0.card.wordID,
                        direction: $0.card.direction
                    )
                )
            }
            .sorted(by: creationOrder)
        return result + remaining
    }

    private func separateSiblings(
        _ original: [ReviewQueueCandidate]
    ) -> [ReviewQueueCandidate] {
        guard original.count > 2 else { return original }
        var items = original

        while true {
            let previousAdjacentCount = adjacentSiblingCount(items)
            var changed = false
            for index in 0..<(items.count - 1) {
                let wordID = items[index].card.wordID
                guard items[index + 1].card.wordID == wordID else {
                    continue
                }

                if let following = ((index + 2)..<items.count).first(
                    where: { items[$0].card.wordID != wordID }
                ) {
                    var moved = items
                    let sibling = moved.remove(at: index + 1)
                    moved.insert(sibling, at: following)
                    if adjacentSiblingCount(moved) < previousAdjacentCount {
                        items = moved
                        changed = true
                        break
                    }
                } else if let preceding = (0..<index).reversed().first(
                    where: { items[$0].card.wordID != wordID }
                ) {
                    var moved = items
                    let sibling = moved.remove(at: index)
                    moved.insert(sibling, at: preceding)
                    if adjacentSiblingCount(moved) < previousAdjacentCount {
                        items = moved
                        changed = true
                        break
                    }
                }
            }
            if !changed {
                return items
            }
        }
    }

    private func adjacentSiblingCount(
        _ items: [ReviewQueueCandidate]
    ) -> Int {
        guard items.count > 1 else { return 0 }
        return (0..<(items.count - 1)).reduce(into: 0) { count, index in
            if items[index].card.wordID == items[index + 1].card.wordID {
                count += 1
            }
        }
    }

    private func counts(
        for items: [ReviewQueueCandidate]
    ) -> ReviewQueueCounts {
        items.reduce(into: ReviewQueueCounts(new: 0, learning: 0, review: 0)) {
            counts, candidate in
            switch candidate.card.state {
            case .new:
                counts = ReviewQueueCounts(
                    new: counts.new + 1,
                    learning: counts.learning,
                    review: counts.review
                )
            case .learning, .relearning:
                counts = ReviewQueueCounts(
                    new: counts.new,
                    learning: counts.learning + 1,
                    review: counts.review
                )
            case .review:
                counts = ReviewQueueCounts(
                    new: counts.new,
                    learning: counts.learning,
                    review: counts.review + 1
                )
            }
        }
    }

    private func dueOrder(
        _ lhs: ReviewQueueCandidate,
        _ rhs: ReviewQueueCandidate
    ) -> Bool {
        if lhs.card.due != rhs.card.due {
            return lhs.card.due < rhs.card.due
        }
        return creationOrder(lhs, rhs)
    }

    private func creationOrder(
        _ lhs: ReviewQueueCandidate,
        _ rhs: ReviewQueueCandidate
    ) -> Bool {
        if lhs.card.createdAt != rhs.card.createdAt {
            return lhs.card.createdAt < rhs.card.createdAt
        }
        return lhs.card.id < rhs.card.id
    }
}

extension ReviewCardRepository: ReviewQueueRepository {}
