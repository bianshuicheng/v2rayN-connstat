namespace ServiceLib.ViewModels;

/// <summary>
/// Live per-connection view for the Xray core, fed by the connstat patch
/// (metrics /debug/vars -> "connstat"). sing-box keeps using
/// ClashConnectionsViewModel; this view is Xray-only.
/// </summary>
public partial class XrayConnectionsViewModel : MyReactiveObject
{
    public BulkObservableCollection<XrayConnectionModel> ConnectionItems { get; } = [];

    [Reactive]
    public partial string HostFilter { get; set; }

    [Reactive]
    public partial bool AutoRefresh { get; set; }

    private readonly Dictionary<long, DateTime> _firstSeen = new();
    private readonly Dictionary<long, (ulong up, ulong down)> _last = new();

    public XrayConnectionsViewModel()
    {
        _config = AppManager.Instance.Config;
        // always start with auto refresh on: the stored setting is shared with
        // the sing-box connections view and defaults to false there
        AutoRefresh = true;

        _ = Task.Run(Run);
    }

    private async Task Run()
    {
        while (true)
        {
            await Task.Delay(1000);
            try
            {
                if (!(AutoRefresh && AppManager.Instance.IsRunningCore(ECoreType.Xray)))
                {
                    continue;
                }

                var url = $"{Global.HttpProtocol}{Global.Loopback}:{AppManager.Instance.StatePort}/debug/vars";
                var result = await HttpClientHelper.Instance.TryGetAsync(url);
                var connlist = JsonUtils.Deserialize<XrayMetricsVars>(result ?? string.Empty)?.connstat;
                if (connlist == null)
                {
                    continue;
                }

                var dtNow = DateTime.Now;
                var lstModel = new List<XrayConnectionModel>();
                var liveIds = new HashSet<long>();
                foreach (var item in connlist)
                {
                    liveIds.Add(item.id);
                    if (!_firstSeen.TryGetValue(item.id, out var start))
                    {
                        start = dtNow;
                        _firstSeen[item.id] = start;
                    }
                    var (lastUp, lastDown) = _last.GetValueOrDefault(item.id, ((ulong)0, (ulong)0));
                    var upSpeed = item.uplink >= lastUp ? item.uplink - lastUp : 0;
                    var downSpeed = item.downlink >= lastDown ? item.downlink - lastDown : 0;
                    _last[item.id] = (item.uplink, item.downlink);

                    var host = item.dest ?? string.Empty;
                    if (HostFilter.IsNotEmpty() && !host.Contains(HostFilter))
                    {
                        continue;
                    }

                    lstModel.Add(new XrayConnectionModel
                    {
                        Id = item.id,
                        Host = host,
                        Network = "tcp",
                        Inbound = item.inbound,
                        Outbound = item.outbound,
                        Process = item.process,
                        ProcessPath = item.path,
                        DownSpeed = Utils.HumanFy((long)downSpeed) + "/s",
                        UpSpeed = Utils.HumanFy((long)upSpeed) + "/s",
                        DownTotal = Utils.HumanFy((long)item.downlink),
                        UpTotal = Utils.HumanFy((long)item.uplink),
                        Time = (dtNow - start).TotalSeconds < 0 ? 1 : (dtNow - start).TotalSeconds,
                        Elapsed = (dtNow - start).ToString(@"hh\:mm\:ss"),
                    });
                }

                foreach (var deadId in _firstSeen.Keys.Where(id => !liveIds.Contains(id)).ToList())
                {
                    _firstSeen.Remove(deadId);
                    _last.Remove(deadId);
                }

                RxSchedulers.MainThreadScheduler.Schedule(() =>
                {
                    ConnectionItems.Clear();
                    ConnectionItems.AddRange(lstModel);
                });
            }
            catch
            {
                // ignored
            }
        }
    }
}
