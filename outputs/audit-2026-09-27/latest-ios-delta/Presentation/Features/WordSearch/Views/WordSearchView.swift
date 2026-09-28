import SwiftUI

/// Parity target: `apps/web-react/src/components/words/AddWordModal.jsx` (layout, spacing, primary actions).
struct WordSearchView: View {
    @EnvironmentObject private var theme: ThemeManager
    @EnvironmentObject private var appSettings: AppSettingsStore
    @StateObject private var viewModel: WordSearchViewModel
    @StateObject private var pronunciation = PronunciationSpeaker()
    @State private var showLanguagePicker = false
    @State private var languageScope: LanguageScope = .thisWord
    @State private var showNotesField = false
    @FocusState private var isWordInputFocused: Bool
    @AccessibilityFocusState private var isWordInputAccessibilityFocused: Bool
    private let flashcardSetID: String
    private let navigationTitle: String
    private let onClose: (() -> Void)?
    private let onSaved: (() -> Void)?

    init(repository: WordRepository, flashcardSetID: String, secondaryReview: SecondaryReviewService? = nil, navigationTitle: String = "Add word", onClose: (() -> Void)? = nil, onSaved: (() -> Void)? = nil) {
        _viewModel = StateObject(wrappedValue: WordSearchViewModel(repository: repository))
        self.flashcardSetID = flashcardSetID
        self.navigationTitle = navigationTitle
        self.onClose = onClose
        self.onSaved = onSaved
    }

    private let contentMaxWidth: CGFloat = 386
    private let cardCorner: CGFloat = 30
    private let controlCorner: CGFloat = 16
    private var addInk: Color { appSettings.currentInk }
    private var addMuted: Color { appSettings.currentMuted }
    private var addPrimary: Color { appSettings.currentAccent }
    private var addSoft: Color { appSettings.currentSoft }
    private var addCard: Color { appSettings.cardSurface }

    private enum LanguageScope: String, CaseIterable, Identifiable {
        case thisWord
        case allWords

        var id: String { rawValue }

        var title: String {
            switch self {
            case .thisWord: return "This word only"
            case .allWords: return "Default for all words"
            }
        }
    }

    var body: some View {
        Group {
            if onClose == nil {
                GeometryReader { _ in
                    ZStack {
                        appSettings.pageBackground(light: theme.palette.background)
                            .ignoresSafeArea()

                        ScrollView {
                            VStack(alignment: .leading, spacing: 0) {
                                cardChrome
                            }
                            .frame(maxWidth: .infinity)
                            .padding(.horizontal, 16)
                            .padding(.vertical, 24)
                        }
                        .scrollDismissesKeyboard(.interactively)
                    }
                }
            } else {
                // Popup mode: render the card content-sized so the host can dock
                // it as a bottom sheet flush on the keyboard.
                cardChrome
            }
        }
        .navigationTitle(navigationTitle)
        .navigationBarTitleDisplayMode(.inline)
        .toolbar(onClose == nil ? .visible : .automatic, for: .navigationBar)
        .onAppear {
            syncLanguagesFromAppSettings()
            // In popup (sheet) mode, raise the keyboard immediately so the sheet
            // opens already docked on it. `async` lets the field commit first.
            if onClose != nil {
                DispatchQueue.main.async {
                    isWordInputFocused = true
                    isWordInputAccessibilityFocused = true
                }
            }
        }
        .onChange(of: appSettings.nativeLanguageCode) { _, _ in
            if languageScope == .allWords || !showLanguagePicker {
                syncLanguagesFromAppSettings()
            }
        }
        .onChange(of: appSettings.learningLanguageCode) { _, _ in
            if languageScope == .allWords || !showLanguagePicker {
                syncLanguagesFromAppSettings()
            }
        }
        .onChange(of: viewModel.nativeLanguage) { _, new in
            applyLanguageChange(new, to: \.nativeLanguageCode, fallback: "es")
        }
        .onChange(of: viewModel.learningLanguage) { _, new in
            applyLanguageChange(new, to: \.learningLanguageCode, fallback: "en-us")
        }
        .onReceive(viewModel.$result) { result in
            if result != nil {
                isWordInputFocused = false
            }
        }
        .onDisappear { viewModel.cancelPendingSearch() }
        .onChange(of: appSettings.secondaryLanguageEnabled) { _, _ in viewModel.clearResult() }
        .onChange(of: appSettings.secondaryLanguageCode) { _, _ in viewModel.clearResult() }
        .onReceive(NotificationCenter.default.publisher(for: .owlAccountSessionChanged)) { _ in
            viewModel.invalidateAccountScope()
        }
        .fullScreenCover(isPresented: $viewModel.showPaywall) {
            PaywallView(onPurchased: {
                // Entitlement is now active — translate the word the user was trying to add.
                Task { await runSearch() }
            })
            .environmentObject(theme)
            .environmentObject(appSettings)
        }
    }

