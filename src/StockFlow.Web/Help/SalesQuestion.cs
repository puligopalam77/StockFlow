using System.Globalization;
using System.Text.RegularExpressions;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Help;

// Approved read-only business tools and validated filters. Models cannot supply SQL, URLs or access scope.
public record SalesQuestion(string Metric, string Period, string? From = null, string? To = null, string? Term = null)
{
    public SalesPeriod? ValidPeriod()
    {
        if (OperationsQuestions.Current.Contains(Metric))
            return Period=="current" && From is null && To is null && (Metric=="warranty-detail" ? Term?.Trim().Length is >=1 and <=150 : Term is null) ? new("current") : null;
        if (OperationsQuestions.Dated.Contains(Metric))
            return Term is null && (Metric!="sales-comparison" || Period is "this-week" or "this-month") ? (this with {Metric="quantity"}).ValidPeriod() : null;
        if (Metric is "customer-dues" or "supplier-balance")
            return Period == "current" && From is null && To is null && (Term is null || Term.Trim().Length is >= 2 and <= 150) ? new("current") : null;
        if (Metric is "product-quantity" or "product-count" or "product-amount")
            return Term?.Trim().Length is >= 2 and <= 150 ? (this with {Metric="quantity",Term=null}).ValidPeriod() : null;
        if (BusinessQuestions.DatedMetrics.Contains(Metric))
            return Term is null ? (this with {Metric="quantity"}).ValidPeriod() : null;
        if (Metric == "product-stock")
            return Period == "current" && From is null && To is null && Term?.Trim().Length is >= 2 and <= 150 ? new("current") : null;
        if (Term is not null) return null;
        if (Metric is "stock" or "low-stock" or "repairs" or "ready-repairs" or "warranties" or "products" or "suppliers" or "customers")
            return Period == "current" && From is null && To is null ? new("current") : null;
        if (Metric is not ("quantity" or "count" or "amount" or "collected")) return null;
        if (Period is "today" or "yesterday" or "day-before-yesterday" or "this-week" or "last-week" or "this-month" or "last-month")
            return From is null && To is null ? new(Period) : null;
        if (Period != "range" || !DateTime.TryParseExact(From, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var start) || !DateTime.TryParseExact(To, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var end) || start > end || (end-start).TotalDays > 366) return null;
        return new("range", start, end);
    }

    public static bool IsValueQuestion(string question) =>
        !Regex.IsMatch(question, @"\bhow\s+(do|can|to|should)\b", RegexOptions.IgnoreCase) &&
        Regex.IsMatch(Normalize(question), @"\b(sold|sales?|bills?|invoices?|collected|collection|payments?|received money)\b", RegexOptions.IgnoreCase) &&
        (Regex.IsMatch(Normalize(question), @"\b(how much|how many|quantity|count|total|amount|units|items|completed|revenue|turnover|money)\b", RegexOptions.IgnoreCase) ||
         // A plain dated sales enquiry means completed invoice revenue, without requiring "amount".
         Regex.IsMatch(Normalize(question), @"\bsales?\b", RegexOptions.IgnoreCase) &&
         Regex.IsMatch(Normalize(question), @"\b(today|yesterday|this week|last week|this month|last month|monthly)\b|\b\d{4}-\d{2}-\d{2}\b|^(what (is|are|was|were)|show|tell)\b", RegexOptions.IgnoreCase));

    private static string Normalize(string question)
    {
        var text = Regex.Replace(question.ToLowerInvariant().Trim().TrimEnd('?', '.', '!'), @"\s+", " ");
        text = Regex.Replace(text, @"\b(amout|ammount|amunt)\b", "amount");
        return Regex.Replace(text, @"\b(tofay|todsy|tody)\b", "today");
    }

    public static bool IsDateOnly(string question) =>
        Regex.IsMatch(Normalize(question), @"^(and |what about |how about )?(today|yesterday|day before yesterday|two days ago|2 days ago|this week|last week|this month|last month|\d{4}-\d{2}-\d{2})$");

