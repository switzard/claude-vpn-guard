using System.Net.NetworkInformation;
using ClaudeVpnGuard.Core;
using ClaudeVpnGuard.Tunnel;
using ClaudeVpnGuard.Tunnel.Providers;
using Xunit;

namespace ClaudeVpnGuard.Tests;

public class ProviderTests
{
    [Fact]
    public void Happ_detects_adapter_and_core()
    {
        var snap = new SystemSnapshot
        {
            Processes =
            [
                new ProcessInfo("Happ", 1, null),
                new ProcessInfo("xray", 2, null)
            ],
            Adapters =
            [
                new AdapterInfo("happ-xray", "Happ Tunnel", OperationalStatus.Up)
            ]
        };

        var result = new HappProvider().Probe(snap);
        Assert.True(result.Healthy);
    }

    [Fact]
    public void Happ_process_only_is_degraded_not_up()
    {
        var snap = new SystemSnapshot
        {
            Processes = [new ProcessInfo("Happ", 1, null), new ProcessInfo("xray", 2, null)],
            Adapters = []
        };
        var result = new HappProvider().Probe(snap);
        Assert.False(result.Healthy);
        Assert.True(result.Degraded);
    }

    [Fact]
    public void Happ_adapter_only_is_degraded_not_up()
    {
        var snap = new SystemSnapshot
        {
            Processes = [],
            Adapters = [new AdapterInfo("happ-xray", "Happ Tunnel", OperationalStatus.Up)]
        };
        var result = new HappProvider().Probe(snap);
        Assert.False(result.Healthy);
        Assert.True(result.Degraded);
    }

    [Fact]
    public void V2rayN_app_only_is_not_healthy()
    {
        var snap = new SystemSnapshot
        {
            Processes = [new ProcessInfo("v2rayN", 1, null)],
            Adapters = []
        };
        var result = new V2rayNProvider().Probe(snap);
        Assert.False(result.Healthy);
        Assert.True(result.Degraded);
    }

    [Fact]
    public void Clash_without_adapter_is_degraded()
    {
        var clash = new SystemSnapshot
        {
            Processes =
            [
                new ProcessInfo("clash-verge", 1, null),
                new ProcessInfo("mihomo", 2, null)
            ],
            Adapters = []
        };
        var result = new ClashVergeProvider().Probe(clash);
        Assert.False(result.Healthy);
        Assert.True(result.Degraded);
    }

    [Fact]
    public void Radmin_is_not_treated_as_wireguard()
    {
        var snap = new SystemSnapshot
        {
            Processes = [],
            Adapters =
            [
                new AdapterInfo("Radmin VPN", "Famatech Radmin VPN Ethernet Adapter", OperationalStatus.Up)
            ]
        };

        Assert.False(new WireGuardProvider().Probe(snap).Healthy);
        Assert.False(new HappProvider().Probe(snap).Healthy);
    }

    [Fact]
    public void TunnelMonitor_requires_enabled_healthy_provider()
    {
        var settings = new AppSettings();
        settings.EnabledProviders["happ"] = true;
        foreach (var key in settings.EnabledProviders.Keys.ToList())
        {
            if (key != "happ") settings.EnabledProviders[key] = false;
        }

        var monitor = new TunnelMonitor([new HappProvider()], () => settings);
        var down = monitor.Evaluate(new SystemSnapshot { Processes = [], Adapters = [] });
        Assert.Equal(TunnelState.Down, down.State);

        var degraded = monitor.Evaluate(new SystemSnapshot
        {
            Processes = [new ProcessInfo("Happ", 1, null)],
            Adapters = []
        });
        Assert.Equal(TunnelState.Degraded, degraded.State);

        var up = monitor.Evaluate(new SystemSnapshot
        {
            Processes = [new ProcessInfo("Happ", 1, null), new ProcessInfo("xray", 2, null)],
            Adapters = [new AdapterInfo("happ-xray", "Happ Tunnel", OperationalStatus.Up)]
        });
        Assert.Equal(TunnelState.Up, up.State);
        Assert.Equal("happ", up.ActiveProviderId);
    }

    [Fact]
    public void Clash_and_hiddify_need_adapter_for_up()
    {
        var empty = new SystemSnapshot { Processes = [], Adapters = [] };
        Assert.False(new ClashVergeProvider().Probe(empty).Healthy);
        Assert.False(new HiddifyProvider().Probe(empty).Healthy);

        var clash = new SystemSnapshot
        {
            Processes =
            [
                new ProcessInfo("clash-verge", 1, null),
                new ProcessInfo("mihomo", 2, null)
            ],
            Adapters = [new AdapterInfo("mihomo-tun", "Mihomo TUN", OperationalStatus.Up)]
        };
        Assert.True(new ClashVergeProvider().Probe(clash).Healthy);
    }

