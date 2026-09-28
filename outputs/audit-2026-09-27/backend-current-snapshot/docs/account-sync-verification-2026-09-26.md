# Account sync verification — 2026-09-26

- Backend: full `dotnet test Mavrylo.slnx --no-restore` against a disposable PostgreSQL 18 instance: **300 passed**. Includes delta reads, empty checks without record queries or write locks, atomic conflicts, account isolation, concurrent revision allocation, identical lost-response/deletion retries, reset uploads applying zero changes, migration backfill and rollback.
- iOS: simulator build plus final AccountSyncStoreTests and SharedAccountSessionTests: **52 passed**. Covers cursor transport/persistence, reconciliation, in-flight edits, timing policy and persistent recovery.
- Windows: all portable Node tests: **81 passed**; TypeScript `--noEmit` passed. No Windows build or application launch.
- All repository diff whitespace checks passed. Independent review's reset data-loss findings were fixed and rechecked.
- Real-device iPhone/Windows end-to-end testing and production deployment were not performed.

The wider iOS run executed 522 tests with 4 skipped and 30 assertions failing across the same 20 hosted UI test cases observed before these sync changes. The final focused run includes subsequently added recovery regressions. The wider suite is not claimed to pass.

## Existing iOS hosted UI failures

- `FlashCardAITests.DashboardAddMenuStateTests testAddButtonStaysAtTrailingEdgeBeforeAndAfterExpansion`
- `FlashCardAITests.LibraryViewHostedTests testActiveLibraryMenuOffersRemoveAndNoVisibilityActions`
- `FlashCardAITests.LibraryViewHostedTests testLibrarySearchExpandsFocusesFiltersLocallyAndClosesCleanly`
- `FlashCardAITests.ProductOnboardingPaywallHostedTests testOnboardingPaywallCloseMarksSettingsCompleteAndFinishesOnce`
- `FlashCardAITests.ProductOnboardingPaywallHostedTests testOnboardingPaywallPurchaseSuccessMarksSettingsCompleteAndFinishesOnce`
- `FlashCardAITests.ProductOnboardingPaywallHostedTests testOnboardingPaywallRestoreSuccessMarksSettingsCompleteAndFinishesOnce`
- `FlashCardAITests.ProductPaywallPageHostedTests testBenefitsShowApprovedFreeValuesAndOmitDailyReminders`
- `FlashCardAITests.RootTabShellHostedTests testCreateSetKeepsStartLearningToggleVisibleWithoutWords`
- `FlashCardAITests.RootTabShellHostedTests testCreateSetOpensWithoutKeyboardAndAcceptsInput`
- `FlashCardAITests.RootTabShellHostedTests testCreateSetPlacesStartLearningToggleAbovePopulatedWords`
- `FlashCardAITests.RootTabShellHostedTests testFloatingTabBarRendersLearnLibraryProCreateAndCreateKeepsSelection`
- `FlashCardAITests.RootTabShellHostedTests testLearnSearchExpandsFocusesAndClosesWhileSettingsRemainVisible`
- `FlashCardAITests.RootTabShellHostedTests testProPurchaseSuccessReturnsToLearnWithoutCompletingOnboarding`
- `FlashCardAITests.RootTabShellHostedTests testProRestoreSuccessReturnsToLearnWithoutCompletingOnboarding`
- `FlashCardAITests.RootTabShellHostedTests testProShowsOnboardingPaywallAndCloseReturnsToLearn`
- `FlashCardAITests.RootTabShellHostedTests testSettingsAccountOpensAccountMenu`
- `FlashCardAITests.RootTabShellHostedTests testSettingsDailyGoalMenuSavesSelection`
- `FlashCardAITests.RootTabShellHostedTests testSettingsRemindersOpensItsMenu`
- `FlashCardAITests.RootTabShellHostedTests testZeroCardDashboardShowsGuidanceAndDoesNotConstructAddSpeedDial`
- `FlashCardAITests.SetGridTileBadgeTests testOptionalBadgeIsRenderedAsAccessibleText`
