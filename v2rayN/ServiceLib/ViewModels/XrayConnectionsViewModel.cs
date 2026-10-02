namespace ServiceLib.ViewModels;

public partial class XrayConnectionsViewModel : MyReactiveObject
{
    public BulkObservableCollection<XrayRow> Rows { get; } = [];

    [Reactive]
    public partial int GroupKind { get; set; }

    [Reactive]
    public partial string TextFilter { get; set; }

    [Reactive]
    public partial bool AutoRefresh { get; set; }

    [Reactive]
    public partial bool ShowClosed { get; set; }

    [Reactive]
    public partial string Summary { get; set; }

    [Reactive]
    public partial string KernelMemory { get; set; }

    /// <summary>悬停提示：内核内存的构成细节（Go 堆 / Go 系统预留）。</summary>
    [Reactive]
    public partial string KernelMemoryDetail { get; set; }

    [Reactive]
    public partial string Notice { get; set; }

    private readonly Dictionary<string, XrayRow> _rows = [];
    private List<XrayRow> _visible = [];
    private List<XrayFlowItem> _flows = [];
    private int _failures;

    /// <summary>Flows seen on the previous poll, keyed by id — the diff source for the closed tab.</summary>
    private readonly Dictionary<long, XrayFlowItem> _lastFlows = [];

    /// <summary>Flows that vanished from a poll, kept so the 已关闭 tab can show what ended.</summary>
    private readonly Dictionary<long, ClosedFlow> _closedArchive = [];

    private const int ClosedArchiveMax = 300;
    private static readonly TimeSpan ClosedArchiveTtl = TimeSpan.FromMinutes(30);

    private readonly record struct ClosedFlow(XrayFlowItem Flow, DateTime ClosedAt);

    /// <summary>Aggregation unit: one program talking to one host.</summary>
    private sealed class Pair
    {
        public string Program = string.Empty;
        public string Host = string.Empty;
        public bool Self;
        public long Uplink;
        public long Downlink;
        public long UpBps;
        public long DownBps;
        public int Connections;
        public readonly HashSet<int> Pids = [];
        public readonly HashSet<string> Inbound = [];
        public readonly HashSet<string> Outbound = [];
        public readonly HashSet<string> Network = [];
        public readonly HashSet<string> Detected = [];
        public string Exe = string.Empty;
        public long NewestAge;
        public bool Closing;
    }

    public XrayConnectionsViewModel()
    {
        _config = AppManager.Instance.Config;
        AutoRefresh = _config.ClashUIItem.ConnectionsAutoRefresh;
        TextFilter = string.Empty;

        this.WhenAnyValue(x => x.AutoRefresh)
            .Subscribe(c => { _config.ClashUIItem.ConnectionsAutoRefresh = AutoRefresh; });
        this.WhenAnyValue(x => x.GroupKind).Subscribe(_ => Rebuild());
        this.WhenAnyValue(x => x.TextFilter).Subscribe(_ => Rebuild());
        this.WhenAnyValue(x => x.ShowClosed).Subscribe(_ => Rebuild());

        _ = Start();
    }