    private func runSearch() async {
        await viewModel.runSearch(secondaryLanguage: appSettings.secondaryLanguageEnabled
            ? appSettings.secondaryLanguageCode : nil)
    }

    private func syncLanguagesFromAppSettings() {
        let n = LanguageOption.canonicalPickerCode(stored: appSettings.nativeLanguageCode, fallback: "es")
        let l = LanguageOption.canonicalPickerCode(stored: appSettings.learningLanguageCode, fallback: "en-us")
        if viewModel.nativeLanguage != n { viewModel.nativeLanguage = n }
        if viewModel.learningLanguage != l { viewModel.learningLanguage = l }
    }

    private func applyLanguageChange(_ newValue: String, to keyPath: ReferenceWritableKeyPath<AppSettingsStore, String>, fallback: String) {
        let canonical = LanguageOption.canonicalPickerCode(stored: newValue, fallback: fallback)
        if canonical != newValue {
            if keyPath == \AppSettingsStore.nativeLanguageCode {
                viewModel.nativeLanguage = canonical
            } else {
                viewModel.learningLanguage = canonical
            }
            return
        }

        if languageScope == .allWords, appSettings[keyPath: keyPath] != canonical {
            appSettings[keyPath: keyPath] = canonical
        }
        if viewModel.result != nil {
            viewModel.clearResult()
        }
    }

    private var cardChrome: some View {
        Group {
            if onClose == nil {
                fullPageCardChrome
            } else {
                popupCardChrome
            }
        }
    }

    private var fullPageCardChrome: some View {
        VStack(alignment: .leading, spacing: 20) {
            cardHeader
            cardBodyContent
            if viewModel.result != nil {
                resultActionRow
            }
        }
        .padding(24)
        .frame(maxWidth: contentMaxWidth)
        .frame(maxWidth: .infinity)
        .background(addCard)
        .clipShape(RoundedRectangle(cornerRadius: cardCorner, style: .continuous))
        .shadow(color: .black.opacity(0.12), radius: 24, x: 0, y: 12)
    }

    private var popupCardChrome: some View {
        ViewThatFits(in: .vertical) {
            popupCardContent(scrolls: false)

            if needsPopupScrolling {
                popupCardContent(scrolls: true)
            }
        }
        .frame(maxWidth: .infinity)
        .background(addCard)
        .clipShape(
            UnevenRoundedRectangle(
                topLeadingRadius: cardCorner,
                bottomLeadingRadius: 0,
                bottomTrailingRadius: 0,
                topTrailingRadius: cardCorner,
                style: .continuous
            )
        )
        .shadow(color: .black.opacity(0.18), radius: 22, x: 0, y: -8)
    }

    private var needsPopupScrolling: Bool {
        viewModel.result != nil || showLanguagePicker || showNotesField || viewModel.errorText != nil
    }

    private func popupCardContent(scrolls: Bool) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            grabHandle
                .frame(maxWidth: .infinity)
                .padding(.top, 10)
                .padding(.bottom, 2)

            cardHeader
                .padding(.horizontal, 23)
                .padding(.top, 10)
                .padding(.bottom, 14)

