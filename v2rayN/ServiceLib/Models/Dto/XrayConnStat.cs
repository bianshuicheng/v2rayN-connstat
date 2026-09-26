namespace ServiceLib.Models.Dto;

/// <summary>
/// Per-connection stats exposed by the xray connstat patch
/// (app/metrics /debug/vars -> "connstat" key).
/// </summary>
public class XrayMetricsVars
{
    public List<XrayConnStatItem>? connstat { get; set; }
}

public class XrayConnStatItem
{
    public long id { get; set; }
    public string? dest { get; set; }
    public string? inbound { get; set; }
    public string? outbound { get; set; }
    public string? process { get; set; }
    public int pid { get; set; }
    public string? path { get; set; }
    public ulong uplink { get; set; }
    public ulong downlink { get; set; }
}
