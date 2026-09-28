# Current iOS reviewed inventory

Baseline: `547dcb32b07c07291811c4a7e386ccb05344e4c2`; sources under `D:/07 Hobby/FlashcardAI/test-results/ios-batch`.

Depth: **full** = full critical file review; **targeted** = relevant gate/auth/queue/persistence methods and callers, not every unrelated line; **scan** = whole-source symbol/caller/config inventory only. Large tests/repositories are targeted, not falsely claimed as fully read.

All 98 Swift files are inventoried below. Source scans searched test mode, limits, entitlement, expiry, restore/purchase, token/auth, free/premium/trial/grace/revoked, creation/edit/import and protected API calls. No source was modified.

| Absolute file | Review depth |
|---|---|
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/App/AppDelegate.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/App/DependencyInjection/AppComposition.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/App/FlashCardAIApp.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/App/Navigation/RootTabShell.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/App/Root/AppRootModel.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/App/Session/AppLaunchState.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Domain/EnglishIrregularVerbForms.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Domain/Entitlement/FreeLimitPolicy.swift | full |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Domain/SpacedRepetition/FSRSSchedulerService.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Domain/SpacedRepetition/ReviewQueueService.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Domain/SpacedRepetition/ReviewSchedulingModels.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Domain/SpacedRepetition/StudyDay.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/FlashCardAITests/AccountSessionTests.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/FlashCardAITests/AccountViewTests.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/FlashCardAITests/ApplicationBehaviorTests.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/FlashCardAITests/FSRSSchedulerServiceTests.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/FlashCardAITests/LocalDatabaseFSRSMigrationTests.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/FlashCardAITests/ReverseDirectionSettingsViewModelTests.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/FlashCardAITests/ReviewCardRepositoryTests.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/FlashCardAITests/ReviewQueueServiceTests.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/FlashCardAITests/ReviewSessionViewModelTests.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/FlashCardAITests/SecondaryReviewTests.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/FlashCardAITests/SharedAccountSyncTests.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/FlashCardAITests/StudyDayTests.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Auth/AccountCredentialStore.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Auth/AccountModels.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Auth/AccountSessionController.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Auth/GoogleAccountIdentityProvider.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/DTOs/AIResponses.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/DTOs/DeviceAttestDTOs.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/DTOs/DeviceWordDTOs.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/DTOs/IapDTOs.swift | full |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/DTOs/PublicFlashcardSetDTOs.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/DTOs/SyncDTOs.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/DTOs/WordDashboardRow.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Networking/AccountAPIClient.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Networking/AccountSessionClient.swift | full |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Networking/APIClient.swift | full |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Networking/APIConfiguration.swift | full |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Networking/APIError.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Networking/ServerFeatureFlags.swift | full |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Notifications/StudyReminderScheduler.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Persistence/FlashcardSetPublicationSchemaMigrator.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Persistence/FSRSSchemaMigrator.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Persistence/LocalDatabase.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Purchases/AppAccountTokenStore.swift | full |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Purchases/EntitlementStore.swift | full |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Purchases/StoreKitService.swift | full |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Purchases/TokenStore.swift | full |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Repositories/PublicFlashcardSetService.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Repositories/ReviewCardRepository.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Repositories/WordRepository.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Security/AppAttest/AIIntegrityCoordinator.swift | full |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Security/AppAttest/AppAttestKeychainStore.swift | full |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/Account/AccountView.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/Account/AccountViewModel.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/Home/Components/DashboardAddSpeedDial.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/Home/Components/DashboardWordCard.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/Home/Components/SetGridTile.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/Home/ViewModels/DashboardViewModel.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/Home/ViewModels/ReviewSessionViewModel.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/Home/ViewModels/SecondaryReviewModel.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/Home/Views/DashboardView.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/Home/Views/EditWordSheet.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/Home/Views/ManageFlashcardSetsView.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/Home/Views/PaywallView.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/Home/Views/ReviewSessionView.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/Home/Views/ScanWordsSheet.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/Home/Views/SecondaryReviewSurface.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/Home/Views/WordHistoryView.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/Library/ViewModels/LibraryViewModel.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/Library/Views/LibraryView.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/Onboarding/Views/ProductOnboardingView.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/Onboarding/Views/ProductPaywallPage.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/Settings/ViewModels/AccountProfileModel.swift | full |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/Settings/ViewModels/ReverseDirectionSettingsViewModel.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/Settings/Views/AccountProfileView.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/Settings/Views/SettingsView.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/WordSearch/Components/WordDetailResultPanel.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/WordSearch/Services/LocalFlashcardImport.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/WordSearch/Services/PronunciationSpeaker.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/WordSearch/ViewModels/CreateFlashcardSetViewModel.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/WordSearch/ViewModels/WordSearchViewModel.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/WordSearch/Views/CreateFlashcardSetPage.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/WordSearch/Views/LocalFlashcardImportView.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/WordSearch/Views/ManualWordEntryView.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/Features/WordSearch/Views/WordSearchView.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/SharedUI/FloatingTabBar.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Presentation/SharedUI/ProductPalette.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Shared/Language/LanguageFlagIcon.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Shared/Language/LanguageOption.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Shared/Logging/AppLog.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Shared/Normalization/WordCacheNormalizer.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Shared/Settings/AppSettingsStore.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Shared/Settings/FSRSSettingsStore.swift | targeted |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Shared/Theme/ChooseThemeSheet.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Shared/Theme/ThemeAppearanceControls.swift | scan |
| D:/07 Hobby/FlashcardAI/test-results/ios-batch/Shared/Theme/ThemeManager.swift | scan |

