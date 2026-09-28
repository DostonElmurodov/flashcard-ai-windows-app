# Task 11 iOS review package
Base: 58dd623c4d63921e8cdd720741c59a0b86526646
Head: f2a8c97df85ce8df9cc807fbf59bf1b9f74fad26
Workspace: D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/ios

## Commits
f2a8c97 test(ios): await paywall status bar restoration
9e6f52a test(ios): migrate hosted accessibility coverage to XCUI
9b5a7c7 test(ios): preserve search reset and paywall status bar state
8e3db01 Verify Pro onboarding state and repair real UI deletion checks
0a0c21a Check device Release configuration and isolate account fixture appearance
2f76d31 Preserve exact-once onboarding state test and add Release compile gate
263a673 Exercise locked-card deletion and account accessibility in XCUI
97367fd Exercise isolated account and onboarding flows in XCUI
5a5d08b Probe Library, Dashboard Add, and Settings menus in XCUI
f6e2491 Correct paywall promises and stabilize real Create UI probe
5da98f2 Add isolated XCUI root-flow checks before paywall copy change
fa3a12f Preinstall dedicated UI fixture before XCUI launch
e2523c1 Add isolated Debug XCUI search fixture checkpoint

## Summary
 .github/workflows/ios-validation.yml               |   83 +-
 App/DependencyInjection/AppComposition.swift       |   17 +-
 App/FlashCardAIApp.swift                           |   67 +-
 App/Navigation/RootTabShell.swift                  |    1 +
 App/Root/AppRootModel.swift                        |    1 +
 App/UIAutomationFixture.swift                      |  310 +++++
 FlashCardAI.xcodeproj/project.pbxproj              |  110 +-
 .../xcshareddata/xcschemes/FlashCardAI.xcscheme    |   25 +
 FlashCardAITests/AccountViewTests.swift            |  197 +--
 FlashCardAITests/ApplicationBehaviorTests.swift    | 1307 +++-----------------
 FlashCardAIUITests/UILaunchFixtureTests.swift      |  465 +++++++
 Presentation/Features/Account/AccountView.swift    |    4 +
 .../Home/Components/DashboardAddSpeedDial.swift    |    7 +-
 .../Home/Components/DashboardWordCard.swift        |    1 +
 .../Features/Home/Views/DashboardView.swift        |    5 +-
 .../Onboarding/Views/ProductOnboardingView.swift   |   30 +-
 .../Onboarding/Views/ProductPaywallPage.swift      |   43 +-
 .../WordSearch/Views/CreateFlashcardSetPage.swift  |    1 +
 Presentation/SharedUI/FloatingTabBar.swift         |    1 +
 19 files changed, 1277 insertions(+), 1398 deletions(-)

## Full diff
diff --git a/.github/workflows/ios-validation.yml b/.github/workflows/ios-validation.yml
index 7445bb3..d79a695 100644
--- a/.github/workflows/ios-validation.yml
+++ b/.github/workflows/ios-validation.yml
@@ -1,80 +1,151 @@
 name: iOS validation
 on:
   workflow_dispatch:
     inputs:
       test_scope:
         description: Test selection
         type: choice
         default: full
         options:
           - existing
           - paid-access
+          - ui-proof
+          - ui-root
+          - release-compile
           - full
           - baseline-full
 permissions:
   contents: read
 jobs:
   test:
     runs-on: macos-latest
     timeout-minutes: 35
     steps:
       - uses: actions/checkout@v4
         with:
           ref: ${{ inputs.test_scope == 'baseline-full' && '9096c1e3b20fb920726bd471ed4a092783ee3cc7' || github.sha }}
       - name: Record tested source commit
         run: git rev-parse HEAD | tee source-head.txt
       - name: Select available iPhone simulator
         run: |
           xcodebuild -version
           xcrun simctl list devices available -j > /tmp/owl-simulators.json
           python3 - <<'PY'
           import json, os
           data = json.load(open('/tmp/owl-simulators.json'))
           devices = [d for runtime, rows in data['devices'].items() if '.iOS-' in runtime for d in rows if d['name'].startswith('iPhone') and d.get('isAvailable')]
           if not devices:
               raise SystemExit('No available iPhone simulator')
           with open(os.environ['GITHUB_ENV'], 'a') as out:
               out.write('OWL_SIMULATOR=' + devices[0]['udid'] + '\n')
           PY
       - name: Build and test selected scope
         env:
           OWL_TEST_SCOPE: ${{ inputs.test_scope }}
         run: |
           set -o pipefail
           selection=()
+          build_settings=()
           case "$OWL_TEST_SCOPE" in
+            ui-proof)
+              selection=(-only-testing:FlashCardAIUITests/UILaunchFixtureTests/testLearnSearchOpensARealEditableField)
+              build_settings=(OWL_DEBUG_BUNDLE_ID=com.mavrylo.owlai.uitest)
+              ;;
+            ui-root)
+              selection=(-only-testing:FlashCardAIUITests/UILaunchFixtureTests)
+              build_settings=(OWL_DEBUG_BUNDLE_ID=com.mavrylo.owlai.uitest)
+              ;;
+            release-compile) ;;
             paid-access)
               selection=(
                 -only-testing:FlashCardAITests/EntitlementMutationBoundaryTests
                 -only-testing:FlashCardAITests/SharedAccountSessionTests
                 -only-testing:FlashCardAITests/ReviewSessionViewModelTests
               )
               ;;
-            full|baseline-full) ;;
+            full)
+              build_settings=(OWL_DEBUG_BUNDLE_ID=com.mavrylo.owlai.uitest)
+              ;;
+            baseline-full) ;;
             existing)
               selection=(
                 -only-testing:FlashCardAITests/SecondaryReviewTests
                 -only-testing:FlashCardAITests/WordSearchViewModelTests
                 -only-testing:FlashCardAITests/ReviewCardRepositoryTests
                 -only-testing:FlashCardAITests/WordRepositoryV10OwnershipTests
                 -only-testing:FlashCardAITests/WordRepositoryPublicSetTests
                 -only-testing:FlashCardAITests/AccountSyncStoreTests
                 -only-testing:FlashCardAITests/SharedAccountSessionTests
               )
               ;;
             *) echo "Unknown test scope" >&2; exit 2 ;;
           esac
-          xcodebuild test -project FlashCardAI.xcodeproj -scheme FlashCardAI \
-            -destination "platform=iOS Simulator,id=$OWL_SIMULATOR" \
-            -parallel-testing-enabled NO \
-            "${selection[@]}" \
-            -resultBundlePath TestResults.xcresult CODE_SIGNING_ALLOWED=NO 2>&1 | tee xcode-test.log
+          common=(-project FlashCardAI.xcodeproj -scheme FlashCardAI
+            -destination "platform=iOS Simulator,id=$OWL_SIMULATOR"
+            -parallel-testing-enabled NO
+            "${build_settings[@]}" CODE_SIGNING_ALLOWED=NO)
+          if [[ "$OWL_TEST_SCOPE" == release-compile ]]; then
+            derived_data="$RUNNER_TEMP/owl-release-derived-data"
+            xcodebuild build -project FlashCardAI.xcodeproj -scheme FlashCardAI \
+              -configuration Release -destination 'generic/platform=iOS' \
+              -derivedDataPath "$derived_data" CODE_SIGNING_ALLOWED=NO 2>&1 | tee xcode-build.log
+            xcodebuild -showBuildSettings -json -project FlashCardAI.xcodeproj -scheme FlashCardAI \
+              -configuration Release -destination 'generic/platform=iOS' CODE_SIGNING_ALLOWED=NO \
+              > "$RUNNER_TEMP/owl-release-settings.json"
+            python3 - "$derived_data" "$RUNNER_TEMP/owl-release-settings.json" <<'PY'
+          import json, plistlib, sys
+          from pathlib import Path
+
+          def require(condition, message):
+              if not condition:
+                  raise SystemExit(message)
+
+          derived = Path(sys.argv[1])
+          settings = next(row['buildSettings'] for row in json.loads(Path(sys.argv[2]).read_text())
+                          if row['target'] == 'FlashCardAI')
+          require(settings['PRODUCT_BUNDLE_IDENTIFIER'] == 'com.mavrylo.owlai', 'Unexpected Release bundle setting')
+          require(settings['API_BASE_URL'] == 'https://api.mavrylo.com', 'Unexpected Release API URL setting')
+          require(settings['APP_ATTEST_ENVIRONMENT'] == 'production', 'Unexpected Release App Attest setting')
+          require(settings['CODE_SIGN_ENTITLEMENTS'] == 'Resources/FlashCardAI.entitlements', 'Unexpected entitlements mapping')
+          require('DEBUG' not in settings.get('SWIFT_ACTIVE_COMPILATION_CONDITIONS', '').split(), 'DEBUG enabled in Release')
+          entitlement_source = plistlib.loads(Path(settings['CODE_SIGN_ENTITLEMENTS']).read_bytes())
+          require(entitlement_source['com.apple.developer.devicecheck.appattest-environment'] == '$(APP_ATTEST_ENVIRONMENT)',
+                  'Unexpected App Attest entitlement source')
+          app = derived / 'Build/Products/Release-iphoneos/FlashCardAI.app'
+          info = plistlib.loads((app / 'Info.plist').read_bytes())
+          require(info['CFBundleIdentifier'] == 'com.mavrylo.owlai', 'Unexpected built Release bundle ID')
+          require(info['APIBaseURL'] == 'https://api.mavrylo.com', 'Unexpected built Release API URL')
+          executable = (app / info['CFBundleExecutable']).read_bytes()
+          for marker in (b'-owl-ui-fixture', b'ui-fixture.invalid', b'ui-fixture-account',
+                         b'learner@example.test', b'correct horse'):
+              require(marker not in executable, 'Debug UI fixture marker found in Release executable')
+          print('Unsigned iOS device Release compile and bounded configuration checks passed')
+          PY
+          elif [[ "$OWL_TEST_SCOPE" == ui-proof || "$OWL_TEST_SCOPE" == ui-root || "$OWL_TEST_SCOPE" == full ]]; then
+            derived_data="$RUNNER_TEMP/owl-ui-derived-data"
+            xcodebuild build-for-testing "${common[@]}" -derivedDataPath "$derived_data" 2>&1 | tee xcode-build.log
+            echo "Simulator boot preflight: $(date -u +%Y-%m-%dT%H:%M:%SZ)" | tee simulator-preflight.log
+            xcrun simctl bootstatus "$OWL_SIMULATOR" -b 2>&1 | tee -a simulator-preflight.log
+            app_path="$derived_data/Build/Products/Debug-iphonesimulator/FlashCardAI.app"
+            bundle_id=$(/usr/libexec/PlistBuddy -c 'Print CFBundleIdentifier' "$app_path/Info.plist")
+            [[ "$bundle_id" == com.mavrylo.owlai.uitest ]] || { echo "Unexpected UI fixture bundle: $bundle_id" >&2; exit 1; }
+            echo "Dedicated app install: $(date -u +%Y-%m-%dT%H:%M:%SZ)" | tee -a simulator-preflight.log
+            xcrun simctl install "$OWL_SIMULATOR" "$app_path" 2>&1 | tee -a simulator-preflight.log
+            xcrun simctl get_app_container "$OWL_SIMULATOR" "$bundle_id" app 2>&1 | tee -a simulator-preflight.log
+            echo "XCTest handoff: $(date -u +%Y-%m-%dT%H:%M:%SZ)" | tee -a simulator-preflight.log
+            xcodebuild test-without-building "${common[@]}" "${selection[@]}" -derivedDataPath "$derived_data" \
+              -resultBundlePath TestResults.xcresult 2>&1 | tee xcode-test.log
+          else
+            xcodebuild test "${common[@]}" "${selection[@]}" -resultBundlePath TestResults.xcresult 2>&1 | tee xcode-test.log
+          fi
       - uses: actions/upload-artifact@v4
         if: always()
         with:
           name: ios-validation
           path: |
             xcode-test.log
+            xcode-build.log
+            simulator-preflight.log
             source-head.txt
             TestResults.xcresult
           retention-days: 7
