# Mac validation efficiency

## Scope

Changed only `test-results/paid-access-hardening/ios/.github/workflows/ios-validation.yml`.

## Workflow behavior

- The existing manual scopes remain available. `full` keeps its existing dedicated `com.mavrylo.owlai.uitest` build-for-testing and full XCTest execution.
- New `verification` performs that same full unit and UI XCTest execution once, then runs the existing unsigned device `Release` build and its production configuration and fixture-marker checks in the same macOS job.
- Every `xcodebuild` invocation now uses the same explicit `-clonedSourcePackagesDirPath` under the runner temporary directory.
- SwiftPM resolution happens before simulator selection and tests. It requires a nonempty checked-in `Package.resolved`, uses only its pinned versions with automatic resolution disabled, and fails if resolution would modify that file. The dependency directory is restored/saved with a key made from runner OS, `uname -m`, the installed Xcode identity reported by `xcodebuild -version`, and the checked-in `FlashCardAI.xcodeproj/project.xcworkspace/xcshareddata/swiftpm/Package.resolved` hash.
- The cache contains only cloned SwiftPM source packages. It does not contain credentials, signing data, DerivedData, build output, test results, or simulator state. A miss is saved immediately after successful dependency resolution, so a later test failure does not prevent that resolved dependency cache from being stored.

## Local checks

1. Parsed the workflow as YAML with the locally available Node `js-yaml` parser and asserted the cache key includes the exact `Package.resolved` path, the `verification` choice exists, and every dependency-resolving `xcodebuild` path uses the explicit source-package directory and locked-version flags.
2. Extracted the shell blocks from the workflow and checked them with the locally installed Git Bash syntax validator.
3. Reviewed the resulting diff to confirm no triggers, credentials, signing settings, cache of build/test output, or product files changed.

## Limitation

Windows cannot prove macOS cache restoration, Xcode dependency resolution, or simulator/Release behavior. The first cold and subsequent warm `verification` runs on GitHub-hosted macOS are the required runtime evidence; this change makes no measured runtime or savings-percentage claim.
