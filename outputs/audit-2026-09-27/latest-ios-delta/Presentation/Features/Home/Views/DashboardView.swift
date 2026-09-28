import SwiftUI

enum DashboardEmptyStatePolicy {
    static let message = "No flashcards yet. Tap Create to build a set with AI, text, a file, or a photo — or choose a topic from Library."

    static func showsAddSpeedDial(allWordsCount: Int, isMenuExpanded: Bool = false) -> Bool {
        allWordsCount > 0 || isMenuExpanded
    }
}

/// Main home screen after onboarding (web dashboard–inspired layout).
struct DashboardView: View {
    private struct ReviewPresentation: Identifiable {
        let id = UUID()
        let row: WordDashboardRow?
    }

    let repository: WordRepository
    let reviewDependencies: ReviewSessionDependencies
    @Binding private var openSettingsOnAppear: Bool
    @Binding private var isAddFlowBlockingRootNavigation: Bool

    @EnvironmentObject private var theme: ThemeManager
    @EnvironmentObject private var appSettings: AppSettingsStore
    @EnvironmentObject private var notificationRouter: StudyNotificationRouter
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @StateObject private var viewModel: DashboardViewModel
    @StateObject private var pronunciation = PronunciationSpeaker()
    @State private var showSettings = false
    @State private var isInitialSettingsSetup = false
    @State private var addFlowState = DashboardAddFlowState()
    @State private var reviewSession: ReviewPresentation?
    @State private var openAddMenuAfterReview = false
    @State private var reviewOpenedFromNotification = false
    @State private var showThemeChooser = false
    @State private var rowToEdit: WordDashboardRow?
    @State private var rowPendingDelete: WordDashboardRow?
    @State private var pulseAddWordButton = false
    @State private var isSearchPresented = false
    @FocusState private var searchFocused: Bool

    private let controlCorner: CGFloat = 16
    private var homeInk: Color { appSettings.currentInk }
    private var homeMuted: Color { appSettings.currentMuted }
    private var homePrimary: Color { appSettings.currentAccent }
    private var homeDeep: Color { appSettings.currentDeep }
    private var homeSoft: Color { appSettings.currentSoft }
    private var homeBackground: Color { appSettings.currentBackground }
    private var homeCard: Color { appSettings.cardSurface }

    init(
        repository: WordRepository,
        reviewDependencies: ReviewSessionDependencies,
        openSettingsOnAppear: Binding<Bool> = .constant(false),
        isAddFlowBlockingRootNavigation: Binding<Bool> = .constant(false),
        viewModel: DashboardViewModel? = nil
    ) {
        self.repository = repository
        self.reviewDependencies = reviewDependencies
        _openSettingsOnAppear = openSettingsOnAppear
        _isAddFlowBlockingRootNavigation = isAddFlowBlockingRootNavigation
        _viewModel = StateObject(
            wrappedValue: viewModel ?? DashboardViewModel(
                repository: repository,
                reviewDependencies: DashboardReviewDependencies(
                    reviewSessionDependencies: reviewDependencies
                )
            )
        )
    }

