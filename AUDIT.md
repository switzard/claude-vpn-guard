# Audit — Claude VPN Guard 1.1.0

Date: 2026-10-31  
Build: `publish/ClaudeVpnGuard.exe` (win-x64 single-file, self-contained)  
Tests: `dotnet test` — **5/5 passed**

## Independent UI / product audit

| Area | Result | Evidence |
|------|--------|----------|
| Frameless chrome | PASS | WindowStyle=None + custom titlebar |
| Mac traffic lights | PASS | Close / Minimize / Style (green) |
| Win caption mode | PASS | Green opens macOS / Windows menu; persisted in settings |
| Open animation | PASS | Window Opacity + `.shell.boot-in` |
| Close animation | PASS | `.shell.boot-out` then Hide/Close |
| App icon | PASS | `Assets/app.ico` ApplicationIcon + window Icon |
| VPN toggles | PASS | `.prov-toggle` switches (no upside-down checks) |
| Real events/graphs | PASS | Journal + GuardService sparks |
| Recommendations | PASS | Gaps + unverifiable only |
| Monitoring tabs | PASS | Processes / Events / Network |
| Glass selects | PASS | Event filter, language, country, mode |
| i18n RU/EN/ZH | PASS | `i18n.js` + settings |
| Autostart + sound | PASS | Registry + balloon + optional WAV |
| Settings persist | PASS | autosave on toggle/chrome/lang |
| Happ live | PASS | NL exit, healthy adapter |
| Unit tests | PASS | 5/5 |

## Residual (non-blocking)

- WMI Access denied → polling (environment ACL, not a regression)
- Monitoring sub-panes are read-only views (no fake data)
- About “updates/docs” stubs intentionally omitted

## Minimize animation fix — 2026-10-31

| Issue | Cause | Fix |
|-------|-------|-----|
| Double fade / flash | CSS `boot-min` + WPF Opacity both ran | Host only minimizes; CSS owns the motion |
| Stuck invisible UI | `FillBehavior` / `forwards` left opacity 0 | `resetShellMotion` + `FillBehavior.Stop` |
| Restore flash | FadeIn on every Normal | FadeIn only when leaving Minimized |
| Blurry glitch | `filter: blur` on WebView2 | Removed blur from minimize keyframes |

## Verdict

**GREEN — ship as main (v1.1.0)**
