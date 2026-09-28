import Foundation

@MainActor
struct AppComposition {
    let database: LocalDatabase
    let words: WordRepository
    let publicSets: PublicFlashcardSetService
    let reviewCards: ReviewCardRepository
    let scheduler: FSRSSchedulerService
    let queue: ReviewQueueService
    let fsrsSettings: FSRSSettingsStore
    let appSettings: AppSettingsStore
    let accountSession: AccountSessionController
    let accountProfile: AccountProfileModel
    let apiClient: APIClient
    let secondaryReview: SecondaryReviewService
    let storeKit: StoreKitService
    let now: () -> Date

    static func make(
        databaseURL: URL,
        defaults: UserDefaults,
        calendar: Calendar,
        now: @escaping () -> Date,
        storeKitTransactionUpdates: StoreKitTransactionUpdates =
            StoreKitTransactionUpdateSource.defaultUpdates(),
        suppliedAPI: APIClient? = nil,
        suppliedAccountSession: AccountSessionController? = nil,
        suppliedAccountProfile: AccountProfileModel? = nil,
        suppliedEntitlements: EntitlementStore? = nil,
        suppliedStoreKit: StoreKitService? = nil
    ) throws -> AppComposition {
        // AppSettingsStore canonicalizes both legacy remote values and final
        // raw values synchronously in its initializer. It must run before the
        // SQLite connection is opened so migration sees the real setting.
        let appSettings = AppSettingsStore(defaults: defaults)
        _ = suppliedEntitlements ?? EntitlementStore.shared
        let migrationNow = now()
        let database = try LocalDatabase(
            url: databaseURL,
            primaryDirection: appSettings.reviewDirection,
            now: migrationNow
        )
        let apiClient = suppliedAPI ?? APIClient()
        let storeKit = suppliedStoreKit ?? StoreKitService(
            api: apiClient,
            transactionUpdates: storeKitTransactionUpdates
        )
        let reviewCards = ReviewCardRepository(
            db: database,
            lockedWordIDs: {
                try WordRepository.lockedWordIDs(in: database)
            }
        )
        let words = WordRepository(
            db: database,
            api: apiClient,
            reviewCards: reviewCards,
            now: now
        )
        let publicSets = PublicFlashcardSetService(
            api: apiClient,
            local: words
        )
        let scheduler = FSRSSchedulerService()
        let queue = ReviewQueueService(repository: reviewCards)
        let fsrsSettings = FSRSSettingsStore(
            settings: appSettings,
            configurationProvider: reviewCards,
            calendar: calendar
        )
        return AppComposition(
            database: database,
            words: words,
            publicSets: publicSets,
            reviewCards: reviewCards,
            scheduler: scheduler,
            queue: queue,
            fsrsSettings: fsrsSettings,
            appSettings: appSettings,
            accountSession: suppliedAccountSession ?? AccountSessionController(),
            accountProfile: suppliedAccountProfile ?? .shared,
            apiClient: apiClient,
            secondaryReview: SecondaryReviewService(api: apiClient, workspace: databaseURL, defaults: defaults),
            storeKit: storeKit,
            now: now
        )
    }

    func makeReviewSessionDependencies() -> ReviewSessionDependencies {
        ReviewSessionDependencies(
            repository: reviewCards,
            scheduler: scheduler,
            queue: queue,
            settings: fsrsSettings,
            now: now,
            makeAttemptID: { UUID().uuidString },
            secondaryReview: secondaryReview
        )
    }
}