    public static bool HasUnsupportedFilters(string question) =>
        Regex.IsMatch(question, @"\b(profit|margin|cashier|employee|staff|credit|net|cancelled|canceled)\b", RegexOptions.IgnoreCase) ||
        (Regex.IsMatch(question,@"\b(service|repairs?)\b",RegexOptions.IgnoreCase) && Regex.IsMatch(question,@"\b(payments?|money|cash|card|upi|amount)\b",RegexOptions.IgnoreCase)) ||
        Regex.IsMatch(question, @"\b(for|by|at|in|from)\s+(store|branch|warehouse|business|tenant|location)\b", RegexOptions.IgnoreCase);

    public bool MatchesStatedPeriod(string question)
    {
        if(Metric=="sales-comparison") return OperationsQuestions.Parse(question)?.Period==Period;
        var dates=Regex.Matches(question,@"\b\d{4}-\d{2}-\d{2}\b").Select(match=>match.Value).ToArray();
        if(dates.Length>0)
            return dates.Length<=2 && Period=="range" && From==dates[0] && To==dates[^1];
        var stated=Regex.Matches(Normalize(question),@"\b(day before yesterday|two days ago|2 days ago|yesterday|today|this week|last week|this month|last month)\b")
            .Select(match=>Parse("sales amount "+match.Value)?.Period).Distinct().ToArray();
        return stated.Length==0 || stated.Length==1 && (Period==stated[0] || Period=="current" && stated[0]=="today");
    }

    public static SalesQuestion? Parse(string question, string? previousMetric = null)
    {
        var text = Normalize(question);
        if (IsDateOnly(text) && previousMetric is "quantity" or "count" or "amount" or "collected")
        {
            var followUp = Regex.Replace(text, @"^(and |what about |how about )", "");
            var followUpIntent = Parse("sales amount " + followUp);
            return followUpIntent is null ? null : followUpIntent with { Metric = previousMetric };
        }
        if (!IsValueQuestion(text)) return null;
        // Never silently drop a product, return, payment, staff or location filter.
        if (Regex.IsMatch(text, @"\b(for|by|at|product|cash|credit|refund|return|net|profit|customer|store|branch|cancelled|pending)\b")) return null;
        var metric = Regex.IsMatch(text, @"\b(collected|collection|payments?|received money)\b") ? "collected" :
            Regex.IsMatch(text, @"\b(quantity|units?|items?)\b") ? "quantity" :
            Regex.IsMatch(text, @"\b(count|bills?|invoices?|completed)\b|how many.*sales?") ? "count" : "amount";
        var periodText = Regex.Replace(text, @"\b(day before yesterday|two days ago|2 days ago)\b", "day-before-yesterday");
        var periods = new[] { ("day-before-yesterday", "day-before-yesterday"), ("yesterday", "yesterday"), ("last week", "last-week"), ("this week", "this-week"),
            ("last month", "last-month"), ("this month", "this-month"), ("monthly", "this-month"), ("today", "today") };
        var matched = periods.Where(pair => Regex.IsMatch(periodText, @"(?<![\w-])" + Regex.Escape(pair.Item1) + @"(?![\w-])")).Select(pair => pair.Item2).Distinct().ToArray();
        var dates = Regex.Matches(text, @"\b\d{4}-\d{2}-\d{2}\b").Select(match => match.Value).ToArray();
        if (matched.Length > 1 || (matched.Length > 0 && dates.Length > 0)) return null;
        SalesQuestion? result = matched.Length == 1 ? new(metric, matched[0]) : dates.Length switch {
            1 => new(metric, "range", dates[0], dates[0]),
            2 => new(metric, "range", dates[0], dates[1]),
            _ => null
        };
        // Remaining unfamiliar words go to AI instead of guessing at a missing filter.
        var remainder = Regex.Replace(text, @"\b\d{4}-\d{2}-\d{2}\b", "");
        remainder = Regex.Replace(remainder, @"\b(how|much|many|what|is|are|was|were|the|a|of|our|my|we|i|did|have|has|been|please|tell|me|show|give|get|number|quantity|quantities|units?|items?|sold|sales?|bills?|invoices?|completed|count|total|amount|revenue|turnover|collected|collection|payments?|received|money|today|yesterday|day|before|two|2|days|ago|this|last|week|month|monthly|in|on|from|to|between|and)\b", "");
        remainder = Regex.Replace(remainder, @"[\s'’:,\-]", "");
        return remainder.Length == 0 && result?.ValidPeriod() is not null ? result : null;
    }
}
