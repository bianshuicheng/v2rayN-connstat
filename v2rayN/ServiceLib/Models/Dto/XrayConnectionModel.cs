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
    public string? DownSpeed { get; set; }
    public string? UpSpeed { get; set; }
    public string? DownTotal { get; set; }
    public string? UpTotal { get; set; }
    public double Time { get; set; }
    public string? Elapsed { get; set; }
}
