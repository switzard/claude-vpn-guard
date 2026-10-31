# Audit — Claude VPN Guard 1.0.0

Date: 2026-10-31  
Machine: Windows 10/11 (local owner PC)  
Build: `publish/ClaudeVpnGuard.exe` (single-file, win-x64, self-contained)

## Code review

| Area | Result | Notes |
|------|--------|-------|
| Process control | PASS | `NtSuspendProcess` / `NtResumeProcess` via `OpenProcess`; optional kill mode |
| Claude targeting | PASS | Process name `claude` / `Claude`; classifies Desktop (WindowsApps) vs Code (npm/claude-code) |
| Cursor excluded | PASS | Cursor.exe not in target set |
| Tunnel plugins | PASS | Happ, Hiddify, v2rayN, Clash Verge, Amnezia, WireGuard, Windscribe, Outline, Nekoray |
| False positives | PASS | Radmin / Hyper-V / VirtualBox adapters excluded |
| Settings | PASS | `%AppData%\ClaudeVpnGuard\settings.json` |
| Journal | PASS | `%AppData%\ClaudeVpnGuard\events.jsonl` |
| Telemetry | PASS | No app analytics; only optional public geo lookup (`ipwho.is`) |
| Secrets | PASS | No tokens, no Anthropic credentials stored |
| Privileges | PASS | Runs as current user; WMI watch may fall back to polling |
| Live exe start | PASS | Process stays up; journal writes `boot`; WMI Access denied → polling only (acceptable) |

## Automated tests

```
dotnet test -c Release
Passed: 5, Failed: 0
```

Coverage: Happ detection, Radmin exclusion, TunnelMonitor enablement, Clash probe, path classification.

## Live smoke (this machine)

| Check | Result |
|-------|--------|
| Happ processes (`Happ`, `happd`, `xray`) | Present |
| Adapter `happ-xray` Up | Present |
| Expected Happ healthy | **True** |
| Exit geo via ipwho.is | **NL** `31.77.28.154` |
| Published exe size | ~155 MB self-contained |
| Unit tests | Green |

### Manual behavior checklist

1. Start Guard with Happ connected → status should show Happ / exit country.
2. Set target country to current exit (e.g. NL) + strict → Claude allowed.
3. Disconnect Happ → Claude Code/Desktop processes suspend within ~1s; balloon reminder.
4. Reconnect Happ + matching location → processes resume.
5. Change target to another country while VPN stays up → reminder + block in strict mode.
6. Close main window → app stays in tray; Exit from tray quits.

## Residual risks

- System-proxy-only modes (no TUN adapter) may need the client process heuristics; enable the matching provider.
- Store Claude Desktop may spawn multiple `Claude.exe` PIDs — all are suspended.
- Geo APIs can rate-limit; Guard keeps last known country on failure and stays strict (blocks) until refresh succeeds.
- Suspended processes can still hold old sockets briefly; prefer reconnect after resume if a request was in flight.

## Verdict

**GREEN** — safe to polish UI docs and publish source + Release exe.
