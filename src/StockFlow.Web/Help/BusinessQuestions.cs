using System.Text.RegularExpressions;

namespace StockFlow.Web.Help;

public static class BusinessQuestions
{
    public static bool IsClearlyOutside(string question) =>
        Regex.IsMatch(question,@"\b(weather|forecast|sports?|politics|recipes?|cooking|poems?|jokes?|planets?)\b",RegexOptions.IgnoreCase) &&
        !Regex.IsMatch(question,@"\b(products?|customers?|suppliers?|inventory|sales?|stock|bills?|invoices?|payments?|repairs?|warrant(?:y|ies))\b",RegexOptions.IgnoreCase);
    public static bool HasBusinessSubject(string question) =>
        question.Any(character=>char.IsLetter(character) && character>255) ||
        Regex.IsMatch(question,@"\b(sales?|sold|sell|selling|stock|inventory|available|availability|units?|items?|products?|brands?|suppliers?|customers?|repairs?|services?|jobs?|warrant(?:y|ies)|payments?|paid|collected|collect|collections?|owe|dues?|outstanding|balances?|receipts?|returns?|returned|refunds?|refunded|revenue|turnover|takings|bills?|invoices?|bikri|vasool|ammakam)\b",RegexOptions.IgnoreCase);
    public static readonly string[] DatedMetrics = ["return-count","return-quantity","return-amount","refunded","payment-breakdown","cash-collected","card-collected","upi-collected","bank-collected","cheque-collected"];
    public static bool IsExtended(SalesQuestion query) => OperationsQuestions.Supports(query.Metric) || DatedMetrics.Contains(query.Metric) || query.Metric is "customer-dues" or "supplier-balance" or "product-quantity" or "product-count" or "product-amount";
    public static SalesQuestion NormalizeNamedMetric(SalesQuestion query,string question)
    {
        if(query.Metric=="stock" && query.Term is null)
        {
            var named=Regex.Match(question.Trim().TrimEnd('?','.'),@"^(?:how many|how much) (.{2,150}?) (?:units?|items?|products?) (?:are )?(?:available|in stock)(?: now| today)?$",RegexOptions.IgnoreCase);
            if(named.Success && !Regex.IsMatch(named.Groups[1].Value,@"^(?:all|total|the|our|my|stock)$",RegexOptions.IgnoreCase))
                return query with {Metric="product-stock",Term=named.Groups[1].Value};
        }
        // Keep an explicitly returned entity filter; never turn it into an unfiltered total.
        // Smaller models sometimes choose the generic metric alongside a named term.
        var term=query.Term?.Trim();
        if(term is null || !question.Contains(term,StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(term,@"^(shops?|stores?|warehouses?|locations?|stock|units?|items?|products?|available|now|today|yesterday|current|कल|నిన్న|\d{4}-\d{2}-\d{2})$",RegexOptions.IgnoreCase)) return query;
        return query.Metric switch {
            "stock"=>query with {Metric="product-stock"},
            "quantity" or "count" or "amount"=>query with {Metric="product-"+query.Metric},
            _=>query
        };
    }
    public static bool MatchesFilters(SalesQuestion query,string question)
    {
        if(query.Metric=="overdue-customers" && Regex.IsMatch(question,@"^[\x00-\x7F]+$") &&
            !Regex.IsMatch(question,@"\boverdue\b|\bpast due\b|\blate\b",RegexOptions.IgnoreCase)) return false;
        if(Regex.IsMatch(question,@"\boverdue\b",RegexOptions.IgnoreCase) && query.Metric is not ("overdue-customers" or "overdue-repairs" or "repair-deliveries")) return false;
        if(Regex.IsMatch(question,@"\bslow[- ]moving\b|sold least",RegexOptions.IgnoreCase) && query.Metric!="slow-products") return false;
        if(Regex.IsMatch(question,@"\btop[- ]selling\b|sold most",RegexOptions.IgnoreCase) && query.Metric!="top-products") return false;
        if(Regex.IsMatch(question,@"\bcompare\b",RegexOptions.IgnoreCase) && query.Metric!="sales-comparison") return false;
        if(Regex.IsMatch(question,@"\btransfers?\b",RegexOptions.IgnoreCase) && query.Metric!="pending-transfers") return false;
        if(query.Term is not null && !question.Contains(query.Term.Trim(),StringComparison.OrdinalIgnoreCase)) return false;
        var mode=Regex.Match(question,@"\b(cash|card|upi|bank|cheque)\b",RegexOptions.IgnoreCase);
        if(mode.Success && query.Metric!=mode.Value.ToLowerInvariant()+"-collected" && query.Metric!="payment-breakdown") return false;
        if(Regex.IsMatch(question,@"\b(customer|supplier)\b",RegexOptions.IgnoreCase) && query.Metric is not ("customer-dues" or "supplier-balance" or "overdue-customers" or "customers" or "suppliers")) return false;
        if(Regex.IsMatch(question,@"\b(refund|refunded|refunds|return|returned|returns)\b",RegexOptions.IgnoreCase) && !DatedMetrics.Take(4).Contains(query.Metric)) return false;
        if(Regex.IsMatch(question,@"\b(for|of)\s+(product|brand)\b",RegexOptions.IgnoreCase) && !query.Metric.StartsWith("product-")) return false;
        return true;
    }
    public static bool IsFact(string text) => !Regex.IsMatch(text,@"\bhow\s+(do|can|to|should)\b|\b(can|should|may|must|could|would|why|where)\b",RegexOptions.IgnoreCase) &&
        (SalesQuestion.IsValueQuestion(text) || Regex.IsMatch(text,@"^(?:(?:how much|how many|what is|what are|show|total)\b.*\b(dues?|outstanding|balances?|returns?|returned|refunds?|refunded|payment breakdown)\b|(?:customer (?:dues?|outstanding)|supplier (?:balance|dues?|outstanding)|return (?:count|amount)|returned quantity|refund amount|payment breakdown|payments by mode|collections by payment mode)\b)",RegexOptions.IgnoreCase));

    // Only these follow-up shapes inherit previous scope. A new unrelated question never does.
    public static SalesQuestion? FollowUp(string text, SalesQuestion? previous)
    {
        if (previous is null) return null;
        if(previous.Metric=="warranty-detail" && string.IsNullOrWhiteSpace(previous.Term) &&
            Regex.IsMatch(text.Trim(),@"^(?:serial(?: number)?\s*[:#]?\s*)?[\w-]{1,150}$",RegexOptions.IgnoreCase))
            return previous with {Term=Regex.Replace(text.Trim(),@"^serial(?: number)?\s*[:#]?\s*","",RegexOptions.IgnoreCase)};
        if (SalesQuestion.IsDateOnly(text))
        {
            var dates=SalesQuestion.Parse("sales amount "+Regex.Replace(text.Trim(),@"^(and|what about|how about)\s+","",RegexOptions.IgnoreCase));
            if (dates is null) return null;
            var next=previous with {Period=dates.Period,From=dates.From,To=dates.To};
            return next.ValidPeriod() is not null ? next : null;
        }
        var only=Regex.Match(text.Trim().TrimEnd('?','.'),@"^(?:and\s+)?(?:only|just|for product|for brand)\s+(.{2,150})$",RegexOptions.IgnoreCase);
        if (only.Success && previous.Metric is "quantity" or "count" or "amount" or "product-quantity" or "product-count" or "product-amount")
            return previous with {Metric=previous.Metric.StartsWith("product-") ? previous.Metric : "product-"+previous.Metric,Term=only.Groups[1].Value.Trim()};
        return null;
    }

    public static SalesQuestion? Parse(string question)
    {
        var operation=OperationsQuestions.Parse(question);
        if(operation is not null) return operation;
        var text=Regex.Replace(question.Trim().TrimEnd('?','.','!'),@"\s+"," ");
        if (!IsFact(text) || SalesQuestion.HasUnsupportedFilters(text)) return null;
        var party=Regex.Match(text,@"^(?:how much (?:is |are )?|what (?:is |are )?|show |total )?(?:customer (?:dues?|outstanding(?: balance)?)|supplier (?:balances?|dues?|outstanding(?: balance)?))(?: (?:for|of) (.{2,150}))?$",RegexOptions.IgnoreCase);
        if (party.Success) return new(text.Contains("supplier",StringComparison.OrdinalIgnoreCase)?"supplier-balance":"customer-dues","current",Term:party.Groups[1].Success?party.Groups[1].Value:null);
        var dates=Regex.Match(text,@"\b(day before yesterday|two days ago|2 days ago|yesterday|today|this week|last week|this month|last month|on \d{4}-\d{2}-\d{2}|from \d{4}-\d{2}-\d{2} to \d{4}-\d{2}-\d{2})\b",RegexOptions.IgnoreCase);
        if (!dates.Success) return null;
        var period=SalesQuestion.Parse("sales amount "+dates.Value);
        if (period is null) return null;
        var rest=Regex.Replace(text.Remove(dates.Index,dates.Length).Trim(),@"\s+"," ");
        var product=Regex.Match(rest,@"^(.*?)(?:\s+(?:for|of)\s+(?:product |brand )?|\s+(?:product|brand)\s+)(.{2,150})$",RegexOptions.IgnoreCase);
        if (product.Success)
        {
            var basis=SalesQuestion.Parse(product.Groups[1].Value+" "+dates.Value);
            if (basis?.Metric is "quantity" or "count" or "amount") return basis with {Metric="product-"+basis.Metric,Term=product.Groups[2].Value.Trim()};
        }
        var metric=rest.ToLowerInvariant() switch {
            "payment breakdown" or "show payment breakdown" or "what is the payment breakdown" or "payments by mode" or "collections by payment mode"=>"payment-breakdown",
            "how many returns" or "return count" or "total returns"=>"return-count",
            "how many items returned" or "how much quantity returned" or "returned quantity"=>"return-quantity",
            "how much returns" or "return amount" or "how much return amount" or "total return amount"=>"return-amount",
            "how much refunded" or "how much refund" or "refund amount" or "how much money refunded"=>"refunded",
            _=>null
        };
        var mode=Regex.Match(rest,@"^(?:how much |total |show )?(cash|card|upi|bank|cheque)(?: (?:amount |money ))?\s*(?:collected|collection|payments?|received)$",RegexOptions.IgnoreCase);
        if (mode.Success) metric=mode.Groups[1].Value.ToLowerInvariant()+"-collected";
        return metric is null ? null : period with {Metric=metric};
    }

    public static SalesQuestion? Pending(string question)
    {
        var operation=OperationsQuestions.Parse(question);
        if(operation?.Period=="missing") return operation;
        if(operation is not null) return null;
        if (!IsFact(question) || SalesQuestion.HasUnsupportedFilters(question) ||
            Regex.IsMatch(question,@"\b(today|tofay|todsy|tody|yesterday|days ago|week|month|\d{4}-\d{2}-\d{2})\b",RegexOptions.IgnoreCase)) return null;
        // Reuse strict parsing, but retain missing period so it cannot be executed until staff choose one.
        var dated=question.Trim().TrimEnd('?','.','!')+" today";
        var query=Parse(dated) ?? SalesQuestion.Parse(dated);
        return query is null || query.Period=="current" ? null : query with {Period="missing"};
    }
}
