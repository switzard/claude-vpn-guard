# Claude VPN Guard

**v2** — Windows killswitch that keeps **Claude Code** and **Claude Desktop** offline when your VPN tunnel is down, and locks you to a pinned exit country.

[![platform](https://img.shields.io/badge/platform-Windows%20x64-2EC4B6)](https://github.com/switzard/claude-vpn-guard/releases)
[![.NET](https://img.shields.io/badge/.NET-8%20WPF-E8EEF2?labelColor=0E1114)](https://dotnet.microsoft.com/)
[![version](https://img.shields.io/badge/version-2.2.0-2EC4B6?labelColor=0E1114)](https://github.com/switzard/claude-vpn-guard/releases/tag/v2.2.0)
[![license](https://img.shields.io/badge/license-MIT-8B9AA5?labelColor=0E1114)](LICENSE)

> Local network hygiene. You are responsible for complying with [Anthropic Terms](https://www.anthropic.com/legal/consumer-terms) and local law.

## What it does

| Piece | Role |
|-------|------|
| **Tunnel detect** | Live TUN/adapter health — not “VPN app installed” |
| **Killswitch** | Suspend / kill Claude while tunnel is Down or Degraded |
| **Exit lock** | Multi-source geo check + remind / block wrong country |
| **Session Passport** | Continuity fingerprint of the exit identity (`Stable` / `Shifted` / `Broken`) |
| **Incident Freeze Pack** | Forensic snapshot when VPN drops or geo breaks mid-session |
| **Guard HUD** | Compact always-on-top strip: passport · VPN · Claude · actions |

Optional: Windows Action Center toasts, sound presets, auto-relaunch Claude after VPN recovers, Custom TUN for long-tail clients.

## What’s new in v2

1. **Session Passport** — home card + shift timeline + reanchor  
2. **Incident Freeze Pack** — Incidents nav, open badge, acknowledge  
3. **Guard HUD** — tray / settings toggle, hide ≠ disable  
4. Cleaner UI (Unslop-style icons, honest recovery copy, no zero-noise badge)  
5. Stronger tunnel probes (Amnezia / Clash / Hiddify / Custom TUN Exactish)

Full notes: [CHANGELOG.md](CHANGELOG.md)

## Install

1. Download **`ClaudeVpnGuard-2.2.0-win-x64.zip`** from [Releases](https://github.com/switzard/claude-vpn-guard/releases/tag/v2.2.0)  
2. Unpack and run `ClaudeVpnGuard.exe`  
3. Set **Target country** → enable **Strict location**  
4. Connect a supported client → confirm exit geo → open Claude  

Settings: `%AppData%\ClaudeVpnGuard\`

## Supported tunnels

Happ · Hiddify · v2rayN · Clash Verge Rev · Amnezia · WireGuard · Windscribe · Outline · Nekoray · Proton · OpenVPN · Surfshark / Nord / Express · Shadowsocks · SoftEther · **Custom TUN**

Client open without a live tunnel → **Degraded**, not “VPN OK”.

## Targets

- Claude Code (`claude.exe` from npm)  
- Claude Desktop (Store / WindowsApps)  
- **Not** Cursor  

## Build

```powershell
dotnet test -c Release
dotnet publish ClaudeVpnGuard\ClaudeVpnGuard.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o dist\ClaudeVpnGuard-2.2.0
```

## Docs & site

- Product site: [switzard.github.io/claude-vpn-guard](https://switzard.github.io/claude-vpn-guard/)  
- [CHANGELOG.md](CHANGELOG.md) · [AUDIT.md](AUDIT.md) · [RESEARCH.md](RESEARCH.md)

## Disclaimer

Does **not** help with KYC fraud, payment spoofing, multi-accounting, or Usage Policy bypass. It only manages local process state relative to tunnel / geo health.
