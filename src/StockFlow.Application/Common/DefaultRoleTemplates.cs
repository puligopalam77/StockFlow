using StockFlow.Application.DTOs.Security;
namespace StockFlow.Application.Common;

// Seed grants identify included navigation modules. Included modules receive all
// child permissions; the repository clips these to the creator's grant authority.
public static class DefaultRoleTemplates
{
    public sealed record Definition(string Key, string Name, string Description, string Limitations, IReadOnlyDictionary<string, string[]> Grants);
    private static Dictionary<string, string[]> Grants(params string[] specs) => specs.ToDictionary(s => s.Split(':')[0], s => s.Split(':')[1].Split(','));
    private const string ShopLimits = "Included modules select all their sections, screens and actions by default, including approvals and stock adjustments. Customize permissions to remove access. Your grant authority, assigned shops and existing owner restrictions still apply.";
    private static readonly string[] Operations = ["DASHBOARD:VIEW", "SCAN:VIEW,SCAN", "SEARCH:VIEW", "INVENTORY:VIEW,EXPORT", "PRODUCTS:VIEW,CREATE,EDIT", "PRODUCT_VARIANTS:VIEW,CREATE,EDIT", "CATEGORIES:VIEW", "BRANDS:VIEW", "BARCODES:VIEW,GENERATE,PRINT,REPRINT", "SALES:VIEW,CREATE,COLLECT,EXPORT", "INVOICES:VIEW,PRINT,EXPORT", "CUSTOMERS:VIEW,CREATE,EDIT", "QUOTATIONS:VIEW,CREATE,EDIT,PRINT", "PURCHASES:VIEW,CREATE,EDIT,EXPORT", "SUPPLIERS:VIEW,CREATE,EDIT", "STOCK_INFLOW:VIEW,RECEIVE,EXPORT", "STOCK_TRANSFER:VIEW", "STOCK_OUTFLOW:VIEW", "EXPENSES:VIEW,CREATE,EDIT,ATTACH", "REPORTS:VIEW,EXPORT", "SERVICE:VIEW,CREATE,UPDATE,ASSIGN", "PENDING_SERVICES:VIEW", "WARRANTY_LOOKUP:VIEW", "WARRANTY_CLAIMS:VIEW", "EXPIRING_WARRANTIES:VIEW"];
    public static readonly Definition[] All = [
        new("shop-admin", "Shop Admin", "Assigned shop operations and reports", ShopLimits, Grants(Operations)),
        new("cashier", "Cashier", "Billing and customer payments", ShopLimits, Grants("DASHBOARD:VIEW", "SALES:VIEW,CREATE,COLLECT", "INVOICES:VIEW,PRINT", "CUSTOMERS:VIEW,CREATE", "PRODUCTS:VIEW", "INVENTORY:VIEW", "SCAN:VIEW,SCAN", "SEARCH:VIEW")),
        new("salesperson", "Salesperson", "Sales and customer service", ShopLimits, Grants("DASHBOARD:VIEW", "SALES:VIEW,CREATE", "INVOICES:VIEW,PRINT", "CUSTOMERS:VIEW,CREATE", "QUOTATIONS:VIEW,CREATE,EDIT,PRINT", "PRODUCTS:VIEW", "INVENTORY:VIEW", "SCAN:VIEW,SCAN", "SEARCH:VIEW")),
        new("inventory-staff", "Inventory Staff", "Products, receiving and stock operations", ShopLimits, Grants("DASHBOARD:VIEW", "INVENTORY:VIEW,EXPORT", "PRODUCTS:VIEW,CREATE,EDIT", "PRODUCT_VARIANTS:VIEW,CREATE,EDIT", "CATEGORIES:VIEW", "BRANDS:VIEW", "BARCODES:VIEW,GENERATE,PRINT,REPRINT", "PURCHASES:VIEW", "SUPPLIERS:VIEW", "STOCK_INFLOW:VIEW,RECEIVE,EXPORT", "STOCK_TRANSFER:VIEW", "SCAN:VIEW,SCAN", "SEARCH:VIEW")),
        new("purchase-manager", "Purchase Manager", "Suppliers, purchasing and receiving", ShopLimits, Grants("DASHBOARD:VIEW", "PURCHASES:VIEW,CREATE,EDIT,EXPORT", "SUPPLIERS:VIEW,CREATE,EDIT,EXPORT", "STOCK_INFLOW:VIEW,RECEIVE,EXPORT", "PRODUCTS:VIEW", "INVENTORY:VIEW", "BARCODES:VIEW,PRINT", "SCAN:VIEW,SCAN")),
        new("accountant", "Accountant", "Expenses and assigned-shop financial reporting", ShopLimits, Grants("DASHBOARD:VIEW", "EXPENSES:VIEW,CREATE,EDIT,ATTACH,EXPORT", "REPORTS:VIEW,EXPORT", "GST_REPORTS:VIEW", "INVOICES:VIEW,PRINT,EXPORT", "SALES:VIEW", "PURCHASES:VIEW", "CUSTOMERS:VIEW", "SUPPLIERS:VIEW")),
        new("store-manager", "Store Manager", "Shop operations and reports", ShopLimits, Grants(Operations)),
        new("custom-role", "Custom Role", "Choose screens and actions manually", "No default permissions. Shop assignments and ownership are configured separately.", Grants())
    ];
    public static List<ModulePermission> Apply(Definition definition, IEnumerable<ModulePermission> catalog)
    {
        var rows = catalog.ToList();
        // Use the main navigation groups, not report aliases, to determine which
        // modules are included. Shared VIEW permissions must not enable Reports.
        string[][] groups = [
            ["DASHBOARD"],
            ["STOCK_INFLOW", "PURCHASES", "SUPPLIERS"],
            ["SALES", "INVOICES", "QUOTATIONS", "EWAY_BILLS"],
            ["INVENTORY", "STOCK_ADJUSTMENT", "REPLENISHMENT", "STOCK_TRANSFER", "STOCK_OUTFLOW", "SCAN", "SEARCH", "PRODUCTS", "PRODUCT_VARIANTS", "CATEGORIES", "BRANDS", "BARCODES"],
            ["EXPENSES"],
            ["REPORTS", "GST_REPORTS"],
            ["SERVICE", "PENDING_SERVICES", "WARRANTY_LOOKUP", "WARRANTY_CLAIMS", "EXPIRING_WARRANTIES"],
            ["CRM", "CUSTOMERS"],
            ["SECURITY", "BANK_ACCOUNTS", "COMPANY_SETTINGS", "GST_SETTINGS", "STORES", "USER_MANAGEMENT", "ROLE_MANAGEMENT", "AUDIT_LOGS"]
        ];
        var included = groups.Where(g => g.Any(code => definition.Grants.GetValueOrDefault(code, []).Contains("VIEW")))
            .SelectMany(g => g).ToHashSet();
        foreach (var row in rows) row.Actions = row.SupportedActions.Where(a => included.Contains(row.ModuleCode) || definition.Grants.GetValueOrDefault(row.ModuleCode, []).Contains(a)).ToArray();
        if (included.Contains("REPORTS"))
        {
            // More Reports and other report aliases use VIEW from operational modules.
            string[] reportViews = ["SALES", "EXPENSES", "STOCK_INFLOW", "STOCK_OUTFLOW", "EXPIRING_WARRANTIES", "WARRANTY_CLAIMS", "PENDING_SERVICES", "SERVICE"];
            foreach (var row in rows.Where(r => reportViews.Contains(r.ModuleCode) && r.SupportedActions.Contains("VIEW")))
                row.Actions = row.Actions!.Append("VIEW").Distinct().ToArray();
        }
        return rows;
    }
}
