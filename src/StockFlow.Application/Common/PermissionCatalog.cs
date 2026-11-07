namespace StockFlow.Application.Common;

public static class PermissionCatalog
{
    public static readonly Dictionary<string, string[]> Modules = new()
    {
        ["DASHBOARD"] = ["VIEW"],
        ["SCAN"] = ["VIEW", "SCAN"],
        ["SEARCH"] = ["VIEW"],
        ["PRODUCTS"] = ["VIEW", "CREATE", "EDIT", "DEACTIVATE", "EXPORT"],
        ["PRODUCT_VARIANTS"] = ["VIEW", "CREATE", "EDIT", "DEACTIVATE"],
        ["BARCODES"] = ["VIEW", "GENERATE", "PRINT", "REPRINT"],
        ["CATEGORIES"] = ["VIEW", "CREATE", "EDIT", "DEACTIVATE"],
        ["BRANDS"] = ["VIEW", "CREATE", "EDIT", "DEACTIVATE"],
        ["SUPPLIERS"] = ["VIEW", "CREATE", "EDIT", "DEACTIVATE", "EXPORT"],
        ["CUSTOMERS"] = ["VIEW", "CREATE", "EDIT", "DEACTIVATE", "EXPORT"],
        ["PURCHASES"] = ["VIEW", "CREATE", "EDIT", "APPROVE", "PAY", "RETURN", "EXPORT"],
        ["STOCK_INFLOW"] = ["VIEW", "RECEIVE", "EXPORT"],
        ["STOCK_OUTFLOW"] = ["VIEW", "EXPORT"],
        ["INVENTORY"] = ["VIEW", "EXPORT"],
        ["REPLENISHMENT"] = ["VIEW", "POLICY_EDIT", "PREPARE_PO", "EXPORT"],
        ["STOCK_ADJUSTMENT"] = ["VIEW", "ADJUST"],
        ["STOCK_TRANSFER"] = ["VIEW", "CREATE", "APPROVE", "DISPATCH", "RECEIVE", "CANCEL", "EXPORT"],
        ["SALES"] = ["VIEW", "CREATE", "COLLECT", "RETURN", "FULFIL", "PRICE_OVERRIDE", "DISCOUNT", "DISCOUNT_APPROVE", "MANUAL_ITEM", "EXPORT"],
        ["INVOICES"] = ["VIEW", "PRINT", "EXPORT"],
        ["QUOTATIONS"] = ["VIEW", "CREATE", "EDIT", "STATUS", "CONVERT", "PRINT", "EXPORT"],
        ["EXPENSES"] = ["VIEW", "CREATE", "EDIT", "POST", "PAY", "REVERSE", "CANCEL", "ATTACH", "EXPORT"],
        ["EWAY_BILLS"] = ["VIEW", "PREPARE", "EDIT", "VALIDATE", "RECORD_EXTERNAL", "EXPORT", "VIEW_USAGE", "CONFIGURE_PROVIDER"],
        ["WARRANTY_LOOKUP"] = ["VIEW", "CREATE", "EDIT"],
        ["WARRANTY_CLAIMS"] = ["VIEW", "CREATE", "UPDATE", "APPROVE", "RESOLVE"],
        ["EXPIRING_WARRANTIES"] = ["VIEW", "EXPORT"],
        ["SERVICE"] = ["VIEW", "CREATE", "UPDATE", "ASSIGN", "DELIVER", "COLLECT"],
        ["PENDING_SERVICES"] = ["VIEW"],
        ["REPORTS"] = ["VIEW", "EXPORT"],
        ["COMPANY_SETTINGS"] = ["VIEW", "EDIT"],
        ["GST_SETTINGS"] = ["VIEW", "EDIT", "APPROVE_TAX", "OVERRIDE_TAX", "BULK_TAX", "IMPORT_TAX", "EXPORT_TAX"],
        ["CRM"] = ["VIEW", "CREATE", "EDIT", "ASSIGN", "FOLLOW_UP", "CLOSE", "VIEW_TEAM", "VIEW_HISTORY", "VIEW_FINANCIAL"],
        ["GST_REPORTS"] = ["VIEW", "VIEW_REGISTRATION", "CREATE", "REVIEW", "EXPORT"],
        ["STORES"] = ["VIEW", "CREATE", "EDIT", "MANAGE_ACCESS"],
        ["USER_MANAGEMENT"] = ["VIEW", "CREATE", "EDIT", "DEACTIVATE", "RESET_PASSWORD", "RESET_AUTHENTICATOR", "MANAGE_PERMISSIONS"],
        ["ROLE_MANAGEMENT"] = ["VIEW", "MANAGE_PERMISSIONS"],
        ["SECURITY"] = ["VIEW", "MANAGE"],
        ["AUDIT_LOGS"] = ["VIEW", "EXPORT"]
    };
    public static string Canonical(string module, string action)
    {
        return action switch
        {
            "DELETE" => "DEACTIVATE",
            "EDIT" when module == "SECURITY" => "MANAGE",
            "EDIT" when module == "ROLE_MANAGEMENT" => "MANAGE_PERMISSIONS",
            "EDIT" when module is "WARRANTY_CLAIMS" or "SERVICE" => "UPDATE",
            "APPROVE" when module == "SERVICE" => "DELIVER",
            _ => action
        };
    }

    public static bool Sensitive(string action)
    {
        if (action is "PREPARE" or "VALIDATE" or "RECORD_EXTERNAL" or "VIEW_USAGE" or "CONFIGURE_PROVIDER") return true;
        return action is "STATUS" or "CONVERT" or "MANUAL_ITEM" or "VIEW_FINANCIAL" or "VIEW_HISTORY" or "VIEW_TEAM" or "CLOSE" or "VIEW_REGISTRATION" or "REVIEW" or "PRICE_OVERRIDE" or "DISCOUNT" or "DISCOUNT_APPROVE" or "DEACTIVATE" or "APPROVE" or "RETURN" or "DISPATCH" or "RECEIVE" or "CANCEL" or "ADJUST" or "RESET_PASSWORD" or "RESET_AUTHENTICATOR" or "MANAGE_PERMISSIONS" or "MANAGE" or "MANAGE_ACCESS";
    }
}
