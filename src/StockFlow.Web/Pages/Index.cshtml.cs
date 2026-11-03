using Microsoft.AspNetCore.Mvc.RazorPages;
using StockFlow.Application.DTOs.Dashboard;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Pages;

[Microsoft.AspNetCore.Authorization.Authorize]
[Microsoft.AspNetCore.Mvc.ResponseCache(NoStore = true, Location = Microsoft.AspNetCore.Mvc.ResponseCacheLocation.None)]
public class IndexModel : PageModel
{
    private readonly IDashboardService dashboard;
    private readonly ILogger<IndexModel> logger;
    private readonly StockFlow.Web.Security.PermissionContext access;
    private readonly StockFlow.Web.ErrorHandling.ErrorLogService errorLog;
    // ASP.NET supplies these services when it creates this class.
    public IndexModel(IDashboardService dashboard,
ILogger<IndexModel> logger,
StockFlow.Web.Security.PermissionContext access,
StockFlow.Web.ErrorHandling.ErrorLogService errorLog)
    {
        this.dashboard = dashboard;
        this.logger = logger;
        this.access = access;
        this.errorLog = errorLog;
    }

    public DashboardResponse Data { get; private set; } = new();
    public bool DataUnavailable
    {
        get; private set;
    }
    public string DisplayName
    {
        get
        {
            var fullName = access.User?.FullName;
            if (!string.IsNullOrWhiteSpace(fullName))
            {
                return fullName;
            }

            return User.Identity?.Name ?? "there";
        }
    }

    public async Task OnGetAsync()
    {
        try
        {
            Data = await dashboard.GetAsync(access.Can("SUPPLIERS"), access.Can("SALES"));
            access.Filter(Data);
        }
        catch (Exception ex)
        {
            DataUnavailable = true;
            logger.LogError(ex, "Dashboard data could not be loaded.");
            errorLog.Record(HttpContext, ex, 500);
        }
    }

    public string Count(long? number)
    {
        if (DataUnavailable || number is null)
        {
            return "—";
        }
        else
        {
            return number.Value.ToString("N0");
        }
    }

    public string Money(decimal? number)
    {
        if (DataUnavailable || number is null)
        {
            return "—";
        }
        else
        {
            return number.Value.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("en-IN"));
        }
    }
}
