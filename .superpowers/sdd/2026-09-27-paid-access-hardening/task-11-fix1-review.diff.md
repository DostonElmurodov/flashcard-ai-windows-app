# Task11 fix round1 review package
Repository: D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/ios
Fix base: f2a8c97df85ce8df9cc807fbf59bf1b9f74fad26
Head: f6a9e194505dfd27ec28b3fbde11bfcb8eca095e
Requirements: task-11-bounded-review-brief.md
Previous findings: task-11-bounded-review.md I1 andI2
Current evidence: task-11-evidence-index.md; task-11-report.md fixround1 appendices.
Windows/backend unchanged duringfix; their originalscopedreview remains applicable.
f6a9e19 fix(ios): keep paywall comparison rows aligned
53ecb1f fix(ios): explain global active-word allowance on paywall
4616a0d test(ios): assert search autofocus and active allowance copy
 .github/workflows/ios-validation.yml                         | 11 ++++++++++-
 FlashCardAIUITests/UILaunchFixtureTests.swift                | 12 ++++++------
 .../Features/Onboarding/Views/ProductPaywallPage.swift       |  7 +++++++
 3 files changed, 23 insertions(+), 7 deletions(-)
diff --git a/.github/workflows/ios-validation.yml b/.github/workflows/ios-validation.yml
index d79a695..bba3fa7 100644
--- a/.github/workflows/ios-validation.yml
+++ b/.github/workflows/ios-validation.yml
@@ -1,87 +1,96 @@
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
           - ui-proof
           - ui-root
+          - ui-review
           - release-compile
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
           build_settings=()
           case "$OWL_TEST_SCOPE" in
             ui-proof)
               selection=(-only-testing:FlashCardAIUITests/UILaunchFixtureTests/testLearnSearchOpensARealEditableField)
               build_settings=(OWL_DEBUG_BUNDLE_ID=com.mavrylo.owlai.uitest)
               ;;
             ui-root)
               selection=(-only-testing:FlashCardAIUITests/UILaunchFixtureTests)
               build_settings=(OWL_DEBUG_BUNDLE_ID=com.mavrylo.owlai.uitest)
               ;;
+            ui-review)
+              selection=(
+                -only-testing:FlashCardAIUITests/UILaunchFixtureTests/testLearnSearchOpensARealEditableField
+                -only-testing:FlashCardAIUITests/UILaunchFixtureTests/testLibrarySearchFiltersAStoredWordAndClosesCleanly
+                -only-testing:FlashCardAIUITests/UILaunchFixtureTests/testPaywallShowsKnownFreeLimitWithoutUnlimitedAIPromises
+              )
+              build_settings=(OWL_DEBUG_BUNDLE_ID=com.mavrylo.owlai.uitest)
+              ;;
             release-compile) ;;
             paid-access)
               selection=(
                 -only-testing:FlashCardAITests/EntitlementMutationBoundaryTests
                 -only-testing:FlashCardAITests/SharedAccountSessionTests
                 -only-testing:FlashCardAITests/ReviewSessionViewModelTests
               )
               ;;
             full)
               build_settings=(OWL_DEBUG_BUNDLE_ID=com.mavrylo.owlai.uitest)
               ;;
             baseline-full) ;;
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
           common=(-project FlashCardAI.xcodeproj -scheme FlashCardAI
             -destination "platform=iOS Simulator,id=$OWL_SIMULATOR"
             -parallel-testing-enabled NO
             "${build_settings[@]}" CODE_SIGNING_ALLOWED=NO)
           if [[ "$OWL_TEST_SCOPE" == release-compile ]]; then
