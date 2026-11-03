namespace StockFlow.Mobile;

public static class DevicePolicy
{
    public const int MaxDownloadBytes = 20 * 1024 * 1024;
    public static string FileName(string? name)
    {
        var value = new string((name ?? "stockflow-document").Take(120)
            .Select(c => char.IsControl(c) || "\\/:*?\"<>|".Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        return string.IsNullOrWhiteSpace(value) ? "stockflow-document" : value;
    }
    public static string Mime(string? mime) => mime?.Split(';')[0].Trim().ToLowerInvariant() switch {
        "application/pdf" => "application/pdf", "text/csv" => "text/csv",
        "application/json" => "application/json", "text/plain" => "text/plain",
        "image/png" => "image/png", "image/jpeg" => "image/jpeg",
        _ => "application/octet-stream"
    };
}