## Latest main delta

All 11 files from the verified compare547dcb→9096c1e were inspected in downloaded form; focused-review logic and queue/session tests were reviewed in detail, cosmetic/comment patches were read in comparison. These exact current-content artifacts carry latest-main line numbers:

- `D:/07 Hobby/FlashcardAI/outputs/audit-2026-09-27/latest-ios-delta/App/Navigation/RootTabShell.swift`
- `D:/07 Hobby/FlashcardAI/outputs/audit-2026-09-27/latest-ios-delta/docs/ui-map.md`
- `D:/07 Hobby/FlashcardAI/outputs/audit-2026-09-27/latest-ios-delta/Domain/SpacedRepetition/ReviewQueueService.swift`
- `D:/07 Hobby/FlashcardAI/outputs/audit-2026-09-27/latest-ios-delta/FlashCardAITests/ReviewQueueServiceTests.swift`
- `D:/07 Hobby/FlashcardAI/outputs/audit-2026-09-27/latest-ios-delta/FlashCardAITests/ReviewSessionViewModelTests.swift`
- `D:/07 Hobby/FlashcardAI/outputs/audit-2026-09-27/latest-ios-delta/Presentation/Features/Home/Components/DashboardWordCard.swift`
- `D:/07 Hobby/FlashcardAI/outputs/audit-2026-09-27/latest-ios-delta/Presentation/Features/Home/ViewModels/ReviewSessionViewModel.swift`
- `D:/07 Hobby/FlashcardAI/outputs/audit-2026-09-27/latest-ios-delta/Presentation/Features/Home/Views/DashboardView.swift`
- `D:/07 Hobby/FlashcardAI/outputs/audit-2026-09-27/latest-ios-delta/Presentation/Features/Home/Views/ReviewSessionView.swift`
- `D:/07 Hobby/FlashcardAI/outputs/audit-2026-09-27/latest-ios-delta/Presentation/Features/WordSearch/Views/WordSearchView.swift`
- `D:/07 Hobby/FlashcardAI/outputs/audit-2026-09-27/latest-ios-delta/Presentation/SharedUI/FloatingTabBar.swift`

Compare metadata: `D:/07 Hobby/FlashcardAI/outputs/audit-2026-09-27/latest-ios-delta/compare.json`.

## Non-Swift and bridge evidence

- Current `CLAUDE.md`, README/project overview; stale no-login prose was treated as historical guidance where actual shared-account implementation differs.
- FlashCardAI.xcodeproj/project.pbxproj: Debug/Release API and AppAttest configuration; shared scheme archive Release and absence of attached StoreKit simulation.
- .github/workflows/ios-validation.yml: exact selected test classes and manual trigger; existing run36255768023 success at baseline reported by parent read-only inspection.
- Backend bridge only: AiProtectionFilter and DeviceWordService authentication/accepted/quota semantics. Parent owns complete backend audit and latest remote-delta tests.
- `current_static_repro.py`: 15/15 executed source/SQL observations; no iOS or StoreKit runtime.

## Known gaps

No Xcode/Simulator on Windows, no new Swift execution, no Sandbox purchase/restore/refund/grace interaction, no submitted archive or App Store Connect inspection, no production account mutation. Full UI runtime reachability of unguarded local edit path and entitlement transition timing remains to be checked on device. Dormant storeTextualAnkiWord has no production caller found. Noncritical scan-only files were not manually reviewed line by line. Current CI excludes specific feature-flag/development-limit and latest-focused tests.
