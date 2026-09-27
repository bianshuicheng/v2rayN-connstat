namespace ServiceLib.ViewModels;

/// <summary>
/// Live per-connection view for the Xray core, fed by the connstat patch
/// (metrics /debug/vars -> "connstat"). sing-box keeps using
/// ClashConnectionsViewModel; this view is Xray-only.
///
/// TUN mode: when a sing-box pre-core fronts the Xray main core, v2rayN
/// reports RunningCoreType as sing_box while the Xray main core still serves
/// every connection - so we poll the connstat endpoint regardless of the
/// reported core type. When the running main core IS sing-box (no xray at
/// all), fall back to its clash API /connections, which also carries
/// process/processPath metadata.
/// </summary>
public partial class XrayConnectionsViewModel : MyReactiveObject
{
    public BulkObservableCollection<XrayConnectionModel> ConnectionItems { get; } = [];

    [Reactive]
    public partial string HostFilter { get; set; }

    /// <summary>
    /// "all" | "dns" | "app" - whether to show every connection, only DNS
    /// traffic, or only application traffic (DNS hidden).
    /// </summary>
    [Reactive]
    public partial string DnsFilter { get; set; }

    [Reactive]
    public partial bool AutoRefresh { get; set; }

    private readonly Dictionary<string, DateTime> _firstSeen = new();
    private readonly Dictionary<string, (ulong up, ulong down)> _last = new();

    public XrayConnectionsViewModel()
    {
        _config = AppManager.Instance.Config;
        // always start with auto refresh on: the stored setting is shared with
        // the sing-box connections view and defaults to false there
        AutoRefresh = true;
        // default to application traffic: DNS plumbing is usually noise
        DnsFilter = "app";

        _ = Task.Run(Run);
    }

    private async Task Run()
    {
        while (true)
        {
            await Task.Delay(1000);
            try
            {
                if (!AutoRefresh)
                {
                    continue;
                }

                // primary source: xray connstat patch (works for the xray main
                // core, with or without a TUN pre-core in front of it)
                var url = $"{Global.HttpProtocol}{Global.Loopback}:{AppManager.Instance.StatePort}/debug/vars";
                var result = await HttpClientHelper.Instance.TryGetAsync(url);
                var connlist = JsonUtils.Deserialize<XrayMetricsVars>(result ?? string.Empty)?.connstat;
                if (connlist != null)
                {
                    await ApplyXrayConnections(connlist);
                    continue;
                }

                // fallback: pure sing-box main core (xray not running) - use
                // its clash API, which also carries process metadata
                if (AppManager.Instance.IsRunningCore(ECoreType.sing_box))
                {
                    await ApplySingboxConnections();
                }
            }
            catch
            {
                // ignored
            }
        }
    }

    private async Task ApplyXrayConnections(List<XrayConnStatItem> connlist)
    {
        var dtNow = DateTime.Now;
        var lstModel = new List<XrayConnectionModel>();
        var liveIds = new HashSet<string>();
        foreach (var item in connlist)
        {
            var idStr = item.id.ToString();
            liveIds.Add(idStr);
            if (!_firstSeen.TryGetValue(idStr, out var start))
            {
                start = dtNow;
                _firstSeen[idStr] = start;
            }
            var (lastUp, lastDown) = _last.GetValueOrDefault(idStr, ((ulong)0, (ulong)0));
            var upSpeed = item.uplink >= lastUp ? item.uplink - lastUp : 0;
            var downSpeed = item.downlink >= lastDown ? item.downlink - lastDown : 0;
            _last[idStr] = (item.uplink, item.downlink);

            var host = item.dest ?? string.Empty;
            var isDns = IsDnsConnection(item.inbound, item.outbound, host);
            if (HostFilter.IsNotEmpty() && !host.Contains(HostFilter))
            {
                continue;
            }
            if (DnsFilter == "dns" && !isDns)
            {
                continue;
            }
            if (DnsFilter == "app" && isDns)
            {
                continue;
            }

            lstModel.Add(new XrayConnectionModel
            {
                Id = idStr,
                Host = host,
                Network = "tcp",
                Inbound = item.inbound,
                Outbound = item.outbound,
                Process = item.process,
                ProcessPath = item.path,
                IsDns = isDns,
                DownSpeed = Utils.HumanFy((long)downSpeed) + "/s",
                UpSpeed = Utils.HumanFy((long)upSpeed) + "/s",
                DownTotal = Utils.HumanFy((long)item.downlink),
                UpTotal = Utils.HumanFy((long)item.uplink),
                Time = (dtNow - start).TotalSeconds < 0 ? 1 : (dtNow - start).TotalSeconds,
                Elapsed = (dtNow - start).ToString(@"hh\:mm\:ss"),
            });
        }

        PruneDead(liveIds);

        RxSchedulers.MainThreadScheduler.Schedule(() =>
        {
            ConnectionItems.Clear();
            ConnectionItems.AddRange(lstModel);
        });
        await Task.CompletedTask;
    }

