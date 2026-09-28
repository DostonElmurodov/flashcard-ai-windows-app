# Owl AI — iOS UI Map (pages & elements)

> A shared vocabulary for talking about the app. Point at a **page** or an **element** by the
> name here, and Claude knows exactly which screen and code it maps to.
> Auto-generated from the SwiftUI source; each page lists its source file.


**32 pages / screens, 276 elements catalogued.**

## Contents

- **1. Navigation shell & Onboarding**
  - [Root Tab Shell](#root-tab-shell)
  - [Floating Tab Bar](#floating-tab-bar)
  - [App Launch Root](#app-launch-root)
  - [Onboarding — Welcome ("Learn only the words you need")](#onboarding--welcome-learn-only-the-words-you-need)
  - [Onboarding — Choose your languages](#onboarding--choose-your-languages)
  - [Onboarding — When should we nudge you? (Reminders)](#onboarding--when-should-we-nudge-you?-reminders)
  - [Onboarding — Learn without limits (Paywall)](#onboarding--learn-without-limits-paywall)
- **2. Home dashboard**
  - [Main Dashboard (post-onboarding landing screen)](#main-dashboard-post-onboarding-landing-screen)
  - [Dashboard (Home) screen](#dashboard-home-screen)
  - [Add New Word popup](#add-new-word-popup)
  - ["Delete this word?" confirmation dialog](#delete-this-word?-confirmation-dialog)
  - [Word Card (DashboardWordCard component)](#word-card-dashboardwordcard-component)
  - [History / All Words screen (WordHistoryView)](#history--all-words-screen-wordhistoryview)
  - [History word card (historyWordCard)](#history-word-card-historywordcard)
- **3. Home actions — review, edit, categories, paywall**
  - [Review](#review)
  - [Loading review queue](#loading-review-queue)
  - [Couldn't load review queue](#couldnt-load-review-queue)
  - [Nothing to review](#nothing-to-review)
  - [Good job](#good-job)
  - [Edit Word](#edit-word)
  - [Manage Categories](#manage-categories)
  - [Rename category](#rename-category)
  - [Add category](#add-category)
  - [Delete category confirmation](#delete-category-confirmation)
  - [Unlock Everything (Pro paywall)](#unlock-everything-pro-paywall)
- **4. Create set & Add word**
  - [Create flashcard set](#create-flashcard-set)
  - [Add New Word (full page)](#add-new-word-full-page)
  - [Add New Word (popup / sheet mode)](#add-new-word-popup--sheet-mode)
  - [Word Detail Result Panel](#word-detail-result-panel)
- **5. Library & Settings**
  - [Library](#library)
  - [Settings](#settings)
  - [New reminder](#new-reminder)


---

# 1. Navigation shell & Onboarding

## Root Tab Shell

- **File:** `App/Navigation/RootTabShell.swift`
- **Purpose:** Post-onboarding root container that hosts the floating tab bar and swaps between the Home, Library, Learn, and Pro tab contents, and pushes the create-set flow.
- **How you get here:** Rendered by App/FlashCardAIApp.swift inside a NavigationStack once AppRootModel.repository is ready and onboarding (ProductOnboardingView) has completed.

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **Background** | image | appSettings.currentBackground filling the screen (ignoresSafeArea). | — |
| **Home tab content** | card | Shown when selection == .home; renders MainDashboardView(repository:, openSettingsOnAppear:) (defined in a different file, not read here). | Displays the Home dashboard screen |
| **Library tab content** | card | Shown when selection == .library; renders LibraryView(repository:) (defined in a different file, not read here). | Displays the Library screen |
| **Learn tab content** | card | Shown when selection == .learn; renders DashboardView(repository:). | Displays the study dashboard with review stats, categories, and saved words |
| **Pro tab content** | card | Shown when selection == .pro; renders the shared ProductPaywallPage as a full page with the tab bar still visible (not a modal). It uses 96pt bottom clearance so the raised action area and final benefits rows remain clear of the floating tab bar. Close, successful purchase, and successful restore set selection back to .learn without changing either onboarding launch flag. | Displays the shared onboarding-style Pro/paywall screen; completion returns to Learn |
| **FloatingTabBar** | tab | Reusable floating tab bar with a 45%-opaque theme background overlaid at the bottom (padding: horizontal 14, bottom 2). It is removed from accessibility and hit testing while any Dashboard Add menu, target-set chooser, or child flow is active. See its own catalog entry for its buttons. | Switches `selection` between .learn/.library/.pro, or triggers showCreatePage via its + button |
| **CreateFlashcardSetPage navigation destination** | row | navigationDestination(isPresented: $showCreatePage) pushing CreateFlashcardSetPage(repository:) with theme/appSettings environment objects. | Pushes the full-page CreateFlashcardSetPage (defined elsewhere), triggered when FloatingTabBar's + button sets showCreatePage = true |

## Floating Tab Bar

- **File:** `Presentation/SharedUI/FloatingTabBar.swift`
- **Purpose:** Reusable bottom navigation with Learn, Library, Pro, and Create buttons. The surrounding capsule uses the current theme background at 45% opacity, with no shared blur, border, or shadow; cards remain visible beneath it and the selected button retains its highlight. The enum AddOption (also defined in this file) lists the 6 ways to add words used later on CreateFlashcardSetPage.
- **How you get here:** Pinned to the bottom of Root Tab Shell (RootTabShell.swift), visible over every tab (Home/Library/Learn/Pro).

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **Home tab button** | tab | Icon house.fill, label 'Home'. accessibilityLabel 'Home'. Highlighted with a soft rounded pill when selected. | Sets selection = .home in RootTabShell, showing MainDashboardView |
| **Library tab button** | tab | Icon books.vertical.fill, label 'Library'. accessibilityLabel 'Library'. | Sets selection = .library in RootTabShell, showing LibraryView |
| **Learn tab button** | tab | Icon graduationcap.fill, label 'Learn'. accessibilityLabel 'Learn'. | Sets selection = .learn in RootTabShell, showing DashboardView |
| **Pro tab button** | tab | Icon crown.fill, label 'Pro'. accessibilityLabel 'Pro'. | Sets selection = .pro in RootTabShell, showing ProductPaywallPage full page with Pro-only bottom clearance |
| **Create button (+ / "Create")** | button | Icon 'plus', label 'Create'. accessibilityLabel 'Create'. Calls the onAdd closure passed in from RootTabShell. | RootTabShell sets showCreatePage = true, pushing CreateFlashcardSetPage |
| **Selected-tab highlight** | row | Soft white rounded-rect (18pt radius) fill behind whichever tab/action is currently selected; pure display, animated with a spring. | — |

## App Launch Root

- **File:** `App/FlashCardAIApp.swift`
- **Purpose:** App entry point (@main WindowGroup) that decides which top-level screen to show based on AppRootModel bootstrap state: a database error, a loading spinner, onboarding, or the main tab shell.
- **How you get here:** This is the very first content the OS shows on launch, before any user navigation.

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **"Could not open local database." error text** | banner/error | Text("Could not open local database.\n(err)") shown full-screen, centered, when root.bootstrapError is non-nil. | Dead-end error state; no recovery action shown |
| **Loading spinner** | progress | A bare ProgressView() shown while root.repository is still nil (before bootstrap completes). | Automatically replaced once AppRootModel finishes bootstrapping (repository becomes non-nil) |
| **Onboarding gate** | row | When repo is ready and root.showProductOnboarding is true, presents ProductOnboardingView(onFinished:). | Shows the 4-page onboarding flow (see separate Onboarding page entries) |
| **Main app gate** | row | When repo is ready and onboarding is complete, presents NavigationStack { RootTabShell(repository:, openSettingsOnAppear:) }. | Shows Root Tab Shell (Home/Library/Learn/Pro/+) |

## Onboarding — Welcome ("Learn only the words you need")

- **File:** `Presentation/Features/Onboarding/Views/ProductOnboardingView.swift`
- **Purpose:** First-launch welcome screen introducing the app before language/reminder/paywall setup.
- **How you get here:** page 0 of ProductOnboardingView; shown first when ProductOnboardingView appears (from App Launch Root's onboarding gate).

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **Owl hero image** | image | Image("OnboardingOwlHero"), sized relative to screen (up to 66% width / 30% height). | — |
| **"Learn only the\nwords you need" headline** | label/text | Large heavy-weight title, ink-colored. | — |
| **Subtitle text** | label/text | "Add personal words, get translations, examples, pronunciation, transcription, and reminders that help you remember them." | — |
| **"Get Started" button** | button | primaryButton with arrow.right icon on a purple capsule. | Advances to page 1 (Onboarding — Choose your languages) and marks maxVisitedPage |
| **Page dots (index 0 active)** | row | 4-dot progress indicator; the active dot is wider/accented, reachable (already-visited) dots are tappable to jump back. | Tapping a reachable dot sets `page` to that index |

## Onboarding — Choose your languages

- **File:** `Presentation/Features/Onboarding/Views/ProductOnboardingView.swift`
- **Purpose:** Lets the user pick their native language and the language they're learning.
- **How you get here:** page 1 of ProductOnboardingView; reached via the Welcome page's "Get Started" button or by tapping page dot 2.

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **"LANGUAGE SETTINGS" header** | label/text | Globe icon + tracked label "LANGUAGE\nSETTINGS" inside a lavender header card, above the "Choose your languages" headline and "Pick your native language and the language you want to learn." subtext. | — |
| **"My language" card** | picker | labeledLanguageCard bound to appSettings.nativeLanguageCode; shows a flag icon, the language display name, and a localized native-language caption (e.g. "Native language"). Tapping opens a Menu of LanguageOption.appChoices. | Selecting a language in the menu sets appSettings.nativeLanguageCode |
| **Swap languages button** | button | Stacked arrow.right/arrow.left icons between the two language cards. accessibilityLabel "Swap languages". | Swaps appSettings.nativeLanguageCode and appSettings.learningLanguageCode |
| **"I am learning" card** | picker | labeledLanguageCard bound to appSettings.learningLanguageCode; same Menu-of-LanguageOption.appChoices behavior as the native card, with a localized "Learning"-style caption. | Selecting a language in the menu sets appSettings.learningLanguageCode |
| **"Next" button** | button | primaryButton, arrow.right icon. | Advances to page 2 (Onboarding — Reminders) and marks maxVisitedPage |
| **Page dots (index 1 active)** | row | Same 4-dot indicator as Welcome page, now showing page 2 as active. | Tapping a reachable dot sets `page` to that index |

## Onboarding — When should we nudge you? (Reminders)

- **File:** `Presentation/Features/Onboarding/Views/ProductOnboardingView.swift`
- **Purpose:** Lets the user set the daily time window for study reminder notifications and request notification permission.
- **How you get here:** page 2 of ProductOnboardingView; reached via the Language page's "Next" button or by tapping page dot 3.

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **"REMINDERS" header** | label/text | Bell icon + tracked "REMINDERS" label inside a lavender header card, above the "When should we nudge you?" headline and "We'll spread your flashcards gently between these times - never before, never after." subtext. | — |
| **FROM time picker** | picker | timePickerBlock labeled "FROM"; a Menu (clock icon + formatted time e.g. "8:00 AM" + chevron.down) listing times every 30 minutes from 5:00 AM to 11:00 PM. | Selecting an option sets reminderFromMinutes |
| **UNTIL time picker** | picker | Same control as FROM, labeled "UNTIL", bound to reminderUntilMinutes. | Selecting an option sets reminderUntilMinutes |
| **"Allow notifications to get daily words" label** | label/text | Heavy-weight prompt text above the notification preview. | — |
| **Notification preview card** | card | Mock push-notification row: owl icon tile, "Owl AI" sender label, a sample word line built from the chosen learning/native languages (e.g. "Apple - Manzana"), and a "now" timestamp. Pure display. | — |
| **"Allow and Save" button** | button | primaryButton; calls allowNotificationsAndContinue(), which saves the chosen reminder window, requests UNUserNotificationCenter authorization (.alert/.sound/.badge), sets appSettings.remindersEnabled, and applies StudyReminderScheduler. | Advances to page 3 (Onboarding — Paywall) and marks maxVisitedPage, after the OS notification-permission prompt is resolved |
| **Page dots (index 2 active)** | row | Same 4-dot indicator, now showing page 3 as active. | Tapping a reachable dot sets `page` to that index |

## Onboarding — Learn without limits (Paywall)

- **File:** `Presentation/Features/Onboarding/Views/ProductPaywallPage.swift` (hosted by `ProductOnboardingView.swift` during onboarding)
- **Purpose:** Shared onboarding/Pro subscription page: presents the yearly/monthly offer with a free-vs-Pro benefits comparison. It owns plan selection and purchase-error visual state while its host supplies loading, purchase, restore, close, and successful-completion behavior.
- **How you get here:** page 3 of ProductOnboardingView, reached via the Reminders page's "Allow and Save" button or by tapping page dot 4; RootTabShell also renders the same page for the Pro tab. Onboarding uses 28pt bottom clearance and delegates close/purchase/restore success to `finishOnboarding()`; Pro uses 96pt clearance and returns to Learn without onboarding mutation.

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **Close button (xmark)** | button | Circular glass button in the top bar. accessibilityLabel "Close purchase page". Calls the host's onClose callback. | During onboarding, calls the parent's finishOnboarding() to mark initial settings complete and finish once; from Pro, returns to Learn without changing either launch flag |
| **"PRO" badge** | label/text | Small gold-on-dark tracked label in the top bar, pure display. | — |
| **"UNLOCK EVERYTHING" label** | label/text | Tracked eyebrow label above the headline. | — |
| **"Learn without limits." headline** | label/text | Large semibold title. | — |
| **YEARLY plan card** | card | Selectable plan option showing "YEARLY", billed price $59.88 (was $83.88, struck through), $4.99/month, "7-day free trial" footnote, and "SPECIAL OFFER" / "DISCOUNT 29%" badges. accessibilityLabel "Select yearly plan". Shows a checkmark overlay when selected (default selection). | Sets ProductPaywallPage.selectedPlan = .yearly |
| **MONTHLY plan card** | card | Selectable plan option showing "MONTHLY" and $6.99/month, no trial footnote or offer badges. accessibilityLabel "Select monthly plan". | Sets ProductPaywallPage.selectedPlan = .monthly |
| **Benefits comparison card** | card | FREE-vs-PRO row list: "AI translation words" (25 / ∞), "Add words manually" (100 / ∞), "Read words from photos" (∞ / ∞), "Phrasal verbs" (blocked-icon / ∞), "Pronunciation & IPA" (25 / ∞). Pure display. | — |
| **"Start 7-day free trial" button** | button | Large purple CTA with arrow.right icon; disabled while the supplied actions are busy. Calls startTrialOrPurchase(). | Purchases the selected plan through ProductPaywallActions; onboarding success calls the parent's finishOnboarding(), while Pro success returns to Learn without changing launch flags; failure sets purchaseErrorText |
| **"Then $4.99/month, billed yearly. Cancel anytime." caption** | label/text | Fine-print under the CTA button. | — |
| **"Restore" button** | button | Underlined text button. Calls restorePurchases(). | Restores through ProductPaywallActions; onboarding success calls the parent's finishOnboarding(), while Pro success returns to Learn without changing launch flags; failure sets purchaseErrorText to "No active purchase was found." or the service's error |
| **"Terms" button** | button | Underlined text button. Opens /owlai/legal/terms resolved against APIConfiguration.baseURL. | Opens the Terms of Service URL via openURL (external browser/Safari view) |
| **"Privacy" button** | button | Underlined text button. Opens /owlai/legal/privacy resolved against APIConfiguration.baseURL. | Opens the Privacy Policy URL via openURL (external browser/Safari view) |
| **Purchase error text** | banner/error | Red text shown below the legal links when purchaseErrorText is set (failed purchase or restore). | — |


---

# 2. Home dashboard

## Main Dashboard — 🟢 NEW / ACTIVE (`MainDashboardView`)

> **This is the dashboard the app actually shows today.** Struct name: `MainDashboardView`.

- **File:** `Presentation/Features/Home/Views/MainDashboardView.swift`
- **Purpose:** Home landing screen with search and a grid of the user's non-empty flashcard sets.
- **How you get here:** The Home tab renders `MainDashboardView` when `selection == .home`.

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **Owl mark image** | image | Circular avatar image ("OnboardingOwlHero") at the left of the top bar. | — |
| **Search words or categories field** | text field | TextField with placeholder "Search words or categories", bound to a local searchText state; has a magnifyingglass icon and highlights its border capsule when focused. | — |
| **Clear search button** | button | xmark.circle.fill icon button (accessibilityLabel "Clear search"); appears only once search text is non-empty. | Clears searchText back to empty. |
| **Settings button** | button | gearshape.fill icon button (accessibilityLabel "Settings") at the right of the top bar. | Presents SettingsView as a large sheet (passing repository and isInitialSetup). Also auto-presented on first appearance if the openSettingsOnAppear binding is true (first-run setup flow). |
| **Flashcard set grid** | grid | Shows non-empty user-created `cat-u-*` sets as two-column SetGridTile items. | Each tile exposes a top-right ⋯ menu. |
| **Activate / Activated menu action** | menu action | Persists the set's active state; once active, the action becomes disabled and reads Activated. | Adds the set as a swipeable filter in Learn and History. |
| **Edit menu action** | menu action | Opens CreateFlashcardSetPage in edit mode. | Renames or edits the set and its cards. |
| **Delete menu action** | destructive action | Confirms, then removes the set and its cards. | Returns to the refreshed Home grid. |

## Dashboard — Learn (`DashboardView`)

> **The full-featured study dashboard shown from the Learn tab.**

- **File:** `Presentation/Features/Home/Views/DashboardView.swift`
- **Purpose:** The fuller study screen: search, due-review banner, TOTAL/DUE/STUDIED/STREAK stats, active flashcard-set chips, the scrollable list of saved word cards, and an expandable Add action.
- **How you get here:** The **Learn** tab renders `DashboardView`; Home continues to render `MainDashboardView`.

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **Owl mark image** | image | Same circular avatar as MainDashboardView's top bar. | — |
| **Search Learn button / field** | button + text field | Collapsed by default beside Settings. `Search Learn` reveals and focuses the existing word/set field; Settings remains visible. `Close Learn search` clears and collapses it. | Filters viewModel.visibleRows to word/set-name matches; while a nonempty query is active, the review banner, stats row, and set chips are hidden and the header reads "Results". |
| **Settings button** | button | gearshape.fill icon (accessibilityLabel "Settings"). | Presents SettingsView as a large sheet; its onCategoriesChanged callback reloads the dashboard. |
| **"BUILD YOUR WORD LIST" empty review banner** | card | Shown when there are zero words: eyebrow "BUILD YOUR WORD LIST", headline "Learn only the words you need", body "I'll translate them, make flashcards, and remind you several times so they stay in memory." Non-interactive. | — |
| **"DUE REVIEW" active review banner** | card | Shown once words exist: eyebrow "DUE REVIEW", large due count + "words due", "~N min · 0-day streak" subtitle, and a white pill reading "Start review" (or "Repeat" when 0 due) with an arrow.right icon. | Presents ReviewSessionView for the selected flashcard set, or all words when All is selected. |
| **TOTAL stat** | button | NavigationLink showing viewModel.totalWords with label "TOTAL". | Pushes WordHistoryView(listSort: .alphabetical), titled "All Flashcards". |
| **DUE stat** | button | Button showing viewModel.reviewToday with label "DUE"; disabled and dimmed when totalWords == 0. | Opens the same full-screen Review Session as the banner. |
| **STUDIED stat** | label/text | Displays `viewModel.studied` with label "STUDIED". | — |
| **STREAK stat** | label/text | Hardcoded "0d" value with label "STREAK" (not yet wired to real streak data). | — |
| **"Flashcards" / "Results" section title** | label/text | Header text that reads "Results" while searching, otherwise "Flashcards". | — |
| **History button** | button | NavigationLink with clock.fill icon and "History" text, right-aligned in the list header. | Opens History with All plus the same activated flashcard-set filters used by Learn. |
| **Flashcard-set chips row** | segmented control | Horizontal scroll containing All plus user/Library sets activated from their ⋯ menus. Seeded categories such as Daily Life and Work are excluded. | Tapping a set filters stats, review, and visible words to it; All keeps the previous unfiltered all-words behavior. |
| **Load error banner** | banner/error | Red-tinted text showing viewModel.loadError when a load fails. | — |
| **Empty-state text** | label/text | "No matches for “<searchText>”" while searching with no results, or "No words yet. Tap Add to add your first one." when the list is empty and not searching. | — |
| **Word list** | list | LazyVStack of DashboardWordCard (style: .homeList) for every row in viewModel.visibleRows, with pronunciation playback, tap-to-expand, and per-card Edit/Delete. | See DashboardWordCard entry for its own controls (Edit opens EditWordSheet; Delete opens the delete confirmation dialog). |
| **Review word button** | button | An icon-only play button beneath the part of speech at the top right of each Learn card, including search results. Its accessibility label includes Review and the word. Disabled for locked words. | Hides the search keyboard and opens only that word in Review using its saved language pair. Previously studied words can be reviewed early; new words retain the daily limit. Grading completes the one-word session, and Start again repeats it. |
| **Add button** | expandable action | Compact right-aligned capsule with plus icon and "Add" text. It keeps the first-time pulse/glow while the total word count is 5 or fewer and the nudge is incomplete. | Expands the Dashboard Add speed dial. |
| **Dashboard Add speed dial** | action menu | Dims the Dashboard and shows, top to bottom: Add with AI, Add manually, Scan document, Select image, and Paste text. Add with AI uses the primary accent treatment. Actions scroll within their available-height region only when needed; Add remains fixed. | Opens the existing AI popup, manual-entry view, or local import flow with a captured real Flashcard Set. Tapping the backdrop or × Add closes the menu. |
| **Choose a Flashcard Set sheet** | sheet | Appears after any Dashboard Add action when **All** is selected. Lists only active user/Library Flashcard Sets; there is no automatic, hidden, uncategorized, seeded-category, or **All** fallback. | Choosing a set opens the requested add flow with that nonoptional set ID. Cancel persists nothing and returns to Dashboard. |
| **Theme chooser sheet** | sheet | Sheet bound to showThemeChooser presenting ChooseThemeSheet at .large detent. No control inside this file's visible UI sets showThemeChooser = true — the state and .sheet exist but their trigger isn't part of DashboardView's own body (possibly wired elsewhere, or currently unused). | Presents ChooseThemeSheet. |
| **Edit word sheet** | sheet | Item-sheet bound to rowToEdit presenting EditWordSheet for the tapped word. | Opened when a DashboardWordCard's "Edit" button sets rowToEdit; onSaved reloads the dashboard. |
| **Delete-word confirmation dialog** | sheet | confirmationDialog titled "Delete this word?" triggered when rowPendingDelete is set (from a card's "Delete" button). | Cataloged as its own entry below. |

## Add New Word popup

- **File:** `Presentation/Features/Home/Views/DashboardView.swift`
- **Purpose:** A bottom-sheet-style overlay (not a system .sheet) that docks flush above the keyboard (or the home indicator) and hosts the word-add form so the user never leaves the Dashboard.
- **How you get here:** Opened by selecting **Add with AI** from the Dashboard Add speed dial; also auto-dismisses and reopens the pulse animation when closed.

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **Scrim backdrop** | card | A black 34%-opacity overlay plus a theme-tinted (homeDeep) 78%-opacity overlay behind the sheet, pushing the Dashboard visually into the background. Tapping it intentionally does nothing. | — |
| **Embedded word-add form (WordSearchView)** | sheet | WordSearchView rendered inline as the popup's content, sized above the keyboard/safe area, using the captured real Flashcard Set selected before presentation. | onSaved reloads the dashboard and closes the popup; onClose closes it without saving. |
| **Swipe-down-to-dismiss gesture** | row | Dragging the sheet down more than ~130pt (or a fast downward flick past 260pt predicted translation) dismisses it; otherwise it springs back to rest. | Closes the popup (equivalent to onClose) and resumes the add-word nudge pulse if still applicable. |

## "Delete this word?" confirmation dialog

- **File:** `Presentation/Features/Home/Views/DashboardView.swift`
- **Purpose:** Destructive-action confirmation before permanently removing a word.
- **How you get here:** Triggered when a DashboardWordCard's "Delete" button (in its expanded section) calls onDelete(row), setting rowPendingDelete.

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **"Delete this word?" title / "This cannot be undone." message** | label/text | Dialog title and message text. | — |
| **Delete button** | button | Destructive-role button. | Calls repository.deleteWord(id:), clears expansion state if that card was expanded, and reloads the dashboard. |
| **Cancel button** | button | Cancel-role button. | Dismisses the dialog with no changes. |

## Word Card (DashboardWordCard component)

- **File:** `Presentation/Features/Home/Components/DashboardWordCard.swift`
- **Purpose:** Reusable flashcard-style card for a single saved word; supports a "standard" grid-card layout and a "homeList" row layout, each expandable to show translations/examples/notes plus Edit and Delete actions.
- **How you get here:** Rendered inside DashboardView's word list (style: .homeList) for each row in viewModel.visibleRows. The "standard" style is defined here but not instantiated by any of the 4 files read — it is presumably used by another screen elsewhere in the app.

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **"LOCKED" badge** | label/text | lock.fill icon + "LOCKED" text; shown when row.isLocked (an expired_trial/revoked word past the free limit). Card content is dimmed to 55% opacity and expansion/tap is disabled. | — |
| **Part-of-speech pill** | label/text | row.partOfSpeech text; small accent-colored capsule in the standard layout, upper-cased tracked label at the top-right in the homeList layout. | — |
| **Word title** | label/text | row.displayWord shown as the card's large heading. | — |
| **Native translation text + flag icon** | label/text | Up to the first 3 of row.displayTranslations joined with commas, preceded by a LanguageFlagIcon for row.nativeLanguageBcp47. | — |
| **Pronunciation chip** | button | Shows the IPA-formatted pronunciation text next to a speaker.wave.2.fill icon (replaced by a small ProgressView spinner while audio is playing). | Calls pronunciation.speak(text: row.displayWord, learningLanguageBCP47:) to play TTS pronunciation of the word. |
| **Category tag** | label/text | row.categoryLabel with a keyword-matched icon (briefcase.fill for "work", house.fill for "daily"/"life", fork.knife for "food", airplane for "travel", archivebox.fill for "archive", else folder.fill). | — |
| **Due status badge** | label/text | "NEW", "DUE NOW", "DUE <1M", "DUE Xm/Xh/Xd", or "TOMORROW" depending on row.nextReviewAt; color-coded (accent for new, muted for future, orange/peach for overdue). Replaced by the LOCKED badge when the row is locked. | — |
| **Card body (tap to expand)** | row | The whole card is tappable when not locked and an expansion binding is supplied. | Toggles the card's expanded section open/closed (expansion binding set to/cleared from row.id). |
| **Expanded: TRANSLATIONS detail** | label/text | Any translations beyond the first 3, shown under a "TRANSLATIONS" label, only when non-empty. | — |
| **Expanded: EXAMPLES section** | label/text | "EXAMPLES" label plus a repeated pronunciation chip; shows the italicized first example sentence (row.exampleFirst) or the placeholder "No examples stored for this word." | — |
| **Expanded: MY NOTES** | label/text | row.userNotes (trimmed), shown in a tinted rounded box under a "MY NOTES" label, only if non-empty. | — |
| **Edit button** | button | pencil icon + "Edit" text, shown in the expanded section when an onEdit callback is supplied. | Calls onEdit(row) — in DashboardView this sets rowToEdit, opening EditWordSheet. |
| **Delete button** | button | trash icon + "Delete" text, shown in the expanded section when an onDelete callback is supplied. | Calls onDelete(row) — in DashboardView this sets rowPendingDelete, opening the "Delete this word?" confirmation dialog. |

## History / All Words screen (WordHistoryView)

- **File:** `Presentation/Features/Home/Views/WordHistoryView.swift`
- **Purpose:** Full word list pulled from SQLite, shown either as "History" (newest-added-first) or "All Words" (A–Z), with mastery stats, learning insights, and per-word detail cards.
- **How you get here:** Pushed from DashboardView: tapping the TOTAL stat opens it with listSort .alphabetical (visible title "All Words"); tapping the "History" button in the list header opens it with listSort .dateAddedNewestFirst (visible title "History"). Same screen/struct, title and default framing differ by the listSort parameter passed in.

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **Back button** | button | chevron.left icon in a circle (accessibilityLabel "Back"), top-left, overlaying the centered title. | Calls dismiss(), popping back to the Dashboard. |
| **Screen title** | label/text | Centered header text: "History" when listSort is .dateAddedNewestFirst, "All Words" when .alphabetical. | — |
| **Category chips row** | segmented control | Horizontal scroll of chips (icon, tab.title, count badge) built from categoryTabs, shown only when categories exist. | Tapping a chip sets selectedCategoryId (animated), filtering selectedRows and toggling which sections/cards are shown. |
| **Mastery summary card** | card | Shown when selectedRows is non-empty: a circular mastery-percent ring, "MASTERY" label, "You're doing great" text, "N reviews across M words. Keep going to fully master them." text, and AGAIN/GOOD/EASY total-count boxes. | — |
| **"LEARNING INSIGHTS" / "STUDY SNAPSHOT" labels** | label/text | Section headers, shown only when selectedCategoryId == "all" and there are rows with no error. | — |
| **Study snapshot grid** | card | 2x2 grid of metric cards: "New" (sparkles icon), "Due Now" (clock.badge.exclamationmark icon), "Learning/Review" (book.pages.fill icon), "Mature" (checkmark.seal.fill icon), each with a count. | — |
| **Progress mix card** | card | "PROGRESS MIX" label, total word count, a segmented horizontal bar (New/Learning/Mature/Paused proportional widths), and a legend row with counts per segment. | — |
| **Next Focus card** | button | Icon + "NEXT FOCUS" label + a title/subtitle that varies by state: "Review due words" (if any due), else "Learn new words" (if any new), else "Keep the streak alive"; dimmed and disabled if there is no candidate row. | Sets showFocusedReviewSession = true, presenting a full-screen ReviewSessionView(repository:, focusedRow: nextFocusRow) for that single word. |
| **Error banner** | banner/error | Red-tinted text showing errorText when loading fails. | — |
| **Empty-category view** | card | Shown when selectedRows is empty with no error: a tray or books.vertical icon, title ("This category is empty" or "No words yet"), and descriptive subtitle text. | — |
| **Per-word history cards list** | list | ForEach(selectedRows) rendering the historyWordCard component, shown only when a specific category (not "all") is selected. | See "History word card" entry below. |
| **Focused review full-screen cover** | sheet | fullScreenCover bound to showFocusedReviewSession, presenting ReviewSessionView focused on nextFocusRow. | Triggered by the Next Focus card; on dismiss the word list reloads (load()). |

## History word card (historyWordCard)

- **File:** `Presentation/Features/Home/Views/WordHistoryView.swift`
- **Purpose:** Detailed per-word card with review stats and a mastery bar, distinct from (and more detailed than) DashboardWordCard.
- **How you get here:** Rendered by WordHistoryView's ForEach(selectedRows) when a specific category (selectedCategoryId != "all") is selected.

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **"LOCKED — RESUBSCRIBE TO USE" badge** | label/text | lock.fill icon + text, shown when row.isLocked; whole card dimmed to 62% opacity. | — |
| **Part-of-speech pill** | label/text | row.partOfSpeech (lowercased) in a rounded capsule. | — |
| **Word title** | label/text | row.displayWord as the card heading. | — |
| **Translation line** | label/text | LanguageFlagIcon for row.nativeLanguageBcp47 plus row.displayTranslations joined with commas. | — |
| **Pronunciation pill** | button | IPA-formatted pronunciation text (italic) + speaker.wave.2.fill icon, shown when row.pronunciation is present. | Calls pronunciation.speak(text: row.displayWord, learningLanguageBCP47:) to play TTS pronunciation. |
| **AGAIN / GOOD / EASY count boxes** | card | Three tinted boxes showing row.againCount, row.goodCount, row.easyCount. | — |
| **History detail grid** | card | 2-column grid of 6 items: SHOWN ("Nx"), LAST REVIEWED (relative date), STATUS (row.reviewStatus capitalized), NEXT REVIEW (relative date), CATEGORY (row.categoryLabel or "All"), MASTERY (percent). | — |
| **Mastery bar** | progress | "MASTERY" label + percent text above a capsule progress bar filled to the computed mastery percentage. | — |


---

# 3. Home actions — review, edit, scan, categories, paywall

## Review

- **File:** `Presentation/Features/Home/Views/ReviewSessionView.swift`
- **Purpose:** Core flashcard review loop: shows the front of a word, lets the user reveal the translation, then rate their recall (Again/Good/Easy) using spaced repetition.
- **How you get here:** Presented over Home when a review session starts — ReviewSessionView(repository:categoryId:) for a whole/queued category, or ReviewSessionView(repository:focusedRow:) to jump straight to one word. Shown once the queue has finished loading and there is at least one card and the session isn't complete.
- **Scrolling:** The card can scroll behind the fixed header, queue counts, and review controls without a rectangular cut-off. The top and bottom control groups have rounded theme-colored backdrops at 45% opacity. Buttons keep their own backgrounds and remain in front of the card.

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **xmark close button** | icon | Circular header button (front state) with an xmark glyph. | Calls dismiss(), closing the review session and returning to the caller. |
| **"Review" title** | label/text | Bold header text shown above the progress bar in the front (unrevealed) state. | — |
| **progress capsule bar** | progress | Two-layer Capsule showing viewModel.progressFraction as fill width. | — |
| **"X / Y" progress label** | label/text | compactProgressLabel — current card ordinal over initial total, e.g. "3 / 12". | — |
| **"Do you remember this word?" prompt** | label/text | Front-state heading above the flashcard. | — |
| **language flag icon** | icon | LanguageFlagIcon showing the flag for whichever language (learning or native) is on the front, depending on review direction. | — |
| **part-of-speech tag** | label/text | Uppercased part of speech (e.g. "NOUN") shown top-right of the front card when the learning word is on front. | — |
| **front word text** | label/text | row.displayWord in large heavy type — the term the user must recall. | — |
| **IPA pronunciation text** | label/text | Italic phonetic spelling next to the front word (learning-side front only). | — |
| **speaker button (speaker.wave.2.fill icon)** | button | Circular button that plays the word's pronunciation via PronunciationSpeaker; shows a spinner while speaking. | Plays audio in place; no navigation. |
| **translation front block** | label/text | Shows the translation(s) as the front prompt instead of the learning word, when the reversed review direction is active. | — |
| **example sentence box** | card | Quoted example sentence for the word, shown below the front word if one exists. | — |
| **"Easy" button (unrevealed)** | button | Checkmark-icon button letting the user mark the card Easy without flipping it. | Calls viewModel.rate(.easy); advances immediately to the next card. |
| **"Tap to reveal translation" button** | button | Full-width dark button with a rectangle.on.rectangle icon. | Sets viewModel.showAnswer = true, switching the screen into the revealed (answer) state. |
| **chevron.left back button (revealed)** | icon | Circular header button shown once the answer is revealed. | Calls dismiss(), closing the review session. |
| **"REVIEW" label + "X of Y" progress text** | label/text | Centered small-caps label and progress readout in the revealed-state header. | — |
| **arrow.left.arrow.right swap button** | icon | Circular header button in the revealed state. | Calls viewModel.swapReviewSide(), flipping which language shows as prompt vs. answer for this session. |
| **answer word text** | label/text | The learning word shown in accent color at the top of the revealed card. | — |
| **answer pronunciation + speaker button** | button | IPA text plus a compact speaker button next to the answer word. | Speaker button plays pronunciation audio. |
| **part-of-speech text (lowercased)** | label/text | Shown under the answer word if the word has a part of speech. | — |
| **verb form chips (V1/V2/V3)** | card | Small chips showing other English irregular verb forms; only shown when appSettings.showVerbForms is on and the learning language is English. | — |
| **divider** | label/text | Horizontal rule separating the word header from the translation block. | — |
| **translation answer block** | label/text | The full translation(s) of the word, centered and prominent. | — |
| **"EXAMPLE" box** | card | Labeled box with the example sentence and its translation. | — |
| **"MY NOTES" box** | card | Labeled box showing the user's saved notes for this word, only if non-empty. | — |
| **"Again" button** | button | Pink rating button with an arrow.counterclockwise icon. | Calls viewModel.rate(.again) — schedules the card as forgotten and advances to the next card. |
| **"Good" button** | button | Amber rating button with a minus icon. | Calls viewModel.rate(.good) and advances to the next card. |
| **"Easy" button (revealed)** | button | Green rating button with a checkmark icon. | Calls viewModel.rate(.easy) and advances to the next card. |

## Loading review queue

- **File:** `Presentation/Features/Home/Views/ReviewSessionView.swift`
- **Purpose:** Transient loading state shown while the review queue is being built.
- **How you get here:** Shown automatically on first appearance of ReviewSessionView, before viewModel.finishedInitialLoad becomes true.

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **spinner** | progress | Centered ProgressView, scaled up, tinted with the theme primary color. | — |

## Couldn't load review queue

- **File:** `Presentation/Features/Home/Views/ReviewSessionView.swift`
- **Purpose:** Error state shown if loading the review queue fails.
- **How you get here:** Shown automatically in place of the review screen when viewModel.loadError is set after loading finishes.

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **"Couldn't load review queue" title** | label/text | Bold error heading. | — |
| **error detail text** | banner/error | The underlying error message (viewModel.loadError). | — |
| **"Close" button** | button | Prominent bordered button. | Calls dismiss(), closing the review session. |

## Nothing to review

- **File:** `Presentation/Features/Home/Views/ReviewSessionView.swift`
- **Purpose:** Empty-queue state shown when there are no cards due and the session isn't already complete.
- **How you get here:** Shown after a successful load when there is no current card and no cards have been completed in this session.

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **"Nothing to review" title** | label/text | Bold heading. | — |
| **"Your active flashcard sets are caught up." text** | label/text | Explanatory subtext. | — |
| **"Start again" button** | button | Replays previously introduced cards from active sets, including cards not yet due. Locked cards and unseen cards are excluded. | Restarts review in place without resetting saved learning history. |
| **"Add words" button** | button | Opens the existing Add menu: Add with AI, Add words manually, Scan document, Select image, and Paste text. | Closes review, then expands the dashboard Add menu. |
| **"Go to main page" button** | button | Returns to the learning dashboard. | Closes review. |

## Good job

- **File:** `Presentation/Features/Home/Views/ReviewSessionView.swift`
- **Purpose:** Completion screen shown after finishing the review session.
- **How you get here:** Shown when there is no current card and completedCount is greater than zero.

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **checkmark.circle.fill icon** | icon | Large success icon at the top. | — |
| **"Great work!" title** | label/text | Bold completion heading. | — |
| **completion message text** | label/text | Shows "You completed N cards." | — |
| **"Start again" button** | button | Replays the same session cards from the beginning using their latest saved state. Each card is shown once per replay; ratings continue to save normally. | Restarts review in place. |
| **"Add words" button** | button | Opens the existing Add menu: Add with AI, Add words manually, Scan document, Select image, and Paste text. | Closes review, then expands the dashboard Add menu. |
| **"Go to main page" button** | button | Returns to the learning dashboard. | Closes review. |

## Edit Word

- **File:** `Presentation/Features/Home/Views/EditWordSheet.swift`
- **Purpose:** Edit the fields of a single saved word card and reassign its category.
- **How you get here:** Presented as a modal sheet (NavigationStack) from an edit action on a saved word row elsewhere in the app (e.g. Library/Home dashboard); the presenting view supplies the WordRepository and the WordDashboardRow being edited. Not itself invoked by any code in this file.

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **WORD field** | text field | Labeled "WORD"; editable primary term, prefilled from row.displayWord. Required — Save is disabled while empty. | — |
| **TRANSLATION field** | text field | Labeled "TRANSLATION"; editable translation text, prefilled from row.translation. | — |
| **PRONUNCIATION field** | text field | Labeled "PRONUNCIATION"; editable IPA/pronunciation text, prefilled from row.pronunciation. | — |
| **MY NOTES field** | text editor | Labeled "MY NOTES"; multi-line free-form notes, prefilled from row.userNotes. | — |
| **CATEGORY label** | label/text | Section heading above the category chips. | — |
| **category chips row** | row | FlowCategoryChips — a wrapping grid of capsule chips, one per category (icon + name), fed by repository.fetchCategoryPickerRows(). Prefilled selection is row.categoryId. | Tapping a chip sets it as the selected category (selectedCategoryId); tapping the already-selected chip clears the category (uncategorized). |
| **error text** | banner/error | Shown below the chips if saving throws an error. | — |
| **"Save Changes" button** | button | Full-width filled button with a square.and.arrow.down.fill icon; shows a spinner while saving. Disabled while saving or when WORD is empty. | Calls repository.updateWordCard(...) with the edited fields, then onSaved() and dismiss() on success; sets error text on failure. |
| **xmark close button (toolbar)** | icon | Circular cancellation button in the nav bar. | Calls dismiss(), discarding any edits. |

## Manage Categories

- **File:** `Presentation/Features/Home/Views/ManageCategoriesView.swift`
- **Purpose:** Reorder, rename, add, and delete word categories (the built-in Archive category cannot be deleted).
- **How you get here:** Presented as a modal sheet (NavigationStack), likely from a "Manage Categories" entry point in Library/Home (the calling view is not among the files read).

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **error text** | banner/error | Shown at the top of the list if loading, renaming, adding, deleting, or reordering fails. | — |
| **category row** | row | One per category: a themed icon (ManageCategoryIconView — SF Symbol or colored monogram), the category name, an edit (pencil) button, and, if row.canDelete, a delete (trash) button. Rows are draggable to reorder (list is forced into active edit mode). | Dragging reorders and calls repository.setCategorySortOrder(...); the pencil button opens the Rename alert; the trash button opens the Delete confirmation. |
| **"Add Category" button** | button | Full-width dashed-border button with a plus icon, below the list. | Opens the Add category alert. |
| **xmark.circle.fill close button (toolbar)** | icon | Top-right close button, accessibilityLabel "Close". | Calls dismiss(). |

## Rename category

- **File:** `Presentation/Features/Home/Views/ManageCategoriesView.swift`
- **Purpose:** Alert for renaming an existing category.
- **How you get here:** Opened by tapping the pencil (edit) button on a category row in Manage Categories; prefilled with the category's current name.

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **"Rename category" title** | label/text | Alert title. | — |
| **Name field** | text field | Editable text field bound to renameDraft, prefilled with the category's current name. | — |
| **"Save" button** | button | Alert action. | Calls repository.renameCategory(id:name:) with the trimmed name (shows an error if empty), then reloads the list and calls onCategoriesChanged(). |
| **"Cancel" button** | button | Cancel-role alert action. | Dismisses the alert without changes. |

## Add category

- **File:** `Presentation/Features/Home/Views/ManageCategoriesView.swift`
- **Purpose:** Alert for creating a new category.
- **How you get here:** Opened by tapping the "Add Category" button in Manage Categories.

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **"Add category" title** | label/text | Alert title. | — |
| **"Enter a name for the new category." message** | label/text | Alert message text. | — |
| **Name field** | text field | Empty editable text field bound to addDraft. | — |
| **"Add" button** | button | Alert action. | Calls repository.addCategory(displayName:) with the trimmed name (shows an error if empty), then reloads the list and calls onCategoriesChanged(). |
| **"Cancel" button** | button | Cancel-role alert action. | Dismisses the alert without creating anything. |

## Delete category confirmation

- **File:** `Presentation/Features/Home/Views/ManageCategoriesView.swift`
- **Purpose:** Confirmation dialog before permanently deleting a category.
- **How you get here:** Opened by tapping the trash (delete) button on a deletable category row (row.canDelete) in Manage Categories.

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **"Delete \"{name}\"?" title** | label/text | Dynamic dialog title naming the category to be deleted. | — |
| **"Words in this category will become uncategorized." message** | label/text | Explanatory message. | — |
| **"Delete" button** | button | Destructive-role dialog action. | Calls repository.deleteCategory(id:), reloads the list, and calls onCategoriesChanged(). |
| **"Cancel" button** | button | Cancel-role dialog action. | Dismisses the dialog without deleting. |

## Unlock Everything (Pro paywall)

- **File:** `Presentation/Features/Home/Views/PaywallView.swift`
- **Purpose:** Standalone gated-flow paywall offering the Pro subscription (yearly/monthly with a 7-day trial). It is not the Pro tab; RootTabShell uses the shared ProductPaywallPage instead.
- **How you get here:** Presented full-screen (fullScreenCover) when a blocked user (free plan over the 10-word limit, expired trial/paid, or revoked) attempts a gated AI, manual-entry, or local-import action via PaywallView(onPurchased:).

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **xmark close button** | icon | Circular button top-left, accessibilityLabel "Close paywall". | Calls close() — invokes onClose() if supplied by a gated flow, otherwise dismisses the sheet/cover. |
| **"PRO" badge** | label/text | Dark pill badge with gold "PRO" text, top-right of the header. | — |
| **"UNLOCK EVERYTHING" label** | label/text | Wide-tracking eyebrow label above the headline. | — |
| **"Learn without limits." headline** | label/text | Large headline text. | — |
| **"Unlimited AI translations, unlimited words, photo scanning, and daily reminders." text** | label/text | Subheadline describing Pro benefits. | — |
| **YEARLY plan card** | card | Selectable card showing "YEARLY", the yearly price per year, and a "7-day free trial" footnote; shows a checkmark badge when selected. | Tapping sets selectedPlan = .yearly. |
| **MONTHLY plan card** | card | Selectable card showing "MONTHLY" and the monthly price per month; shows a checkmark badge when selected. | Tapping sets selectedPlan = .monthly. |
| **"Start 7-day free trial" button** | button | Large full-width dark button with an arrow.right icon; shows a spinner while storeKit.isBusy. | Calls startPurchase() — buys the selected plan's product via StoreKitService; on success calls onPurchased?() and close(); on failure shows purchaseErrorText. |
| **"Restore" button** | button | Underlined text button. | Calls restore() — restores a prior purchase via StoreKitService; on success calls onPurchased?() and close(); on failure shows an error (defaults to "No active purchase was found."). |
| **"Terms" button** | button | Underlined text button. | Opens the Terms of Service URL (/owlai/legal/terms, relative to APIConfiguration.baseURL) via openURL. |
| **"Privacy" button** | button | Underlined text button. | Opens the Privacy Policy URL (/owlai/legal/privacy) via openURL. |
| **purchase error text** | banner/error | Shown below the legal links if a purchase or restore attempt fails. | — |


---

# 4. Create set & Add word

## Create flashcard set

- **File:** `Presentation/Features/WordSearch/Views/CreateFlashcardSetPage.swift`
- **Purpose:** Hub page where the user names a new flashcard set (name required, description optional) and chooses one of six ways to add words to it; every add-method returns here when it finishes so the set can be populated from more than one source.
- **How you get here:** Pushed full-page from the app's floating tab bar '+' action (per CLAUDE.md / RootTabShell, not in this file's read scope).

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **Back button** | icon button | Circular button with a 'chevron.left' SF Symbol, accessibilityLabel "Back". | Calls dismiss(), popping this page. |
| **"Create flashcard set" title** | label/text | Centered header title of the page (font size 20, heavy weight). | — |
| **NAME field label** | label/text | Small all-caps section label "NAME" above the name input. | — |
| **NAME text field** | text field | TextField with placeholder "e.g. Travel phrases", bound to vm.name. Auto-focused on first appearance only (not when returning from a completed add-method). | Updates vm.name; a non-empty, valid name enables the "Add words by…" rows below (vm.isNameValid). |
| **DESCRIPTION field label** | label/text | Small all-caps section label "DESCRIPTION" above the description editor. | — |
| **DESCRIPTION text editor** | text editor | Multi-line TextEditor (min height 80) bound to vm.description; no placeholder text shown. | Updates vm.description. |
| **Error text banner** | banner/error | Destructive-colored text shown only when vm.errorText is set (e.g. a set-creation failure). | — |
| **"Add words by…" section label** | label/text | Section header text above the six add methods. | — |
| **Add with AI** | primary action | Creates/reuses the set and pushes `WordSearchView`. | Server-backed word analysis and translation. |
| **Add manually** | tile | Creates/reuses the set and pushes `ManualWordEntryView`. | Fully local entry of a word and its translation. |
| **Paste text** | tile | Opens `LocalFlashcardImportView` with an editable text area. | Local parsing and review; no AI extraction. |
| **Scan document** | tile | Opens the VisionKit document camera. | Multi-page on-device Vision OCR, then local review. |
| **Select image** | tile | Opens `PhotosPicker`. | On-device Vision OCR, then local review. |
| **Select files** | tile | Opens `.fileImporter` for TXT, CSV, TSV, or PDF. | Local text/PDFKit extraction; scanned PDFs fall back to Vision OCR. |

## Local flashcard import

- **Files:** `Presentation/Features/WordSearch/Views/LocalFlashcardImportView.swift`, `Presentation/Features/WordSearch/Services/LocalFlashcardImport.swift`
- **Purpose:** Shared offline import flow for pasted text, document scans, images, and files.
- **Behavior:** The Words, Pairs, Auto segments parse individual tokens, alternating lines, or delimiter-separated pairs. The review list supports selection, editing, swapping word/translation, duplicate removal, and local batch saving through `WordRepository.addManualWord`. Selected rows require both a word and translation. Words and Auto show an `AI translate` button that translates every non-empty draft through a non-persisting repository path; Pairs hides the button.

## Add New Word (full page)

- **File:** `Presentation/Features/WordSearch/Views/WordSearchView.swift`
- **Purpose:** Search/translate a word or phrase with AI and save the result as a new flashcard, optionally with the user's own notes.
- **How you get here:** Pushed via navigationDestination from the Create flashcard set page's "manual" or "paste text" rows (onClose is nil in this path, so the view renders as a full page rather than a sheet). The OS nav bar's inline title is set to the flashcard set's trimmed name (vm.trimmedName); WordSearchView's own default title parameter is "Add word" when no caller overrides it.

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **"Add New Word" header** | label/text | Heavy-weight header text at the top of the card (cardHeader). | — |
| **Word input field** | text field | TextField with placeholder "Enter a word or phrase...", bound to viewModel.word; submit key is "Go". | On submit (onSubmit / Go key) calls viewModel.runSearch(). |
| **"Language pair" disclosure group** | row | DisclosureGroup labeled "Language pair", shown only in full-page mode (onClose == nil). Expands to reveal two picker rows. | Expands/collapses in place; changing either language inside clears an existing result via viewModel.clearResult(). |
| **"Native language" picker row** | picker | Inside the Language pair disclosure group: labeled "Native language", a menu-style Picker listing LanguageOption.appChoices with flag icons and display names. | Sets viewModel.nativeLanguage to the chosen language code. |
| **"Learning language" picker row** | picker | Inside the Language pair disclosure group: labeled "Learning language", same menu-style Picker of LanguageOption.appChoices. | Sets viewModel.learningLanguage to the chosen language code. |
| **Error banner** | banner/error | Shown when viewModel.errorText is set; exclamationmark.circle.fill icon plus message text. | — |
| **Language pair button (globe icon)** | icon button | Square button with a "globe" SF Symbol, accessibilityLabel "Change language pair"; part of the fetch action row shown before a result exists. | Toggles showLanguagePicker to reveal/hide the language pair panel. |
| **"Translate with AI" button** | button | Primary button in the fetch action row; shows a spinner and "Analyzing..." text while viewModel.isLoading is true, otherwise "Translate with AI". Disabled/dimmed when the word field is empty or a search is in progress. | Calls viewModel.runSearch(); on success populates viewModel.result (rendering the Word Detail Result Panel); on a gated/blocked response sets viewModel.showPaywall = true. |
| **Language pair panel** | card | Revealed when the globe button is tapped; contains a two-option segmented choice and two language picker cards with a swap button. | — |
| **"This word only" option** | segmented control | One of two LanguageScope options in the language pair panel; filled circle icon when selected. | Sets languageScope = .thisWord (language change applies only to the current search, not saved as app default). |
| **"Default for all words" option** | segmented control | The other LanguageScope option in the language pair panel. | Sets languageScope = .allWords and immediately writes viewModel's current native/learning languages into appSettings.nativeLanguageCode / learningLanguageCode, making them the app-wide default. |
| **Native language picker card** | picker | Card labeled "Native" showing a flag icon and display name for viewModel.nativeLanguage; tapping opens a Menu of LanguageOption.appChoices. | Sets viewModel.nativeLanguage to the chosen option's bcp47Code. |
| **Swap languages button** | icon button | Stacked arrow.right/arrow.left icons between the two language cards, accessibilityLabel "Swap languages". | Swaps viewModel.nativeLanguage and viewModel.learningLanguage. |
| **Learning language picker card** | picker | Card labeled "Learning" (visually highlighted), showing a flag icon and display name for viewModel.learningLanguage; tapping opens a Menu of LanguageOption.appChoices. | Sets viewModel.learningLanguage to the chosen option's bcp47Code. |
| **Word Detail Result Panel** | card | Reusable result card (WordDetailResultPanel) rendered once viewModel.result is non-nil, showing part of speech, translations, pronunciation, and an example sentence. | Cataloged separately as its own page/component entry. |
| **"Add my own notes" button** | button | Dashed-border button with a "plus" icon and text "Add my own notes", shown after a result exists and the notes field hasn't been revealed yet, accessibilityLabel "Add my own notes". | Reveals the notes text editor (showNotesField = true) with a spring animation. |
| **Notes text editor** | text editor | TextEditor bound to viewModel.userNotes with placeholder overlay "Add your own notes (optional)..." when empty; shown once the Add-notes button is tapped. | Updates viewModel.userNotes, saved along with the word. |
| **"Edit Word" button** | button | Left button of the result action row, shown once a result exists. | Calls viewModel.clearResult(), discarding the current result and returning to the input/fetch state. |
| **"Save Word" button** | button | Right (primary) button of the result action row; shows checkmark icon and a spinner while viewModel.isSaving is true. Disabled while there's no result or a save is in progress, accessibilityLabel "Save Word". | Calls `viewModel.saveWord(flashcardSetID:)` with the required captured set ID; on success invokes `onSaved` (the Create Set flow pops to its hub, while Dashboard reloads and closes its popup). |
| **Paywall full-screen cover** | sheet | fullScreenCover bound to viewModel.showPaywall. | Presents PaywallView (Pro upsell, defined elsewhere); on a successful purchase its onPurchased callback re-runs viewModel.runSearch() to retry the originally blocked translate action. |

## Add New Word (popup / sheet mode)

- **File:** `Presentation/Features/WordSearch/Views/WordSearchView.swift`
- **Purpose:** Same word-search/translate/save flow as the full-page version, but docked as a bottom sheet flush against the keyboard, with its own close control.
- **How you get here:** `CreateFlashcardSetPage` opens this view only for **Add with AI**. Keyboard is raised automatically on appear.

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **Grab handle** | icon | Small capsule shape at the very top of the sheet, indicating it can be dragged. | — |
| **"Add New Word" header** | label/text | Same header text as the full-page mode. | — |
| **Close button (xmark)** | icon button | Circular button with an "xmark" SF Symbol, only rendered when onClose is provided, accessibilityLabel "Close add word". | Calls the onClose closure supplied by the presenting screen, dismissing the sheet. |
| **Word input field, language pair button/panel, fetch/translate button, notes controls, result panel, Edit/Save buttons, paywall cover** | row | Identical controls and behavior to the full-page mode described above (the "Language pair" DisclosureGroup is the one exception — it is NOT shown in popup mode, only in full-page mode). | See the corresponding full-page entries for each control's behavior. |

## Word Detail Result Panel

- **File:** `Presentation/Features/WordSearch/Components/WordDetailResultPanel.swift`
- **Purpose:** Reusable result card displaying an AI-translated word's part of speech, translations, pronunciation, irregular-verb forms (English only), and an example sentence with its translation.
- **How you get here:** Rendered inside the "Add New Word" screen (both full-page and popup/sheet modes, in WordSearchView) once viewModel.result becomes non-nil after a successful "Translate with AI" search.

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **Part of speech chip** | label/text | Capsule-shaped chip showing detail.partOfSpeech (e.g. "noun", "verb"); only shown if present. | — |
| **Verb forms row** | row | Row of chips (V1/V2/V3 labels + word) for English irregular verbs, shown only when appSettings.showVerbForms is on, the learning language is English (en-us), and the searched word matches a known irregular verb in the built-in EnglishIrregularVerbForms table. | — |
| **Primary translations text** | label/text | The first up to 3 translation strings (deduplicated, capitalized), comma-joined, in a larger bold font centered in the card. | — |
| **Secondary translations text** | label/text | Any additional translations beyond the first 3 (up to 12 total), comma-joined, in a muted smaller font; only shown if present. | — |
| **"No translation returned" text** | label/text | Muted fallback text shown when the detail has no translations at all. | — |
| **Pronunciation chip (IPA text)** | label/text | Chip showing the phonetic pronunciation wrapped in slashes (e.g. "/wɜːrd/"); shown only if detail.pronunciation is present. | — |
| **Speaker button** | icon button | "speaker.wave.2.fill" icon inside the pronunciation chip; replaced by a small ProgressView spinner while pronunciation.isSpeaking is true. Disabled if the word text is empty. | Calls pronunciation.speak(text:learningLanguageBCP47:) to play the audio pronunciation of the word. |
| **Divider** | label/text | Thin horizontal divider separating the header (chips/translations) from the example sentence section; only shown when an example exists. | — |
| **Example sentence text** | label/text | Italic quoted text showing the first example sentence from detail.examples, if present and non-empty. | — |
| **Example translation text** | label/text | Italic quoted muted text showing the translation of the example sentence (detail.exampleTranslations.first), if present. | — |


---

# 5. Library & Settings

## Library

- **File:** `Presentation/Features/Library/Views/LibraryView.swift`
- **Purpose:** Shows the user's local flashcard sets and the cached approved public catalog. Refresh occurs on entry or pull-to-refresh; search filters cached content locally.
- **How you get here:** The Library tab in the app's floating tab bar (RootTabShell), alongside Home, Learn, Pro, and the + create action.

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **Search Library button / field** | button + text field | Search is collapsed by default. `Search Library` reveals and focuses the cached local/public search field; `Close Library search` clears and collapses it. | Filters local and public cached sets without requests. |
| **Local set card ⋯ menu** | menu | Shows Start/Add learning for inactive sets or Remove from learning for active sets, plus Edit and Delete. Public/Private is not present. | Learning actions toggle the local active flag; Edit opens flashcard-set settings where visibility is managed. |
| **Refresh failure** | behavior | Entry/pull failures preserve cached content and render no top-level notice. | The next entry or pull may refresh again. |

## Settings

- **File:** `Presentation/Features/Settings/Views/SettingsView.swift`
- **Purpose:** Full settings surface: language selection, daily learning goal, study reminders, review-card direction, display preferences, category management, and app design system (theme/appearance/font/accent). Also doubles as the first-run "choose your languages" setup screen when `isInitialSetup` is true.
- **How you get here:** Presented as a modal sheet (SwiftUI `.sheet`). The presenting call site (e.g. a settings/gear entry point elsewhere in the app, or the onboarding flow passing `isInitialSetup: true`) is outside the two files read for this catalog, so it isn't shown here. `SettingsView` accepts `isInitialSetup` and `onInitialSetupCompleted` params confirming it's reused for first-run language setup.

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **Drag indicator capsule** | icon | Thin rounded capsule at the very top of the sheet; purely decorative sheet-drag affordance. | — |
| **"Settings" title** | label/text | Top bar heading text, always shown. | — |
| **"Done" button** | button | Shown only when `isInitialSetup` is true, in the top bar. Disabled (dimmed) until the native and learning languages differ (`canCompleteInitialSetup`). | Calls appSettings.markInitialSettingsComplete(), invokes the onInitialSetupCompleted callback, then dismisses the Settings sheet. |
| **"Close settings" button** | button | Circular "xmark" icon button in the top bar, shown only when `isInitialSetup` is false. accessibilityLabel "Close settings". | Dismisses the Settings sheet. |
| **"Choose your languages" banner** | banner/error | Shown only during first-run setup (`isInitialSetup`). Title "Choose your languages" plus body text "Pick your native language and the language you want to learn. Your choices save automatically to your profile." on a tinted background card. | — |
| **"Language Settings" section header** | label/text | Section header with a "globe" icon and the title "Language Settings". | — |
| **Native language menu** | picker | Card showing a flag icon, the current native language's display name, and the role caption "Native". accessibilityLabel "Choose native language". | Opens a Menu listing every LanguageOption.appChoices entry (flag icon + display name + short code). Selecting one sets appSettings.nativeLanguageCode. |
| **Language swap button** | button | Small button showing a stacked "arrow.right" over "arrow.left" icon, between the native and learning language cards. accessibilityLabel "Swap native and learning languages". | Swaps appSettings.nativeLanguageCode and appSettings.learningLanguageCode in place. |
| **Learning language menu** | picker | Same style as the native language card but bound to the learning language; role caption "Learning", with a purple 2pt border to distinguish it. accessibilityLabel "Choose learning language". | Opens a Menu listing every LanguageOption.appChoices entry. Selecting one sets appSettings.learningLanguageCode. |
| **"Learn new words per day" section header** | label/text | Section header with a "target" icon and the title "Learn new words per day". | — |
| **Daily goal number buttons** | segmented control | Row of pill buttons, one per value in AppSettingsStore.dailyGoalChoices, each labeled with its number. The button matching appSettings.dailyReviewGoal is highlighted (purple fill, white text). | Sets appSettings.dailyReviewGoal to the tapped value. |
| **"new words per day" caption** | label/text | Caption under the number-button row, in the muted color. | — |
| **"Reminders" section header** | label/text | Section header with a "bell" icon and the title "Reminders". | — |
| **"FROM" time row** | picker | Label "FROM" above a Menu-button row showing a clock icon and the formatted start time of the reminder window (default 8:00 AM). | Opens a Menu of times in 30-minute increments from 5:00 AM to 11:00 PM. Selecting one sets the first (earliest) entry of appSettings.reminderMinutesFromMidnight. |
| **"UNTIL" time row** | picker | Label "UNTIL" above a Menu-button row showing a clock icon and the formatted end time of the reminder window (default 7:00 PM). | Opens the same 30-minute-increment time Menu. Selecting one sets the second (latest) entry of appSettings.reminderMinutesFromMidnight. |
| **"NOTIFICATIONS PER DAY" stepper** | row | Label "NOTIFICATIONS PER DAY" above a stepper row: a "minus" icon button (disabled/dimmed at AppSettingsStore.notificationCountRange.lowerBound), a center display of the current count plus a "times daily" caption, and a "plus" icon button (disabled/dimmed at the upper bound). | Increments or decrements appSettings.notificationCountPerDay by 1. |
| **Reminder status footnote** | label/text | Dynamic caption reflecting the current notification authorization status, e.g. "You'll get a daily nudge at each time above when notifications are allowed.", "Tap Enable to turn on reminders.", "Notifications are off. Turn them on in Settings ▸ FlashCard AI ▸ Notifications.", or "Tap Enable to allow notifications for study reminders." | — |
| **"Allow and Save" button** | button | Full-width purple button with an "arrow.right" icon, shown only when appSettings.remindersEnabled is false. | Requests iOS notification authorization (alert/sound/badge). If granted, sets appSettings.remindersEnabled = true and applies StudyReminderScheduler.shared.apply(using:). (The same underlying toggleReminders() function also handles turning reminders off, but that path's button isn't shown since this button only renders while reminders are disabled.) |
| **"Review Direction" section header** | label/text | Section header with an "arrow.left.arrow.right" icon and the title "Review Direction". | — |
| **Direction option 1 button** | button | Text reads "Show {Learning language} → recall {Native language}" (e.g. "Show Spanish → recall English"), built from directionTitle(.showLearningRecallNative). Highlighted purple border/text when selected. | Sets appSettings.reviewDirection = .showLearningRecallNative. |
| **Direction option 2 button** | button | Text reads "Show {Native language} → recall {Learning language}", built from directionTitle(.showNativeRecallLearning). Highlighted purple border/text when selected. | Sets appSettings.reviewDirection = .showNativeRecallLearning. |
| **"Display Preferences" section header** | label/text | Section header with a "book.fill" icon and the title "Display Preferences". | — |
| **"Expand all cards in History" toggle** | toggle | Toggle with title "Expand all cards in History" and subtitle "Show examples & notes on word cards by default". | Binds to and flips appSettings.expandHistoryCards, affecting default expansion state of word cards in the History view. |
| **"Show forms of verbs" toggle** | toggle | Toggle with title "Show forms of verbs" and subtitle "Show V1, V2, and V3 forms for English irregular verbs". | Binds to and flips appSettings.showVerbForms. |
| **"Categories" section header** | label/text | Section header with a "folder.fill" icon and the title "Categories". | — |
| **"Manage Categories" row** | row | Row with title "Manage Categories", subtitle "Add, rename, reorder, or delete word categories", and a trailing "chevron.right" icon. | Sets showManageCategories = true, which presents ManageCategoriesView as a sheet with [.medium, .large] detents, passing repository and an onCategoriesChanged callback. ManageCategoriesView is defined in a separate file not covered by this catalog pass. |
| **"Design System" section header** | label/text | Header row with a "paintpalette.fill" icon and the title "Design System", both tinted the accent purple. | — |
| **MODE picker ("System" / "Light" / "Dark")** | segmented control | Label "MODE" above three buttons: "System" (icon circle.lefthalf.filled, caption "Match device"), "Light" (icon sun.max.fill, caption "Bright & clean"), "Dark" (icon moon.stars.fill, caption "Easy on eyes"). Selected option is purple-highlighted. | Sets theme.appearance to the tapped ThemeAppearance case, switching the app's color scheme. |
| **BACKGROUND tone picker** | picker | Label "BACKGROUND" above one button per BgTonePreset case, each showing a small color-swatch circle plus the preset's displayName. | Sets appSettings.bgTone to the tapped preset, changing the app's background tint. |
| **ACCENT COLOR swatches** | picker | Label "ACCENT COLOR" above a row of circular color-swatch buttons, one per DesignAccentPreset case (accessibilityLabel = preset.displayName). The selected swatch gets a ring outline in the preset's ink color. | Sets appSettings.designAccent to the tapped preset, changing the app's accent color throughout the UI. |
| **DISPLAY FONT picker** | segmented control | Label "DISPLAY FONT" above one button per FontDisplayPreset case, each showing the preset's displayName rendered in that preset's own font design. Selected option is purple-highlighted. | Sets appSettings.fontPreset to the tapped preset, changing the app's display font. |

## New reminder

- **File:** `Presentation/Features/Settings/Views/SettingsView.swift`
- **Purpose:** A small time-picker sheet for adding one extra study-reminder time to the schedule (appSettings.reminderMinutesFromMidnight), beyond the FROM/UNTIL window set on the main Settings page.
- **How you get here:** Presented via `.sheet(isPresented: $showAddReminderPicker)` on the Settings page. Note: within this file, `showAddReminderPicker` is only ever set back to `false` (by this sheet's own Cancel/Add actions) — no button in SettingsView.swift sets it to `true`, so as currently coded this sheet has no visible trigger in this file; it may be an orphaned/legacy control or triggered from code outside the two files reviewed.

| Element | Type | What it does | Leads to |
|---|---|---|---|
| **Time wheel picker** | picker | A DatePicker labeled "Time" (label hidden via .labelsHidden()), wheel style, restricted to hour-and-minute components. | Updates the local @State newReminderDate as the user scrolls. |
| **"Cancel" button** | button | Toolbar button in the cancellation-action placement. | Sets showAddReminderPicker = false, closing the sheet without saving. |
| **"Add" button** | button | Toolbar button in the confirmation-action placement. | Converts newReminderDate to minutes-from-midnight, appends it to appSettings.reminderMinutesFromMidnight if not already present (re-sorted), closes the sheet, and calls StudyReminderScheduler.shared.apply(using:) to reschedule notifications. |