    [Fact]
    public void Clash_shared_Meta_nic_is_not_healthy()
    {
        // Short "Meta" fragment must not claim foreign Meta-ish adapters.
        var snap = new SystemSnapshot
        {
            Processes =
            [
                new ProcessInfo("clash-verge", 1, null),
                new ProcessInfo("mihomo", 2, null)
            ],
            Adapters = [new AdapterInfo("MetaTunnel", "Some Meta VPN", OperationalStatus.Up)]
        };
        Assert.False(new ClashVergeProvider().Probe(snap).Healthy);
    }

    [Fact]
    public void Hiddify_idle_gui_plus_foreign_singbox_is_not_healthy()
    {
        var snap = new SystemSnapshot
        {
            Processes = [new ProcessInfo("Hiddify", 1, null)], // no sing-box core
            Adapters = [new AdapterInfo("sing-box-tun", "sing-box TUN", OperationalStatus.Up)]
        };
        var r = new HiddifyProvider().Probe(snap);
        Assert.False(r.Healthy);
        Assert.True(r.Degraded);
    }

    [Fact]
    public void ProcessGuard_classifies_desktop_and_code_paths()
    {
        Assert.Equal("Claude Desktop",
            ProcessGuard.Classify(@"C:\Program Files\WindowsApps\Claude_1.0\app\Claude.exe", "Claude"));
        Assert.Equal("Claude Code",
            ProcessGuard.Classify(@"C:\Users\x\AppData\Roaming\npm\node_modules\@anthropic-ai\claude-code\bin\claude.exe", "claude"));
    }

    [Fact]
    public void SoftEther_requires_client_and_adapter()
    {
        var up = new SystemSnapshot
        {
            Processes = [new ProcessInfo("vpnclient_x64", 1, null)],
            Adapters = [new AdapterInfo("VPN Client Adapter - VPN", "SoftEther Virtual Network Adapter", OperationalStatus.Up)]
        };
        Assert.True(new SoftEtherProvider().Probe(up).Healthy);

        var degraded = new SystemSnapshot
        {
            Processes = [new ProcessInfo("vpnclient_x64", 1, null)],
            Adapters = []
        };
        var d = new SoftEtherProvider().Probe(degraded);
        Assert.False(d.Healthy);
        Assert.True(d.Degraded);
    }

    [Fact]
    public void TunnelMonitor_respects_priority_and_pin_mode()
    {
        var settings = new AppSettings
        {
            ProviderPriority = ["wireguard", "happ"],
            PreferSingleProvider = true,
            EnabledProviders = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["wireguard"] = true,
                ["happ"] = true
            }
        };
        var monitor = new TunnelMonitor([new WireGuardProvider(), new HappProvider()], () => settings);
        var snap = new SystemSnapshot
        {
            Processes =
            [
                new ProcessInfo("Happ", 1, null),
                new ProcessInfo("xray", 2, null)
            ],
            Adapters = [new AdapterInfo("happ-xray", "Happ Tunnel", OperationalStatus.Up)]
        };
        // Happ is healthy but pin expects WireGuard first → not Up from Happ.
        var pinned = monitor.Evaluate(snap);
        Assert.NotEqual(TunnelState.Up, pinned.State);
        Assert.Equal("wireguard", pinned.ActiveProviderId);

