import SwiftUI

enum MainTabDestination: Equatable {
    case learningDashboard
    case library
    case pro
}

/// The switchable destinations that live *inside* the tab shell. The add
/// (`+`) action pushes `CreateFlashcardSetPage` rather than switching tabs, so
/// it stays out of this enum; `Pro` is a real tab (full page, tab bar stays
/// visible) like Learn and Library.
enum MainTab: Hashable, CaseIterable {
    case learn
    case library
    case pro

    var label: String {
        switch self {
        case .learn: return "Learn"
        case .library: return "Library"
        case .pro: return "Pro"
        }
    }

    var systemImage: String {
        switch self {
        case .learn: return "graduationcap.fill"
        case .library: return "books.vertical.fill"
        case .pro: return "crown.fill"
        }
    }

    var destination: MainTabDestination {
        switch self {
        case .learn: return .learningDashboard
        case .library: return .library
        case .pro: return .pro
        }
    }
}

/// The six ways to add words, listed on `CreateFlashcardSetPage`.
///
/// `aiTranslate` and `manualEntry` are the two ends of the same task — typing a
/// word in — and differ only in who supplies the translation: the AI (network
/// call, counts against the free AI limit) or the user (fully offline).
enum AddOption: Hashable, CaseIterable {
    case aiTranslate
    case manualEntry
    case scanDocument
    case selectImage
    case pasteText
    case selectFiles

    var title: String {
        switch self {
        case .aiTranslate:  return "Add with AI"
        case .manualEntry:  return "Add words manually"
        case .scanDocument: return "Scan document"
        case .selectImage:  return "Select image"
        case .pasteText:    return "Paste text"
        case .selectFiles:  return "Select files"
        }
    }

    var subtitle: String? {
        switch self {
        case .aiTranslate:  return "Type a word and let AI translate it"
        case .manualEntry:  return "Type the word and translation yourself"
        case .scanDocument: return "Capture a page with the camera"
        case .selectImage:  return "Pick a photo from your library"
        case .pasteText:    return "Paste words you copied"
        case .selectFiles:  return ".txt, .csv, .tsv, .pdf"
        }
    }

    var systemImage: String {
        switch self {
        case .aiTranslate:  return "sparkles"
        case .manualEntry:  return "square.and.pencil"
        case .scanDocument: return "doc.viewfinder"
        case .selectImage:  return "photo"
        case .pasteText:    return "doc.on.clipboard"
        case .selectFiles:  return "folder"
        }
    }
}

/// Floating navigation with a 45%-opaque theme background so cards remain
/// visible around the buttons. The selected item keeps its capsule highlight.
struct FloatingTabBar: View {
    @EnvironmentObject private var appSettings: AppSettingsStore

    @Binding var selection: MainTab
    var onAdd: () -> Void

    private var ink: Color { appSettings.currentInk }

    var body: some View {
        HStack(spacing: 2) {
            ForEach(MainTab.allCases, id: \.self) { tab in
                tabButton(tab, icon: tab.systemImage, label: tab.label)
            }
            actionButton(icon: "plus", label: "Create", action: onAdd)
        }
        .padding(5)
        .frame(maxWidth: .infinity)
        .background {
            Capsule()
                .fill(appSettings.currentBackground.opacity(0.45))
                .allowsHitTesting(false)
        }
    }

    private func tabButton(_ tab: MainTab, icon: String, label: String) -> some View {
        let selected = selection == tab
        return Button {
            withAnimation(.spring(response: 0.34, dampingFraction: 0.78)) {
                selection = tab
            }
        } label: {
            pillContent(icon: icon, label: label, selected: selected)
        }
        .buttonStyle(.plain)
        .accessibilityLabel(label)
        .accessibilityAddTraits(selected ? .isSelected : [])
    }

    private func actionButton(icon: String, label: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            pillContent(icon: icon, label: label, selected: false)
        }
        .buttonStyle(.plain)
        .accessibilityLabel(label)
    }

    /// Unselected items are fully transparent; the selected item gets a
    /// soft, light capsule highlight behind just its own icon/label.
    private func pillContent(icon: String, label: String, selected: Bool) -> some View {
        VStack(spacing: 3) {
            Image(systemName: icon)
                .font(.system(size: 19, weight: .semibold))
            Text(label)
                .font(.system(size: 10, weight: .bold))
                .lineLimit(1)
                .minimumScaleFactor(0.8)
        }
        .foregroundStyle(ink.opacity(selected ? 0.95 : 0.62))
        .frame(maxWidth: .infinity)
        .frame(height: 52)
        .background(
            Capsule()
                .fill(selected ? Color.white.opacity(0.16) : Color.clear)
        )
    }
}
