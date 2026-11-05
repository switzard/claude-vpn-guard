namespace ClaudeVpnGuard.Core;

public sealed class AppSettings
{
    public string TargetCountryCode { get; set; } = "NL";
    public string TargetLocationLabel { get; set; } = "Netherlands";
    public bool StrictLocation { get; set; } = true;
    public bool KillInsteadOfSuspend { get; set; } = false;
    public bool GuardEnabled { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public string UiLanguage { get; set; } = "ru";
    /// <summary>mac = traffic lights, win = caption buttons on the right</summary>
    public string WindowChromeStyle { get; set; } = "win";
    /// <summary>When true, Action Center toast only — no tray balloon duplicate.</summary>
    public bool PreferToastOnly { get; set; } = true;
    /// <summary>Always-on-top Guard HUD mini window.</summary>
    public bool HudEnabled { get; set; } = true;
    public bool SoundEnabled { get; set; } = true;
    /// <summary>Custom TUN: adapter name fragment (required for custom provider).</summary>
    public string CustomTunAdapterFragment { get; set; } = "";
    /// <summary>Optional process name fragment for custom TUN.</summary>
    public string CustomTunProcessFragment { get; set; } = "";
    public List<KillswitchProfile> Profiles { get; set; } = [];
    public string? ActiveProfileId { get; set; }
    public string CustomSoundPath { get; set; } = "";
    /// <summary>meme | beep | chime | alert | custom</summary>
    public string SoundPreset { get; set; } = "meme";
    /// <summary>0..100</summary>
    public int SoundVolume { get; set; } = 70;
    public bool RelaunchClaudeOnVpnRestore { get; set; } = true;
    public int PollIntervalMs { get; set; } = 800;
    public int GeoCheckIntervalSeconds { get; set; } = 45;
    public int ReminderCooldownSeconds { get; set; } = 300;
    /// <summary>Consecutive samples required before flipping tunnel Up/Down (anti-flap).</summary>
    public int TunnelConfirmSamples { get; set; } = 2;
    public Dictionary<string, bool> EnabledProviders { get; set; } = CreateDefaultProviders();
    /// <summary>Ordered provider ids — first enabled healthy wins. Missing ids append in default order.</summary>
    public List<string> ProviderPriority { get; set; } = CreateDefaultPriority();
    /// <summary>When true, only the highest-priority enabled provider may count as Up.</summary>
    public bool PreferSingleProvider { get; set; } = false;

    public bool IsProviderEnabled(string id) =>
        !EnabledProviders.TryGetValue(id, out var enabled) || enabled;

    public static Dictionary<string, bool> CreateDefaultProviders() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["happ"] = true,
        ["hiddify"] = true,
        ["v2rayn"] = true,
        ["clash-verge"] = true,
        ["amnezia"] = true,
        ["wireguard"] = true,
        ["windscribe"] = true,
        ["outline"] = true,
        ["nekoray"] = true,
        ["proton"] = true,
        ["openvpn"] = true,
        ["surfshark"] = true,
        ["nordvpn"] = true,
        ["expressvpn"] = true,
        ["shadowsocks"] = true,
        ["softether"] = true,
        ["custom"] = true
    };

    public static List<string> CreateDefaultPriority() =>
    [
        "happ", "hiddify", "v2rayn", "clash-verge", "amnezia", "wireguard",
        "windscribe", "outline", "nekoray", "proton", "openvpn",
        "surfshark", "nordvpn", "expressvpn", "shadowsocks", "softether", "custom"
    ];

    public IReadOnlyList<string> ResolveProviderOrder(IEnumerable<string> knownIds)
    {
        var known = knownIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();
        foreach (var id in ProviderPriority)
        {
            if (known.Remove(id))
                ordered.Add(id);
        }
        foreach (var id in CreateDefaultPriority())
        {
            if (known.Remove(id))
                ordered.Add(id);
        }
        ordered.AddRange(known);
        return ordered;
    }
}

