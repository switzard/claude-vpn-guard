window.I18n = {
  lang: "ru",
  dict: {
    ru: {
      home: "Главная", monitoring: "Мониторинг", vpn: "VPN", security: "Безопасность",
      journal: "Журнал", settings: "Настройки", about: "О программе",
      title: "Claude Code Guardian", subtitle: "Полный контроль и защита вашего аккаунта",
      active: "Активен", events: "Активность и события", stats: "Статистика",
      risks: "Риски и угрозы", tips: "Рекомендации", lastCheck: "Последняя проверка",
      allEvents: "Все события", clear: "Очистить", save: "Сохранить изменения",
      autostart: "Автозапуск", autostartHint: "Запускать вместе с Windows",
      blockVpn: "Блокировать без VPN", blockVpnHint: "Запретить Claude без туннеля",
      suspend: "Пауза при обрыве VPN", suspendHint: "Приостанавливать процессы Claude",
      targetLoc: "Целевая локация", workMode: "Режим работы", strict: "Строгий", soft: "Мягкий",
      language: "Язык интерфейса", winChrome: "Кнопки окна",
      sound: "Звук уведомлений", soundHint: "Системные balloon + звук",
      pickSound: "Выбрать WAV…", general: "Общие",
      processes: "Процессы", monEvents: "События", network: "Сеть",
      searchProc: "Поиск по процессам…", noProc: "Нет процессов Claude",
      noEvents: "Событий пока нет", export: "Экспорт журнала",
      checkVpn: "Проверить VPN", allow: "Разрешить запуск", runAudit: "Запустить аудит",
      moreProtect: "Подробнее о защите →", providers: "Выберите активные провайдеры туннелей",
      ratingApprox: "Рейтинг аккаунта (примерный)", ratingHint: "На основе VPN, локации и аудита",
      aboutBody: "Локальный VPN-killswitch и location lock для Claude Code / Desktop.",
      aboutPolicy: "Не обходит KYC, платежи, мультиаккаунты и Usage Policy."
    },
    en: {
      home: "Home", monitoring: "Monitoring", vpn: "VPN", security: "Security",
      journal: "Journal", settings: "Settings", about: "About",
      title: "Claude Code Guardian", subtitle: "Full control and account protection",
      active: "Active", events: "Activity & events", stats: "Statistics",
      risks: "Risks", tips: "Recommendations", lastCheck: "Last check",
      allEvents: "All events", clear: "Clear", save: "Save changes",
      autostart: "Autostart", autostartHint: "Launch with Windows",
      blockVpn: "Block without VPN", blockVpnHint: "Deny Claude without a tunnel",
      suspend: "Suspend on VPN drop", suspendHint: "Suspend Claude processes",
      targetLoc: "Target location", workMode: "Mode", strict: "Strict", soft: "Soft",
      language: "UI language", winChrome: "Window buttons",
      sound: "Notification sound", soundHint: "Windows balloon + sound",
      pickSound: "Choose WAV…", general: "General",
      processes: "Processes", monEvents: "Events", network: "Network",
      searchProc: "Search processes…", noProc: "No Claude processes",
      noEvents: "No events yet", export: "Export journal",
      checkVpn: "Check VPN", allow: "Allow launch", runAudit: "Run audit",
      moreProtect: "More about protection →", providers: "Select active tunnel providers",
      ratingApprox: "Account rating (approx.)", ratingHint: "Based on VPN, location and audit",
      aboutBody: "Local VPN killswitch and location lock for Claude Code / Desktop.",
      aboutPolicy: "Does not bypass KYC, payments, multi-accounts or Usage Policy."
    },
    zh: {
      home: "主页", monitoring: "监控", vpn: "VPN", security: "安全",
      journal: "日志", settings: "设置", about: "关于",
      title: "Claude Code Guardian", subtitle: "全面控制与账户保护",
      active: "运行中", events: "活动与事件", stats: "统计",
      risks: "风险", tips: "建议", lastCheck: "上次检查",
      allEvents: "全部事件", clear: "清除", save: "保存更改",
      autostart: "开机自启", autostartHint: "随 Windows 启动",
      blockVpn: "无 VPN 时拦截", blockVpnHint: "无隧道时禁止 Claude",
      suspend: "VPN 断开时暂停", suspendHint: "暂停 Claude 进程",
      targetLoc: "目标地区", workMode: "模式", strict: "严格", soft: "宽松",
      language: "界面语言", winChrome: "窗口按钮",
      sound: "通知声音", soundHint: "系统气泡 + 声音",
      pickSound: "选择 WAV…", general: "常规",
      processes: "进程", monEvents: "事件", network: "网络",
      searchProc: "搜索进程…", noProc: "没有 Claude 进程",
      noEvents: "暂无事件", export: "导出日志",
      checkVpn: "检查 VPN", allow: "允许启动", runAudit: "运行审计",
      moreProtect: "了解更多保护 →", providers: "选择活动的隧道提供商",
      ratingApprox: "账户评分（近似）", ratingHint: "基于 VPN、地区与审计",
      aboutBody: "适用于 Claude Code / Desktop 的本地 VPN 断开保护与地区锁定。",
      aboutPolicy: "不绕过 KYC、付款、多账户或使用政策。"
    }
  },
  t(key) {
    return (this.dict[this.lang] || this.dict.ru)[key] || (this.dict.ru[key] || key);
  },
  set(lang) {
    this.lang = ["ru", "en", "zh"].includes(lang) ? lang : "ru";
    document.documentElement.lang = this.lang;
    document.querySelectorAll("[data-i18n]").forEach(el => {
      const k = el.getAttribute("data-i18n");
      if (k) el.textContent = this.t(k);
    });
    document.querySelectorAll("[data-i18n-placeholder]").forEach(el => {
      const k = el.getAttribute("data-i18n-placeholder");
      if (k) el.setAttribute("placeholder", this.t(k));
    });
  }
};
