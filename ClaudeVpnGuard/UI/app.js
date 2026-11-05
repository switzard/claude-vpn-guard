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

  const pageTitles = {
    home: ["titleHome", "subHome"],
    monitoring: ["titleMonitoring", "subMonitoring"],
    vpn: ["titleVpn", "subVpn"],
    security: ["titleSecurity", "subSecurity"],
    incidents: ["titleIncidents", "subIncidents"],
    journal: ["titleJournal", "subJournal"],
    settings: ["titleSettings", "subSettings"],
    about: ["titleAbout", "subAbout"]
  };

  function updateTopbar(page) {
    const keys = pageTitles[page] || pageTitles.home;
    const title = $("#topTitle");
    const sub = $("#topSub");
    if (title) {
      title.setAttribute("data-i18n", keys[0]);
      title.textContent = I18n?.t(keys[0]) || title.textContent;
    }
    if (sub) {
      sub.setAttribute("data-i18n", keys[1]);
      sub.textContent = I18n?.t(keys[1]) || sub.textContent;
    }
  }

  function navigate(page) {
    state.page = page;
    $$(".nav-item").forEach(b => b.classList.toggle("active", b.dataset.page === page));
    $$(".page").forEach(p => p.classList.toggle("active", p.id === `page-${page}`));
    updateTopbar(page);
  }

  function toast(type, title, body, time, link) {
    const host = $("#toasts");
    if (!host) return;
    const el = document.createElement("div");
    el.className = `toast ${type}`;
    const icon = window.Icons
      ? (type === "danger" ? window.Icons.alert() : type === "success" ? window.Icons.check() : window.Icons.info())
      : "!";
    const linkHtml = link
      ? `<button type="button" class="toast-link" data-toast-nav="${escapeHtml(link.page)}">${escapeHtml(link.label)}</button>`
      : "";
    el.innerHTML = `
      <div class="toast-ico">${icon}</div>
      <div class="toast-body">
        <div class="toast-top"><strong>${escapeHtml(title)}</strong><span>${escapeHtml(time || "")}</span></div>
        <div class="toast-msg">${escapeHtml(body || "")}</div>
        ${linkHtml}
      </div>`;
    el.querySelector("[data-toast-nav]")?.addEventListener("click", () => {
      navigate(link.page);
      el.remove();
    });
    host.prepend(el);
    requestAnimationFrame(() => el.classList.add("show"));
    setTimeout(() => { el.classList.remove("show"); setTimeout(() => el.remove(), 320); }, 5200);
    while (host.children.length > 4) host.lastElementChild.remove();
  }

  function localizePassportState(st) {
    const key = { stable: "passportStable", shifted: "passportShifted", broken: "passportBroken" }[st];
    return key && I18n ? I18n.t(key) : (st || "—");
  }

  function toastIncidentsLink(t) {
    const blob = `${t?.title || ""} ${t?.body || ""}`;
    if (/инцидент|incident|freeze|passport|geo|vpn\s*drop|отключ|напоминан|exit mismatch|неверн/i.test(blob))
      return { page: "incidents", label: I18n?.t("incidentsCta") || "Инциденты" };
    return null;
  }

  function L(map) {
    return map[I18n?.lang] || map.ru || Object.values(map)[0];
  }

  function escapeHtml(s) {
    return String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
  }

  function flagCss(code) {
    // Flat 2–3 band / solid swatches only — dense stripe hacks read as crooked «flags».
    const map = {
      DE: "linear-gradient(180deg,#000 33.33%,#dd0000 33.33% 66.66%,#ffce00 66.66%)",
      NL: "linear-gradient(180deg,#ae1c28 33.33%,#fff 33.33% 66.66%,#21468b 66.66%)",
      US: "linear-gradient(90deg,#3c3b6e 38%,#b22234 38%)",
      GB: "linear-gradient(135deg,#012169 50%,#c8102e 50%)",
      FR: "linear-gradient(90deg,#002395 33.33%,#fff 33.33% 66.66%,#ed2939 66.66%)",
      FI: "linear-gradient(90deg,#fff 36%,#003580 36% 52%,#fff 52%)",
      SE: "linear-gradient(90deg,#006aa7 36%,#fecc00 36% 52%,#006aa7 52%)",
      CH: "linear-gradient(90deg,#ff0000 42%,#fff 42% 58%,#ff0000 58%)",
      PL: "linear-gradient(180deg,#fff 50%,#dc143c 50%)",
      TR: "#e30a17",
      JP: "radial-gradient(circle at 50% 50%,#bc002d 0 36%,#fff 37%)",
      SG: "linear-gradient(180deg,#ed2939 50%,#fff 50%)"
    };
    return map[code] || "linear-gradient(135deg,#2ec4b6,#1a4a44)";
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
      let tone = "info";
      if (kind === "launch" || kind === "suspend" || kind === "kill" || kind === "error"
          || (kind === "vpn" && /блок|отключ|drop|fail|error|denied|down/i.test(e.message || ""))) {
        tone = "err";
      } else if (kind === "geo" || kind === "remind") {
        tone = "warn";
      } else if (kind === "boot" || kind === "audit" || /подключ|восстанов|OK|запущен|up/i.test(e.message || "")) {
        tone = "ok";
      }
      // Plain CSS dots only — no Lucide/SVG in the rail (SVG + stroke looked like crooked «flags»).
      const time = e.timestamp ? new Date(e.timestamp).toLocaleTimeString(undefined, { hour: "2-digit", minute: "2-digit", second: "2-digit" }) : "";
      const parts = (e.message || "").split("—");
      const msg = parts[0]?.trim() || e.message || "";
      const reason = parts.slice(1).join("—").trim();
      return `<div class="event tone-${tone}">
        <span class="ev-dot" aria-hidden="true"></span>
        <span class="time">${escapeHtml(time)}</span>
        <div class="ev-body"><div class="msg">${escapeHtml(msg)}</div>${reason ? `<div class="reason">${escapeHtml(reason)}</div>` : ""}</div>
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
    dots.innerHTML = coords.map(c => `<circle cx="${c[0]}" cy="${c[1]}" r="3" fill="#2EC4B6" stroke="#0E1114" stroke-width="1.4"/>`).join("");
  }

  function setDelta(el, text, tone) {
    if (!el) return;
    el.textContent = text || "";
    el.className = `delta ${tone || "flat"}`;
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

  const glassHosts = new WeakMap();

  function restoreGlassMenu(menu) {
    if (!menu) return;
    menu.hidden = true;
    menu.classList.remove("is-open");
    menu.style.cssText = "";
    const host = glassHosts.get(menu);
    if (host && menu.parentElement !== host) host.appendChild(menu);
    glassHosts.delete(menu);
  }

  function closeAllGlassSelects() {
    $$(".glass-select").forEach(w => {
      w.classList.remove("open");
      const b = w.querySelector(".glass-select-btn");
      if (b) b.setAttribute("aria-expanded", "false");
    });
    $$(".glass-select-menu").forEach(restoreGlassMenu);
  }

  function openGlassMenu(wrap, menu) {
    // Measure BEFORE move — then portal to body so no ancestor transform/overflow clips.
    const r = wrap.getBoundingClientRect();
    glassHosts.set(menu, wrap);
    document.body.appendChild(menu);

    const width = Math.max(160, Math.round(r.width));
    const maxH = Math.min(260, Math.max(120, window.innerHeight - 24));
    let top = Math.round(r.bottom + 6);
    let left = Math.round(r.left);
    if (top + 140 > window.innerHeight) {
      top = Math.max(8, Math.round(r.top - Math.min(maxH, 200) - 6));
    }
    if (left + width > window.innerWidth - 8) left = Math.max(8, window.innerWidth - width - 8);
    if (left < 8) left = 8;

    menu.hidden = false;
    menu.classList.add("is-open");
    menu.setAttribute("role", "listbox");
    Object.assign(menu.style, {
      position: "fixed",
      left: `${left}px`,
      top: `${top}px`,
      width: `${width}px`,
      right: "auto",
      zIndex: "9000",
      maxHeight: `${maxH}px`,
      margin: "0"
    });
    const btn = wrap.querySelector(".glass-select-btn");
    if (btn) {
      btn.setAttribute("aria-haspopup", "listbox");
      btn.setAttribute("aria-expanded", "true");
    }
    const active = menu.querySelector("button.active") || menu.querySelector("button[data-val]");
    active?.focus();
  }

  function wireGlassSelect(wrapId, onChange) {
    const wrap = $(wrapId);
    if (!wrap || wrap.dataset.wired) return;
    wrap.dataset.wired = "1";
    const btn = wrap.querySelector(".glass-select-btn");
    const menu = wrap.querySelector(".glass-select-menu");
    if (btn) {
      btn.setAttribute("aria-haspopup", "listbox");
      btn.setAttribute("aria-expanded", "false");
    }
    btn?.addEventListener("click", e => {
      e.stopPropagation();
      const willOpen = !wrap.classList.contains("open");
      closeAllGlassSelects();
      if (!willOpen) return;
      wrap.classList.add("open");
      openGlassMenu(wrap, menu);
    });
    btn?.addEventListener("keydown", e => {
      if (e.key === "ArrowDown" || e.key === "Enter" || e.key === " ") {
        e.preventDefault();
        closeAllGlassSelects();
        wrap.classList.add("open");
        openGlassMenu(wrap, menu);
      }
    });
    menu?.addEventListener("click", e => {
      e.stopPropagation();
      const opt = e.target.closest("button[data-val]");
      if (!opt) return;
      wrap.dataset.value = opt.dataset.val;
      btn.textContent = opt.textContent;
      menu.querySelectorAll("button").forEach(b => b.classList.toggle("active", b === opt));
      closeAllGlassSelects();
      btn?.focus();
      onChange?.(opt.dataset.val, opt.textContent);
    });
    menu?.addEventListener("keydown", e => {
      const opts = [...menu.querySelectorAll("button[data-val]")];
      const i = opts.indexOf(document.activeElement);
      if (e.key === "Escape") {
        e.preventDefault();
        closeAllGlassSelects();
        btn?.focus();
        return;
      }
      if (e.key === "ArrowDown") {
        e.preventDefault();
        opts[Math.min(opts.length - 1, i + 1)]?.focus();
      } else if (e.key === "ArrowUp") {
        e.preventDefault();
        opts[Math.max(0, i - 1)]?.focus();
      } else if (e.key === "Enter" || e.key === " ") {
        e.preventDefault();
        document.activeElement?.click();
      }
    });
  }

  function collectSave() {
    return {
      targetCountry: $("#countryWrap")?.dataset.value || "NL",
      strictLocation: ($("#modeWrap")?.dataset.value || "strict") === "strict",
      preferSingleProvider: $("#preferSingleProvider")?.checked ?? false,
      preferToastOnly: $("#preferToastOnly")?.checked ?? true,
      hudEnabled: $("#hudEnabled")?.checked ?? true,
      customTunAdapter: $("#customTunAdapter")?.value || "",
      customTunProcess: $("#customTunProcess")?.value || "",
      guardEnabled: $("#guardEnabled")?.checked ?? true,
      killMode: $("#killMode")?.checked ?? false,
      startWithWindows: $("#autostart")?.checked ?? false,
      uiLanguage: $("#langWrap")?.dataset.value || "ru",
      windowChromeStyle: $("#chromeWrap")?.dataset.value || document.documentElement.dataset.chrome || "win",
      soundEnabled: $("#soundEnabled")?.checked ?? true,
      soundPreset: $("#presetWrap")?.dataset.value || "meme",
      soundVolume: Number($("#soundVolume")?.value || 70),
      relaunchClaudeOnVpnRestore: $("#relaunchClaude")?.checked ?? true,
      providers: $$("#providers [data-prov-row]").map(row => {
        const el = row.querySelector("[data-prov]");
        return {
          id: row.dataset.provRow,
          enabled: !!(el && (el.classList.contains("on") || el.getAttribute("aria-pressed") === "true"))
        };
      })
    };
  }

  function renderProfiles(list) {
    const root = $("#profilesList");
    if (!root) return;
    if (!list || !list.length) {
      root.innerHTML = `<p class="sub">${I18n?.t("profilesEmpty") || "Нет сохранённых профилей"}</p>`;
      return;
    }
    root.innerHTML = list.map(p => `
      <div class="profile-row${p.active ? " active" : ""}" data-profile="${escapeHtml(p.id)}">
        <div>
          <strong>${escapeHtml(p.name)}</strong>
          <span>${escapeHtml(p.targetCountryCode)} · ${p.strictLocation ? "strict" : "soft"}${p.preferSingleProvider ? " · pin" : ""}${p.killInsteadOfSuspend ? " · kill" : ""}</span>
        </div>
        <div class="profile-actions">
          <button type="button" class="btn-sm" data-apply-profile="${escapeHtml(p.id)}">Apply</button>
          <button type="button" class="btn-sm danger" data-del-profile="${escapeHtml(p.id)}">Del</button>
        </div>
      </div>`).join("");
  }

  function renderExitHistory(list) {
    const root = $("#exitHistory");
    if (!root) return;
    if (!list || !list.length) {
      root.innerHTML = `<p class="sub">Пока нет смен exit IP / страны</p>`;
      return;
    }
    root.innerHTML = list.map(e => `
      <div class="exit-row ${e.ok ? "ok" : "bad"}">
        <div class="exit-time">${escapeHtml(e.time || "")}</div>
        <div class="exit-main">
          <strong>${escapeHtml(e.countryCode || "?")} · ${escapeHtml(e.ip || "—")}</strong>
          <span>${escapeHtml(e.source || "")}${e.note ? " · " + escapeHtml(e.note) : ""}</span>
        </div>
      </div>`).join("");
  }

  function renderPassport(p, shifts) {
    if (!p) return;
    const st = (p.state || "Broken").toLowerCase();
    const badge = $("#passportState");
    if (badge) {
      badge.textContent = localizePassportState(st);
      badge.className = "pass-state " + st;
    }
    const exit = [p.countryCode, p.ip].filter(Boolean).join(" · ") || "—";
    const fp = p.fingerprint || "—";
    if ($("#passportFp")) $("#passportFp").textContent = fp;
    if ($("#passportExit")) $("#passportExit").textContent = exit;
    if ($("#passportAgree")) $("#passportAgree").textContent = p.agreement || "—";
    if ($("#passportTime")) $("#passportTime").textContent = p.updatedAt || "—";
    if ($("#passportCompactExit")) $("#passportCompactExit").textContent = exit;
    if ($("#passportCompactFp")) $("#passportCompactFp").textContent = (fp || "—").slice(0, 12);
    const card = $("#passportCard");
    if (card) {
      card.classList.toggle("warn", st === "shifted");
      card.classList.toggle("broken", st === "broken");
      card.classList.toggle("compact", st === "stable");
    }
    // Reanchor only on Shifted with a live fingerprint — Broken empty-FP is a silent no-op.
    const reanchor = $("#btnReanchor");
    if (reanchor) {
      const canReanchor = st === "shifted" && !!p.fingerprint;
      reanchor.hidden = !canReanchor;
      reanchor.disabled = !canReanchor;
    }
    const root = $("#passportShifts");
    if (root) {
      if (!shifts || !shifts.length) root.innerHTML = `<p class="sub">${I18n?.t("passportNoShifts") || "Смен identity пока нет"}</p>`;
      else root.innerHTML = shifts.slice(0, 8).map(s => `
        <div class="pass-shift">
          <span>${escapeHtml(s.time)} · ${escapeHtml(s.reason)} · ${escapeHtml(localizePassportState((s.state || "").toLowerCase()))}</span>
          <span>${escapeHtml((s.from || "—").slice(0, 8))}→${escapeHtml((s.to || "—").slice(0, 8))}</span>
        </div>`).join("");
    }
  }

  function renderIncidents(list) {
    const root = $("#incidentsList");
    if (!root) return;
    if (!list || !list.length) {
      root.innerHTML = `<p class="sub">${I18n?.t("incidentsEmpty") || "Инцидентов нет"}</p>`;
      return;
    }
    const openL = I18n?.t("incidentsOpen") || "OPEN";
    const ackL = I18n?.t("incidentsAck") || "Acknowledge";
    root.innerHTML = list.map(i => {
      const snap = [
        i.providerName && `provider: ${i.providerName}`,
        i.adapterName && `adapter: ${i.adapterName}`,
        i.exitCountry && `exit: ${i.exitCountry} ${i.exitIp || ""}`,
        i.passportBefore && `passport: ${i.passportBefore} → ${i.passportAfter || "?"}`,
        i.adapters?.length && `up adapters: ${i.adapters.slice(0, 4).join("; ")}`,
        i.processes?.length && `claude: ${i.processes.join(", ")}`
      ].filter(Boolean).join("\n");
      return `
        <div class="incident-card${i.open ? " open" : ""}" data-incident="${escapeHtml(i.id)}">
          <strong>${escapeHtml(i.kind)} · ${escapeHtml(i.summary || "")}</strong>
          <div class="incident-meta">${escapeHtml(i.time || "")}${i.open ? " · " + openL : (i.ackAt ? " · ack " + escapeHtml(i.ackAt) : "")}</div>
          <div class="incident-snap">${escapeHtml(snap)}</div>
          ${i.open ? `<div class="incident-actions"><button type="button" class="btn-sm" data-ack="${escapeHtml(i.id)}">${escapeHtml(ackL)}</button></div>` : ""}
        </div>`;
    }).join("");
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

  // Window motion is native DWM (same path Cursor uses on Windows) — no CSS fake minimize.

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

    // Toast-only messages must not wipe full UI state.
    if (data.toast && data.events === undefined && data.systemTitle === undefined) {
      const key = `${data.toast.title}|${data.toast.body}|${data.toast.time}`;
      if (!state.lastToastKeys.has(key)) {
        state.lastToastKeys.add(key);
        if (state.lastToastKeys.size > 40) {
          const first = state.lastToastKeys.values().next().value;
          state.lastToastKeys.delete(first);
        }
        toast(data.toast.type || "info", data.toast.title, data.toast.body, data.toast.time, toastIncidentsLink(data.toast));
      }
      return;
    }

    if (data.uiLanguage) {
      I18n.set(data.uiLanguage);
      const langWrap = $("#langWrap");
      if (langWrap) {
        langWrap.dataset.value = data.uiLanguage;
        const labels = { ru: "Русский", en: "English", zh: "中文" };
        $("#langBtn").textContent = labels[data.uiLanguage] || data.uiLanguage;
      }
      updateTopbar(state.page);
    }

    $("#sysTitle").textContent = data.systemTitle || "Система активна";
    $("#sysSub").textContent = data.systemSub || "Туннель + локация OK";
    if ($("#globalStatus")) $("#globalStatus").textContent = data.globalStatus || I18n.t("active");

    $("#protectionTitle").textContent = data.protectionTitle || L({ ru: "VPN OK", en: "VPN OK", zh: "VPN OK" });
    $("#protectionSub").textContent = data.protectionSub || "";
    $("#uptime").textContent = data.uptime || "00:00:00";
    // Mini "Blocked" = launch blocks only (not suspends)
    $("#blocked").textContent = data.blockedStarts ?? 0;

    const recovery = $("#recoveryBanner");
    const recoveryText = $("#recoveryText");
    if (recovery && recoveryText) {
      const reminder = (data.reminderText || "").trim();
      if (reminder) {
        recoveryText.textContent = reminder;
        recovery.classList.add("show");
      } else {
        recovery.classList.remove("show");
        recoveryText.textContent = "";
      }
    }
    const vpnMini = $("#vpnMini span");
    if (vpnMini) {
      vpnMini.textContent = data.vpnUp ? I18n.t("active")
        : data.vpnDegraded ? L({ ru: "Idle", en: "Idle", zh: "空闲" })
          : L({ ru: "Выкл", en: "Off", zh: "关闭" });
    }
    $("#vpnMini")?.classList.toggle("off", !data.vpnUp);

    // Exit card: country is the headline; tunnel state stays on tunnel card
    $("#vpnTitle").textContent = data.vpnCountry || "—";
    $("#vpnSub").textContent = data.vpnUp
      ? (data.vpnIp || L({ ru: "exit OK", en: "exit OK", zh: "出口正常" }))
      : (data.vpnDegraded
        ? (data.vpnDetail || L({ ru: "Клиент открыт, туннель выкл", en: "Client open, tunnel off", zh: "客户端开着，隧道关" }))
        : L({ ru: "Нет туннеля", en: "No tunnel", zh: "无隧道" }));
    $("#vpnCountry").textContent = data.vpnCountry || "—";
    $("#vpnIp").textContent = data.vpnIp || "—";
    $("#vpnLatency").textContent = data.vpnLatency || "—";
    if ($("#geoAgreement"))
      $("#geoAgreement").textContent = data.geoAgreement ? `geo ${data.geoAgreement}` : "geo —";
    if ($("#tunnelWhy")) $("#tunnelWhy").textContent = data.tunnelWhy || "—";
    if ($("#latencyMode"))
      $("#latencyMode").textContent = data.latencyMode === "adapter-egress" ? "egress ms" : "icmp ms";
    if (data.customTunAdapter != null && $("#customTunAdapter") && document.activeElement !== $("#customTunAdapter"))
      $("#customTunAdapter").value = data.customTunAdapter;
    if (data.customTunProcess != null && $("#customTunProcess") && document.activeElement !== $("#customTunProcess"))
      $("#customTunProcess").value = data.customTunProcess;
    if (data.preferToastOnly != null)
      syncToggle($("#toastOnlyToggle"), $("#preferToastOnly"), !!data.preferToastOnly);
    if (data.profiles) renderProfiles(data.profiles);
    if (data.exitHistory) renderExitHistory(data.exitHistory);
    if (data.passport) renderPassport(data.passport, data.passportShifts);
    if (data.incidents) renderIncidents(data.incidents);
    if (data.hudEnabled != null)
      syncToggle($("#hudToggle"), $("#hudEnabled"), !!data.hudEnabled);
    const badge = $("#incidentBadge");
    const openN = data.openIncidentCount || 0;
    if (badge) {
      // Never show a distracting "0" — badge only when there is something open
      if (openN > 0) {
        badge.hidden = false;
        badge.textContent = String(openN);
      } else {
        badge.hidden = true;
        badge.textContent = "";
      }
    }
    $("#homeIncidentsCta")?.classList.toggle("show", openN > 0);
    $("#flag").style.background = data.countryCode
      ? flagCss(data.countryCode)
      : "linear-gradient(135deg,#64748b,#334155)";
    if (data.appVersion && $("#appVersion"))
      $("#appVersion").textContent = "v" + String(data.appVersion).replace(/^v/i, "");
    if (Array.isArray(data.countries) && data.countries.length) {
      state.countries = data.countries.map(c => Array.isArray(c) ? c : [c.code, c.label]);
      rebuildCountryMenu();
    }

    const guardOn = data.guardEnabled !== false;
    const allowed = !!data.claudeAllowed;
    const status = $("#claudeStatus");
    const okLabel = L({ ru: "OK", en: "OK", zh: "正常" });
    const blockedLabel = L({ ru: "Блок", en: "Blocked", zh: "拦截" });
    const pauseLabel = L({ ru: "Пауза", en: "Paused", zh: "暂停" });
    // Guard off: do not paint Claude as healthy OK — unprotected ≠ green.
    if (!guardOn) {
      status.className = "blocked";
      status.innerHTML = `<span class="status-ico">${Icons.ban()}</span> ${pauseLabel}`;
      $("#claudeCard")?.classList.remove("ok");
    } else {
      status.className = allowed ? "allowed" : "blocked";
      const ico = allowed ? Icons.check() : Icons.ban();
      status.innerHTML = allowed
        ? `<span class="status-ico">${ico}</span> ${okLabel}`
        : `<span class="status-ico">${ico}</span> ${blockedLabel}`;
      $("#claudeCard")?.classList.toggle("ok", allowed);
    }
    // Prefer StatusSummary-backed copy from host when blocked (VPN vs geo)
    $("#claudeSub").textContent = data.claudeSub || data.statusSummary || "";
    const allowBtn = $("#btnAllow");
    const revokeBtn = $("#btnRevokeAllow");
    if (allowBtn) allowBtn.style.display = (!allowed && !data.temporaryAllowActive) ? "" : "none";
    if (revokeBtn) revokeBtn.style.display = data.temporaryAllowActive ? "" : "none";

    // Attempts = all detected Claude starts; Blocked = denied starts; Suspends = enforce suspends
    $("#attempts").textContent = data.launchAttempts ?? 0;
    $("#blockedBig").textContent = data.blockedStarts ?? 0;
    if ($("#suspendBig")) $("#suspendBig").textContent = data.suspendEvents ?? 0;
    setDelta($("#attemptsDelta"), data.attemptsDelta, data.attemptsDeltaTone);
    setDelta($("#blockedDelta"), data.blockedDelta?.replace("вмешательств", "блок") || data.blockedDelta, data.blockedDeltaTone);
    setDelta($("#suspendDelta"), data.suspendDelta, data.suspendDeltaTone);
    $("#sysCard")?.classList.toggle("warn", !(data.vpnUp && data.locationOk !== false && data.guardEnabled));
    $("#globalPill")?.classList.toggle("warn", !(data.vpnUp && data.locationOk !== false && data.guardEnabled));

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

    // providers — priority order + toggles
    const prov = $("#providers");
    if (prov && !state.skipProvRewrite) {
      const list = data.providers || [];
      prov.innerHTML = list.map((p, idx) => `
        <div class="prov-row ${p.active ? "active" : ""}" data-prov-row="${escapeHtml(p.id)}">
          <div class="prov-rank">
            <button type="button" class="btn-sm prov-move" data-move="up" data-idx="${idx}" title="Выше" ${idx === 0 ? "disabled" : ""}>↑</button>
            <button type="button" class="btn-sm prov-move" data-move="down" data-idx="${idx}" title="Ниже" ${idx === list.length - 1 ? "disabled" : ""}>↓</button>
          </div>
          <span class="name">${escapeHtml(p.name)}${p.active ? ' <span class="tag low">активен</span>' : ""}</span>
          <span class="status ${p.healthy ? "on" : p.degraded ? "deg" : ""}">${escapeHtml(p.status || "")}</span>
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
      prov.querySelectorAll("[data-move]").forEach(btn => {
        btn.addEventListener("click", e => {
          e.stopPropagation();
          const row = btn.closest("[data-prov-row]");
          if (!row || !prov) return;
          const sibling = btn.dataset.move === "up" ? row.previousElementSibling : row.nextElementSibling;
          if (!sibling) return;
          if (btn.dataset.move === "up") prov.insertBefore(row, sibling);
          else prov.insertBefore(sibling, row);
          state.skipProvRewrite = true;
          autoSave();
          setTimeout(() => { state.skipProvRewrite = false; }, 1200);
        });
      });
    }
    syncToggle($("#preferSingleToggle"), $("#preferSingleProvider"), !!data.preferSingleProvider);

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
    syncToggle($("#relaunchToggle"), $("#relaunchClaude"), data.relaunchClaudeOnVpnRestore !== false);
    syncToggle($("#killToggle"), $("#killMode"), !!data.killMode);
    if ($("#soundPath")) $("#soundPath").textContent = data.customSoundPath || data.soundPreset || "meme";
    if ($("#presetWrap") && data.soundPreset) {
      $("#presetWrap").dataset.value = data.soundPreset;
      const labels = { meme: "Meme", beep: "Beep", chime: "Chime", alert: "Alert", custom: "Custom WAV" };
      if ($("#presetBtn")) $("#presetBtn").textContent = labels[data.soundPreset] || data.soundPreset;
    }
    if ($("#soundVolume") && data.soundVolume != null) {
      $("#soundVolume").value = data.soundVolume;
      if ($("#soundVolumeLabel")) $("#soundVolumeLabel").textContent = `${data.soundVolume}%`;
    }

    const hy = $("#hygiene");
    if (hy && data.hygiene) {
      const lang = I18n?.lang || "ru";
      const guardL = lang === "en" ? "Guard" : lang === "zh" ? "Guard" : "Guard";
      const naL = lang === "en" ? "manual" : lang === "zh" ? "手动" : "вручную";
      const unverL = lang === "en" ? "unverified" : lang === "zh" ? "未核实" : "не проверено";
      hy.innerHTML = data.hygiene.map(t => {
        const badge = t.enforcedByGuard
          ? `<span class="tag low">${guardL}</span>`
          : `<span class="tag mid">${naL}</span>`;
        const unver = t.unverified ? `<span class="tag high">${unverL}</span>` : "";
        return `<div class="hy-item"><strong>${escapeHtml(t.title)} ${badge} ${unver}</strong><p>${escapeHtml(t.body)}</p></div>`;
      }).join("");
    }

    if (data.toast) {
      const key = `${data.toast.title}|${data.toast.body}|${data.toast.time}`;
      if (!state.lastToastKeys.has(key)) {
        state.lastToastKeys.add(key);
        toast(data.toast.type || "info", data.toast.title, data.toast.body, data.toast.time, toastIncidentsLink(data.toast));
      }
    }

    Icons.inject();
  }

  let cachedEvents = [];
  function filteredEvents() {
    const f = ($("#eventFilterWrap")?.dataset.value || "all").toLowerCase();
    if (!f || f === "all" || f === "все" || f === "все события") return cachedEvents;
    return cachedEvents.filter(e => (e.kind || "").toLowerCase() === f);
  }

  function rebuildCountryMenu() {
    const countryMenu = $("#countryMenu");
    if (!countryMenu) return;
    const selected = $("#countryWrap")?.dataset.value;
    countryMenu.innerHTML = state.countries.map(([c, n]) =>
      `<button type="button" data-val="${escapeHtml(c)}" class="${c === selected ? "active" : ""}">${escapeHtml(c)} — ${escapeHtml(n)}</button>`).join("");
  }
  rebuildCountryMenu();

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

  document.addEventListener("click", () => closeAllGlassSelects());
  document.addEventListener("keydown", e => {
    if (e.key === "Escape") closeAllGlassSelects();
  });
  window.addEventListener("resize", () => closeAllGlassSelects());
  document.querySelector(".workspace")?.addEventListener("scroll", () => closeAllGlassSelects(), true);

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
  $("#winClose")?.addEventListener("click", () => post("windowClose"));
  $("#winClose2")?.addEventListener("click", () => post("windowClose"));
  $("#winMin")?.addEventListener("click", () => post("windowMinimize"));
  $("#winMin2")?.addEventListener("click", () => post("windowMinimize"));
  $("#winDrag")?.addEventListener("mousedown", e => {
    if (e.button === 0) post("windowDrag");
  });

  // Soft content reveal only (window itself uses OS/DWM transitions)
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
  wireToggle("#preferSingleToggle", "#preferSingleProvider");
  wireToggle("#soundToggle", "#soundEnabled");
  wireToggle("#relaunchToggle", "#relaunchClaude");
  $("#killToggle")?.addEventListener("click", () => {
    const box = $("#killMode");
    if (!box) return;
    box.checked = !box.checked;
    syncToggle($("#killToggle"), $("#killMode"), box.checked);
    const suspend = !box.checked;
    $("#suspendOnDrop")?.classList.toggle("on", suspend);
    $("#suspendOnDrop")?.setAttribute("aria-pressed", String(suspend));
    autoSave();
  });
  $("#suspendOnDrop")?.addEventListener("click", () => {
    const box = $("#killMode");
    const btn = $("#suspendOnDrop");
    if (!box || !btn) return;
    box.checked = !box.checked;
    const suspend = !box.checked;
    btn.classList.toggle("on", suspend);
    btn.setAttribute("aria-pressed", String(suspend));
    syncToggle($("#killToggle"), $("#killMode"), box.checked);
    autoSave();
  });

  $$(".nav-item").forEach(b => b.addEventListener("click", () => navigate(b.dataset.page)));
  $$("[data-settings]").forEach(b => b.addEventListener("click", () => {
    const id = b.dataset.settings;
    $$(".settings-nav .pill").forEach(p => p.classList.toggle("active", p === b));
    $$("[data-settings-pane]").forEach(pane => {
      const on = pane.dataset.settingsPane === id;
      pane.hidden = !on;
      pane.classList.toggle("active", on);
    });
  }));

  wireGlassSelect("#presetWrap", () => autoSave());
  $("#soundVolume")?.addEventListener("input", () => {
    if ($("#soundVolumeLabel"))
      $("#soundVolumeLabel").textContent = `${$("#soundVolume").value}%`;
    autoSave();
  });

  $("#btnCheckVpn")?.addEventListener("click", () => post("checkVpn"));
  $("#btnCheckVpn2")?.addEventListener("click", () => post("checkVpn"));
  $("#btnAllow")?.addEventListener("click", () => post("allowLaunch"));
  $("#btnRevokeAllow")?.addEventListener("click", () => post("revokeAllow"));
  $("#btnAudit")?.addEventListener("click", () => post("audit"));
  $("#btnClear")?.addEventListener("click", () => post("clearEvents"));
  $("#btnMoreProtect")?.addEventListener("click", () => navigate("security"));
  $("#btnExport")?.addEventListener("click", () => post("exportJournal"));
  $("#btnPickSound")?.addEventListener("click", () => post("pickSound"));
  $("#btnPreviewSound")?.addEventListener("click", () => post("previewSound"));
  $("#btnSaveProfile")?.addEventListener("click", () => {
    post("saveProfile", { name: $("#profileName")?.value || "" });
    if ($("#profileName")) $("#profileName").value = "";
  });
  $("#profilesList")?.addEventListener("click", e => {
    const applyBtn = e.target.closest("[data-apply-profile]");
    if (applyBtn) {
      post("applyProfile", { id: applyBtn.dataset.applyProfile });
      return;
    }
    const delBtn = e.target.closest("[data-del-profile]");
    if (delBtn) post("deleteProfile", { id: delBtn.dataset.delProfile });
  });
  $("#toastOnlyToggle")?.addEventListener("click", () => {
    const box = $("#preferToastOnly");
    const btn = $("#toastOnlyToggle");
    if (!box || !btn) return;
    box.checked = !box.checked;
    syncToggle(btn, box, box.checked);
    autoSave();
  });
  $("#hudToggle")?.addEventListener("click", () => {
    const box = $("#hudEnabled");
    const btn = $("#hudToggle");
    if (!box || !btn) return;
    box.checked = !box.checked;
    syncToggle(btn, box, box.checked);
    autoSave();
    if (box.checked) post("showHud");
  });
  $("#btnReanchor")?.addEventListener("click", () => post("reanchorPassport"));
  $("#btnShowHud")?.addEventListener("click", () => {
    const box = $("#hudEnabled");
    if (box && !box.checked) {
      box.checked = true;
      syncToggle($("#hudToggle"), box, true);
      autoSave();
    }
    post("showHud");
  });
  $("#btnOpenIncidents")?.addEventListener("click", () => navigate("incidents"));
  $("#btnAckAll")?.addEventListener("click", () => post("ackAllIncidents"));
  $("#incidentsList")?.addEventListener("click", e => {
    const btn = e.target.closest("[data-ack]");
    if (btn) post("ackIncident", { id: btn.dataset.ack });
  });
  $("#customTunAdapter")?.addEventListener("change", () => autoSave());
  $("#customTunProcess")?.addEventListener("change", () => autoSave());
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
