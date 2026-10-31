/* Claude Code Guardian — UI bridge */
(function () {
  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => [...r.querySelectorAll(s)];

  const state = {
    page: "home",
    mon: "proc",
    countries: [
      ["NL", "Netherlands"], ["DE", "Germany"], ["US", "United States"], ["GB", "United Kingdom"],
      ["FR", "France"], ["FI", "Finland"], ["SE", "Sweden"], ["CH", "Switzerland"],
      ["PL", "Poland"], ["TR", "Turkey"], ["JP", "Japan"], ["SG", "Singapore"]
    ],
    lastToastKeys: new Set(),
    processes: [],
    network: [],
    skipProvRewrite: false
  };

  function post(cmd, payload = {}) {
    try {
      if (window.chrome?.webview)
        window.chrome.webview.postMessage(JSON.stringify({ cmd, ...payload }));
    } catch (_) { /* design preview */ }
  }

  function navigate(page) {
    state.page = page;
    $$(".nav-item").forEach(b => b.classList.toggle("active", b.dataset.page === page));
    $$(".page").forEach(p => p.classList.toggle("active", p.id === `page-${page}`));
  }

  function toast(type, title, body, time) {
    const host = $("#toasts");
    if (!host) return;
    const el = document.createElement("div");
    el.className = `toast ${type}`;
    const icon = window.Icons
      ? (type === "danger" ? window.Icons.alert() : type === "success" ? window.Icons.check() : window.Icons.info())
      : "!";
    el.innerHTML = `
      <div class="toast-ico">${icon}</div>
      <div class="toast-body">
        <div class="toast-top"><strong>${escapeHtml(title)}</strong><span>${escapeHtml(time || "")}</span></div>
        <div class="toast-msg">${escapeHtml(body || "")}</div>
      </div>`;
    host.prepend(el);
    requestAnimationFrame(() => el.classList.add("show"));
    setTimeout(() => { el.classList.remove("show"); setTimeout(() => el.remove(), 320); }, 5200);
    while (host.children.length > 4) host.lastElementChild.remove();
  }

  function escapeHtml(s) {
    return String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
  }

  function flagCss(code) {
    const map = {
      DE: "linear-gradient(180deg,#000 33%,#dd0000 33% 66%,#ffce00 66%)",
      NL: "linear-gradient(180deg,#ae1c28 33%,#fff 33% 66%,#21468b 66%)",
      US: "linear-gradient(180deg,#b22234 14%,#fff 14% 28%,#b22234 28% 42%,#fff 42% 56%,#b22234 56% 70%,#fff 70% 84%,#b22234 84%)",
      GB: "#012169", FR: "linear-gradient(90deg,#002395 33%,#fff 33% 66%,#ed2939 66%)",
      FI: "#003580", SE: "#006aa7", CH: "#ff0000", PL: "linear-gradient(180deg,#fff 50%,#dc143c 50%)",
      TR: "#e30a17", JP: "#fff", SG: "linear-gradient(180deg,#ed2939 50%,#fff 50%)"
    };
    return map[code] || "linear-gradient(135deg,#3b82f6,#1e3a8a)";
  }

  function renderEvents(list, mount) {
    const root = $(mount);
    if (!root) return;
    if (!list?.length) {
      root.innerHTML = `<div class="empty">${window.I18n ? I18n.t("noEvents") : "Событий пока нет"}</div>`;
      return;
    }
    root.innerHTML = list.map(e => {
      const kind = (e.kind || "").toLowerCase();
      let tone = "info", iconName = "info";
      if (["vpn", "error", "launch"].includes(kind) && /блок|отключ|drop|fail|error|denied/i.test(e.message || "")) {
        tone = "err"; iconName = "alert";
      } else if (kind === "geo" || kind === "remind") { tone = "warn"; iconName = "alert"; }
      else if (kind === "boot" || kind === "audit" || /подключ|восстанов|OK|запущен/i.test(e.message || "")) {
        tone = "ok"; iconName = "check";
      }
      const icon = window.Icons?.[iconName] ? window.Icons[iconName]() : "";
      const time = e.timestamp ? new Date(e.timestamp).toLocaleTimeString(undefined, { hour: "2-digit", minute: "2-digit", second: "2-digit" }) : "";
      const parts = (e.message || "").split("—");
      const msg = parts[0]?.trim() || e.message || "";
      const reason = parts.slice(1).join("—").trim();
      const color = tone === "err" ? "red" : tone === "ok" ? "green" : tone === "warn" ? "yellow" : "blue";
      return `<div class="event">
        <span class="tl" style="color:var(--${color})"></span>
        <span class="time">${escapeHtml(time)}</span>
        <span class="ev-ico ${tone}">${icon}</span>
        <div class="ev-body"><div class="msg">${escapeHtml(msg)}</div>${reason ? `<div class="reason">${escapeHtml(reason)}</div>` : ""}</div>
        <span class="tl-end" style="background:var(--${color})"></span>
      </div>`;
    }).join("");
  }

  function drawLineChart(points) {
    const path = $("#linePath"), fill = $("#lineFill"), dots = $("#lineDots");
    if (!path || !points?.length) return;
    const w = 280, h = 90, pad = 8;
    const max = Math.max(...points, 1), min = Math.min(...points, 0);
    const span = Math.max(max - min, 1);
    const coords = points.map((v, i) => {
      const x = pad + (i / Math.max(points.length - 1, 1)) * (w - pad * 2);
      const y = pad + (1 - (v - min) / span) * (h - pad * 2);
      return [x, y];
    });
    const d = coords.map((c, i) => `${i ? "L" : "M"}${c[0].toFixed(1)} ${c[1].toFixed(1)}`).join(" ");
    path.setAttribute("d", d);
    fill.setAttribute("d", `${d} L${coords.at(-1)[0]} ${h} L${coords[0][0]} ${h} Z`);
    dots.innerHTML = coords.map(c => `<circle cx="${c[0]}" cy="${c[1]}" r="3" fill="#3B82F6" stroke="#0B1224" stroke-width="1.4"/>`).join("");
  }

  function drawWave(points) {
    const path = $("#wavePath"), fill = $("#waveFill");
    if (!path) return;
    const pts = (points?.length ? points : [18, 22, 16, 24, 20, 26, 18, 22]).map(Number);
    const w = 320, h = 60, pad = 4;
    const max = Math.max(...pts, 1), min = Math.min(...pts, 0);
    const span = Math.max(max - min, 1);
    const coords = pts.map((v, i) => {
      const x = (i / Math.max(pts.length - 1, 1)) * w;
      const y = pad + (1 - (v - min) / span) * (h - pad * 2 - 8);
      return [x, y];
    });
    const d = coords.map((c, i) => `${i ? "L" : "M"}${c[0].toFixed(1)} ${c[1].toFixed(1)}`).join(" ");
    path.setAttribute("d", d);
    fill.setAttribute("d", `${d} L${w} ${h} L0 ${h} Z`);
  }

  function setRing(rating, tone) {
    const ring = $("#ratingRing");
    const wrap = $(".ring-wrap");
    if (!ring) return;
    const pct = Math.max(0, Math.min(100, rating));
    // pathLength=100 → dashoffset is simply remaining percent
    ring.setAttribute("stroke-dasharray", "100");
    ring.style.strokeDashoffset = `${100 - pct}`;
    const grads = {
      low: ["#86efac", "#4ade80", "#22c55e"],
      mid: ["#fde68a", "#fbbf24", "#f59e0b"],
      high: ["#fdba74", "#fb923c", "#ea580c"],
      crit: ["#fda4af", "#fb7185", "#e11d48"]
    };
    const g = grads[tone] || grads.low;
    const stops = document.querySelectorAll("#ringGrad stop");
    stops.forEach((s, i) => s.setAttribute("stop-color", g[Math.min(i, g.length - 1)]));
    if (wrap) wrap.className = `ring-wrap tone-${tone}`;
  }

  function wireGlassSelect(wrapId, onChange) {
    const wrap = $(wrapId);
    if (!wrap || wrap.dataset.wired) return;
    wrap.dataset.wired = "1";
    const btn = wrap.querySelector(".glass-select-btn");
    const menu = wrap.querySelector(".glass-select-menu");
    btn?.addEventListener("click", e => {
      e.stopPropagation();
      const open = !menu.hidden;
      $$(".glass-select-menu").forEach(m => m.hidden = true);
      menu.hidden = open;
    });
    menu?.addEventListener("click", e => {
      const opt = e.target.closest("button[data-val]");
      if (!opt) return;
      wrap.dataset.value = opt.dataset.val;
      btn.textContent = opt.textContent;
      menu.querySelectorAll("button").forEach(b => b.classList.toggle("active", b === opt));
      menu.hidden = true;
      onChange?.(opt.dataset.val, opt.textContent);
    });
  }

  function collectSave() {
    return {
      targetCountry: $("#countryWrap")?.dataset.value || "NL",
      strictLocation: ($("#modeWrap")?.dataset.value || "strict") === "strict",
      guardEnabled: $("#guardEnabled")?.checked ?? true,
      killMode: $("#killMode")?.checked ?? false,
      startWithWindows: $("#autostart")?.checked ?? false,
      uiLanguage: $("#langWrap")?.dataset.value || "ru",
      windowChromeStyle: $("#chromeWrap")?.dataset.value || document.documentElement.dataset.chrome || "mac",
      soundEnabled: $("#soundEnabled")?.checked ?? true,
      providers: $$("#providers [data-prov]").map(el => ({
        id: el.dataset.prov,
        enabled: el.classList.contains("on") || el.getAttribute("aria-pressed") === "true"
      }))
    };
  }

  function applyChromeStyle(style) {
    const mode = style === "win" ? "win" : "mac";
    document.documentElement.dataset.chrome = mode;
    const bar = $("#titlebar");
    bar?.classList.toggle("chrome-mac", mode === "mac");
    bar?.classList.toggle("chrome-win", mode === "win");
    const wrap = $("#chromeWrap");
    if (wrap) {
      wrap.dataset.value = mode;
      const btn = $("#chromeBtn");
      if (btn) btn.textContent = mode === "win" ? "Windows" : "macOS";
      wrap.querySelectorAll(".glass-select-menu button").forEach(b =>
        b.classList.toggle("active", b.dataset.val === mode));
    }
  }

  function resetShellMotion(shell) {
    if (!shell) return;
    shell.classList.remove("boot-in", "boot-out", "boot-min");
    shell.style.animation = "none";
    // force reflow so the next animation class restarts cleanly
    void shell.offsetWidth;
    shell.style.animation = "";
    shell.style.opacity = "";
    shell.style.transform = "";
    shell.style.filter = "";
  }

  function closeAppAnimated() {
    const shell = $(".shell");
    if (!shell || shell.classList.contains("boot-out") || shell.classList.contains("boot-min")) {
      post("windowClose");
      return;
    }
    resetShellMotion(shell);
    shell.classList.add("boot-out");
    setTimeout(() => post("windowClose"), 260);
  }

  function minimizeAppAnimated() {
    const shell = $(".shell");
    if (!shell || shell.classList.contains("boot-min") || shell.classList.contains("boot-out")) {
      post("windowMinimize");
      return;
    }
    // CSS preview only — host does the real minimize without a second opacity fight
    resetShellMotion(shell);
    shell.classList.add("boot-min");
    setTimeout(() => {
      post("windowMinimize");
      resetShellMotion(shell);
      shell.classList.add("boot-in");
    }, 200);
  }

  function autoSave() {
    post("save", collectSave());
  }

  function syncToggle(btn, box, on) {
    if (!btn || !box) return;
    box.checked = on;
    btn.classList.toggle("on", on);
    btn.setAttribute("aria-pressed", on);
  }

  function renderRecommendations(list) {
    const root = $("#recommendations");
    if (!root) return;
    if (!list?.length) {
      root.innerHTML = `<li class="tone-muted"><span class="check">${Icons?.check?.() || "✓"}</span><div>OK</div></li>`;
      return;
    }
    root.innerHTML = list.map(r => {
      const tone = r.tone || "muted";
      const ico = tone === "warn" ? Icons?.alert?.() : tone === "info" ? Icons?.info?.() : Icons?.check?.();
      return `<li class="tone-${tone}"><span class="check">${ico || ""}</span><div><strong>${escapeHtml(r.title)}</strong><small>${escapeHtml(r.body)}</small></div></li>`;
    }).join("");
  }

  function renderProcesses() {
    const body = $("#procBody");
    if (!body) return;
    const q = ($("#procSearch")?.value || "").toLowerCase();
    const rows = state.processes.filter(p =>
      !q || `${p.kind} ${p.path} ${p.pid}`.toLowerCase().includes(q)
    );
    body.innerHTML = rows.map(p => `
      <tr>
        <td><code>${escapeHtml(p.kind || "claude")}</code> <span class="pid">PID ${p.pid}</span></td>
        <td class="${/приост|suspend/i.test(p.state || "") ? "st-block" : "st-run"}">${escapeHtml(p.state)}</td>
        <td>${escapeHtml(p.path || "—")}</td>
      </tr>`).join("") || `<tr><td colspan="3" class="empty">${I18n?.t("noProc") || "Нет процессов Claude"}</td></tr>`;
  }

  function renderNetwork() {
    const body = $("#netBody");
    if (!body) return;
    body.innerHTML = (state.network || []).map(r => `
      <tr>
        <td>${escapeHtml(r.name)}</td>
        <td><code>${escapeHtml(r.value)}</code></td>
        <td class="${r.ok ? "st-run" : "st-block"}">${r.ok ? "OK" : "—"}</td>
      </tr>`).join("") || `<tr><td colspan="3" class="empty">—</td></tr>`;
  }

  function apply(data) {
    if (!data) return;

    if (data.uiLanguage) {
      I18n.set(data.uiLanguage);
      const langWrap = $("#langWrap");
      if (langWrap) {
        langWrap.dataset.value = data.uiLanguage;
        const labels = { ru: "Русский", en: "English", zh: "中文" };
        $("#langBtn").textContent = labels[data.uiLanguage] || data.uiLanguage;
      }
    }

    $("#sysTitle").textContent = data.systemTitle || "Система активна";
    $("#sysSub").textContent = data.systemSub || "Все защиты работают";
    if ($("#globalStatus")) $("#globalStatus").textContent = data.globalStatus || I18n.t("active");

    $("#protectionTitle").textContent = data.protectionTitle || "Защита включена";
    $("#protectionSub").textContent = data.protectionSub || "";
    $("#uptime").textContent = data.uptime || "00:00:00";
    $("#blocked").textContent = data.blockedStarts ?? 0;
    const vpnMini = $("#vpnMini span");
    if (vpnMini) vpnMini.textContent = data.vpnUp ? I18n.t("active") : "Off";
    $("#vpnMini")?.classList.toggle("off", !data.vpnUp);

    $("#vpnTitle").textContent = data.vpnUp ? "VPN OK" : "VPN down";
    $("#vpnSub").textContent = data.vpnUp ? (data.vpnCountry || "") : "No tunnel";
    $("#vpnCountry").textContent = data.vpnCountry || "—";
    $("#vpnIp").textContent = data.vpnIp || "—";
    $("#vpnLatency").textContent = data.vpnLatency || "—";
    $("#flag").style.background = flagCss(data.countryCode || "NL");

    const allowed = !!data.claudeAllowed;
    const status = $("#claudeStatus");
    status.className = allowed ? "allowed" : "blocked";
    const ico = allowed ? Icons.check() : Icons.ban();
    status.innerHTML = allowed
      ? `<span class="status-ico">${ico}</span> OK`
      : `<span class="status-ico">${ico}</span> Blocked`;
    $("#claudeSub").textContent = data.claudeSub || "";
    $("#claudeCard")?.classList.toggle("ok", allowed);
    const allowBtn = $("#btnAllow");
    if (allowBtn) allowBtn.style.display = allowed ? "none" : "";

    $("#attempts").textContent = data.launchAttempts ?? 0;
    $("#blockedBig").textContent = data.blockedStarts ?? 0;
    $("#attemptsDelta").textContent = data.attemptsDelta || "";
    $("#blockedDelta").textContent = data.blockedDelta || "";

    const rating = Math.max(0, Math.min(100, data.accountRating ?? 70));
    $("#ratingPct").textContent = `${rating}%`;
    let riskLabel = "Critical", riskTone = "crit";
    if (rating >= 90) { riskLabel = "Low"; riskTone = "low"; }
    else if (rating >= 75) { riskLabel = "Medium"; riskTone = "mid"; }
    else if (rating >= 50) { riskLabel = "High"; riskTone = "high"; }
    if (I18n.lang === "ru") {
      riskLabel = { low: "Низкий", mid: "Средний", high: "Высокий", crit: "Критичный" }[riskTone];
    } else if (I18n.lang === "zh") {
      riskLabel = { low: "低", mid: "中", high: "高", crit: "危" }[riskTone];
    }
    setRing(rating, riskTone);
    const riskTag = $("#riskTag");
    riskTag.textContent = riskLabel;
    riskTag.className = `tag ${riskTone}`;

    $("#auditTime").textContent = data.auditTime || "—";
    const auditIco = data.auditOk ? Icons.check() : Icons.alert();
    $("#auditOk").innerHTML = data.auditOk
      ? `<span class="status-ico">${auditIco}</span> OK`
      : `<span class="status-ico">${auditIco}</span> ${escapeHtml(data.auditDetail || "—")}`;
    $("#auditOk").style.color = data.auditOk ? "var(--green)" : "var(--yellow)";

    cachedEvents = data.events || [];
    renderEvents(filteredEvents(), "#events");
    renderEvents(filteredEvents(), "#eventsFull");
    renderEvents(cachedEvents, "#monEventsList");
    drawLineChart(data.spark?.length ? data.spark : [0, 0, 0, 0]);
    drawWave(data.wave);

    renderRecommendations(data.recommendations);

    state.processes = data.processes || [];
    state.network = data.network || [];
    renderProcesses();
    renderNetwork();

    if (data.windowChromeStyle) applyChromeStyle(data.windowChromeStyle);

    // providers — toggles (not upside-down checks)
    const prov = $("#providers");
    if (prov && !state.skipProvRewrite) {
      prov.innerHTML = (data.providers || []).map(p => `
        <div class="prov-row">
          <span class="name">${escapeHtml(p.name)}</span>
          <span class="status ${p.healthy ? "on" : ""}">${escapeHtml(p.status || "")}</span>
          <button type="button" class="prov-toggle ${p.enabled ? "on" : ""}" data-prov="${escapeHtml(p.id)}"
            aria-pressed="${p.enabled ? "true" : "false"}"></button>
        </div>`).join("");
      prov.querySelectorAll("[data-prov]").forEach(el => {
        el.addEventListener("click", e => {
          e.stopPropagation();
          const on = !el.classList.contains("on");
          el.classList.toggle("on", on);
          el.setAttribute("aria-pressed", on);
          state.skipProvRewrite = true;
          autoSave();
          setTimeout(() => { state.skipProvRewrite = false; }, 1200);
        });
      });
    }

    // settings
    const cw = $("#countryWrap");
    if (cw && data.targetCountry) {
      cw.dataset.value = data.targetCountry;
      const label = state.countries.find(c => c[0] === data.targetCountry);
      $("#countryBtn").textContent = label ? `${label[0]} — ${label[1]}` : data.targetCountry;
    }
    const mw = $("#modeWrap");
    if (mw) {
      const mode = data.strictLocation ? "strict" : "soft";
      mw.dataset.value = mode;
      $("#modeBtn").textContent = I18n.t(mode);
      $("#strict").checked = !!data.strictLocation;
    }
    syncToggle($("#autostartToggle"), $("#autostart"), !!data.startWithWindows);
    syncToggle($("#blockWithoutVpn"), $("#guardEnabled"), !!data.guardEnabled);
    syncToggle($("#suspendOnDrop"), $("#killMode"), !data.killMode);
    // suspend ON means killMode OFF — fix sync: button on when suspend
    if ($("#suspendOnDrop") && $("#killMode")) {
      const suspend = !data.killMode;
      $("#killMode").checked = !!data.killMode;
      $("#suspendOnDrop").classList.toggle("on", suspend);
      $("#suspendOnDrop").setAttribute("aria-pressed", suspend);
    }
    syncToggle($("#soundToggle"), $("#soundEnabled"), data.soundEnabled !== false);
    if ($("#soundPath")) $("#soundPath").textContent = data.customSoundPath || "default";

    const hy = $("#hygiene");
    if (hy && data.hygiene) {
      hy.innerHTML = data.hygiene.map(t => {
        const badge = t.enforcedByGuard
          ? `<span class="tag low">Guard</span>`
          : `<span class="tag mid">N/A</span>`;
        return `<div class="hy-item"><strong>${escapeHtml(t.title)} ${badge}</strong><p>${escapeHtml(t.body)}</p></div>`;
      }).join("");
    }

    if (data.toast) {
      const key = `${data.toast.title}|${data.toast.body}|${data.toast.time}`;
      if (!state.lastToastKeys.has(key)) {
        state.lastToastKeys.add(key);
        toast(data.toast.type || "info", data.toast.title, data.toast.body, data.toast.time);
      }
    }

    Icons.inject();
  }

  let cachedEvents = [];
  function filteredEvents() {
    const f = $("#eventFilterWrap")?.dataset.value || "Все";
    if (!f || f === "Все") return cachedEvents;
    return cachedEvents.filter(e => (e.kind || "").toLowerCase() === f.toLowerCase());
  }

  // country menu
  const countryMenu = $("#countryMenu");
  if (countryMenu) {
    countryMenu.innerHTML = state.countries.map(([c, n]) =>
      `<button type="button" data-val="${c}">${c} — ${n}</button>`).join("");
  }

  wireGlassSelect("#eventFilterWrap", () => {
    renderEvents(filteredEvents(), "#events");
    renderEvents(filteredEvents(), "#eventsFull");
  });
  wireGlassSelect("#langWrap", val => {
    I18n.set(val);
    autoSave();
  });
  wireGlassSelect("#chromeWrap", val => {
    applyChromeStyle(val);
    autoSave();
  });
  wireGlassSelect("#countryWrap", () => autoSave());
  wireGlassSelect("#modeWrap", val => {
    $("#strict").checked = val === "strict";
    autoSave();
  });

  document.addEventListener("click", () => $$(".glass-select-menu").forEach(m => m.hidden = true));

  // monitoring tabs
  $("#monTabs")?.addEventListener("click", e => {
    const btn = e.target.closest("[data-mon]");
    if (!btn) return;
    state.mon = btn.dataset.mon;
    $$("#monTabs .pill").forEach(p => p.classList.toggle("active", p === btn));
    $$(".mon-pane").forEach(p => p.classList.toggle("active", p.id === `mon-${state.mon}`));
  });
  $("#procSearch")?.addEventListener("input", renderProcesses);

  // window chrome
  $("#winClose")?.addEventListener("click", closeAppAnimated);
  $("#winClose2")?.addEventListener("click", closeAppAnimated);
  $("#winMin")?.addEventListener("click", minimizeAppAnimated);
  $("#winMin2")?.addEventListener("click", minimizeAppAnimated);
  $("#winDrag")?.addEventListener("mousedown", e => {
    if (e.button === 0) post("windowDrag");
  });

  // boot animation
  requestAnimationFrame(() => $(".shell")?.classList.add("boot-in"));

  // toggles
  function wireToggle(btnId, boxId, invert = false) {
    const btn = $(btnId), box = $(boxId);
    if (!btn || !box) return;
    btn.addEventListener("click", () => {
      box.checked = !box.checked;
      const on = invert ? !box.checked : box.checked;
      btn.classList.toggle("on", on);
      btn.setAttribute("aria-pressed", on);
      autoSave();
    });
  }
  wireToggle("#autostartToggle", "#autostart");
  wireToggle("#blockWithoutVpn", "#guardEnabled");
  wireToggle("#soundToggle", "#soundEnabled");
  $("#suspendOnDrop")?.addEventListener("click", () => {
    const box = $("#killMode");
    const btn = $("#suspendOnDrop");
    box.checked = !box.checked;
    const suspend = !box.checked;
    btn.classList.toggle("on", suspend);
    btn.setAttribute("aria-pressed", suspend);
    autoSave();
  });

  $$(".nav-item").forEach(b => b.addEventListener("click", () => navigate(b.dataset.page)));
  $$("[data-page-jump]").forEach(b => b.addEventListener("click", () => navigate(b.dataset.pageJump)));

  $("#btnCheckVpn")?.addEventListener("click", () => post("checkVpn"));
  $("#btnCheckVpn2")?.addEventListener("click", () => post("checkVpn"));
  $("#btnAllow")?.addEventListener("click", () => post("allowLaunch"));
  $("#btnAudit")?.addEventListener("click", () => post("audit"));
  $("#btnClear")?.addEventListener("click", () => post("clearEvents"));
  $("#btnMoreProtect")?.addEventListener("click", () => navigate("security"));
  $("#btnExport")?.addEventListener("click", () => post("exportJournal"));
  $("#btnPickSound")?.addEventListener("click", () => post("pickSound"));
  $("#btnSave")?.addEventListener("click", () => {
    autoSave();
    toast("success", "OK", I18n.t("save"), new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }));
  });

  if (window.chrome?.webview) {
    window.chrome.webview.addEventListener("message", ev => {
      try {
        const data = typeof ev.data === "string" ? JSON.parse(ev.data) : ev.data;
        apply(data);
      } catch (e) { console.error(e); }
    });
    post("ready");
  }

  window.__guardianApply = apply;
  window.__guardianToast = toast;
})();
