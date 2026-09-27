namespace ServiceLib.Models.Dto;

/// <summary>
/// One live connection row for the Xray connections view
/// (data source: xray connstat patch, metrics /debug/vars "connstat").
/// </summary>
public class XrayConnectionModel
{
    public string? Id { get; set; }
    public string? Host { get; set; }
    public string? Network { get; set; }
    public string? Type { get; set; }
    public string? Inbound { get; set; }
    public string? Outbound { get; set; }
    public string? Process { get; set; }
    public string? ProcessPath { get; set; }
    public string? Src { get; set; }
    public bool IsDns { get; set; }
    public string? DownSpeed { get; set; }
    public string? UpSpeed { get; set; }
    public string? DownTotal { get; set; }
    public string? UpTotal { get; set; }
    public double Time { get; set; }
    public string? Elapsed { get; set; }

    // Numeric sort keys. The four fields above are pre-formatted display
    // strings ("2.0 MB/s"), and DataGrid would order those lexicographically,
    // which is not magnitude order. The columns point SortMemberPath here.
    public double DownSpeedVal { get; set; }
    public double UpSpeedVal { get; set; }
    public double DownTotalVal { get; set; }
    public double UpTotalVal { get; set; }
}
