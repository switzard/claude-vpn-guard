(function () {
  const $ = (s) => document.querySelector(s);
  function post(cmd, payload = {}) {
    try {
      if (window.chrome?.webview)
        window.chrome.webview.postMessage(JSON.stringify({ cmd, ...payload }));
    } catch (_) { /* preview */ }
  }

  const labels = {
    ru: {
      stable: "Стабилен", shifted: "Сдвиг", broken: "Сломан",
      vpnUp: "VPN Up", vpnIdle: "VPN Idle", vpnDown: "VPN Down",
      claudeOk: "Claude OK", claudeBlocked: "Claude блок",
      check: "Проверить VPN", allow: "15 мин (риск)", revoke: "Отозвать", ack: "Подтвердить", open: "Открыть"
    },
    en: {
      stable: "Stable", shifted: "Shifted", broken: "Broken",
      vpnUp: "VPN Up", vpnIdle: "VPN Idle", vpnDown: "VPN Down",
      claudeOk: "Claude OK", claudeBlocked: "Claude Blocked",
      check: "Check VPN", allow: "15m (risk)", revoke: "Revoke", ack: "Ack", open: "Open"
    }
  };

  let lang = "ru";
  function L() {
    if (lang === "zh") return labels.ru; // zh falls back to RU chrome until full HUD zh pack
    return labels[lang] || labels.ru;
  }

  function localizeState(st) {
    const k = (st || "").toLowerCase();
    return L()[k] || st || "—";
  }

  function apply(data) {
    if (!data) return;
    if (data.lang === "en" || data.lang === "ru") lang = data.lang;
    const t = L();
    $("#btnCheck").textContent = t.check;
    const allowActive = (data.allowSeconds | 0) > 0;
    const allowBtn = $("#btnAllow");
    if (allowBtn) {
      allowBtn.textContent = allowActive ? t.revoke : t.allow;
      allowBtn.dataset.mode = allowActive ? "revoke" : "allow";
    }
    $("#btnAck").textContent = t.ack;
    $("#btnMain").textContent = t.open;

    const st = (data.passportState || "Broken").toLowerCase();
    const pass = $("#pass");
    pass.textContent = localizeState(data.passportState || "Broken");
    pass.className = "pill pass-lead " + st;
    const vpn = $("#vpn");
    if (data.vpnUp) { vpn.textContent = t.vpnUp; vpn.className = "pill ok"; }
    else if (data.vpnDegraded) { vpn.textContent = t.vpnIdle; vpn.className = "pill shifted"; }
    else { vpn.textContent = t.vpnDown; vpn.className = "pill"; }
    const cl = $("#claude");
    if (data.guardEnabled === false) {
      cl.textContent = lang === "en" ? "Guard paused" : "Пауза Guard";
      cl.className = "pill dim";
    } else if (data.claudeAllowed) {
      const left = data.allowSeconds > 0
        ? ` ${String(Math.floor(data.allowSeconds / 60)).padStart(2, "0")}:${String(data.allowSeconds % 60).padStart(2, "0")}`
        : "";
      cl.textContent = t.claudeOk + left;
      cl.className = "pill ok";
    } else {
      cl.textContent = t.claudeBlocked;
      cl.className = "pill";
    }
    // Country + short status only — no fingerprint hash in HUD
    $("#sum").textContent = [data.country, data.summary].filter(Boolean).join(" · ") || "—";
    const inc = $("#incident");
    if (data.openIncident) {
      inc.hidden = false;
      // Drop raw kind prefix (vpn-drop / geo-break) — summary is enough
      inc.textContent = data.openIncident.summary || data.openIncident.kind || "";
      $("#btnAck").disabled = false;
    } else {
      inc.hidden = true;
      $("#btnAck").disabled = true;
    }
  }

  $("#drag")?.addEventListener("mousedown", e => {
    if (e.button === 0 && e.target.id !== "btnClose") post("hudDrag");
  });
  // Hide only — do not disable HudEnabled (settings toggle owns that)
  $("#btnClose")?.addEventListener("click", () => post("hudHide"));
  $("#btnCheck")?.addEventListener("click", () => post("checkVpn"));
  $("#btnAllow")?.addEventListener("click", () => {
    const mode = $("#btnAllow")?.dataset.mode;
    post(mode === "revoke" ? "revokeAllow" : "allowLaunch");
  });
  $("#btnAck")?.addEventListener("click", () => post("ackLatest"));
  $("#btnMain")?.addEventListener("click", () => post("openMain"));

  if (window.chrome?.webview) {
    window.chrome.webview.addEventListener("message", ev => {
      try {
        const data = typeof ev.data === "string" ? JSON.parse(ev.data) : ev.data;
        apply(data);
      } catch (_) { /* ignore */ }
    });
    post("ready");
  }
})();
