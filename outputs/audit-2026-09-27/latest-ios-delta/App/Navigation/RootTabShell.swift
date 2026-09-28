import SwiftUI

/// Top-level shell hosting the floating tab bar with a translucent container.
///
/// Tabs:
///  1. Learn   → `DashboardView` (switchable tab)
///  2. Library → `LibraryView` (switchable tab)
///  3. Pro     → `ProductPaywallPage` as a full page (tab bar stays visible)
///  4. `+`     → pushes `CreateFlashcardSetPage` (full page)
struct RootTabShell: View {
    let composition: AppComposition
    @Binding private var openSettingsOnAppear: Bool
    private let paywallActionsOverride: ProductPaywallActions?

    @EnvironmentObject private var theme: ThemeManager
    @EnvironmentObject private var appSettings: AppSettingsStore
    @EnvironmentObject private var storeKit: StoreKitService

    @State private var selection: MainTab = .learn
    @State private var showCreatePage = false
    @State private var dashboardAddFlowBlocksNavigation = false

    init(
        composition: AppComposition,
        openSettingsOnAppear: Binding<Bool> = .constant(false),
        paywallActions: ProductPaywallActions? = nil
    ) {
        self.composition = composition
        _openSettingsOnAppear = openSettingsOnAppear
        paywallActionsOverride = paywallActions
    }

    private var repository: WordRepository { composition.words }

    var body: some View {
        ZStack(alignment: .bottom) {
            appSettings.currentBackground
                .ignoresSafeArea()

            // Selected tab content.
            Group {
                switch selection.destination {
                case .learningDashboard:
                    DashboardView(
                        repository: repository,
                        reviewDependencies: composition.makeReviewSessionDependencies(),
                        openSettingsOnAppear: $openSettingsOnAppear,
                        isAddFlowBlockingRootNavigation:
                            $dashboardAddFlowBlocksNavigation
                    )
                case .library:
                    LibraryView(
                        repository: repository,
                        publicSets: composition.publicSets,
                        secondaryReview: composition.secondaryReview
                    )
                case .pro:
                    ProductPaywallPage(
                        bottomContentInset: 96,
                        loadsProductsOnAppear: true,
                        actions: paywallActionsOverride
                            ?? .live(storeKit: storeKit),
                        onClose: { selection = .learn },
                        onPurchaseCompleted: { selection = .learn }
                    )
                }
            }

            // Floating tab bar.
            FloatingTabBar(
                selection: $selection,
                onAdd: { showCreatePage = true }
            )
            .padding(.horizontal, 14)
            .padding(.bottom, 2)
            .allowsHitTesting(!dashboardAddFlowBlocksNavigation)
            .accessibilityHidden(dashboardAddFlowBlocksNavigation)
        }
        .navigationDestination(isPresented: $showCreatePage) {
            CreateFlashcardSetPage(
                repository: repository,
                publicSets: composition.publicSets,
                secondaryReview: composition.secondaryReview
            )
                .environmentObject(theme)
                .environmentObject(appSettings)
        }
        .toolbar(.hidden, for: .navigationBar)
    }
}
