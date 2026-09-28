from pathlib import Path
import json
root = Path(r'D:\07 Hobby\FlashcardAI\test-results\ios-batch')
out = Path(__file__).parent
delta = out.parent / 'latest-ios-delta'
full = set('''Domain/Entitlement/FreeLimitPolicy.swift
Infrastructure/Purchases/EntitlementStore.swift
Infrastructure/Purchases/StoreKitService.swift
Infrastructure/Purchases/TokenStore.swift
Infrastructure/Purchases/AppAccountTokenStore.swift
Infrastructure/Networking/ServerFeatureFlags.swift
Infrastructure/Networking/APIClient.swift
Infrastructure/Networking/APIConfiguration.swift
Infrastructure/Networking/AccountSessionClient.swift
Infrastructure/DTOs/IapDTOs.swift
Infrastructure/Security/AppAttest/AIIntegrityCoordinator.swift
Infrastructure/Security/AppAttest/AppAttestKeychainStore.swift
Presentation/Features/Settings/ViewModels/AccountProfileModel.swift'''.splitlines())
targeted = set('''App/FlashCardAIApp.swift
App/Root/AppRootModel.swift
App/DependencyInjection/AppComposition.swift
App/Session/AppLaunchState.swift
Infrastructure/Auth/AccountSessionController.swift
Infrastructure/Auth/AccountCredentialStore.swift
Infrastructure/Auth/AccountModels.swift
Infrastructure/Networking/AccountAPIClient.swift
Infrastructure/Networking/APIError.swift
Infrastructure/DTOs/SyncDTOs.swift
Infrastructure/DTOs/DeviceWordDTOs.swift
Infrastructure/DTOs/DeviceAttestDTOs.swift
Infrastructure/DTOs/AIResponses.swift
Infrastructure/Repositories/WordRepository.swift
Infrastructure/Repositories/ReviewCardRepository.swift
Infrastructure/Repositories/PublicFlashcardSetService.swift
Infrastructure/Notifications/StudyReminderScheduler.swift
Infrastructure/Persistence/LocalDatabase.swift
Domain/SpacedRepetition/ReviewQueueService.swift
Shared/Settings/AppSettingsStore.swift
Shared/Settings/FSRSSettingsStore.swift
Presentation/Features/WordSearch/ViewModels/WordSearchViewModel.swift
Presentation/Features/WordSearch/Services/LocalFlashcardImport.swift
Presentation/Features/WordSearch/Views/LocalFlashcardImportView.swift
Presentation/Features/WordSearch/Views/ManualWordEntryView.swift
Presentation/Features/Home/Views/EditWordSheet.swift
Presentation/Features/Home/Views/ScanWordsSheet.swift
Presentation/Features/Home/Views/DashboardView.swift
Presentation/Features/Home/Views/ReviewSessionView.swift
Presentation/Features/Home/Views/PaywallView.swift
Presentation/Features/Home/ViewModels/DashboardViewModel.swift
Presentation/Features/Home/ViewModels/ReviewSessionViewModel.swift
Presentation/Features/Home/ViewModels/SecondaryReviewModel.swift
Presentation/Features/Library/ViewModels/LibraryViewModel.swift
Presentation/Features/Settings/Views/AccountProfileView.swift'''.splitlines())
test_targeted = {'ApplicationBehaviorTests.swift','ReviewCardRepositoryTests.swift','SharedAccountSyncTests.swift','SecondaryReviewTests.swift','ReviewQueueServiceTests.swift','ReviewSessionViewModelTests.swift','AccountSessionTests.swift'}
files = sorted(root.rglob('*.swift'))
lines = ['# Current iOS reviewed inventory', '', 'Baseline: `547dcb32b07c07291811c4a7e386ccb05344e4c2`; sources under `D:/07 Hobby/FlashcardAI/test-results/ios-batch`.', '',
         'Depth: **full** = full critical file review; **targeted** = relevant gate/auth/queue/persistence methods and callers, not every unrelated line; **scan** = whole-source symbol/caller/config inventory only. Large tests/repositories are targeted, not falsely claimed as fully read.', '',
         f'All {len(files)} Swift files are inventoried below. Source scans searched test mode, limits, entitlement, expiry, restore/purchase, token/auth, free/premium/trial/grace/revoked, creation/edit/import and protected API calls. No source was modified.', '', '| Absolute file | Review depth |', '|---|---|']
for p in files:
    rel = p.relative_to(root).as_posix()
    depth = 'full' if rel in full else 'targeted' if rel in targeted or p.name in test_targeted else 'scan'
    lines.append(f'| {p.as_posix()} | {depth} |')
lines += ['', '## Latest main delta', '', 'All 11 files from the verified compare547dcb→9096c1e were inspected in downloaded form; focused-review logic and queue/session tests were reviewed in detail, cosmetic/comment patches were read in comparison. These exact current-content artifacts carry latest-main line numbers:', '']
for p in sorted(delta.rglob('*')):
    if p.is_file() and p.name != 'compare.json': lines.append(f'- `{p.as_posix()}`')
lines += ['', 'Compare metadata: `' + (delta/'compare.json').as_posix() + '`.', '', '## Non-Swift and bridge evidence', '',
          '- Current `CLAUDE.md`, README/project overview; stale no-login prose was treated as historical guidance where actual shared-account implementation differs.',
          '- FlashCardAI.xcodeproj/project.pbxproj: Debug/Release API and AppAttest configuration; shared scheme archive Release and absence of attached StoreKit simulation.',
          '- .github/workflows/ios-validation.yml: exact selected test classes and manual trigger; existing run36255768023 success at baseline reported by parent read-only inspection.',
          '- Backend bridge only: AiProtectionFilter and DeviceWordService authentication/accepted/quota semantics. Parent owns complete backend audit and latest remote-delta tests.',
          '- `current_static_repro.py`: 15/15 executed source/SQL observations; no iOS or StoreKit runtime.', '', '## Known gaps', '',
          'No Xcode/Simulator on Windows, no new Swift execution, no Sandbox purchase/restore/refund/grace interaction, no submitted archive or App Store Connect inspection, no production account mutation. Full UI runtime reachability of unguarded local edit path and entitlement transition timing remains to be checked on device. Dormant storeTextualAnkiWord has no production caller found. Noncritical scan-only files were not manually reviewed line by line. Current CI excludes specific feature-flag/development-limit and latest-focused tests.']
(out/'reviewed-inventory.md').write_text('\n'.join(lines)+'\n', encoding='utf-8')
print(json.dumps({'swift_files': len(files), 'full': sum(p.relative_to(root).as_posix() in full for p in files), 'inventory': str(out/'reviewed-inventory.md')}))