@@ -94,58 +103,58 @@ jobs:
               > "$RUNNER_TEMP/owl-release-settings.json"
             python3 - "$derived_data" "$RUNNER_TEMP/owl-release-settings.json" <<'PY'
           import json, plistlib, sys
           from pathlib import Path
 
           def require(condition, message):
               if not condition:
                   raise SystemExit(message)
 
           derived = Path(sys.argv[1])
           settings = next(row['buildSettings'] for row in json.loads(Path(sys.argv[2]).read_text())
                           if row['target'] == 'FlashCardAI')
           require(settings['PRODUCT_BUNDLE_IDENTIFIER'] == 'com.mavrylo.owlai', 'Unexpected Release bundle setting')
           require(settings['API_BASE_URL'] == 'https://api.mavrylo.com', 'Unexpected Release API URL setting')
           require(settings['APP_ATTEST_ENVIRONMENT'] == 'production', 'Unexpected Release App Attest setting')
           require(settings['CODE_SIGN_ENTITLEMENTS'] == 'Resources/FlashCardAI.entitlements', 'Unexpected entitlements mapping')
           require('DEBUG' not in settings.get('SWIFT_ACTIVE_COMPILATION_CONDITIONS', '').split(), 'DEBUG enabled in Release')
           entitlement_source = plistlib.loads(Path(settings['CODE_SIGN_ENTITLEMENTS']).read_bytes())
           require(entitlement_source['com.apple.developer.devicecheck.appattest-environment'] == '$(APP_ATTEST_ENVIRONMENT)',
                   'Unexpected App Attest entitlement source')
           app = derived / 'Build/Products/Release-iphoneos/FlashCardAI.app'
           info = plistlib.loads((app / 'Info.plist').read_bytes())
           require(info['CFBundleIdentifier'] == 'com.mavrylo.owlai', 'Unexpected built Release bundle ID')
           require(info['APIBaseURL'] == 'https://api.mavrylo.com', 'Unexpected built Release API URL')
           executable = (app / info['CFBundleExecutable']).read_bytes()
           for marker in (b'-owl-ui-fixture', b'ui-fixture.invalid', b'ui-fixture-account',
                          b'learner@example.test', b'correct horse'):
               require(marker not in executable, 'Debug UI fixture marker found in Release executable')
           print('Unsigned iOS device Release compile and bounded configuration checks passed')
           PY
-          elif [[ "$OWL_TEST_SCOPE" == ui-proof || "$OWL_TEST_SCOPE" == ui-root || "$OWL_TEST_SCOPE" == full ]]; then
+          elif [[ "$OWL_TEST_SCOPE" == ui-proof || "$OWL_TEST_SCOPE" == ui-root || "$OWL_TEST_SCOPE" == ui-review || "$OWL_TEST_SCOPE" == full ]]; then
             derived_data="$RUNNER_TEMP/owl-ui-derived-data"
             xcodebuild build-for-testing "${common[@]}" -derivedDataPath "$derived_data" 2>&1 | tee xcode-build.log
             echo "Simulator boot preflight: $(date -u +%Y-%m-%dT%H:%M:%SZ)" | tee simulator-preflight.log
             xcrun simctl bootstatus "$OWL_SIMULATOR" -b 2>&1 | tee -a simulator-preflight.log
             app_path="$derived_data/Build/Products/Debug-iphonesimulator/FlashCardAI.app"
             bundle_id=$(/usr/libexec/PlistBuddy -c 'Print CFBundleIdentifier' "$app_path/Info.plist")
             [[ "$bundle_id" == com.mavrylo.owlai.uitest ]] || { echo "Unexpected UI fixture bundle: $bundle_id" >&2; exit 1; }
             echo "Dedicated app install: $(date -u +%Y-%m-%dT%H:%M:%SZ)" | tee -a simulator-preflight.log
             xcrun simctl install "$OWL_SIMULATOR" "$app_path" 2>&1 | tee -a simulator-preflight.log
             xcrun simctl get_app_container "$OWL_SIMULATOR" "$bundle_id" app 2>&1 | tee -a simulator-preflight.log
             echo "XCTest handoff: $(date -u +%Y-%m-%dT%H:%M:%SZ)" | tee -a simulator-preflight.log
             xcodebuild test-without-building "${common[@]}" "${selection[@]}" -derivedDataPath "$derived_data" \
               -resultBundlePath TestResults.xcresult 2>&1 | tee xcode-test.log
           else
             xcodebuild test "${common[@]}" "${selection[@]}" -resultBundlePath TestResults.xcresult 2>&1 | tee xcode-test.log
           fi
       - uses: actions/upload-artifact@v4
         if: always()
         with:
           name: ios-validation
           path: |
             xcode-test.log
             xcode-build.log
             simulator-preflight.log
             source-head.txt
             TestResults.xcresult
           retention-days: 7
