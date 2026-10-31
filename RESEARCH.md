# Research notes (community / unverified)

Sources include forum reports, Telegram channels, and anecdotal write-ups. **None of this is confirmed by Anthropic.** Treat as risk hygiene, not fact.

## What Claude Code can observe natively on Windows

Claude Code is not an EDR. On Windows it typically:

- Runs as `claude.exe` and spawns `powershell.exe` for shell tools (command lines visible to OS security products).
- Performs Read/Write/WebFetch inside its own process (no child process for those tools).
- Sends operational usage telemetry toward Anthropic endpoints.
- Identifies its helpers by binary path, not display name.
- Ignores `CLAUDE_CODE_PROCESS_WRAPPER` on Windows.

Guard therefore controls **process lifetime / suspension** relative to tunnel health, rather than trying to hide from Claude.

## Reported trust / ban signals

### Soft trust hits (reported)

| Signal | Guard response |
|--------|----------------|
| VPN country hopping same day | Location lock + change alerts |
| VPN drop exposing real IP | Killswitch suspend/kill |
| Burst quota burn | Advice only |
| Multi-device thrash | Advice only |
| Fresh throwaway email | Advice only |
| Linux-only web usage | Advice only |

### Hard ban signals (reported)

| Signal | Guard response |
|--------|----------------|
| Unsupported-region IP flash | Killswitch |
| KYC with unsupported ID | **Out of scope** |
| Card country ≠ VPN exit | Advice only (cannot fix payments) |
| Usage Policy / jailbreak abuse | **Out of scope** |
| Account sharing / token resale | **Out of scope** |

## What can actually be mitigated in software

Mitigable with this app:

1. Keep a tunnel up before Claude starts.
2. Suspend Claude immediately on tunnel drop.
3. Pin one exit country and remind/block on mismatch.
4. Watch Run-key autostart for Claude and disable it on demand.
5. Journal network/process events for the user.

Not mitigable (and not implemented):

- Identity / KYC fraud
- Payment instrument mismatches
- Multi-accounting
- Policy jailbreaks
- Guaranteeing Anthropic will not ban an account

## Practical recommendation

Pick one reputable exit country, enable Happ (or another supported client), turn on **Strict location**, start Guard, confirm geo, then open Claude.
