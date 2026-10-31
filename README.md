# Claude VPN Guard

Windows tray app that keeps **Claude Code** and **Claude Desktop** offline when your VPN/proxy tunnel is down — and reminds you to enable a **specific exit location**.

![status](https://img.shields.io/badge/platform-Windows%20x64-2EC4B6)
![stack](https://img.shields.io/badge/.NET-8%20WPF-E8EEF2?labelColor=0E1114)
![license](https://img.shields.io/badge/license-MIT-8B9AA5?labelColor=0E1114)

> Network hygiene tool. You are responsible for complying with [Anthropic Terms](https://www.anthropic.com/legal/consumer-terms) and local law.

## Why

If the tunnel drops for a second, Claude may briefly use your real IP. Guard:

1. Detects popular VPN/proxy clients
2. Blocks / suspends Claude until the tunnel is healthy
3. Checks exit country and reminds you to switch to your pinned location
4. Journals launches, drops, geo changes, suspend/resume

## Supported tunnels

| Client | Detection |
|--------|-----------|
| Happ | `happ-xray` adapter + `Happ`/`happd`/`xray` |
| Hiddify | app + `sing-box` / TUN |
| v2rayN | app + core / TUN |
| Clash Verge Rev | app + `mihomo` / TUN |
| AmneziaVPN / AmneziaWG | Amnezia adapters / processes |
| WireGuard | WireGuard tunnel adapters |
| Windscribe | Windscribe adapter / service |
| Outline | Outline + core |
| Nekoray / NekoBox | app + `sing-box` |

Radmin / Hyper-V / VirtualBox adapters are ignored.

## Targets

- Claude Code (`claude.exe` from npm `@anthropic-ai/claude-code`)
- Claude Desktop (Store / WindowsApps `Claude.exe`)
- **Not** Cursor

## Features

- VPN killswitch with **suspend** (default) or **kill**
- WMI start-watch + polling fallback
- Location lock + toast reminders
- Strict mode: Claude stays blocked until country matches
- Provider toggles
- Autostart for Guard; scan/disable Claude Run-key entries
- Account hygiene panel (community signals, unverified)
- Dark control-room UI + tray status colors

## Install

1. Download `ClaudeVpnGuard.exe` from [Releases](https://github.com/switzard/claude-vpn-guard/releases)
2. Run it (current user is enough)
3. Set **Target country** to your usual node (example: `NL`)
4. Enable **Strict location**
5. Connect Happ (or another supported client), confirm exit geo, then open Claude

Settings live in `%AppData%\ClaudeVpnGuard\`.

## Build

```powershell
dotnet test -c Release
dotnet publish ClaudeVpnGuard\ClaudeVpnGuard.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

## Docs

- [AUDIT.md](AUDIT.md) — code + smoke audit
- [RESEARCH.md](RESEARCH.md) — what Claude observes + unverified ban signals
- [docs/](docs/) — GitHub Pages landing

## Disclaimer

This software does **not** help with KYC fraud, payment spoofing, multi-accounting, or Usage Policy bypass. It only manages local process state relative to tunnel/geo health.
