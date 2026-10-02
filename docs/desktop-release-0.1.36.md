# Desktop release 0.1.36 — Windows

Scope: release candidate for the light-theme sidebar accent update. Green uses a dark green sidebar and Red uses dark burgundy; Blue and Dark retain their existing sidebar palettes. The package and lockfile root version are 0.1.36, with dependencies unchanged.

Validation on 2026-10-01: `npm test` passed 116/116 with a checkout-local TEMP/TMP directory; `npm run build` passed TypeScript and production compilation. The final NSIS installer and portable executable were built with `electron-builder --win nsis portable --x64 --publish never` after `npm ci` in the isolated checkout. The packaged `win-unpacked/Owl AI.exe` opened in an isolated profile; a Playwright check saved Green, Red, and Blue light accents, verified sidebar backgrounds of `rgb(16, 53, 47)`, `rgb(52, 26, 41)`, and `rgb(19, 35, 64)`, and confirmed saved Red after restart. Packaged metadata reports 0.1.36. The approved local desktop OAuth configuration was embedded and its source file remains ignored; no credential values are recorded here.

| Candidate file | Bytes | SHA-256 |
| --- | ---: | --- |
| `release/Owl-AI-Setup-0.1.36-x64.exe` | 147,365,048 | `B9294EA7445F3ED0850D01704E9ECE6C63F3D94CD0109711419E33B4B43600D4` |
| `release/Owl-AI-Portable-0.1.36-x64.exe` | 147,146,650 | `08B771F287EA2ED01CD8769855AAE6E6B44EA9AA5F24954516F4867994525355` |
| `release/win-unpacked/Owl AI.exe` | 246,201,856 | `4728798C18C4D3B042BED8CD170BA6CDB7FB84794A35724AEC34B715594CFE35` |

All three executables report file version 0.1.36. The installer and portable wrapper were built but their visible launch/install flows were not exercised. Interactive Google sign-in was not exercised. The executables are unsigned. No release was published by these local checks.