diff --git a/FlashCardAIUITests/UILaunchFixtureTests.swift b/FlashCardAIUITests/UILaunchFixtureTests.swift
index 10edffc..3444281 100644
--- a/FlashCardAIUITests/UILaunchFixtureTests.swift
+++ b/FlashCardAIUITests/UILaunchFixtureTests.swift
@@ -1,46 +1,46 @@
 import XCTest
 
 final class UILaunchFixtureTests: XCTestCase {
     func testLearnSearchOpensARealEditableField() {
         let app = launchFixture()
 
         let search = app.buttons["Search Learn"]
         XCTAssertTrue(search.waitForExistence(timeout: 10), app.debugDescription)
         XCTAssertTrue(app.buttons["Settings"].exists)
         XCTAssertFalse(app.textFields["Learn search field"].exists)
         search.tap()
 
         let field = app.textFields["Learn search field"]
         XCTAssertTrue(field.waitForExistence(timeout: 5), app.debugDescription)
-        field.tap()
-        field.typeText("owl")
+        XCTAssertTrue(app.keyboards.firstMatch.waitForExistence(timeout: 5), app.debugDescription)
+        app.typeText("owl")
         XCTAssertEqual(field.value as? String, "owl")
         XCTAssertTrue(app.buttons["Settings"].exists)
         app.buttons["Close Learn search"].tap()
         XCTAssertFalse(field.exists)
         XCTAssertTrue(search.exists)
         XCTAssertTrue(app.buttons["Settings"].exists)
         search.tap()
         XCTAssertTrue(field.waitForExistence(timeout: 5), app.debugDescription)
         let clearedValue = field.value as? String
         XCTAssertTrue(clearedValue == "" || clearedValue == "Search words or flashcard sets", app.debugDescription)
     }
 
     func testZeroCardDashboardShowsGuidanceWithoutAddSpeedDial() {
         let app = launchFixture()
         XCTAssertTrue(app.staticTexts[
             "No flashcards yet. Tap Create to build a set with AI, text, a file, or a photo — or choose a topic from Library."
         ].waitForExistence(timeout: 10), app.debugDescription)
         XCTAssertFalse(app.buttons["Add words"].exists)
     }
 
     func testFloatingTabsAreOrderedAndCreateKeepsLibrarySelected() {
         let app = launchFixture()
         let tabs = ["Learn", "Library", "Pro", "Create"].map { app.buttons[$0] }
         for tab in tabs { XCTAssertTrue(tab.waitForExistence(timeout: 10), app.debugDescription) }
         let positions = tabs.map { $0.frame.minX }
         XCTAssertEqual(positions, positions.sorted())
         XCTAssertFalse(app.buttons["Home"].exists)
 
         tabs[1].tap()
         XCTAssertTrue(app.buttons["Library"].isSelected)
@@ -62,66 +62,65 @@ final class UILaunchFixtureTests: XCTestCase {
         XCTAssertTrue(toggle.exists, app.debugDescription)
         XCTAssertFalse(app.staticTexts["Words in this set"].exists)
         name.tap()
         name.typeText("Travel")
         XCTAssertEqual(name.value as? String, "Travel")
     }
 
     func testPopulatedSetPlacesLearningToggleAboveWords() {
         let app = launchFixture("populated-set")
         openTravelSetForEditing(in: app)
         let toggle = app.switches["Start learning this set"]
         let heading = app.staticTexts["Words in this set"]
         XCTAssertTrue(toggle.waitForExistence(timeout: 10), app.debugDescription)
         XCTAssertTrue(heading.waitForExistence(timeout: 10), app.debugDescription)
         XCTAssertGreaterThan(toggle.frame.height, 0)
         XCTAssertGreaterThan(heading.frame.height, 0)
         XCTAssertLessThanOrEqual(toggle.frame.maxY, heading.frame.minY)
         app.scrollViews.firstMatch.swipeUp()
         XCTAssertTrue(app.staticTexts["Ticket"].waitForExistence(timeout: 5), app.debugDescription)
     }
 
     func testLibrarySearchFiltersAStoredWordAndClosesCleanly() {
         let app = launchFixture("populated-set")
         app.buttons["Library"].tap()
         let options = app.buttons["Set options for Travel"]
         XCTAssertTrue(options.waitForExistence(timeout: 10), app.debugDescription)
         XCTAssertFalse(app.textFields["Library search field"].exists)
         app.buttons["Search Library"].tap()
         let field = app.textFields["Library search field"]
         XCTAssertTrue(field.waitForExistence(timeout: 5), app.debugDescription)
-        field.tap()
-        XCTAssertTrue(app.keyboards.firstMatch.exists)
-        field.typeText("ticket")
+        XCTAssertTrue(app.keyboards.firstMatch.waitForExistence(timeout: 5), app.debugDescription)
+        app.typeText("ticket")
         XCTAssertEqual(field.value as? String, "ticket")
         XCTAssertTrue(options.exists)
-        field.typeText("zzzz")
+        app.typeText("zzzz")
         XCTAssertFalse(options.exists)
         app.buttons["Close Library search"].tap()
         XCTAssertFalse(field.exists)
         XCTAssertTrue(options.waitForExistence(timeout: 5), app.debugDescription)
         XCTAssertTrue(app.buttons["Search Library"].exists)
     }
 
     func testActiveLibraryMenuAndPendingBadge() {
         let app = launchFixture("active-pending-set")
         app.buttons["Library"].tap()
         let options = app.buttons["Set options for Travel"]
         XCTAssertTrue(options.waitForExistence(timeout: 10), app.debugDescription)
         XCTAssertTrue(app.staticTexts["Pending approval"].exists, app.debugDescription)
         options.tap()
         XCTAssertTrue(app.buttons["Remove from learning"].waitForExistence(timeout: 5), app.debugDescription)
         XCTAssertFalse(app.buttons["Public"].exists)
         XCTAssertFalse(app.buttons["Private"].exists)
     }
 
     func testDashboardAddButtonKeepsItsTrailingEdgeWhenExpanded() {
         let app = launchFixture("active-pending-set")
         let add = app.buttons["Add words"]
         XCTAssertTrue(add.waitForExistence(timeout: 10), app.debugDescription)
         let trailingEdge = add.frame.maxX
         XCTAssertEqual(trailingEdge, app.frame.maxX - 18, accuracy: 2)
         add.tap()
         let close = app.buttons["Close add menu"]
         XCTAssertTrue(close.waitForExistence(timeout: 5), app.debugDescription)
         XCTAssertEqual(close.frame.maxX, trailingEdge, accuracy: 2)
     }
@@ -284,60 +283,61 @@ final class UILaunchFixtureTests: XCTestCase {
     }
 
     func testProRestoreSuccessReturnsToLearn() {
         let app = launchFixture("restore-success")
         openPro(in: app)
         app.buttons["Restore"].tap()
         XCTAssertTrue(app.buttons["Search Learn"].waitForExistence(timeout: 10), app.debugDescription)
         XCTAssertFalse(app.textFields["Learn search field"].exists)
     }
 
     func testProCloseKeepsIncompleteOnboardingAcrossRelaunch() {
         assertProActionKeepsOnboardingIncomplete("Close purchase page")
     }
 
     func testProPurchaseKeepsIncompleteOnboardingAcrossRelaunch() {
         assertProActionKeepsOnboardingIncomplete("Start 7-day free trial")
     }
 
     func testProRestoreKeepsIncompleteOnboardingAcrossRelaunch() {
         assertProActionKeepsOnboardingIncomplete("Restore")
     }
 
     // The rendered 100/unlimited-copy RED was captured in Mac run 36447802604.
     func testPaywallShowsKnownFreeLimitWithoutUnlimitedAIPromises() {
         let app = launchFixture()
         openPro(in: app)
         XCTAssertTrue(app.staticTexts["Add words manually"].waitForExistence(timeout: 10), app.debugDescription)
         XCTAssertTrue(app.staticTexts["AI translations"].exists)
         XCTAssertTrue(app.staticTexts["Extract words from photos"].exists)
         XCTAssertTrue(app.staticTexts["10"].exists, app.debugDescription)
+        XCTAssertTrue(app.staticTexts["10 active words across all languages"].exists, app.debugDescription)
         XCTAssertTrue(app.staticTexts["Limited"].exists, app.debugDescription)
         XCTAssertFalse(app.staticTexts["100"].exists)
         XCTAssertFalse(app.staticTexts["25"].exists)
         XCTAssertFalse(app.staticTexts["∞"].exists)
         XCTAssertFalse(app.staticTexts["Daily reminders"].exists)
     }
 
     private func launchFixture(_ name: String = "empty-library") -> XCUIApplication {
         let app = XCUIApplication()
         app.launchArguments = ["-owl-ui-fixture", name]
         app.launch()
         return app
     }
 
     private func launchPersistentFixture(_ name: String) -> XCUIApplication {
         let app = XCUIApplication()
         app.launchArguments = ["-owl-ui-fixture", name, "-owl-ui-fixture-id", UUID().uuidString]
         app.launch()
         return app
     }
 
     private func assertDisappears(_ element: XCUIElement) {
         let gone = XCTNSPredicateExpectation(predicate: NSPredicate(format: "exists == false"), object: element)
         XCTAssertEqual(XCTWaiter.wait(for: [gone], timeout: 5), .completed)
     }
 
     private func searchLearn(in app: XCUIApplication, for query: String) {
         app.buttons["Search Learn"].tap()
         let field = app.textFields["Learn search field"]
         XCTAssertTrue(field.waitForExistence(timeout: 5), app.debugDescription)
diff --git a/Presentation/Features/Onboarding/Views/ProductPaywallPage.swift b/Presentation/Features/Onboarding/Views/ProductPaywallPage.swift
index 7273e6d..7762058 100644
--- a/Presentation/Features/Onboarding/Views/ProductPaywallPage.swift
+++ b/Presentation/Features/Onboarding/Views/ProductPaywallPage.swift
@@ -295,60 +295,67 @@ struct ProductPaywallPage: View {
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
                         .frame(width: 48, alignment: .center)
                 }
                 .padding(.bottom, 10)
 
                 Rectangle()
                     .fill(lavender)
                     .frame(height: 1)
                     .padding(.bottom, 14)
 
                 VStack(spacing: 12) {
                     paywallBenefitComparisonRow(icon: "character", title: "AI translations", freeValue: "Limited")
                     paywallBenefitComparisonRow(icon: "plus.circle.fill", title: "Add words manually", freeValue: "10")
                     paywallBenefitComparisonRow(icon: "camera", title: "Extract words from photos", freeValue: "Limited")
                     paywallBenefitComparisonRow(icon: "text.bubble", title: "Phrasal verbs", freeValue: nil)
                     paywallBenefitComparisonRow(icon: "speaker.wave.2.fill", title: "Pronunciation and IPA", freeValue: "Limited")
                 }
+
+                Text("10 active words across all languages")
+                    .font(.footnote.weight(.medium))
+                    .foregroundStyle(mutedInk)
+                    .frame(maxWidth: .infinity, alignment: .leading)
+                    .fixedSize(horizontal: false, vertical: true)
+                    .padding(.top, 10)
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
                         Image(systemName: "checkmark")
                             .font(.system(size: 17, weight: .semibold))
                             .foregroundStyle(.white)
                             .frame(width: 34, height: 34)
                             .background(purple)
                             .clipShape(Circle())
                             .accessibilityLabel("Included")
                     }
                 }
             }
             .frame(width: 58)
             .padding(.vertical, 12)
             .background(lavender)
