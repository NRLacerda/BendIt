namespace BendIt.Api.TestBattery;

internal sealed class ExecutedRequest
{
    public int StatusCode { get; set; }
    public string StatusText { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string Body { get; set; } = "";
    public int BodySizeBytes { get; set; }
    public int DurationMs { get; set; }
    public bool Truncated { get; set; }
    public Dictionary<string, string> ResponseHeaders { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Error { get; set; }
}
