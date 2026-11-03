namespace StockFlow.Application.DTOs.Operations;

public class ScreenLanguageSetting
{
    public string Screen { get; set; } = "";
    public string Language { get; set; } = "en";
    public Dictionary<string, string> Captions { get; set; } = new();
}
