namespace StockFlow.Web.Navigation;

// Presentation ownership only. Existing route and API authorization stays in place.
public static class RetailNavigation
{
    public static string CanonicalPath(string path)
    {
        path=path.TrimEnd('/');
        return path.EndsWith("/Index",StringComparison.OrdinalIgnoreCase)?path[..^6]:path;
    }
    public static bool IsReportPath(string path, bool fromReports)
    {
        path=CanonicalPath(path);
        return
        path.Equals("/Reports", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/Reports/", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/Expenses/Report", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/CashClosing", StringComparison.OrdinalIgnoreCase) ||
        fromReports && new[] { "/Sales", "/StockInflow", "/StockOutflow", "/Warranty/Expiring", "/Warranty/Claims", "/Service/Pending", "/Service" }
            .Contains(path, StringComparer.OrdinalIgnoreCase);
    }
}