    private async Task ApplySingboxConnections()
    {
        var ret = await ClashApiManager.Instance.GetClashConnectionsAsync();
        if (ret == null)
        {
            return;
        }

        var dtNow = DateTime.Now;
        var lstModel = new List<XrayConnectionModel>();
        var liveIds = new HashSet<string>();
        foreach (var item in ret.connections ?? [])
        {
            var idStr = item.id ?? string.Empty;
            if (idStr.IsNullOrEmpty())
            {
                continue;
            }
            liveIds.Add(idStr);
            if (!_firstSeen.TryGetValue(idStr, out var start))
            {
                start = item.start;
                _firstSeen[idStr] = start;
            }
            var (lastUp, lastDown) = _last.GetValueOrDefault(idStr, ((ulong)0, (ulong)0));
            var upSpeed = item.upload >= lastUp ? item.upload - lastUp : 0;
            var downSpeed = item.download >= lastDown ? item.download - lastDown : 0;
            _last[idStr] = (item.upload, item.download);

            var host = $"{(item.metadata.host.IsNullOrEmpty() ? item.metadata.destinationIP : item.metadata.host)}:{item.metadata.destinationPort}";
            var isDns = IsDnsConnection(null, null, host);
            if (HostFilter.IsNotEmpty() && !host.Contains(HostFilter))
            {
                continue;
            }
            if (DnsFilter == "dns" && !isDns)
            {
                continue;
            }
            if (DnsFilter == "app" && isDns)
            {
                continue;
            }

            lstModel.Add(new XrayConnectionModel
            {
                Id = idStr,
                Host = host,
                Network = item.metadata.network,
                Type = item.metadata.type,
                Inbound = "-",
                Outbound = string.Join("->", item.chains ?? []),
                Process = item.metadata.process,
                ProcessPath = item.metadata.processPath,
                IsDns = isDns,
                DownSpeed = Utils.HumanFy((long)downSpeed) + "/s",
                UpSpeed = Utils.HumanFy((long)upSpeed) + "/s",
                DownTotal = Utils.HumanFy((long)item.download),
                UpTotal = Utils.HumanFy((long)item.upload),
                Time = (dtNow - start).TotalSeconds < 0 ? 1 : (dtNow - start).TotalSeconds,
                Elapsed = (dtNow - start).ToString(@"hh\:mm\:ss"),
            });
        }

        PruneDead(liveIds);

        RxSchedulers.MainThreadScheduler.Schedule(() =>
        {
            ConnectionItems.Clear();
            ConnectionItems.AddRange(lstModel);
        });
        await Task.CompletedTask;
    }

    private void PruneDead(HashSet<string> liveIds)
    {
        foreach (var deadId in _firstSeen.Keys.Where(id => !liveIds.Contains(id)).ToList())
        {
            _firstSeen.Remove(deadId);
            _last.Remove(deadId);
        }
    }

    /// <summary>
    /// A connection counts as DNS when it talks to a DNS port, or when either
    /// tag belongs to the kernel's own DNS plumbing (the "dns" outbound,
    /// v2rayN's "dns-module" DoH inbound, "direct-dns-N" direct DNS inbound).
    /// Those connections originate inside the core itself, which is exactly
    /// why they carry no process name.
    /// </summary>
    private static bool IsDnsConnection(string? inbound, string? outbound, string host)
    {
        if (host.EndsWith(":53"))
        {
            return true;
        }
        if (inbound.IsNotEmpty() && inbound.Contains("dns", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (outbound.IsNotEmpty() && outbound.Contains("dns", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return false;
    }
}
