using StockFlow.Application.DTOs.Security;

namespace StockFlow.Web.Security;

public class PermissionContext
{
    private readonly StockFlow.Application.Common.BusinessScope? scope;
    public PermissionContext(StockFlow.Application.Common.BusinessScope? scope = null) => this.scope = scope;
    public UserAccess? User
    {
        get; set;
    }

    public bool Can(string module, string action = "VIEW")
    {
        if (!StockFlow.Application.Common.BusinessProfiles.AllowsModule(
            scope?.BusinessProfileCode ?? StockFlow.Application.Common.BusinessProfiles.Electrical, module)) return false;
        bool Raw(string a) => User?.Can(module, a) == true;
        if (Raw(action))
        {
            return true;
        }

        // Compatibility for existing Razor screens while each new operation remains a distinct assignable action.
        return (module, action) switch
        {
            ("SALES", "EDIT") => Raw("COLLECT") || Raw("FULFIL"),
            ("SALES", "APPROVE") => Raw("RETURN"),
            ("PURCHASES", "EDIT") => Raw("PAY"),
            ("SERVICE", "EDIT") => Raw("UPDATE"),
            ("SERVICE", "APPROVE") => Raw("DELIVER") || Raw("COLLECT"),
            _ => false
        };
    }

    // Corrections require the assigned action; payment compatibility must not grant editing.
    public bool CanExplicit(string module, string action) =>
        StockFlow.Application.Common.BusinessProfiles.AllowsModule(scope?.BusinessProfileCode ?? StockFlow.Application.Common.BusinessProfiles.Electrical, module)
        && User?.Can(module, action) == true;

    public bool CanVisit(string url)
    {
        var (module, action) = PermissionMiddleware.Requirement(url.Split('?', '#')[0], "GET");
        return module is not null && Can(module, action);
    }

    public void Filter(StockFlow.Application.DTOs.Dashboard.BusinessDashboardSummaryResponse b)
    {
        if (!Can("SALES") || !Can("CUSTOMERS"))
        {
            b.CustomerOutstandingAmount=null;
            b.OverduePaymentAmount=null;
            b.OverdueInvoiceCount=null;
        }
        if (!Can("SALES"))
        {
            b.TodaySalesAmount = null;
            b.MonthSalesAmount = null;
            b.TodayInvoiceCount = null;
        }

        if (!Can("INVENTORY"))
        {
            b.LowStockCount = 0;
        }

        if (!Can("PENDING_SERVICES"))
        {
            b.PendingServiceCount = 0;
        }

        if (!Can("SERVICE"))
        {
            b.ReadyServiceCount = 0;
        }

        if (!Can("EXPIRING_WARRANTIES"))
        {
            b.ExpiringWarrantyCount = 0;
        }
    }

    public void Filter(StockFlow.Application.DTOs.Dashboard.DashboardInventorySummaryResponse b)
    {
        if (!Can("PRODUCTS"))
        {
            b.TotalProducts = 0;
        }

        if (!Can("STOCK_INFLOW"))
        {
            b.StockInflow = 0;
            b.TodayInflow = 0;
        }

        if (!Can("STOCK_OUTFLOW"))
        {
            b.StockOutflow = 0;
            b.TodayOutflow = 0;
        }

        if (!Can("INVENTORY"))
        {
            b.CurrentStock = 0;
        }

        if (!Can("SUPPLIERS"))
        {
            b.TotalSuppliers = null;
        }

        if (!Can("CUSTOMERS"))
        {
            b.TotalCustomers = 0;
        }
    }

    public void Filter(StockFlow.Application.DTOs.Dashboard.DashboardResponse b)
    {
        Filter((StockFlow.Application.DTOs.Dashboard.DashboardInventorySummaryResponse)b);
        Filter(b.Business);
        if (!Can("SALES"))
        {
            b.RecentSales = [];
        }

        if (!Can("SERVICE"))
        {
            b.RecentServices = [];
        }

        if (!Can("INVENTORY"))
        {
            b.LowStock = [];
            b.Categories = [];
        }

        if (!Can("STOCK_INFLOW"))
        {
            b.RecentInflow = [];
        }

        if (!Can("STOCK_OUTFLOW"))
        {
            b.RecentOutflow = [];
        }

        foreach (var m in b.Months)
        {
            if (!Can("STOCK_INFLOW"))
            {
                m.Inflow = 0;
            }

            if (!Can("STOCK_OUTFLOW"))
            {
                m.Outflow = 0;
            }
        }
    }
}