diff --git a/App/DependencyInjection/AppComposition.swift b/App/DependencyInjection/AppComposition.swift
index f5358e5..a37fb49 100644
--- a/App/DependencyInjection/AppComposition.swift
+++ b/App/DependencyInjection/AppComposition.swift
@@ -1,94 +1,101 @@
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
+    let accountProfile: AccountProfileModel
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
-            StoreKitTransactionUpdateSource.defaultUpdates()
+            StoreKitTransactionUpdateSource.defaultUpdates(),
+        suppliedAPI: APIClient? = nil,
+        suppliedAccountSession: AccountSessionController? = nil,
+        suppliedAccountProfile: AccountProfileModel? = nil,
+        suppliedEntitlements: EntitlementStore? = nil,
+        suppliedStoreKit: StoreKitService? = nil
     ) throws -> AppComposition {
         // AppSettingsStore canonicalizes both legacy remote values and final
         // raw values synchronously in its initializer. It must run before the
         // SQLite connection is opened so migration sees the real setting.
         let appSettings = AppSettingsStore(defaults: defaults)
-        _ = EntitlementStore.shared
+        _ = suppliedEntitlements ?? EntitlementStore.shared
         let migrationNow = now()
         let database = try LocalDatabase(
             url: databaseURL,
             primaryDirection: appSettings.reviewDirection,
             now: migrationNow
         )
-        let apiClient = APIClient()
-        let storeKit = StoreKitService(
+        let apiClient = suppliedAPI ?? APIClient()
+        let storeKit = suppliedStoreKit ?? StoreKitService(
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
-            accountSession: AccountSessionController(),
+            accountSession: suppliedAccountSession ?? AccountSessionController(),
+            accountProfile: suppliedAccountProfile ?? .shared,
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
diff --git a/App/FlashCardAIApp.swift b/App/FlashCardAIApp.swift
index 4b89043..b6538ef 100644
--- a/App/FlashCardAIApp.swift
+++ b/App/FlashCardAIApp.swift
@@ -1,147 +1,196 @@
 import SwiftUI
 
 /// Pushes the resolved `ColorScheme` (after `preferredColorScheme`) into `ThemeManager` so accent palettes match system mode when appearance is `.system`.
 private struct EffectiveColorSchemeBridge: View {
     @Environment(\.colorScheme) private var colorScheme
     @ObservedObject var themeManager: ThemeManager
     @ObservedObject var appSettings: AppSettingsStore
 
     var body: some View {
         Color.clear
             .frame(width: 0, height: 0)
             .accessibilityHidden(true)
             .onAppear {
                 themeManager.syncEffectiveColorScheme(colorScheme)
                 appSettings.syncColorScheme(colorScheme)
             }
             .onChange(of: colorScheme) { _, newValue in
                 themeManager.syncEffectiveColorScheme(newValue)
                 appSettings.syncColorScheme(newValue)
             }
     }
 }
 
+private struct UIAutomationDynamicType: ViewModifier {
+    @ViewBuilder
+    func body(content: Content) -> some View {
+        #if DEBUG
+        if UIAutomationFixture.isLargeDarkAccount {
+            content.environment(\.dynamicTypeSize, .accessibility5)
+        } else {
+            content
+        }
+        #else
+        content
+        #endif
+    }
+}
+
 @main
 struct FlashCardAIApp: App {
     @UIApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
-    @StateObject private var root = AppRootModel()
-    @StateObject private var themeManager = ThemeManager()
-    @StateObject private var notificationRouter = StudyNotificationRouter.shared
+    @StateObject private var root: AppRootModel
+    @StateObject private var themeManager: ThemeManager
+    @StateObject private var notificationRouter: StudyNotificationRouter
     @State private var hasRefreshedFeatureFlags = false
     @Environment(\.scenePhase) private var scenePhase
 
+    init() {
+        #if DEBUG
+        if let composition = UIAutomationFixture.makeIfRequested() {
+            let theme = ThemeManager()
+            theme.appearance = UIAutomationFixture.isLargeDarkAccount ? .dark : .light
+            _themeManager = StateObject(wrappedValue: theme)
+            _notificationRouter = StateObject(wrappedValue: .shared)
+            _root = StateObject(wrappedValue: AppRootModel(
+                composition: composition,
+                calendar: { Calendar(identifier: .gregorian) },
+                now: Date.init,
+                notificationCenter: .default,
+                externalRefreshEnabled: false
+            ))
+            _hasRefreshedFeatureFlags = State(initialValue: true)
+            return
+        }
+        #endif
+        _themeManager = StateObject(wrappedValue: ThemeManager())
+        _notificationRouter = StateObject(wrappedValue: .shared)
+        _root = StateObject(wrappedValue: AppRootModel())
+    }
+
     var body: some Scene {
         WindowGroup {
             Group {
                 if !hasRefreshedFeatureFlags {
                     ProgressView()
                 } else if root.bootstrapError != nil {
                     Text("Owl AI couldn’t open your local data. Restart the app and try again.")
                         .multilineTextAlignment(.center)
                         .padding()
                 } else if let composition = root.composition {
                     Group {
-                        if root.showProductOnboarding {
-                            ProductOnboardingView {
+                        if root.showProductOnboarding && !UIAutomationFixture.showTabsOnce {
+                            ProductOnboardingView(
+                                initialPage: UIAutomationFixture.onboardingInitialPage,
+                                paywallActions: UIAutomationFixture.paywallActions,
+                                paywallAccount: composition.accountProfile
+                            ) {
                                 root.completeProductOnboarding()
                             }
                         } else {
                             NavigationStack {
                                 RootTabShell(
                                     composition: composition,
-                                    openSettingsOnAppear: $root.shouldOpenInitialSettings
+                                    openSettingsOnAppear: $root.shouldOpenInitialSettings,
+                                    paywallActions: UIAutomationFixture.paywallActions
                                 )
+                                .modifier(UIAutomationDynamicType())
                             }
                         }
                     }
                     .id(composition.database.pathURL)
                     .environmentObject(themeManager)
                     .environmentObject(composition.appSettings)
                     .environmentObject(composition.storeKit)
                     .environmentObject(composition.accountSession)
                     .environmentObject(notificationRouter)
                     .preferredColorScheme(themeManager.preferredColorScheme)
                     .background {
                         EffectiveColorSchemeBridge(
                             themeManager: themeManager,
                             appSettings: composition.appSettings
                         )
                     }
                     .onAppear {
                         configureProfileBackedSettings(composition)
                     }
                     .onReceive(NotificationCenter.default.publisher(for: .owlAccountSessionChanged)) { _ in
                         guard !AppRootModel.isHostedUnitTest else { return }
                         Task { await root.accountScopeChanged(); await root.composition?.accountSession.restore(); await AccountProfileModel.shared.refresh() }
                     }
                     .task {
                         guard !AppRootModel.isHostedUnitTest else { return }
                         await composition.accountSession.restore()
                     }
                     .task {
+                        guard !AppRootModel.isHostedUnitTest else { return }
                         configureProfileBackedSettings(composition)
                         StudyReminderScheduler.shared.register(
                             composition: composition
                         )
                         await StudyReminderScheduler.shared.apply(
                             using: composition.appSettings
                         )
                     }
                     .onChange(of: scenePhase) { _, newPhase in
+                        guard !AppRootModel.isHostedUnitTest else { return }
                         if newPhase == .active {
                             root.onAppBecameActive()
                             StudyReminderScheduler.shared.register(
                                 composition: composition
                             )
                             Task {
                                 await StudyReminderScheduler.shared.apply(
                                     using: composition.appSettings
                                 )
                             }
                         }
                     }
                 } else {
                     ProgressView()
                 }
             }
             .onChange(of: scenePhase, initial: true) { _, phase in
-                guard ProcessInfo.processInfo.environment["XCTestConfigurationFilePath"] == nil else { return }
+                guard !AppRootModel.isHostedUnitTest else { return }
                 switch phase {
                 case .active: AccountSyncCoordinator.shared.setPhase(.active)
                 case .inactive: AccountSyncCoordinator.shared.setPhase(.inactive)
                 case .background: AccountSyncCoordinator.shared.setPhase(.background)
                 @unknown default: AccountSyncCoordinator.shared.setPhase(.inactive)
                 }
             }
             .task(id: scenePhase) {
                 // Hosted unit tests provide their own configuration responses.
-                guard ProcessInfo.processInfo.environment["XCTestConfigurationFilePath"] == nil else {
+                guard !AppRootModel.isHostedUnitTest else {
                     hasRefreshedFeatureFlags = true
                     return
                 }
                 if scenePhase == .background {
                     ServerFeatureFlags.shared.disable()
                     return
                 }
                 guard scenePhase == .active else { return }
                 await ServerFeatureFlags.shared.refresh()
                 guard !Task.isCancelled else { return }
                 hasRefreshedFeatureFlags = true
                 while !Task.isCancelled {
                     do { try await Task.sleep(for: .seconds(30)) }
                     catch { return }
                     await ServerFeatureFlags.shared.refresh()
                 }
             }
-            .onOpenURL { GoogleAccountIdentityProvider.handle($0) }
+            .onOpenURL {
+                guard !AppRootModel.isHostedUnitTest else { return }
+                GoogleAccountIdentityProvider.handle($0)
+            }
         }
     }
 
     private func configureProfileBackedSettings(
         _ composition: AppComposition
     ) {
         guard !AppRootModel.isHostedUnitTest else { return }
         composition.appSettings.configure(api: composition.apiClient)
         themeManager.configure(api: composition.apiClient)
     }
 }
diff --git a/App/Navigation/RootTabShell.swift b/App/Navigation/RootTabShell.swift
index 6865469..8942a04 100644
--- a/App/Navigation/RootTabShell.swift
+++ b/App/Navigation/RootTabShell.swift
@@ -23,68 +23,69 @@ struct RootTabShell: View {
     init(
         composition: AppComposition,
         openSettingsOnAppear: Binding<Bool> = .constant(false),
         paywallActions: ProductPaywallActions? = nil
     ) {
         self.composition = composition
         _openSettingsOnAppear = openSettingsOnAppear
         paywallActionsOverride = paywallActions
     }
 
     private var repository: WordRepository { composition.words }
 
     var body: some View {
         ZStack(alignment: .bottom) {
             appSettings.currentBackground
                 .ignoresSafeArea()
 
             // Selected tab content.
             Group {
                 switch selection.destination {
                 case .learningDashboard:
                     DashboardView(
                         repository: repository,
                         reviewDependencies: composition.makeReviewSessionDependencies(),
                         openSettingsOnAppear: $openSettingsOnAppear,
                         isAddFlowBlockingRootNavigation:
                             $dashboardAddFlowBlocksNavigation
                     )
                 case .library:
                     LibraryView(
                         repository: repository,
                         publicSets: composition.publicSets,
                         secondaryReview: composition.secondaryReview
                     )
                 case .pro:
                     ProductPaywallPage(
                         bottomContentInset: 96,
                         loadsProductsOnAppear: true,
                         actions: paywallActionsOverride
                             ?? .live(storeKit: storeKit),
+                        account: composition.accountProfile,
                         onClose: { selection = .learn },
                         onPurchaseCompleted: { selection = .learn }
                     )
                 }
             }
 
             // Floating tab bar.
             FloatingTabBar(
                 selection: $selection,
                 onAdd: { showCreatePage = true }
             )
             .padding(.horizontal, 14)
             .padding(.bottom, 2)
             .allowsHitTesting(!dashboardAddFlowBlocksNavigation)
             .accessibilityHidden(dashboardAddFlowBlocksNavigation)
         }
         .navigationDestination(isPresented: $showCreatePage) {
             CreateFlashcardSetPage(
                 repository: repository,
                 publicSets: composition.publicSets,
                 secondaryReview: composition.secondaryReview
             )
                 .environmentObject(theme)
                 .environmentObject(appSettings)
         }
         .toolbar(.hidden, for: .navigationBar)
     }
 }
diff --git a/App/Root/AppRootModel.swift b/App/Root/AppRootModel.swift
index 79efe34..978057b 100644
--- a/App/Root/AppRootModel.swift
+++ b/App/Root/AppRootModel.swift
@@ -1,53 +1,54 @@
 import Foundation
 
 extension Notification.Name {
     static let reviewQueueRefreshRequested = Notification.Name(
         "FlashCardAI.reviewQueueRefreshRequested"
     )
 }
 
 @MainActor
 final class AppRootModel: ObservableObject {
     static var isHostedUnitTest: Bool {
         #if DEBUG
         ProcessInfo.processInfo.environment["XCTestConfigurationFilePath"] != nil
+            || UIAutomationFixture.isActive
         #else
         false
         #endif
     }
 
     private struct StudyRefreshFingerprint: Equatable {
         struct SetState: Equatable {
             let id: String
             let isActive: Bool
             let reverseDirectionEnabled: Bool
         }
 
         struct Membership: Equatable {
             let wordID: String
             let setIDs: [String]
         }
 
         let studyDayKey: TimeInterval
         let studyDayEnd: TimeInterval
         let timeZoneIdentifier: String
         let timeZoneOffset: Int
         let reminderFromMinutes: Int
         let sets: [SetState]
         let memberships: [Membership]
         let nativeLanguage: String
         let learningLanguage: String
         let primaryDirection: ReviewDirection
         let newWordsPerDay: Int
     }
 
     @Published private(set) var composition: AppComposition?
     @Published var bootstrapError: String?
     @Published private(set) var reviewRefreshGeneration = 0
     @Published private(set) var reviewRefreshError: String?
     /// Multi-slide product tour. Owl AI v1 is no-login, so it goes straight to the app afterward.
     @Published var showProductOnboarding: Bool
     @Published var shouldOpenInitialSettings: Bool
 
     private var guestURL = LocalDatabase.defaultDatabaseURL
     private var accountDefaults = UserDefaults.standard
diff --git a/App/UIAutomationFixture.swift b/App/UIAutomationFixture.swift
new file mode 100644
index 0000000..bd083c4
--- /dev/null
+++ b/App/UIAutomationFixture.swift
@@ -0,0 +1,310 @@
+import Foundation
+
+#if DEBUG
+import StoreKit
+import UIKit
+import SQLite3
+
+/// A UI test may use this path only in its separate simulator app container.
+/// The normal app never opens a fixture database or installs fixture dependencies.
+@MainActor
+enum UIAutomationFixture {
+    private static let argument = "-owl-ui-fixture"
+    private static let bundleID = "com.mavrylo.owlai.uitest"
+
+    static var isActive: Bool {
+        ProcessInfo.processInfo.arguments.contains(argument)
+    }
+
+    static var accountIdentity: (any AccountIdentityProviding)? {
+        isActive ? OfflineAccountIdentity() : nil
+    }
+
+    static var onboardingInitialPage: Int {
+        isActive && requestedFixture?.hasPrefix("onboarding-") == true ? 3 : 0
+    }
+
+    static var isLargeDarkAccount: Bool {
+        isActive && requestedFixture == "account-large-dark"
+    }
+
+    static var showTabsOnce: Bool {
+        isActive && requestedFixture == "pro-incomplete"
+            && ProcessInfo.processInfo.arguments.contains("-owl-ui-show-tabs-once")
+    }
+
+    static var paywallActions: ProductPaywallActions? {
+        guard isActive else { return nil }
+        let fixture = requestedFixture
+        return ProductPaywallActions(
+            isBusy: false,
+            hasProducts: true,
+            loadProducts: {},
+            purchase: { _ in fixture == "purchase-success" || fixture == "onboarding-purchase" || fixture == "pro-incomplete"
+                ? .success : .failure("Purchases are unavailable in the UI fixture.") },
+            restore: { fixture == "restore-success" || fixture == "onboarding-restore" || fixture == "pro-incomplete"
+                ? .success : .failure("Purchases are unavailable in the UI fixture.") }
+        )
+    }
+
+    private static var requestedFixture: String? {
+        let arguments = ProcessInfo.processInfo.arguments
+        guard let index = arguments.firstIndex(of: argument), arguments.indices.contains(index + 1) else { return nil }
+        return arguments[index + 1]
+    }
+
+    static func makeIfRequested() -> AppComposition? {
+        guard isActive else { return nil }
+        precondition(Bundle.main.bundleIdentifier == bundleID,
+                     "UI fixture requires its separate Debug app bundle ID")
+        guard let fixture = requestedFixture else { preconditionFailure("Unknown UI fixture") }
+        precondition(["empty-library", "populated-set", "active-pending-set", "account-success", "account-large-dark", "purchase-success", "restore-success", "onboarding-close", "onboarding-purchase", "onboarding-restore", "locked-rejected", "locked-eleventh", "pro-incomplete"].contains(fixture),
+                     "Unknown UI fixture")
+
+        do {
+            let isPersistent = fixture == "locked-rejected" || fixture == "locked-eleventh" || fixture == "pro-incomplete"
+            let identifier: String
+            if isPersistent {
+                let arguments = ProcessInfo.processInfo.arguments
+                guard let index = arguments.firstIndex(of: "-owl-ui-fixture-id"),
+                      arguments.indices.contains(index + 1),
+                      let parsed = UUID(uuidString: arguments[index + 1]) else {
+                    preconditionFailure("Persistent UI fixture requires a UUID identity")
+                }
+                identifier = parsed.uuidString
+            } else {
+                identifier = UUID().uuidString
+            }
+            let defaults = UserDefaults(suiteName: "\(bundleID).\(identifier)")!
+            let root = isPersistent
+                ? FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
+                : FileManager.default.temporaryDirectory
+            let directory = root
+                .appendingPathComponent("owl-ui-fixture-\(identifier)", isDirectory: true)
+            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
+            let databaseURL = directory.appendingPathComponent("library.sqlite3")
+            let shouldSeed = !FileManager.default.fileExists(atPath: databaseURL.path)
+            precondition(!showTabsOnce || shouldSeed, "Pro UI fixture may bypass onboarding only on first launch")
+
+            // AppLaunchState and ThemeManager use standard defaults. The separate
+            // bundle ID keeps their writes outside the normal app's defaults.
+            let isOnboarding = fixture.hasPrefix("onboarding-")
+            let isProIncomplete = fixture == "pro-incomplete"
+            if isProIncomplete && !shouldSeed {
+                precondition(
+                    !AppLaunchState.hasCompletedProductOnboarding
+                        && !AppLaunchState.hasCompletedSettingsOnboarding,
+                    "Pro UI action completed onboarding unexpectedly"
+                )
+            } else {
+                AppLaunchState.hasCompletedProductOnboarding = !isOnboarding && !isProIncomplete
+                AppLaunchState.hasCompletedSettingsOnboarding = !isOnboarding && !isProIncomplete
+            }
+            EntitlementStore.shared.update(.free)
+
+            let configuration = URLSessionConfiguration.ephemeral
+            configuration.protocolClasses = [OfflineURLProtocol.self]
+            let offlineSession = URLSession(configuration: configuration)
+            let offlineURL = URL(string: "https://ui-fixture.invalid")!
+            let entitlements = EntitlementStore(defaults: defaults)
+            let accountClient = AccountSessionClient(
+                session: offlineSession,
+                baseURL: offlineURL,
+                storage: EmptyAccountStorage(),
+                deviceToken: { nil },
+                integrityHeaders: { _, _, _ in [:] }
+            )
+            let account = AccountProfileModel(client: accountClient, entitlements: entitlements)
+            let api = APIClient(
+                session: offlineSession,
+                entitlementStore: entitlements,
+                baseURL: offlineURL,
+                deviceTokenProvider: { nil },
+                accountTokenProvider: { nil },
+                accountTokenRecovery: { _ in nil },
+                accountSessionGenerationProvider: { await accountClient.sessionGeneration() },
+                integrityHeadersProvider: { _, _, _ in [:] }
+            )
+            let storeKit = StoreKitService(
+                api: api,
+                transactionUpdates: EmptyTransactionUpdates(),
+                account: account,
+                entitlements: entitlements,
+                accountProfile: { nil },
+                persistToken: { _, _ in }
+            )
+            let accountAPI: any AccountAPIProviding
+            if fixture == "account-success" || fixture == "account-large-dark" {
+                accountAPI = OfflineAccountAPI()
+            } else {
+                accountAPI = AccountAPIClient(session: offlineSession, baseURL: offlineURL)
+            }
+            let composition = try AppComposition.make(
+                databaseURL: databaseURL,
+                defaults: defaults,
+                calendar: Calendar(identifier: .gregorian),
+                now: Date.init,
+                storeKitTransactionUpdates: EmptyTransactionUpdates(),
+                suppliedAPI: api,
+                suppliedAccountSession: AccountSessionController(
+                    api: accountAPI,
+                    credentials: EmptyAccountUIStorage()
+                ),
+                suppliedAccountProfile: account,
+                suppliedEntitlements: entitlements,
+                suppliedStoreKit: storeKit
+            )
+            if fixture == "populated-set" || fixture == "active-pending-set" {
+                let setID = try composition.words.addFlashcardSet(displayName: "Travel")
+                let seededAt = Date(timeIntervalSince1970: 1_754_200_000)
+                try composition.words.storeTextualAnkiWord(
+                    TextualAnkiWord(
+                        id: "ui-fixture-ticket",
+                        normalizedWord: "ticket",
+                        displayWord: "Ticket",
+                        nativeLanguage: composition.appSettings.nativeLanguageCode,
+                        learningLanguage: composition.appSettings.learningLanguageCode,
+                        wordDetailJSON: #"{"translations":["Billete"],"translation":"Billete"}"#,
+                        translation: "Billete",
+                        pronunciation: nil,
+                        partOfSpeech: "noun",
+                        userNotes: nil,
+                        externalCollectionID: "ui-fixture",
+                        externalNoteID: "ticket-1",
+                        importBatchID: "fixture",
+                        sourceMetadataJSON: #"{"model":"Basic"}"#,
+                        tagsJSON: nil,
+                        createdAt: seededAt,
+                        updatedAt: seededAt,
+                        templates: []
+                    ),
+                    in: setID
+                )
+                if fixture == "active-pending-set" {
+                    try composition.words.setFlashcardSetActive(id: setID, isActive: true)
+                    try composition.words.setFlashcardSetPublicationStatus(id: setID, status: .pending)
+                }
+            }
+            if (fixture == "locked-rejected" || fixture == "locked-eleventh") && shouldSeed {
+                try seedLockedCards(in: composition, fixture: fixture)
+            }
+            return composition
+        } catch {
+            fatalError("UI fixture could not start: \(error)")
+        }
+    }
+
+    private static func seedLockedCards(in composition: AppComposition, fixture: String) throws {
+        try composition.database.withDB { handle in
+            let count = fixture == "locked-eleventh" ? 11 : 1
+            for index in 1...count {
+                let isRejected = fixture == "locked-rejected"
+                let id = isRejected ? "ui-rejected" : String(format: "word-%02d", index)
+                let display = isRejected ? "Rejected retained" : "Saved \(index)"
+                let normalized = isRejected ? "rejected retained" : "saved-\(index)"
+                let native = index == 11 || isRejected ? "es" : "ru"
+                let metadata = isRejected
+                    ? #"'{"access_denial":"Server did not accept this save. Your text is preserved. Check access, then retry."}'"#
+                    : "NULL"
+                let sql = """
+                INSERT INTO words (id, normalized_word, display_word, native_language, learning_language,
+                    word_detail_json, translation, created_at, updated_at, source, source_metadata_json)
+                VALUES ('\(id)', '\(normalized)', '\(display)', '\(native)', 'en-us',
+                    '{"translations":["Saved translation"],"translation":"Saved translation"}',
+                    'Saved translation', 1700000000, 1700000000, 'manual', \(metadata));
+                """
+                guard sqlite3_exec(handle, sql, nil, nil, nil) == SQLITE_OK else {
+                    throw LocalDatabaseError.execFailed(String(cString: sqlite3_errmsg(handle)))
+                }
+            }
+        }
+        let setID = try composition.words.addFlashcardSet(displayName: "Travel", isActive: true)
+        let target = fixture == "locked-eleventh" ? "word-11" : "ui-rejected"
+        try composition.words.replaceFlashcardSetMemberships(wordID: target, flashcardSetIDs: [setID])
+    }
+}
+
+private final class OfflineURLProtocol: URLProtocol {
+    override class func canInit(with request: URLRequest) -> Bool { true }
+    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }
+    override func startLoading() {
+        client?.urlProtocol(self, didFailWithError: URLError(.notConnectedToInternet))
+    }
+    override func stopLoading() {}
+}
+
+@MainActor
+private struct OfflineAccountIdentity: AccountIdentityProviding {
+    let isConfigured = true
+    func signIn(presenting: UIViewController) async throws -> String { throw CancellationError() }
+    func signOut() {}
+}
+
+private actor OfflineAccountAPI: AccountAPIProviding {
+    private let profileValue = AccountProfile(
+        id: "ui-fixture-account", email: "learner@example.test", displayName: nil, provider: "email"
+    )
+
+    func register(email: String, password: String, confirmPassword: String) async throws -> AccountSession {
+        guard email == profileValue.email, password == "correct horse", confirmPassword == password else {
+            throw AccountAPIError.invalidCredentials
+        }
+        return session()
+    }
+
+    func signIn(email: String, password: String) async throws -> AccountSession {
+        guard email == profileValue.email, password == "correct horse" else {
+            throw AccountAPIError.invalidCredentials
+        }
+        return session()
+    }
+
+    func signIn(idToken: String) async throws -> AccountSession { throw CancellationError() }
+    func refresh(refreshToken: String) async throws -> AccountSession { throw AccountAPIError.invalidCredentials }
+    func signOut(refreshToken: String) async throws {}
+    func profile(accessToken: String) async throws -> AccountProfile {
+        guard accessToken == "ui-fixture-access" else { throw AccountAPIError.invalidCredentials }
+        return profileValue
+    }
+    func deleteAccount(accessToken: String) async throws {}
+
+    private func session() -> AccountSession {
+        AccountSession(
+            accessToken: "ui-fixture-access",
+            accessTokenExpiresAt: Date().addingTimeInterval(3600),
+            refreshToken: "ui-fixture-refresh",
+            profile: profileValue
+        )
+    }
+}
+
+private final class EmptyAccountStorage: AccountCredentialStorage {
+    func load() throws -> AccountSessionDTO? { nil }
+    func save(_ session: AccountSessionDTO) throws {}
+    func clear() throws {}
+}
+
+@MainActor
+private final class EmptyAccountUIStorage: AccountCredentialStoring {
+    func load() throws -> AccountSession? { nil }
+    func save(_ session: AccountSession) throws {}
+    func clear() throws {}
+}
+
+private struct EmptyTransactionUpdates: StoreKitTransactionUpdates {
+    @MainActor
+    func observe(
+        _ handle: @escaping @MainActor (VerificationResult<Transaction>) async -> Void
+    ) -> StoreKitTransactionObservation {
+        StoreKitTransactionObservation(task: Task {})
+    }
+}
+#else
+@MainActor
+enum UIAutomationFixture {
+    static let isActive = false
+    static var paywallActions: ProductPaywallActions? { nil }
+    static let onboardingInitialPage = 0
+    static let showTabsOnce = false
+}
+#endif
diff --git a/FlashCardAI.xcodeproj/project.pbxproj b/FlashCardAI.xcodeproj/project.pbxproj
index fcd1976..b147a73 100644
--- a/FlashCardAI.xcodeproj/project.pbxproj
+++ b/FlashCardAI.xcodeproj/project.pbxproj
@@ -1,49 +1,51 @@
 // !$*UTF8*$!
 {
 	archiveVersion = 1;
 	classes = {
 	};
 	objectVersion = 56;
 	objects = {
 
 /* Begin PBXBuildFile section */
+		B71100000000000000000001 /* UILaunchFixtureTests.swift in Sources */ = {isa = PBXBuildFile; fileRef = B71100000000000000000002 /* UILaunchFixtureTests.swift */; };
+		B71100000000000000000003 /* UIAutomationFixture.swift in Sources */ = {isa = PBXBuildFile; fileRef = B71100000000000000000004 /* UIAutomationFixture.swift */; };
 		FA0800000000000000000001 /* EntitlementMutationBoundaryTests.swift in Sources */ = {isa = PBXBuildFile; fileRef = FA0800000000000000000002 /* EntitlementMutationBoundaryTests.swift */; };
 		AE1800000000000000000011 /* EnglishIrregularVerbForms.swift in Sources */ = {isa = PBXBuildFile; fileRef = AE1800000000000000000012 /* EnglishIrregularVerbForms.swift */; };
 		A10000000000000000000001 /* FlashCardAIApp.swift in Sources */ = {isa = PBXBuildFile; fileRef = A10000000000000000000002 /* FlashCardAIApp.swift */; };
 		A10000000000000000000003 /* AppRootModel.swift in Sources */ = {isa = PBXBuildFile; fileRef = A10000000000000000000004 /* AppRootModel.swift */; };
 		A10000000000000000000005 /* WordCacheNormalizer.swift in Sources */ = {isa = PBXBuildFile; fileRef = A10000000000000000000006 /* WordCacheNormalizer.swift */; };
 		A10000000000000000000007 /* AppLog.swift in Sources */ = {isa = PBXBuildFile; fileRef = A10000000000000000000008 /* AppLog.swift */; };
 		F6E000000000000000000001 /* ServerFeatureFlags.swift in Sources */ = {isa = PBXBuildFile; fileRef = F6E000000000000000000002 /* ServerFeatureFlags.swift */; };
 		A10000000000000000000009 /* APIConfiguration.swift in Sources */ = {isa = PBXBuildFile; fileRef = A1000000000000000000000A /* APIConfiguration.swift */; };
 		A1000000000000000000000B /* APIError.swift in Sources */ = {isa = PBXBuildFile; fileRef = A1000000000000000000000C /* APIError.swift */; };
 		A1000000000000000000000D /* APIClient.swift in Sources */ = {isa = PBXBuildFile; fileRef = A1000000000000000000000E /* APIClient.swift */; };
 		A1000000000000000000000F /* AIResponses.swift in Sources */ = {isa = PBXBuildFile; fileRef = A10000000000000000000010 /* AIResponses.swift */; };
 		A10000000000000000000011 /* LocalDatabase.swift in Sources */ = {isa = PBXBuildFile; fileRef = A10000000000000000000012 /* LocalDatabase.swift */; };
 		A10000000000000000000013 /* WordRepository.swift in Sources */ = {isa = PBXBuildFile; fileRef = A10000000000000000000014 /* WordRepository.swift */; };
 		B10300000000000000000001 /* PublicFlashcardSetService.swift in Sources */ = {isa = PBXBuildFile; fileRef = B10300000000000000000002 /* PublicFlashcardSetService.swift */; };
 		A10000000000000000000015 /* WordSearchView.swift in Sources */ = {isa = PBXBuildFile; fileRef = A10000000000000000000016 /* WordSearchView.swift */; };
 		D3A000000000000000000001 /* ManualWordEntryView.swift in Sources */ = {isa = PBXBuildFile; fileRef = D3A000000000000000000002 /* ManualWordEntryView.swift */; };
 		F4C000000000000000000001 /* LocalFlashcardImport.swift in Sources */ = {isa = PBXBuildFile; fileRef = F4C000000000000000000002 /* LocalFlashcardImport.swift */; };
 		F4C000000000000000000003 /* LocalFlashcardImportView.swift in Sources */ = {isa = PBXBuildFile; fileRef = F4C000000000000000000004 /* LocalFlashcardImportView.swift */; };
 		A10000000000000000000019 /* Assets.xcassets in Resources */ = {isa = PBXBuildFile; fileRef = A1000000000000000000001A /* Assets.xcassets */; };
 		A1000000000000000000001B /* libsqlite3.tbd in Frameworks */ = {isa = PBXBuildFile; fileRef = A1000000000000000000001C /* libsqlite3.tbd */; };
 		A10000000000000000000035 /* PronunciationSpeaker.swift in Sources */ = {isa = PBXBuildFile; fileRef = A10000000000000000000036 /* PronunciationSpeaker.swift */; };
 		A20000000000000000000003 /* WordSearchViewModel.swift in Sources */ = {isa = PBXBuildFile; fileRef = A20000000000000000000001 /* WordSearchViewModel.swift */; };
 		D3900000000000000000000B /* CreateFlashcardSetViewModel.swift in Sources */ = {isa = PBXBuildFile; fileRef = D3900000000000000000000C /* CreateFlashcardSetViewModel.swift */; };
 		A20000000000000000000004 /* WordDetailResultPanel.swift in Sources */ = {isa = PBXBuildFile; fileRef = A20000000000000000000002 /* WordDetailResultPanel.swift */; };
 		C1A000000000000000000001 /* AppLaunchState.swift in Sources */ = {isa = PBXBuildFile; fileRef = C1A000000000000000000002 /* AppLaunchState.swift */; };
 		C1A000000000000000000007 /* SyncDTOs.swift in Sources */ = {isa = PBXBuildFile; fileRef = C1A000000000000000000008 /* SyncDTOs.swift */; };
 		C1C000000000000000000001 /* LanguageOption.swift in Sources */ = {isa = PBXBuildFile; fileRef = C1C000000000000000000002 /* LanguageOption.swift */; };
 		D30100000000000000000001 /* WordDashboardRow.swift in Sources */ = {isa = PBXBuildFile; fileRef = D30100000000000000000002 /* WordDashboardRow.swift */; };
 		D30100000000000000000003 /* DashboardView.swift in Sources */ = {isa = PBXBuildFile; fileRef = D30100000000000000000004 /* DashboardView.swift */; };
 		F50A00000000000000000001 /* DashboardAddSpeedDial.swift in Sources */ = {isa = PBXBuildFile; fileRef = F50A00000000000000000002 /* DashboardAddSpeedDial.swift */; };
 		D30100000000000000000005 /* DashboardViewModel.swift in Sources */ = {isa = PBXBuildFile; fileRef = D30100000000000000000006 /* DashboardViewModel.swift */; };
 		D30100000000000000000007 /* WordHistoryView.swift in Sources */ = {isa = PBXBuildFile; fileRef = D30100000000000000000008 /* WordHistoryView.swift */; };
 		D30100000000000000000009 /* DashboardWordCard.swift in Sources */ = {isa = PBXBuildFile; fileRef = D3010000000000000000000A /* DashboardWordCard.swift */; };
 		D3900000000000000000000D /* SetGridTile.swift in Sources */ = {isa = PBXBuildFile; fileRef = D3900000000000000000000E /* SetGridTile.swift */; };
 		D39000000000000000000001 /* RootTabShell.swift in Sources */ = {isa = PBXBuildFile; fileRef = D39000000000000000000002 /* RootTabShell.swift */; };
 		D39000000000000000000003 /* FloatingTabBar.swift in Sources */ = {isa = PBXBuildFile; fileRef = D39000000000000000000004 /* FloatingTabBar.swift */; };
 		D39000000000000000000005 /* LibraryView.swift in Sources */ = {isa = PBXBuildFile; fileRef = D39000000000000000000006 /* LibraryView.swift */; };
 		B10500000000000000000001 /* LibraryViewModel.swift in Sources */ = {isa = PBXBuildFile; fileRef = B10500000000000000000002 /* LibraryViewModel.swift */; };
 		D39000000000000000000009 /* CreateFlashcardSetPage.swift in Sources */ = {isa = PBXBuildFile; fileRef = D3900000000000000000000A /* CreateFlashcardSetPage.swift */; };
 		D30200000000000000000001 /* ThemeManager.swift in Sources */ = {isa = PBXBuildFile; fileRef = D30200000000000000000002 /* ThemeManager.swift */; };
@@ -78,90 +80,100 @@
 		F50900000000000000000001 /* ReverseDirectionSettingsViewModel.swift in Sources */ = {isa = PBXBuildFile; fileRef = F50900000000000000000002 /* ReverseDirectionSettingsViewModel.swift */; };
 		F50900000000000000000003 /* ReverseDirectionSettingsViewModelTests.swift in Sources */ = {isa = PBXBuildFile; fileRef = F50900000000000000000004 /* ReverseDirectionSettingsViewModelTests.swift */; };
 		D3FF00000000000000000001 /* PaywallView.swift in Sources */ = {isa = PBXBuildFile; fileRef = D3FF00000000000000000002 /* PaywallView.swift */; };
 		E1A000000000000000000001 /* ProductOnboardingView.swift in Sources */ = {isa = PBXBuildFile; fileRef = E1A000000000000000000002 /* ProductOnboardingView.swift */; };
 		F60A00000000000000000001 /* ProductPalette.swift in Sources */ = {isa = PBXBuildFile; fileRef = F60A00000000000000000002 /* ProductPalette.swift */; };
 		F60B00000000000000000001 /* ProductPaywallPage.swift in Sources */ = {isa = PBXBuildFile; fileRef = F60B00000000000000000002 /* ProductPaywallPage.swift */; };
 		E3A000000000000000000001 /* IapDTOs.swift in Sources */ = {isa = PBXBuildFile; fileRef = E3A000000000000000000002 /* IapDTOs.swift */; };
 		E3A000000000000000000003 /* AppAccountTokenStore.swift in Sources */ = {isa = PBXBuildFile; fileRef = E3A000000000000000000004 /* AppAccountTokenStore.swift */; };
 		E3A000000000000000000005 /* TokenStore.swift in Sources */ = {isa = PBXBuildFile; fileRef = E3A000000000000000000006 /* TokenStore.swift */; };
 		E3A000000000000000000007 /* EntitlementStore.swift in Sources */ = {isa = PBXBuildFile; fileRef = E3A000000000000000000008 /* EntitlementStore.swift */; };
 		E3A000000000000000000009 /* StoreKitService.swift in Sources */ = {isa = PBXBuildFile; fileRef = E3A00000000000000000000A /* StoreKitService.swift */; };
 		E3A00000000000000000000B /* FreeLimitPolicy.swift in Sources */ = {isa = PBXBuildFile; fileRef = E3A00000000000000000000C /* FreeLimitPolicy.swift */; };
 		E3A00000000000000000000D /* Owl AI.storekit in Resources */ = {isa = PBXBuildFile; fileRef = E3A00000000000000000000E /* Owl AI.storekit */; };
 		F1A000000000000000000001 /* AppDelegate.swift in Sources */ = {isa = PBXBuildFile; fileRef = F1A000000000000000000002 /* AppDelegate.swift */; };
 		F2A00000000000000000001 /* DeviceAttestDTOs.swift in Sources */ = {isa = PBXBuildFile; fileRef = F2A00000000000000000002 /* DeviceAttestDTOs.swift */; };
 		F2A00000000000000000003 /* AppAttestKeychainStore.swift in Sources */ = {isa = PBXBuildFile; fileRef = F2A00000000000000000004 /* AppAttestKeychainStore.swift */; };
 		F2A00000000000000000005 /* AIIntegrityCoordinator.swift in Sources */ = {isa = PBXBuildFile; fileRef = F2A00000000000000000006 /* AIIntegrityCoordinator.swift */; };
 		F2A00000000000000000009 /* DeviceWordDTOs.swift in Sources */ = {isa = PBXBuildFile; fileRef = F2A0000000000000000000A /* DeviceWordDTOs.swift */; };
 		B10100000000000000000001 /* PublicFlashcardSetDTOs.swift in Sources */ = {isa = PBXBuildFile; fileRef = B10100000000000000000002 /* PublicFlashcardSetDTOs.swift */; };
 		FAAC00000000000000000001 /* AccountSessionClient.swift in Sources */ = {isa = PBXBuildFile; fileRef = FAAC00000000000000000002 /* AccountSessionClient.swift */; };
 		FAAC00000000000000000003 /* AccountProfileModel.swift in Sources */ = {isa = PBXBuildFile; fileRef = FAAC00000000000000000004 /* AccountProfileModel.swift */; };
 		FAAC00000000000000000005 /* AccountProfileView.swift in Sources */ = {isa = PBXBuildFile; fileRef = FAAC00000000000000000006 /* AccountProfileView.swift */; };
 		FAAC00000000000000000007 /* SharedAccountSyncTests.swift in Sources */ = {isa = PBXBuildFile; fileRef = FAAC00000000000000000008 /* SharedAccountSyncTests.swift */; };
 		A29B4C8E22E416826820DF69 /* AccountModels.swift in Sources */ = {isa = PBXBuildFile; fileRef = E34D7AFBC74EBBFE110F2C36 /* AccountModels.swift */; };
 		13C3782571EB7B7F00569064 /* AccountCredentialStore.swift in Sources */ = {isa = PBXBuildFile; fileRef = 0D0BE809ECA61955B1C5E300 /* AccountCredentialStore.swift */; };
 		E26DB6C3C8EF0286C4ECEEEC /* AccountSessionController.swift in Sources */ = {isa = PBXBuildFile; fileRef = 3D53A098574D5FED4AC5C84F /* AccountSessionController.swift */; };
 		456F9DEFA098F950FB01476B /* AccountAPIClient.swift in Sources */ = {isa = PBXBuildFile; fileRef = 55BA1DFF989CB9DAC102D244 /* AccountAPIClient.swift */; };
 		12CEAF6DF410B890BE99B14A /* GoogleAccountIdentityProvider.swift in Sources */ = {isa = PBXBuildFile; fileRef = 3F46066C7603A19BC2D64E7D /* GoogleAccountIdentityProvider.swift */; };
 		E182ED7BDDAE3DBE5D09B54F /* AccountView.swift in Sources */ = {isa = PBXBuildFile; fileRef = F447EB47B3262087377798D9 /* AccountView.swift */; };
 		A9582801F99345CFDFBF9B15 /* AccountViewModel.swift in Sources */ = {isa = PBXBuildFile; fileRef = CD55AD4BBEFE031DA83C5BF7 /* AccountViewModel.swift */; };
 		753CEF6B4D4407310A740A55 /* AccountSessionTests.swift in Sources */ = {isa = PBXBuildFile; fileRef = F9A55EDA139B7FB057F3B29B /* AccountSessionTests.swift */; };
 		4C087833FEC84DA00096CF81 /* AccountViewTests.swift in Sources */ = {isa = PBXBuildFile; fileRef = 5D0050EC7EE9A7CDDB5E2E81 /* AccountViewTests.swift */; };
 		EF4043FE3325DFC04B814FEA /* GoogleSignIn in Frameworks */ = {isa = PBXBuildFile; productRef = C00313621390BAA29843126F /* GoogleSignIn */; };
 		DC2E1CD73A9D6DB9A44EE403 /* GoogleSignInSwift in Frameworks */ = {isa = PBXBuildFile; productRef = 37660ACEC194C776B8BA746F /* GoogleSignInSwift */; };
 		AE1800000000000000000001 /* SecondaryReviewTests.swift in Sources */ = {isa = PBXBuildFile; fileRef = AE1800000000000000000002 /* SecondaryReviewTests.swift */; };
 		AE1800000000000000000003 /* SecondaryReviewModel.swift in Sources */ = {isa = PBXBuildFile; fileRef = AE1800000000000000000004 /* SecondaryReviewModel.swift */; };
 		AE1800000000000000000005 /* SecondaryReviewSurface.swift in Sources */ = {isa = PBXBuildFile; fileRef = AE1800000000000000000006 /* SecondaryReviewSurface.swift */; };
 /* End PBXBuildFile section */
 
 /* Begin PBXContainerItemProxy section */
+		B7110000000000000000000B /* PBXContainerItemProxy */ = {
+			isa = PBXContainerItemProxy;
+			containerPortal = A1000000000000000000002D /* Project object */;
+			proxyType = 1;
+			remoteGlobalIDString = A10000000000000000000029;
+			remoteInfo = FlashCardAI;
+		};
 		D3110000000000000000000B /* PBXContainerItemProxy */ = {
 			isa = PBXContainerItemProxy;
 			containerPortal = A1000000000000000000002D /* Project object */;
 			proxyType = 1;
 			remoteGlobalIDString = A10000000000000000000029;
 			remoteInfo = FlashCardAI;
 		};
 /* End PBXContainerItemProxy section */
 
 /* Begin PBXFileReference section */
+		B71100000000000000000002 /* UILaunchFixtureTests.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = UILaunchFixtureTests.swift; sourceTree = "<group>"; };
+		B71100000000000000000004 /* UIAutomationFixture.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = UIAutomationFixture.swift; sourceTree = "<group>"; };
+		B71100000000000000000005 /* FlashCardAIUITests.xctest */ = {isa = PBXFileReference; explicitFileType = wrapper.cfbundle; includeInIndex = 0; path = FlashCardAIUITests.xctest; sourceTree = BUILT_PRODUCTS_DIR; };
 		FA0800000000000000000002 /* EntitlementMutationBoundaryTests.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = EntitlementMutationBoundaryTests.swift; sourceTree = "<group>"; };
 		AE1800000000000000000012 /* EnglishIrregularVerbForms.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = EnglishIrregularVerbForms.swift; sourceTree = "<group>"; };
 		A10000000000000000000002 /* FlashCardAIApp.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = FlashCardAIApp.swift; sourceTree = "<group>"; };
 		A10000000000000000000004 /* AppRootModel.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = AppRootModel.swift; sourceTree = "<group>"; };
 		A10000000000000000000006 /* WordCacheNormalizer.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = WordCacheNormalizer.swift; sourceTree = "<group>"; };
 		A10000000000000000000008 /* AppLog.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = AppLog.swift; sourceTree = "<group>"; };
 		F6E000000000000000000002 /* ServerFeatureFlags.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = ServerFeatureFlags.swift; sourceTree = "<group>"; };
 		A1000000000000000000000A /* APIConfiguration.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = APIConfiguration.swift; sourceTree = "<group>"; };
 		A1000000000000000000000C /* APIError.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = APIError.swift; sourceTree = "<group>"; };
 		A1000000000000000000000E /* APIClient.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = APIClient.swift; sourceTree = "<group>"; };
 		A10000000000000000000010 /* AIResponses.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = AIResponses.swift; sourceTree = "<group>"; };
 		A10000000000000000000012 /* LocalDatabase.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = LocalDatabase.swift; sourceTree = "<group>"; };
 		A10000000000000000000014 /* WordRepository.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = WordRepository.swift; sourceTree = "<group>"; };
 		B10300000000000000000002 /* PublicFlashcardSetService.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = PublicFlashcardSetService.swift; sourceTree = "<group>"; };
 		A10000000000000000000016 /* WordSearchView.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = WordSearchView.swift; sourceTree = "<group>"; };
 		D3A000000000000000000002 /* ManualWordEntryView.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = ManualWordEntryView.swift; sourceTree = "<group>"; };
 		F4C000000000000000000002 /* LocalFlashcardImport.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = LocalFlashcardImport.swift; sourceTree = "<group>"; };
 		F4C000000000000000000004 /* LocalFlashcardImportView.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = LocalFlashcardImportView.swift; sourceTree = "<group>"; };
 		A1000000000000000000001A /* Assets.xcassets */ = {isa = PBXFileReference; lastKnownFileType = folder.assetcatalog; path = Assets.xcassets; sourceTree = "<group>"; };
 		A1000000000000000000001C /* libsqlite3.tbd */ = {isa = PBXFileReference; lastKnownFileType = "sourcecode.text-based-dylib-definition"; name = libsqlite3.tbd; path = usr/lib/libsqlite3.tbd; sourceTree = SDKROOT; };
 		A1000000000000000000001D /* FlashCardAI.app */ = {isa = PBXFileReference; explicitFileType = wrapper.application; includeInIndex = 0; path = FlashCardAI.app; sourceTree = BUILT_PRODUCTS_DIR; };
 		A10000000000000000000036 /* PronunciationSpeaker.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = PronunciationSpeaker.swift; sourceTree = "<group>"; };
 		A20000000000000000000001 /* WordSearchViewModel.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = WordSearchViewModel.swift; sourceTree = "<group>"; };
 		D3900000000000000000000C /* CreateFlashcardSetViewModel.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = CreateFlashcardSetViewModel.swift; sourceTree = "<group>"; };
 		A20000000000000000000002 /* WordDetailResultPanel.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = WordDetailResultPanel.swift; sourceTree = "<group>"; };
 		C1A000000000000000000002 /* AppLaunchState.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = AppLaunchState.swift; sourceTree = "<group>"; };
 		C1A000000000000000000008 /* SyncDTOs.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = SyncDTOs.swift; sourceTree = "<group>"; };
 		C1A00000000000000000000D /* FlashCardAI.entitlements */ = {isa = PBXFileReference; lastKnownFileType = text.plist.entitlements; path = FlashCardAI.entitlements; sourceTree = "<group>"; };
 		C1C000000000000000000002 /* LanguageOption.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = LanguageOption.swift; sourceTree = "<group>"; };
 		D30100000000000000000002 /* WordDashboardRow.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = WordDashboardRow.swift; sourceTree = "<group>"; };
 		D30100000000000000000004 /* DashboardView.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = DashboardView.swift; sourceTree = "<group>"; };
 		F50A00000000000000000002 /* DashboardAddSpeedDial.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = DashboardAddSpeedDial.swift; sourceTree = "<group>"; };
 		D30100000000000000000006 /* DashboardViewModel.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = DashboardViewModel.swift; sourceTree = "<group>"; };
 		D30100000000000000000008 /* WordHistoryView.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = WordHistoryView.swift; sourceTree = "<group>"; };
 		D3010000000000000000000A /* DashboardWordCard.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = DashboardWordCard.swift; sourceTree = "<group>"; };
 		D3900000000000000000000E /* SetGridTile.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = SetGridTile.swift; sourceTree = "<group>"; };
 		D39000000000000000000002 /* RootTabShell.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = RootTabShell.swift; sourceTree = "<group>"; };
 		D39000000000000000000004 /* FloatingTabBar.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = FloatingTabBar.swift; sourceTree = "<group>"; };
 		D39000000000000000000006 /* LibraryView.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = LibraryView.swift; sourceTree = "<group>"; };
 		B10500000000000000000002 /* LibraryViewModel.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = LibraryViewModel.swift; sourceTree = "<group>"; };
@@ -197,158 +209,168 @@
 		F50900000000000000000002 /* ReverseDirectionSettingsViewModel.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = ReverseDirectionSettingsViewModel.swift; sourceTree = "<group>"; };
 		F50900000000000000000004 /* ReverseDirectionSettingsViewModelTests.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = ReverseDirectionSettingsViewModelTests.swift; sourceTree = "<group>"; };
 		D31100000000000000000003 /* FlashCardAITests.xctest */ = {isa = PBXFileReference; explicitFileType = wrapper.cfbundle; includeInIndex = 0; path = FlashCardAITests.xctest; sourceTree = BUILT_PRODUCTS_DIR; };
 		D3FF00000000000000000002 /* PaywallView.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = PaywallView.swift; sourceTree = "<group>"; };
 		E1A000000000000000000002 /* ProductOnboardingView.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = ProductOnboardingView.swift; sourceTree = "<group>"; };
 		F60A00000000000000000002 /* ProductPalette.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = ProductPalette.swift; sourceTree = "<group>"; };
 		F60B00000000000000000002 /* ProductPaywallPage.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = ProductPaywallPage.swift; sourceTree = "<group>"; };
 		E3A000000000000000000002 /* IapDTOs.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = IapDTOs.swift; sourceTree = "<group>"; };
 		E3A000000000000000000004 /* AppAccountTokenStore.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = AppAccountTokenStore.swift; sourceTree = "<group>"; };
 		E3A000000000000000000006 /* TokenStore.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = TokenStore.swift; sourceTree = "<group>"; };
 		E3A000000000000000000008 /* EntitlementStore.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = EntitlementStore.swift; sourceTree = "<group>"; };
 		E3A00000000000000000000A /* StoreKitService.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = StoreKitService.swift; sourceTree = "<group>"; };
 		E3A00000000000000000000C /* FreeLimitPolicy.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = FreeLimitPolicy.swift; sourceTree = "<group>"; };
 		E3A00000000000000000000E /* Owl AI.storekit */ = {isa = PBXFileReference; lastKnownFileType = text; path = "Owl AI.storekit"; sourceTree = "<group>"; };
 		F1A000000000000000000002 /* AppDelegate.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = AppDelegate.swift; sourceTree = "<group>"; };
 		F2A00000000000000000002 /* DeviceAttestDTOs.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = DeviceAttestDTOs.swift; sourceTree = "<group>"; };
 		F2A00000000000000000004 /* AppAttestKeychainStore.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = AppAttestKeychainStore.swift; sourceTree = "<group>"; };
 		F2A00000000000000000006 /* AIIntegrityCoordinator.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = AIIntegrityCoordinator.swift; sourceTree = "<group>"; };
 		F2A0000000000000000000A /* DeviceWordDTOs.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = DeviceWordDTOs.swift; sourceTree = "<group>"; };
 		B10100000000000000000002 /* PublicFlashcardSetDTOs.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = PublicFlashcardSetDTOs.swift; sourceTree = "<group>"; };
 		FAAC00000000000000000002 /* AccountSessionClient.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = Infrastructure/Networking/AccountSessionClient.swift; sourceTree = SOURCE_ROOT; };
 		FAAC00000000000000000004 /* AccountProfileModel.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = Presentation/Features/Settings/ViewModels/AccountProfileModel.swift; sourceTree = SOURCE_ROOT; };
 		FAAC00000000000000000006 /* AccountProfileView.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = Presentation/Features/Settings/Views/AccountProfileView.swift; sourceTree = SOURCE_ROOT; };
 		FAAC00000000000000000008 /* SharedAccountSyncTests.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = FlashCardAITests/SharedAccountSyncTests.swift; sourceTree = SOURCE_ROOT; };
 		E34D7AFBC74EBBFE110F2C36 /* AccountModels.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = Infrastructure/Auth/AccountModels.swift; sourceTree = SOURCE_ROOT; };
 		0D0BE809ECA61955B1C5E300 /* AccountCredentialStore.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = Infrastructure/Auth/AccountCredentialStore.swift; sourceTree = SOURCE_ROOT; };
 		3D53A098574D5FED4AC5C84F /* AccountSessionController.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = Infrastructure/Auth/AccountSessionController.swift; sourceTree = SOURCE_ROOT; };
 		55BA1DFF989CB9DAC102D244 /* AccountAPIClient.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = Infrastructure/Networking/AccountAPIClient.swift; sourceTree = SOURCE_ROOT; };
 		3F46066C7603A19BC2D64E7D /* GoogleAccountIdentityProvider.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = Infrastructure/Auth/GoogleAccountIdentityProvider.swift; sourceTree = SOURCE_ROOT; };
 		F447EB47B3262087377798D9 /* AccountView.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = Presentation/Features/Account/AccountView.swift; sourceTree = SOURCE_ROOT; };
 		CD55AD4BBEFE031DA83C5BF7 /* AccountViewModel.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = Presentation/Features/Account/AccountViewModel.swift; sourceTree = SOURCE_ROOT; };
 		F9A55EDA139B7FB057F3B29B /* AccountSessionTests.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = FlashCardAITests/AccountSessionTests.swift; sourceTree = SOURCE_ROOT; };
 		5D0050EC7EE9A7CDDB5E2E81 /* AccountViewTests.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = FlashCardAITests/AccountViewTests.swift; sourceTree = SOURCE_ROOT; };
 		ACCA00000000000000000001 /* Account.xcconfig */ = {isa = PBXFileReference; lastKnownFileType = text.xcconfig; path = Config/Account.xcconfig; sourceTree = SOURCE_ROOT; };
 		AE1800000000000000000002 /* SecondaryReviewTests.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = SecondaryReviewTests.swift; sourceTree = "<group>"; };
 		AE1800000000000000000004 /* SecondaryReviewModel.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = SecondaryReviewModel.swift; sourceTree = "<group>"; };
 		AE1800000000000000000006 /* SecondaryReviewSurface.swift */ = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = SecondaryReviewSurface.swift; sourceTree = "<group>"; };
 /* End PBXFileReference section */
 
 /* Begin PBXFrameworksBuildPhase section */
+		B71100000000000000000006 /* Frameworks */ = {
+			isa = PBXFrameworksBuildPhase;
+			buildActionMask = 2147483647;
+			files = (
+			);
+			runOnlyForDeploymentPostprocessing = 0;
+		};
 		A1000000000000000000001E /* Frameworks */ = {
 			isa = PBXFrameworksBuildPhase;
 			buildActionMask = 2147483647;
 			files = (
 				DC2E1CD73A9D6DB9A44EE403 /* GoogleSignInSwift in Frameworks */,
 				EF4043FE3325DFC04B814FEA /* GoogleSignIn in Frameworks */,
 				A1000000000000000000001B /* libsqlite3.tbd in Frameworks */,
 				F50000000000000000000007 /* FSRS in Frameworks */,
 			);
 			runOnlyForDeploymentPostprocessing = 0;
 		};
 		D31100000000000000000004 /* Frameworks */ = {
 			isa = PBXFrameworksBuildPhase;
 			buildActionMask = 2147483647;
 			files = (
 			);
 			runOnlyForDeploymentPostprocessing = 0;
 		};
 /* End PBXFrameworksBuildPhase section */
 
 /* Begin PBXGroup section */
 		0483CD492FE4BDFA00023BA0 /* Recovered References */ = {
 			isa = PBXGroup;
 			children = (
 				A1000000000000000000001C /* libsqlite3.tbd */,
 			);
 			name = "Recovered References";
 			sourceTree = "<group>";
 		};
 		A1000000000000000000001F = {
 			isa = PBXGroup;
 			children = (
 				FAAC00000000000000000002 /* AccountSessionClient.swift */,
 				FAAC00000000000000000004 /* AccountProfileModel.swift */,
 				FAAC00000000000000000006 /* AccountProfileView.swift */,
 				FAAC00000000000000000008 /* SharedAccountSyncTests.swift */,
 				ACCA00000000000000000001 /* Account.xcconfig */,
 				242E903CE4807F8B3C5FD3BA /* Account */,
 				A10000000000000000000020 /* App */,
 				A10000000000000000000040 /* Presentation */,
 				A10000000000000000000050 /* Infrastructure */,
 				D31000000000000000000003 /* Domain */,
 				A10000000000000000000060 /* Shared */,
 				A10000000000000000000024 /* Resources */,
 				D31100000000000000000005 /* FlashCardAITests */,
+				B71100000000000000000007 /* FlashCardAIUITests */,
 				A10000000000000000000025 /* Products */,
 				0483CD492FE4BDFA00023BA0 /* Recovered References */,
 			);
 			sourceTree = "<group>";
 		};
 		A10000000000000000000020 /* App */ = {
 			isa = PBXGroup;
 			children = (
 				A10000000000000000000002 /* FlashCardAIApp.swift */,
+				B71100000000000000000004 /* UIAutomationFixture.swift */,
 				F1A000000000000000000002 /* AppDelegate.swift */,
 				C1A00000000000000000000E /* Session */,
 				A10000000000000000000070 /* Root */,
 				A10000000000000000000071 /* Navigation */,
 				A10000000000000000000072 /* DependencyInjection */,
 			);
 			path = App;
 			sourceTree = "<group>";
 		};
 		A10000000000000000000024 /* Resources */ = {
 			isa = PBXGroup;
 			children = (
 				A1000000000000000000001A /* Assets.xcassets */,
 				C1A00000000000000000000D /* FlashCardAI.entitlements */,
 				E3A00000000000000000000E /* Owl AI.storekit */,
 			);
 			path = Resources;
 			sourceTree = "<group>";
 		};
 		A10000000000000000000025 /* Products */ = {
 			isa = PBXGroup;
 			children = (
 				A1000000000000000000001D /* FlashCardAI.app */,
 				D31100000000000000000003 /* FlashCardAITests.xctest */,
+				B71100000000000000000005 /* FlashCardAIUITests.xctest */,
 			);
 			name = Products;
 			sourceTree = "<group>";
 		};
 		A10000000000000000000040 /* Presentation */ = {
 			isa = PBXGroup;
 			children = (
 				A10000000000000000000041 /* Features */,
 				A10000000000000000000042 /* SharedUI */,
 			);
 			path = Presentation;
 			sourceTree = "<group>";
 		};
 		A10000000000000000000041 /* Features */ = {
 			isa = PBXGroup;
 			children = (
 				D3010000000000000000000B /* Home */,
 				A10000000000000000000043 /* WordSearch */,
 				A10000000000000000000044 /* Onboarding */,
 				E2B000000000000000000005 /* Auth */,
 				D3040000000000000000000B /* Settings */,
 				D39000000000000000000007 /* Library */,
 			);
 			path = Features;
 			sourceTree = "<group>";
 		};
 		A10000000000000000000042 /* SharedUI */ = {
 			isa = PBXGroup;
 			children = (
 				D39000000000000000000004 /* FloatingTabBar.swift */,
 				F60A00000000000000000002 /* ProductPalette.swift */,
 			);
 			path = SharedUI;
 			sourceTree = "<group>";
 		};
 		A10000000000000000000043 /* WordSearch */ = {
 			isa = PBXGroup;
 			children = (
 				A10000000000000000000045 /* Views */,
 				A10000000000000000000046 /* ViewModels */,
@@ -679,80 +701,88 @@
 			sourceTree = "<group>";
 		};
 		D31000000000000000000003 /* Domain */ = {
 			isa = PBXGroup;
 			children = (
 				AE1800000000000000000012 /* EnglishIrregularVerbForms.swift */,
 				D31000000000000000000004 /* SpacedRepetition */,
 				E3A000000000000000000010 /* Entitlement */,
 			);
 			path = Domain;
 			sourceTree = "<group>";
 		};
 		D31000000000000000000004 /* SpacedRepetition */ = {
 			isa = PBXGroup;
 			children = (
 				F50000000000000000000002 /* ReviewSchedulingModels.swift */,
 				F50000000000000000000004 /* FSRSSchedulerService.swift */,
 				F50100000000000000000002 /* StudyDay.swift */,
 				F50500000000000000000002 /* ReviewQueueService.swift */,
 			);
 			path = SpacedRepetition;
 			sourceTree = "<group>";
 		};
 		D31100000000000000000005 /* FlashCardAITests */ = {
 			isa = PBXGroup;
 			children = (
 				FA0800000000000000000002 /* EntitlementMutationBoundaryTests.swift */,
 				F50300000000000000000002 /* LocalDatabaseFSRSMigrationTests.swift */,
 				F50400000000000000000002 /* ReviewCardRepositoryTests.swift */,
 				F50500000000000000000004 /* ReviewQueueServiceTests.swift */,
 				F50700000000000000000002 /* ReviewSessionViewModelTests.swift */,
 				AE1800000000000000000002 /* SecondaryReviewTests.swift */,
 				D31100000000000000000002 /* ApplicationBehaviorTests.swift */,
 				F50000000000000000000006 /* FSRSSchedulerServiceTests.swift */,
 				F50100000000000000000006 /* StudyDayTests.swift */,
 				F50900000000000000000004 /* ReverseDirectionSettingsViewModelTests.swift */,
 			);
 			path = FlashCardAITests;
 			sourceTree = "<group>";
 		};
+		B71100000000000000000007 /* FlashCardAIUITests */ = {
+			isa = PBXGroup;
+			children = (
+				B71100000000000000000002 /* UILaunchFixtureTests.swift */,
+			);
+			path = FlashCardAIUITests;
+			sourceTree = "<group>";
+		};
 		E2B000000000000000000005 /* Auth */ = {
 			isa = PBXGroup;
 			children = (
 			);
 			path = Auth;
 			sourceTree = "<group>";
 		};
 		E3A00000000000000000000F /* Purchases */ = {
 			isa = PBXGroup;
 			children = (
 				E3A000000000000000000004 /* AppAccountTokenStore.swift */,
 				E3A000000000000000000006 /* TokenStore.swift */,
 				E3A000000000000000000008 /* EntitlementStore.swift */,
 				E3A00000000000000000000A /* StoreKitService.swift */,
 			);
 			path = Purchases;
 			sourceTree = "<group>";
 		};
 		E3A000000000000000000010 /* Entitlement */ = {
 			isa = PBXGroup;
 			children = (
 				E3A00000000000000000000C /* FreeLimitPolicy.swift */,
 			);
 			path = Entitlement;
 			sourceTree = "<group>";
 		};
 		F2A00000000000000000007 /* Security */ = {
 			isa = PBXGroup;
 			children = (
 				F2A00000000000000000008 /* AppAttest */,
 			);
 			path = Security;
 			sourceTree = "<group>";
 		};
 		F2A00000000000000000008 /* AppAttest */ = {
 			isa = PBXGroup;
 			children = (
 				F2A00000000000000000004 /* AppAttestKeychainStore.swift */,
 				F2A00000000000000000006 /* AIIntegrityCoordinator.swift */,
 			);
@@ -781,157 +811,188 @@
 		A10000000000000000000029 /* FlashCardAI */ = {
 			isa = PBXNativeTarget;
 			buildConfigurationList = A1000000000000000000002A /* Build configuration list for PBXNativeTarget "FlashCardAI" */;
 			buildPhases = (
 				A1000000000000000000002B /* Sources */,
 				A1000000000000000000001E /* Frameworks */,
 				A1000000000000000000002C /* Resources */,
 			);
 			buildRules = (
 			);
 			dependencies = (
 			);
 			name = FlashCardAI;
 			packageProductDependencies = (
 				37660ACEC194C776B8BA746F /* GoogleSignInSwift */,
 				C00313621390BAA29843126F /* GoogleSignIn */,
 				F50000000000000000000008 /* FSRS */,
 			);
 			productName = FlashCardAI;
 			productReference = A1000000000000000000001D /* FlashCardAI.app */;
 			productType = "com.apple.product-type.application";
 		};
 		D31100000000000000000006 /* FlashCardAITests */ = {
 			isa = PBXNativeTarget;
 			buildConfigurationList = D31100000000000000000007 /* Build configuration list for PBXNativeTarget "FlashCardAITests" */;
 			buildPhases = (
 				D31100000000000000000008 /* Sources */,
 				D31100000000000000000004 /* Frameworks */,
 				D31100000000000000000009 /* Resources */,
 			);
 			buildRules = (
 			);
 			dependencies = (
 				D3110000000000000000000A /* PBXTargetDependency */,
 			);
 			name = FlashCardAITests;
 			productName = FlashCardAITests;
 			productReference = D31100000000000000000003 /* FlashCardAITests.xctest */;
 			productType = "com.apple.product-type.bundle.unit-test";
 		};
+		B71100000000000000000008 /* FlashCardAIUITests */ = {
+			isa = PBXNativeTarget;
+			buildConfigurationList = B71100000000000000000009 /* Build configuration list for PBXNativeTarget "FlashCardAIUITests" */;
+			buildPhases = (
+				B7110000000000000000000C /* Sources */,
+				B71100000000000000000006 /* Frameworks */,
+			);
+			buildRules = (
+			);
+			dependencies = (
+				B7110000000000000000000D /* PBXTargetDependency */,
+			);
+			name = FlashCardAIUITests;
+			productName = FlashCardAIUITests;
+			productReference = B71100000000000000000005 /* FlashCardAIUITests.xctest */;
+			productType = "com.apple.product-type.bundle.ui-testing";
+		};
 /* End PBXNativeTarget section */
 
 /* Begin PBXProject section */
 		A1000000000000000000002D /* Project object */ = {
 			isa = PBXProject;
 			attributes = {
 				BuildIndependentTargetsInParallel = 1;
 				LastSwiftUpdateCheck = 1500;
 				LastUpgradeCheck = 1500;
 				TargetAttributes = {
 					A10000000000000000000029 = {
 						CreatedOnToolsVersion = 15.0;
 					};
 					D31100000000000000000006 = {
 						CreatedOnToolsVersion = 15.0;
 						TestTargetID = A10000000000000000000029;
 					};
+					B71100000000000000000008 = {
+						CreatedOnToolsVersion = 15.0;
+						TestTargetID = A10000000000000000000029;
+					};
 				};
 			};
 			buildConfigurationList = A1000000000000000000002E /* Build configuration list for PBXProject "FlashCardAI" */;
 			compatibilityVersion = "Xcode 14.0";
 			developmentRegion = en;
 			hasScannedForEncodings = 0;
 			knownRegions = (
 				en,
 				Base,
 			);
 			mainGroup = A1000000000000000000001F;
 			packageReferences = (
 				D2A45DA4B05998CBBA40CAA7 /* GoogleSignIn-iOS */,
 				F50000000000000000000009 /* XCRemoteSwiftPackageReference \"swift-fsrs\" */,
 			);
 			productRefGroup = A10000000000000000000025 /* Products */;
 			projectDirPath = "";
 			projectRoot = "";
 			targets = (
 				A10000000000000000000029 /* FlashCardAI */,
 				D31100000000000000000006 /* FlashCardAITests */,
+				B71100000000000000000008 /* FlashCardAIUITests */,
 			);
 		};
 /* End PBXProject section */
 
 /* Begin PBXResourcesBuildPhase section */
 		A1000000000000000000002C /* Resources */ = {
 			isa = PBXResourcesBuildPhase;
 			buildActionMask = 2147483647;
 			files = (
 				A10000000000000000000019 /* Assets.xcassets in Resources */,
 				E3A00000000000000000000D /* Owl AI.storekit in Resources */,
 			);
 			runOnlyForDeploymentPostprocessing = 0;
 		};
 		D31100000000000000000009 /* Resources */ = {
 			isa = PBXResourcesBuildPhase;
 			buildActionMask = 2147483647;
 			files = (
 			);
 			runOnlyForDeploymentPostprocessing = 0;
 		};
 /* End PBXResourcesBuildPhase section */
 
 /* Begin PBXSourcesBuildPhase section */
+		B7110000000000000000000C /* Sources */ = {
+			isa = PBXSourcesBuildPhase;
+			buildActionMask = 2147483647;
+			files = (
+				B71100000000000000000001 /* UILaunchFixtureTests.swift in Sources */,
+			);
+			runOnlyForDeploymentPostprocessing = 0;
+		};
 		A1000000000000000000002B /* Sources */ = {
 			isa = PBXSourcesBuildPhase;
 			buildActionMask = 2147483647;
 			files = (
 				FAAC00000000000000000005 /* AccountProfileView.swift in Sources */,
 				FAAC00000000000000000003 /* AccountProfileModel.swift in Sources */,
 				FAAC00000000000000000001 /* AccountSessionClient.swift in Sources */,
 				A9582801F99345CFDFBF9B15 /* AccountViewModel.swift in Sources */,
 				E182ED7BDDAE3DBE5D09B54F /* AccountView.swift in Sources */,
 				12CEAF6DF410B890BE99B14A /* GoogleAccountIdentityProvider.swift in Sources */,
 				456F9DEFA098F950FB01476B /* AccountAPIClient.swift in Sources */,
 				E26DB6C3C8EF0286C4ECEEEC /* AccountSessionController.swift in Sources */,
 				13C3782571EB7B7F00569064 /* AccountCredentialStore.swift in Sources */,
 				A29B4C8E22E416826820DF69 /* AccountModels.swift in Sources */,
 				A10000000000000000000001 /* FlashCardAIApp.swift in Sources */,
+				B71100000000000000000003 /* UIAutomationFixture.swift in Sources */,
 				F1A000000000000000000001 /* AppDelegate.swift in Sources */,
 				A10000000000000000000003 /* AppRootModel.swift in Sources */,
 				A10000000000000000000005 /* WordCacheNormalizer.swift in Sources */,
 				A10000000000000000000007 /* AppLog.swift in Sources */,
 				A10000000000000000000009 /* APIConfiguration.swift in Sources */,
 				F6E000000000000000000001 /* ServerFeatureFlags.swift in Sources */,
 				A1000000000000000000000B /* APIError.swift in Sources */,
 				A1000000000000000000000D /* APIClient.swift in Sources */,
 				A1000000000000000000000F /* AIResponses.swift in Sources */,
 				F50300000000000000000003 /* FSRSSchemaMigrator.swift in Sources */,
 				F51100000000000000000001 /* FlashcardSetPublicationSchemaMigrator.swift in Sources */,
 				A10000000000000000000011 /* LocalDatabase.swift in Sources */,
 				A10000000000000000000013 /* WordRepository.swift in Sources */,
 				B10300000000000000000001 /* PublicFlashcardSetService.swift in Sources */,
 				F50400000000000000000003 /* ReviewCardRepository.swift in Sources */,
 				F50500000000000000000001 /* ReviewQueueService.swift in Sources */,
 				F50800000000000000000001 /* AppComposition.swift in Sources */,
 				F50900000000000000000001 /* ReverseDirectionSettingsViewModel.swift in Sources */,
 				A20000000000000000000003 /* WordSearchViewModel.swift in Sources */,
 				D3900000000000000000000B /* CreateFlashcardSetViewModel.swift in Sources */,
 				A20000000000000000000004 /* WordDetailResultPanel.swift in Sources */,
 				A10000000000000000000015 /* WordSearchView.swift in Sources */,
 				D39000000000000000000009 /* CreateFlashcardSetPage.swift in Sources */,
 				D3A000000000000000000001 /* ManualWordEntryView.swift in Sources */,
 				F4C000000000000000000003 /* LocalFlashcardImportView.swift in Sources */,
 				F4C000000000000000000001 /* LocalFlashcardImport.swift in Sources */,
 				A10000000000000000000035 /* PronunciationSpeaker.swift in Sources */,
 				E1A000000000000000000001 /* ProductOnboardingView.swift in Sources */,
 				F60B00000000000000000001 /* ProductPaywallPage.swift in Sources */,
 				F60A00000000000000000001 /* ProductPalette.swift in Sources */,
 				D30200000000000000000001 /* ThemeManager.swift in Sources */,
 				D30200000000000000000003 /* ChooseThemeSheet.swift in Sources */,
 				D30400000000000000000005 /* ThemeAppearanceControls.swift in Sources */,
 				D30400000000000000000001 /* AppSettingsStore.swift in Sources */,
 				D30400000000000000000003 /* StudyReminderScheduler.swift in Sources */,
 				D30400000000000000000007 /* SettingsView.swift in Sources */,
 				C1A000000000000000000001 /* AppLaunchState.swift in Sources */,
 				C1A000000000000000000007 /* SyncDTOs.swift in Sources */,
 				F2A00000000000000000001 /* DeviceAttestDTOs.swift in Sources */,
 				F2A00000000000000000009 /* DeviceWordDTOs.swift in Sources */,
@@ -959,80 +1020,85 @@
 				D30900000000000000000001 /* ReviewSessionView.swift in Sources */,
 				AE1800000000000000000005 /* SecondaryReviewSurface.swift in Sources */,
 				AE1800000000000000000003 /* SecondaryReviewModel.swift in Sources */,
 				D30900000000000000000003 /* ReviewSessionViewModel.swift in Sources */,
 				D30100000000000000000005 /* DashboardViewModel.swift in Sources */,
 				D30100000000000000000007 /* WordHistoryView.swift in Sources */,
 				D30100000000000000000009 /* DashboardWordCard.swift in Sources */,
 				D3900000000000000000000D /* SetGridTile.swift in Sources */,
 				AE1800000000000000000011 /* EnglishIrregularVerbForms.swift in Sources */,
 				F50000000000000000000001 /* ReviewSchedulingModels.swift in Sources */,
 				F50000000000000000000003 /* FSRSSchedulerService.swift in Sources */,
 				F50100000000000000000001 /* StudyDay.swift in Sources */,
 				F50100000000000000000003 /* FSRSSettingsStore.swift in Sources */,
 				E3A00000000000000000000B /* FreeLimitPolicy.swift in Sources */,
 			);
 			runOnlyForDeploymentPostprocessing = 0;
 		};
 		D31100000000000000000008 /* Sources */ = {
 			isa = PBXSourcesBuildPhase;
 			buildActionMask = 2147483647;
 			files = (
 				FA0800000000000000000001 /* EntitlementMutationBoundaryTests.swift in Sources */,
 				FAAC00000000000000000007 /* SharedAccountSyncTests.swift in Sources */,
 				4C087833FEC84DA00096CF81 /* AccountViewTests.swift in Sources */,
 				753CEF6B4D4407310A740A55 /* AccountSessionTests.swift in Sources */,
 				F50300000000000000000001 /* LocalDatabaseFSRSMigrationTests.swift in Sources */,
 				F50400000000000000000001 /* ReviewCardRepositoryTests.swift in Sources */,
 				F50500000000000000000003 /* ReviewQueueServiceTests.swift in Sources */,
 				F50700000000000000000001 /* ReviewSessionViewModelTests.swift in Sources */,
 				AE1800000000000000000001 /* SecondaryReviewTests.swift in Sources */,
 				D31100000000000000000001 /* ApplicationBehaviorTests.swift in Sources */,
 				F50000000000000000000005 /* FSRSSchedulerServiceTests.swift in Sources */,
 				F50100000000000000000005 /* StudyDayTests.swift in Sources */,
 				F50900000000000000000003 /* ReverseDirectionSettingsViewModelTests.swift in Sources */,
 			);
 			runOnlyForDeploymentPostprocessing = 0;
 		};
 /* End PBXSourcesBuildPhase section */
 
 /* Begin PBXTargetDependency section */
+		B7110000000000000000000D /* PBXTargetDependency */ = {
+			isa = PBXTargetDependency;
+			target = A10000000000000000000029 /* FlashCardAI */;
+			targetProxy = B7110000000000000000000B /* PBXContainerItemProxy */;
+		};
 		D3110000000000000000000A /* PBXTargetDependency */ = {
 			isa = PBXTargetDependency;
 			target = A10000000000000000000029 /* FlashCardAI */;
 			targetProxy = D3110000000000000000000B /* PBXContainerItemProxy */;
 		};
 /* End PBXTargetDependency section */
 
 /* Begin XCRemoteSwiftPackageReference section */
 		F50000000000000000000009 /* XCRemoteSwiftPackageReference \"swift-fsrs\" */ = {
 			isa = XCRemoteSwiftPackageReference;
 			repositoryURL = "https://github.com/open-spaced-repetition/swift-fsrs";
 			requirement = {
 				kind = revision;
 				revision = 4fbaf20184d62f82a9f44f343337c61a2c5483e9;
 			};
 		};
 		D2A45DA4B05998CBBA40CAA7 /* GoogleSignIn-iOS */ = {isa = XCRemoteSwiftPackageReference; repositoryURL = "https://github.com/google/GoogleSignIn-iOS"; requirement = {kind = exactVersion; version = 9.2.0; }; };
 /* End XCRemoteSwiftPackageReference section */
 
 /* Begin XCSwiftPackageProductDependency section */
 		F50000000000000000000008 /* FSRS */ = {
 			isa = XCSwiftPackageProductDependency;
 			package = F50000000000000000000009 /* XCRemoteSwiftPackageReference \"swift-fsrs\" */;
 			productName = FSRS;
 		};
 		C00313621390BAA29843126F /* GoogleSignIn */ = {isa = XCSwiftPackageProductDependency; package = D2A45DA4B05998CBBA40CAA7 /* GoogleSignIn-iOS */; productName = GoogleSignIn; };
 		37660ACEC194C776B8BA746F /* GoogleSignInSwift */ = {isa = XCSwiftPackageProductDependency; package = D2A45DA4B05998CBBA40CAA7 /* GoogleSignIn-iOS */; productName = GoogleSignInSwift; };
 /* End XCSwiftPackageProductDependency section */
 
 /* Begin XCBuildConfiguration section */
 		A1000000000000000000002F /* Debug */ = {
 			isa = XCBuildConfiguration;
 			buildSettings = {
 				ALWAYS_SEARCH_USER_PATHS = NO;
 				ASSETCATALOG_COMPILER_GENERATE_SWIFT_ASSET_SYMBOL_EXTENSIONS = YES;
 				CLANG_ANALYZER_NONNULL = YES;
 				CLANG_ANALYZER_NUMBER_OBJECT_CONVERSION = YES_AGGRESSIVE;
 				CLANG_CXX_LANGUAGE_STANDARD = "gnu++20";
 				CLANG_ENABLE_MODULES = YES;
 				CLANG_ENABLE_OBJC_ARC = YES;
@@ -1130,81 +1196,82 @@
 				GCC_C_LANGUAGE_STANDARD = gnu17;
 				GCC_NO_COMMON_BLOCKS = YES;
 				GCC_WARN_64_TO_32_BIT_CONVERSION = YES;
 				GCC_WARN_ABOUT_RETURN_TYPE = YES_ERROR;
 				GCC_WARN_UNDECLARED_SELECTOR = YES;
 				GCC_WARN_UNINITIALIZED_AUTOS = YES_AGGRESSIVE;
 				GCC_WARN_UNUSED_FUNCTION = YES;
 				GCC_WARN_UNUSED_VARIABLE = YES;
 				IPHONEOS_DEPLOYMENT_TARGET = 17.0;
 				LOCALIZATION_PREFERS_STRING_CATALOGS = YES;
 				MTL_ENABLE_DEBUG_INFO = NO;
 				MTL_FAST_MATH = YES;
 				SDKROOT = iphoneos;
 				SWIFT_COMPILATION_MODE = wholemodule;
 				VALIDATE_PRODUCT = YES;
 			};
 			name = Release;
 		};
 		A10000000000000000000031 /* Debug */ = {
 			isa = XCBuildConfiguration;
 			baseConfigurationReference = ACCA00000000000000000001 /* Account.xcconfig */;
 			buildSettings = {
 				API_BASE_URL = "https://api.mavrylo.com";
 				APP_ATTEST_ENVIRONMENT = development;
 				ASSETCATALOG_COMPILER_APPICON_NAME = AppIcon;
 				ASSETCATALOG_COMPILER_GLOBAL_ACCENT_COLOR_NAME = AccentColor;
 				CODE_SIGN_ENTITLEMENTS = Resources/FlashCardAI.entitlements;
 				CODE_SIGN_STYLE = Automatic;
 				CURRENT_PROJECT_VERSION = 1;
 				DEVELOPMENT_TEAM = 8DTMM37PGV;
 				ENABLE_PREVIEWS = YES;
 				ENABLE_USER_SCRIPT_SANDBOXING = NO;
 				GENERATE_INFOPLIST_FILE = NO;
 				INFOPLIST_FILE = Resources/Info.plist;
 				INFOPLIST_KEY_UIApplicationSceneManifest_Generation = YES;
 				LD_RUNPATH_SEARCH_PATHS = (
 					"$(inherited)",
 					"@executable_path/Frameworks",
 				);
 				MARKETING_VERSION = 1.0;
-				PRODUCT_BUNDLE_IDENTIFIER = com.mavrylo.owlai;
+				OWL_DEBUG_BUNDLE_ID = com.mavrylo.owlai;
+				PRODUCT_BUNDLE_IDENTIFIER = "$(OWL_DEBUG_BUNDLE_ID)";
 				PRODUCT_NAME = "$(TARGET_NAME)";
 				SWIFT_EMIT_LOC_STRINGS = YES;
 				SWIFT_VERSION = 5.0;
 				TARGETED_DEVICE_FAMILY = "1,2";
 			};
 			name = Debug;
 		};
 		A10000000000000000000032 /* Release */ = {
 			isa = XCBuildConfiguration;
 			baseConfigurationReference = ACCA00000000000000000001 /* Account.xcconfig */;
 			buildSettings = {
 				API_BASE_URL = "https://api.mavrylo.com";
 				APP_ATTEST_ENVIRONMENT = production;
 				ASSETCATALOG_COMPILER_APPICON_NAME = AppIcon;
 				ASSETCATALOG_COMPILER_GLOBAL_ACCENT_COLOR_NAME = AccentColor;
 				CODE_SIGN_ENTITLEMENTS = Resources/FlashCardAI.entitlements;
 				CODE_SIGN_STYLE = Automatic;
 				CURRENT_PROJECT_VERSION = 1;
 				DEVELOPMENT_TEAM = 8DTMM37PGV;
 				ENABLE_PREVIEWS = YES;
 				ENABLE_USER_SCRIPT_SANDBOXING = NO;
 				GENERATE_INFOPLIST_FILE = NO;
 				INFOPLIST_FILE = Resources/Info.plist;
 				INFOPLIST_KEY_UIApplicationSceneManifest_Generation = YES;
 				LD_RUNPATH_SEARCH_PATHS = (
 					"$(inherited)",
 					"@executable_path/Frameworks",
 				);
 				MARKETING_VERSION = 1.0;
 				PRODUCT_BUNDLE_IDENTIFIER = com.mavrylo.owlai;
 				PRODUCT_NAME = "$(TARGET_NAME)";
 				SWIFT_EMIT_LOC_STRINGS = YES;
 				SWIFT_VERSION = 5.0;
 				TARGETED_DEVICE_FAMILY = "1,2";
 			};
 			name = Release;
 		};
 		D3110000000000000000000C /* Debug */ = {
 			isa = XCBuildConfiguration;
 			buildSettings = {
@@ -1216,74 +1283,115 @@
 				IPHONEOS_DEPLOYMENT_TARGET = 17.0;
 				LD_RUNPATH_SEARCH_PATHS = (
 					"$(inherited)",
 					"@executable_path/Frameworks",
 					"@loader_path/Frameworks",
 				);
 				MARKETING_VERSION = 1.0;
 				PRODUCT_BUNDLE_IDENTIFIER = com.flashcardai.FlashCardAITests;
 				PRODUCT_NAME = "$(TARGET_NAME)";
 				SDKROOT = iphoneos;
 				SWIFT_VERSION = 5.0;
 				TARGETED_DEVICE_FAMILY = "1,2";
 				TEST_HOST = "$(BUILT_PRODUCTS_DIR)/FlashCardAI.app/$(BUNDLE_EXECUTABLE_FOLDER_PATH)/FlashCardAI";
 			};
 			name = Debug;
 		};
 		D3110000000000000000000D /* Release */ = {
 			isa = XCBuildConfiguration;
 			buildSettings = {
 				BUNDLE_LOADER = "$(TEST_HOST)";
 				CODE_SIGN_STYLE = Automatic;
 				CURRENT_PROJECT_VERSION = 1;
 				DEVELOPMENT_TEAM = "";
 				GENERATE_INFOPLIST_FILE = YES;
 				IPHONEOS_DEPLOYMENT_TARGET = 17.0;
 				LD_RUNPATH_SEARCH_PATHS = (
 					"$(inherited)",
 					"@executable_path/Frameworks",
 					"@loader_path/Frameworks",
 				);
 				MARKETING_VERSION = 1.0;
 				PRODUCT_BUNDLE_IDENTIFIER = com.flashcardai.FlashCardAITests;
 				PRODUCT_NAME = "$(TARGET_NAME)";
 				SDKROOT = iphoneos;
 				SWIFT_VERSION = 5.0;
 				TARGETED_DEVICE_FAMILY = "1,2";
 				TEST_HOST = "$(BUILT_PRODUCTS_DIR)/FlashCardAI.app/$(BUNDLE_EXECUTABLE_FOLDER_PATH)/FlashCardAI";
 			};
 			name = Release;
 		};
+		B7110000000000000000000E /* Debug */ = {
+			isa = XCBuildConfiguration;
+			buildSettings = {
+				CODE_SIGN_STYLE = Automatic;
+				GENERATE_INFOPLIST_FILE = YES;
+				IPHONEOS_DEPLOYMENT_TARGET = 17.0;
+				MARKETING_VERSION = 1.0;
+				PRODUCT_BUNDLE_IDENTIFIER = com.flashcardai.FlashCardAIUITests;
+				PRODUCT_NAME = "$(TARGET_NAME)";
+				SDKROOT = iphoneos;
+				SWIFT_VERSION = 5.0;
+				TARGETED_DEVICE_FAMILY = "1,2";
+				TEST_TARGET_NAME = FlashCardAI;
+			};
+			name = Debug;
+		};
+		B7110000000000000000000F /* Release */ = {
+			isa = XCBuildConfiguration;
+			buildSettings = {
+				CODE_SIGN_STYLE = Automatic;
+				GENERATE_INFOPLIST_FILE = YES;
+				IPHONEOS_DEPLOYMENT_TARGET = 17.0;
+				MARKETING_VERSION = 1.0;
+				PRODUCT_BUNDLE_IDENTIFIER = com.flashcardai.FlashCardAIUITests;
+				PRODUCT_NAME = "$(TARGET_NAME)";
+				SDKROOT = iphoneos;
+				SWIFT_VERSION = 5.0;
+				TARGETED_DEVICE_FAMILY = "1,2";
+				TEST_TARGET_NAME = FlashCardAI;
+			};
+			name = Release;
+		};
 /* End XCBuildConfiguration section */
 
 /* Begin XCConfigurationList section */
 		A1000000000000000000002A /* Build configuration list for PBXNativeTarget "FlashCardAI" */ = {
 			isa = XCConfigurationList;
 			buildConfigurations = (
 				A10000000000000000000031 /* Debug */,
 				A10000000000000000000032 /* Release */,
 			);
 			defaultConfigurationIsVisible = 0;
 			defaultConfigurationName = Release;
 		};
 		A1000000000000000000002E /* Build configuration list for PBXProject "FlashCardAI" */ = {
 			isa = XCConfigurationList;
 			buildConfigurations = (
 				A1000000000000000000002F /* Debug */,
 				A10000000000000000000030 /* Release */,
 			);
 			defaultConfigurationIsVisible = 0;
 			defaultConfigurationName = Release;
 		};
 		D31100000000000000000007 /* Build configuration list for PBXNativeTarget "FlashCardAITests" */ = {
 			isa = XCConfigurationList;
 			buildConfigurations = (
 				D3110000000000000000000C /* Debug */,
 				D3110000000000000000000D /* Release */,
 			);
 			defaultConfigurationIsVisible = 0;
 			defaultConfigurationName = Release;
 		};
+		B71100000000000000000009 /* Build configuration list for PBXNativeTarget "FlashCardAIUITests" */ = {
+			isa = XCConfigurationList;
+			buildConfigurations = (
+				B7110000000000000000000E /* Debug */,
+				B7110000000000000000000F /* Release */,
+			);
+			defaultConfigurationIsVisible = 0;
+			defaultConfigurationName = Release;
+		};
 /* End XCConfigurationList section */
 	};
 	rootObject = A1000000000000000000002D /* Project object */;
 }
diff --git a/FlashCardAI.xcodeproj/xcshareddata/xcschemes/FlashCardAI.xcscheme b/FlashCardAI.xcodeproj/xcshareddata/xcschemes/FlashCardAI.xcscheme
index 5e4b7b2..84a34ad 100644
--- a/FlashCardAI.xcodeproj/xcshareddata/xcschemes/FlashCardAI.xcscheme
+++ b/FlashCardAI.xcodeproj/xcshareddata/xcschemes/FlashCardAI.xcscheme
@@ -1,96 +1,121 @@
 <?xml version="1.0" encoding="UTF-8"?>
 <Scheme
    LastUpgradeVersion = "1500"
    version = "1.7">
    <BuildAction
       parallelizeBuildables = "YES"
       buildImplicitDependencies = "YES">
       <BuildActionEntries>
          <BuildActionEntry
             buildForTesting = "YES"
             buildForRunning = "YES"
             buildForProfiling = "YES"
             buildForArchiving = "YES"
             buildForAnalyzing = "YES">
             <BuildableReference
                BuildableIdentifier = "primary"
                BlueprintIdentifier = "A10000000000000000000029"
                BuildableName = "FlashCardAI.app"
                BlueprintName = "FlashCardAI"
                ReferencedContainer = "container:FlashCardAI.xcodeproj">
             </BuildableReference>
          </BuildActionEntry>
          <BuildActionEntry
             buildForTesting = "YES"
             buildForRunning = "NO"
             buildForProfiling = "NO"
             buildForArchiving = "NO"
             buildForAnalyzing = "NO">
             <BuildableReference
                BuildableIdentifier = "primary"
                BlueprintIdentifier = "D31100000000000000000006"
                BuildableName = "FlashCardAITests.xctest"
                BlueprintName = "FlashCardAITests"
                ReferencedContainer = "container:FlashCardAI.xcodeproj">
             </BuildableReference>
          </BuildActionEntry>
+         <BuildActionEntry
+            buildForTesting = "YES"
+            buildForRunning = "NO"
+            buildForProfiling = "NO"
+            buildForArchiving = "NO"
+            buildForAnalyzing = "NO">
+            <BuildableReference
+               BuildableIdentifier = "primary"
+               BlueprintIdentifier = "B71100000000000000000008"
+               BuildableName = "FlashCardAIUITests.xctest"
+               BlueprintName = "FlashCardAIUITests"
+               ReferencedContainer = "container:FlashCardAI.xcodeproj">
+            </BuildableReference>
+         </BuildActionEntry>
       </BuildActionEntries>
    </BuildAction>
    <TestAction
       buildConfiguration = "Debug"
       selectedDebuggerIdentifier = "Xcode.DebuggerFoundation.Debugger.LLDB"
       selectedLauncherIdentifier = "Xcode.DebuggerFoundation.Launcher.LLDB"
       shouldUseLaunchSchemeArgsEnv = "NO"
       shouldAutocreateTestPlan = "YES">
       <Testables>
          <TestableReference
             skipped = "NO"
             parallelizable = "YES">
             <BuildableReference
                BuildableIdentifier = "primary"
                BlueprintIdentifier = "D31100000000000000000006"
                BuildableName = "FlashCardAITests.xctest"
                BlueprintName = "FlashCardAITests"
                ReferencedContainer = "container:FlashCardAI.xcodeproj">
             </BuildableReference>
          </TestableReference>
+         <TestableReference
+            skipped = "NO"
+            parallelizable = "NO">
+            <BuildableReference
+               BuildableIdentifier = "primary"
+               BlueprintIdentifier = "B71100000000000000000008"
+               BuildableName = "FlashCardAIUITests.xctest"
+               BlueprintName = "FlashCardAIUITests"
+               ReferencedContainer = "container:FlashCardAI.xcodeproj">
+            </BuildableReference>
+         </TestableReference>
       </Testables>
    </TestAction>
    <LaunchAction
       buildConfiguration = "Debug"
       selectedDebuggerIdentifier = "Xcode.DebuggerFoundation.Debugger.LLDB"
       selectedLauncherIdentifier = "Xcode.DebuggerFoundation.Launcher.LLDB"
       launchStyle = "0"
       useCustomWorkingDirectory = "NO"
       ignoresPersistentStateOnLaunch = "NO"
       debugDocumentVersioning = "YES"
       debugServiceExtension = "internal"
       allowLocationSimulation = "YES">
       <BuildableProductRunnable
          runnableDebuggingMode = "0">
          <BuildableReference
             BuildableIdentifier = "primary"
             BlueprintIdentifier = "A10000000000000000000029"
             BuildableName = "FlashCardAI.app"
             BlueprintName = "FlashCardAI"
             ReferencedContainer = "container:FlashCardAI.xcodeproj">
          </BuildableReference>
       </BuildableProductRunnable>
    </LaunchAction>
    <ProfileAction
       buildConfiguration = "Release"
       shouldUseLaunchSchemeArgsEnv = "YES"
       savedToolIdentifier = ""
       useCustomWorkingDirectory = "NO"
       debugDocumentVersioning = "YES">
       <BuildableProductRunnable
          runnableDebuggingMode = "0">
          <BuildableReference
             BuildableIdentifier = "primary"
             BlueprintIdentifier = "A10000000000000000000029"
             BuildableName = "FlashCardAI.app"
             BlueprintName = "FlashCardAI"
             ReferencedContainer = "container:FlashCardAI.xcodeproj">
          </BuildableReference>
       </BuildableProductRunnable>
    </ProfileAction>
diff --git a/FlashCardAITests/AccountViewTests.swift b/FlashCardAITests/AccountViewTests.swift
index aa0b3ea..de716ef 100644
--- a/FlashCardAITests/AccountViewTests.swift
+++ b/FlashCardAITests/AccountViewTests.swift
@@ -1,308 +1,137 @@
 import SwiftUI
 import UIKit
 import XCTest
 @testable import FlashCardAI
 
 final class GoogleAccountConfigurationTests: XCTestCase {
     func testRequiresBothClientIDsAndMatchingCallbackScheme() {
         let ios = "123-ios.apps.googleusercontent.com"
         let server = "123-web.apps.googleusercontent.com"
         XCTAssertNil(GoogleAccountConfiguration.validated(clientID: "$(GOOGLE_IOS_CLIENT_ID)", serverClientID: server, callbackSchemes: []))
         XCTAssertNil(GoogleAccountConfiguration.validated(clientID: ios, serverClientID: nil, callbackSchemes: []))
         XCTAssertNil(GoogleAccountConfiguration.validated(clientID: ios, serverClientID: server, callbackSchemes: ["wrong"]))
         XCTAssertNotNil(GoogleAccountConfiguration.validated(clientID: ios, serverClientID: server, callbackSchemes: ["com.googleusercontent.apps.123-ios"]))
     }
 }
 
 @MainActor
 final class AccountViewTests: XCTestCase {
-    func testSuccessfulRegistrationPresentsAccountCreatedNotice() async throws {
-        let defaults = try XCTUnwrap(UserDefaults(suiteName: "AccountSuccessTests.\(UUID())"))
-        let settings = AppSettingsStore(defaults: defaults)
+    func testEmailRegistrationAndLoginMaintainDistinctSessionState() async {
         let profile = AccountProfile(id: "email-user", email: "learner@example.com", displayName: nil, provider: "email")
-        let session = AccountSessionController(api: AccountViewAPI(storedProfile: profile), credentials: AccountViewCredentials())
-        let host = UIHostingController(rootView: NavigationStack { AccountView() }
-            .environmentObject(settings).environmentObject(session))
-        let window = makeWindow(width: 390, height: 844)
-        window.rootViewController = host
-        window.makeKeyAndVisible()
-        defer { window.isHidden = true }
-        settle(host)
-        try requireSwiftUIAccessibility(in: host.view)
-        let create = try XCTUnwrap(element("Create account", in: host.view))
-        XCTAssertTrue(create.accessibilityActivate())
-        settle(host)
-        await session.register(email: "learner@example.com", password: "correct horse", confirmPassword: "correct horse")
-        settle(host)
-        XCTAssertTrue(session.accountCreated)
-        XCTAssertNotNil(element("Account created", in: window))
-        XCTAssertNotNil(element("Continue", in: window))
-        // UIKit alert actions do not implement in-process accessibilityActivate.
-        // Continue and the ensuing navigation are exercised by external UI automation.
-        XCTAssertEqual(session.profile, profile)
-        attach(host, name: "Email account created notice")
-    }
+        let registration = AccountSessionController(
+            api: AccountViewAPI(storedProfile: profile), credentials: AccountViewCredentials()
+        )
+        await registration.register(email: "learner@example.com", password: "correct horse", confirmPassword: "correct horse")
+        XCTAssertEqual(registration.profile, profile)
+        XCTAssertTrue(registration.accountCreated)
 
-    func testEmailLoginClosesFormAndShowsEmailProfile() async throws {
-        let defaults = try XCTUnwrap(UserDefaults(suiteName: "AccountLoginTests.\(UUID())"))
-        let settings = AppSettingsStore(defaults: defaults)
-        let profile = AccountProfile(id: "email-user", email: "learner@example.com", displayName: nil, provider: "email")
-        let session = AccountSessionController(api: AccountViewAPI(storedProfile: profile), credentials: AccountViewCredentials())
-        let host = UIHostingController(rootView: NavigationStack { AccountView() }
-            .environmentObject(settings).environmentObject(session))
-        let window = makeWindow(width: 390, height: 844)
-        window.rootViewController = host
-        window.makeKeyAndVisible()
-        defer { window.isHidden = true }
-        settle(host)
-        try requireSwiftUIAccessibility(in: host.view)
-        let login = try XCTUnwrap(element("Log in", in: host.view))
-        XCTAssertTrue(login.accessibilityActivate())
-        settle(host)
-        XCTAssertNotNil(element("Email", in: host.view))
-        XCTAssertNotNil(element("Password", in: host.view))
-        XCTAssertNil(element("Confirm password", in: host.view))
-        await session.signIn(email: "learner@example.com", password: "correct horse")
-        settle(host)
-        XCTAssertNotNil(element("Signed in with email", in: host.view))
-        XCTAssertFalse(session.accountCreated)
-        attach(host, name: "Email login profile")
+        let login = AccountSessionController(
+            api: AccountViewAPI(storedProfile: profile), credentials: AccountViewCredentials()
+        )
+        await login.signIn(email: "learner@example.com", password: "correct horse")
+        XCTAssertEqual(login.profile, profile)
+        XCTAssertFalse(login.accountCreated)
     }
 
     func testInvalidRegistrationShowsFieldErrorsWithoutCallingBackend() async {
         let api = AccountViewAPI()
         let session = AccountSessionController(api: api, credentials: AccountViewCredentials())
         let model = AccountViewModel(identity: CancelledGoogleIdentity())
         model.email = "bad address"
         model.password = "short"
         model.confirmPassword = "different"
         XCTAssertFalse(model.submitEmail(create: true, session: session))
         XCTAssertNotNil(model.fieldErrors[.email])
         XCTAssertNotNil(model.fieldErrors[.password])
         XCTAssertNotNil(model.fieldErrors[.confirmation])
         let count = await api.emailRequestCount
         XCTAssertEqual(count, 0)
     }
 
     func testEmailValidationNormalizesAndRejectsMalformedAddresses() {
         XCTAssertEqual(AccountFormValidation.normalizedEmail("  Learner@Example.COM \n"), "learner@example.com")
         for email in ["learner@example.com", "first.last+study@sub.example.co.uk"] {
             XCTAssertTrue(AccountFormValidation.isValidEmail(email), email)
         }
         for email in ["", "learner", "a@localhost", "a@@example.com", ".a@example.com", "a..b@example.com", "a@-example.com", "a@example..com", "a b@example.com", "名@example.com", String(repeating: "a", count:65) + "@example.com"] {
             XCTAssertFalse(AccountFormValidation.isValidEmail(email), email)
         }
     }
 
     func testPasswordValidationUsesUnicodeScalarsAndPreservesSpaces() {
         XCTAssertTrue(AccountFormValidation.isValidNewPassword("  pass  "))
         XCTAssertTrue(AccountFormValidation.isValidNewPassword(String(repeating: "🔐", count:64)))
         XCTAssertFalse(AccountFormValidation.isValidNewPassword(String(repeating: "🔐", count:65)))
         XCTAssertFalse(AccountFormValidation.isValidNewPassword("        "))
         XCTAssertFalse(AccountFormValidation.isValidNewPassword("short"))
     }
 
     func testLoginDoesNotRequireConfirmationOrRejectLegacyShortPassword() {
         let model = AccountViewModel(identity: CancelledGoogleIdentity())
         model.email = "learner@example.com"
         model.password = "short"
         XCTAssertTrue(model.validateEmailForm(create: false))
         XCTAssertTrue(model.fieldErrors.isEmpty)
     }
 
-    func testAccountOffersRegistrationWithEmailPasswordConfirmationAndGoogle() throws {
-        let defaults = try XCTUnwrap(UserDefaults(suiteName: "AccountViewTests.\(UUID())"))
-        let settings = AppSettingsStore(defaults: defaults)
-        let session = AccountSessionController(api: AccountViewAPI(), credentials: AccountViewCredentials())
-        let host = UIHostingController(rootView: NavigationStack { AccountView() }
-            .environmentObject(settings).environmentObject(session))
-        let window = makeWindow(width: 390, height: 844)
-        window.rootViewController = host
-        window.makeKeyAndVisible()
-        defer { window.isHidden = true }
-        settle(host)
-        try requireSwiftUIAccessibility(in: host.view)
-        let create = try XCTUnwrap(element("Create account", in: host.view))
-        XCTAssertNotNil(element("Log in", in: host.view))
-        attach(host, name: "Account welcome")
-        XCTAssertTrue(create.accessibilityActivate())
-        settle(host)
-        XCTAssertNotNil(element("Continue with Google", in: host.view))
-        XCTAssertNotNil(element("Email", in: host.view))
-        XCTAssertNotNil(element("Password", in: host.view))
-        XCTAssertNotNil(element("Confirm password", in: host.view))
-        let showPassword = try XCTUnwrap(element("Show password", in: host.view))
-        XCTAssertTrue(showPassword.accessibilityActivate())
-        settle(host)
-        XCTAssertNotNil(element("Hide password", in: host.view))
-        XCTAssertNil(element("Continue with Apple", in: host.view))
-        attach(host, name: "Create account email form")
-    }
-
-    func testDarkProfileAndLargeTextWelcomeRemainAccessible() async throws {
-        let defaults = try XCTUnwrap(UserDefaults(suiteName: "AccountLayoutTests.\(UUID())"))
-        let settings = AppSettingsStore(defaults: defaults)
-        settings.syncColorScheme(.dark)
-        let profile = AccountProfile(id: "test-user", email: "learner@example.com", displayName: "Owl Learner", provider: "google")
-        let credentials = AccountViewCredentials()
-        credentials.session = AccountSession(accessToken: "test-access", accessTokenExpiresAt: Date().addingTimeInterval(900), refreshToken: "test-refresh", profile: profile)
-        let session = AccountSessionController(api: AccountViewAPI(storedProfile: profile), credentials: credentials)
-        await session.restore()
-        let host = UIHostingController(rootView: NavigationStack { AccountView() }
-            .environmentObject(settings).environmentObject(session)
-            .environment(\.sizeCategory, .accessibilityExtraExtraLarge)
-            .preferredColorScheme(.dark))
-        let window = makeWindow(width: 320, height: 740)
-        window.rootViewController = host
-        window.makeKeyAndVisible()
-        defer { window.isHidden = true }
-        settle(host)
-        try requireSwiftUIAccessibility(in: host.view)
-        XCTAssertNotNil(element("Sign out", in: host.view))
-        XCTAssertNotNil(element("Delete account", in: host.view))
-        attach(host, name: "Account dark large text profile")
-        await session.signOut()
-        settle(host)
-        XCTAssertNotNil(element("Create account", in: host.view))
-        attach(host, name: "Account dark large text welcome")
-    }
-
     func testGoogleCancellationDoesNotShowAnErrorOrCallBackend() async {
         let api = AccountViewAPI()
         let session = AccountSessionController(api: api, credentials: AccountViewCredentials())
         let model = AccountViewModel(identity: CancelledGoogleIdentity())
         model.signIn(session: session, presenting: UIViewController())
         for _ in 0..<100 where model.isAuthorizing { await Task.yield() }
         XCTAssertFalse(model.isAuthorizing)
         XCTAssertNil(session.errorMessage)
         XCTAssertNil(session.profile)
         let calls = await api.signInCount
         XCTAssertEqual(calls, 0)
     }
 
     func testUnconfiguredGoogleDoesNotStartProviderOrBackend() {
         let session = AccountSessionController(api: AccountViewAPI(), credentials: AccountViewCredentials())
         let model = AccountViewModel(identity: CancelledGoogleIdentity(isConfigured: false))
         model.signIn(session: session, presenting: UIViewController())
         XCTAssertFalse(model.isAuthorizing)
         XCTAssertNotNil(session.errorMessage)
         XCTAssertNil(session.profile)
     }
-
-    private func makeWindow(width: CGFloat, height: CGFloat) -> UIWindow {
-        let frame = CGRect(x: 0, y: 0, width: width, height: height)
-        guard let scene = UIApplication.shared.connectedScenes.compactMap({ $0 as? UIWindowScene }).first else {
-            return UIWindow(frame: frame)
-        }
-        let window = UIWindow(windowScene: scene)
-        window.frame = frame
-        return window
-    }
-
-    private func requireSwiftUIAccessibility(in view: UIView) throws {
-        func exposesElements(_ view: UIView) -> Bool {
-            let count = view.accessibilityElementCount()
-            return (count != NSNotFound && count > 0) || view.subviews.contains(where: exposesElements)
-        }
-        guard exposesElements(view) else {
-            throw XCTSkip("This simulator runtime does not expose SwiftUI accessibility containers to in-process XCTest. Exercise this flow with external UI automation.")
-        }
-    }
-
-    private func settle<V: View>(_ host: UIHostingController<V>) {
-        host.view.layoutIfNeeded()
-        RunLoop.main.run(until: Date().addingTimeInterval(0.5))
-        host.view.layoutIfNeeded()
-    }
-
-    private func attach<V: View>(_ host: UIHostingController<V>, name: String) {
-        let image = UIGraphicsImageRenderer(bounds: host.view.bounds).image { _ in
-            host.view.drawHierarchy(in: host.view.bounds, afterScreenUpdates: true)
-        }
-        let attachment = XCTAttachment(image: image)
-        attachment.name = name
-        attachment.lifetime = .keepAlways
-        add(attachment)
-    }
-
-    private func element(_ label: String, in object: NSObject) -> NSObject? {
-        var visited = Set<ObjectIdentifier>()
-        func walk(_ object: NSObject) -> NSObject? {
-            guard visited.insert(ObjectIdentifier(object)).inserted else { return nil }
-            if object.accessibilityLabel == label { return object }
-            let count = object.accessibilityElementCount()
-            if count != NSNotFound, count > 0 {
-                for index in 0..<count {
-                    if let child = object.accessibilityElement(at: index) as? NSObject,
-                       let match = walk(child) { return match }
-                }
-            }
-            if let view = object as? UIView {
-                for child in view.subviews { if let match = walk(child) { return match } }
-            }
-            return nil
-        }
-        if let match = walk(object) { return match }
-        // SwiftUI exposes only the visible portion of a scroll view to accessibility.
-        // Search the content as a user would, including at accessibility text sizes.
-        guard let root = object as? UIView else { return nil }
-        func scrollViews(_ view: UIView) -> [UIScrollView] {
-            (view as? UIScrollView).map { [$0] } ?? view.subviews.flatMap(scrollViews)
-        }
-        for scroll in scrollViews(root) {
-            let top = -scroll.adjustedContentInset.top
-            let bottom = max(top, scroll.contentSize.height - scroll.bounds.height + scroll.adjustedContentInset.bottom)
-            let step = max(100, scroll.bounds.height * 0.6)
-            var y = top
-            for _ in 0..<30 {
-                scroll.setContentOffset(CGPoint(x: 0, y: y), animated: false)
-                root.layoutIfNeeded()
-                RunLoop.main.run(until: Date().addingTimeInterval(0.05))
-                visited = []
-                if let match = walk(object) { return match }
-                if y >= bottom { break }
-                y = min(bottom, y + step)
-            }
-        }
-        return nil
-    }
 }
 
 @MainActor
 private struct CancelledGoogleIdentity: AccountIdentityProviding {
     var isConfigured = true
     func signIn(presenting: UIViewController) async throws -> String { throw CancellationError() }
     func signOut() {}
 }
 
 private actor AccountViewAPI: AccountAPIProviding {
     let storedProfile: AccountProfile?
     init(storedProfile: AccountProfile? = nil) { self.storedProfile = storedProfile }
     private(set) var signInCount = 0
     private(set) var emailRequestCount = 0
     func register(email: String, password: String, confirmPassword: String) async throws -> AccountSession {
         emailRequestCount += 1
         guard let storedProfile else { throw AccountAPIError.emailExists }
         return AccountSession(accessToken: "created-access", accessTokenExpiresAt: Date().addingTimeInterval(900), refreshToken: "created-refresh", profile: storedProfile)
     }
     func signIn(email: String, password: String) async throws -> AccountSession {
         emailRequestCount += 1
         guard let storedProfile else { throw AccountAPIError.invalidCredentials }
         return AccountSession(accessToken: "login-access", accessTokenExpiresAt: Date().addingTimeInterval(900), refreshToken: "login-refresh", profile: storedProfile)
     }
     func signIn(idToken: String) async throws -> AccountSession { signInCount += 1; throw URLError(.badServerResponse) }
     func refresh(refreshToken: String) async throws -> AccountSession { throw URLError(.badServerResponse) }
     func signOut(refreshToken: String) async throws {}
     func profile(accessToken: String) async throws -> AccountProfile {
         guard let storedProfile else { throw URLError(.badServerResponse) }
         return storedProfile
     }
     func deleteAccount(accessToken: String) async throws {}
 }
 
 @MainActor
 private final class AccountViewCredentials: AccountCredentialStoring {
     var session: AccountSession?
     func load() throws -> AccountSession? { session }
     func save(_ session: AccountSession) throws { self.session = session }
     func clear() throws { session = nil }
diff --git a/FlashCardAITests/ApplicationBehaviorTests.swift b/FlashCardAITests/ApplicationBehaviorTests.swift
index 979f242..e7e807e 100644
--- a/FlashCardAITests/ApplicationBehaviorTests.swift
+++ b/FlashCardAITests/ApplicationBehaviorTests.swift
@@ -1,75 +1,61 @@
 import Foundation
 import Combine
 import SQLite3
 import SwiftUI
 import UIKit
 import XCTest
 @testable import FlashCardAI
 
 @MainActor
 private func successfulPaywallActions(
     onLoad: @escaping () -> Void = {}
 ) -> ProductPaywallActions {
     ProductPaywallActions(
         isBusy: false,
         hasProducts: true,
         loadProducts: { onLoad() },
         purchase: { _ in .success },
         restore: { .success }
     )
 }
 
-private struct OnboardingCompletionDefaultsSnapshot: Equatable {
-    let settingsOnboarding: Bool?
-    let productOnboarding: Bool?
-
-    init() {
-        settingsOnboarding = UserDefaults.standard.object(
-            forKey: "FlashCardAI.hasCompletedSettingsOnboarding"
-        ) as? Bool
-        productOnboarding = UserDefaults.standard.object(
-            forKey: "FlashCardAI.hasCompletedProductOnboarding"
-        ) as? Bool
-    }
-}
-
 final class ProductPaletteTests: XCTestCase {
     func testPrimaryPurpleMatchesApprovedColorInLightAndDarkMode() throws {
         try assertApprovedPurple(ProductPalette.primaryPurple)
     }
 
     func testReviewRevealUsesSharedPrimaryPurple() throws {
         try assertApprovedPurple(ReviewSessionView.revealAnswerBackground)
     }
 
     private func assertApprovedPurple(_ color: Color) throws {
         for style in [
             UIUserInterfaceStyle.light,
             UIUserInterfaceStyle.dark,
         ] {
             let resolved = UIColor(color).resolvedColor(
                 with: UITraitCollection(userInterfaceStyle: style)
             )
             var red: CGFloat = 0
             var green: CGFloat = 0
             var blue: CGFloat = 0
             var alpha: CGFloat = 0
 
             XCTAssertTrue(
                 resolved.getRed(
                     &red,
                     green: &green,
                     blue: &blue,
                     alpha: &alpha
                 )
             )
             XCTAssertEqual(red, 0x34 / 255.0, accuracy: 0.001)
             XCTAssertEqual(green, 0x2B / 255.0, accuracy: 0.001)
             XCTAssertEqual(blue, 0x99 / 255.0, accuracy: 0.001)
             XCTAssertEqual(alpha, 1, accuracy: 0.001)
         }
     }
 }
 
 final class PublicFlashcardSetDTOTests: XCTestCase {
     func testUpsertRequestEncodesThePublicCatalogWireContract() throws {
@@ -4330,289 +4316,117 @@ final class LibraryViewHostedTests: XCTestCase {
         XCTAssertEqual(local.summaryReadCount, 2)
     }
 
     func testRefreshFailureDoesNotRenderATopLevelNotice()
         async throws {
         let fixture = try makeCompositionFixture()
         defer { fixture.cleanup() }
         let local = LibraryLocalStoreFake()
         let publicSets = LibraryPublicSetServiceFake()
         publicSets.refreshResults = [.failure(.refresh)]
         publicSets.catalogResults = [.failure(.catalog)]
         let vm = LibraryViewModel(
             local: local,
             publicSets: publicSets
         )
         let host = UIHostingController(
             rootView: LibraryView(
                 repository: fixture.composition.words,
                 publicSets: publicSets,
                 viewModel: vm
             )
             .environmentObject(fixture.composition.appSettings)
         )
         let window = hostInWindow(host)
         defer { window.isHidden = true }
 
         let message = "Couldn’t update Library. Pull down to try again."
         await waitUntil {
             publicSets.catalogQueries.count == 1 && !vm.isRefreshing
         }
         host.view.layoutIfNeeded()
 
         let labels = accessibilityLabels(in: host.view)
         XCTAssertFalse(labels.contains(message))
         XCTAssertNil(accessibilityElement(label: message, in: host.view))
         XCTAssertFalse(
             labels.contains { $0.localizedCaseInsensitiveContains("retry") }
         )
     }
 
-    func testLibrarySearchExpandsFocusesFiltersLocallyAndClosesCleanly()
-        async throws {
-        let fixture = try makeCompositionFixture()
-        defer { fixture.cleanup() }
-        let local = LibraryLocalStoreFake()
-        let publicSets = LibraryPublicSetServiceFake()
-        publicSets.catalogResults = [
-            .success([
-                Self.catalogItem(id: "public-travel", title: "TRAVEL"),
-                Self.catalogItem(id: "public-food", title: "Cooking"),
-            ])
-        ]
-        let vm = LibraryViewModel(
-            local: local,
-            publicSets: publicSets
-        )
-        let host = UIHostingController(
-            rootView: LibraryView(
-                repository: fixture.composition.words,
-                publicSets: publicSets,
-                viewModel: vm
-            )
-            .environmentObject(fixture.composition.appSettings)
-        )
-        let window = hostInWindow(host)
-        defer { window.isHidden = true }
-
-        await waitUntil {
-            publicSets.catalogQueries.count == 1 && !vm.isRefreshing
-        }
-        XCTAssertNotNil(accessibilityElement(label: "Search Library", in: host.view))
-        XCTAssertNil(accessibilityElement(label: "Library search field", in: host.view))
-
-        let statusCount = publicSets.refreshCallCount
-        let catalogCount = publicSets.catalogQueries.count
-        let summaryCount = local.summaryReadCount
-        let search = try XCTUnwrap(
-            accessibilityElement(label: "Search Library", in: host.view)
-        )
-        XCTAssertTrue(search.accessibilityActivate())
-        await waitUntil { firstTextField(in: host.view)?.isFirstResponder == true }
-        XCTAssertNotNil(
-            accessibilityElement(label: "Library search field", in: host.view)
-        )
-
-        vm.searchText = "travel"
-        host.view.layoutIfNeeded()
-        XCTAssertEqual(vm.visibleCatalogSets.map(\.id), ["public-travel"])
-        XCTAssertEqual(publicSets.refreshCallCount, statusCount)
-        XCTAssertEqual(publicSets.catalogQueries.count, catalogCount)
-        XCTAssertEqual(local.summaryReadCount, summaryCount)
-
-        let close = try XCTUnwrap(
-            accessibilityElement(label: "Close Library search", in: host.view)
-        )
-        XCTAssertTrue(close.accessibilityActivate())
-        await waitUntil {
-            accessibilityElement(label: "Library search field", in: host.view) == nil
-        }
-        XCTAssertEqual(vm.searchText, "")
-        XCTAssertEqual(vm.visibleCatalogSets.count, 2)
-        XCTAssertNotNil(accessibilityElement(label: "Search Library", in: host.view))
-    }
-
-    func testActiveLibraryMenuOffersRemoveAndNoVisibilityActions()
-        async throws {
-        let fixture = try makeCompositionFixture()
-        defer { fixture.cleanup() }
-        let set = Self.localSummary(
-            id: "cat-u-active",
-            title: "Active set",
-            isActive: true
-        )
-        let local = LibraryLocalStoreFake(summaries: [set])
-        let publicSets = LibraryPublicSetServiceFake()
-        let vm = LibraryViewModel(local: local, publicSets: publicSets)
-        let host = UIHostingController(
-            rootView: LibraryView(
-                repository: fixture.composition.words,
-                publicSets: publicSets,
-                viewModel: vm
-            )
-            .environmentObject(fixture.composition.appSettings)
-        )
-        let window = hostInWindow(host)
-        defer { window.isHidden = true }
-        await waitUntil { local.summaryReadCount == 1 && !vm.isRefreshing }
-        await waitUntil {
-            host.view.layoutIfNeeded()
-            return accessibilityElement(
-                label: "Set options for Active set",
-                in: host.view
-            ) != nil
-        }
-
-        let menu = try XCTUnwrap(
-            accessibilityElement(
-                label: "Set options for Active set",
-                in: host.view
-            )
-        )
-        XCTAssertTrue(menu.accessibilityActivate())
-        await waitUntil {
-            accessibilityElement(
-                label: "Remove from learning",
-                in: window.windowScene?.windows ?? [window]
-            ) != nil
-        }
-        let windows = window.windowScene?.windows ?? [window]
-        let labels = windows.flatMap { accessibilityLabels(in: $0) }
-        XCTAssertFalse(labels.contains("Public"))
-        XCTAssertFalse(labels.contains("Private"))
-
-        // Hosted UIKit discovers SwiftUI Menu popup commands on this toolchain,
-        // but cannot programmatically activate their accessibility nodes.
-        XCTAssertNotNil(
-            accessibilityElement(label: "Remove from learning", in: windows)
-        )
-    }
-
-    private static func catalogItem(
-        id: String,
-        title: String
-    ) -> PublicFlashcardSetCatalogItem {
-        PublicFlashcardSetCatalogItem(
-            id: id,
-            title: title,
-            description: "Complete approved snapshot",
-            wordCount: 1,
-            cards: [
-                PublicFlashcardSetCard(
-                    clientCardId: "\(id)-card",
-                    word: "Ticket",
-                    translations: ["Billete"],
-                    pronunciation: nil,
-                    partOfSpeech: nil,
-                    examples: [],
-                    exampleTranslations: [],
-                    notes: nil,
-                    nativeLanguage: "es",
-                    learningLanguage: "en-us"
-                )
-            ],
-            updatedAt: "2026-08-03T15:16:17Z"
-        )
-    }
-
-    private static func localSummary(
-        id: String,
-        title: String,
-        isActive: Bool
-    ) -> FlashcardSetSummary {
-        FlashcardSetSummary(
-            id: id,
-            title: title,
-            wordCount: 2,
-            systemImage: "rectangle.stack.fill",
-            isActive: isActive,
-            description: "Description",
-            searchableText: title,
-            publicationStatus: .privateOnly
-        )
-    }
-
     private func makeCompositionFixture() throws -> HostedCompositionFixture {
         try HostedCompositionFixture(testCase: self)
     }
 
     private func hostInWindow<Content: View>(
         _ host: UIHostingController<Content>
     ) -> UIWindow {
         let frame = CGRect(x: 0, y: 0, width: 390, height: 844)
         let window: UIWindow
         if let scene = UIApplication.shared.connectedScenes
             .compactMap({ $0 as? UIWindowScene })
             .first {
             window = UIWindow(windowScene: scene)
             window.frame = frame
         } else {
             window = UIWindow(frame: frame)
         }
         window.rootViewController = host
         window.makeKeyAndVisible()
         host.view.frame = window.bounds
         host.view.setNeedsLayout()
         host.view.layoutIfNeeded()
         return window
     }
 
     private func firstRefreshControl(in root: UIView) -> UIRefreshControl? {
         if let refreshControl = root as? UIRefreshControl {
             return refreshControl
         }
         for subview in root.subviews {
             if let refreshControl = firstRefreshControl(in: subview) {
                 return refreshControl
             }
         }
         return nil
     }
 
-    private func firstTextField(in root: UIView) -> UITextField? {
-        if let field = root as? UITextField { return field }
-        for subview in root.subviews {
-            if let field = firstTextField(in: subview) { return field }
-        }
-        return nil
-    }
-
     private func waitUntil(
         _ condition: @MainActor () -> Bool,
         file: StaticString = #filePath,
         line: UInt = #line
     ) async {
         let clock = ContinuousClock()
         let deadline = clock.now.advanced(by: .seconds(1))
         while clock.now < deadline {
             if condition() { return }
             await Task.yield()
         }
         XCTFail("Timed out waiting for hosted Library condition", file: file, line: line)
     }
 
     private func accessibilityElement(
         label: String,
         in root: NSObject
     ) -> NSObject? {
         var visited: Set<ObjectIdentifier> = []
         return accessibilityElement(
             label: label,
             in: root,
             visited: &visited
         )
     }
 
     private func accessibilityElement(
         label: String,
         in windows: [UIWindow]
     ) -> NSObject? {
         for window in windows {
             if let match = accessibilityElement(label: label, in: window) {
                 return match
             }
         }
         return nil
     }
 
     private func accessibilityElement(
         label: String,
@@ -4661,1162 +4475,403 @@ final class LibraryViewHostedTests: XCTestCase {
             labels: &labels
         )
         return labels
     }
 
     private func collectAccessibilityLabels(
         in root: NSObject,
         visited: inout Set<ObjectIdentifier>,
         labels: inout [String]
     ) {
         guard visited.insert(ObjectIdentifier(root)).inserted else { return }
         if let label = root.accessibilityLabel, !label.isEmpty {
             labels.append(label)
         }
         let count = root.accessibilityElementCount()
         if count != NSNotFound, count > 0 {
             for index in 0..<count {
                 guard let child = root.accessibilityElement(at: index) as? NSObject else {
                     continue
                 }
                 collectAccessibilityLabels(
                     in: child,
                     visited: &visited,
                     labels: &labels
                 )
             }
         }
         if let view = root as? UIView {
             for child in view.subviews {
                 collectAccessibilityLabels(
                     in: child,
                     visited: &visited,
                     labels: &labels
                 )
             }
         }
     }
 }
 
 @MainActor
-final class RootTabShellHostedTests: XCTestCase {
-    func testLearnSearchExpandsFocusesAndClosesWhileSettingsRemainVisible()
-        async throws {
-        let fixture = try makeCompositionFixture()
-        defer { fixture.cleanup() }
-        let theme = ThemeManager()
-        let dependencies = fixture.composition.makeReviewSessionDependencies()
-        let vm = DashboardViewModel(
-            repository: fixture.composition.words,
-            reviewDependencies: DashboardReviewDependencies(
-                reviewSessionDependencies: dependencies
-            )
-        )
-        let host = UIHostingController(
-            rootView: DashboardView(
-                repository: fixture.composition.words,
-                reviewDependencies: dependencies,
-                viewModel: vm
+final class ProductOnboardingPaywallHostedTests: XCTestCase {
+    func testCompletionMarksSettingsAndFinishesExactlyOnceWhenTriggeredAgain() {
+        var completion = ProductOnboardingCompletion()
+        var settingsMarks = 0
+        var finishes = 0
+        for _ in 0..<2 {
+            completion.finish(
+                markSettingsComplete: { settingsMarks += 1 },
+                onFinished: { finishes += 1 }
             )
-            .environmentObject(theme)
-            .environmentObject(fixture.composition.appSettings)
-            .environmentObject(StudyNotificationRouter.shared)
-        )
-        let window = hostInWindow(host)
-        defer { window.isHidden = true }
-
-        XCTAssertNotNil(accessibilityElement(label: "Search Learn", in: host.view))
-        XCTAssertNotNil(accessibilityElement(label: "Settings", in: host.view))
-        XCTAssertNil(accessibilityElement(label: "Learn search field", in: host.view))
-
-        let search = try XCTUnwrap(
-            accessibilityElement(label: "Search Learn", in: host.view)
-        )
-        XCTAssertTrue(search.accessibilityActivate())
-        await waitUntil { firstTextField(in: host.view)?.isFirstResponder == true }
-        XCTAssertNotNil(accessibilityElement(label: "Learn search field", in: host.view))
-        XCTAssertNotNil(accessibilityElement(label: "Settings", in: host.view))
-
-        vm.searchText = "ticket"
-        let close = try XCTUnwrap(
-            accessibilityElement(label: "Close Learn search", in: host.view)
-        )
-        XCTAssertTrue(close.accessibilityActivate())
-        await waitUntil {
-            accessibilityElement(label: "Learn search field", in: host.view) == nil
         }
-        XCTAssertEqual(vm.searchText, "")
-        XCTAssertNotNil(accessibilityElement(label: "Search Learn", in: host.view))
-        XCTAssertNotNil(accessibilityElement(label: "Settings", in: host.view))
+        XCTAssertEqual(settingsMarks, 1)
+        XCTAssertEqual(finishes, 1)
     }
 
-    func testProShowsOnboardingPaywallAndCloseReturnsToLearn() throws {
-        let fixture = try makeCompositionFixture()
+    func testTestModeSkipsOnboardingPaywallAndCompletesExactlyOnce() throws {
+        let previous = ServerFeatureFlags.shared.isTestModeEnabled
+        defer { try? ServerFeatureFlags.shared.apply(responseData: Data("{\"test_mode\":\(previous)}".utf8)) }
+        try ServerFeatureFlags.shared.apply(responseData: Data(#"{"test_mode":true}"#.utf8))
+        let fixture = try HostedCompositionFixture(testCase: self)
         defer { fixture.cleanup() }
-        let onboardingCompletionBefore = OnboardingCompletionDefaultsSnapshot()
-        let theme = ThemeManager()
-        let host = UIHostingController(
-            rootView: RootTabShell(composition: fixture.composition)
-            .environmentObject(theme)
+        var markCount = 0
+        var finishCount = 0
+        let host = UIHostingController(rootView: ProductOnboardingView(
+            initialPage: 3,
+            paywallActions: successfulPaywallActions(),
+            markSettingsComplete: { markCount += 1 },
+            onFinished: { finishCount += 1 }
+        ).environmentObject(ThemeManager())
             .environmentObject(fixture.composition.appSettings)
-            .environmentObject(fixture.composition.storeKit)
-            .environmentObject(StudyNotificationRouter.shared)
-        )
+            .environmentObject(fixture.composition.storeKit))
         let window = hostInWindow(host)
         defer { window.isHidden = true }
-
-        XCTAssertNotNil(accessibilityElement(label: "Search Learn", in: host.view))
-        XCTAssertNil(accessibilityElement(label: "Learn search field", in: host.view))
-        let pro = try XCTUnwrap(
-            accessibilityElement(label: "Pro", in: host.view)
-        )
-        XCTAssertTrue(pro.accessibilityActivate())
         settle(host)
+        XCTAssertEqual(markCount, 1)
+        XCTAssertEqual(finishCount, 1)
+        XCTAssertNil(accessibilityElement(label: "Start 7-day free trial", in: host.view))
+    }
 
-        for label in [
-            "UNLOCK EVERYTHING",
-            "Learn without limits.",
-            "Select yearly plan",
-            "Select monthly plan",
-            "Start 7-day free trial",
-            "AI translations",
-        ] {
-            XCTAssertNotNil(
-                accessibilityElement(label: label, in: host.view),
-                "Missing \(label). Labels: \(accessibilityLabels(in: host.view))"
-            )
-        }
-        XCTAssertNotNil(
-            accessibilityElement(label: "Library", in: host.view),
-            "The floating tab bar must remain visible."
-        )
-        XCTAssertTrue(host.prefersStatusBarHidden)
-
-        let close = try XCTUnwrap(
-            accessibilityElement(label: "Close purchase page", in: host.view)
+    private func hostInWindow<Content: View>(
+        _ host: UIHostingController<Content>
+    ) -> UIWindow {
+        let window = UIWindow(
+            frame: CGRect(x: 0, y: 0, width: 390, height: 844)
         )
-        XCTAssertTrue(close.accessibilityActivate())
+        window.rootViewController = host
+        window.makeKeyAndVisible()
+        host.view.frame = window.bounds
         settle(host)
-        XCTAssertEqual(
-            OnboardingCompletionDefaultsSnapshot(),
-            onboardingCompletionBefore
-        )
+        return window
+    }
 
-        XCTAssertNotNil(accessibilityElement(label: "Search Learn", in: host.view))
-        XCTAssertNil(accessibilityElement(label: "Learn search field", in: host.view))
-        XCTAssertFalse(host.prefersStatusBarHidden)
+    private func settle<Content: View>(
+        _ host: UIHostingController<Content>
+    ) {
+        host.view.setNeedsLayout()
+        host.view.layoutIfNeeded()
+        RunLoop.main.run(until: Date().addingTimeInterval(0.4))
+        host.view.layoutIfNeeded()
     }
 
-    func testProPurchaseSuccessReturnsToLearnWithoutCompletingOnboarding()
-        throws {
-        try assertSuccessfulProActionReturnsToLearn(
-            actionLabel: "Start 7-day free trial"
+    private func accessibilityElement(
+        label: String,
+        in root: NSObject
+    ) -> NSObject? {
+        var visited: Set<ObjectIdentifier> = []
+        return accessibilityElement(
+            label: label,
+            in: root,
+            visited: &visited
         )
     }
 
-    func testProRestoreSuccessReturnsToLearnWithoutCompletingOnboarding()
-        throws {
-        try assertSuccessfulProActionReturnsToLearn(actionLabel: "Restore")
+    private func accessibilityElement(
+        label: String,
+        in root: NSObject,
+        visited: inout Set<ObjectIdentifier>
+    ) -> NSObject? {
+        guard visited.insert(ObjectIdentifier(root)).inserted else {
+            return nil
+        }
+        if root.accessibilityLabel == label { return root }
+        let count = root.accessibilityElementCount()
+        if count != NSNotFound, count > 0 {
+            for index in 0..<count {
+                guard let child =
+                        root.accessibilityElement(at: index) as? NSObject
+                else {
+                    continue
+                }
+                if let match = accessibilityElement(
+                    label: label,
+                    in: child,
+                    visited: &visited
+                ) {
+                    return match
+                }
+            }
+        }
+        if let view = root as? UIView {
+            for child in view.subviews {
+                if let match = accessibilityElement(
+                    label: label,
+                    in: child,
+                    visited: &visited
+                ) {
+                    return match
+                }
+            }
+        }
+        return nil
     }
+}
 
-    func testFloatingTabBarRendersLearnLibraryProCreateAndCreateKeepsSelection() throws {
-        let suite = "RootTabShellHostedTests.tabs.\(UUID().uuidString)"
+private struct EmptyPaywallCredentials: AccountCredentialStorage {
+    func load() throws -> AccountSessionDTO? { nil }
+    func save(_ session: AccountSessionDTO) throws {}
+    func clear() throws {}
+}
+
+@MainActor
+final class ProductPaywallPageHostedTests: XCTestCase {
+    func testPaywallStatusBarPreferenceEndsWithPage() throws {
+        let suite = "ProductPaywallPageHostedTests.StatusBar.\(UUID().uuidString)"
         let defaults = try XCTUnwrap(UserDefaults(suiteName: suite))
         defer { defaults.removePersistentDomain(forName: suite) }
         let settings = AppSettingsStore(defaults: defaults)
-        let state = MainTabHostedState(selection: .library)
-        let host = UIHostingController(
-            rootView: FloatingTabBar(
-                selection: Binding(
-                    get: { state.selection },
-                    set: { state.selection = $0 }
-                ),
-                onAdd: { state.createPresentationCount += 1 }
-            )
-            .environmentObject(settings)
-        )
-        let window = hostInWindow(host)
+        let previousTestMode = ServerFeatureFlags.shared.isTestModeEnabled
+        defer {
+            try? ServerFeatureFlags.shared.apply(responseData: Data("{\"test_mode\":\(previousTestMode)}".utf8))
+        }
+        try ServerFeatureFlags.shared.apply(responseData: Data(#"{"test_mode":false}"#.utf8))
+        let account = AccountProfileModel(
+            client: AccountSessionClient(
+                baseURL: URL(string: "https://paywall-fixture.invalid")!,
+                storage: EmptyPaywallCredentials(),
+                deviceToken: { nil },
+                integrityHeaders: { _, _, _ in [:] }
+            ),
+            entitlements: EntitlementStore(defaults: defaults)
+        )
+        let page = ProductPaywallPage(
+            bottomContentInset: 96,
+            loadsProductsOnAppear: false,
+            actions: successfulPaywallActions(),
+            account: account,
+            onClose: {},
+            onPurchaseCompleted: {}
+        )
+        let host = UIHostingController(rootView: AnyView(page.environmentObject(settings)))
+        let window = UIWindow(frame: CGRect(x: 0, y: 0, width: 390, height: 844))
+        window.rootViewController = host
+        window.makeKeyAndVisible()
         defer { window.isHidden = true }
+        host.view.frame = window.bounds
+        host.view.layoutIfNeeded()
+        XCTAssertTrue(host.prefersStatusBarHidden)
 
-        XCTAssertNil(accessibilityElement(label: "Home", in: host.view))
-        let ordered = try ["Learn", "Library", "Pro", "Create"].map {
-            try XCTUnwrap(accessibilityElement(label: $0, in: host.view))
-        }
-        XCTAssertEqual(
-            ordered.map(\.accessibilityFrame.minX),
-            ordered.map(\.accessibilityFrame.minX).sorted()
-        )
-
-        XCTAssertTrue(ordered[3].accessibilityActivate())
-        settle(host)
-        XCTAssertEqual(state.createPresentationCount, 1)
-        XCTAssertEqual(state.selection, .library)
-    }
-
-    func testZeroCardDashboardShowsGuidanceAndDoesNotConstructAddSpeedDial() throws {
-        let fixture = try makeCompositionFixture()
-        defer { fixture.cleanup() }
-        let theme = ThemeManager()
-        let host = UIHostingController(
-            rootView: DashboardView(
-                repository: fixture.composition.words,
-                reviewDependencies:
-                    fixture.composition.makeReviewSessionDependencies()
-            )
-            .environmentObject(theme)
-            .environmentObject(fixture.composition.appSettings)
-            .environmentObject(StudyNotificationRouter.shared)
-        )
-        let window = hostInWindow(host)
-        defer { window.isHidden = true }
-
-        XCTAssertNotNil(
-            accessibilityElement(
-                label: "No flashcards yet. Tap Create to build a set with AI, text, a file, or a photo — or choose a topic from Library.",
-                in: host.view
-            ),
-            "Labels: \(accessibilityLabels(in: host.view))"
-        )
-        XCTAssertNil(
-            accessibilityElement(label: "Add words", in: host.view),
-            "At zero Learn cards, the speed dial must not exist in the accessibility tree."
-        )
-    }
-
-    func testSettingsAccountOpensAccountMenu() throws {
-        let fixture = try makeCompositionFixture()
-        defer { fixture.cleanup() }
-        let host = UIHostingController(
-            rootView: SettingsView(repository: fixture.composition.words)
-                .environmentObject(ThemeManager())
-                .environmentObject(fixture.composition.appSettings)
-                .environmentObject(fixture.composition.accountSession)
-        )
-        let window = hostInWindow(host)
-        defer { window.isHidden = true }
-        let account = try XCTUnwrap(accessibilityElement(label: "Account", in: host.view))
-        XCTAssertTrue(account.accessibilityActivate())
-        settle(host)
-        XCTAssertNotNil(accessibilityElement(label: "Create account", in: host.view))
-        XCTAssertNotNil(accessibilityElement(label: "Log in", in: host.view))
-    }
-
-    func testSettingsRemindersOpensItsMenu() throws {
-        let fixture = try makeCompositionFixture()
-        defer { fixture.cleanup() }
-        let theme = ThemeManager()
-        let host = UIHostingController(
-            rootView: SettingsView(repository: fixture.composition.words)
-                .environmentObject(theme)
-                .environmentObject(fixture.composition.appSettings)
-        )
-        let window = hostInWindow(host)
-        defer { window.isHidden = true }
-
-        let account = try XCTUnwrap(accessibilityElement(label: "Account", in: host.view))
-        XCTAssertNil(accessibilityElement(label: "When can we remind you?", in: host.view))
-        XCTAssertTrue(account.accessibilityTraits.contains(.button))
-
-        let reminders = try XCTUnwrap(accessibilityElement(label: "Reminders", in: host.view))
-        XCTAssertTrue(reminders.accessibilityActivate())
-        settle(host)
-
-        XCTAssertNotNil(
-            accessibilityElement(
-                label: "When can we remind you?",
-                in: host.view
-            ),
-            "The rendered Settings reminders section must expose the approved title. Labels: \(accessibilityLabels(in: host.view))"
-        )
-    }
-
-    func testSettingsDailyGoalMenuSavesSelection() throws {
-        let fixture = try makeCompositionFixture()
-        defer { fixture.cleanup() }
-        let settings = fixture.composition.appSettings
-        settings.newWordsPerDay = 5
-        let host = UIHostingController(
-            rootView: SettingsView(repository: fixture.composition.words)
-                .environmentObject(ThemeManager())
-                .environmentObject(settings)
-        )
-        let window = hostInWindow(host)
-        defer { window.isHidden = true }
-
-        let goal = try XCTUnwrap(accessibilityElement(label: "Daily goal", in: host.view))
-        XCTAssertEqual(goal.accessibilityValue, "5 new words per day")
-        XCTAssertTrue(goal.accessibilityActivate())
-        settle(host)
-        let pause = try XCTUnwrap(accessibilityElement(label: "0", in: host.view))
-        XCTAssertTrue(pause.accessibilityActivate())
-        settle(host)
-        XCTAssertEqual(settings.newWordsPerDay, 0)
-    }
-
-    func testCreateSetOpensWithoutKeyboardAndAcceptsInput() throws {
-        let fixture = try makeCompositionFixture()
-        defer { fixture.cleanup() }
-        let theme = ThemeManager()
-        let host = UIHostingController(
-            rootView: NavigationStack {
-                RootTabShell(composition: fixture.composition)
-            }
-            .environmentObject(theme)
-            .environmentObject(fixture.composition.appSettings)
-            .environmentObject(fixture.composition.storeKit)
-            .environmentObject(StudyNotificationRouter.shared)
-        )
-        let window = hostInWindow(host)
-        defer { window.isHidden = true }
-        let create = try XCTUnwrap(accessibilityElement(label: "Create", in: host.view))
-        XCTAssertTrue(create.accessibilityActivate())
-        settle(host)
-        let field = try XCTUnwrap(firstTextField(in: host.view))
-        XCTAssertFalse(
-            field.isFirstResponder,
-            "Opening Create must not start keyboard setup during the navigation transition."
-        )
-        field.becomeFirstResponder()
-        field.insertText("Travel")
-        host.view.layoutIfNeeded()
-        XCTAssertEqual(field.text, "Travel")
-    }
-
-    func testCreateSetKeepsStartLearningToggleVisibleWithoutWords() throws {
-        let fixture = try makeCompositionFixture()
-        defer { fixture.cleanup() }
-        let setID = try fixture.composition.words.addFlashcardSet(
-            displayName: "Travel"
-        )
-        let theme = ThemeManager()
-        let host = UIHostingController(
-            rootView: NavigationStack {
-                CreateFlashcardSetPage(
-                    repository: fixture.composition.words,
-                    publicSets: fixture.composition.publicSets,
-                    editingFlashcardSetID: setID
-                )
-            }
-            .environmentObject(theme)
-            .environmentObject(fixture.composition.appSettings)
-        )
-        let window = hostInWindow(host)
-        defer { window.isHidden = true }
-
-        XCTAssertNotNil(
-            accessibilityElement(
-                label: "Start learning this set",
-                requiredTraits: .button,
-                in: host.view
-            )
-        )
-        XCTAssertNil(
-            accessibilityElement(
-                label: "Words in this set",
-                in: host.view
-            )
-        )
-    }
-
-    func testCreateSetPlacesStartLearningToggleAbovePopulatedWords() throws {
-        let fixture = try makeCompositionFixture()
-        defer { fixture.cleanup() }
-        let setID = try fixture.composition.words.addFlashcardSet(
-            displayName: "Travel"
-        )
-        try fixture.composition.words.storeTextualAnkiWord(
-            TextualAnkiWord(
-                id: "create-set-order-ticket",
-                normalizedWord: "ticket",
-                displayWord: "Ticket",
-                nativeLanguage:
-                    fixture.composition.appSettings.nativeLanguageCode,
-                learningLanguage:
-                    fixture.composition.appSettings.learningLanguageCode,
-                wordDetailJSON:
-                    #"{"translations":["Billete"],"translation":"Billete"}"#,
-                translation: "Billete",
-                pronunciation: nil,
-                partOfSpeech: "noun",
-                userNotes: nil,
-                externalCollectionID: "create-set-order-fixture",
-                externalNoteID: "ticket-1",
-                importBatchID: "fixture",
-                sourceMetadataJSON: #"{"model":"Basic"}"#,
-                tagsJSON: nil,
-                createdAt: Date(timeIntervalSince1970: 1_754_200_000),
-                updatedAt: Date(timeIntervalSince1970: 1_754_200_000),
-                templates: []
-            ),
-            in: setID
-        )
-        let theme = ThemeManager()
-        let host = UIHostingController(
-            rootView: NavigationStack {
-                CreateFlashcardSetPage(
-                    repository: fixture.composition.words,
-                    publicSets: fixture.composition.publicSets,
-                    editingFlashcardSetID: setID
-                )
-            }
-            .environmentObject(theme)
-            .environmentObject(fixture.composition.appSettings)
-        )
-        let window = hostInWindow(host)
-        defer { window.isHidden = true }
-
-        let toggle = try XCTUnwrap(
-            accessibilityElement(
-                label: "Start learning this set",
-                requiredTraits: .button,
-                in: host.view
-            )
-        )
-        let wordsHeading = try XCTUnwrap(
-            accessibilityElement(
-                label: "Words in this set",
-                in: host.view
-            )
-        )
-
-        XCTAssertGreaterThan(toggle.accessibilityFrame.height, 0)
-        XCTAssertGreaterThan(wordsHeading.accessibilityFrame.height, 0)
-        XCTAssertLessThanOrEqual(
-            toggle.accessibilityFrame.maxY,
-            wordsHeading.accessibilityFrame.minY,
-            "The learning toggle must be immediately above the populated words section."
-        )
-    }
-
-    private func assertSuccessfulProActionReturnsToLearn(
-        actionLabel: String
-    ) throws {
-        let fixture = try makeCompositionFixture()
-        defer { fixture.cleanup() }
-        let onboardingCompletionBefore = OnboardingCompletionDefaultsSnapshot()
-        let theme = ThemeManager()
-        let host = UIHostingController(
-            rootView: RootTabShell(
-                composition: fixture.composition,
-                paywallActions: successfulPaywallActions()
-            )
-            .environmentObject(theme)
-            .environmentObject(fixture.composition.appSettings)
-            .environmentObject(fixture.composition.storeKit)
-            .environmentObject(StudyNotificationRouter.shared)
-        )
-        let window = hostInWindow(host)
-        defer { window.isHidden = true }
-
-        let pro = try XCTUnwrap(
-            accessibilityElement(label: "Pro", in: host.view)
-        )
-        XCTAssertTrue(pro.accessibilityActivate())
-        settle(host)
-
-        let action = try XCTUnwrap(
-            accessibilityElement(label: actionLabel, in: host.view)
-        )
-        XCTAssertTrue(action.accessibilityActivate())
-        settle(host)
-
-        XCTAssertEqual(
-            OnboardingCompletionDefaultsSnapshot(),
-            onboardingCompletionBefore
-        )
-        XCTAssertNotNil(accessibilityElement(label: "Search Learn", in: host.view))
-        XCTAssertNil(accessibilityElement(label: "Learn search field", in: host.view))
-    }
-
-    private func accessibilityElement(
-        label: String,
-        requiredTraits: UIAccessibilityTraits,
-        in root: NSObject
-    ) -> NSObject? {
-        var visited: Set<ObjectIdentifier> = []
-        return accessibilityElement(
-            label: label,
-            requiredTraits: requiredTraits,
-            in: root,
-            visited: &visited
-        )
-    }
-
-    private func accessibilityElement(
-        label: String,
-        requiredTraits: UIAccessibilityTraits,
-        in root: NSObject,
-        visited: inout Set<ObjectIdentifier>
-    ) -> NSObject? {
-        guard visited.insert(ObjectIdentifier(root)).inserted else {
-            return nil
-        }
-        if root.accessibilityLabel == label,
-           root.accessibilityTraits.contains(requiredTraits) {
-            return root
-        }
-        let count = root.accessibilityElementCount()
-        if count != NSNotFound, count > 0 {
-            for index in 0..<count {
-                guard let child = root.accessibilityElement(at: index) as? NSObject else {
-                    continue
-                }
-                if let match = accessibilityElement(
-                    label: label,
-                    requiredTraits: requiredTraits,
-                    in: child,
-                    visited: &visited
-                ) {
-                    return match
-                }
-            }
-        }
-        if let view = root as? UIView {
-            for child in view.subviews {
-                if let match = accessibilityElement(
-                    label: label,
-                    requiredTraits: requiredTraits,
-                    in: child,
-                    visited: &visited
-                ) {
-                    return match
-                }
-            }
-        }
-        return nil
-    }
-
-    private func makeCompositionFixture() throws -> HostedCompositionFixture {
-        try HostedCompositionFixture(testCase: self)
-    }
-
-    private func hostInWindow<Content: View>(
-        _ host: UIHostingController<Content>
-    ) -> UIWindow {
-        let window = UIWindow(
-            frame: CGRect(x: 0, y: 0, width: 390, height: 844)
-        )
-        window.rootViewController = host
-        window.makeKeyAndVisible()
-        host.view.frame = window.bounds
-        settle(host)
-        return window
-    }
-
-    private func settle<Content: View>(
-        _ host: UIHostingController<Content>
-    ) {
-        host.view.setNeedsLayout()
-        host.view.layoutIfNeeded()
-        RunLoop.main.run(until: Date().addingTimeInterval(0.4))
-        host.view.layoutIfNeeded()
-    }
-
-    private func firstTextField(in root: UIView) -> UITextField? {
-        if let field = root as? UITextField { return field }
-        for subview in root.subviews {
-            if let field = firstTextField(in: subview) { return field }
-        }
-        return nil
-    }
-
-    private func waitUntil(
-        _ condition: @MainActor () -> Bool,
-        file: StaticString = #filePath,
-        line: UInt = #line
-    ) async {
-        let clock = ContinuousClock()
-        let deadline = clock.now.advanced(by: .seconds(1))
-        while clock.now < deadline {
-            if condition() { return }
-            await Task.yield()
-        }
-        XCTFail(
-            "Timed out waiting for hosted Learn condition",
-            file: file,
-            line: line
-        )
-    }
-
-    private func accessibilityElement(
-        label: String,
-        in root: NSObject
-    ) -> NSObject? {
-        var visited: Set<ObjectIdentifier> = []
-        return accessibilityElement(
-            label: label,
-            in: root,
-            visited: &visited
-        )
-    }
-
-    private func accessibilityElement(
-        label: String,
-        in root: NSObject,
-        visited: inout Set<ObjectIdentifier>
-    ) -> NSObject? {
-        guard visited.insert(ObjectIdentifier(root)).inserted else {
-            return nil
-        }
-        if root.accessibilityLabel == label { return root }
-        let count = root.accessibilityElementCount()
-        if count != NSNotFound, count > 0 {
-            for index in 0..<count {
-                guard let child = root.accessibilityElement(at: index) as? NSObject else {
-                    continue
-                }
-                if let match = accessibilityElement(
-                    label: label,
-                    in: child,
-                    visited: &visited
-                ) {
-                    return match
-                }
-            }
-        }
-        if let view = root as? UIView {
-            for child in view.subviews {
-                if let match = accessibilityElement(
-                    label: label,
-                    in: child,
-                    visited: &visited
-                ) {
-                    return match
-                }
-            }
-        }
-        return nil
-    }
-
-    private func accessibilityLabels(in root: NSObject) -> [String] {
-        var visited: Set<ObjectIdentifier> = []
-        var labels: [String] = []
-        collectAccessibilityLabels(
-            in: root,
-            visited: &visited,
-            labels: &labels
-        )
-        return labels
-    }
-
-    private func collectAccessibilityLabels(
-        in root: NSObject,
-        visited: inout Set<ObjectIdentifier>,
-        labels: inout [String]
-    ) {
-        guard visited.insert(ObjectIdentifier(root)).inserted else { return }
-        if let label = root.accessibilityLabel, !label.isEmpty {
-            labels.append(label)
-        }
-        let count = root.accessibilityElementCount()
-        if count != NSNotFound, count > 0 {
-            for index in 0..<count {
-                guard let child = root.accessibilityElement(at: index) as? NSObject else {
-                    continue
-                }
-                collectAccessibilityLabels(
-                    in: child,
-                    visited: &visited,
-                    labels: &labels
-                )
-            }
-        }
-        if let view = root as? UIView {
-            for child in view.subviews {
-                collectAccessibilityLabels(
-                    in: child,
-                    visited: &visited,
-                    labels: &labels
-                )
-            }
-        }
-    }
-}
-
-@MainActor
-final class ProductOnboardingPaywallHostedTests: XCTestCase {
-    func testTestModeSkipsOnboardingPaywallAndCompletesExactlyOnce() throws {
-        let previous = ServerFeatureFlags.shared.isTestModeEnabled
-        defer { try? ServerFeatureFlags.shared.apply(responseData: Data("{\"test_mode\":\(previous)}".utf8)) }
-        try ServerFeatureFlags.shared.apply(responseData: Data(#"{"test_mode":true}"#.utf8))
-        let fixture = try HostedCompositionFixture(testCase: self)
-        defer { fixture.cleanup() }
-        var markCount = 0
-        var finishCount = 0
-        let host = UIHostingController(rootView: ProductOnboardingView(
-            initialPage: 3,
-            paywallActions: successfulPaywallActions(),
-            markSettingsComplete: { markCount += 1 },
-            onFinished: { finishCount += 1 }
-        ).environmentObject(ThemeManager())
-            .environmentObject(fixture.composition.appSettings)
-            .environmentObject(fixture.composition.storeKit))
-        let window = hostInWindow(host)
-        defer { window.isHidden = true }
-        settle(host)
-        XCTAssertEqual(markCount, 1)
-        XCTAssertEqual(finishCount, 1)
-        XCTAssertNil(accessibilityElement(label: "Start 7-day free trial", in: host.view))
-    }
-
-    func testOnboardingPaywallCloseMarksSettingsCompleteAndFinishesOnce()
-        throws {
-        try assertOnboardingCompletion(actionLabel: "Close purchase page")
-    }
-
-    func testOnboardingPaywallPurchaseSuccessMarksSettingsCompleteAndFinishesOnce()
-        throws {
-        try assertOnboardingCompletion(actionLabel: "Start 7-day free trial")
-    }
-
-    func testOnboardingPaywallRestoreSuccessMarksSettingsCompleteAndFinishesOnce()
-        throws {
-        try assertOnboardingCompletion(actionLabel: "Restore")
-    }
-
-    private func assertOnboardingCompletion(actionLabel: String) throws {
-        let fixture = try HostedCompositionFixture(testCase: self)
-        defer { fixture.cleanup() }
-        var markCount = 0
-        var finishCount = 0
-        let theme = ThemeManager()
-        let host = UIHostingController(
-            rootView: ProductOnboardingView(
-                initialPage: 3,
-                paywallActions: successfulPaywallActions(),
-                markSettingsComplete: { markCount += 1 },
-                onFinished: { finishCount += 1 }
-            )
-            .environmentObject(theme)
-            .environmentObject(fixture.composition.appSettings)
-            .environmentObject(fixture.composition.storeKit)
-        )
-        let window = hostInWindow(host)
-        defer { window.isHidden = true }
-
-        let action = try XCTUnwrap(
-            accessibilityElement(label: actionLabel, in: host.view)
-        )
-        XCTAssertTrue(action.accessibilityActivate())
-        settle(host)
-
-        XCTAssertEqual(markCount, 1)
-        XCTAssertEqual(finishCount, 1)
-    }
-
-    private func hostInWindow<Content: View>(
-        _ host: UIHostingController<Content>
-    ) -> UIWindow {
-        let window = UIWindow(
-            frame: CGRect(x: 0, y: 0, width: 390, height: 844)
-        )
-        window.rootViewController = host
-        window.makeKeyAndVisible()
-        host.view.frame = window.bounds
-        settle(host)
-        return window
-    }
-
-    private func settle<Content: View>(
-        _ host: UIHostingController<Content>
-    ) {
+        host.rootView = AnyView(Color.clear)
         host.view.setNeedsLayout()
-        host.view.layoutIfNeeded()
-        RunLoop.main.run(until: Date().addingTimeInterval(0.4))
-        host.view.layoutIfNeeded()
-    }
-
-    private func accessibilityElement(
-        label: String,
-        in root: NSObject
-    ) -> NSObject? {
-        var visited: Set<ObjectIdentifier> = []
-        return accessibilityElement(
-            label: label,
-            in: root,
-            visited: &visited
+        host.setNeedsStatusBarAppearanceUpdate()
+        let restored = XCTNSPredicateExpectation(
+            predicate: NSPredicate(format: "prefersStatusBarHidden == NO"),
+            object: host
         )
+        XCTAssertEqual(XCTWaiter.wait(for: [restored], timeout: 2), .completed)
+        XCTAssertFalse(host.prefersStatusBarHidden)
     }
 
-    private func accessibilityElement(
-        label: String,
-        in root: NSObject,
-        visited: inout Set<ObjectIdentifier>
-    ) -> NSObject? {
-        guard visited.insert(ObjectIdentifier(root)).inserted else {
-            return nil
-        }
-        if root.accessibilityLabel == label { return root }
-        let count = root.accessibilityElementCount()
-        if count != NSNotFound, count > 0 {
-            for index in 0..<count {
-                guard let child =
-                        root.accessibilityElement(at: index) as? NSObject
-                else {
-                    continue
-                }
-                if let match = accessibilityElement(
-                    label: label,
-                    in: child,
-                    visited: &visited
-                ) {
-                    return match
-                }
-            }
-        }
-        if let view = root as? UIView {
-            for child in view.subviews {
-                if let match = accessibilityElement(
-                    label: label,
-                    in: child,
-                    visited: &visited
-                ) {
-                    return match
-                }
-            }
-        }
-        return nil
-    }
-}
-
-@MainActor
-final class ProductPaywallPageHostedTests: XCTestCase {
     func testTestModeProPageSkipsStoreProducts() throws {
         let previous = ServerFeatureFlags.shared.isTestModeEnabled
         defer { try? ServerFeatureFlags.shared.apply(responseData: Data("{\"test_mode\":\(previous)}".utf8)) }
         try ServerFeatureFlags.shared.apply(responseData: Data(#"{"test_mode":true}"#.utf8))
         XCTAssertEqual(try productLoadCount(loadsProductsOnAppear: true, hasProducts: false, expectsTestMode: true), 0)
     }
 
     func testStandalonePaywallLoadsProductsWhenMissing() throws {
         XCTAssertEqual(
             try productLoadCount(
                 loadsProductsOnAppear: true,
                 hasProducts: false
             ),
             1
         )
     }
 
     func testLoadedPaywallDoesNotRequestProductsAgain() throws {
         XCTAssertEqual(
             try productLoadCount(
                 loadsProductsOnAppear: true,
                 hasProducts: true
             ),
             0
         )
     }
 
     func testEmbeddedOnboardingPaywallDoesNotStartSecondProductLoad()
         throws {
         XCTAssertEqual(
             try productLoadCount(
                 loadsProductsOnAppear: false,
                 hasProducts: false
             ),
             0
         )
     }
 
-    func testBenefitsShowApprovedFreeValuesAndOmitDailyReminders()
-        throws {
-        let suite = "ProductPaywallPageHostedTests.Benefits.\(UUID().uuidString)"
-        let defaults = try XCTUnwrap(UserDefaults(suiteName: suite))
-        defer { defaults.removePersistentDomain(forName: suite) }
-        let settings = AppSettingsStore(defaults: defaults)
-        let host = UIHostingController(
-            rootView: ProductPaywallPage(
-                bottomContentInset: 28,
-                loadsProductsOnAppear: false,
-                actions: successfulPaywallActions(),
-                onClose: {},
-                onPurchaseCompleted: {}
-            )
-            .environmentObject(settings)
-        )
-        let window = UIWindow(
-            frame: CGRect(x: 0, y: 0, width: 390, height: 844)
-        )
-        window.rootViewController = host
-        window.makeKeyAndVisible()
-        defer { window.isHidden = true }
-        host.view.frame = window.bounds
-        host.view.setNeedsLayout()
-        host.view.layoutIfNeeded()
-        RunLoop.main.run(until: Date().addingTimeInterval(0.4))
-        host.view.layoutIfNeeded()
-
-        let labels = accessibilityLabels(in: host.view)
-        XCTAssertTrue(labels.contains("AI translations"))
-        XCTAssertTrue(labels.contains("Add words manually"))
-        XCTAssertTrue(labels.contains("100"))
-        XCTAssertTrue(labels.contains("Extract words from photos"))
-        XCTAssertTrue(labels.contains("Phrasal verbs"))
-        XCTAssertFalse(labels.contains("Daily reminders"))
-        XCTAssertFalse(labels.contains("5"))
-        XCTAssertEqual(labels.filter { $0 == "∞" }.count, 6)
-    }
-
     private func productLoadCount(
         loadsProductsOnAppear: Bool,
         hasProducts: Bool,
         expectsTestMode: Bool = false
     ) throws -> Int {
         let suite = "ProductPaywallPageHostedTests.\(UUID().uuidString)"
         let defaults = try XCTUnwrap(UserDefaults(suiteName: suite))
         defer { defaults.removePersistentDomain(forName: suite) }
         let settings = AppSettingsStore(defaults: defaults)
         var loadCount = 0
         let actions = ProductPaywallActions(
             isBusy: false,
             hasProducts: hasProducts,
             loadProducts: { loadCount += 1 },
             purchase: { _ in .success },
             restore: { .success }
         )
         let host = UIHostingController(
             rootView: ProductPaywallPage(
                 bottomContentInset: 28,
                 loadsProductsOnAppear: loadsProductsOnAppear,
                 actions: actions,
                 onClose: {},
                 onPurchaseCompleted: {}
             )
             .environmentObject(settings)
         )
         let window = UIWindow(
             frame: CGRect(x: 0, y: 0, width: 390, height: 844)
         )
         window.rootViewController = host
         window.makeKeyAndVisible()
         host.view.frame = window.bounds
         host.view.setNeedsLayout()
         host.view.layoutIfNeeded()
         RunLoop.main.run(until: Date().addingTimeInterval(0.4))
         host.view.layoutIfNeeded()
         if expectsTestMode {
             let screenshot = UIGraphicsImageRenderer(bounds: host.view.bounds).image { _ in
                 host.view.drawHierarchy(in: host.view.bounds, afterScreenUpdates: true)
             }
             let attachment = XCTAttachment(image: screenshot)
             attachment.name = "Test mode Pro page"
             attachment.lifetime = .keepAlways
             add(attachment)
             let screenshotURL = FileManager.default.temporaryDirectory.appendingPathComponent("owl-test-mode-pro.png")
             try screenshot.pngData()?.write(to: screenshotURL)
             print("TEST_MODE_SCREENSHOT=\(screenshotURL.path)")
         }
         window.isHidden = true
         return loadCount
     }
 
-    private func accessibilityLabels(in root: NSObject) -> [String] {
-        var visited: Set<ObjectIdentifier> = []
-        return accessibilityLabels(in: root, visited: &visited)
-    }
-
-    private func accessibilityLabels(
-        in root: NSObject,
-        visited: inout Set<ObjectIdentifier>
-    ) -> [String] {
-        guard visited.insert(ObjectIdentifier(root)).inserted else { return [] }
-        var labels = root.accessibilityLabel.map { [$0] } ?? []
-        let count = root.accessibilityElementCount()
-        if count != NSNotFound, count > 0 {
-            for index in 0..<count {
-                guard let child = root.accessibilityElement(at: index) as? NSObject
-                else { continue }
-                labels += accessibilityLabels(in: child, visited: &visited)
-            }
-        }
-        if let view = root as? UIView {
-            for child in view.subviews {
-                labels += accessibilityLabels(in: child, visited: &visited)
-            }
-        }
-        return labels
-    }
 }
 
 final class ProductPaywallLayoutTests: XCTestCase {
     func testPaywallBottomClearanceIncludesAdditionalTabBarInset() {
         XCTAssertEqual(
             ProductPaywallLayout.contentBottomPadding(
                 bottomContentInset: 28
             ),
             178
         )
         XCTAssertEqual(
             ProductPaywallLayout.contentBottomPadding(
                 bottomContentInset: 96
             ),
             246
         )
     }
 }
 
-@MainActor
-private final class MainTabHostedState {
-    var selection: MainTab
-    var createPresentationCount = 0
-
-    init(selection: MainTab) {
-        self.selection = selection
-    }
-}
-
 @MainActor
 private final class HostedCompositionFixture {
     let composition: AppComposition
     private let directory: URL
     private let defaults: UserDefaults
     private let suite: String
 
     init(testCase: XCTestCase) throws {
         directory = FileManager.default.temporaryDirectory
             .appendingPathComponent(
                 "RootTabShellHostedTests-\(UUID().uuidString)"
             )
         try FileManager.default.createDirectory(
             at: directory,
             withIntermediateDirectories: true
         )
         suite = "RootTabShellHostedTests.\(UUID().uuidString)"
         defaults = try XCTUnwrap(UserDefaults(suiteName: suite))
         composition = try AppComposition.make(
             databaseURL: directory.appendingPathComponent("hosted.sqlite3"),
             defaults: defaults,
             calendar: Calendar(identifier: .gregorian),
             now: { Date(timeIntervalSince1970: 1_754_200_000) }
         )
         let cleanupDirectory = directory
         let openedDatabase = composition.database
         testCase.addTeardownBlock { [weak openedDatabase] in
             for _ in 0..<20 {
                 if openedDatabase == nil { break }
                 try await Task.sleep(nanoseconds: 25_000_000)
             }
             guard openedDatabase == nil else {
                 NSLog("Preserving hosted test SQLite directory while its owner is still open: %@", cleanupDirectory.path)
                 return
             }
             try FileManager.default.removeItem(at: cleanupDirectory)
         }
     }
 
     func cleanup() {
         defaults.removePersistentDomain(forName: suite)
     }
 }
 
-@MainActor
-final class SetGridTileBadgeTests: XCTestCase {
-    func testOptionalBadgeIsRenderedAsAccessibleText() throws {
-        let suite = "SetGridTileBadgeTests.\(UUID().uuidString)"
-        let defaults = try XCTUnwrap(UserDefaults(suiteName: suite))
-        defer { defaults.removePersistentDomain(forName: suite) }
-        let settings = AppSettingsStore(defaults: defaults)
-        let host = UIHostingController(
-            rootView: SetGridTile(
-                title: "Travel",
-                subtitle: "2 words",
-                systemImage: "airplane",
-                tint: [.blue, .purple],
-                badgeText: "Pending approval"
-            )
-            .environmentObject(settings)
-        )
-        let window = UIWindow(frame: CGRect(x: 0, y: 0, width: 220, height: 300))
-        window.rootViewController = host
-        window.makeKeyAndVisible()
-        host.view.frame = window.bounds
-        host.view.layoutIfNeeded()
-        defer { window.isHidden = true }
-
-        XCTAssertNotNil(
-            accessibilityElement(
-                label: "Pending approval",
-                in: host.view
-            )
-        )
-    }
-
-    private func accessibilityElement(
-        label: String,
-        in root: NSObject
-    ) -> NSObject? {
-        var visited: Set<ObjectIdentifier> = []
-        return accessibilityElement(
-            label: label,
-            in: root,
-            visited: &visited
-        )
-    }
-
-    private func accessibilityElement(
-        label: String,
-        in root: NSObject,
-        visited: inout Set<ObjectIdentifier>
-    ) -> NSObject? {
-        guard visited.insert(ObjectIdentifier(root)).inserted else {
-            return nil
-        }
-        if root.accessibilityLabel == label { return root }
-        let count = root.accessibilityElementCount()
-        guard count != NSNotFound, count > 0 else { return nil }
-        for index in 0..<count {
-            guard let child = root.accessibilityElement(at: index) as? NSObject else {
-                continue
-            }
-            if let match = accessibilityElement(
-                label: label,
-                in: child,
-                visited: &visited
-            ) {
-                return match
-            }
-        }
-        return nil
-    }
-}
-
 @MainActor
 private final class LibraryLocalStoreFake: LibraryLocalStore {
     var summaries: [FlashcardSetSummary]
     var summaryError: LibraryTestFailure?
     var activeError: LibraryTestFailure?
     var deleteError: LibraryTestFailure?
     private(set) var summaryReadCount = 0
     private(set) var activeWrites: [(id: String, isActive: Bool)] = []
     private(set) var deletedSetIDs: [String] = []
     private let events: LibraryEventRecorder?
 
     init(
         summaries: [FlashcardSetSummary] = [],
         events: LibraryEventRecorder? = nil
     ) {
         self.summaries = summaries
         self.events = events
     }
 
     func libraryFlashcardSetSummaries() throws -> [FlashcardSetSummary] {
         summaryReadCount += 1
         if let summaryError { throw summaryError }
         return summaries
     }
 
     func setFlashcardSetActive(id: String, isActive: Bool) throws {
         activeWrites.append((id, isActive))
         if let activeError { throw activeError }
         if let index = summaries.firstIndex(where: { $0.id == id }) {
             let set = summaries[index]
             summaries[index] = FlashcardSetSummary(
                 id: set.id,
                 title: set.title,
                 wordCount: set.wordCount,
                 systemImage: set.systemImage,
                 isActive: isActive,
                 reverseDirectionEnabled: set.reverseDirectionEnabled,
                 description: set.description,
                 searchableText: set.searchableText,
                 publicationStatus: set.publicationStatus
@@ -6018,185 +5073,103 @@ private final class LibraryPublicSetServiceFake: PublicFlashcardSetServing {
     ) throws -> Value {
         switch result {
         case .success(let value):
             return value
         case .failure(.cancelled):
             throw CancellationError()
         case .failure(.urlCancelled):
             throw URLError(.cancelled)
         case .failure(.transportWrappedCancelled):
             throw APIError.transport(CancellationError())
         case .failure(.nestedTransportWrappedURLCancelled):
             throw APIError.transport(
                 APIError.transport(URLError(.cancelled))
             )
         case .failure(let error):
             throw error
         }
     }
 }
 
 @MainActor
 private final class LibraryEventRecorder {
     var values: [String] = []
 }
 
 private enum LibraryTestFailure: Error {
     case refresh
     case catalog
     case publish
     case unpublish
     case importing
     case local
     case cancelled
     case urlCancelled
     case transportWrappedCancelled
     case nestedTransportWrappedURLCancelled
 }
 
 final class DashboardAddMenuStateTests: XCTestCase {
     @MainActor
-    func testAddButtonStaysAtTrailingEdgeBeforeAndAfterExpansion() throws {
-        let defaultsSuite = "DashboardAddMenuStateTests.trailingEdge"
-        let defaults = try XCTUnwrap(
-            UserDefaults(suiteName: defaultsSuite)
-        )
-        defaults.removePersistentDomain(forName: defaultsSuite)
-        defer { defaults.removePersistentDomain(forName: defaultsSuite) }
-        let settings = AppSettingsStore(defaults: defaults)
-        let screenSize = CGSize(width: 390, height: 844)
-        for isExpanded in [false, true] {
-            let host = UIHostingController(
-                rootView: DashboardAddSpeedDial(
-                    isExpanded: isExpanded,
-                    shouldPulse: false,
-                    pulse: false,
-                    focusRestorationRequest: 0,
-                    onToggle: {},
-                    onDismiss: {},
-                    onSelect: { _ in }
-                )
-                .environmentObject(settings)
-            )
-            let window = UIWindow(frame: CGRect(origin: .zero, size: screenSize))
-            window.rootViewController = host
-            window.makeKeyAndVisible()
-            host.view.frame = window.bounds
-            host.view.layoutIfNeeded()
-
-            let expectedLabel = isExpanded ? "Close add menu" : "Add words"
-            let addButton = try XCTUnwrap(
-                accessibilityElement(label: expectedLabel, in: host.view)
-            )
-
-            XCTAssertEqual(
-                addButton.accessibilityFrame.maxX,
-                screenSize.width - 18,
-                accuracy: 1,
-                isExpanded ? "expanded" : "collapsed"
-            )
-            window.isHidden = true
-        }
-    }
-
     func testAccessibilityDynamicTypeUsesExpandedLayout() {
         XCTAssertEqual(
             DashboardAddLayoutPolicy.mode(for: .large),
             .compact
         )
         XCTAssertEqual(
             DashboardAddLayoutPolicy.mode(for: .xxxLarge),
             .compact
         )
         XCTAssertEqual(
             DashboardAddLayoutPolicy.mode(for: .accessibility1),
             .accessibility
         )
         XCTAssertEqual(
             DashboardAddLayoutPolicy.mode(for: .accessibility3),
             .accessibility
         )
         XCTAssertEqual(
             DashboardAddLayoutPolicy.mode(for: .accessibility5),
             .accessibility
         )
     }
 
-    private func accessibilityElement(
-        label: String,
-        in root: NSObject
-    ) -> NSObject? {
-        var visited: Set<ObjectIdentifier> = []
-        return accessibilityElement(
-            label: label,
-            in: root,
-            visited: &visited
-        )
-    }
-
-    private func accessibilityElement(
-        label: String,
-        in root: NSObject,
-        visited: inout Set<ObjectIdentifier>
-    ) -> NSObject? {
-        let identifier = ObjectIdentifier(root)
-        guard visited.insert(identifier).inserted else { return nil }
-        if root.accessibilityLabel == label {
-            return root
-        }
-        let count = root.accessibilityElementCount()
-        guard count != NSNotFound, count > 0 else { return nil }
-        for index in 0..<count {
-            guard let child = root.accessibilityElement(at: index) as? NSObject else {
-                continue
-            }
-            if let match = accessibilityElement(
-                label: label,
-                in: child,
-                visited: &visited
-            ) {
-                return match
-            }
-        }
-        return nil
-    }
-
     func testApprovedOptionsHaveStableOrderAndDestinations() {
         XCTAssertEqual(
             DashboardAddMenuState.options,
             [.aiTranslate, .manualEntry, .scanDocument, .selectImage, .pasteText]
         )
         XCTAssertEqual(
             DashboardAddMenuState.destination(for: .aiTranslate),
             .aiTranslate
         )
         XCTAssertEqual(
             DashboardAddMenuState.destination(for: .manualEntry),
             .manualEntry
         )
         XCTAssertEqual(
             DashboardAddMenuState.destination(for: .scanDocument),
             .localImport(.scanDocument)
         )
         XCTAssertEqual(
             DashboardAddMenuState.destination(for: .selectImage),
             .localImport(.photo)
         )
         XCTAssertEqual(
             DashboardAddMenuState.destination(for: .pasteText),
             .localImport(.paste)
         )
         XCTAssertNil(DashboardAddMenuState.destination(for: .selectFiles))
     }
 
     func testExpandDismissSelectAndPulseTransitions() {
         var state = DashboardAddMenuState()
 
         XCTAssertFalse(state.isExpanded)
         XCTAssertTrue(state.shouldPulse(selectedWordCount: 3))
 
         state.toggle()
         XCTAssertTrue(state.isExpanded)
         XCTAssertFalse(state.shouldPulse(selectedWordCount: 3))
 
         XCTAssertEqual(state.select(.scanDocument), .localImport(.scanDocument))
         XCTAssertFalse(state.isExpanded)
diff --git a/FlashCardAIUITests/UILaunchFixtureTests.swift b/FlashCardAIUITests/UILaunchFixtureTests.swift
new file mode 100644
index 0000000..10edffc
--- /dev/null
+++ b/FlashCardAIUITests/UILaunchFixtureTests.swift
@@ -0,0 +1,465 @@
+import XCTest
+
+final class UILaunchFixtureTests: XCTestCase {
+    func testLearnSearchOpensARealEditableField() {
+        let app = launchFixture()
+
+        let search = app.buttons["Search Learn"]
+        XCTAssertTrue(search.waitForExistence(timeout: 10), app.debugDescription)
+        XCTAssertTrue(app.buttons["Settings"].exists)
+        XCTAssertFalse(app.textFields["Learn search field"].exists)
+        search.tap()
+
+        let field = app.textFields["Learn search field"]
+        XCTAssertTrue(field.waitForExistence(timeout: 5), app.debugDescription)
+        field.tap()
+        field.typeText("owl")
+        XCTAssertEqual(field.value as? String, "owl")
+        XCTAssertTrue(app.buttons["Settings"].exists)
+        app.buttons["Close Learn search"].tap()
+        XCTAssertFalse(field.exists)
+        XCTAssertTrue(search.exists)
+        XCTAssertTrue(app.buttons["Settings"].exists)
+        search.tap()
+        XCTAssertTrue(field.waitForExistence(timeout: 5), app.debugDescription)
+        let clearedValue = field.value as? String
+        XCTAssertTrue(clearedValue == "" || clearedValue == "Search words or flashcard sets", app.debugDescription)
+    }
+
+    func testZeroCardDashboardShowsGuidanceWithoutAddSpeedDial() {
+        let app = launchFixture()
+        XCTAssertTrue(app.staticTexts[
+            "No flashcards yet. Tap Create to build a set with AI, text, a file, or a photo — or choose a topic from Library."
+        ].waitForExistence(timeout: 10), app.debugDescription)
+        XCTAssertFalse(app.buttons["Add words"].exists)
+    }
+
+    func testFloatingTabsAreOrderedAndCreateKeepsLibrarySelected() {
+        let app = launchFixture()
+        let tabs = ["Learn", "Library", "Pro", "Create"].map { app.buttons[$0] }
+        for tab in tabs { XCTAssertTrue(tab.waitForExistence(timeout: 10), app.debugDescription) }
+        let positions = tabs.map { $0.frame.minX }
+        XCTAssertEqual(positions, positions.sorted())
+        XCTAssertFalse(app.buttons["Home"].exists)
+
+        tabs[1].tap()
+        XCTAssertTrue(app.buttons["Library"].isSelected)
+        tabs[3].tap()
+        XCTAssertTrue(app.staticTexts["Create flashcard set"].waitForExistence(timeout: 10), app.debugDescription)
+        app.buttons["Back"].tap()
+        XCTAssertTrue(app.buttons["Library"].waitForExistence(timeout: 10), app.debugDescription)
+        XCTAssertTrue(app.buttons["Library"].isSelected)
+    }
+
+    func testCreateSetOpensWithoutKeyboardAcceptsNameAndKeepsEmptyToggle() {
+        let app = launchFixture()
+        app.buttons["Create"].tap()
+        XCTAssertTrue(app.staticTexts["Create flashcard set"].waitForExistence(timeout: 10), app.debugDescription)
+        let name = app.textFields["e.g. Travel phrases"]
+        XCTAssertTrue(name.exists, app.debugDescription)
+        XCTAssertFalse(app.keyboards.firstMatch.exists)
+        let toggle = app.switches["Start learning this set"]
+        XCTAssertTrue(toggle.exists, app.debugDescription)
+        XCTAssertFalse(app.staticTexts["Words in this set"].exists)
+        name.tap()
+        name.typeText("Travel")
+        XCTAssertEqual(name.value as? String, "Travel")
+    }
+
+    func testPopulatedSetPlacesLearningToggleAboveWords() {
+        let app = launchFixture("populated-set")
+        openTravelSetForEditing(in: app)
+        let toggle = app.switches["Start learning this set"]
+        let heading = app.staticTexts["Words in this set"]
+        XCTAssertTrue(toggle.waitForExistence(timeout: 10), app.debugDescription)
+        XCTAssertTrue(heading.waitForExistence(timeout: 10), app.debugDescription)
+        XCTAssertGreaterThan(toggle.frame.height, 0)
+        XCTAssertGreaterThan(heading.frame.height, 0)
+        XCTAssertLessThanOrEqual(toggle.frame.maxY, heading.frame.minY)
+        app.scrollViews.firstMatch.swipeUp()
+        XCTAssertTrue(app.staticTexts["Ticket"].waitForExistence(timeout: 5), app.debugDescription)
+    }
+
+    func testLibrarySearchFiltersAStoredWordAndClosesCleanly() {
+        let app = launchFixture("populated-set")
+        app.buttons["Library"].tap()
+        let options = app.buttons["Set options for Travel"]
+        XCTAssertTrue(options.waitForExistence(timeout: 10), app.debugDescription)
+        XCTAssertFalse(app.textFields["Library search field"].exists)
+        app.buttons["Search Library"].tap()
+        let field = app.textFields["Library search field"]
+        XCTAssertTrue(field.waitForExistence(timeout: 5), app.debugDescription)
+        field.tap()
+        XCTAssertTrue(app.keyboards.firstMatch.exists)
+        field.typeText("ticket")
+        XCTAssertEqual(field.value as? String, "ticket")
+        XCTAssertTrue(options.exists)
+        field.typeText("zzzz")
+        XCTAssertFalse(options.exists)
+        app.buttons["Close Library search"].tap()
+        XCTAssertFalse(field.exists)
+        XCTAssertTrue(options.waitForExistence(timeout: 5), app.debugDescription)
+        XCTAssertTrue(app.buttons["Search Library"].exists)
+    }
+
+    func testActiveLibraryMenuAndPendingBadge() {
+        let app = launchFixture("active-pending-set")
+        app.buttons["Library"].tap()
+        let options = app.buttons["Set options for Travel"]
+        XCTAssertTrue(options.waitForExistence(timeout: 10), app.debugDescription)
+        XCTAssertTrue(app.staticTexts["Pending approval"].exists, app.debugDescription)
+        options.tap()
+        XCTAssertTrue(app.buttons["Remove from learning"].waitForExistence(timeout: 5), app.debugDescription)
+        XCTAssertFalse(app.buttons["Public"].exists)
+        XCTAssertFalse(app.buttons["Private"].exists)
+    }
+
+    func testDashboardAddButtonKeepsItsTrailingEdgeWhenExpanded() {
+        let app = launchFixture("active-pending-set")
+        let add = app.buttons["Add words"]
+        XCTAssertTrue(add.waitForExistence(timeout: 10), app.debugDescription)
+        let trailingEdge = add.frame.maxX
+        XCTAssertEqual(trailingEdge, app.frame.maxX - 18, accuracy: 2)
+        add.tap()
+        let close = app.buttons["Close add menu"]
+        XCTAssertTrue(close.waitForExistence(timeout: 5), app.debugDescription)
+        XCTAssertEqual(close.frame.maxX, trailingEdge, accuracy: 2)
+    }
+
+    func testSettingsRemindersMenuShowsApprovedGuidance() {
+        let app = launchFixture()
+        openSettings(in: app)
+        let reminders = app.buttons["Reminders"]
+        XCTAssertTrue(reminders.waitForExistence(timeout: 5), app.debugDescription)
+        reminders.tap()
+        XCTAssertTrue(app.staticTexts["When can we remind you?"].waitForExistence(timeout: 5), app.debugDescription)
+    }
+
+    func testSettingsDailyGoalSelectionUpdatesItsSummary() {
+        let app = launchFixture()
+        openSettings(in: app)
+        let goal = app.buttons["Daily goal"]
+        XCTAssertTrue(goal.waitForExistence(timeout: 5), app.debugDescription)
+        XCTAssertEqual(goal.value as? String, "5 new words per day")
+        goal.tap()
+        let pause = app.buttons["0"]
+        XCTAssertTrue(pause.waitForExistence(timeout: 5), app.debugDescription)
+        pause.tap()
+        app.navigationBars["Daily goal"].buttons.firstMatch.tap()
+        XCTAssertEqual(goal.value as? String, "New cards paused")
+    }
+
+    func testAccountRegistrationShowsCreatedNoticeAndProfile() {
+        let app = launchFixture("account-success")
+        openAccount(in: app)
+        let optionalAccountNote = app.staticTexts.matching(
+            NSPredicate(format: "label BEGINSWITH %@", "An account is optional.")
+        ).firstMatch
+        XCTAssertTrue(optionalAccountNote.exists, app.debugDescription)
+        app.buttons["Create account"].tap()
+        XCTAssertTrue(app.textFields["account.email"].waitForExistence(timeout: 5), app.debugDescription)
+        XCTAssertTrue(app.secureTextFields["account.password"].exists)
+        XCTAssertTrue(app.secureTextFields["account.confirmPassword"].exists)
+        XCTAssertTrue(app.buttons["account.google"].exists)
+        XCTAssertFalse(app.buttons["Continue with Apple"].exists)
+        app.buttons["Show password"].tap()
+        XCTAssertTrue(app.buttons["Hide password"].waitForExistence(timeout: 5), app.debugDescription)
+        app.buttons["Hide password"].tap()
+        XCTAssertTrue(app.secureTextFields["account.password"].waitForExistence(timeout: 5), app.debugDescription)
+        enterEmailCredentials(in: app, confirming: true)
+        app.buttons["account.email.submit"].tap()
+        let notice = app.alerts["Account created"]
+        XCTAssertTrue(notice.waitForExistence(timeout: 10), app.debugDescription)
+        notice.buttons["Continue"].tap()
+        XCTAssertTrue(app.staticTexts["Signed in with email"].waitForExistence(timeout: 5), app.debugDescription)
+    }
+
+    func testEmailLoginClosesFormAndShowsProfile() {
+        let app = launchFixture("account-success")
+        openAccount(in: app)
+        app.buttons["Log in"].tap()
+        XCTAssertTrue(app.textFields["account.email"].waitForExistence(timeout: 5), app.debugDescription)
+        XCTAssertTrue(app.secureTextFields["account.password"].exists)
+        XCTAssertFalse(app.secureTextFields["account.confirmPassword"].exists)
+        enterEmailCredentials(in: app, confirming: false)
+        app.buttons["account.email.submit"].tap()
+        XCTAssertTrue(app.staticTexts["Signed in with email"].waitForExistence(timeout: 10), app.debugDescription)
+        XCTAssertFalse(app.secureTextFields["account.password"].exists)
+    }
+
+    func testDarkLargeTextAccountKeepsProfileActionsAndWelcomeReachable() {
+        let app = launchFixture("account-large-dark")
+        openAccount(in: app)
+        app.buttons["Log in"].tap()
+        enterEmailCredentials(in: app, confirming: false)
+        app.buttons["account.email.submit"].tap()
+        XCTAssertTrue(app.staticTexts["Signed in with email"].waitForExistence(timeout: 10), app.debugDescription)
+        app.scrollViews.firstMatch.swipeUp()
+        XCTAssertTrue(app.buttons["Sign out"].waitForExistence(timeout: 5), app.debugDescription)
+        XCTAssertTrue(app.buttons["Delete account"].exists, app.debugDescription)
+        app.buttons["Sign out"].tap()
+        XCTAssertTrue(app.buttons["Create account"].waitForExistence(timeout: 10), app.debugDescription)
+    }
+
+    func testOnboardingPaywallCloseLeavesOnboarding() {
+        assertOnboardingPaywallCompletes(fixture: "onboarding-close", action: "Close purchase page")
+    }
+
+    func testOnboardingPaywallPurchaseLeavesOnboarding() {
+        assertOnboardingPaywallCompletes(fixture: "onboarding-purchase", action: "Start 7-day free trial")
+    }
+
+    func testOnboardingPaywallRestoreLeavesOnboarding() {
+        assertOnboardingPaywallCompletes(fixture: "onboarding-restore", action: "Restore")
+    }
+
+    func testRejectedSavedDashboardCardCanBeDeletedAcrossRelaunch() {
+        let app = launchPersistentFixture("locked-rejected")
+        assertLockedCard(in: app, id: "ui-rejected", title: "Rejected retained", onDashboard: true)
+        deleteAndVerifyAcrossRelaunch(in: app, id: "ui-rejected", title: "Rejected retained")
+    }
+
+    func testRejectedSavedEditorCardCanBeDeletedAcrossRelaunch() {
+        let app = launchPersistentFixture("locked-rejected")
+        openTravelSetForEditing(in: app)
+        app.scrollViews.firstMatch.swipeUp()
+        assertLockedCard(in: app, id: "ui-rejected", title: "Rejected retained", onDashboard: false)
+        deleteAndVerifyAcrossRelaunch(in: app, id: "ui-rejected", title: "Rejected retained")
+    }
+
+    func testEleventhFreeDashboardCardCanBeDeletedAcrossRelaunch() {
+        let app = launchPersistentFixture("locked-eleventh")
+        searchLearn(in: app, for: "Saved 11")
+        assertLockedCard(in: app, id: "word-11", title: "Saved 11", onDashboard: true)
+        deleteAndVerifyAcrossRelaunch(in: app, id: "word-11", title: "Saved 11")
+    }
+
+    func testEleventhFreeEditorCardCanBeDeletedAcrossRelaunch() {
+        let app = launchPersistentFixture("locked-eleventh")
+        openTravelSetForEditing(in: app)
+        app.scrollViews.firstMatch.swipeUp()
+        assertLockedCard(in: app, id: "word-11", title: "Saved 11", onDashboard: false)
+        deleteAndVerifyAcrossRelaunch(in: app, id: "word-11", title: "Saved 11")
+    }
+
+    func testSavedSetEditorKeepsLearningToggleAfterLastCardDeletion() {
+        let app = launchFixture("populated-set")
+        openTravelSetForEditing(in: app)
+        app.scrollViews.firstMatch.swipeUp()
+        let card = app.staticTexts["Ticket"]
+        XCTAssertTrue(card.waitForExistence(timeout: 5), app.debugDescription)
+        card.tap()
+        XCTAssertTrue(app.staticTexts["word.expanded.ui-fixture-ticket"].exists, app.debugDescription)
+        app.scrollViews.firstMatch.swipeUp()
+        let delete = app.buttons["word.delete.ui-fixture-ticket"]
+        XCTAssertTrue(delete.waitForExistence(timeout: 5), app.debugDescription)
+        delete.tap()
+        confirmWordDeletion(in: app)
+        assertDisappears(delete)
+        XCTAssertTrue(app.staticTexts["Edit flashcard set"].exists)
+        XCTAssertTrue(app.switches["Start learning this set"].exists)
+        XCTAssertFalse(app.staticTexts["Words in this set"].exists)
+    }
+
+    func testProPageCloseReturnsToLearn() {
+        let app = launchFixture()
+        openPro(in: app)
+        for label in ["EXPLORE PREMIUM", "Grow your vocabulary with Premium.", "AI translations"] {
+            XCTAssertTrue(app.staticTexts[label].exists, "Missing \(label): \(app.debugDescription)")
+        }
+        XCTAssertTrue(app.buttons["Select yearly plan"].exists)
+        XCTAssertTrue(app.buttons["Select monthly plan"].exists)
+        XCTAssertTrue(app.buttons["Library"].exists)
+        app.buttons["Close purchase page"].tap()
+        XCTAssertTrue(app.buttons["Search Learn"].waitForExistence(timeout: 10), app.debugDescription)
+        XCTAssertFalse(app.textFields["Learn search field"].exists)
+    }
+
+    func testProPurchaseSuccessReturnsToLearn() {
+        let app = launchFixture("purchase-success")
+        openPro(in: app)
+        app.buttons["Start 7-day free trial"].tap()
+        XCTAssertTrue(app.buttons["Search Learn"].waitForExistence(timeout: 10), app.debugDescription)
+        XCTAssertFalse(app.textFields["Learn search field"].exists)
+    }
+
+    func testProRestoreSuccessReturnsToLearn() {
+        let app = launchFixture("restore-success")
+        openPro(in: app)
+        app.buttons["Restore"].tap()
+        XCTAssertTrue(app.buttons["Search Learn"].waitForExistence(timeout: 10), app.debugDescription)
+        XCTAssertFalse(app.textFields["Learn search field"].exists)
+    }
+
+    func testProCloseKeepsIncompleteOnboardingAcrossRelaunch() {
+        assertProActionKeepsOnboardingIncomplete("Close purchase page")
+    }
+
+    func testProPurchaseKeepsIncompleteOnboardingAcrossRelaunch() {
+        assertProActionKeepsOnboardingIncomplete("Start 7-day free trial")
+    }
+
+    func testProRestoreKeepsIncompleteOnboardingAcrossRelaunch() {
+        assertProActionKeepsOnboardingIncomplete("Restore")
+    }
+
+    // The rendered 100/unlimited-copy RED was captured in Mac run 36447802604.
+    func testPaywallShowsKnownFreeLimitWithoutUnlimitedAIPromises() {
+        let app = launchFixture()
+        openPro(in: app)
+        XCTAssertTrue(app.staticTexts["Add words manually"].waitForExistence(timeout: 10), app.debugDescription)
+        XCTAssertTrue(app.staticTexts["AI translations"].exists)
+        XCTAssertTrue(app.staticTexts["Extract words from photos"].exists)
+        XCTAssertTrue(app.staticTexts["10"].exists, app.debugDescription)
+        XCTAssertTrue(app.staticTexts["Limited"].exists, app.debugDescription)
+        XCTAssertFalse(app.staticTexts["100"].exists)
+        XCTAssertFalse(app.staticTexts["25"].exists)
+        XCTAssertFalse(app.staticTexts["∞"].exists)
+        XCTAssertFalse(app.staticTexts["Daily reminders"].exists)
+    }
+
+    private func launchFixture(_ name: String = "empty-library") -> XCUIApplication {
+        let app = XCUIApplication()
+        app.launchArguments = ["-owl-ui-fixture", name]
+        app.launch()
+        return app
+    }
+
+    private func launchPersistentFixture(_ name: String) -> XCUIApplication {
+        let app = XCUIApplication()
+        app.launchArguments = ["-owl-ui-fixture", name, "-owl-ui-fixture-id", UUID().uuidString]
+        app.launch()
+        return app
+    }
+
+    private func assertDisappears(_ element: XCUIElement) {
+        let gone = XCTNSPredicateExpectation(predicate: NSPredicate(format: "exists == false"), object: element)
+        XCTAssertEqual(XCTWaiter.wait(for: [gone], timeout: 5), .completed)
+    }
+
+    private func searchLearn(in app: XCUIApplication, for query: String) {
+        app.buttons["Search Learn"].tap()
+        let field = app.textFields["Learn search field"]
+        XCTAssertTrue(field.waitForExistence(timeout: 5), app.debugDescription)
+        field.tap()
+        field.typeText(query)
+    }
+
+    private func assertLockedCard(in app: XCUIApplication, id: String, title: String, onDashboard: Bool) {
+        let card = app.staticTexts[title]
+        XCTAssertTrue(card.waitForExistence(timeout: 10), app.debugDescription)
+        XCTAssertTrue(app.staticTexts["LOCKED"].exists)
+        let review = app.buttons["learn.reviewWord.\(id)"]
+        if onDashboard {
+            XCTAssertTrue(review.exists)
+            XCTAssertFalse(review.isEnabled)
+        } else {
+            XCTAssertFalse(review.exists)
+        }
+        let expanded = app.descendants(matching: .any)["word.expanded.\(id)"]
+        XCTAssertFalse(expanded.exists)
+        card.tap()
+        XCTAssertFalse(expanded.exists)
+        XCTAssertFalse(app.buttons["Edit"].exists)
+        XCTAssertTrue(app.buttons["word.delete.\(id)"].exists)
+    }
+
+    private func deleteAndVerifyAcrossRelaunch(in app: XCUIApplication, id: String, title: String) {
+        let card = app.staticTexts[title]
+        app.buttons["word.delete.\(id)"].tap()
+        confirmWordDeletion(in: app)
+        assertDisappears(card)
+        app.terminate()
+        app.launch()
+        searchLearn(in: app, for: title)
+        XCTAssertFalse(card.exists, app.debugDescription)
+        XCTAssertFalse(app.buttons["word.delete.\(id)"].exists)
+        XCTAssertTrue(app.staticTexts["No matches for “\(title)”"].waitForExistence(timeout: 5), app.debugDescription)
+    }
+
+    private func confirmWordDeletion(in app: XCUIApplication) {
+        let dialog = app.sheets["Delete this word?"]
+        XCTAssertTrue(dialog.waitForExistence(timeout: 5), app.debugDescription)
+        let buttons = dialog.buttons.matching(identifier: "word.confirmDelete")
+        let count = buttons.count
+        guard count > 0 else {
+            XCTFail("Missing Delete action in confirmation dialog")
+            return
+        }
+        // XCTest reports the SwiftUI action and its nested native button; tap the leaf.
+        buttons.element(boundBy: count - 1).tap()
+    }
+
+    private func openPro(in app: XCUIApplication) {
+        let pro = app.buttons["Pro"]
+        XCTAssertTrue(pro.waitForExistence(timeout: 10), app.debugDescription)
+        pro.tap()
+        XCTAssertTrue(app.buttons["Close purchase page"].waitForExistence(timeout: 10), app.debugDescription)
+    }
+
+    private func assertProActionKeepsOnboardingIncomplete(_ action: String) {
+        let app = XCUIApplication()
+        let persistentArguments = ["-owl-ui-fixture", "pro-incomplete", "-owl-ui-fixture-id", UUID().uuidString]
+        app.launchArguments = persistentArguments + ["-owl-ui-show-tabs-once"]
+        app.launch()
+        openPro(in: app)
+        let control = app.buttons[action]
+        XCTAssertTrue(control.waitForExistence(timeout: 10), app.debugDescription)
+        control.tap()
+        XCTAssertTrue(app.buttons["Search Learn"].waitForExistence(timeout: 10), app.debugDescription)
+
+        app.terminate()
+        app.launchArguments = persistentArguments
+        app.launch()
+        XCTAssertTrue(app.buttons["Get Started"].waitForExistence(timeout: 10), app.debugDescription)
+        XCTAssertFalse(app.buttons["Search Learn"].exists)
+    }
+
+    private func openSettings(in app: XCUIApplication) {
+        let settings = app.buttons["Settings"]
+        XCTAssertTrue(settings.waitForExistence(timeout: 10), app.debugDescription)
+        settings.tap()
+        XCTAssertTrue(app.buttons["Close settings"].waitForExistence(timeout: 5), app.debugDescription)
+    }
+
+    private func openAccount(in app: XCUIApplication) {
+        openSettings(in: app)
+        let account = app.buttons["settings.account"]
+        XCTAssertTrue(account.waitForExistence(timeout: 5), app.debugDescription)
+        account.tap()
+        XCTAssertTrue(app.staticTexts["Your Owl AI account"].waitForExistence(timeout: 5), app.debugDescription)
+    }
+
+    private func enterEmailCredentials(in app: XCUIApplication, confirming: Bool) {
+        let email = app.textFields["account.email"]
+        email.tap()
+        email.typeText("learner@example.test")
+        let password = app.secureTextFields["account.password"]
+        password.tap()
+        password.typeText("correct horse")
+        if confirming {
+            let confirmation = app.secureTextFields["account.confirmPassword"]
+            confirmation.tap()
+            confirmation.typeText("correct horse")
+        }
+    }
+
+    private func assertOnboardingPaywallCompletes(fixture: String, action: String) {
+        let app = launchFixture(fixture)
+        let control = app.buttons[action]
+        XCTAssertTrue(control.waitForExistence(timeout: 10), app.debugDescription)
+        control.tap()
+        XCTAssertTrue(app.buttons["Search Learn"].waitForExistence(timeout: 10), app.debugDescription)
+        XCTAssertFalse(app.buttons["Close purchase page"].exists)
+        XCTAssertFalse(app.buttons["Close settings"].exists)
+    }
+
+    private func openTravelSetForEditing(in app: XCUIApplication) {
+        app.buttons["Library"].tap()
+        let options = app.buttons["Set options for Travel"]
+        XCTAssertTrue(options.waitForExistence(timeout: 10), app.debugDescription)
+        options.tap()
+        app.buttons["Edit"].tap()
+        XCTAssertTrue(app.staticTexts["Edit flashcard set"].waitForExistence(timeout: 10), app.debugDescription)
+    }
+}
diff --git a/Presentation/Features/Account/AccountView.swift b/Presentation/Features/Account/AccountView.swift
index 7fded40..b8213a3 100644
--- a/Presentation/Features/Account/AccountView.swift
+++ b/Presentation/Features/Account/AccountView.swift
@@ -1,47 +1,51 @@
 import SwiftUI
 import UIKit
 
 struct AccountView: View {
     @EnvironmentObject private var session: AccountSessionController
     @EnvironmentObject private var settings: AppSettingsStore
+    #if DEBUG
+    @StateObject private var model = AccountViewModel(identity: UIAutomationFixture.accountIdentity)
+    #else
     @StateObject private var model = AccountViewModel()
+    #endif
     @State private var entry: AccountEntry?
 
     var body: some View {
         ScrollView {
             VStack(spacing: 28) {
                 if let profile = session.profile {
                     AccountProfileContent(profile: profile, model: model)
                 } else {
                     welcome
                 }
             }
             .frame(maxWidth: 520)
             .frame(maxWidth: .infinity)
             .padding(24)
         }
         .background(settings.currentBackground)
         .navigationTitle("Account")
         .navigationBarTitleDisplayMode(.inline)
         .toolbar(.visible, for: .navigationBar)
         .tint(settings.currentAccent)
         .task { await session.restore() }
         .navigationDestination(item: $entry) { entry in
             AccountProviderView(entry: entry, model: model, showLogin: { self.entry = .login })
         }
     }
 
     private var welcome: some View {
         VStack(spacing: 28) {
             Image("OnboardingOwlHero")
                 .resizable()
                 .scaledToFit()
                 .frame(maxWidth: 210, maxHeight: 230)
                 .accessibilityHidden(true)
                 .padding(.top, 24)
 
             VStack(spacing: 12) {
                 Text("Your Owl AI account")
                     .font(.system(.largeTitle, design: settings.fontPreset.design, weight: .bold))
                     .foregroundStyle(settings.currentInk)
                 Text("A little space for your learning journey.")
diff --git a/Presentation/Features/Home/Components/DashboardAddSpeedDial.swift b/Presentation/Features/Home/Components/DashboardAddSpeedDial.swift
index c9bf646..3c10f92 100644
--- a/Presentation/Features/Home/Components/DashboardAddSpeedDial.swift
+++ b/Presentation/Features/Home/Components/DashboardAddSpeedDial.swift
@@ -406,103 +406,102 @@ struct DashboardAddSpeedDial: View {
                 maxWidth: usesAccessibilityLayout ? .infinity : nil,
                 minHeight: 48,
                 alignment: .trailing
             )
             .background(option == .aiTranslate ? accent : card)
             .clipShape(optionShape)
             .overlay(
                 optionShape.stroke(
                     option == .aiTranslate ? Color.clear : appSettings.currentSoft,
                     lineWidth: 1
                 )
             )
             .shadow(color: .black.opacity(0.12), radius: 12, x: 0, y: 6)
         }
         .buttonStyle(.plain)
         .accessibilityLabel(option.title)
         .accessibilityHint(option.subtitle ?? "Opens this add method")
         .accessibilityFocused($focusedOption, equals: option)
     }
 
     private var optionShape: AnyShape {
         if usesAccessibilityLayout {
             AnyShape(RoundedRectangle(cornerRadius: 24, style: .continuous))
         } else {
             AnyShape(Capsule())
         }
     }
 
     private var addButton: some View {
         let showsAnimatedNudge = shouldPulse && pulse && !reduceMotion
         let showsStaticNudge = shouldPulse && reduceMotion
         let hasNudgeEmphasis = showsAnimatedNudge || showsStaticNudge
 
         return Button(action: onToggle) {
             HStack(spacing: 8) {
                 Image(systemName: isExpanded ? "xmark" : "plus")
                     .font(.system(size: 16, weight: .heavy))
                 Text("Add")
                     .font(.headline.weight(.heavy))
             }
+            .scaleEffect(showsAnimatedNudge ? 1.07 : 1)
             .foregroundStyle(Color.white)
             .padding(.horizontal, 18)
             .frame(minHeight: 52)
             .background(hasNudgeEmphasis ? accent : deep)
             .overlay(
-                Capsule().stroke(
+                Capsule().strokeBorder(
                     Color.white.opacity(hasNudgeEmphasis ? 0.96 : 0),
                     lineWidth: 4
                 )
             )
             .overlay(
                 Capsule()
-                    .stroke(accent.opacity(showsAnimatedNudge ? 0.44 : 0), lineWidth: 7)
-                    .padding(-7)
+                    .strokeBorder(accent.opacity(showsAnimatedNudge ? 0.44 : 0), lineWidth: 7)
             )
             .clipShape(Capsule())
             .shadow(
                 color: accent.opacity(showsAnimatedNudge ? 0.62 : 0.16),
                 radius: showsAnimatedNudge ? 26 : 8,
                 x: 0,
                 y: showsAnimatedNudge ? 12 : 4
             )
-            .scaleEffect(showsAnimatedNudge ? 1.07 : 1)
         }
         .buttonStyle(.plain)
         .accessibilityLabel(isExpanded ? "Close add menu" : "Add words")
         .accessibilityHint(
             isExpanded
                 ? "Closes the add methods"
                 : "Opens available ways to add words"
         )
         .accessibilityFocused($addButtonFocused)
     }
 }
 
 struct DashboardAddSetPicker: View {
     @EnvironmentObject private var appSettings: AppSettingsStore
 
     let request: DashboardAddSetSelectionRequest
     let sets: [FlashcardSetSummary]
     let onSelect: (FlashcardSetSummary) -> Void
     let onCancel: () -> Void
 
     var body: some View {
         NavigationStack {
             Group {
                 if sets.isEmpty {
                     ContentUnavailableView(
                         "No active flashcard sets",
                         systemImage: "rectangle.stack.badge.plus",
                         description: Text(
                             "Create a set from Create or activate one in Library, then try \(request.option.title) again."
                         )
                     )
                 } else {
                     List(sets) { set in
                         Button {
                             onSelect(set)
                         } label: {
                             HStack(spacing: 12) {
                                 Image(systemName: set.systemImage)
                                     .foregroundStyle(appSettings.currentAccent)
                                     .frame(width: 30, height: 30)
diff --git a/Presentation/Features/Home/Components/DashboardWordCard.swift b/Presentation/Features/Home/Components/DashboardWordCard.swift
index d2d02ba..09de09e 100644
--- a/Presentation/Features/Home/Components/DashboardWordCard.swift
+++ b/Presentation/Features/Home/Components/DashboardWordCard.swift
@@ -347,80 +347,81 @@ struct DashboardWordCard: View {
     private var flashcardSetRow: some View {
         if let setName = row.flashcardSetLabel, !setName.isEmpty {
             HStack {
                 Spacer(minLength: 0)
                 HStack(spacing: 4) {
                     Image(systemName: flashcardSetTrailingIcon(setName))
                         .font(.system(size: 11))
                     Text(setName)
                         .font(.system(size: 12, weight: .medium))
                 }
                 .foregroundStyle(theme.palette.mutedForeground)
             }
         }
     }
 
     private var expandedSection: some View {
         VStack(alignment: .leading, spacing: 14) {
             Divider()
                 .background(theme.palette.border.opacity(0.5))
 
             let expandedTranslations = row.displayTranslations.dropFirst(3).joined(separator: ", ")
             if !expandedTranslations.isEmpty {
                 VStack(alignment: .leading, spacing: 6) {
                     Text("TRANSLATIONS")
                         .font(.system(size: 11, weight: .semibold))
                         .foregroundStyle(theme.palette.mutedForeground)
                         .tracking(0.6)
 
                     Text(expandedTranslations)
                         .font(.system(size: 16, weight: .semibold))
                         .foregroundStyle(theme.palette.foreground.opacity(0.88))
                         .fixedSize(horizontal: false, vertical: true)
                 }
             }
 
             HStack(alignment: .center) {
                 Text("EXAMPLES")
                     .font(.system(size: 11, weight: .semibold))
                     .foregroundStyle(theme.palette.mutedForeground)
                     .tracking(0.6)
+                    .accessibilityIdentifier("word.expanded.\(row.id)")
 
                 Spacer(minLength: 12)
                 pronunciationChip
             }
 
             if let ex = row.exampleFirst, !ex.isEmpty {
                 Text("*" + ex + "*")
                     .font(.system(size: 15))
                     .italic()
                     .foregroundStyle(theme.palette.foreground.opacity(0.85))
             } else {
                 Text("No examples stored for this word.")
                     .font(.system(size: 14))
                     .foregroundStyle(theme.palette.mutedForeground)
             }
 
             if let notes = trimmedUserNotes {
                 VStack(alignment: .leading, spacing: 6) {
                     Text("MY NOTES")
                         .font(.system(size: 11, weight: .semibold))
                         .foregroundStyle(theme.palette.mutedForeground)
                         .tracking(0.6)
 
                     Text(notes)
                         .font(.system(size: 15, weight: .medium))
                         .foregroundStyle(theme.palette.foreground.opacity(0.84))
                         .lineLimit(nil)
                         .fixedSize(horizontal: false, vertical: true)
                 }
                 .padding(.horizontal, 14)
                 .padding(.vertical, 12)
                 .frame(maxWidth: .infinity, alignment: .leading)
                 .background(theme.palette.primaryMutedBackground)
                 .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
             }
 
             HStack(spacing: 20) {
                 if row.allowsContentEditing, let onEdit {
                     Button {
                         onEdit(row)
diff --git a/Presentation/Features/Home/Views/DashboardView.swift b/Presentation/Features/Home/Views/DashboardView.swift
index c76fb4a..0e08607 100644
--- a/Presentation/Features/Home/Views/DashboardView.swift
+++ b/Presentation/Features/Home/Views/DashboardView.swift
@@ -275,80 +275,81 @@ struct DashboardView: View {
             reloadDashboard()
         }
         .onChange(
             of: addFlowState.isRootNavigationBlocked,
             initial: true
         ) { _, isBlocked in
             isAddFlowBlockingRootNavigation = isBlocked
         }
         .sheet(isPresented: $showThemeChooser) {
             ChooseThemeSheet(theme: theme)
                 .presentationDetents([.large])
         }
         .sheet(item: $rowToEdit) { row in
             EditWordSheet(repository: repository, row: row, onSaved: {
                 reloadDashboard()
             })
             .environmentObject(theme)
         }
         .confirmationDialog(
             "Delete this word?",
             isPresented: Binding(
                 get: { rowPendingDelete != nil },
                 set: { if !$0 { rowPendingDelete = nil } }
             ),
             titleVisibility: .visible
         ) {
             Button("Delete", role: .destructive) {
                 if let r = rowPendingDelete {
                     do {
                         try repository.deleteWord(id: r.id)
                         if viewModel.expandedCardId == r.id {
                             viewModel.expandedCardId = nil
                         }
                         reloadDashboard()
                     } catch {
                         viewModel.loadError = error.localizedDescription
                     }
                 }
                 rowPendingDelete = nil
             }
+            .accessibilityIdentifier("word.confirmDelete")
             Button("Cancel", role: .cancel) {
                 rowPendingDelete = nil
             }
         } message: {
             Text("This cannot be undone.")
         }
         .toolbar(.hidden, for: .navigationBar)
     }
 
     private var topBar: some View {
         HStack(alignment: .center, spacing: 10) {
             Image("OnboardingOwlHero")
                 .resizable()
                 .scaledToFill()
                 .frame(width: 40, height: 40)
                 .clipShape(Circle())
                 .background(Circle().fill(homeSoft))
 
             if isSearchPresented {
                 learnSearchField
                     .onAppear { searchFocused = true }
             } else {
                 Spacer(minLength: 0)
                 Button {
                     viewModel.searchText = ""
                     isSearchPresented = true
                 } label: {
                     Image(systemName: "magnifyingglass")
                         .font(.system(size: 17, weight: .bold))
                         .foregroundStyle(homeInk)
                         .frame(width: 44, height: 44)
                         .background(homeCard)
                         .clipShape(Circle())
                         .shadow(
                             color: .black.opacity(0.07),
                             radius: 14,
                             x: 0,
                             y: 7
                         )
                 }
@@ -697,81 +698,83 @@ struct DashboardView: View {
 
     private func selectAddOption(_ option: AddOption) {
         let targetSets = availableAddTargetSets
         let targetSetID: String
         if option == .aiTranslate,
            DashboardAddMenuState.targetSetID(from: selectedSetId) == nil,
            targetSets.count == 1,
            let onlySet = targetSets.first {
             targetSetID = onlySet.id
         } else {
             targetSetID = selectedSetId
         }
 
         withAnimation(addMenuAnimation) {
             addFlowState.select(
                 option,
                 selectedSetID: targetSetID
             )
         }
     }
 
     private func chooseAddTargetSet(_ set: FlashcardSetSummary) {
         addFlowState.chooseFlashcardSet(set)
     }
 
     private func cancelAddTargetSelection() {
         addFlowState.cancelSetSelection()
     }
 
     private func completeSetSelectionDismissal() {
         let outcome = withAnimation(addMenuAnimation) {
             addFlowState.completeSetSelectionDismissal()
         }
         if outcome == .cancelled {
             updateAddWordPulse()
         }
     }
 
     private func reloadDashboard() {
         viewModel.reload()
-        Task { await StudyReminderScheduler.shared.apply(using: appSettings) }
+        if !UIAutomationFixture.isActive {
+            Task { await StudyReminderScheduler.shared.apply(using: appSettings) }
+        }
         updateAddWordPulse()
     }
 
     private func updateAddWordPulse() {
         guard addFlowState.menuState.shouldAnimatePulse(
             selectedWordCount: viewModel.totalWords,
             reduceMotion: reduceMotion
         ) else {
             pulseAddWordButton = false
             return
         }
         pulseAddWordButton = false
         withAnimation(
             .easeInOut(duration: 0.65)
                 .repeatForever(autoreverses: true)
         ) {
             pulseAddWordButton = true
         }
     }
 
     // NOTE: `todayHeader`, `greeting`, and `greetingTitle` are no longer used by
     // the top bar (replaced by the search field). They are kept here in case they
     // are referenced elsewhere; delete them if the compiler flags them as unused.
     private var todayHeader: String {
         let formatter = DateFormatter()
         formatter.locale = Locale(identifier: "en_US_POSIX")
         formatter.dateFormat = "EEEE, MMM d"
         return formatter.string(from: Date()).uppercased()
     }
 
     private var greeting: String {
         let hour = Calendar.current.component(.hour, from: Date())
         switch hour {
         case 5..<12:
             return "Good morning"
         case 12..<17:
             return "Good afternoon"
         case 17..<22:
             return "Good evening"
         default:
diff --git a/Presentation/Features/Onboarding/Views/ProductOnboardingView.swift b/Presentation/Features/Onboarding/Views/ProductOnboardingView.swift
index d139446..b16181d 100644
--- a/Presentation/Features/Onboarding/Views/ProductOnboardingView.swift
+++ b/Presentation/Features/Onboarding/Views/ProductOnboardingView.swift
@@ -1,113 +1,128 @@
 import SwiftUI
 import UserNotifications
 
+struct ProductOnboardingCompletion {
+    private var hasFinished = false
+
+    mutating func finish(markSettingsComplete: () -> Void, onFinished: () -> Void) {
+        guard !hasFinished else { return }
+        hasFinished = true
+        markSettingsComplete()
+        onFinished()
+    }
+}
+
 /// First-launch welcome, language setup, reminders, and paywall for the no-login flow.
 struct ProductOnboardingView: View {
     static let reminderTitle = "When can we remind you?"
     static let reminderDescription = "We’ll send study reminders between these times—never before or after."
 
     let onFinished: () -> Void
     private let paywallActionsOverride: ProductPaywallActions?
+    private let paywallAccount: AccountProfileModel?
     private let markSettingsCompleteOverride: (() -> Void)?
 
     @EnvironmentObject private var appSettings: AppSettingsStore
     @EnvironmentObject private var storeKit: StoreKitService
     @ObservedObject private var featureFlags = ServerFeatureFlags.shared
-    @State private var hasFinished = false
+    @State private var completion = ProductOnboardingCompletion()
     @State private var page: Int
     @State private var maxVisitedPage = 0
     @State private var reminderFromMinutes = 8 * 60
     @State private var reminderUntilMinutes = 19 * 60
 
     private var ink: Color { appSettings.currentInk }
     private var mutedInk: Color { appSettings.currentMuted }
     private var purple: Color { ProductPalette.primaryPurple }
     private var cream: Color { appSettings.currentBackground }
     private var lavender: Color { appSettings.currentSoft }
 
     // MARK: - Dark-mode-aware helpers
     // In light mode these are identical to the originals (`purple`, `.white`, `ink`),
     // so the light appearance is unchanged. In dark mode they flip so text stays legible.
 
     /// Accent color for text and icons. Lifts in dark mode for contrast on dark surfaces.
     private var accentText: Color {
         appSettings.isDarkMode
             ? Color(red: 0.68, green: 0.62, blue: 0.99)
             : purple
     }
     /// Card / sheet surface — white in light, elevated dark surface in dark.
     private var surface: Color { appSettings.cardSurface }
 
     init(
         initialPage: Int = 0,
         paywallActions: ProductPaywallActions? = nil,
+        paywallAccount: AccountProfileModel? = nil,
         markSettingsComplete: (() -> Void)? = nil,
         onFinished: @escaping () -> Void
     ) {
         _page = State(initialValue: initialPage)
         paywallActionsOverride = paywallActions
+        self.paywallAccount = paywallAccount
         markSettingsCompleteOverride = markSettingsComplete
         self.onFinished = onFinished
     }
 
     var body: some View {
         ZStack {
             cream.ignoresSafeArea()
 
             if page == 0 {
                 welcomePage
                     .transition(.opacity)
             } else if page == 1 {
                 languagePage
                     .transition(.opacity)
             } else if page == 2 {
                 remindersPage
                     .transition(.opacity)
             } else if page == 3, featureFlags.testMode {
                 Color.clear.task { finishOnboarding() }
             } else if page == 3 {
                 ProductPaywallPage(
                     bottomContentInset: 28,
                     loadsProductsOnAppear: false,
                     actions: paywallActionsOverride
                         ?? .live(storeKit: storeKit),
+                    account: paywallAccount,
                     onClose: finishOnboarding,
                     onPurchaseCompleted: finishOnboarding
                 )
                     .transition(.opacity)
             }
         }
         .animation(.easeInOut(duration: 0.2), value: page)
         .statusBarHidden(page == 0 || page == 1 || page == 2 || page == 3)
         .task(id: featureFlags.testMode) {
             guard !featureFlags.testMode else { return }
             if let paywallActionsOverride {
                 guard !paywallActionsOverride.hasProducts else { return }
                 await paywallActionsOverride.loadProducts()
             } else {
                 await storeKit.loadProducts()
             }
         }
     }
 
     private var welcomePage: some View {
         GeometryReader { proxy in
             let height = proxy.size.height
             let heroSide = min(proxy.size.width * 0.66, height * 0.30)
 
             VStack(spacing: 0) {
                 Spacer()
                     .frame(height: max(76, height * 0.12))
 
                 Image("OnboardingOwlHero")
                     .resizable()
                     .scaledToFit()
                     .frame(width: heroSide, height: heroSide)
 
                 Spacer(minLength: max(32, height * 0.05))
 
                 VStack(spacing: 18) {
                     Text("Learn only the\nwords you need")
                         .font(.system(size: 38, weight: .heavy, design: appSettings.fontPreset.design))
                         .foregroundStyle(ink)
                         .multilineTextAlignment(.center)
@@ -617,88 +632,85 @@ struct ProductOnboardingView: View {
         case "it": return "Mela"
         case "de": return "Apfel"
         case "fr": return "Pomme"
         case "ja": return "りんご"
         case "zh": return "苹果"
         case "pt": return "Maçã"
         case "hi": return "सेब"
         case "ko": return "사과"
         case "uk": return "Яблуко"
         case "pl": return "Jabłko"
         case "tg": return "Себ"
         case "uz": return "Olma"
         case "kk": return "Алма"
         default: return "Apple"
         }
     }
 
     private var reminderTimeOptions: [Int] {
         stride(from: 5 * 60, through: 23 * 60, by: 30).map { $0 }
     }
 
     private func formattedReminderTime(_ minutes: Int) -> String {
         let hour = (minutes / 60) % 24
         let minute = minutes % 60
         let isPM = hour >= 12
         let displayHour = hour % 12 == 0 ? 12 : hour % 12
         let displayMinute = String(format: "%02d", minute)
         return "\(displayHour):\(displayMinute) \(isPM ? "PM" : "AM")"
     }
 
     private func allowNotificationsAndContinue() async {
         appSettings.reminderMinutesFromMidnight = [reminderFromMinutes, reminderUntilMinutes]
         let ok = (try? await UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound, .badge])) ?? false
         appSettings.remindersEnabled = ok
         await StudyReminderScheduler.shared.apply(using: appSettings)
         page = 3
         maxVisitedPage = max(maxVisitedPage, 3)
     }
 
     private func finishOnboarding() {
-        guard !hasFinished else { return }
-        hasFinished = true
-        if let markSettingsCompleteOverride {
-            markSettingsCompleteOverride()
-        } else {
-            appSettings.markInitialSettingsComplete()
-        }
-        onFinished()
+        completion.finish(
+            markSettingsComplete: markSettingsCompleteOverride
+                ?? { appSettings.markInitialSettingsComplete() },
+            onFinished: onFinished
+        )
     }
 
     private func primaryButton(title: String, action: @escaping () -> Void) -> some View {
         Button(action: action) {
             HStack(spacing: 14) {
                 Text(title)
                 Image(systemName: "arrow.right")
             }
             .font(.system(size: 23, weight: .heavy, design: appSettings.fontPreset.design))
             .foregroundStyle(.white)
             .frame(maxWidth: .infinity)
             .frame(height: 68)
             .background(purple)
             .clipShape(Capsule())
             .shadow(color: purple.opacity(0.25), radius: 16, x: 0, y: 10)
         }
         .buttonStyle(.plain)
         .padding(.horizontal, 4)
     }
 
     private func compactPrimaryButton(title: String, action: @escaping () -> Void) -> some View {
         Button(action: action) {
             HStack(spacing: 14) {
                 Text(title)
                 Image(systemName: "arrow.right")
             }
             .font(.system(size: 21, weight: .heavy, design: appSettings.fontPreset.design))
             .foregroundStyle(.white)
             .frame(maxWidth: .infinity)
             .frame(height: 60)
             .background(purple)
             .clipShape(Capsule())
             .shadow(color: purple.opacity(0.22), radius: 12, x: 0, y: 8)
         }
         .buttonStyle(.plain)
         .padding(.horizontal, 4)
     }
 
     private func pageDots(activeIndex: Int) -> some View {
         HStack(spacing: 10) {
diff --git a/Presentation/Features/Onboarding/Views/ProductPaywallPage.swift b/Presentation/Features/Onboarding/Views/ProductPaywallPage.swift
index 34af4bf..7273e6d 100644
--- a/Presentation/Features/Onboarding/Views/ProductPaywallPage.swift
+++ b/Presentation/Features/Onboarding/Views/ProductPaywallPage.swift
@@ -38,151 +38,167 @@ enum ProductPaywallLayout {
     }
 }
 
 enum ProductPaywallPlan {
     case yearly
     case monthly
 }
 
 enum ProductPaywallCopy {
     static func actionTitle(for plan: ProductPaywallPlan) -> String {
         switch plan {
         case .yearly:
             return "Start 7-day free trial"
         case .monthly:
             return "Continue with monthly"
         }
     }
 
     static func billingCaption(for plan: ProductPaywallPlan) -> String {
         switch plan {
         case .yearly:
             return "Then $59.88/year ($4.99/month). Cancel anytime."
         case .monthly:
             return "$6.99/month. Cancel anytime."
         }
     }
 }
 
 struct ProductPaywallPage: View {
     let bottomContentInset: CGFloat
     let loadsProductsOnAppear: Bool
     let actions: ProductPaywallActions
     let onClose: () -> Void
     let onPurchaseCompleted: () -> Void
 
     @Environment(\.openURL) private var openURL
     @EnvironmentObject private var appSettings: AppSettingsStore
     @ObservedObject private var featureFlags = ServerFeatureFlags.shared
     @State private var selectedPlan: ProductPaywallPlan = .yearly
     @State private var purchaseErrorText: String?
-    @ObservedObject private var account = AccountProfileModel.shared
+    @ObservedObject private var account: AccountProfileModel
     @State private var showProfile = false
 
+    init(
+        bottomContentInset: CGFloat,
+        loadsProductsOnAppear: Bool,
+        actions: ProductPaywallActions,
+        account: AccountProfileModel? = nil,
+        onClose: @escaping () -> Void,
+        onPurchaseCompleted: @escaping () -> Void
+    ) {
+        self.bottomContentInset = bottomContentInset
+        self.loadsProductsOnAppear = loadsProductsOnAppear
+        self.actions = actions
+        _account = ObservedObject(wrappedValue: account ?? .shared)
+        self.onClose = onClose
+        self.onPurchaseCompleted = onPurchaseCompleted
+    }
+
     private var ink: Color { appSettings.currentInk }
     private var mutedInk: Color { appSettings.currentMuted }
     private var purple: Color { ProductPalette.primaryPurple }
     private var lavender: Color { appSettings.currentSoft }
     private var paywallBackground: Color {
         appSettings.pageBackground(light: lavender)
     }
     private var accentText: Color {
         appSettings.isDarkMode
             ? Color(red: 0.68, green: 0.62, blue: 0.99)
             : purple
     }
     private var surface: Color { appSettings.cardSurface }
     private var ctaFill: Color {
         appSettings.isDarkMode
             ? Color(red: 0.16, green: 0.16, blue: 0.20)
             : ink
     }
     private var paywallGold: Color { Color(red: 0xFA / 255, green: 0xEA / 255, blue: 0xB0 / 255) }
     private var paywallOrange: Color { Color(red: 0xEE / 255, green: 0x98 / 255, blue: 0x68 / 255) }
     private var paywallRed: Color { Color(red: 0xE1 / 255, green: 0x4F / 255, blue: 0x4F / 255) }
 
     var body: some View {
         Group {
             if featureFlags.testMode {
                 VStack(spacing: 20) {
                     Label("Test mode", systemImage: "checkmark.seal")
                         .font(.title.bold())
                     Text("Word limits and subscription requirements are disabled for testing.")
                         .multilineTextAlignment(.center)
                     Button("Continue", action: onClose)
                         .buttonStyle(.borderedProminent)
                 }
                 .padding(32)
                 .frame(maxWidth: .infinity, maxHeight: .infinity)
                 .background(appSettings.currentBackground)
             } else {
                 purchaseContent
             }
         }
     }
 
     private var purchaseContent: some View {
         GeometryReader { proxy in
             let height = proxy.size.height
 
             ZStack(alignment: .bottom) {
                 ScrollView(showsIndicators: false) {
                     VStack(alignment: .leading, spacing: 0) {
                         paywallTopBar
                             .padding(.top, -20)
 
                         Button { showProfile = true } label: {
                             Label(account.hasSharedPremium ? "Shared Premium active · Apple" : "Profile & shared subscription", systemImage: "person.crop.circle")
                                 .font(.subheadline).padding(.top, 14)
                         }
                         if let expiry = account.entitlement?.expiresAt.flatMap(AccountWire.date), account.hasSharedPremium {
                             Text("Premium until \(expiry.formatted(date: .abbreviated, time: .omitted))")
                                 .font(.footnote).foregroundStyle(mutedInk)
                         }
 
-                        Text("UNLOCK EVERYTHING")
+                        Text("EXPLORE PREMIUM")
                             .font(.system(size: 14, weight: .medium, design: appSettings.fontPreset.design))
                             .tracking(4)
                             .foregroundStyle(accentText)
                             .padding(.top, 24)
 
-                        Text("Learn without limits.")
+                        Text("Grow your vocabulary with Premium.")
                             .font(.system(size: 32, weight: .semibold, design: appSettings.fontPreset.design))
                             .foregroundStyle(ink)
                             .lineLimit(2)
                             .minimumScaleFactor(0.68)
                             .padding(.top, 14)
 
                         paywallPlanOptions
                             .padding(.top, 34)
 
                         paywallBenefitsCard
                             .padding(.top, 26)
                             .padding(
                                 .bottom,
                                 ProductPaywallLayout.contentBottomPadding(
                                     bottomContentInset: bottomContentInset
                                 )
                             )
                     }
                     .padding(.horizontal, 24)
                     .frame(minHeight: height, alignment: .top)
                 }
 
                 paywallFixedActionArea
                     .padding(.horizontal, 24)
                     .padding(.bottom, bottomContentInset)
             }
             .background(paywallBackground.ignoresSafeArea())
         }
         .task {
             await account.refresh()
             guard loadsProductsOnAppear, !actions.hasProducts else { return }
             await actions.loadProducts()
         }
         .statusBarHidden(true)
         .sheet(isPresented: $showProfile) { AccountProfileView() }
     }
 
     private var paywallFixedActionArea: some View {
         VStack(spacing: 0) {
             Button {
@@ -253,158 +269,161 @@ struct ProductPaywallPage: View {
     private var paywallTopBar: some View {
         HStack {
             Button {
                 onClose()
             } label: {
                 Image(systemName: "xmark")
                     .font(.system(size: 19, weight: .semibold))
                     .foregroundStyle(ink)
                     .frame(width: 46, height: 46)
                     .background(surface.opacity(0.74))
                     .clipShape(Circle())
                     .shadow(color: .black.opacity(0.08), radius: 10, x: 0, y: 4)
             }
             .buttonStyle(.plain)
             .accessibilityLabel("Close purchase page")
 
             Spacer()
 
             Text("PRO")
                 .font(.system(size: 16, weight: .medium, design: appSettings.fontPreset.design))
                 .tracking(2.4)
             .foregroundStyle(paywallGold)
             .padding(.horizontal, 12)
             .frame(height: 32)
             .background(ctaFill)
             .clipShape(RoundedRectangle(cornerRadius: 8, style: .continuous))
         }
     }
 
     private var paywallBenefitsCard: some View {
         ZStack(alignment: .topTrailing) {
             VStack(spacing: 0) {
                 HStack(spacing: 0) {
                     Spacer(minLength: 0)
                     Text("FREE")
                         .font(.system(size: 13, weight: .semibold, design: appSettings.fontPreset.design))
                         .tracking(1.8)
                         .foregroundStyle(mutedInk)
                         .lineLimit(1)
                         .fixedSize(horizontal: true, vertical: false)
-                        .frame(width: 32, alignment: .center)
+                        .frame(width: 48, alignment: .center)
                 }
                 .padding(.bottom, 10)
 
                 Rectangle()
                     .fill(lavender)
                     .frame(height: 1)
                     .padding(.bottom, 14)
 
                 VStack(spacing: 12) {
-                    paywallBenefitComparisonRow(icon: "character", title: "AI translations", freeValue: "25")
-                    paywallBenefitComparisonRow(icon: "plus.circle.fill", title: "Add words manually", freeValue: "100")
-                    paywallBenefitComparisonRow(icon: "camera", title: "Extract words from photos", freeValue: "∞")
+                    paywallBenefitComparisonRow(icon: "character", title: "AI translations", freeValue: "Limited")
+                    paywallBenefitComparisonRow(icon: "plus.circle.fill", title: "Add words manually", freeValue: "10")
+                    paywallBenefitComparisonRow(icon: "camera", title: "Extract words from photos", freeValue: "Limited")
                     paywallBenefitComparisonRow(icon: "text.bubble", title: "Phrasal verbs", freeValue: nil)
-                    paywallBenefitComparisonRow(icon: "speaker.wave.2.fill", title: "Pronunciation and IPA", freeValue: "25")
+                    paywallBenefitComparisonRow(icon: "speaker.wave.2.fill", title: "Pronunciation and IPA", freeValue: "Limited")
                 }
             }
             .padding(.leading, 18)
             .padding(.trailing, 82)
             .padding(.top, 24)
             .padding(.bottom, 18)
 
             VStack(spacing: 25) {
                 Text("★ PRO")
                     .font(.system(size: 12, weight: .semibold, design: appSettings.fontPreset.design))
                     .tracking(0.8)
                     .foregroundStyle(paywallGold)
                     .frame(width: 56, height: 24)
                     .background(ctaFill)
                     .clipShape(RoundedRectangle(cornerRadius: 11, style: .continuous))
 
                 VStack(spacing: 20) {
                     ForEach(0..<5, id: \.self) { _ in
-                        Text("∞")
-                            .font(.system(size: 25, weight: .semibold, design: appSettings.fontPreset.design))
+                        Image(systemName: "checkmark")
+                            .font(.system(size: 17, weight: .semibold))
                             .foregroundStyle(.white)
                             .frame(width: 34, height: 34)
                             .background(purple)
                             .clipShape(Circle())
+                            .accessibilityLabel("Included")
                     }
                 }
             }
             .frame(width: 58)
             .padding(.vertical, 12)
             .background(lavender)
             .clipShape(RoundedRectangle(cornerRadius: 17, style: .continuous))
             .padding(.top, 8)
             .padding(.trailing, 12)
         }
         .frame(maxWidth: .infinity)
         .background(surface)
         .clipShape(RoundedRectangle(cornerRadius: 20, style: .continuous))
     }
 
     private func paywallBenefitComparisonRow(icon: String, title: String, freeValue: String?) -> some View {
         HStack(spacing: 10) {
             RoundedRectangle(cornerRadius: 13, style: .continuous)
                 .fill(lavender)
                 .frame(width: 42, height: 42)
                 .overlay(
                     Image(systemName: icon)
                         .font(.system(size: 20, weight: .semibold))
                         .foregroundStyle(accentText)
                 )
 
             Text(title)
                 .font(.system(size: 16, weight: .medium, design: appSettings.fontPreset.design))
                 .foregroundStyle(ink)
                 .lineLimit(2)
                 .minimumScaleFactor(0.70)
 
             Spacer(minLength: 4)
 
             if let freeValue {
                 Text(freeValue)
-                    .font(.system(size: 20, weight: .semibold, design: appSettings.fontPreset.design))
+                    .font(.system(size: freeValue == "10" ? 20 : 12, weight: .semibold, design: appSettings.fontPreset.design))
                     .foregroundStyle(ink)
-                    .frame(width: 32, alignment: .center)
+                    .lineLimit(1)
+                    .minimumScaleFactor(0.8)
+                    .frame(width: 48, alignment: .center)
             } else {
                 Image(systemName: "xmark.circle.fill")
                     .font(.system(size: 22, weight: .semibold))
                     .foregroundStyle(Color(red: 0.85, green: 0.25, blue: 0.25).opacity(0.80))
                     .frame(width: 32, alignment: .center)
             }
         }
         .frame(height: 42)
     }
 
     private var paywallPlanOptions: some View {
         VStack(spacing: 14) {
             Button {
                 selectedPlan = .yearly
             } label: {
                 paywallPlanCard(
                     title: "YEARLY",
                     billedPrice: "$59.88",
                     originalPrice: "$83.88",
                     monthlyPrice: "$4.99",
                     footnote: "7-day free trial",
                     showsOfferBadges: true,
                     isSelected: selectedPlan == .yearly
                 )
             }
             .buttonStyle(.plain)
             .accessibilityLabel("Select yearly plan")
 
             Button {
                 selectedPlan = .monthly
             } label: {
                 paywallPlanCard(
                     title: "MONTHLY",
                     billedPrice: nil,
                     originalPrice: nil,
                     monthlyPrice: "$6.99",
                     footnote: nil,
                     showsOfferBadges: false,
                     isSelected: selectedPlan == .monthly
                 )
diff --git a/Presentation/Features/WordSearch/Views/CreateFlashcardSetPage.swift b/Presentation/Features/WordSearch/Views/CreateFlashcardSetPage.swift
index 66831e6..0f74fa9 100644
--- a/Presentation/Features/WordSearch/Views/CreateFlashcardSetPage.swift
+++ b/Presentation/Features/WordSearch/Views/CreateFlashcardSetPage.swift
@@ -153,80 +153,81 @@ struct CreateFlashcardSetPage: View {
         }
         .onDisappear {
             // Unexpected-pop fallback only. The Back button owns metadata and
             // publication finalization; disappearance must never start network
             // work. Empty create-set cleanup remains local and idempotent.
             if !pushWordEntry
                 && !pushManualEntry
                 && localImportSource == nil
                 && !showFileImporter {
                 vm.deleteSetIfEmpty()
             }
         }
         .onReceive(NotificationCenter.default.publisher(for: .reviewQueueRefreshRequested)) { _ in
             vm.reloadWords()
         }
         .sheet(item: $rowToEdit) { row in
             EditWordSheet(
                 repository: repository,
                 row: row,
                 requiredFlashcardSetID: vm.createdFlashcardSetID,
                 onSaved: { vm.contentDidPersist() }
             )
                 .environmentObject(theme)
                 .environmentObject(appSettings)
         }
         .confirmationDialog(
             "Delete this word?",
             isPresented: Binding(
                 get: { rowPendingDelete != nil },
                 set: { if !$0 { rowPendingDelete = nil } }
             ),
             titleVisibility: .visible
         ) {
             Button("Delete", role: .destructive) {
                 if let r = rowPendingDelete {
                     if expandedCardId == r.id { expandedCardId = nil }
                     Task { _ = await vm.deleteWord(id: r.id) }
                 }
                 rowPendingDelete = nil
             }
+            .accessibilityIdentifier("word.confirmDelete")
             .disabled(vm.isPublishing || vm.isFinalizing)
             Button("Cancel", role: .cancel) {
                 rowPendingDelete = nil
             }
         } message: {
             Text("This cannot be undone.")
         }
     }
 
     // MARK: - Header
 
     private var header: some View {
         ZStack {
             Text(vm.isEditing ? "Edit flashcard set" : "Create flashcard set")
                 .font(.system(size: 20, weight: .heavy, design: fontDesign))
                 .foregroundStyle(ink)
                 .frame(maxWidth: .infinity)
 
             HStack {
                 Button {
                     Task {
                         if await vm.finalizeForDismissal() {
                             dismiss()
                         }
                     }
                 } label: {
                     Image(systemName: "chevron.left")
                         .font(.system(size: 22, weight: .heavy))
                         .foregroundStyle(ink)
                         .frame(width: 44, height: 44)
                         .background(card)
                         .clipShape(Circle())
                         .shadow(color: .black.opacity(0.06), radius: 14, x: 0, y: 7)
                 }
                 .buttonStyle(.plain)
                 .accessibilityLabel("Back")
                 .disabled(vm.isFinalizing)
 
                 Spacer()
 
diff --git a/Presentation/SharedUI/FloatingTabBar.swift b/Presentation/SharedUI/FloatingTabBar.swift
index 966d52e..39950aa 100644
--- a/Presentation/SharedUI/FloatingTabBar.swift
+++ b/Presentation/SharedUI/FloatingTabBar.swift
@@ -116,42 +116,43 @@ struct FloatingTabBar: View {
     private func tabButton(_ tab: MainTab, icon: String, label: String) -> some View {
         let selected = selection == tab
         return Button {
             withAnimation(.spring(response: 0.34, dampingFraction: 0.78)) {
                 selection = tab
             }
         } label: {
             pillContent(icon: icon, label: label, selected: selected)
         }
         .buttonStyle(.plain)
         .accessibilityLabel(label)
         .accessibilityAddTraits(selected ? .isSelected : [])
     }
 
     private func actionButton(icon: String, label: String, action: @escaping () -> Void) -> some View {
         Button(action: action) {
             pillContent(icon: icon, label: label, selected: false)
         }
         .buttonStyle(.plain)
         .accessibilityLabel(label)
     }
 
     /// Unselected items are fully transparent; the selected item gets a
     /// soft, light capsule highlight behind just its own icon/label.
     private func pillContent(icon: String, label: String, selected: Bool) -> some View {
         VStack(spacing: 3) {
             Image(systemName: icon)
                 .font(.system(size: 19, weight: .semibold))
             Text(label)
                 .font(.system(size: 10, weight: .bold))
                 .lineLimit(1)
                 .minimumScaleFactor(0.8)
         }
         .foregroundStyle(ink.opacity(selected ? 0.95 : 0.62))
         .frame(maxWidth: .infinity)
         .frame(height: 52)
         .background(
             Capsule()
                 .fill(selected ? Color.white.opacity(0.16) : Color.clear)
         )
+        .contentShape(Capsule())
     }
 }
