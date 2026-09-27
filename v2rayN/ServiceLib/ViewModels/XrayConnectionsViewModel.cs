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
            var host = item.dest ?? string.Empty;
            // the core's connection id restarts from 1 on every core restart,
            // and the sing-box fallback below shares these dictionaries, so the
            // baseline key must carry the destination as well
            var idStr = item.id.ToString();
            var key = $"{idStr}|{host}";
            liveIds.Add(key);
            var seen = _last.ContainsKey(key);
            if (!_firstSeen.TryGetValue(key, out var start))
            {
                start = dtNow;
                _firstSeen[key] = start;
            }
            var (lastUp, lastDown) = _last.GetValueOrDefault(key, ((ulong)0, (ulong)0));
            // on the first sight of a connection the cumulative counters must
            // not be shown as a per-second rate
            var upSpeed = seen && item.uplink >= lastUp ? item.uplink - lastUp : 0;
            var downSpeed = seen && item.downlink >= lastDown ? item.downlink - lastDown : 0;
            _last[key] = (item.uplink, item.downlink);

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
                Src = item.src,
                IsDns = isDns,
                DownSpeed = FmtBytes(downSpeed) + "/s",
                UpSpeed = FmtBytes(upSpeed) + "/s",
                DownTotal = FmtBytes(item.downlink),
                UpTotal = FmtBytes(item.uplink),
                DownSpeedVal = downSpeed,
                UpSpeedVal = upSpeed,
                DownTotalVal = item.downlink,
                UpTotalVal = item.uplink,
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
            var host = $"{(item.metadata.host.IsNullOrEmpty() ? item.metadata.destinationIP : item.metadata.host)}:{item.metadata.destinationPort}";
            // shared baselines with the connstat path: keep the key unique
            var key = $"{idStr}|{host}";
            liveIds.Add(key);
            var seen = _last.ContainsKey(key);
            if (!_firstSeen.TryGetValue(key, out var start))
            {
                start = item.start;
                _firstSeen[key] = start;
            }
            var (lastUp, lastDown) = _last.GetValueOrDefault(key, ((ulong)0, (ulong)0));
            var upSpeed = seen && item.upload >= lastUp ? item.upload - lastUp : 0;
            var downSpeed = seen && item.download >= lastDown ? item.download - lastDown : 0;
            _last[key] = (item.upload, item.download);

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
                DownSpeed = FmtBytes(downSpeed) + "/s",
                UpSpeed = FmtBytes(upSpeed) + "/s",
                DownTotal = FmtBytes(item.download),
                UpTotal = FmtBytes(item.upload),
                DownSpeedVal = downSpeed,
                UpSpeedVal = upSpeed,
                DownTotalVal = item.download,
                UpTotalVal = item.upload,
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
    /// Formats a byte count. Utils.HumanFy expects its argument in KB (see
    /// StatisticsXrayService, which divides by linkBase=1024 before calling it),
    /// so passing raw bytes there would inflate every value by 1024x.
    /// </summary>
    private static string FmtBytes(double bytes)
    {
        if (bytes < 1024)
        {
            return $"{(long)bytes} B";
        }

        string[] units = ["KB", "MB", "GB", "TB", "PB"];
        var i = -1;
        for (; bytes >= 1024 && i < units.Length - 1; i++)
        {
            bytes /= 1024;
        }

        return $"{bytes:f1} {units[i]}";
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
