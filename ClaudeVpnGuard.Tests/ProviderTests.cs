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

        var up = monitor.Evaluate(new SystemSnapshot
        {
            Processes = [new ProcessInfo("Happ", 1, null), new ProcessInfo("xray", 2, null)],
            Adapters = [new AdapterInfo("happ-xray", "Happ Tunnel", OperationalStatus.Up)]
        });
        Assert.Equal(TunnelState.Up, up.State);
        Assert.Equal("happ", up.ActiveProviderId);
    }

    [Fact]
    public void Clash_and_hiddify_need_app_plus_core_or_adapter()
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
            Adapters = []
        };
        Assert.True(new ClashVergeProvider().Probe(clash).Healthy);
    }

    [Fact]
    public void ProcessGuard_classifies_desktop_and_code_paths()
    {
        Assert.Equal("Claude Desktop",
            ProcessGuard.Classify(@"C:\Program Files\WindowsApps\Claude_1.0\app\Claude.exe", "Claude"));
        Assert.Equal("Claude Code",
            ProcessGuard.Classify(@"C:\Users\x\AppData\Roaming\npm\node_modules\@anthropic-ai\claude-code\bin\claude.exe", "claude"));
    }
}