        settings.PreferSingleProvider = false;
        var free = monitor.Evaluate(snap);
        Assert.Equal(TunnelState.Up, free.State);
        Assert.Equal("happ", free.ActiveProviderId);
    }

    [Fact]
    public void Nord_does_not_match_generic_nord_adapter_without_lynx()
    {
        var snap = new SystemSnapshot
        {
            Processes = [new ProcessInfo("NordVPN", 1, null)],
            Adapters = [new AdapterInfo("Nordic Virtual", "Nordic VPN Helper", OperationalStatus.Up)]
        };
        var r = new NordVpnProvider().Probe(snap);
        Assert.False(r.Healthy);
        Assert.True(r.Degraded);
    }

    [Fact]
    public void CustomTun_requires_adapter_fragment_match()
    {
        var settings = new AppSettings
        {
            CustomTunAdapterFragment = "MyFancy",
            CustomTunProcessFragment = "fancyvpn"
        };
        var provider = new CustomTunProvider(() => settings);
        Assert.False(provider.Probe(new SystemSnapshot { Processes = [], Adapters = [] }).Healthy);

        var up = new SystemSnapshot
        {
            Processes = [new ProcessInfo("fancyvpn", 1, null)],
            Adapters = [new AdapterInfo("MyFancy TUN", "MyFancy Virtual", OperationalStatus.Up)]
        };
        Assert.True(provider.Probe(up).Healthy);

        var noProc = new SystemSnapshot
        {
            Processes = [],
            Adapters = [new AdapterInfo("MyFancy TUN", "MyFancy Virtual", OperationalStatus.Up)]
        };
        var d = provider.Probe(noProc);
        Assert.False(d.Healthy);
        Assert.True(d.Degraded);
    }

    [Fact]
    public void CreateProviders_includes_custom()
    {
        var ids = GuardService.CreateProviders().Select(p => p.Id).ToList();
        Assert.Contains("custom", ids);
    }

    [Fact]
    public void KillswitchProfile_roundtrips_in_settings_defaults()
    {
        var s = new AppSettings();
        Assert.Empty(s.Profiles);
        s.Profiles.Add(new KillswitchProfile
        {
            Name = "NL Work",
            TargetCountryCode = "NL",
            StrictLocation = true,
            PreferSingleProvider = true,
            PreferredProviderId = "happ"
        });
        Assert.Single(s.Profiles);
        Assert.Equal("NL", s.Profiles[0].TargetCountryCode);
        Assert.True(s.PreferToastOnly);
        Assert.Contains("custom", s.ProviderPriority);
    }

    [Fact]
    public void ExitHistoryStore_dedupes_identical_consecutive()
    {
        var a = new ExitHistoryEntry
        {
            Ok = true,
            Ip = "203.0.113.10",
            CountryCode = "NL",
            Source = "test",
            Note = "poll"
        };
        ExitHistoryStore.Append(a);
        ExitHistoryStore.Append(new ExitHistoryEntry
        {
            Ok = true,
            Ip = "203.0.113.10",
            CountryCode = "NL",
            Source = "test",
            Note = "poll-2"
        });
        var recent = ExitHistoryStore.Recent(5);
        Assert.Contains(recent, e => e.Ip == "203.0.113.10" && e.CountryCode == "NL");
        Assert.Equal(1, recent.Count(e => e.Ip == "203.0.113.10" && e.CountryCode == "NL" && e.Ok));
    }

    [Fact]
    public void SessionPassport_stable_then_shifted_on_ip_change()
    {
        var p = new SessionPassport();
        var geo1 = new GeoResult
        {
            Ok = true,
            Ip = "203.0.113.10",
            CountryCode = "NL",
            AgreementCount = 3,
            SampleCount = 4
        };
        var a = p.Observe(geo1, "happ", tunnelUp: true);
        Assert.Equal(PassportState.Stable, a.State);
        Assert.False(string.IsNullOrEmpty(a.Fingerprint));

        var same = p.Observe(geo1, "happ", tunnelUp: true);
        Assert.Equal(a.Fingerprint, same.Fingerprint);
        Assert.Equal(PassportState.Stable, same.State);

        var geo2 = new GeoResult
        {
            Ok = true,
            Ip = "198.51.100.20",
            CountryCode = "NL",
            AgreementCount = 3,
            SampleCount = 4
        };
        var shifted = p.Observe(geo2, "happ", tunnelUp: true);
        Assert.Equal(PassportState.Shifted, shifted.State);
        Assert.NotEqual(a.Fingerprint, shifted.Fingerprint);

        var broken = p.Observe(geo2, "happ", tunnelUp: false);
        Assert.Equal(PassportState.Broken, broken.State);
    }

    [Fact]
    public void IncidentStore_ack_closes_open()
    {
        var id = "testh" + Guid.NewGuid().ToString("N")[..4];
        var inc = IncidentStore.Append(new GuardIncident
        {
            Id = id,
            Kind = "vpn-drop",
            Summary = "unit test incident",
            Open = true
        });
        Assert.True(inc.Open);
        Assert.True(IncidentStore.Acknowledge(inc.Id));
        var again = IncidentStore.Recent(20).First(i => i.Id == inc.Id);
        Assert.False(again.Open);
        Assert.NotNull(again.AcknowledgedAt);
    }
}
