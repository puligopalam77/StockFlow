using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using StockFlow.Application.Common;
using StockFlow.Application.Interfaces;
using StockFlow.Web.Security;

namespace StockFlow.Web.Help;

public record HelpFact(string Label, string Value, string? Note = null);
public record HelpAnswerContext(string Title, string Scope, string From, string To, string TimeZone,
    DateTimeOffset CheckedAt, string Basis, HelpLink[] Sources, string[] FollowUps);
public record RetailQuestion(string Metric, SalesPeriod Period);

public sealed class RetailAssistantService(PermissionContext access, BusinessScope scope,
    ISalesEnquiryRepository sales, IDashboardService dashboard, IHelpBusinessRepository business,
    IHelpRetailRepository retail, IOptions<ShopTimeOptions> time, TimeProvider clock)
{
    public string ScopeName => string.IsNullOrWhiteSpace(scope.LocationName) ? "Current authorized shop context" : scope.LocationName;
    public bool CanCompare => scope.IsOwner && scope.CanReviewStores && access.Can("SALES") && access.Can("REPORTS");

    public static RetailQuestion? Parse(string question)
    {
        var text = Regex.Replace(question.ToLowerInvariant().Trim().TrimEnd('?', '.', '!'), @"\s+", " ");
        text = Regex.Replace(text, @"^compare (today's|this week's|this month's) (sales|collections) by shops?$", wording =>
            "compare " + wording.Groups[2].Value + " by shop " + (wording.Groups[1].Value == "today's" ? "today" : wording.Groups[1].Value[..^2]));
        if (text is "summarize today's business" or "summarise today's business" or "daily business summary" or "today's business summary" or "show today's summary")
            return new("daily-summary", new("today"));
        if (text is "what needs attention today" or "today's attention" or "today attention" or "pending work summary")
            return new("attention", new("current"));
        // An explicit closed grammar prevents dropping named shops or other filters.
        var match = Regex.Match(text, @"^(?:compare (sales|collections) (?:by|across) (?:my |all |my authorized )?shops?|which shop has the highest (sales|collections)|show (today's sales|this month's collections) across all my shops)(?: (today|yesterday|day before yesterday|this week|last week|this month|last month|on \d{4}-\d{2}-\d{2}|from \d{4}-\d{2}-\d{2} to \d{4}-\d{2}-\d{2}))?$");
        if (!match.Success)
            return null;
        var metric = (match.Groups[1].Value + match.Groups[2].Value + match.Groups[3].Value).Contains("collections") ? "shop-collections" : "shop-sales";
        var date = match.Groups[4].Value;
        if (date.Length == 0 && match.Groups[3].Success)
            date = match.Groups[3].Value.StartsWith("today") ? "today" : "this month";
        var period = SalesQuestion.Parse("sales amount " + date)?.ValidPeriod();
        return new(metric, period ?? new("missing"));
    }

    public string[] Suggestions(string? path)
    {
        var questions = new List<string>();
        var hasAttention = access.Can("INVENTORY") || access.Can("PENDING_SERVICES") || access.Can("EXPIRING_WARRANTIES") ||
            access.Can("PURCHASES") || access.Can("STOCK_TRANSFER") || access.Can("SALES") && access.Can("CUSTOMERS");
        if (hasAttention || access.Can("SALES"))
            questions.Add("Summarize today's business");
        if (hasAttention)
            questions.Add("What needs attention today?");
        if (CanCompare)
            questions.Add("Compare sales by shop this week");
        if ((path ?? "").StartsWith("/inventory", StringComparison.OrdinalIgnoreCase) && access.Can("INVENTORY"))
            questions.Add("How many low stock products?");
        if (access.Can("SALES"))
        {
            questions.Add("How much sales today?");
            questions.Add("Payment breakdown today");
        }
        if (access.Can("INVENTORY"))
        {
            questions.Add("How much stock is available?");
            questions.Add("Which products need purchasing?");
        }
        if (access.Can("PURCHASES"))
            questions.Add("Which purchase orders are still waiting for stock?");
        if (access.Can("SALES") && access.Can("CUSTOMERS"))
            questions.Add("Which customers have overdue dues?");
        if (access.Can("SERVICE") && access.Can("PENDING_SERVICES"))
            questions.Add("Which repairs are overdue or ready for collection?");
        if (access.Can("STOCK_TRANSFER"))
            questions.Add("Which transfers are pending receipt?");
        if (questions.Count == 0)
            questions.Add("How do I use other StockFlow features?");
        return questions.Distinct().Take(6).ToArray();
    }

    public HelpAnswerContext Describe(string metric, SalesPeriod period)
    {
        var dates = ShopDateRange.Resolve(period, time.Value, clock);
        var (title, url, basis) = metric switch
        {
            "quantity" or "product-quantity" => ("Units sold", "/Sales", "Gross units on completed bills; returns are shown separately."),
            "count" or "product-count" => ("Completed bills", "/Sales", "Completed bills dated within this period."),
            "amount" or "product-amount" or "shop-sales" => ("Completed sales", "/Sales", "Completed invoice totals by sale date, before returns; this is not profit."),
            "collected" or "payment-breakdown" or "shop-collections" or "cash-collected" or "card-collected" or "upi-collected" or "bank-collected" or "cheque-collected"
                => ("Money collected", "/Sales", "Non-credit payments by payment date, including older completed bills; refunds are not deducted. This is not sales revenue or net cash."),
            "stock" or "product-stock" => ("Available stock", "/Inventory", "Current accessible stock; serialized goods use available serial units. No reserved-stock estimate is calculated."),
            "low-stock" or "reorder" => ("Low stock", "/Inventory/LowStock", "Current minimum-stock rules; review pending purchases. This is not demand forecasting."),
            "customer-dues" or "overdue-customers" => ("Customer dues", "/Sales", "Current invoice balances reconciled with recorded payments, returns and refunds; overdue requires a recorded due date."),
            "supplier-balance" => ("Supplier balances", "/Purchases", "Current completed purchases less recorded supplier payments and purchase returns."),
            "pending-purchases" => ("Pending purchases", "/Purchases", "Current draft and partially received orders; ordered less received and damaged quantities."),
            "pending-transfers" => ("Transfers awaiting receipt", "/Transfers", "Current dispatched or partially received transfers within your permitted scope."),
            "repairs" or "ready-repairs" or "overdue-repairs" or "ready-repair-list" or "repair-deliveries" => ("Repair status", "/Service/Pending", "Current repair status; overdue uses the expected delivery date."),
            "warranty-detail" => ("Warranty status", "/Warranty/Lookup", "Recorded warranty dates and status for the exact serial number."),
            "warranties" => ("Expiring warranties", "/Warranty/Expiring", "Recorded warranties expiring within the next 30 days."),
            "top-products" or "slow-products" => ("Product performance", "/Sales", "Completed sale quantities before returns in the stated period; slow-moving includes eligible zero-sale variants."),
            "return-count" or "return-quantity" or "return-amount" or "refunded" => ("Returns and refunds", "/Sales", "Recorded customer returns or refunds by return date, separate from gross sales."),
            "sales-comparison" => ("Sales period comparison", "/Sales", "Completed sales for this period to date versus the full previous period; the reply shows both ranges."),
            "products" => ("Active products", "/Products", "Current active product records in your authorized context."),
            "customers" => ("Customers", "/Customers", "Current accessible customer records."),
            "suppliers" => ("Suppliers", "/Suppliers", "Current accessible supplier records."),
            "attention" => ("Today's attention", "", "Current permitted operational records; each item is checked independently."),
            _ => ("Today's business", "", "Sales and collections use today's shop dates; stock and pending work show their current status.")
        };
        var sources = url.Length > 0 && access.CanVisit(url) ? new[] { new HelpLink("Open source screen", url) } : [];
        var followUps = metric is "amount" or "count" or "quantity" or "collected" ? new[] { "Yesterday", "Payment breakdown today" }
            : metric == "shop-sales" ? new[] { "Compare collections by shop this month" }
            : Suggestions(null).Where(q => q != (metric == "daily-summary" ? "Summarize today's business" : metric == "attention" ? "What needs attention today?" : "")).Take(2).ToArray();
        return new(title, ScopeName, dates.From.ToString("yyyy-MM-dd"), dates.To.ToString("yyyy-MM-dd"), time.Value.TimeZoneId,
            clock.GetUtcNow(), basis, sources, followUps.Where(q => !q.Contains("Payment") || access.Can("SALES")).ToArray());
    }

    public async Task<HelpAnswer> ExecuteAsync(RetailQuestion question, CancellationToken cancellation)
    {
        if (scope.TenantId <= 0 || scope.UserId <= 0 || !scope.CanReviewStores && (scope.Consolidated || scope.LocationId <= 0))
            return new("restricted", "Your shop access must be refreshed before checking records.", [], []);
        if (question.Metric.StartsWith("shop-"))
        {
            if (!CanCompare)
                return new("restricted", "Shop comparisons require owner access and permission to view sales and reports.", [], []);
            if (!scope.Consolidated || scope.StoreId != 0 || scope.LocationId != 0)
                return new("clarify", "Choose All authorized shops in the shop selector, then ask again. I will not change your shop scope automatically.", [], []);
            if (question.Period.Period == "missing")
                return new("clarify", "Which period should I compare: today, this week or this month?", [], []);
            var rows = await retail.CompareShopsAsync(question.Period, cancellation);
            var collections = question.Metric == "shop-collections";
            var sorted = collections ? rows.OrderByDescending(row => row.Collected).ThenBy(row => row.StoreId) : rows.OrderByDescending(row => row.SalesAmount).ThenBy(row => row.StoreId);
            var facts = sorted.Take(50).Select(row => new HelpFact(row.StoreName, Money(collections ? row.Collected : row.SalesAmount), $"{row.InvoiceCount:N0} completed bills dated in this period")).ToArray();
            return new("answered", rows.Count == 0 ? "No accessible active shops were found." : rows.Count > 50 ? "Showing a partial comparison of 50 shops; this is not a complete business ranking." : "Authorized shop comparison", [], [])
            {
                Context = Describe(question.Metric, question.Period),
                Facts = facts
            };
        }

        var output = new List<HelpFact>();
        var sources = new List<HelpLink>();
        void Source(string label, string url)
        {
            if (access.CanVisit(url))
                sources.Add(new(label, url));
        }
        async Task Read(string label, Func<Task<string>> operation)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                output.Add(new(label, await operation()));
            }
            catch (Exception error) when (error is System.Data.Common.DbException or TimeoutException || error is OperationCanceledException && !cancellation.IsCancellationRequested)
            {
                output.Add(new(label, "Unavailable", "No value was calculated; retry or open the source screen."));
            }
        }
        if (question.Metric == "daily-summary" && access.Can("SALES"))
        {
            SalesPeriodSummary? totals = null;
            await Read("Today's sales", async () => { totals = await sales.GetSummaryAsync(new("today"), cancellation); return $"{Money(totals.Amount)} | {totals.InvoiceCount:N0} completed bills | {totals.Quantity:N0} units"; });
            output.Add(new("Money collected today", totals is null ? "Unavailable" : Money(totals.Collected), "Payment date, including older bills; refunds are separate."));
            await Read("Today's returns", async () => (await business.GetAsync(new("return-amount", new("today")), cancellation)).Value);
            Source("Sales and collections", "/Sales");
        }
        if (access.Can("INVENTORY") || access.Can("PENDING_SERVICES") || access.Can("EXPIRING_WARRANTIES"))
        {
            await Read("Current operational counts", async () =>
            {
                var data = await dashboard.GetBusinessSummaryAsync(false);
                var values = new List<string>();
                if (access.Can("INVENTORY"))
                {
                    values.Add($"Low stock: {data.LowStockCount:N0}");
                    Source("Low stock", "/Inventory/LowStock");
                }
                if (access.Can("PENDING_SERVICES"))
                {
                    values.Add($"Pending repairs: {data.PendingServiceCount:N0}");
                    Source("Pending repairs", "/Service/Pending");
                }
                if (access.Can("EXPIRING_WARRANTIES"))
                {
                    values.Add($"Warranties expiring in 30 days: {data.ExpiringWarrantyCount:N0}");
                    Source("Expiring warranties", "/Warranty/Expiring");
                }
                return string.Join(" | ", values);
            });
        }
        // Each fixed question lists every permission it needs before reading data.
        var operationalQuestions = new (string Label, string Metric, string[] Modules, string SourceUrl)[] {
            ("Overdue customer dues", "overdue-customers", new[]{"SALES","CUSTOMERS"}, "/Sales"),
            ("Purchase deliveries", "pending-purchases", new[]{"PURCHASES"}, "/Purchases"),
            ("Repair deliveries", "repair-deliveries", new[]{"SERVICE","PENDING_SERVICES"}, "/Service/Pending"),
            ("Transfers awaiting receipt", "pending-transfers", new[]{"STOCK_TRANSFER"}, "/Transfers")
        };
        foreach (var item in operationalQuestions)
        {
            if (!item.Modules.All(module => access.Can(module)))
                continue;
            await Read(item.Label, async () =>
            {
                var answer = await business.GetAsync(new(item.Metric, new("current")), cancellation);
                return answer.Status == "answered" ? answer.Value : "Unavailable: " + answer.Value;
            });
            Source(item.Label, item.SourceUrl);
        }
        return new(output.Count == 0 ? "restricted" : "answered", output.Count == 0 ? "Your account has no permitted business-summary sources." : "Summary of your permitted records. Expand details for the source and calculation basis.", [], [])
        {
            Context = Describe(question.Metric, question.Period) with
            {
                Sources = sources.DistinctBy(link => link.Url).ToArray()
            },
            Facts = output.ToArray()
        };
    }

    private static string Money(decimal value) => value.ToString("C2", CultureInfo.GetCultureInfo("en-IN"));
}
