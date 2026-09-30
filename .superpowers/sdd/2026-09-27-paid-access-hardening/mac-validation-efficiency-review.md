# Mac validation efficiency review

Date: 2026-09-29
Reviewer: clean-context GPT-6 Astra, Medium
Repository: D:\07 Hobby\FlashcardAI\test-results\paid-access-hardening\ios
Base HEAD: d3aa559aab9e092d01253e9f16004772379540bd
Scope: dirty .github/workflows/ios-validation.yml and README.md against HEAD. Product changes from the separate frozen 6B task excluded.

## Result

No actionable findings in the reviewed change.

## Evidence

- Existing manual scope options, selections, baseline checkout, and full-suite behavior remain present. The added verification scope selects no test subset, sets OWL_DEBUG_BUNDLE_ID=com.mavrylo.owlai.uitest, builds for testing, and invokes test-without-building once (workflow lines 116-118, 187-203). The unchanged shared scheme includes both unit and UI targets. The built bundle identity guard remains in place.
- After successful tests, verification calls the same extracted unsigned generic iOS device Release build and configuration/fixture-exclusion guards used by release-compile. Debug bundle overrides do not flow into the Release commands. Release output appends to xcode-build.log, retaining the earlier simulator build log (lines 140-203).
- The exact dependency cache key uses runner OS, actual uname architecture, selected xcodebuild version/build identity, and checked-in Package.resolved hash; there are no fallback restore keys (lines 32-45).
- All dependency-consuming xcodebuild commands use the explicit source-packages directory plus both locked-resolution flags. Resolution requires a nonempty lockfile, fails on pipeline failure, and checks the lockfile for changes before cache save. Cache save requires success and occurs before simulator selection and tests (lines 46-62, 133-150). Toolchain version queries need no package directory.
- The cache includes only the dedicated source-packages directory, saved before application builds. No DerivedData, test results, simulator state, credential paths, or signing material are added to the cache.
- Package.resolved pins all nine dependencies to revisions; versioned entries also specify versions. The direct project references pin GoogleSignIn exactly and swift-fsrs to a revision. The historical baseline checkout contains the same lockfile. The inspected lockfile and shared scheme have no diff against HEAD.
- The always-run artifact upload retains source-head.txt, existing logs and TestResults.xcresult, and adds swiftpm-resolve.log (lines 207-218). README describes the combined completion-boundary run and checking reuse during the next necessary run without claiming measured savings.

## Static validation and limits

- git diff --check passed for both scoped files.
- Bash syntax validation passed for all multiline shell blocks, including the extracted Release function and embedded Python heredocs. No commands from those blocks were executed.
- No source edits, remote calls, builds, tests, commits, or pushes were performed. This report is the only file written by the reviewer.
- No macOS runtime proof exists from this review: actual Xcode locked resolution, cache restore/save behavior, combined job duration, and test/build success remain unverified. Observe cache reuse on the next necessary verification run; an additional paid warm-cache benchmark is not requested or warranted by this static review.
