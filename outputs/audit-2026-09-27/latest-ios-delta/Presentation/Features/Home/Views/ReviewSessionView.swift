import SwiftUI

enum ReviewIntervalFormatter {
    static func string(seconds: TimeInterval) -> String {
        let value = max(0, Int(seconds.rounded()))
        if value >= 86_400 {
            return "\(max(1, Int((Double(value) / 86_400).rounded())))d"
        }
        if value >= 3_600 {
            return "\(max(1, Int((Double(value) / 3_600).rounded())))h"
        }
        return "\(max(1, Int(ceil(Double(value) / 60))))m"
    }
}

enum ReviewGradePresentation {
    static func grades(isRevealed: Bool) -> [ReviewGrade] {
        isRevealed ? [.again, .hard, .good, .easy] : []
    }
}

/// FSRS review session. The view only controls presentation; all queue construction,
/// introduction, scheduling, and persistence is delegated to the injected graph.
struct ReviewSessionView: View {
    static let revealAnswerBackground = ProductPalette.primaryPurple

    @EnvironmentObject private var theme: ThemeManager
    @EnvironmentObject private var appSettings: AppSettingsStore
    @Environment(\.dismiss) private var dismiss

    @StateObject private var viewModel: ReviewSessionViewModel
    @StateObject private var secondaryReview: SecondaryReviewModel
    @State private var secondaryRetry = UUID()
    @StateObject private var pronunciation = PronunciationSpeaker()
    @State private var hasLoaded = false
    @State private var spelling = SpellingPractice()
    @State private var spellingFlip = false

    private let focusedRow: WordDashboardRow?
    private let onAddWords: () -> Void

    init(
        dependencies: ReviewSessionDependencies,
        focusedRow: WordDashboardRow? = nil,
        onAddWords: @escaping () -> Void
    ) {
        self.focusedRow = focusedRow
        self.onAddWords = onAddWords
        _secondaryReview = StateObject(wrappedValue: SecondaryReviewModel(service: dependencies.secondaryReview))
        _viewModel = StateObject(
            wrappedValue: ReviewSessionViewModel(dependencies: dependencies)
        )
    }

