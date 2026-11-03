using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using StockFlow.Web.Help;
using StockFlow.Web.Security;
using StockFlow.Application.Interfaces;
using StockFlow.Application.Common;
using System.Text.RegularExpressions;

namespace StockFlow.Web.Controllers;

[Authorize, ApiController, Route("api/help/questions")]
[ServiceFilter(typeof(HelpTelemetryFilter))]
public class HelpAiController(HelpAiService help, PermissionContext access, IMemoryCache cache,
    ISalesEnquiryRepository sales, BusinessScope scope, IDashboardService dashboard, IInventoryRepository inventory,
    IHelpBusinessRepository business, HelpTelemetry telemetry) : ControllerBase
{
    public class QuestionInput
    {
        [Required, StringLength(500, MinimumLength = 3)]
        public string Question { get; set; } = "";
        [RegularExpression("^[a-fA-F0-9-]{36}$")] public string? ConversationId {get;set;}
    }
    public class FeedbackInput
    {
        [Required, RegularExpression("^[a-f0-9]{32}$")] public string AnswerId {get;set;}="";
        public bool Helpful {get;set;}
    }
    [HttpPost("feedback")]
    public IActionResult Feedback(FeedbackInput input) => telemetry.Feedback(HelpTelemetry.Owner(scope,User),input.AnswerId,input.Helpful)
        ? Ok(new {message="Thank you for your feedback."}) : NotFound(new {message="Feedback expired or was already recorded."});

    [HttpPost]
    public async Task<IActionResult> Ask(QuestionInput input, CancellationToken cancellation)
    {
        var question = input.Question.Trim();
        if (question.Length < 3) return BadRequest(new { message = "Enter a question with 3–500 characters." });
        // One in-flight question per account per server, with a short cooldown.
        var key = $"help-question:{scope.TenantId}:{scope.UserId}:{scope.StoreId}:{scope.LocationId}:{scope.Consolidated}:" +
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        QuestionGate gate;
        lock (cache) gate = cache.GetOrCreate(key, entry => { entry.SlidingExpiration = TimeSpan.FromMinutes(10); return new QuestionGate(); })!;
        lock (gate)
        {
            if (gate.Busy || gate.Next > DateTimeOffset.UtcNow)
                return StatusCode(429, new { message = "Please wait before asking another question." });
            gate.Busy = true;
        }
        QuestionContext conversation;
        lock(cache) conversation=cache.GetOrCreate(key+":context:"+scope.BusinessProfileCode+":"+scope.BusinessProfileVersion+":"+input.ConversationId,entry=> {entry.SlidingExpiration=TimeSpan.FromMinutes(10);return new QuestionContext();})!;
        try
        {
            Response.Headers.CacheControl = "no-store";
            if(BusinessQuestions.IsClearlyOutside(question))
            {
                conversation.LastEnquiry=null;conversation.LastMetric=null;
                return Ok(new HelpAnswer("unsupported","This question is outside StockFlow functionality. Contact Super Admin.",[],[]));
            }
            var followUp=BusinessQuestions.FollowUp(question,conversation.LastEnquiry);
            var pending=BusinessQuestions.Pending(question);
            if(pending is not null)
            {
                conversation.LastEnquiry=pending;
                HttpContext.Items["help.metric"]=pending.Metric;
                return Ok(new HelpAnswer("clarify","Which date or period should I check? For example, today, yesterday or last week.",[],[]));
            }
            var extended=followUp ?? BusinessQuestions.Parse(question);
            if(extended is not null && BusinessQuestions.IsExtended(extended))
            {
                if(followUp is null && !extended.MatchesStatedPeriod(question)) return Ok(new HelpAnswer("clarify","Please choose one date or period.",[],[]));
                return await Extended(extended,conversation,cancellation);
            }
            var summary = await BusinessSummary(question);
            if (summary is not null) { conversation.LastMetric = SalesQuestion.Parse(question)?.Metric; conversation.LastEnquiry=null; return summary; }
            if (BusinessQuestions.IsFact(question) && SalesQuestion.HasUnsupportedFilters(question))
                return Ok(new HelpAnswer("clarify", "That filter is not available. I can check permitted shop records, but cannot calculate per-employee, profit or net figures.", [], []));
            var salesText=question.ToLowerInvariant().Trim().TrimEnd('?','.') switch {
                "what are today's sales"=>"sales amount today", "what are monthly sales"=>"sales amount this month", _=>question
            };
            var enquiry = followUp ?? SalesQuestion.Parse(salesText, conversation.LastMetric);
            if (enquiry is null && SalesQuestion.IsDateOnly(question))
                return Ok(new HelpAnswer("clarify", "For that date, would you like sold quantity, completed bill count, sales amount or money collected?", [], []));
            if (enquiry is null && !BusinessQuestions.IsFact(question) && help.QuickAnswer(question, access) is { } quick)
            {
                conversation.LastMetric=null;conversation.LastEnquiry=null;
                return Ok(quick);
            }
            if (enquiry is null) enquiry = await help.InterpretSalesAsync(question, cancellation);
            if(enquiry is null && help.InterpretationUnavailable)
            {
                if(BusinessQuestions.IsFact(question))
                    return Ok(new HelpAnswer("clarify","I could not identify the requested figure and filters. Please specify the sales period or exact product/account name; no total was calculated.",[],[]));
                var local=LocalHelpSearch.Answer(question,access);
                if(local.Status is "answered" or "restricted") return Ok(local);
                return Ok(new HelpAnswer("unavailable","The assistant is temporarily busy or unavailable. Please retry.",[],[]));
            }
            if(enquiry is not null && BusinessQuestions.IsExtended(enquiry)) return await Extended(enquiry,conversation,cancellation);
            if (enquiry?.ValidPeriod() is { } period)
            {
                if (period.Period == "current")
                {
                    conversation.LastMetric = null;
                    conversation.LastEnquiry=null;
                    if (enquiry.Metric == "product-stock") return await ProductStock(enquiry.Term!);
                    return (await BusinessSummary(question, enquiry.Metric))!;
                }
                if (!access.Can("SALES")) return StatusCode(403, new { message = "You do not have permission to view sales. Ask your administrator for access." });
                if (scope.TenantId <= 0 || scope.UserId <= 0) return Unauthorized();
                if (!scope.CanReviewStores && (scope.Consolidated || scope.LocationId <= 0)) return Forbid();
                var result = await sales.GetSummaryAsync(period, cancellation);
                HttpContext.Items["help.metric"]=enquiry.Metric;
                Response.Headers.CacheControl = "no-store";
                var value = enquiry.Metric switch {
                    "quantity" => result.Quantity.ToString("N0"),
                    "count" => result.InvoiceCount.ToString("N0"),
                    "collected" => result.Collected.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("en-IN")),
                    _ => result.Amount.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("en-IN"))
                };
                conversation.LastMetric = enquiry.Metric;
                conversation.LastEnquiry=enquiry;
                return Ok(new HelpAnswer("answered", value, [], []));
            }
            if (BusinessQuestions.IsFact(question))
                return Ok(new HelpAnswer("clarify", "Please specify the figure, date and any exact product or account name. I can check sales, collections, returns, customer dues and supplier balances.", [], []));
            conversation.LastMetric = null;
            conversation.LastEnquiry=null;
            return Ok(await help.AskAsync(question, access, cancellation));
        }
        catch (System.Data.Common.DbException error) when (error is not Npgsql.PostgresException)
        {
            return StatusCode(503,new HelpAnswer("unavailable","The records could not be reached. Please retry; no total was calculated.",[],[]));
        }
        catch (Npgsql.PostgresException error) when (error.SqlState is not "42501")
        {
            return StatusCode(503,new HelpAnswer("unavailable","The records are temporarily unavailable. Please retry; no total was calculated.",[],[]));
        }
        catch (TimeoutException)
        {
            return StatusCode(503,new HelpAnswer("unavailable","The records took too long to respond. Please retry.",[],[]));
        }
        finally { lock (gate) { gate.Busy = false; gate.Next = DateTimeOffset.UtcNow.AddSeconds(3); } }
    }
    private async Task<IActionResult> Extended(SalesQuestion enquiry,QuestionContext gate,CancellationToken cancellation)
    {
        if(enquiry.Metric=="warranty-detail" && (string.IsNullOrWhiteSpace(enquiry.Term) || enquiry.Term.Equals("number",StringComparison.OrdinalIgnoreCase)))
        {
            gate.LastEnquiry=enquiry with {Term=null};gate.LastMetric=null;
            return Ok(new HelpAnswer("clarify","What is the exact serial number?",[],[]));
        }
        if(enquiry.ValidPeriod() is not {} period) return Ok(new HelpAnswer("clarify","Please specify a valid date or period.",[],[]));
        var modules=enquiry.Metric switch {
            "pending-purchases"=>new[]{"PURCHASES"}, "reorder"=>new[]{"INVENTORY"},
            "top-products"=>new[]{"SALES","PRODUCTS"}, "slow-products"=>new[]{"SALES","PRODUCTS","INVENTORY"},
            "overdue-customers"=>new[]{"SALES","CUSTOMERS"},
            "overdue-repairs" or "ready-repair-list" or "repair-deliveries"=>new[]{"SERVICE","PENDING_SERVICES"},
            "warranty-detail"=>new[]{"WARRANTY_LOOKUP"}, "pending-transfers"=>new[]{"STOCK_TRANSFER"},
            "customer-dues"=>new[]{"SALES","CUSTOMERS"}, "supplier-balance"=>new[]{"PURCHASES","SUPPLIERS"},
            "return-count" or "return-quantity" or "return-amount" or "refunded"=>new[]{"SALES"}, _=>new[]{"SALES"}
        };
        if(modules.Any(module=>!access.Can(module))) return StatusCode(403,new {message="Your account cannot view this business information."});
        if(scope.TenantId<=0 || scope.UserId<=0) return Unauthorized();
        if(!scope.CanReviewStores && (scope.Consolidated || scope.LocationId<=0)) return Forbid();
        HttpContext.Items["help.metric"]=enquiry.Metric;
        var result=await business.GetAsync(new(enquiry.Metric,period,enquiry.Term),cancellation);
        if(result.Status=="answered") {gate.LastEnquiry=enquiry;gate.LastMetric=null;}
        return Ok(new HelpAnswer(result.Status,result.Value,[],[]));
    }
    private async Task<IActionResult> ProductStock(string term)
    {
        HttpContext.Items["help.metric"]="product-stock";
        if (!access.Can("INVENTORY")) return StatusCode(403, new {message="Your account cannot view inventory."});
        if (scope.TenantId <= 0 || scope.UserId <= 0) return Unauthorized();
        if (!scope.CanReviewStores && (scope.Consolidated || scope.LocationId <= 0)) return Forbid();
        Response.Headers.CacheControl = "no-store";
        var rows = (await inventory.GetBalancesAsync(term.Trim(), false, false)).Take(21).ToArray();
        if (rows.Length == 0) return Ok(new HelpAnswer("answered", "No matching product found.", [], []));
        if (rows.Length > 20) return Ok(new HelpAnswer("clarify", "Several products match. What is the exact product name, item code or barcode?", [], []));
        if (rows.Any(row=>row.HasDiscrepancy))
            return Ok(new HelpAnswer("clarify", "The stock balance and available serial units disagree for this product. I cannot give a reliable single stock value.", [], []));
        var value = rows.Length == 1 ? (rows[0].IsSerialTracked ? rows[0].AvailableUnits : rows[0].CurrentStock).ToString("N0") :
            string.Join("; ", rows.Select(row=>$"{row.ProductName} ({row.Sku}): {(row.IsSerialTracked ? row.AvailableUnits : row.CurrentStock):N0}"));
        return Ok(new HelpAnswer("answered", value, [], []));
    }
    private async Task<IActionResult?> BusinessSummary(string question, string? metric = null)
    {
        var text = Regex.Replace(question.ToLowerInvariant().Trim().TrimEnd('?', '.', '!'), @"\s+", " ");
        var enquiry = metric ?? text switch {
            "how many units are currently in stock" or "how much stock is available" or "what is current stock" => "stock",
            "what are today's sales" or "how much sales today" or "today sales amount" => "today-sales",
            "what are monthly sales" or "this month sales amount" or "how much sales this month" => "month-sales",
            "how many low stock products" or "how many products are low in stock" => "low-stock",
            "how many pending repairs" or "how many repairs are pending" => "repairs",
            "how many warranties are expiring" => "warranties",
            "how many products" or "total products" or "product count" => "products",
            "how many suppliers" or "total suppliers" or "supplier count" => "suppliers",
            "how many customers" or "total customers" or "customer count" => "customers",
            "how many repairs are ready" or "ready repairs count" => "ready-repairs",
            "how much sale completed today" or "how much sales completed today" or "how many sales completed today" or
            "how many sales were completed today" or "how many bills completed today" or "today completed sales count" => "completed-sales",
            _ => null
        };
        if (enquiry is null) return null;
        HttpContext.Items["help.metric"]=enquiry;
        // Sales answers use shop timezone boundaries, rather than the DB server's CURRENT_DATE.
        if(enquiry is "today-sales" or "month-sales" or "completed-sales") return null;
        var module = enquiry switch { "stock" or "low-stock" => "INVENTORY", "products" => "PRODUCTS", "suppliers" => "SUPPLIERS", "customers" => "CUSTOMERS", "repairs" or "ready-repairs" => "PENDING_SERVICES", "warranties" => "EXPIRING_WARRANTIES", _ => "SALES" };
        if (!access.Can(module)) return StatusCode(403, new {message="Your account cannot view this business information. Contact Super Admin for access."});
        if(enquiry=="ready-repairs" && !access.Can("SERVICE")) return StatusCode(403,new {message="Your account cannot view ready repairs."});
        if (scope.TenantId <= 0 || scope.UserId <= 0) return Unauthorized();
        if (!scope.CanReviewStores && (scope.Consolidated || scope.LocationId <= 0)) return Forbid();
        Response.Headers.CacheControl = "no-store";
        if (enquiry is "stock" or "products" or "suppliers" or "customers") {
            var inventory = await dashboard.GetInventorySummaryAsync(enquiry == "suppliers");
            var count = enquiry switch { "products" => inventory.TotalProducts, "suppliers" => inventory.TotalSuppliers, "customers" => inventory.TotalCustomers, _ => inventory.CurrentStock };
            return Ok(new HelpAnswer(count is null ? "unavailable" : "answered", count?.ToString("N0") ?? "This total is temporarily unavailable. Please try again.", [], []));
        }
        var data = await dashboard.GetBusinessSummaryAsync(module == "SALES");
        var amount = enquiry == "today-sales" ? data.TodaySalesAmount : data.MonthSalesAmount;
        var message = enquiry switch {
            "today-sales" or "month-sales" => amount is null ? "Sales totals are unavailable. Please try again." : amount.Value.ToString("C2",System.Globalization.CultureInfo.GetCultureInfo("en-IN")),
            "completed-sales" => data.TodayInvoiceCount?.ToString("N0") ?? "Sales count is unavailable. Please try again.",
            "low-stock" => $"{data.LowStockCount:N0}",
            "repairs" => $"{data.PendingServiceCount:N0}",
            "ready-repairs" => $"{data.ReadyServiceCount:N0}",
            _ => $"{data.ExpiringWarrantyCount:N0}"
        };
        return Ok(new HelpAnswer("answered", message, [], []));
    }
    private class QuestionGate { public bool Busy; public DateTimeOffset Next; }
    private class QuestionContext { public string? LastMetric; public SalesQuestion? LastEnquiry; }
}
