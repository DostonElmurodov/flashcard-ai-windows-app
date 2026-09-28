# iOS reviewed source inventory

HEAD: f4925b74cae9572cd04ea920ad90ed40b51a920e. All source searches include 49 Swift files. Depth labels are explicit: full = entire control-path source read; targeted = relevant methods and call sites read; search = cross-file control-reference search only. Visual layout was not exhaustively audited.

| Absolute file | Review depth |
|---|---|
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\App\AppDelegate.swift | search |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\App\FlashCardAIApp.swift | full |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\App\Root\AppRootModel.swift | full |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\App\Session\AppLaunchState.swift | full |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Domain\Entitlement\FreeLimitPolicy.swift | full |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Domain\SpacedRepetition\SpacedRepetitionService.swift | search |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\FlashCardAITests\SpacedRepetitionServiceTests.swift | search |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Infrastructure\DTOs\AIResponses.swift | full |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Infrastructure\DTOs\DeviceAttestDTOs.swift | full |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Infrastructure\DTOs\DeviceWordDTOs.swift | full |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Infrastructure\DTOs\IapDTOs.swift | full |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Infrastructure\DTOs\SyncDTOs.swift | search |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Infrastructure\DTOs\WordDashboardRow.swift | search |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Infrastructure\Networking\APIClient.swift | full |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Infrastructure\Networking\APIConfiguration.swift | full |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Infrastructure\Networking\APIError.swift | full |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Infrastructure\Notifications\StudyReminderScheduler.swift | targeted |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Infrastructure\Persistence\LocalDatabase.swift | full |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Infrastructure\Purchases\AppAccountTokenStore.swift | full |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Infrastructure\Purchases\EntitlementStore.swift | full |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Infrastructure\Purchases\StoreKitService.swift | full |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Infrastructure\Purchases\TokenStore.swift | full |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Infrastructure\Repositories\WordRepository.swift | targeted |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Infrastructure\Security\AppAttest\AIIntegrityCoordinator.swift | full |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Infrastructure\Security\AppAttest\AppAttestKeychainStore.swift | full |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Presentation\Features\Home\Components\DashboardWordCard.swift | targeted |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Presentation\Features\Home\ViewModels\DashboardViewModel.swift | full |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Presentation\Features\Home\ViewModels\ReviewSessionViewModel.swift | full |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Presentation\Features\Home\Views\DashboardView.swift | targeted |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Presentation\Features\Home\Views\EditWordSheet.swift | targeted |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Presentation\Features\Home\Views\ManageCategoriesView.swift | search |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Presentation\Features\Home\Views\PaywallView.swift | full |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Presentation\Features\Home\Views\ReviewSessionView.swift | targeted |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Presentation\Features\Home\Views\ScanWordsSheet.swift | targeted |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Presentation\Features\Home\Views\WordHistoryView.swift | targeted |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Presentation\Features\Onboarding\Views\ProductOnboardingView.swift | targeted |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Presentation\Features\Settings\Views\SettingsView.swift | search |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Presentation\Features\WordSearch\Components\WordDetailResultPanel.swift | search |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Presentation\Features\WordSearch\Services\PronunciationSpeaker.swift | search |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Presentation\Features\WordSearch\ViewModels\WordSearchViewModel.swift | full |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Presentation\Features\WordSearch\Views\WordSearchView.swift | targeted |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Shared\Language\LanguageFlagIcon.swift | search |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Shared\Language\LanguageOption.swift | search |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Shared\Logging\AppLog.swift | search |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Shared\Normalization\WordCacheNormalizer.swift | full |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Shared\Settings\AppSettingsStore.swift | search |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Shared\Theme\ChooseThemeSheet.swift | search |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Shared\Theme\ThemeAppearanceControls.swift | search |
| D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Shared\Theme\ThemeManager.swift | search |


Metadata/config read:
- D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Resources\Info.plist
- D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Resources\FlashCardAI.entitlements
- D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Resources\Owl AI.storekit (syntax/products)
- D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\FlashCardAI.xcodeproj\project.pbxproj (build settings/source membership)
- D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\FlashCardAI.xcodeproj\xcshareddata\xcschemes\FlashCardAI.xcscheme
- D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\README.md (targeted documentation/search)
- D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\docs\ai-handoff-owl-ai-no-login.md (reference search only; historical deployment claims not relied on)

Backend bridge read:
- D:\07 Hobby\FlashcardAI\backend\Filters\AiProtectionFilter.cs (full AI request gate)
- D:\07 Hobby\FlashcardAI\backend\src\Mavrylo.Services\Services\DeviceWordService.cs (count/mutation/reservation reference search; full server audit handled by parent)

No iOS runtime tests executed. No source edits. Artifact-only Python characterization successfully executed.