    var body: some View {
        Group {
            if !hasLoaded {
                ProgressView()
                    .tint(theme.palette.primary)
            } else if let loadError = viewModel.loadError {
                errorState(
                    title: "Couldn’t load review queue",
                    message: loadError,
                    retry: {
                        resetSpelling()
                        viewModel.retryLoad()
                    }
                )
            } else if let candidate = viewModel.current {
                reviewScreen(candidate)
            } else {
                completionState
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(appSettings.currentBackground)
        .navigationBarBackButtonHidden(true)
        .toolbar(.hidden, for: .navigationBar)
        .onAppear {
            guard !hasLoaded else { return }
            load()
        }
        .task(id: SecondaryTaskID(request: secondaryRequest, retry: secondaryRetry)) {
            await secondaryReview.load(secondaryRequest)
        }
        .onChange(of: viewModel.current?.card.id) { _, _ in resetSpelling() }
        .onChange(of: viewModel.current?.word.displayWord) { _, _ in resetSpelling() }
        .onChange(of: viewModel.completedCount) { _, _ in resetSpelling() }
        .onChange(of: appSettings.spellingPracticeEnabled) { _, _ in resetSpelling() }
        .onDisappear { secondaryReview.invalidate() }
        .onReceive(NotificationCenter.default.publisher(for: .owlAccountSessionChanged)) { _ in
            secondaryReview.invalidateAccountScope()
        }
        .onReceive(
            NotificationCenter.default.publisher(
                for: .reviewQueueRefreshRequested
            )
        ) { _ in
            guard hasLoaded else { return }
            resetSpelling()
            viewModel.refresh(
                nativeLanguage: nativeLanguage,
                learningLanguage: learningLanguage,
                cardID: focusedCardID
            )
        }
    }

    private struct SecondaryTaskID: Hashable {
        let request: SecondaryReviewRequest?
        let retry: UUID
    }

    private var secondaryRequest: SecondaryReviewRequest? {
        guard viewModel.showAnswer, let candidate = viewModel.current, !candidate.isLocked else { return nil }
        return SecondaryReviewRequest.make(row: candidate.word,
                                           enabled: appSettings.secondaryLanguageEnabled,
                                           secondaryLanguage: appSettings.secondaryLanguageCode)
    }

    private func resetSpelling() {
        spelling = SpellingPractice()
        spellingFlip = false
    }

    private func load() {
        resetSpelling()
        hasLoaded = true
        viewModel.load(
            nativeLanguage: nativeLanguage,
            learningLanguage: learningLanguage,
            cardID: focusedCardID
        )
    }

    private var nativeLanguage: String {
        focusedRow?.nativeLanguageBcp47 ?? appSettings.nativeLanguageCode
    }

    private var learningLanguage: String {
        focusedRow?.learningLanguageBcp47 ?? appSettings.learningLanguageCode
    }

    private var focusedCardID: String? {
        guard let focusedRow else { return nil }
        return FSRSSchemaMigrator.reviewCardID(
            wordID: focusedRow.id,
            direction: appSettings.reviewDirection
        )
    }

    private var completionState: some View {
        VStack(spacing: 18) {
            Image(systemName: viewModel.completedCount > 0 ? "checkmark.circle.fill" : "tray")
                .font(.system(size: 54))
                .foregroundStyle(appSettings.currentAccent)
            Text(viewModel.completedCount > 0 ? "Great work!" : "Nothing to review")
                .font(.system(size: 24, weight: .bold))
                .foregroundStyle(appSettings.currentInk)
            Text(
                viewModel.completedCount > 0
                    ? "You completed \(viewModel.completedCount) cards."
                    : "Your active flashcard sets are caught up."
            )
            .font(.system(size: 15))
            .foregroundStyle(appSettings.currentMuted)
            .multilineTextAlignment(.center)
            VStack(spacing: 12) {
                Button {
                    resetSpelling()
                    secondaryReview.invalidate()
                    viewModel.restart()
                } label: {
                    Label("Start again", systemImage: "arrow.counterclockwise")
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .accessibilityIdentifier("review.restart")

                Button(action: onAddWords) {
                    Label("Add words", systemImage: "plus")
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.bordered)
                .accessibilityIdentifier("review.addWords")

                Button {
                    dismiss()
                } label: {
                    Text("Go to main page")
                        .frame(maxWidth: .infinity, minHeight: 44)
                }
                .buttonStyle(.plain)
                .foregroundStyle(appSettings.currentAccent)
                .accessibilityIdentifier("review.home")
            }
            .controlSize(.large)
            .tint(appSettings.currentAccent)
            .frame(maxWidth: 280)
        }
        .padding(28)
    }

    private func errorState(
        title: String,
        message: String,
        retry: @escaping () -> Void
    ) -> some View {
        VStack(spacing: 16) {
            Image(systemName: "exclamationmark.triangle")
                .font(.system(size: 38))
                .foregroundStyle(theme.palette.destructive)
            Text(title)
                .font(.system(size: 19, weight: .bold))
                .foregroundStyle(appSettings.currentInk)
            Text(message)
                .font(.system(size: 14))
                .foregroundStyle(appSettings.currentMuted)
                .multilineTextAlignment(.center)
            Button("Retry", action: retry)
                .buttonStyle(.borderedProminent)
            Button("Close") { dismiss() }
                .buttonStyle(.plain)
        }
        .padding(28)
    }

    private func reviewScreen(_ candidate: ReviewQueueCandidate) -> some View {
        VStack(spacing: 18) {
            VStack(spacing: 18) {
                header
                queueCounts
            }
            .background(controlBackdrop)

            ScrollView {
                VStack(spacing: 16) {
                    card(candidate)
                    if appSettings.spellingPracticeEnabled {
                        SpellingPracticeInput(expectedWord: candidate.word.displayWord,
                                              practice: $spelling,
                                              answerRevealed: viewModel.showAnswer)
                    }
                }
                .padding(.vertical, 4)
            }
            .scrollDismissesKeyboard(.interactively)
            // Keep cards visible behind the translucent areas around controls.
            .scrollClipDisabled()
            .zIndex(-1)

            if let saveError = viewModel.saveError {
                VStack(spacing: 8) {
                    Text(saveError)
                        .font(.system(size: 13))
                        .foregroundStyle(theme.palette.destructive)
                        .multilineTextAlignment(.center)
                    Button("Retry save") {
                        // A failed apply retains its immutable attempt. `rate` will
                        // retry that exact attempt regardless of this placeholder.
                        viewModel.rate(.good)
                    }
                    .font(.system(size: 14, weight: .semibold))
                }
            }

            VStack(spacing: 4) {
                HStack {
                    Spacer()
                    spellingToggle
                }
                if viewModel.showAnswer {
                    ratingBar
                } else {
                    Button {
                        viewModel.revealAnswer()
                    } label: {
                        Label("Reveal answer", systemImage: "rectangle.on.rectangle")
                            .font(.system(size: 17, weight: .bold))
                            .foregroundStyle(.white)
                            .frame(maxWidth: .infinity)
                            .frame(height: 56)
                            .background(Self.revealAnswerBackground)
                            .clipShape(RoundedRectangle(cornerRadius: 18, style: .continuous))
                    }
                    .buttonStyle(.plain)
                }
            }
            .background(controlBackdrop)
        }
        .padding(.horizontal, 20)
        .padding(.top, 12)
        .padding(.bottom, 20)
    }

    private var controlBackdrop: some View {
        RoundedRectangle(cornerRadius: 26, style: .continuous)
            .fill(appSettings.currentBackground.opacity(0.45))
            .allowsHitTesting(false)
    }

    private var header: some View {
        HStack {
            Button {
                dismiss()
            } label: {
                Image(systemName: "xmark")
                    .font(.system(size: 17, weight: .bold))
                    .foregroundStyle(appSettings.currentInk)
                    .frame(width: 44, height: 44)
                    .background(appSettings.cardSurface)
                    .clipShape(Circle())
            }
            .buttonStyle(.plain)

            Spacer()

            VStack(spacing: 2) {
                Text("Review")
                    .font(.system(size: 21, weight: .heavy))
                    .foregroundStyle(appSettings.currentInk)
                Text("\(viewModel.completedCount) completed")
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundStyle(appSettings.currentMuted)
            }

            Spacer()

            Button {
                if appSettings.spellingPracticeEnabled {
                    spellingFlip.toggle()
                } else {
                    viewModel.swapReviewSide()
                }
            } label: {
                Image(systemName: "arrow.left.arrow.right")
                    .font(.system(size: 16, weight: .bold))
                    .foregroundStyle(appSettings.currentInk)
                    .frame(width: 44, height: 44)
                    .background(appSettings.cardSurface)
                    .clipShape(Circle())
            }
            .buttonStyle(.plain)
            .accessibilityLabel("Swap review side")
        }
    }

    private var spellingToggle: some View {
        Button {
            appSettings.spellingPracticeEnabled.toggle()
        } label: {
            Image(systemName: "keyboard")
                .font(.system(size: 20, weight: .medium))
                .foregroundStyle(appSettings.spellingPracticeEnabled ? appSettings.currentAccent : appSettings.currentMuted)
                .overlay(alignment: .bottomTrailing) {
                    if appSettings.spellingPracticeEnabled {
                        Image(systemName: "checkmark")
                            .font(.system(size: 9, weight: .heavy))
                            .foregroundStyle(appSettings.currentAccent)
                            .offset(x: 5, y: 5)
                    }
                }
                .frame(width: 44, height: 44)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel("Practice spelling")
        .accessibilityValue(appSettings.spellingPracticeEnabled ? "On" : "Off")
        .accessibilityIdentifier("review.spelling.toggle")
    }

    private var queueCounts: some View {
        HStack(spacing: 8) {
            countChip(title: "New", value: viewModel.queue.counts.new)
            countChip(title: "Learning", value: viewModel.queue.counts.learning)
            countChip(title: "Review", value: viewModel.queue.counts.review)
        }
    }

    private func countChip(title: String, value: Int) -> some View {
        HStack(spacing: 5) {
            Text(title)
            Text("\(value)")
                .fontWeight(.heavy)
        }
        .font(.system(size: 12, weight: .semibold))
        .foregroundStyle(appSettings.currentMuted)
        .frame(maxWidth: .infinity)
        .padding(.vertical, 8)
        .background(appSettings.currentSoft)
        .clipShape(Capsule())
    }

    private func card(_ candidate: ReviewQueueCandidate) -> some View {
        let row = candidate.word
        let learningFirst = appSettings.spellingPracticeEnabled
            ? spellingFlip
            : (candidate.card.direction == .learningToNative) != viewModel.sessionFlip

        return VStack(spacing: 24) {
            HStack {
                LanguageFlagIcon(
                    bcp47Code: learningFirst
                        ? row.learningLanguageBcp47
                        : row.nativeLanguageBcp47,
                    size: 28
                )
                Spacer()
                if let part = row.partOfSpeech, !part.isEmpty {
                    Text(part.uppercased())
                        .font(.system(size: 11, weight: .bold))
                        .tracking(1.5)
                        .foregroundStyle(appSettings.currentMuted)
                }
            }

            sideContent(row: row, learningSide: learningFirst)

            if viewModel.showAnswer {
                Divider()
                sideContent(row: row, learningSide: !learningFirst, isAnswer: true)

                if let example = row.exampleLines.first {
                    VStack(alignment: .leading, spacing: 7) {
                        Text(example.sentence)
                            .font(.system(size: 14, weight: .semibold))
                        if let translation = example.translation, !translation.isEmpty {
                            Text(translation)
                                .font(.system(size: 14))
                                .foregroundStyle(appSettings.currentMuted)
                        }
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(14)
                    .background(appSettings.currentSoft)
                    .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
                }
                if let request = secondaryRequest {
                    SecondaryReviewSurface(request: request, model: secondaryReview) {
                        secondaryRetry = UUID()
                    }
                }
            }
        }
        .padding(26)
        .frame(maxWidth: .infinity)
        .background(appSettings.cardSurface)
        .clipShape(RoundedRectangle(cornerRadius: 26, style: .continuous))
        .shadow(color: .black.opacity(0.06), radius: 18, x: 0, y: 9)
        .onTapGesture {
            if !viewModel.showAnswer {
                viewModel.revealAnswer()
            }
        }
    }

    @ViewBuilder
    private func sideContent(
        row: WordDashboardRow,
        learningSide: Bool,
        isAnswer: Bool = false
    ) -> some View {
        if learningSide {
            VStack(spacing: 10) {
                Text(row.displayWord)
                    .font(.system(size: 38, weight: .heavy))
                    .foregroundStyle(appSettings.currentInk)
                    .multilineTextAlignment(.center)
                    .minimumScaleFactor(0.55)
                let forms = EnglishIrregularVerbForms.reviewForms(
                    for: row.displayWord,
                    language: row.learningLanguageBcp47,
                    partOfSpeech: row.partOfSpeech
                )
                if !forms.isEmpty {
                    Text(forms.map(\.word).joined(separator: " · "))
                        .font(.system(size: 17, weight: .medium))
                        .foregroundStyle(appSettings.currentMuted)
                        .multilineTextAlignment(.center)
                        .accessibilityLabel(forms.map { "\($0.label): \($0.word)" }.joined(separator: ", "))
                        .accessibilityIdentifier("review.verbForms")
                }
                HStack(spacing: 10) {
                    if let pronunciationText = row.pronunciation,
                       !pronunciationText.isEmpty {
                        Text(pronunciationText)
                            .font(.system(size: 15, weight: .semibold))
                            .italic()
                            .foregroundStyle(appSettings.currentMuted)
                    }
                    Button {
                        pronunciation.speak(
                            text: row.displayWord,
                            learningLanguageBCP47: row.learningLanguageBcp47
                        )
                    } label: {
                        Image(systemName: pronunciation.isSpeaking
                              ? "speaker.wave.3.fill"
                              : "speaker.wave.2.fill")
                            .foregroundStyle(appSettings.currentAccent)
                    }
                    .buttonStyle(.plain)
                }
            }
        } else {
            Text(row.displayTranslations.isEmpty
                 ? "—"
                 : row.displayTranslations.joined(separator: ", "))
                .font(.system(size: isAnswer ? 21 : 30, weight: .heavy))
                .foregroundStyle(appSettings.currentInk)
                .multilineTextAlignment(.center)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private var ratingBar: some View {
        HStack(spacing: 7) {
            ForEach(
                ReviewGradePresentation.grades(
                    isRevealed: viewModel.showAnswer
                ),
                id: \.self
            ) { grade in
                Button {
                    viewModel.rate(grade)
                } label: {
                    VStack(spacing: 4) {
                        Text(ratingTitle(grade))
                            .font(.system(size: 13, weight: .heavy))
                        Text(ratingInterval(grade))
                            .font(.system(size: 11, weight: .semibold))
                    }
                    .foregroundStyle(ratingTint(grade))
                    .frame(maxWidth: .infinity)
                    .frame(height: 54)
                    .background(ratingFill(grade))
                    .clipShape(RoundedRectangle(cornerRadius: 15, style: .continuous))
                }
                .buttonStyle(.plain)
                .disabled(viewModel.isSubmitting)
            }
        }
        .opacity(viewModel.isSubmitting ? 0.55 : 1)
    }

    private func ratingInterval(_ grade: ReviewGrade) -> String {
        guard let transition = viewModel.previews[grade] else { return "—" }
        return ReviewIntervalFormatter.string(
            seconds: transition.next.due.timeIntervalSince(
                transition.reviewedAt
            )
        )
    }

    private func ratingTitle(_ grade: ReviewGrade) -> String {
        switch grade {
        case .again: "Again"
        case .hard: "Hard"
        case .good: "Good"
        case .easy: "Easy"
        }
    }

    private func ratingTint(_ grade: ReviewGrade) -> Color {
        switch grade {
        case .again: Color(red: 0.83, green: 0.25, blue: 0.40)
        case .hard: Color(red: 0.73, green: 0.39, blue: 0.10)
        case .good: Color(red: 0.17, green: 0.47, blue: 0.70)
        case .easy: Color(red: 0.18, green: 0.57, blue: 0.38)
        }
    }

    private func ratingFill(_ grade: ReviewGrade) -> Color {
        switch grade {
        case .again: Color(red: 1.0, green: 0.86, blue: 0.90)
        case .hard: Color(red: 1.0, green: 0.91, blue: 0.66)
        case .good: Color(red: 0.84, green: 0.92, blue: 0.98)
        case .easy: Color(red: 0.81, green: 0.92, blue: 0.84)
        }
    }
}


/// Input is outside the tappable answer card, so typing never reveals the answer.
struct SpellingPracticeInput: View {
    @Environment(\.colorScheme) private var colorScheme
    @EnvironmentObject private var appSettings: AppSettingsStore
    let expectedWord: String
    @Binding var practice: SpellingPractice
    let answerRevealed: Bool
    @FocusState private var focused: Bool

    private var feedbackColor: Color {
        switch practice.isCorrect {
        case true: return colorScheme == .dark
            ? Color(red: 0.44, green: 0.84, blue: 0.63)
            : Color(red: 0.086, green: 0.455, blue: 0.247)
        case false: return colorScheme == .dark
            ? Color(red: 1, green: 0.60, blue: 0.64)
            : Color(red: 0.706, green: 0.137, blue: 0.196)
        case nil: return appSettings.currentAccent
        }
    }

    private var buttonColor: Color {
        switch practice.isCorrect {
        case true: return Color(red: 0.086, green: 0.455, blue: 0.247)
        case false: return Color(red: 0.706, green: 0.137, blue: 0.196)
        case nil: return appSettings.currentAccent
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Write the word")
                .font(.system(size: 14, weight: .semibold))
                .foregroundStyle(appSettings.currentMuted)
            HStack(spacing: 8) {
                TextField("Type the word", text: $practice.answer)
                    .font(.system(size: 18, weight: .semibold))
                    .textInputAutocapitalization(.never)
                    .autocorrectionDisabled()
                    .submitLabel(.done)
                    .focused($focused)
                    .onSubmit { practice.check(expected: expectedWord) }
                    .padding(.horizontal, 12)
                    .frame(minHeight: 48)
                    .foregroundStyle(appSettings.currentInk)
                    .background(appSettings.cardSurface)
                    .overlay {
                        RoundedRectangle(cornerRadius: 12)
                            .stroke(practice.isCorrect == nil ? appSettings.currentSoft : feedbackColor, lineWidth: 2)
                    }
                    .clipShape(RoundedRectangle(cornerRadius: 12))
                    .accessibilityLabel("Write the word")
                    .accessibilityIdentifier("review.spelling.input")
                Button("Check") { practice.check(expected: expectedWord) }
                    .font(.system(size: 15, weight: .bold))
                    .padding(.horizontal, 16)
                    .frame(minHeight: 48)
                    .foregroundStyle(.white)
                    .background(buttonColor)
                    .clipShape(RoundedRectangle(cornerRadius: 12))
                    .disabled(!practice.canCheck)
                    .opacity(practice.canCheck ? 1 : 0.5)
                    .accessibilityIdentifier("review.spelling.check")
            }
            if let correct = practice.isCorrect {
                Label(correct ? "Correct" : "Not quite. Try again.",
                      systemImage: correct ? "checkmark.circle.fill" : "xmark.circle.fill")
                    .font(.system(size: 14, weight: .semibold))
                    .foregroundStyle(feedbackColor)
                    .accessibilityIdentifier("review.spelling.feedback")
            }
        }
        .onChange(of: answerRevealed) { _, revealed in if revealed { focused = false } }
    }
}