    var body: some View {
        ZStack {
            homeBackground
                .ignoresSafeArea()

            ScrollView {
                VStack(alignment: .leading, spacing: 12) {
                    topBar

                    if !viewModel.isSearching {
                        reviewBanner

                        statsRow
                    }

                    listHeaderRow

                    if !viewModel.isSearching {
                        flashcardSetsSection
                    }

                    if let err = viewModel.loadError {
                        Text(err)
                            .font(.system(size: 14))
                            .foregroundStyle(theme.palette.destructive)
                            .padding(.horizontal, 4)
                    }

                    if viewModel.visibleRows.isEmpty && viewModel.loadError == nil {
                        Text(viewModel.isSearching
                             ? "No matches for “\(viewModel.searchText)”"
                             : DashboardEmptyStatePolicy.message)
                            .font(.system(size: 15))
                            .foregroundStyle(theme.palette.mutedForeground)
                            .multilineTextAlignment(.center)
                            .frame(maxWidth: .infinity)
                            .padding(.vertical, 24)
                    }

                    LazyVStack(spacing: 12) {
                        ForEach(viewModel.visibleRows) { row in
                            DashboardWordCard(
                                row: row,
                                pronunciation: pronunciation,
                                controlCorner: controlCorner,
                                expandHistoryDetail: appSettings.expandHistoryCards,
                                style: .homeList,
                                expansion: $viewModel.expandedCardId,
                                onEdit: { rowToEdit = $0 },
                                onDelete: { rowPendingDelete = $0 },
                                onReview: { openReview(word: $0) }
                            )
                        }
                    }
                }
                .padding(.horizontal, 18)
                .padding(.top, 20)
                .padding(.bottom, 164)
            }
            .accessibilityHidden(
                addFlowState.menuState.isExpanded
            )

            if DashboardEmptyStatePolicy.showsAddSpeedDial(
                allWordsCount: viewModel.allWordsCount,
                isMenuExpanded: addFlowState.menuState.isExpanded
            ) {
                DashboardAddSpeedDial(
                    isExpanded: addFlowState.menuState.isExpanded,
                    shouldPulse: shouldPulseAddWordButton,
                    pulse: pulseAddWordButton,
                    focusRestorationRequest:
                        addFlowState.menuState.focusRestorationRequest,
                    onToggle: toggleAddMenu,
                    onDismiss: dismissAddMenu,
                    onSelect: selectAddOption
                )
                .environmentObject(appSettings)
                .zIndex(9)
            }

        }
        .onReceive(NotificationCenter.default.publisher(for: .serverFeatureFlagsChanged)) { _ in reloadDashboard() }
        .onReceive(NotificationCenter.default.publisher(for: .accountDataSynced)) { _ in reloadDashboard() }
        .onAppear {
            reloadDashboard()
            openReviewIfRequestedByNotification()
            if openSettingsOnAppear {
                isInitialSettingsSetup = true
                showSettings = true
                openSettingsOnAppear = false
            }
        }
        .sheet(isPresented: $showSettings) {
            SettingsView(
                repository: repository,
                isInitialSetup: isInitialSettingsSetup
            ) {
                isInitialSettingsSetup = false
            }
                .environmentObject(theme)
                .presentationDetents([.large])
        }
        .sheet(
            item: setSelectionRequestBinding,
            onDismiss: completeSetSelectionDismissal
        ) { request in
            DashboardAddSetPicker(
                request: request,
                sets: availableAddTargetSets,
                onSelect: chooseAddTargetSet,
                onCancel: cancelAddTargetSelection
            )
            .environmentObject(appSettings)
            .presentationDetents([.medium, .large])
        }
        .fullScreenCover(item: $reviewSession, onDismiss: {
            guard openAddMenuAfterReview else { return }
            openAddMenuAfterReview = false
            if !addFlowState.menuState.isExpanded {
                toggleAddMenu()
            }
        }) { presentation in
            NavigationStack {
                ReviewSessionView(
                    dependencies: reviewDependencies,
                    focusedRow: presentation.row,
                    onAddWords: {
                        openAddMenuAfterReview = true
                        reviewSession = nil
                    }
                )
                    .environmentObject(theme)
                    .environmentObject(appSettings)
            }
        }
        .onChange(of: reviewSession != nil) { _, isOn in
            if !isOn {
                reviewOpenedFromNotification = false
                reloadDashboard()
            }
        }
        .navigationDestination(item: navigationAddRouteBinding) { route in
            switch route.destination {
            case .aiTranslate:
                WordSearchView(
                    repository: repository,
                    flashcardSetID: route.flashcardSetID,
                    secondaryReview: reviewDependencies.secondaryReview,
                    navigationTitle: "Add with AI",
                    onSaved: {
                        addFlowState.recordPersistence(.ai(saved: true))
                        addFlowState.dismissActiveRoute()
                    }
                )
                .environmentObject(theme)
                .environmentObject(appSettings)
            case .manualEntry:
                ManualWordEntryView(
                    repository: repository,
                    flashcardSetID: route.flashcardSetID,
                    onSaved: {
                        addFlowState.recordPersistence(.manual(saved: true))
                    }
                )
                .environmentObject(theme)
                .environmentObject(appSettings)
            case .localImport(let source):
                LocalFlashcardImportView(
                    repository: repository,
                    flashcardSetID: route.flashcardSetID,
                    source: source,
                    onPersisted: { count in
                        addFlowState.recordPersistence(
                            .localImport(savedCount: count)
                        )
                    },
                    onFinished: {
                        addFlowState.dismissActiveRoute()
                    }
                )
                .environmentObject(theme)
                .environmentObject(appSettings)
            }
        }
        .onChange(of: notificationRouter.reviewOpenNonce) { _, _ in
            openReviewIfRequestedByNotification()
        }
        .onChange(of: appSettings.newWordsPerDay) { _, _ in
            reloadDashboard()
        }
        .onChange(of: appSettings.reminderFromMinutes) { _, _ in
            reloadDashboard()
        }
        .onChange(of: viewModel.totalWords) { _, _ in
            updateAddWordPulse()
        }
        .onChange(of: addFlowState.dashboardReloadRequest) { _, _ in
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
                .buttonStyle(.plain)
                .accessibilityLabel("Search Learn")
            }

            Button {
                showSettings = true
            } label: {
                Image(systemName: "gearshape.fill")
                    .font(.system(size: 17, weight: .bold))
                    .foregroundStyle(homeInk)
                    .frame(width: 44, height: 44)
                    .background(homeCard)
                    .clipShape(Circle())
                    .shadow(color: .black.opacity(0.07), radius: 14, x: 0, y: 7)
            }
            .buttonStyle(.plain)
            .accessibilityLabel("Settings")
        }
        .padding(.top, 0)
    }

    private var learnSearchField: some View {
        HStack(spacing: 8) {
            Image(systemName: "magnifyingglass")
                .font(.system(size: 15, weight: .bold))
                .foregroundStyle(homeMuted)
                .accessibilityHidden(true)
            TextField("Search words or flashcard sets", text: $viewModel.searchText)
                .font(.system(size: 16, weight: .semibold))
                .foregroundStyle(homeInk)
                .tint(homePrimary)
                .autocorrectionDisabled()
                .textInputAutocapitalization(.never)
                .submitLabel(.search)
                .focused($searchFocused)
                .accessibilityLabel("Learn search field")
            Button(action: closeLearnSearch) {
                Image(systemName: "xmark.circle.fill")
                    .font(.system(size: 16, weight: .bold))
                    .foregroundStyle(homeMuted)
                    .frame(width: 44, height: 44)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityLabel("Close Learn search")
        }
        .padding(.leading, 14)
        .frame(height: 44)
        .frame(maxWidth: .infinity)
        .background(homeCard)
        .clipShape(Capsule())
        .overlay(
            Capsule().stroke(
                searchFocused ? homePrimary.opacity(0.5) : homeSoft,
                lineWidth: 1
            )
        )
        .shadow(color: .black.opacity(0.05), radius: 12, x: 0, y: 6)
    }

    private func closeLearnSearch() {
        viewModel.searchText = ""
        searchFocused = false
        isSearchPresented = false
    }

    private var statsRow: some View {
        HStack(spacing: 0) {
            NavigationLink {
                WordHistoryView(
                    repository: repository,
                    reviewDependencies: reviewDependencies,
                    listSort: .alphabetical
                )
                    .environmentObject(appSettings)
            } label: {
                metricValue(value: "\(viewModel.totalWords)", label: "TOTAL")
            }
            .buttonStyle(.plain)

            Button {
                openReview()
            } label: {
                metricValue(value: "\(viewModel.reviewToday)", label: "DUE")
            }
            .buttonStyle(.plain)
            .disabled(viewModel.totalWords == 0)
            .opacity(viewModel.totalWords == 0 ? 0.55 : 1)

            metricValue(value: "\(viewModel.studied)", label: "STUDIED")

            metricValue(value: "0d", label: "STREAK")
        }
        .padding(.top, 4)
    }

    private func metricValue(value: String, label: String) -> some View {
        VStack(spacing: 6) {
            Text(value)
                .font(.system(size: 24, weight: .heavy))
                .foregroundStyle(homeInk)
            Text(label)
                .font(.system(size: 10, weight: .bold))
                .tracking(1.6)
                .foregroundStyle(homeMuted)
        }
        .frame(maxWidth: .infinity)
    }

    private var reviewBanner: some View {
        Group {
            if viewModel.totalWords == 0 {
                emptyReviewBannerContent
            } else {
                Button {
                    openReview()
                } label: {
                activeReviewBannerContent
                }
                .buttonStyle(.plain)
            }
        }
        .padding(.top, 10)
    }

    private var emptyReviewBannerContent: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("BUILD YOUR WORD LIST")
                .font(.system(size: 13, weight: .bold))
                .tracking(2.2)
                .foregroundStyle(Color.white.opacity(0.9))

            Text("Learn only the words you need")
                .font(.system(size: 28, weight: .heavy))
                .foregroundStyle(Color.white)
                .lineLimit(2)
                .minimumScaleFactor(0.78)

            Text("Use AI to translate words, create flashcards, and schedule reminders to help you remember them.")
                .font(.system(size: 16, weight: .bold))
                .foregroundStyle(Color.white.opacity(0.92))
                .lineSpacing(3)
                .fixedSize(horizontal: false, vertical: true)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(24)
        .background(reviewCardBackground)
        .clipShape(RoundedRectangle(cornerRadius: 25, style: .continuous))
        .shadow(color: homePrimary.opacity(0.16), radius: 14, x: 0, y: 8)
    }

    private var activeReviewBannerContent: some View {
        VStack(alignment: .leading, spacing: 15) {
            VStack(alignment: .leading, spacing: 7) {
                Text("DUE REVIEW")
                    .font(.system(size: 12, weight: .bold))
                    .tracking(2.2)
                    .foregroundStyle(Color.white.opacity(0.72))

                HStack(alignment: .lastTextBaseline, spacing: 8) {
                    Text("\(viewModel.reviewToday)")
                        .font(.system(size: 48, weight: .heavy))
                        .foregroundStyle(Color.white)
                    Text("words due")
                        .font(.system(size: 18, weight: .heavy))
                        .foregroundStyle(Color.white.opacity(0.92))
                }

                Text("~ \(estimatedReviewMinutes) min · 0-day streak")
                    .font(.system(size: 14, weight: .medium))
                    .foregroundStyle(Color.white.opacity(0.72))
            }

            HStack(spacing: 12) {
                Text(viewModel.reviewToday == 0 ? "Review anyway" : "Start review")
                    .font(.system(size: 16, weight: .heavy))
                Image(systemName: "arrow.right")
                    .font(.system(size: 16, weight: .heavy))
            }
            .foregroundStyle(homePrimary)
            .frame(maxWidth: .infinity)
            .frame(height: 48)
            .background(Color.white)
            .clipShape(Capsule())
        }
        .padding(19)
        .frame(maxWidth: .infinity, alignment: .leading)
        .frame(minHeight: 188)
        .background(reviewCardBackground)
        .clipShape(RoundedRectangle(cornerRadius: 25, style: .continuous))
        .shadow(color: homePrimary.opacity(0.16), radius: 14, x: 0, y: 8)
    }

    private var flashcardSetsSection: some View {
        VStack(alignment: .leading, spacing: 0) {
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 8) {
                    ForEach(viewModel.flashcardSets) { set in
                        flashcardSetChip(
                            set: set,
                            selected: viewModel.selectedSetId == set.id
                        )
                    }
                }
            }
        }
    }

    private func flashcardSetChip(set: FlashcardSetSummary, selected: Bool) -> some View {
        Button {
            viewModel.selectSet(set)
        } label: {
            HStack(spacing: 8) {
                Image(systemName: set.systemImage)
                    .font(.system(size: 13, weight: .semibold))
                Text(set.title)
                    .font(.system(size: 14, weight: .heavy))
                    .lineLimit(2)
                Text("\(set.wordCount)")
                    .font(.system(size: 12, weight: .bold))
                    .foregroundStyle(selected ? theme.palette.primaryForeground.opacity(0.9) : theme.palette.mutedForeground)
                    .padding(.horizontal, 8)
                    .padding(.vertical, 2)
                    .background(
                        Capsule().fill(selected ? theme.palette.primaryForeground.opacity(0.2) : theme.palette.mutedFill.opacity(0.8))
                    )
            }
            .foregroundStyle(selected ? Color.white : homeInk)
            .padding(.horizontal, 12)
            .padding(.vertical, 10)
            .frame(minHeight: 50)
            .background(selected ? homePrimary : homeCard)
            .overlay(
                RoundedRectangle(cornerRadius: 28, style: .continuous)
                    .stroke(selected ? Color.clear : homeSoft, lineWidth: 1)
            )
            .clipShape(RoundedRectangle(cornerRadius: 28, style: .continuous))
            .shadow(color: selected ? .clear : .black.opacity(0.04), radius: 10, x: 0, y: 5)
        }
        .buttonStyle(.plain)
    }

    private var listHeaderRow: some View {
        HStack {
            Text(viewModel.isSearching ? "Results" : "Words")
                .font(.system(size: 23, weight: .heavy))
                .foregroundStyle(homeInk)
            Spacer()
            NavigationLink {
                WordHistoryView(
                    repository: repository,
                    reviewDependencies: reviewDependencies,
                    listSort: .dateAddedNewestFirst
                )
                    .environmentObject(appSettings)
            } label: {
                headerActionLabel(title: "History", systemImage: "clock.fill")
            }
        }
        .padding(.top, 8)
    }

    private func headerActionLabel(title: String, systemImage: String) -> some View {
        HStack(spacing: 6) {
            Image(systemName: systemImage)
                .font(.system(size: 14, weight: .bold))
            Text(title)
                .font(.system(size: 15, weight: .heavy))
        }
        .foregroundStyle(homePrimary)
        .padding(.horizontal, 10)
        .padding(.vertical, 8)
        .contentShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
    }

    private var shouldPulseAddWordButton: Bool {
        addFlowState.menuState.shouldPulse(
            selectedWordCount: viewModel.totalWords
        )
    }

    private var availableAddTargetSets: [FlashcardSetSummary] {
        DashboardAddSetSelectionPolicy.availableSets(
            from: viewModel.flashcardSets
        )
    }

    private var setSelectionRequestBinding: Binding<
        DashboardAddSetSelectionRequest?
    > {
        Binding(
            get: { addFlowState.setSelectionRequest },
            set: { request in
                guard request == nil,
                      addFlowState.setSelectionRequest != nil else { return }
                addFlowState.cancelSetSelection()
            }
        )
    }

    private var navigationAddRouteBinding: Binding<DashboardAddRoute?> {
        Binding(
            get: {
                if case .navigation(let route) = addFlowState.presentation {
                    return route
                }
                return nil
            },
            set: { route in
                if route == nil {
                    addFlowState.dismissActiveRoute()
                }
            }
        )
    }

    private var addMenuAnimation: Animation {
        reduceMotion
            ? .linear(duration: 0.12)
            : .spring(response: 0.28, dampingFraction: 0.86)
    }

    private func toggleAddMenu() {
        if !addFlowState.shouldResignSearchBeforeMenuExpansion {
            dismissAddMenu()
            return
        }
        searchFocused = false
        withAnimation(.linear(duration: 0)) {
            pulseAddWordButton = false
        }
        withAnimation(addMenuAnimation) {
            addFlowState.toggleMenu()
        }
    }

    private func dismissAddMenu() {
        withAnimation(addMenuAnimation) {
            addFlowState.dismissMenu()
        }
        updateAddWordPulse()
    }

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
        Task { await StudyReminderScheduler.shared.apply(using: appSettings) }
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
            return "Good night"
        }
    }

    private var greetingTitle: String {
        let name = appSettings.displayName.trimmingCharacters(in: .whitespacesAndNewlines)
        return name.isEmpty ? greeting : "\(greeting), \(name)"
    }

    private var estimatedReviewMinutes: Int {
        max(1, Int(ceil(Double(max(viewModel.reviewToday, 1)) * 0.5)))
    }

    private var reviewCardBackground: some View {
        ZStack(alignment: .topTrailing) {
            homeDeep
            Circle()
                .stroke(Color.white.opacity(0.14), lineWidth: 2)
                .frame(width: 220, height: 220)
                .offset(x: 68, y: -66)
            Circle()
                .stroke(Color.white.opacity(0.16), lineWidth: 2)
                .frame(width: 158, height: 158)
                .offset(x: 46, y: -44)
            Circle()
                .stroke(Color.white.opacity(0.18), lineWidth: 2)
                .frame(width: 94, height: 94)
                .offset(x: 20, y: -22)
        }
    }

    private func openReview(word: WordDashboardRow? = nil) {
        guard word?.isLocked != true else { return }
        searchFocused = false
        reviewSession = ReviewPresentation(row: word)
    }

    private func openReviewIfRequestedByNotification() {
        guard notificationRouter.consumePendingReviewOpen() else { return }
        reviewOpenedFromNotification = true
        openReview()
    }

    private var selectedSetId: String {
        viewModel.selectedSetId
    }

}
