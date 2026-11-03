using System.Text.RegularExpressions;

namespace StockFlow.Web.Help;

// Bounded tools: a model selects a tool, never a database query or access scope.
public static class OperationsQuestions
{
    public static readonly string[] Current = ["pending-purchases", "reorder", "overdue-customers", "overdue-repairs", "ready-repair-list", "repair-deliveries", "pending-transfers", "warranty-detail"];
    public static readonly string[] Dated = ["top-products", "slow-products", "sales-comparison"];
    public static bool Supports(string metric) => Current.Contains(metric) || Dated.Contains(metric);

    public static SalesQuestion? Parse(string question)
    {
        if (SalesQuestion.HasUnsupportedFilters(question)) return null;
        var text = Regex.Replace(question.Trim().TrimEnd('?', '.', '!'), @"\s+", " ").ToLowerInvariant();
        text = Regex.Replace(text, @"^(please |show me |tell me )", "");
        var metric = text switch {
            "which purchase orders are still waiting for stock" or "pending purchase deliveries" or "pending purchase orders" or "which purchases are pending delivery" => "pending-purchases",
            "which products need purchasing, based on minimum stock" or "which products need purchasing based on minimum stock" or "which products need purchasing" or "reorder suggestions" or "which products need reordering" => "reorder",
            "which customers have overdue dues" or "overdue customer payments" or "which customers have overdue payments" => "overdue-customers",
            "which repairs are overdue" or "overdue repairs" => "overdue-repairs",
            "which repairs are ready for collection" or "repairs ready for collection" => "ready-repair-list",
            "which repairs are overdue or ready for collection" or "repair delivery tracking" => "repair-deliveries",
            "which transfers are pending receipt" or "pending stock transfers" or "stock transfer status" => "pending-transfers",
            _ => null
        };
        if (metric is not null) return new(metric, "current");
        var serial = Regex.Match(question.Trim().TrimEnd('?', '.', '!'), @"^(?:when does (?:the )?warranty expire for|warranty (?:details|expiry) for) (?:this )?(?:serial(?: number)?\s*[:#]?\s*)?(.*)$", RegexOptions.IgnoreCase);
        if (serial.Success) return new("warranty-detail", "current", Term: Regex.IsMatch(serial.Groups[1].Value.Trim(),@"^(?:this )?serial(?: number)?$",RegexOptions.IgnoreCase) ? null : serial.Groups[1].Value.Trim());
        if (Regex.IsMatch(text, @"^(?:when does (?:the )?warranty expire|warranty details)$")) return new("warranty-detail", "current");
        var compare = Regex.Match(text, @"^compare (?:this (week|month)(?:['\u2019]s)? sales with last \1|sales this (week|month) (?:with|vs|versus) last \2)$");
        if (compare.Success) return new("sales-comparison", compare.Groups[1].Success ? "this-"+compare.Groups[1].Value : "this-"+compare.Groups[2].Value);
        var ranking = Regex.Match(text, @"^(?:which products sold (most|least)|(?:show )?(top-selling|slow-moving) products)(?: (today|yesterday|this week|last week|this month|last month|on \d{4}-\d{2}-\d{2}|from \d{4}-\d{2}-\d{2} to \d{4}-\d{2}-\d{2}))?$");
        if (!ranking.Success) return null;
        var rankMetric = ranking.Groups[1].Value == "least" || ranking.Groups[2].Value == "slow-moving" ? "slow-products" : "top-products";
        var period = SalesQuestion.Parse("sales amount " + ranking.Groups[3].Value);
        return period is null ? new(rankMetric, "missing") : period with { Metric=rankMetric };
    }
}
