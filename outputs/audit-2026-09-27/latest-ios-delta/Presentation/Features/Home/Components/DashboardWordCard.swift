import SwiftUI

struct DashboardWordCard: View {
    enum CardStyle {
        case standard
        case homeList
    }

    @EnvironmentObject private var theme: ThemeManager
    @EnvironmentObject private var appSettings: AppSettingsStore
    let row: WordDashboardRow
    @ObservedObject var pronunciation: PronunciationSpeaker
    let controlCorner: CGFloat
    var expandHistoryDetail: Bool = false
    var style: CardStyle = .standard
    var expansion: Binding<String?>?
    var onEdit: ((WordDashboardRow) -> Void)?
    var onDelete: ((WordDashboardRow) -> Void)?
    var onReview: ((WordDashboardRow) -> Void)?

    private var homeInk: Color { appSettings.currentInk }
    private var homeMuted: Color { appSettings.currentMuted }
    private var homeSoft: Color { appSettings.currentSoft }
    private var homeCard: Color { appSettings.cardSurface }

    /// `expired_trial` words beyond the free limit: read-only, greyed, non-expandable.
    private var isLocked: Bool { row.isLocked }

    private var isExpanded: Bool {
        expansion?.wrappedValue == row.id
    }

    private var shouldShowExpandedContent: Bool {
        !isLocked && (isExpanded || expandHistoryDetail)
    }

    var body: some View {
        if style == .homeList {
            homeListBody
        } else {
            standardBody
        }
    }

