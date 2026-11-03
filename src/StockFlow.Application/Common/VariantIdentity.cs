using System.Globalization;
using System.Text.Json;
namespace StockFlow.Application.Common;

public static class VariantIdentity
{
    public static Dictionary<string,string> Read(string? json) => string.IsNullOrWhiteSpace(json)
        ? [] : JsonSerializer.Deserialize<Dictionary<string,string>>(json) ?? [];
    public static string Summary(IEnumerable<KeyValuePair<string,string>> values) => string.Join(" · ",
        values.Where(v=>!string.IsNullOrWhiteSpace(v.Value)).Select(v=>
            CultureInfo.InvariantCulture.TextInfo.ToTitleCase(v.Key.Replace('_',' '))+": "+v.Value));
    public static string Summary(string? json) => Summary(Read(json));
}
