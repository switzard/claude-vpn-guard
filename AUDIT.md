# Audit — Claude VPN Guard 2.0.0

Date: 2026-11-05  
Build: `dist/ClaudeVpnGuard-2.0.0-win-x64/ClaudeVpnGuard.exe`  
Tests: `dotnet test -c Release` — **9/9 passed**

## Step 1 bugfixes verified in code

| Bug | Status | Notes |
|-----|--------|-------|
| VPN ON while OFF / installed≠running | FIXED | Providers require live adapter; Degraded for client-only; live egress TCP/ping gate |
| Wrong geo / 0 ms ping | FIXED | 2ip + ipinfo + CF + ipwho + ip-api; ICMP ping for latency label |
| Stats always 0 | FIXED | Persist stats.json; count suspend + WMI blocks |
| No Claude relaunch after VPN | FIXED | Opt-in `RelaunchClaudeOnVpnRestore` |
| GDI+ tray spam | FIXED | Icon cache + DestroyIcon; skip recreate if color unchanged |
| Settings deep-links | FIXED | Unique panes: General / Sounds / Location / Advanced |
| Slider texture bleed | FIXED | z-index + search input without chevron |
| Star → Claude icon | FIXED | `Icons.claude()` |
| Sounds meme + 3 + volume | FIXED | Assets/Sounds + scaler |
| Version | FIXED | csproj 2.0.0 → About via `appVersion` |
| False flaps | FIXED | TunnelConfirmSamples debounce |
| Full countries | FIXED | Expanded `KnownCountries` |
| Windows toasts | SHIPPED | `WindowsToastService` + balloon fallback |

## Independent residual (non-blocking)

| Severity | Item | Suggestion |
|----------|------|------------|
| Medium | Proxy-only modes (system proxy without TUN) always Degraded | Optional future “trust system proxy” setting with stronger live checks |
| Medium | Store Claude path relaunch may need `shell:AppsFolder` | Improve Desktop relaunch discovery |
| Low | Toast + balloon can both fire | Setting to prefer toast-only |
| Low | WMI Access denied → polling | Environment ACL |

## Redesign gate

Mock: `docs/v2-redesign-mock.jpg` — **awaiting owner «подтверждаю»**. Until then UI shell remains current menu.

## Unit tests

9 provider/monitor/classification tests covering Happ up/degraded, v2rayN app-only, Clash without adapter, TunnelMonitor states.