    private var standardBody: some View {
        VStack(alignment: .leading, spacing: 0) {
            if isLocked {
                HStack {
                    lockedChip
                    Spacer(minLength: 0)
                }
                .padding(.bottom, 8)
            }

            cardSummary
                .opacity(isLocked ? 0.55 : 1)

            flashcardSetRow
                .padding(.top, 10)
                .opacity(isLocked ? 0.55 : 1)

            if shouldShowExpandedContent {
                expandedSection
            }
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(theme.palette.card)
        .overlay(
            RoundedRectangle(cornerRadius: controlCorner, style: .continuous)
                .stroke(theme.palette.border, lineWidth: 1)
        )
        .clipShape(RoundedRectangle(cornerRadius: controlCorner, style: .continuous))
        .shadow(color: .black.opacity(0.06), radius: 8, x: 0, y: 4)
        .contentShape(Rectangle())
        .onTapGesture {
            guard !isLocked, expansion != nil else { return }
            if expansion?.wrappedValue == row.id {
                expansion?.wrappedValue = nil
            } else {
                expansion?.wrappedValue = row.id
            }
        }
    }

    /// Read-only badge shown on `expired_trial` locked words.
    private var lockedChip: some View {
        HStack(spacing: 5) {
            Image(systemName: "lock.fill")
                .font(.system(size: 10, weight: .bold))
            Text("LOCKED")
                .font(.system(size: 11, weight: .heavy))
                .tracking(0.7)
        }
        .foregroundStyle(homeMuted)
        .padding(.horizontal, 10)
        .padding(.vertical, 5)
        .background(homeSoft)
        .clipShape(Capsule())
    }

    private var homeListBody: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .top, spacing: 10) {
                VStack(alignment: .leading, spacing: 6) {
                    homeWordTitle

                    HStack(spacing: 7) {
                        let nativeText = previewTranslations.joined(separator: ", ")
                        if !nativeText.isEmpty {
                            LanguageFlagIcon(bcp47Code: row.nativeLanguageBcp47, size: 18)
                            Text(nativeText)
                                .font(.system(size: 17, weight: .semibold))
                                .foregroundStyle(homeMuted)
                                .lineLimit(2)
                                .minimumScaleFactor(0.72)
                        }
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)

                VStack(alignment: .trailing, spacing: 2) {
                    homePartOfSpeechLabel
                    if let onReview {
                        Button {
                            onReview(row)
                        } label: {
                            Label("Review", systemImage: "play.fill")
                                .labelStyle(.iconOnly)
                                .font(.system(size: 12, weight: .bold))
                                .foregroundStyle(appSettings.currentAccent)
                                .padding(.horizontal, 10)
                                .padding(.vertical, 7)
                                .background(homeSoft, in: Capsule())
                                .frame(minWidth: 44, minHeight: 44)
                                .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        .disabled(isLocked)
                        .accessibilityLabel("Review \(row.displayWord)")
                        .accessibilityIdentifier("learn.reviewWord.\(row.id)")
                    }
                    if isLocked {
                        lockedChip
                    }
                }
                .frame(width: 96, alignment: .trailing)
            }
            .frame(minHeight: 74)
            .opacity(isLocked ? 0.55 : 1)

            if shouldShowExpandedContent {
                expandedSection
                    .padding(.top, 12)
            }
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(homeCard)
        .clipShape(RoundedRectangle(cornerRadius: 18, style: .continuous))
        .shadow(color: .black.opacity(0.045), radius: 12, x: 0, y: 6)
        .contentShape(Rectangle())
        .onTapGesture {
            guard !isLocked, expansion != nil else { return }
            if expansion?.wrappedValue == row.id {
                expansion?.wrappedValue = nil
            } else {
                expansion?.wrappedValue = row.id
            }
        }
    }

    private var homeWordTitle: some View {
        homeWordText
    }

    private var homeWordText: some View {
        Text(row.displayWord)
            .font(.system(size: 23, weight: .heavy))
            .foregroundStyle(homeInk)
            .lineLimit(2)
            .minimumScaleFactor(0.72)
    }

    @ViewBuilder
    private var homePronunciationChip: some View {
        if let p = row.pronunciation?.trimmingCharacters(in: .whitespacesAndNewlines), !p.isEmpty {
            HStack(spacing: 5) {
                Text(ipaDisplayText(from: p))
                    .font(.system(size: 13, weight: .bold))
                    .italic()
                    .lineLimit(1)
                    .minimumScaleFactor(0.7)

                Button {
                    pronunciation.speak(text: row.displayWord, learningLanguageBCP47: row.learningLanguageBcp47)
                } label: {
                    Group {
                        if pronunciation.isSpeaking {
                            ProgressView()
                                .scaleEffect(0.58)
                        } else {
                            Image(systemName: "speaker.wave.2.fill")
                                .font(.system(size: 11, weight: .bold))
                        }
                    }
                    .frame(width: 18, height: 18)
                }
                .buttonStyle(.plain)
            }
            .foregroundStyle(homeMuted)
            .tint(homeMuted)
            .padding(.horizontal, 8)
            .padding(.vertical, 5)
            .background(homeSoft)
            .clipShape(Capsule())
        }
    }

    @ViewBuilder
    private var homePartOfSpeechLabel: some View {
        if let pos = row.partOfSpeech, !pos.isEmpty {
            Text(pos.uppercased())
                .font(.system(size: 11, weight: .bold))
                .tracking(1.6)
                .foregroundStyle(homeMuted.opacity(0.75))
                .multilineTextAlignment(.trailing)
                .lineLimit(2)
                .frame(maxWidth: 96, alignment: .trailing)
        }
    }

    private var cardSummary: some View {
        VStack(spacing: 10) {
            HStack {
                if let pos = row.partOfSpeech, !pos.isEmpty {
                    Text(pos.lowercased())
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundStyle(theme.palette.accent)
                        .padding(.horizontal, 8)
                        .padding(.vertical, 4)
                        .background(theme.palette.accentBadgeBackground)
                        .clipShape(Capsule())
                }
                Spacer(minLength: 0)
            }

            Spacer(minLength: 0)

            VStack(spacing: 10) {
                Text(row.displayWord)
                    .font(.system(size: 20, weight: .bold))
                    .foregroundStyle(theme.palette.foreground)
                    .multilineTextAlignment(.center)
                    .frame(maxWidth: .infinity, alignment: .center)

                let nativeText = previewTranslations.joined(separator: ", ")
                if !nativeText.isEmpty {
                    HStack(alignment: .center, spacing: 8) {
                        LanguageFlagIcon(bcp47Code: row.nativeLanguageBcp47, size: 18)
                        Text(nativeText)
                            .font(.system(size: translationFontSize(for: nativeText), weight: .medium))
                            .foregroundStyle(theme.palette.mutedForeground)
                            .multilineTextAlignment(.center)
                    }
                    .frame(maxWidth: .infinity, alignment: .center)
                }
            }

            Spacer(minLength: 0)

            HStack {
                Spacer(minLength: 0)
                pronunciationChip
            }
        }
        .frame(maxWidth: .infinity)
        .frame(minHeight: 128)
    }

    private var previewTranslations: [String] {
        Array(row.displayTranslations.prefix(3))
    }

    private var pronunciationChip: some View {
        Button {
            pronunciation.speak(text: row.displayWord, learningLanguageBCP47: row.learningLanguageBcp47)
        } label: {
            HStack(spacing: 6) {
                if let p = row.pronunciation?.trimmingCharacters(in: .whitespacesAndNewlines), !p.isEmpty {
                    Text(ipaDisplayText(from: p))
                        .font(.system(size: 14, weight: .medium))
                        .foregroundStyle(theme.palette.primary)
                        .lineLimit(1)
                        .minimumScaleFactor(0.7)
                }

                Group {
                    if pronunciation.isSpeaking {
                        ProgressView()
                            .scaleEffect(0.65)
                    } else {
                        Image(systemName: "speaker.wave.2.fill")
                            .font(.system(size: 12))
                    }
                }
                .frame(width: 22, height: 22)
                .foregroundStyle(theme.palette.primary.opacity(0.72))
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 6)
            .background(theme.palette.primary.opacity(0.08))
            .overlay(
                RoundedRectangle(cornerRadius: 8, style: .continuous)
                    .stroke(theme.palette.border.opacity(0.45), lineWidth: 1)
            )
            .clipShape(RoundedRectangle(cornerRadius: 8, style: .continuous))
            .contentShape(RoundedRectangle(cornerRadius: 8, style: .continuous))
        }
        .buttonStyle(.plain)
    }

    @ViewBuilder
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
                if let onEdit {
                    Button {
                        onEdit(row)
                    } label: {
                        HStack(spacing: 6) {
                            Image(systemName: "pencil")
                            Text("Edit")
                        }
                        .font(.system(size: 15, weight: .medium))
                        .foregroundStyle(theme.palette.mutedForeground)
                    }
                    .buttonStyle(.plain)
                }
                if let onDelete {
                    Button {
                        onDelete(row)
                    } label: {
                        HStack(spacing: 6) {
                            Image(systemName: "trash")
                            Text("Delete")
                        }
                        .font(.system(size: 15, weight: .medium))
                        .foregroundStyle(theme.palette.mutedForeground)
                    }
                    .buttonStyle(.plain)
                }
            }
            .padding(.top, 4)
        }
        .padding(.top, 8)
    }

    private func translationFontSize(for text: String) -> CGFloat {
        wordCount(in: text) <= 2 ? 18 : 16
    }

    private func wordCount(in text: String) -> Int {
        text
            .split { $0.isWhitespace || $0.isNewline }
            .count
    }

    private func capitalizeFirst(_ text: String) -> String {
        guard let first = text.first else { return text }
        return String(first).localizedUppercase + text.dropFirst()
    }

    private func ipaDisplayText(from text: String) -> String {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return trimmed }
        if trimmed.hasPrefix("/") && trimmed.hasSuffix("/") {
            return trimmed
        }
        return "/\(trimmed.trimmingCharacters(in: CharacterSet(charactersIn: "/")))/"
    }

    private var trimmedUserNotes: String? {
        let trimmed = row.userNotes?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        return trimmed.isEmpty ? nil : trimmed
    }

    private func flashcardSetTrailingIcon(_ name: String) -> String {
        let n = name.lowercased()
        if n.contains("work") { return "briefcase.fill" }
        if n.contains("daily") || n.contains("life") { return "house.fill" }
        if n.contains("food") { return "fork.knife" }
        if n.contains("travel") { return "airplane" }
        if n.contains("archive") { return "archivebox.fill" }
        return "folder.fill"
    }

    private var monogram: String {
        let trimmed = row.displayWord.trimmingCharacters(in: .whitespacesAndNewlines)
        guard let first = trimmed.first else { return "?" }
        return String(first).uppercased()
    }
}
