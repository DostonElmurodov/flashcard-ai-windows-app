# Light accent sidebar palette

When the effective theme is Light, the Green light accent gives the sidebar a dark green palette and the Red light accent gives it a dark burgundy palette. The sidebar surface, inset cards, borders, labels, navigation, hover, collection markers, account area, and keyboard focus outline use coordinated colors. The active navigation color continues to use the selected accent.

The default Blue light accent retains the existing navy sidebar. Dark theme keeps its separate accent choices and existing sidebar palette. System appearance uses the chosen light accent when it resolves to Light. Preferences still apply when saved.

Verification on 2026-10-01:

- Windows and macOS source builds: `npm run build` passed, including TypeScript checks.
- Windows isolated Electron profile: `node scripts/native-theme-smoke.mjs` passed.
- Windows isolated Electron inspection: saved Green and Red, captured full Settings and sidebar screenshots, checked computed surface/card/border/label/hover/focus colors, restarted with Red saved, returned to Blue, inspected all three Dark accents, and resolved System to Light. The computed sidebar backgrounds were `rgb(16, 53, 47)` for Green, `rgb(52, 26, 41)` for Red, `rgb(19, 35, 64)` for Blue, and `rgb(17, 19, 24)` for all Dark accents.
- The shared CSS change is identical on both platforms; the pre-existing macOS titlebar rule remains platform-specific.

Native macOS UI and packaging require a Mac runner. No release or deployment was performed.