public static class KnownCountries
{
    public static IReadOnlyList<(string Code, string Label)> All { get; } =
    [
        ("NL", "Netherlands"),
        ("DE", "Germany"),
        ("US", "United States"),
        ("GB", "United Kingdom"),
        ("FR", "France"),
        ("FI", "Finland"),
        ("SE", "Sweden"),
        ("CH", "Switzerland"),
        ("PL", "Poland"),
        ("TR", "Turkey"),
        ("JP", "Japan"),
        ("SG", "Singapore"),
        ("CA", "Canada"),
        ("AT", "Austria"),
        ("IE", "Ireland"),
        ("BE", "Belgium"),
        ("IT", "Italy"),
        ("ES", "Spain"),
        ("PT", "Portugal"),
        ("NO", "Norway"),
        ("DK", "Denmark"),
        ("CZ", "Czechia"),
        ("EE", "Estonia"),
        ("LV", "Latvia"),
        ("LT", "Lithuania"),
        ("AU", "Australia"),
        ("HK", "Hong Kong"),
        ("KR", "South Korea"),
        ("IN", "India"),
        ("BR", "Brazil"),
        ("AE", "United Arab Emirates"),
        ("IL", "Israel"),
        ("MX", "Mexico"),
        ("AR", "Argentina"),
        ("NZ", "New Zealand"),
        ("RO", "Romania"),
        ("HU", "Hungary"),
        ("BG", "Bulgaria"),
        ("HR", "Croatia"),
        ("SK", "Slovakia"),
        ("SI", "Slovenia"),
        ("IS", "Iceland"),
        ("LU", "Luxembourg"),
        ("MT", "Malta"),
        ("CY", "Cyprus"),
        ("GR", "Greece"),
        ("TW", "Taiwan"),
        ("TH", "Thailand"),
        ("MY", "Malaysia"),
        ("ID", "Indonesia"),
        ("PH", "Philippines"),
        ("VN", "Vietnam"),
        ("ZA", "South Africa"),
        ("NG", "Nigeria"),
        ("EG", "Egypt"),
        ("SA", "Saudi Arabia"),
        ("UA", "Ukraine"),
        ("KZ", "Kazakhstan"),
        ("GE", "Georgia"),
        ("MD", "Moldova"),
        ("RS", "Serbia"),
        ("BA", "Bosnia and Herzegovina"),
        ("MK", "North Macedonia"),
        ("AL", "Albania"),
        ("ME", "Montenegro"),
        ("XK", "Kosovo"),
        ("BY", "Belarus"),
        ("UZ", "Uzbekistan"),
        ("AM", "Armenia"),
        ("AZ", "Azerbaijan"),
        ("CL", "Chile"),
        ("CO", "Colombia"),
        ("PE", "Peru"),
        ("CR", "Costa Rica"),
        ("PA", "Panama"),
        ("UY", "Uruguay"),
        ("PK", "Pakistan"),
        ("BD", "Bangladesh"),
        ("LK", "Sri Lanka"),
        ("NP", "Nepal"),
        ("KH", "Cambodia"),
        ("MM", "Myanmar"),
        ("MO", "Macao"),
        ("QA", "Qatar"),
        ("KW", "Kuwait"),
        ("BH", "Bahrain"),
        ("OM", "Oman"),
        ("JO", "Jordan"),
        ("LB", "Lebanon"),
        ("MA", "Morocco"),
        ("TN", "Tunisia"),
        ("KE", "Kenya"),
        ("GH", "Ghana"),
        ("CI", "Côte d'Ivoire"),
        ("SN", "Senegal"),
        ("TZ", "Tanzania"),
        ("UG", "Uganda"),
        ("ET", "Ethiopia"),
        ("MU", "Mauritius"),
        ("SC", "Seychelles"),
        ("FJ", "Fiji"),
        ("PG", "Papua New Guinea"),
        ("NC", "New Caledonia"),
        ("GU", "Guam"),
        ("PR", "Puerto Rico"),
        ("DO", "Dominican Republic"),
        ("JM", "Jamaica"),
        ("TT", "Trinidad and Tobago"),
        ("BS", "Bahamas"),
        ("BB", "Barbados"),
        ("CW", "Curaçao"),
        ("AW", "Aruba"),
        ("GI", "Gibraltar"),
        ("AD", "Andorra"),
        ("MC", "Monaco"),
        ("LI", "Liechtenstein"),
        ("SM", "San Marino"),
        ("VA", "Vatican City"),
        ("FO", "Faroe Islands"),
        ("GL", "Greenland"),
        ("IM", "Isle of Man"),
        ("JE", "Jersey"),
        ("GG", "Guernsey"),
        ("AX", "Åland Islands"),
        ("RE", "Réunion"),
        ("GP", "Guadeloupe"),
        ("MQ", "Martinique"),
        ("GF", "French Guiana"),
        ("PF", "French Polynesia"),
        ("YT", "Mayotte"),
        ("PM", "Saint Pierre and Miquelon"),
        ("WF", "Wallis and Futuna"),
        ("BL", "Saint Barthélemy"),
        ("MF", "Saint Martin"),
        ("SX", "Sint Maarten"),
        ("BQ", "Caribbean Netherlands"),
        ("TC", "Turks and Caicos Islands"),
        ("KY", "Cayman Islands"),
        ("BM", "Bermuda"),
        ("VG", "British Virgin Islands"),
        ("VI", "U.S. Virgin Islands"),
        ("AS", "American Samoa"),
        ("MP", "Northern Mariana Islands"),
        ("FM", "Micronesia"),
        ("MH", "Marshall Islands"),
        ("PW", "Palau"),
        ("CK", "Cook Islands"),
        ("NU", "Niue"),
        ("TK", "Tokelau"),
        ("WS", "Samoa"),
        ("TO", "Tonga"),
        ("TV", "Tuvalu"),
        ("KI", "Kiribati"),
        ("NR", "Nauru"),
        ("SB", "Solomon Islands"),
        ("VU", "Vanuatu"),
        ("RU", "Russia")
    ];
}
