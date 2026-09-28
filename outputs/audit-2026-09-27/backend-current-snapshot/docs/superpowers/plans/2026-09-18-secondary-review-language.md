# Secondary Review Language Implementation Plan

> **For agentic workers:** Use superpowers:subagent-driven-development to implement the independent backend and client tasks, with final review and verification.

**Goal:** Bring CDLVocab's optional Settings → revealed Review secondary-language support to Owl AI, with user-approved AI translation and explanation cached for repeat display.

**Architecture:** Local optional preference, default off. Clients request a separate protected review translation on reveal; cached secondary content never creates a learning card or changes review progress. Server shares validated results by word/source/target language, preserving existing authentication and test-mode rules.

**Tech Stack:** SwiftUI/Swift, Electron/React/TypeScript, ASP.NET Core/C#.

**Spec:** User-approved behavior in this conversation and reference `/Users/dostonelmurodov/Projects/Personal/cdl-main/Presentation/Features/Home/Views/ReviewAnswerDetailsView.swift`; Owl permits runtime AI (CDL does not).

## Global Constraints
- Preserve native/learning languages, real subscriptions, server-authoritative test mode, FSRS scheduling and review counts.
- Optional secondary selection uses supported languages excluding native language; equal or invalid selection is cleared.
- Show compact flag/name, translation, explanation only after reveal. No secondary audio. No requests while disabled, hidden, or locked.
- Preserve current card, reveal state and progress when the preference changes; discard stale async responses.
- Cache success only, by source word/source language/target language and client workspace/API context. Failures remain retryable without blocking grading.
- No changes in the reference CDLVocab repositories.

## Task 1: Protected server translation
Files: `src/Mavrylo.Services/Dtos/Dtos.cs`, `Services/WordAiService.cs`, both Owl AI controllers, translation cache kind, relevant tests.
Contract: POST `/owlai/ai/review-translation` (device) or `/owlai/account/ai/review-translation` (account), JSON `{word,native_language,learning_language,secondary_language}`; response `{language_code,translation,explanation}`. Return canonical language code and short explanation in the selected language.
- [ ] Add red tests for validation, language-separated cache, provider failure/invalid response, authentication and false-mode commercial gates.
- [ ] Implement validated AI result using a separate translation-cache kind; no word/card writes. Device free access must validate existing original word slot instead of reserving a new secondary-language slot.
- [ ] Run full backend tests. Keep normal quotas and App Attest. Deploy after review.

## Task 2: iOS Settings and Review
Files: `Shared/Settings/AppSettingsStore.swift`, SettingsView, ReviewSessionView, APIClient/AI DTOs, focused secondary service/view, project file, tests.
- [ ] Test default off, persistence, valid canonicalization, selection cancellation, equality reset; test content caching and stale results.
- [ ] Add reference-style toggle/picker using local settings; cancellation must not commit selection.
- [ ] Add protected API call and local cache separate from words/review state. Render compact secondary surface after examples only when revealed.
- [ ] Verify settings and Review contracts, build, and inspect rendered UI. No second review direction/card.

## Task 3: Windows Settings and Review
Files: shared/types.ts, electron/store.ts/api.ts/main.ts/preload.ts, src/Settings.tsx/Review.tsx, focused helper/service and tests.
- [ ] Test preference normalization and scope-safe secondary cache with no word/schedule mutations.
- [ ] Implement optional settings picker and protected scoped IPC request, selecting source languages from card/deck.
- [ ] Render revealed secondary card; handle loading/failure/retry; cancel or ignore obsolete results.
- [ ] Run tests and build, inspect UI, review changes across all projects, then publish tested commits.