            if scrolls {
                ScrollView {
                    popupBodyContent
                }
                .scrollDismissesKeyboard(.interactively)
                .scrollIndicators(.hidden)
            } else {
                popupBodyContent
            }
        }
    }

    private var popupBodyContent: some View {
        VStack(alignment: .leading, spacing: 14) {
            cardBodyContent
            if viewModel.result != nil {
                resultActionRow
            }
        }
        .padding(.horizontal, 23)
        .padding(.bottom, 23)
    }

    private var grabHandle: some View {
        Capsule(style: .continuous)
            .fill(addInk.opacity(0.18))
            .frame(width: 40, height: 5)
    }

    private var cardHeader: some View {
        HStack {
            Text("Add word")
                .font(.system(size: 19, weight: .heavy))
                .foregroundStyle(addInk)

            Spacer()

            if let onClose {
                Button(action: onClose) {
                    Image(systemName: "xmark")
                        .font(.system(size: 16, weight: .medium))
                        .foregroundStyle(addInk)
                        .frame(width: 44, height: 44)
                        .background(addSoft)
                        .clipShape(Circle())
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Close add word")
            }
        }
    }

    @ViewBuilder
    private var cardBodyContent: some View {
        VStack(alignment: .leading, spacing: onClose == nil ? 20 : 14) {
            inputRow

            if showLanguagePicker {
                languagePairPanel
                    .transition(.opacity.combined(with: .move(edge: .top)))
            }

            if let errorText = viewModel.errorText {
                errorBanner(errorText)
            }

            if viewModel.result == nil {
                fetchActionRow
            } else if let detail = viewModel.result {
                WordDetailResultPanel(
                    word: viewModel.word.trimmingCharacters(in: .whitespacesAndNewlines),
                    detail: detail,
                    learningLanguage: viewModel.learningLanguage,
                    pronunciation: pronunciation,
                    controlCorner: controlCorner
                )

                if let content = viewModel.bundledSecondaryTranslation(
                    enabled: appSettings.secondaryLanguageEnabled,
                    language: appSettings.secondaryLanguageCode
                ) {
                    bundledSecondarySurface(content)
                }

                if showNotesField {
                    notesField
                } else {
                    showNotesButton
                }
            }
        }
    }

    private func bundledSecondarySurface(_ content: SecondaryReviewContent) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 8) {
                LanguageFlagIcon(bcp47Code: content.languageCode, size: 18)
                Text(LanguageOption.appChoices.first { $0.bcp47Code == content.languageCode }?.nativeName
                     ?? content.languageCode.uppercased())
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundStyle(appSettings.currentMuted)
            }
            Text(content.translation)
                .font(.system(size: 14, weight: .semibold))
                .foregroundStyle(appSettings.currentInk)
                .fixedSize(horizontal: false, vertical: true)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(14)
        .background(appSettings.cardSurface)
        .overlay {
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .stroke(appSettings.currentSoft, lineWidth: 1)
        }
        .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("add_word.secondary_language")
    }

    private var resultActionRow: some View {
        HStack(spacing: 10) {
            Button(action: { viewModel.clearResult() }) {
                Text("Edit word")
                    .font(.system(size: 16, weight: .heavy))
                    .frame(maxWidth: .infinity)
                    .frame(height: 52)
            }
            .buttonStyle(.plain)
            .background(addCard)
            .overlay(
                RoundedRectangle(cornerRadius: controlCorner, style: .continuous)
                    .stroke(addSoft, lineWidth: 1.4)
            )
            .clipShape(RoundedRectangle(cornerRadius: controlCorner, style: .continuous))
            .foregroundStyle(addInk)

            Button(action: {
                Task {
                    if await viewModel.saveWord(
                        flashcardSetID: flashcardSetID
                    ) {
                        onSaved?()
                    }
                }
            }) {
                HStack(spacing: 12) {
                    if viewModel.isSaving {
                        ProgressView()
                            .tint(theme.palette.primaryForeground)
                    }
                    Text("Save word")
                    Image(systemName: "checkmark")
                }
                .font(.system(size: 16, weight: .heavy))
                .frame(maxWidth: .infinity)
                .frame(height: 52)
            }
            .buttonStyle(.plain)
            .background(
                (viewModel.result == nil || viewModel.isSaving)
                    ? addPrimary.opacity(0.45)
                    : addPrimary
            )
            .foregroundStyle(Color.white.opacity(
                (viewModel.result == nil || viewModel.isSaving) ? 0.85 : 1
            ))
            .clipShape(RoundedRectangle(cornerRadius: controlCorner, style: .continuous))
            .disabled(viewModel.result == nil || viewModel.isSaving)
            .accessibilityLabel("Save word")
        }
    }

    private var inputRow: some View {
        TextField("Enter a word or phrase…", text: $viewModel.word)
            .textFieldStyle(.plain)
            .font(.system(size: 16, weight: .heavy))
            .foregroundStyle(addInk)
            .padding(.horizontal, 17)
            .frame(height: 52)
            .background(addCard)
            .overlay(
                RoundedRectangle(cornerRadius: controlCorner, style: .continuous)
                    .stroke(addSoft, lineWidth: 1.4)
            )
            .clipShape(RoundedRectangle(cornerRadius: controlCorner, style: .continuous))
            .textInputAutocapitalization(.never)
            .autocorrectionDisabled()
            .submitLabel(.go)
            .focused($isWordInputFocused)
            .accessibilityFocused($isWordInputAccessibilityFocused)
            .onSubmit { Task { await runSearch() } }
    }

    private var fetchActionRow: some View {
        HStack(spacing: 10) {
            languagePairButton
            primaryFetchButton
        }
    }

    private var languagePairButton: some View {
        Button {
            withAnimation(.spring(response: 0.24, dampingFraction: 0.9)) {
                showLanguagePicker.toggle()
            }
        } label: {
            Image(systemName: "globe")
                .font(.system(size: 20, weight: .semibold))
                .foregroundStyle(addPrimary)
                .frame(width: 58, height: 58)
                .background(addSoft)
                .clipShape(RoundedRectangle(cornerRadius: controlCorner, style: .continuous))
        }
        .buttonStyle(.plain)
        .accessibilityLabel("Change language pair")
    }

    private var languagePairPanel: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(spacing: 8) {
                ForEach(LanguageScope.allCases) { scope in
                    Button {
                        languageScope = scope
                        if scope == .allWords {
                            appSettings.nativeLanguageCode = viewModel.nativeLanguage
                            appSettings.learningLanguageCode = viewModel.learningLanguage
                        }
                    } label: {
                        HStack(spacing: 7) {
                            Image(systemName: languageScope == scope ? "largecircle.fill.circle" : "circle")
                                .font(.system(size: 13, weight: .semibold))
                            Text(scope.title)
                                .font(.system(size: 12, weight: .semibold))
                                .lineLimit(2)
                                .minimumScaleFactor(0.75)
                        }
                        .foregroundStyle(languageScope == scope ? theme.palette.primary : theme.palette.mutedForeground)
                        .frame(maxWidth: .infinity)
                        .padding(.horizontal, 8)
                        .frame(height: 42)
                        .background(languageScope == scope ? theme.palette.primary.opacity(0.10) : theme.palette.card.opacity(0.6))
                        .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
                    }
                    .buttonStyle(.plain)
                }
            }

            HStack(alignment: .center, spacing: 10) {
                languagePickerCard(selection: $viewModel.nativeLanguage, role: "My language", isHighlighted: false)

                Button {
                    let native = viewModel.nativeLanguage
                    viewModel.nativeLanguage = viewModel.learningLanguage
                    viewModel.learningLanguage = native
                } label: {
                    VStack(spacing: 3) {
                        Image(systemName: "arrow.right")
                        Image(systemName: "arrow.left")
                    }
                    .font(.system(size: 18, weight: .semibold))
                    .foregroundStyle(theme.palette.mutedForeground)
                    .frame(width: 34)
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Swap languages")

                languagePickerCard(selection: $viewModel.learningLanguage, role: "I’m learning", isHighlighted: true)
            }
        }
        .padding(12)
        .background(theme.palette.mutedFill.opacity(0.42))
        .clipShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
    }

    private func languagePickerCard(selection: Binding<String>, role: String, isHighlighted: Bool) -> some View {
        Menu {
            ForEach(LanguageOption.appChoices) { opt in
                Button {
                    selection.wrappedValue = opt.bcp47Code
                } label: {
                    HStack(spacing: 10) {
                        LanguageFlagIcon(option: opt, size: 22)
                        Text(opt.displayName)
                        Spacer(minLength: 8)
                        Text(opt.shortUpperCode)
                            .font(.system(size: 13, weight: .medium))
                            .foregroundStyle(.secondary)
                    }
                }
            }
        } label: {
            VStack(spacing: 9) {
                LanguageFlagIcon(bcp47Code: selection.wrappedValue, size: 34)
                Text(LanguageOption.displayName(forCode: selection.wrappedValue))
                    .font(.system(size: 14, weight: .heavy, design: appSettings.fontPreset.design))
                    .foregroundStyle(theme.palette.foreground)
                    .multilineTextAlignment(.center)
                    .lineLimit(2)
                    .minimumScaleFactor(0.65)
                    .frame(minHeight: 34)
                Text(role)
                    .font(.system(size: 12, weight: .medium, design: appSettings.fontPreset.design))
                    .foregroundStyle(theme.palette.mutedForeground)
            }
            .frame(maxWidth: .infinity)
            .frame(height: 112)
            .padding(.horizontal, 8)
            .background(isHighlighted ? theme.palette.primary.opacity(0.10) : theme.palette.card)
            .overlay(
                RoundedRectangle(cornerRadius: 14, style: .continuous)
                    .stroke(isHighlighted ? theme.palette.primary.opacity(0.55) : theme.palette.border.opacity(0.55), lineWidth: isHighlighted ? 1.5 : 1)
            )
            .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
        }
        .buttonStyle(.plain)
    }

    private func errorBanner(_ message: String) -> some View {
        HStack(alignment: .top, spacing: 10) {
            Image(systemName: "exclamationmark.circle.fill")
                .foregroundStyle(theme.palette.destructive)
            Text(message)
                .font(.system(size: 14))
                .foregroundStyle(theme.palette.foreground)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(theme.palette.destructive.opacity(0.08))
        .overlay(
            RoundedRectangle(cornerRadius: controlCorner, style: .continuous)
                .stroke(theme.palette.destructive.opacity(0.25), lineWidth: 1)
        )
        .clipShape(RoundedRectangle(cornerRadius: controlCorner, style: .continuous))
    }

    private var primaryFetchButton: some View {
        Button(action: { Task { await runSearch() } }) {
            HStack(spacing: 8) {
                if viewModel.isLoading {
                    ProgressView()
                        .tint(theme.palette.primaryForeground)
                    Text("Analyzing…")
                } else {
                    Text("Translate with AI")
                }
            }
            .font(.system(size: 15, weight: .semibold))
            .frame(maxWidth: .infinity)
            .frame(height: 58)
        }
        .buttonStyle(.plain)
        .background(viewModel.isLoading ? addPrimary.opacity(0.85) : addPrimary)
        .foregroundStyle(theme.palette.primaryForeground)
        .clipShape(RoundedRectangle(cornerRadius: controlCorner, style: .continuous))
        .shadow(color: theme.palette.primary.opacity(0.25), radius: 8, x: 0, y: 4)
        .disabled(viewModel.word.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || viewModel.isLoading)
        .opacity(viewModel.word.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? 0.5 : 1)
    }

    private var notesField: some View {
        ZStack(alignment: .topLeading) {
            if viewModel.userNotes.isEmpty {
                Text("Add notes (optional)…")
                    .font(.system(size: 16))
                    .foregroundStyle(theme.palette.mutedForeground)
                    .padding(.horizontal, 14)
                    .padding(.vertical, 12)
                    .allowsHitTesting(false)
            }
            TextEditor(text: $viewModel.userNotes)
                .font(.system(size: 16))
                .foregroundStyle(theme.palette.foreground)
                .frame(minHeight: 72, maxHeight: 100)
                .scrollContentBackground(.hidden)
                .padding(.horizontal, 8)
                .padding(.vertical, 6)
        }
        .background(theme.palette.card)
        .overlay(
            RoundedRectangle(cornerRadius: controlCorner, style: .continuous)
                .stroke(theme.palette.border, lineWidth: 1)
        )
        .clipShape(RoundedRectangle(cornerRadius: controlCorner, style: .continuous))
    }

    private var showNotesButton: some View {
        Button {
            withAnimation(.spring(response: 0.22, dampingFraction: 0.9)) {
                showNotesField = true
            }
        } label: {
            HStack(spacing: 8) {
                Image(systemName: "plus")
                    .font(.system(size: 16, weight: .medium))
                Text("Add notes")
                    .font(.system(size: 16, weight: .heavy))
            }
            .foregroundStyle(addPrimary)
            .frame(maxWidth: .infinity)
            .frame(height: 52)
            .background(addCard)
            .overlay(
                RoundedRectangle(cornerRadius: controlCorner, style: .continuous)
                    .stroke(addSoft, style: StrokeStyle(lineWidth: 1.4, dash: [5, 5]))
            )
            .clipShape(RoundedRectangle(cornerRadius: controlCorner, style: .continuous))
        }
        .buttonStyle(.plain)
        .accessibilityLabel("Add notes")
    }
}
