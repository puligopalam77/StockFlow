namespace StockFlow.Application.Common;

// Formats current explicitly entered address components, never historical snapshots.
public static class PostalAddress
{
    public static string Format(string? line1,string? line2,string? city=null,string? district=null,string? state=null,string? pin=null)
        => string.Join("\n",new[]{line1?.Trim(),line2?.Trim(),
            string.Join(", ",new[]{city,district}.Where(v=>!string.IsNullOrWhiteSpace(v)).Select(v=>v!.Trim())),
            string.Join(" \u2014 ",new[]{state,pin}.Where(v=>!string.IsNullOrWhiteSpace(v)).Select(v=>v!.Trim()))}
            .Where(v=>!string.IsNullOrWhiteSpace(v)));
}
