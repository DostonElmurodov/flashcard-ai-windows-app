# Task3B fix2 exact-source Mac evidence

Candidate22c36f59159ec9a32b6c600af7ae6bc1d94c293d, clean iOS checkout. Root tested-source/artifact SHA matched each run via collector, preserving all raw data and per-file hashes.

- Test-only68b0ddc7a48336b664734dc1485478395969f20e, restore-proof36584051326:59cases54passed5failed,7assertions0unexpected. Each new case failed intendedbehavior; prior54passed. Folder36584051326-restore-proof-fix2-red-68b0ddc,1601hashedfiles.
- Final22 full36585128732:677/677passed,0failed. Raw totals649unit+28UI;48AnonymousPurchaseRestore+11AppAttestSessionResume;58WordRepositoryV10Ownership;28UILaunchFixture. Every one of five new cases individuallyPASS. Folder36585128732-full-fix2-22c36f5,1425hashedfiles.
- Final22 unsignedRelease36585132762:SUCCESS/BUILD SUCCEEDED, configured Release fixture marker exclusions and bundle check. No testcases in buildscope. Folder36585132762-release-compile-fix2-22c36f5,5hashedfiles.

All folders are under D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/task3b-results. Raw logs/cases/XCResults/source-head files retained. Existing warnings including API await, unreachable offline branches, AppIntents metadata and Node action migration remain; not a warning-free claim. Neither simulator nor unsignedRelease proves real-device AppAttest/AppleSandbox/iOS17minimum or production configuration. Independent scoped review pending at time of this note; wholefeature review followsremainingtasks.
