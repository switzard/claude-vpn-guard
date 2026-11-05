# Changelog

## v2.2.0 — V2 · Session Passport · Incident Freeze · Guard HUD

### Revolutionary Big3

- **Session Passport** — exit continuity fingerprint (`Stable` / `Shifted` / `Broken`), reanchor, Home card  
- **Incident Freeze Pack** — mid-session VPN-drop / geo-break snapshots, Incidents page, ack workflow  
- **Guard HUD** — always-on-top mini window; × hides without disabling; tray syncs Settings toggle  

### Killswitch & tunnel

- Deny-first WMI allow; temporary allow 15m slice / 45m wall-clock + revoke (Home, tray, HUD)  
- Egress debounce (×2) before demoting Up  
- Stricter Amnezia / Clash / Hiddify / Custom TUN matching  
- Geo-break only when geo is reliable; ReminderGeoUnreliable when tunnel Up but geo flaky  

### UI / UX

- Lucide line icons (Unslop); badge-check Passport title  
- Incident nav badge hidden at `0`  
- Compact HUD (no fingerprint hash, denser layout)  
- Recovery banner + honest StatusSummary copy (ru / en / zh)  

### Packaging

- Self-contained `win-x64` publish · version **2.2.0**

---

## v1.1.0

Stuck-Claude resume fixes, single-instance guard, geo consensus, native minimize.

## v1.0.0

Initial public killswitch + location lock.