    private async Task Start()
    {
        await Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(1000);
                if (!AutoRefresh || !AppManager.Instance.ShowInTaskbar)
                {
                    continue;
                }

                if (!AppManager.Instance.IsRunningCore(ECoreType.Xray))
                {
                    RxSchedulers.MainThreadScheduler.Schedule(Reset);
                    continue;
                }

                var vars = await XrayFlowApiManager.Instance.GetFlowVarsAsync();
                if (vars == null)
                {
                    RxSchedulers.MainThreadScheduler.Schedule(OnMissedPoll);
                    continue;
                }

                RxSchedulers.MainThreadScheduler.Schedule(() => Apply(vars));
            }
        });
    }

    private void Reset()
    {
        _flows = [];
        Rebuild();
        Summary = string.Empty;
        KernelMemory = string.Empty;
        KernelMemoryDetail = string.Empty;
        Notice = AppManager.Instance.IsRunningCore(ECoreType.Xray) ? string.Empty : ResUI.TbXrayNotXrayCore;
    }

    /// <summary>
    /// A poll that produced nothing must not leave the old numbers standing there: rates
    /// go to zero, totals stay until the kernel says otherwise.
    /// </summary>
    private void OnMissedPoll()
    {
        _failures++;
        if (_failures < 2)
        {
            return;
        }

        foreach (var row in _rows.Values)
        {
            row.ClearIdle();
        }
        Summary = string.Format(ResUI.TbXraySummary, 0, XraySpeedText.HumanBps(0), XraySpeedText.HumanBps(0));
        Notice = _failures > 3 ? ResUI.TbXrayNoData : string.Empty;
    }

    private void Apply(XrayFlowVars vars)
    {
        _failures = 0;
        Notice = string.Empty;
        _flows = vars.flowwatch?.flows ?? [];
        TrackClosed(_flows);

        Build();
        Rebuild();

        var totals = vars.flowwatch?.totals;
        Summary = string.Format(ResUI.TbXraySummary,
            totals?.flows ?? 0,
            XraySpeedText.HumanBps(totals?.upBps ?? 0),
            XraySpeedText.HumanBps(totals?.downBps ?? 0));
        KernelMemory = BuildKernelMemory(vars.memstats);
        KernelMemoryDetail = BuildKernelMemoryDetail(vars.memstats);
    }

    /// <summary>
    /// Flows that were on the previous poll but vanished from this one have closed. The
    /// kernel only keeps a closed flow for a couple of seconds, so the archive here is
    /// what lets the 已关闭 tab show anything at all.
    /// </summary>
    private void TrackClosed(List<XrayFlowItem> flows)
    {
        var current = new Dictionary<long, XrayFlowItem>();
        foreach (var flow in flows)
        {
            current[flow.id] = flow;
        }

        var now = DateTime.Now;
        foreach (var (id, flow) in _lastFlows)
        {
            if (!current.ContainsKey(id))
            {
                _closedArchive[id] = new ClosedFlow(flow, now);
            }
        }
        _lastFlows.Clear();
        foreach (var kv in current)
        {
            _lastFlows[kv.Key] = kv.Value;
        }

        if (_closedArchive.Count > ClosedArchiveMax)
        {
            foreach (var id in _closedArchive.OrderBy(kv => kv.Value.ClosedAt)
                         .Take(_closedArchive.Count - ClosedArchiveMax)
                         .Select(kv => kv.Key)
                         .ToList())
            {
                _closedArchive.Remove(id);
            }
        }
        var cutoff = now - ClosedArchiveTtl;
        foreach (var id in _closedArchive.Where(kv => kv.Value.ClosedAt < cutoff).Select(kv => kv.Key).ToList())
        {
            _closedArchive.Remove(id);
        }
    }

    /// <summary>Closed flows, newest first, for the 已关闭 tab.</summary>
    private List<(XrayFlowItem Flow, DateTime ClosedAt)> ClosedSnapshot()
    {
        return _closedArchive.Values.OrderByDescending(c => c.ClosedAt).Select(c => (c.Flow, c.ClosedAt)).ToList();
    }

    /// <summary>One archived closed flow per row, rates zeroed, closing time in the state column.</summary>
    private void BuildClosed()
    {
        var rows = new List<XrayRow>();
        foreach (var (flow, closedAt) in ClosedSnapshot())
        {
            var row = RowFor("closed:" + flow.id, false);
            row.Program = ProgramName(flow);
            row.TargetHost = flow.target ?? string.Empty;
            row.IconPath = flow.self ? string.Empty : (flow.exe ?? string.Empty);
            row.SetTotals(flow.uplink, flow.downlink, 0, 0);
            row.Connections = 1;
            row.Domains = null;
            row.Pids = flow.pid > 0 ? flow.pid.ToString() : string.Empty;
            row.Inbound = flow.inbound ?? string.Empty;
            row.Outbound = flow.outbound ?? string.Empty;
            row.Network = flow.net ?? string.Empty;
            row.Detected = flow.detected ?? string.Empty;
            row.Source = flow.src ?? string.Empty;
            row.Age = $"{flow.ageSec}s";
            row.State = $"{ResUI.TbXrayClosed} {closedAt:HH:mm:ss}";
            row.Exe = flow.exe ?? string.Empty;
            rows.Add(row);
        }

        _visible = Filter(rows);
    }

    private void Build()
    {
        if (ShowClosed)
        {
            BuildClosed();
            return;
        }

        var pairs = new Dictionary<string, Pair>();
        foreach (var flow in _flows)
        {
            var program = ProgramName(flow);
            var host = HostOf(flow.target) ?? string.Empty;
            var key = program + "\n" + host;
            if (!pairs.TryGetValue(key, out var pair))
            {
                pair = new Pair { Program = program, Host = host };
                pairs[key] = pair;
            }

            pair.Uplink += flow.uplink;
            pair.Downlink += flow.downlink;
            pair.UpBps += flow.upBps;
            pair.DownBps += flow.downBps;
            pair.Connections++;
            if (flow.pid > 0)
            {
                pair.Pids.Add(flow.pid);
            }
            if (flow.exe.IsNotEmpty())
            {
                pair.Exe = flow.exe;
            }
            AddTag(pair.Inbound, flow.inbound);
            AddTag(pair.Outbound, flow.outbound);
            AddTag(pair.Network, flow.net);
            AddTag(pair.Detected, flow.detected);
            pair.NewestAge = Math.Max(pair.NewestAge, flow.ageSec);
            pair.Closing |= flow.closing;
            pair.Self |= flow.self;
        }

        switch (GroupKind)
        {
            case 0:
                BuildByProgram(pairs);
                break;
            case 2:
            case 3:
                BuildByTag(pairs, GroupKind == 2);
                break;
            case 4:
                BuildByConnection();
                break;
            default:
                BuildFlat(pairs);
                break;
        }
    }

    private void BuildByProgram(Dictionary<string, Pair> pairs)
    {
        var byProgram = new Dictionary<string, List<Pair>>();
        foreach (var pair in pairs.Values)
        {
            if (!byProgram.TryGetValue(pair.Program, out var list))
            {
                list = [];
                byProgram[pair.Program] = list;
            }
            list.Add(pair);
        }

        var entries = new List<(XrayRow Group, List<XrayRow> Children)>();
        foreach (var (program, list) in byProgram)
        {
            var group = RowFor("group:app:" + program, true);
            group.Program = program;
            group.TargetHost = string.Empty;
            group.IconPath = list.FirstOrDefault(p => p.Exe.IsNotEmpty() && !p.Self)?.Exe ?? string.Empty;
            group.Connections = list.Sum(p => p.Connections);
            group.Domains = list.Count;
            group.Pids = Join(list.SelectMany(p => p.Pids).Distinct());
            group.Exe = list.FirstOrDefault(p => p.Exe.IsNotEmpty())?.Exe ?? string.Empty;
            group.SetTotals(list.Sum(p => p.Uplink), list.Sum(p => p.Downlink), list.Sum(p => p.UpBps), list.Sum(p => p.DownBps));
            // 程序与它的域名/IP 合并成一张常开卡片：分组头就是汇总，域名/IP 直接铺在下面
            group.Expanded = true;
            group.Marker = string.Empty;

            var children = list.Select(p => PairRow("pair:" + p.Program + "|" + p.Host, p, nameInProgramColumn: true)).ToList();
            SortPairs(children);
            entries.Add((group, children));
        }

        SortGroups(entries);
    }

    private void BuildByTag(Dictionary<string, Pair> pairs, bool outbound)
    {
        var byTag = new Dictionary<string, List<Pair>>();
        foreach (var pair in pairs.Values)
        {
            var tags = outbound ? pair.Outbound : pair.Inbound;
            foreach (var tag in tags.DefaultIfEmpty("-"))
            {
                if (!byTag.TryGetValue(tag, out var list))
                {
                    list = [];
                    byTag[tag] = list;
                }
                list.Add(pair);
            }
        }

        var entries = new List<(XrayRow Group, List<XrayRow> Children)>();
        foreach (var (tag, list) in byTag)
        {
            var group = RowFor("group:tag:" + tag, true);
            group.Program = tag;
            group.TargetHost = string.Empty;
            group.IconPath = string.Empty;
            group.Connections = list.Sum(p => p.Connections);
            group.Domains = list.Select(p => p.Host).Distinct().Count();
            group.Pids = Join(list.SelectMany(p => p.Pids).Distinct());
            group.SetTotals(list.Sum(p => p.Uplink), list.Sum(p => p.Downlink), list.Sum(p => p.UpBps), list.Sum(p => p.DownBps));

            var children = list.Select(p => PairRow("pair:" + p.Program + "|" + p.Host, p, nameInProgramColumn: false)).ToList();
            SortPairs(children);
            entries.Add((group, children));
        }

        SortGroups(entries);
    }

    /// <summary>域名/IP 分类：一行一个"程序-域名"，不再套一层程序分组。</summary>
    private void BuildFlat(Dictionary<string, Pair> pairs)
    {
        var rows = pairs.Values
            .Select(p => PairRow("pair:" + p.Program + "|" + p.Host, p, nameInProgramColumn: false))
            .ToList();
        SortPairs(rows);
        _visible = Filter(rows);
    }

    private void BuildByConnection()
    {
        var rows = new List<XrayRow>();
        foreach (var flow in _flows)
        {
            var row = RowFor("flow:" + flow.id, false);
            row.Program = ProgramName(flow);
            row.TargetHost = flow.target ?? string.Empty;
            row.IconPath = flow.self ? string.Empty : (flow.exe ?? string.Empty);
            row.SetTotals(flow.uplink, flow.downlink, flow.upBps, flow.downBps);
            row.Connections = 1;
            row.Domains = null;
            row.Pids = flow.pid > 0 ? flow.pid.ToString() : string.Empty;
            row.Inbound = flow.inbound ?? string.Empty;
            row.Outbound = flow.outbound ?? string.Empty;
            row.Network = flow.net ?? string.Empty;
            row.Detected = flow.detected ?? string.Empty;
            row.Source = flow.src ?? string.Empty;
            row.Age = $"{flow.ageSec}s";
            row.State = flow.closing ? ResUI.TbXrayClosing : ResUI.TbXrayOpen;
            row.Exe = flow.exe ?? string.Empty;
            rows.Add(row);
        }

        rows.Sort((a, b) => (b.Uplink + b.Downlink).CompareTo(a.Uplink + a.Downlink));
        _visible = Filter(rows);
    }

    private XrayRow PairRow(string key, Pair pair, bool nameInProgramColumn)
    {
        var row = RowFor(key, false);
        if (nameInProgramColumn)
        {
            // 程序分类下，域名直接挂在程序下面，不再另开一列
            row.Program = pair.Host;
            row.TargetHost = string.Empty;
            row.IconPath = string.Empty;
        }
        else
        {
            row.Program = pair.Program;
            row.TargetHost = pair.Host;
            row.IconPath = pair.Self ? string.Empty : pair.Exe;
        }

        row.SetTotals(pair.Uplink, pair.Downlink, pair.UpBps, pair.DownBps);
        row.Connections = pair.Connections;
        row.Domains = null;
        row.Pids = Join(pair.Pids);
        row.Inbound = Join(pair.Inbound);
        row.Outbound = Join(pair.Outbound);
        row.Network = Join(pair.Network);
        row.Detected = Join(pair.Detected);
        row.Source = string.Empty;
        row.Age = $"{pair.NewestAge}s";
        row.State = pair.Closing ? ResUI.TbXrayClosing : ResUI.TbXrayOpen;
        row.Exe = pair.Exe;
        return row;
    }

    private static void SortPairs(List<XrayRow> rows)
    {
        rows.Sort((a, b) => (b.Uplink + b.Downlink).CompareTo(a.Uplink + a.Downlink));
    }

    private void SortGroups(List<(XrayRow Group, List<XrayRow> Children)> entries)
    {
        entries.Sort((a, b) => (b.Group.Uplink + b.Group.Downlink).CompareTo(a.Group.Uplink + a.Group.Downlink));

        var filter = TextFilter;
        var visible = new List<XrayRow>();
        foreach (var entry in entries)
        {
            var groupMatches = Matches(entry.Group, filter);
            var children = groupMatches
                ? entry.Children
                : entry.Children.Where(c => Matches(c, filter)).ToList();
            if (!groupMatches && children.Count == 0)
            {
                continue;
            }

            visible.Add(entry.Group);
            if (entry.Group.Expanded)
            {
                visible.AddRange(children);
            }
        }

        _visible = visible;
    }

    private List<XrayRow> Filter(List<XrayRow> rows)
    {
        var filter = TextFilter;
        if (filter.IsNullOrEmpty())
        {
            return rows;
        }

        return rows.Where(r => Matches(r, filter)).ToList();
    }

    private static bool Matches(XrayRow row, string? filter)
    {
        if (filter.IsNullOrEmpty())
        {
            return true;
        }

        return (row.Program.IsNotEmpty() && row.Program.Contains(filter, StringComparison.OrdinalIgnoreCase))
               || (row.TargetHost.IsNotEmpty() && row.TargetHost.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    private void Rebuild()
    {
        var desired = _visible;
        var changed = desired.Count != Rows.Count;
        if (!changed)
        {
            for (var i = 0; i < desired.Count; i++)
            {
                if (!ReferenceEquals(desired[i], Rows[i]))
                {
                    changed = true;
                    break;
                }
            }
        }

        if (changed)
        {
            Rows.Clear();
            Rows.AddRange(desired);
        }
    }

    public void Toggle(XrayRow? row)
    {
        if (GroupKind == 0)
        {
            // 程序分类是合并卡片，程序与域名/IP 常显一体，没有折叠态
            return;
        }

        if (row is not { IsGroup: true })
        {
            return;
        }

        row.Toggle();
        Rebuild();
    }

    public void SetClosed(bool closed)
    {
        ShowClosed = closed;
    }

    public void CollapseAll()
    {
        SetExpanded(false);
    }

    public void ExpandAll()
    {
        SetExpanded(true);
    }

    private void SetExpanded(bool expanded)
    {
        if (GroupKind == 0)
        {
            return;
        }

        foreach (var row in _rows.Values.Where(r => r.IsGroup))
        {
            row.Expanded = expanded;
            row.Marker = expanded ? "▼" : "▶";
        }
        Rebuild();
    }

    private XrayRow RowFor(string key, bool isGroup)
    {
        if (!_rows.TryGetValue(key, out var row))
        {
            row = new XrayRow(key, isGroup);
            _rows[key] = row;
        }
        return row;
    }

    private static void AddTag(HashSet<string> target, string? value)
    {
        if (value.IsNotEmpty())
        {
            target.Add(value);
        }
    }

    private static string Join<T>(IEnumerable<T> values)
    {
        return string.Join(",", values);
    }

    private static string DisplayName(string? app)
    {
        return app.IsNotEmpty() ? app : ResUI.TbXrayUnresolved;
    }

    /// <summary>Program cell text; the kernel's own traffic is called out instead of "xray".</summary>
    private static string ProgramName(XrayFlowItem flow)
    {
        if (flow.self)
        {
            return ResUI.TbXraySelf;
        }

        return DisplayName(flow.app);
    }

    /// <summary>Host part of a "host:port" target, so a domain and its port stay one row.</summary>
    private static string? HostOf(string? target)
    {
        if (target.IsNullOrEmpty())
        {
            return null;
        }

        var index = target.LastIndexOf(':');
        return index > 0 ? target[..index] : target;
    }

    /// <summary>状态栏只显示一个总占用数值（工作集）；构成细节放悬停提示。</summary>
    private string BuildKernelMemory(XrayMemStats? mem)
    {
        var workingSet = CoreManager.Instance.RunningCoreWorkingSet();
        return workingSet.HasValue ? XraySpeedText.HumanBytes(workingSet.Value) : string.Empty;
    }

    private string BuildKernelMemoryDetail(XrayMemStats? mem)
    {
        if (mem == null)
        {
            return string.Empty;
        }

        var parts = new List<string>
        {
            $"{ResUI.TbXrayGoHeap} {XraySpeedText.HumanBytes(mem.HeapAlloc)}",
            $"{ResUI.TbXrayGoSys} {XraySpeedText.HumanBytes(mem.Sys)}"
        };
        return string.Join("    ", parts);
    }
}
