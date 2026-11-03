using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace StockFlow.Web.Filters;

// Validate only read-only query inputs. Existing POST/PUT business rules remain
// in their controllers/services, and each query retains its authenticated scope.
public sealed class ListQueryValidationFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!HttpMethods.IsGet(context.HttpContext.Request.Method)) return;
        var query = context.HttpContext.Request.Query;
        foreach (var (name, minimum, maximum) in new[] {
            ("offset",0,int.MaxValue),("page",1,int.MaxValue),("pageNumber",1,int.MaxValue),
            ("pageSize",1,100),("limit",1,100) })
        {
            if (!query.TryGetValue(name,out var values)) continue;
            if (values.Count != 1 || !int.TryParse(values[0],NumberStyles.Integer,CultureInfo.InvariantCulture,out var value)
                || value < minimum || value > maximum)
            { Reject($"Choose a whole {name} value between {minimum} and {maximum}."); return; }
        }
        if (query.TryGetValue("term",out var term) && (term.Count!=1 || term[0]?.Trim().Length>200))
        { Reject("Search text must not exceed 200 characters."); return; }
        foreach (var name in new[]{"from","to"})
        {
            if (!query.TryGetValue(name,out var date) || string.IsNullOrWhiteSpace(date)) continue;
            if (date.Count!=1 || !DateTime.TryParse(date,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out _))
            { Reject("Choose a valid "+name+" date."); return; }
        }
        if (query.TryGetValue("from",out var from) && query.TryGetValue("to",out var to)
            && !string.IsNullOrWhiteSpace(from) && !string.IsNullOrWhiteSpace(to)
            && DateTime.TryParse(from,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var start)
            && DateTime.TryParse(to,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var end) && start>end)
            Reject("From date must be on or before To date.");
        void Reject(string message) => context.Result = new BadRequestObjectResult(new { message });
    }
    public void OnActionExecuted(ActionExecutedContext context) { }
}
